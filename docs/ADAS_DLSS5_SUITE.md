# Adas DLSS 5 Suite

Adas adds a reviewable DLSS 5 component to every detected RHI game. It inspects the selected executable folder, graphics API, bitness, native DLSS evidence, ReShade state, and safety markers before selecting one deployment path.

| Game path | Adas selection | Runtime behavior |
| --- | --- | --- |
| 64-bit DirectX 12 with native DLSS | Native DirectX 12 | Uses stable RenoDX DLSS5 4.70 directly on the game's D3D12 DLSS calls. |
| 64-bit DirectX 11 with native or injected DLSS | Native DirectX 11 | Uses RenoDX DLSS5 4.70 with DLSS5 Bridge 1.4.8 for D3D11-to-D3D12 transport. |
| 64-bit Vulkan with native DLSS | Native Vulkan DLSS mirror | Uses RenoDX DLSS5 4.70 with Bridge 1.4.8 and mirrors the game's own DLSS contract, depth, motion vectors, jitter, and quality preset onto D3D12. |
| DirectX 11 without DLSS | Feeder | Builds a DLAA contract from ReShade depth and estimated motion vectors. |
| DirectX 12 without DLSS | Feeder | Evaluates directly on the game's D3D12 device and queue. |
| Vulkan without native DLSS | Feeder | Imports shared D3D12 textures, fences, and semaphores into the game's Vulkan device. The 32-bit Vulkan route requires the matched 0.13.1 beta. |
| OpenGL | Feeder | Installs local ReShade as `opengl32.dll` and uses NVIDIA OpenGL/D3D12 interop. |
| DirectX 9 | Feeder through dgVoodoo2 | Downloads and configures current dgVoodoo2 to translate D3D9 to D3D11. ReShade is installed as `dxgi.dll`; dgVoodoo owns `d3d9.dll`. |
| 32-bit DirectX 10 | Native Feeder relay | Feeder 0.13.1 creates a private D3D11 relay and installs normally through `dxgi.dll`; no DXVK or machine-wide Vulkan layer is needed. |
| 64-bit DirectX 10 | Feeder through DXVK | The upstream x64 add-on has no D3D10 backend, so Adas installs stable DXVK and the 64-bit Vulkan ReShade layer. |
| 32-bit supported path | Hosted Feeder | Installs the x86 add-on in the game and creates a `host64` folder with the helper and complete x64 runtime. |

RHI's current ShortFuse neural-rendering runtime supports GeForce RTX 20-, 30-, 40-, and 50-series GPUs. Adas refuses installation when it finds anti-cheat or multiplayer/online-only evidence, requires explicit single-player/offline confirmation, and refuses to guess between equally likely executable folders.

> The ReShade suite and OptiScaler hook routes must not be installed together. Stable Feeder 0.7 also requires NVIDIA Smooth Motion to be disabled. The separately labelled 0.13.1-beta.1 test route includes the current Smooth Motion synchronization fixes, but remains an upstream test build.

## Install profiles

The default **Maximum Quality** profile uses a route-specific, reviewed component set packaged with Adas:

- RenoDX DLSS5 **4.70** for native 64-bit DX11/DX12/Vulkan games. Native DX11 and native Vulkan also receive DLSS5 Bridge **1.4.8**.
- RenoDX DLSS5 **4.55** for Feeder routes. Feeder's released builds explicitly require this pinned compatibility version.
- DLSS5-Feeder **0.7.0** as one matched release: x64 add-on, x86 add-on, x64 host, companion shader, and optional Vulkan fallback layer. Protocol v2 makes mismatched hosted halves fail safely.
- NVIDIA's matched Streamline/DLSS runtime ZIP, ReShade **6.8.0** x86/x64 runtimes, and the standard `ReShade.fxh`, `ReShadeUI.fxh`, and `DrawText.fxh` headers. ReShade **6.3.3** remains packaged only as an explicit legacy channel; no game title is hardcoded to it.
- The exact known-good DLSSNR standby-repair implementation for `nvngx_dlssnr.dll` 310.8.0.0. Repair does not broaden its accepted source hash when RHI adds newer NR downloads.

The optional **Latest Feeder test build** profile packages the matched **0.13.1-beta.1** x64/x86 add-ons, protocol-v7 x64 host, shader, and Vulkan fallback. It adds a native 32-bit DirectX 10 relay on top of the current D3D11 Smooth Motion binding fix, 32-bit Feature Level 10 texture fix, non-blocking Vulkan/DXVK present path, in-game host controls, minidumps, crash logs, and upstream verifier. Adas never mixes these beta files with stable 0.7. It is selected automatically for native 32-bit D3D10 and other 32-bit routes that require the newer transport; elsewhere it remains an explicit beta choice.

The **OptiScaler DLSS-NR 0.2.0** profile supports 64-bit native-DLSS DX11, DX12 and Vulkan games. Adas selects `dlss_12` automatically for DX11 because that D3D11-on-12 backend is the one that can run Neural Rendering. The upstream Insert overlay exposes hybrid color composition, live exposure, frame hold, working-scale supersampling and downscalers. The separate NR-before-SR fork remains pinned independently and DX12-only.

The **Standalone AIO 2.0.3** profile keeps NR, DLAA/SR and optional frame generation in one presentation pipeline. Adas defaults to DLSS Preset L, automatic window virtualization and telemetry, while leaving frame generation, VORT guidance and serialized presentation off. Hold F8 during launch to request the add-on's serialized safe-start recovery after a bad session.

The optional **Experimental unified add-on** profile uses ShortFuse **0.3** (`renodx-dlss.addon64`) on supported 64-bit DirectX/Feeder routes. It can replace the separate DirectX bridge and provides the newer combined UI, but it is marked experimental because some games show flicker, shadow instability, black screens, or startup crashes. Native Vulkan remains on the reviewed 4.70 + Bridge mirror. When this profile is paired with stable Feeder, Adas changes only Feeder's old null-terminated RenoDX filename probe; no code or protocol data changes.

Local import accepts canonical and versioned RenoDX names plus legacy `renodx-dlss5(2).addon64` and `dlss5-feed-32bit.addon32` names, validates their architecture, and normalizes them. Repair backs up and removes incompatible generations so ReShade cannot load the stable and experimental add-ons together.

## Motion vectors and automatic preset setup

Feeder 0.7 supports five providers through the per-effect `DLSS5_MV_PROVIDER` definition: shared `texMotionVectors`, iMMERSE LaunchPad, VORT, LumeniteFX Kernel, and LumeniteFX QuantMotion.

Adas uses upstream's recommended [LumeniteFX Kernel](https://github.com/umar-afzaal/LumeniteFX). Its license requires official distribution links, so the installer does not embed it. Review / Repair downloads the `mainline` branch directly from the author's GitHub codeload URL, validates the Kernel interface, deploys its shader/include/texture files, writes `DLSS5_MV_PROVIDER=3` into the active preset, and enables **LUMENITE: Kernel 2.0** above **DLSS 5 Feed**. A previously validated official download is used if a later refresh is temporarily unavailable.

DRME remains available in RHI's general shader-pack library, but it is not the automatic Feeder provider because Feeder 0.7 reports its ReShade 6.8 render-target compile error and recommends LumeniteFX instead.

The Feeder configuration includes `enabled`, `mode`, `hdr`, `depth_inverted`, `flags`, `reset_every`, `warmup_rebuild`, `rebuild`, `log_frames`, `create_delay`, `preset`, `host_window`, `work_resolution`, `mv_scale_x`, and `mv_scale_y`. Beta 0.11 additionally exposes `gpu_timeout_ms` (default 2000, valid 100–60000). Work resolution is a processing-cost control rather than DLSS upscaling; 100% remains the quality default. The overlay reports provider mismatch/compile failures and probes whether non-zero motion vectors are reaching DLSS.

## 32-bit host layout

For a 32-bit game, Adas installs:

- `dlss5-feed.addon32` beside the game executable, using the game's x86 ReShade runtime.
- `DLSS5_Feed.fx` and LumeniteFX under `reshade-shaders`, with provider 3 enabled in the preset.
- `host64/dlss5-feed-host64.exe`, `host64/dxgi.dll` (x64 ReShade), `host64/renodx-dlss5.addon64`, `host64/nvngx_dlssnr.dll`, and `host64/nvngx_dlss.dll`.

Adas installs the packaged Streamline/DLSS runtime set automatically. A manually supplied `Downloads\DLSS5\streamline.zip` or `Downloads\streamline.zip` remains a fallback for advanced replacement. Installation stops instead of reporting success if either required runtime remains unavailable. The x86 Feeder exposes the helper's day-to-day neural-rendering controls directly in the game's ReShade Add-ons tab.

## Vulkan fallback

Feeder normally hooks `vkCreateDevice` itself and needs no extra layer. Adas also installs the official fallback under `DLSS5-Vulkan-Fallback`. Use its launcher only when `dlss5-feed.log` explicitly says the normal hook was not reached or the interop entry points are missing. It sets layer variables for that launch only and does not register a system-wide layer.

Only 64-bit DirectX 10 uses the DXVK fallback and requires a registered Vulkan ReShade layer. The native 32-bit DirectX 10 relay is game-local and does not touch that shared registration.

## Repair and rollback

Review / Repair selects one coherent generation. The default native DX11 and native Vulkan routes install current `dlss5-bridge.addon64` 1.4.8 and remove the obsolete `dlss5-dx11-bridge.addon64`. The Vulkan route writes safe mirror-only defaults (`vk_mirror=1`, `source=mirror`, synthetic fallback off). Native DX12 does not need the bridge. The experimental unified profile removes both bridge generations on its supported routes because its DirectX transport is built in.

DLSSNR Standby Repair retains its preview, exact source validation, Authenticode and NVIDIA signer checks, same-directory staging, backup, atomic replacement, rollback, and locked-file reporting.

Suite-owned writes are recorded in `.adas/dlss5-install.json` before replacement. Existing files are copied to `.adas/backups`. Uninstall removes only files whose hashes still match the installed copies, preserves modified files, restores tracked originals when safe, and retains the recovery record after any incomplete operation. Repair also removes only Adas DLSS entries from ReShade's crash-generated `DisabledAddons` list and prunes stale early-load references after switching routes.

## In-game verification

Adas automatically enables **LUMENITE: Kernel 2.0** and **DLSS 5 Feed** and binds provider 3. It verifies required files and their tracked hashes immediately after installation. **Check current setup** then reads ReShade, Feeder, Bridge, and host logs and explains common architecture, shader-header, motion-vector, host, runtime, and NGX failures in plain language. In a 32-bit game, routine host controls are in **Add-ons → DLSS 5 Feed**. Keep MSAA/SSAA and the separate OptiScaler route off. Keep Smooth Motion off with stable Feeder. Native DX11/DX12/Vulkan setups use the RenoDX DLSS panel directly.
