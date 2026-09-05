# Ada 2.6.20 — focused setup improvements

## Scope and result

Implemented the four accepted DLSS5-Swapper ideas: prerequisite checks, settings-preserving local profile switching, 32-bit DX8 translation, and broader emulator-aware setup. Existing dirty work and the 2.6.19 component versions were retained. No installed game folders were changed during development.

- Runtime checks verify the presence and PE architecture of the C++ runtime files; hosted games need x86 and x64. Wrong-architecture app-local files cannot be masked by valid system copies. This is not a guarantee that every third-party binary's minimum runtime version is met. Missing prerequisites link to Microsoft's current v14 installers; Ada does not silently elevate or install system software.
- Profile switching has an explicit per-game write-ahead journal, immutable recovery copies and SHA-256 verification before rollback. It saves visual tuning separately by profile/API, never restores old loader/search paths or provider definitions, and blocks shared Vulkan-layer changes until the old route is removed. Interrupted recovery is retried by Repair, not by a launch-time background scan.
- DX8 uses the x86 D3D8 dgVoodoo wrapper, DX11 ReShade and matched Feeder host. Wrapper architecture and file presence are verified. The official dgVoodoo package is downloaded/cached through the existing route; it is not newly bundled.
- Nineteen emulator families have explicit renderer choices. Multiple executable builds are selectable and the selected executable is persisted; its actual PE architecture determines installation. Ada does not modify the emulator's own settings or promise compatibility for every emulated title/core.

## Validation

- Test-first DX8 classifier failure observed before implementation.
- Profile tests caught and resolved an ownership-path mismatch for preset restoration and a recovery-result flag that was changed by cleanup.
- Full Release suite: **176 passed, 0 failed**. Receipt: `RenoDXCommander.Tests/TestResults/adas-2.6.20.trx`.
- Covers consumed-backup rollback, interrupted recovery, corrupt/missing snapshots, committed switches, legacy shader directory moves, independent profile tuning, loader/provider preservation, shared-layer gating, runtime architectures, emulator aliases and executable persistence.
- `git diff --check` passed; Git reported existing CRLF conversion notices. No standalone lint task is configured; the Release compilation provides type checking. Existing compiler warnings and unavailable NuGet vulnerability metadata are not claimed fixed.
- Simplification skill: inline reuse/quality/efficiency passes limited to this change; one efficiency improvement applied (merge whole-file settings once through the existing tracked installer). Safety validation retained.
- Code review: skipped (ce-code-review unavailable) — the required independent workflow cannot run under this session's no-delegation mapping. No independent review receipt is claimed. A manual scan covered path boundaries, write hooks, backup consumption, enum routing, architecture selection, profile exclusivity and cancellation/error recovery.

## Sources

Installer build completed with Inno Setup 6.7.3. Output: `artifacts/installer/Adas-Setup.exe`, version **2.6.20**, **785,611,502 bytes**. SHA-256: `23A71DDF565E113917B10E7E0113530AAF252F454F2871F9D2F7D63CA843D1C1`. Published `RHI.exe` reports **2.6.20.0**. Source/published third-party notices match. All pinned source and published component hashes passed the packaging checks. Packaging required permission to run the installed Inno compiler; no game was launched or modified.

- [DLSS5-Swapper](https://github.com/rakanki911/DLSS5-Swapper), including its backend-manager profile journal and emulator catalog.
- [DLSS5-Autopilot emulator catalog](https://github.com/Kizzuwatnaa/DLSS5-Autopilot/blob/main/core/emulators.py), credited by Swapper.
- [Microsoft's supported runtime downloads](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist?view=msvc-170).
- [Official dgVoodoo2 2.87.4](https://github.com/dege-diosg/dgVoodoo2/releases/tag/v2.87.4).

## Post-Deploy Monitoring & Validation

On the first user-initiated installation, close the game and confirm the selected executable/renderer. A healthy file installation produces the verification-complete message and a valid `.adas/dlss5-install.json`; inspect the in-game overlay separately to confirm actual frames and picture quality. When switching back, compare saved visual sliders. On a failed/interrupted switch, close the game and run Repair; keep `.adas/switch-recovery` until Ada reports recovery. Do not manually delete recovery files or layer registrations. Owner: the user, on the next chosen game installation. No live GPU/game, emulator renderer, performance, or black-screen validation was performed in this development run.
