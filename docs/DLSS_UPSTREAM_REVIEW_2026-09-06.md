# DLSS upstream review — 2026-09-06

This review compares the DLSS-related GitHub projects referenced by the Adas
source tree and the supplied DLSS 5 material with the payloads and routes
currently present in Adas. It is an audit, not a claim of live-game rendering
compatibility.

## Current Adas payloads

| Component | Adas currently carries | Upstream status | Result |
| --- | --- | --- | --- |
| DLSS5-Feeder | Stable 0.7.0; optional beta 0.14.0-beta.4 | 0.14.0-beta.4 is latest | **Implemented**; beta.4 is a matched replacement set and is not mixed with beta.2. |
| DLSS5 Bridge | 1.4.12 | 1.4.12 is latest stable; 1.4.13-pre1 is a targeted BG3 split-screen test | **Implemented**: 1.4.12 fixes Vulkan half-float exposure and format handling. The split-screen prerelease remains excluded. |
| Standalone AIO | 2.0.7-experimental.1 | 2.0.7-experimental.1 is latest upstream prerelease | **Current**. Its adaptive GPU pressure governor remains experimental and is not forced by Adas. |
| Neural Upstream | 0.3.0 | 0.3.0 is latest release | **Current**. Adas already installs the correct `nvngx.dll.addon64` filename and keeps it exclusive from other NGX consumers. |
| OptiScaler DLSS-NR | 0.2.0 | 0.2.0 is latest release | **Current**. The release's exposure, frame-hold, proxy, supersampling and Vulkan changes are already represented by the pinned package. |
| DLSS5oneclick | 0.11.15 | 0.11.15 is latest release | **Current where used**. Adas already pins the same helper version rather than embedding its installer workflow. |
| DLSSNR signature repair | 1.1.1 source workflow | 1.1.1 is the current published repair workflow | **Covered** by Adas's native preview, exact hash/signature validation, backup, atomic replace and rollback service. |

## Important upstream changes not yet in Adas

### Feeder beta 0.14.0-beta.4

The current Adas beta payload is now `0.14.0-beta.4`. Upstream beta.4 fixes the
host-window resize path, re-fits ReShade's docked panel before `ResizeBuffers`,
and publishes a one-command installer script. The release explicitly requires
updating both halves of a 32-bit installation together. Because beta.4 is a
single ZIP with a changed matched payload, this is a payload refresh rather than
a version-string edit.

Source: <https://github.com/jlrouzies-fr/DLSS5-Feeder/releases/tag/v0.14.0-beta.4>

### Bridge 1.4.12

The current Adas bridge asset is now 1.4.12. Upstream 1.4.12 adds Vulkan support for
`R16_FLOAT` exposure textures and stops coupling the output allocation format to
the input format. The release provides a pinned SHA-256 for the replacement
asset. The newer 1.4.13-pre1 is a narrow BG3 split-screen test and should remain
opt-in until it is validated broadly.

Sources:

- <https://github.com/NIGos/dlss5-bridge/releases/tag/v1.4.12>
- <https://github.com/NIGos/dlss5-bridge/releases/tag/v1.4.13-pre1>

## Features in other managers, not component updates

- **DLSS5 Autopilot 1.6.1** adds architecture-correct Vulkan-layer diagnosis
  for 32-bit DXVK games, better missing-layer reporting, resumable downloads,
  scan budgets, and improved DX12/D3D9 classification. These are useful ideas
  for Adas's detection/diagnostic layer. Adas now treats a disabled implicit
  layer registration as missing and checks the correct 32-bit registry view;
  Autopilot's scan-budget and resumable-download workflow remains outside
  Adas because it is a separate installer.
- **DLSS5 Autopilot 1.6.0** adds an RTX Remix route that installs into a Remix
  `.trex` runtime rather than using ReShade/Feeder. Adas does not currently
  support that distinct deployment shape.
- **DLSS5 Swapper 2.2.1** adds an in-game F8 control overlay and theme system.
  Adas has its own desktop controls and emulator/library flows, but does not
  have that in-game overlay.
- **UnityDLSSNR** is a Unity 6.3/URP native package that requires an engine
  integration and a separately supplied NVIDIA model DLL. It is not a
  drop-in game-folder add-on that Adas can safely offer for arbitrary titles.
- **DLSS5oneclick 0.11.15** adds hybrid-GPU process exports and RE Engine
  OptiScaler hotfix defaults; its current version is already represented where
  Adas uses that helper. These are not a missing Adas payload.

## Deliberately not bundled

Aggregate repacks such as `ShugokiFable/dlss5-aio` and Easy-AIO installers
combine NVIDIA/runtime files and other projects into a new distribution. Adas
keeps those components separate, verifies hashes, and respects upstream license
and redistribution restrictions. They are not authoritative upstream payloads.

The DFC archive supplied separately is also imported and cached from the user's
official ZIP; it is not bundled into the Adas installer because its license
prohibits rehosting or bundling.
