# DLSS 5 setup: operation lifecycle, snapshots, and concurrency

This documents the reliability model introduced by the "reliability, architecture, and UI
improvement" plan (Phases 1–7). It explains how a per-game setup operation is identified, how its
inputs are frozen, how concurrent/superseded work is handled, and how a release is verified.

## 1. Stable operation identity (Phase 1)

A single install/repair/remove flow can outlive the mutable state of the card that started it: the
background scan may re-write the card's `InstallPath`, detected API, or bitness while an install is
mid-flight. To keep a flow self-consistent, every flow **captures an immutable snapshot at entry**:

- `Dlss5GameOperation` (`Adas.Core/Services/Dlss5GameOperation.cs`) is a `record` holding
  `GameName`, a fully-resolved `InstallPath` (`Path.GetFullPath`), `Source`, `Is32Bit`, and an
  optional `ApiOverride`.
- `Dlss5GameOperation.Capture(card[, apiOverride])` takes the snapshot **once**, at the top of
  `InstallAsync` / `RemoveAsync` / `RepairAsync` in `Adas.App/Shell/Dlss5Installer.cs`.
- Everything downstream — probe, assessment, install, cleanup, retry, "ensure game closed",
  installed-root resolution — reads from `op.*`, never from the live `GameCardViewModel` again.

**Rule:** once an operation begins, do not re-read mutable card identity or paths. If the card
changes, the *next* operation captures a new snapshot; the in-flight one finishes on its own inputs.

## 2. Latest assessment wins (Phase 2)

Background re-assessments race with the user selecting a different game. Applying a stale result
would show one game's routes under another game's name. This is resolved with a monotonic
generation gate rather than sleeps or best-effort cancellation:

- `GenerationGate` (`Adas.Core/Services/GenerationGate.cs`) hands out a `GenerationToken` from
  `Begin()`; each `Begin()` `Interlocked.Increment`s the current generation, so only the newest
  token reports `IsCurrent == true`.
- `GameSetupViewModel.BeginAssessment()` starts a generation; the background `Assess(op)` computes a
  pure `GameSetupAssessment`; the UI applies it **only if** `generation.IsCurrent` **and** the pane
  still points at the same card (`ReferenceEquals(Card, card)`).
- A superseded assessment is discarded silently — its `CancellationToken` is also cancelled so it
  stops early where possible.

**Rule:** assessment is a pure function of a snapshot; results are committed to the UI only when both
the generation and the selected game still match. In-flight *awaited* refreshes inside an install
flow pass the gate naturally (they hold the current generation), so this does not deadlock the flow.

## 3. Shared operation coordination (Phase 3)

Conflicting file operations (install vs. repair vs. remove vs. RR update/restore vs. a tuning write)
must not run at once, and outcomes must be routed by type, not by parsing exception message text.

- **Execution guards:** the setup pane serialises user-triggered mutations behind a `_busy` flag —
  RR update/restore bail if `_busy`, tuning refuses with "Finish the current operation before
  changing tuning.", and the RR buttons disable while busy.
- **Engine locks:** the underlying per-game file operations continue to use the engine's existing
  locking; the UI guard is an additional, earlier gate so two mutations never enter the engine
  concurrently from this pane.
- **Typed outcomes:** message-prefix routing was replaced with typed exceptions in
  `Adas.Core/Services/Dlss5OperationExceptions.cs`:
  - `Dlss5RecoveredInterruptedSwitchException` — a previous switch was interrupted and has been
    rolled back/recovered; the flow retries once (`when (attempt == 0)`).
  - `Dlss5ConflictingPipelineException` — another DLSS pipeline is present and must be removed
    first; the flow surfaces this as a first-attempt retry opportunity.
  Both derive from `InvalidOperationException`, so `Assert.Throws<T>` in tests must name the exact
  subclass (a base-type exact match fails on a subclass).

**Deferred:** an explicit public status enum (preparing/running/succeeded/failed/cancelled/partial)
and game-named progress strings were folded into the Phase 6 UI rather than added as an unused
engine API, to avoid dead code. The generation gate + `_busy` guard already provide the concurrency
guarantees; the enum is a presentation concern.

## 4. Architecture cleanup (Phase 5)

The pure assessment computation was lifted out of the Avalonia code-behind (which reached the engine
through the `AppServices.Services` locator) into a focused, constructor-injected
`GameSetupViewModel` (`Adas.Core/ViewModels/GameSetupViewModel.cs`), registered **transient** (one
generation gate per pane) in `AdasEngineServices.cs`. The view keeps only UI application of a
`GameSetupAssessment`. This is a deliberate single-responsibility extraction — **not** a
`MainViewModel` rewrite. Abstracting the file/folder pickers behind interfaces remains a follow-up;
the assessment extraction was the highest-value testability win and is covered by unit tests.

## 5. Accessibility (Phase 6)

`Adas.App/Views/GameSetupView.axaml` now carries `AutomationProperties.Name` /
`AutomationProperties.LabeledBy` on every primary setup control (API/bitness combos, install DLSS 5
checkbox, route list, FSR-FG option, and the Install/Preview/Repair/Remove/Open/RR/Diagnose/Share
buttons), a logical `TabIndex` order through the primary flow (1–11), and `LiveSetting` regions on
the readiness banner, install result, and diagnosis result so screen readers announce state changes.
The action area is a persistent, named `WrapPanel` (`ActionArea`); Install/Preview/Open are always
present, Repair/Remove appear only when DLSS 5 is installed.

Installed-vs-working-vs-needs-attention is driven from the assessment: a saved record →
`GameStatus.Installed`; a `Ready` readiness state on an installed game → the "installed … launch the
game" summary; otherwise the readiness headline names what needs attention.

> **Verification limitation (honest):** DPI-scaling checks at 125/150/200% and before/after UI
> screenshots require a rendered Avalonia desktop window and could not be performed in the headless
> build/test environment used here. They must be run on a Windows desktop before release. The XAML
> uses relative layout (`*`/`Auto` grids, `WrapPanel`, `TextWrapping`) with no fixed pixel widths on
> the setup flow, which is the correct basis for DPI independence, but this has not been visually
> confirmed.

## 6. Secure automatic updates (Phase 4)

Update-package handling is covered by `UpdateServiceSecurityTests`. Dependency-vulnerability warning
suppression (`NoWarn NU1902/NU1903`) was **removed** and replaced with real fixes: `SharpCompress`
bumped to `0.48.0` (clears the archive directory-traversal advisory; the API rename
`ArchiveFactory.Open` → `ArchiveFactory.OpenArchive` was applied at all call sites), and
`Tmds.DBus.Protocol` pinned to `0.21.3` (the backported fix for the transitive high-severity
advisory pulled by Avalonia 11.2.3). `dotnet list package --vulnerable --include-transitive` reports
clean with no NU19xx warnings.

## 7. Release verification checklist (Phase 7)

Automated, run in this environment:

1. `dotnet build Adas.App/Adas.App.csproj -c Release` → 0 errors.
2. `dotnet test RenoDXCommander.Tests/RenoDXCommander.Tests.csproj -c Release` → all pass (504/504).
3. `pwsh -NoProfile -File tools/build-adas.ps1` → trimmed self-contained publish, 0 errors.
4. Smoke: launch the published `Adas.exe` under an **isolated** `APPDATA`/`LOCALAPPDATA` for ~25 s;
   a timeout (exit 124) with no crash confirms the DI container builds and the main window runs.
5. `.github/workflows/ci.yml` runs steps 1–2 on `windows-latest` for every push/PR to `main`.

Requires a Windows desktop (not done here):

6. Visual DPI check at 125/150/200% and before/after screenshots of the setup pane.
7. End-to-end install/verify on a real RTX title. **Automated verification must never install into a
   real game, change real driver settings, or launch downloaded installers** — it uses fixtures and
   test doubles only.
