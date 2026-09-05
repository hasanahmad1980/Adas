# MFG Ada Unlock in Adas

## What it is

**MFG Ada Unlock** adds an optional per-game component that unlocks NVIDIA DLSS **Multi Frame Generation (3x/4x/6x)** on **GeForce RTX 40-series (Ada)** GPUs. NVIDIA restricts driver-level MFG to RTX 50-series (Blackwell); Adas's existing "Multi Frame Gen" driver-profile control therefore only applies to RTX 50 cards. This component fills the gap for RTX 40 owners.

It integrates the [MFGAdaUnlock-RenoDx](https://github.com/mavismmg/MFGAdaUnlock-RenoDx) ReShade add-on (`renodx-mfgunlock.addon64`, MIT, a fork of Dreamt's original). The add-on is **in-memory only**: it patches the running game at runtime, modifies no files on disk, redistributes no NVIDIA runtime files, and reverts on unload.

## Requirements & gating

- **GeForce RTX 40-series** GPU. The row is **hidden on all other GPUs** — RTX 30-series lacks the required machine code and RTX 50-series already ships MFG natively. Detection extends `Dlss5CompatibilityService` with an Ada classifier (`IsAdaGpu`).
- **ReShade with add-on support** — install it from the ReShade row first. The Install button is greyed until ReShade is present.
- A game using **DLSS Frame Generation** (Streamline), plus a modern **`nvngx_dlssg.dll` (310.x+)** — manage this through the normal DLSS/Streamline flow.
- Feature-flag gated (`mfgUnlock` in `manifest.json` `featureFlags`, or a local `unlock.txt` dev preview).

> **Single-player only.** Patching game memory may trigger anti-cheat in online titles. Uninstall before playing multiplayer.

## Using it

1. Select an RTX 40-series machine's game. If eligible, an **MFG Unlock** row appears in the detail panel.
2. Click **Install** — Adas downloads the latest `renodx-mfgunlock.addon64` from the official GitHub release, deploys it to the game's ReShade add-on folder, and writes default `[RenoDX.MFGUnlock]` settings to `reshade.ini` (only if absent — never clobbering user edits).
3. Use the **⚙ cog** to tune settings, or edit them live from ReShade → Add-ons in-game.
4. Launch the game; configure the multiplier in graphics settings or the add-on panel.
5. **✕** removes only the deployed add-on file.

## Configuration (`[RenoDX.MFGUnlock]` in reshade.ini)

| Key | Default | Meaning |
|---|---|---|
| `Enabled` | 1 | Master switch |
| `MaxCount` | 4 | Maximum frame multiplier (2–6) |
| `ForceFlipMeteringOff` | 1 | Required for 3x+ on Ada |
| `TemporalFix` | 1 | Temporal-midpoint / interpolation correction |
| `ForceMultiplier` | 0 | 0 = respect the game; 2–6 = force |
| `RaiseFrameCeiling` | 0 | Raise older plugin limits up to 6x |
| `ForceOTAPlugins` | 0 | Load the driver OTA plugin set |

## Distribution & licensing

The add-on is **not bundled**. Adas downloads the author's prebuilt release asset at runtime and deploys only that file, consistent with the project's policy for MIT/forked third-party mods. No NVIDIA DLSS/Streamline binaries are included. See [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md).

## Implementation notes

- Service: `RenoDXCommander/Services/MfgUnlockService.cs` (+ `IMfgUnlockService`), modeled on `DofFixService` (single `.addon64` from GitHub releases; staging under `%LocalAppData%\RHI\mfg-unlock`).
- GPU gating: `Dlss5CompatibilityService.IsAdaGpu` / `IsAdaGpuDetected`.
- Feature flag: `FeatureFlags.MfgUnlock`, `ManifestFeatureFlags.MfgUnlock`, `manifest.json`.
- UI: `DetailMfgUnlockRow` in `MainWindow.xaml`, wired via `DetailPanelBuilder.Components.cs`, `ViewModels/GameCardViewModel.MfgUnlock.cs`, handlers in `MainWindow.Events.Install.cs`, cog dialog `MfgUnlockDialog.cs`.
- Config I/O reuses `IniTextDocument` for formatting-preserving `reshade.ini` edits.
