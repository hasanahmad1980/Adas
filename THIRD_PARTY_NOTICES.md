# Third-party notices

Adas is based on **RHI** by RankFTW and is distributed under the repository's GNU General Public License v3.0 (`LICENSE`). Third-party integrations retain their respective licenses, as noted below.

## Experimental OptiScaler Neural Rendering packages

The standard OptiScaler NR 0.2.0, NR-before-SR English 0.1.2, and pre-SR multipass 0.7.6 release archives are included in this private local package. Their sources are GPL-3.0; dependency notices and licenses (including the RenoDX attribution, FidelityFX, XeSS and DirectX notices carried in the pre-SR multipass archive) remain in the original archives and are copied alongside installed dependencies.

- OptiScaler NR source at the packaged tag: <https://github.com/Dagherbou/OptiScaler_DLSSNR/tree/v0.2.0-dlssnr>
- NR-before-SR source at the packaged tag: <https://github.com/Markxiao94/OptiScaler-DLSSNR-NR-before-SR/tree/v0.1.2-nr-before-sr-english>
- Pre-SR multipass source at the packaged tag: <https://github.com/wilsjo2/OptiScaler-DLSSNR-PreSR-Multipass/tree/v0.7.6>
- Original OptiScaler project: <https://github.com/optiscaler/OptiScaler>

NVIDIA, Intel and AMD dependency binaries retain their own terms. Inclusion in this private package does not establish permission for public redistribution; audit every dependency and corresponding-source obligation before publishing an installer. The local Adas installer includes hash-pinned AIO release files for offline use; the upstream repository did not expose redistribution terms when reviewed.

## DLSS5-Feeder

Source: <https://github.com/jlrouzies-fr/DLSS5-Feeder>

Copyright (c) 2026 Jean-Laurent ROUZIES

Portions derived from DLSS5 Bridge, Copyright (c) 2026 NIGos (MIT).

Adas bundles the author's matched stable `v0.7.0` release set and separately bundles the matched `0.15.1` release set. The 0.15.1 x86 add-on and x64 host use IPC protocol v9 (both halves must match) and are never mixed with stable protocol-v2 files. Only when the user selects the experimental unified RenoDX profile with stable Feeder does Adas change the null-terminated filename probe in the x64 add-on/host from `renodx-dlss5.addon64` to `renodx-dlss.addon64`; code and protocol data are unchanged.

## DLSS5 Bridge

Source: <https://github.com/NIGos/dlss5-bridge>

Copyright (c) 2026 NIGos. Licensed under the MIT License. Adas bundles release 1.4.12 for the recommended native DirectX 11 and native Vulkan mirror routes.

## Neural Upstream

Source: <https://github.com/matiasLombo/neural-upstream>

Copyright (c) 2026 matiasLombo. Licensed under the MIT License. Adas bundles the
author's `v0.3.0` `nvngx.dll.addon64` release asset, pinned by SHA-256, for the
separate 64-bit native DirectX 12 Neural Upstream profile. It is not installed
beside RenoDX, Deep Fried Chicken, OptiScaler or another NGX hook.

## MFG Ada Unlock

Source: <https://github.com/mavismmg/MFGAdaUnlock-RenoDx>

A fork of the original add-on by Dreamt, itself built against <https://github.com/clshortfuse/renodx>. Licensed under the MIT License. The add-on unlocks DLSS Multi Frame Generation (3x/4x/6x) on GeForce RTX 40-series (Ada) GPUs by patching the running game in memory only; it modifies no files on disk and redistributes no NVIDIA runtime files.

Adas does not bundle this add-on. The MFG Unlock component downloads the author's prebuilt `renodx-mfgunlock.addon64` release asset directly from the official GitHub release at install time and deploys only that file. No NVIDIA DLSS/Streamline binaries are included with this integration; Adas manages the game's existing `nvngx_dlssg.dll` through its normal DLSS/Streamline flow.

## ReShade standard shader headers

Source: <https://github.com/crosire/reshade-shaders>

Adas bundles the upstream `ReShade.fxh`, `ReShadeUI.fxh`, and `DrawText.fxh` framework headers so shader compilation does not depend on an install-time shader-pack download. Upstream notices embedded in those files are preserved.

## dgVoodoo2

Source and official download: <https://github.com/dege-diosg/dgVoodoo2>

Adas does not redistribute dgVoodoo2. For a DirectX 9 Feeder route it downloads the current release from the author's official GitHub release, installs only the matching D3D9 wrapper and control panel, and records those files for exact uninstall and restoration.

## DLSS5-NeuralScreen

Source: <https://github.com/perseval-BLR/DLSS5-NeuralScreen>

Copyright (c) 2026 perseval-BLR. The application's own code is licensed under the MIT License (see the MIT text below). NeuralScreen is a standalone whole-desktop Neural Rendering overlay — not a per-game route — and processes the whole screen or one selected window on RTX 30/40/50 GPUs (RTX 40/30 via an architecture spoof to `0x1B0`).

Adas does not bundle or redistribute NeuralScreen. The optional "Launch NeuralScreen" tool downloads the author's pinned `neuralscreen-v1.5.1-full.zip` release archive from the official GitHub release at run time (verified by SHA-256), extracts it into the per-user tool cache, and starts `NeuralScreen.exe`. The 226 MB archive carries its own bundled Python runtime and NVIDIA's leaked pre-release `nvngx_dlssnr.dll` (310.8.0); that NVIDIA runtime is NVIDIA's property, is not covered by the MIT licence, and is never redistributed by Adas — it reaches the user only through the author's official release. Do not run NeuralScreen in competitive online games: a process named `nvngx.dll` plus a fullscreen overlay is exactly what anti-cheat systems look for.

## Deep Fried Chicken

Copyright (c) 2026 Alexander. "Deep Fried Chicken Binary Use Licence" — a limited, personal, non-commercial licence to run unmodified official binary releases; it **forbids copying, rehosting, mirroring, redistributing, or bundling** the software.

Adas therefore never bundles Deep Fried Chicken. The neural-consumer option imports the author's official archive that the user supplies, caches the **unmodified** binaries locally, and deploys them (in place of the RenoDX consumer) into the folder Adas already resolved for the game — beside the game executable, or inside the Feeder host folder for 32-bit games. No NVIDIA runtime files are included; the user supplies a trusted `nvngx_dlssnr.dll`. RenoDX-derived colour/tone portions within Deep Fried Chicken are MIT (see the release's `LICENSE-RenoDX.md`).

Release 1.7.4 adds a native D3D11 ingress add-on (`dfc-native-d3d11.addon64` and its `.cfg`). When the user imports a release that carries it, Adas caches the unmodified pair and, on an x64 D3D11 game that already has native DLSS, deploys it beside the core Deep Fried Chicken add-on for normal ReShade add-on discovery ("Native DFC plus D3D11 ingress"). Native D3D12 games use the core add-on directly and never receive the native pair.

## DLSSNR Standby Repair

Source: <https://github.com/kayle2203/dlssnr-signature-repair>

Copyright (c) 2026 Khalil

## NVIDIA DLSS and Streamline runtime files

The private Adas package includes the user-supplied DLSS/Streamline runtime archive, including the accompanying `nvngx_dlss.license.txt`. Its DLSS-NR entry is the user's community compatibility-patched 310.8.0.0 build; Adas identifies it as a custom runtime instead of presenting it as an unmodified NVIDIA-signed file.

## ReshadeMotionEstimation

Source: <https://github.com/JakobPCoder/ReshadeMotionEstimation>

Copyright Jakob Wapenhensch. Licensed under Creative Commons Attribution-NonCommercial 4.0 International (CC BY-NC 4.0). This remains available through RHI's general shader-pack library but is no longer the DLSS5-Feeder default because upstream Feeder 0.7 reports that DRME does not compile on ReShade 6.8.

License: <https://creativecommons.org/licenses/by-nc/4.0/>

## LumeniteFX

Source and official download: <https://github.com/umar-afzaal/LumeniteFX>

Copyright (C) 2025-2026 Afzaal (Kaidō). LumeniteFX uses the AGNYA License. Its terms require redistribution through the author's official links. Adas therefore does not embed LumeniteFX; during DLSS 5 installation it downloads the `mainline` branch directly from the author's official GitHub codeload URL and installs the files locally for the user.

License: <https://github.com/umar-afzaal/LumeniteFX/blob/mainline/LICENSE.md>

## DLSS5-Swapper / DLSS5-Autopilot

The profile-switching workflow and emulator recognition catalog were informed by
<https://github.com/rakanki911/DLSS5-Swapper> (MIT, Copyright (c) 2026 Rakan Alkhaldi)
and the emulator catalog it credits at <https://github.com/Kizzuwatnaa/DLSS5-Autopilot>
(MIT, Copyright (c) 2026 DLSS 5 Autopilot contributors).
Ada's C# installation and recovery implementation is local to this project; it does not run their installer scripts.
The MIT permission notice below applies to the Swapper-derived catalog information.

## MIT License

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
# Standalone AIO integration

Adas packages the three unmodified author-published assets from
[DLSS5-Reshade-AIO v2.2.1](https://github.com/kibblerz/DLSS5-Reshade-AIO/releases/tag/v2.2.1)
for local offline installation. SHA-256 values are pinned.
The repository did not expose a redistribution licence when checked on 2026-09-10;
do not publicly redistribute this private installer without resolving that permission.
The optional VORT provider comes from [vort_Shaders](https://github.com/vortigern11/vort_Shaders),
under its MIT licence; downloaded shader copyright/permission headers remain intact.
The existing separately obtained NVIDIA runtime package retains its own applicable terms.
