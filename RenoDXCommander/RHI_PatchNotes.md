## v2.6.34 — Full game cleanup and reliable folder confirmation

- Fixed **Install anyway** for games with multiple executable folders. A folder explicitly chosen by the user now clears only the folder-resolution blocker while retaining real safety blocks such as anti-cheat detection.
- Added **Full cleanup** under Advanced. It restores Adas-tracked originals, removes managed ReShade add-ons, scans the full game tree for recognizable DLSS 5/ReShade leftovers, and moves uncertain/user settings to an external recovery folder.
- Full cleanup never removes unrecognized game DLLs or a native `nvngx_dlss.dll`. If a tracked original backup is missing or locked, Adas keeps its recovery record and directs the user to the store's verify/repair action.
- Added **Remove game from list**. Manually added games are removed from the saved library; detected games are persistently hidden without touching their files.

## v2.6.33 — Current DLSS 5 components and simple native controls

- Updated standalone AIO to **2.0.4-experimental.1**, including its buffered presentation path and improved windowed DLAA detection.
- Updated DLSS5 Bridge to **1.4.11**, including recovery from temporary GPU stalls instead of disabling neural rendering for the rest of the session.
- Updated the complete matched Feeder beta set to **0.14.0-beta.2**, including the matched 32-bit host protocol, deadlock recovery, D3D11 crash breadcrumbs, and stricter ReShade add-on validation.
- Added managed **DLSS 5 OpenGL Bridge 1.0.5**, verified **OneClick 0.11.13** launch, and a separate **mainline OptiScaler beta/nightly** install choice.
- Ported the essential RHI 2.6.1 reliability changes without restoring its UI: zero-byte DLSS runtimes are rejected and current D3D12 game/config overrides are included.
- Added simple persistent neural-rendering and appearance controls for the native stable and ShortFuse experimental routes. The full ReShade interface is no longer required for normal on/off configuration.
- Preserved automatic 32-bit Vulkan layer validation and persistent OptiScaler NR enablement. Adas continues to use its own tracked installer and rollback instead of importing RHI's interface or an untracked ASI-loader chain.
- Kept Visual Enhancer as a separate media-processing application; its very large GPU-specific runtime packs do not improve game injection or guaranteed game FPS and are not added to the installer.

## v2.6.31 — Simple Adas workflow

- Replaced the visible multi-component game screen with a focused Adas workflow: choose a game, install or repair the best setup, launch, or remove and restore.
- Moved the renderer override and alternate experimental methods under Advanced.
- Corrected automatic routing so ShortFuse's unified 64-bit add-on is never installed with DLSS5-Feeder.
- Kept the current 32-bit D3D10 Feeder relay and matched x86/host64 deployment while isolating AIO and OptiScaler as explicit experimental alternatives.
- Kept every reviewed experimental payload in the offline package. The interface stays simple by moving alternate routes under one Advanced entry, not by removing features.

## v2.6.30 — Per-game DX9 crash recovery

- Adas now recognizes the exact Windows `STATUS_STACK_OVERFLOW` failure caused by a managed 32-bit DirectX 9 dgVoodoo/ReShade chain, including a recent crash that happened before this update.
- The next DLSS 5 Review / Repair automatically offers a game-local DXVK/Vulkan recovery route, removes the tracked DirectX ReShade proxy, and deploys matched Feeder 0.13.1 beta 32-bit/host64 components.
- The fallback is persisted only in that game's `.adas` record. Working DX9 games retain dgVoodoo, and DirectX 10/11/12, OpenGL, native Vulkan, and unrelated games are not rerouted.

## v2.6.29 — September 4 DLSS 5 ecosystem refresh

- Updated the matched optional Feeder set to **0.13.1-beta.1**. Its new native 32-bit DirectX 10 relay is selected automatically and no longer installs DXVK or a machine-wide Vulkan layer; rare 64-bit D3D10 games retain the translated fallback.
- Updated DLSS5 Bridge to **1.4.8**, including corrected HDR10 color handling and Vulkan delivery when the game swapchain cannot accept transfer copies.
- Updated the standard OptiScaler DLSS-NR profile to **0.2.0** with hybrid color composition, live exposure, frame hold, model supersampling and native Vulkan crash fixes. Native-DLSS DX11 games are now supported and automatically select the required D3D11-on-12 DLSS backend.
- Updated standalone AIO to **2.0.3** with its compatibility-aware compositor, automatic window virtualization, serialized safe-start recovery and lower-smearing DLSS Preset L default. Expensive VORT guidance remains off by default.
- Game cards now show **Update Ready** when an Adas-managed DLSS profile uses a superseded Feeder, Bridge, OptiScaler NR or AIO generation.

## v2.6.28 — Automatic lock, native-DLSS, and permission preflight

- DLSS 5 removal and profile switching now detect a running game, ask once, close it, wait for loaded add-ons to unlock, and continue automatically.
- Unreal Engine games now detect native DLSS runtimes under shared Engine and project plug-in folders instead of incorrectly installing the Feeder route beside the executable.
- DLSS 5 installation now verifies real write access before changing files. Protected game folders prompt to restart Adas as administrator instead of failing after a partial install.

## v2.6.27 — Reliable profile repair after missing shader folders

- Profile switching now recreates a removed destination folder before restoring an original backed-up file.
- Repair no longer fails when a previous attempt already removed `reshade-shaders\Shaders`.
- Added regression coverage for the exact New Heights `ReShade.fxh` rollback failure.

## v2.6.26 — Manual renderer fallback that installers actually obey

- When reliable launch detection cannot identify the renderer, DLSS 5 Review & Install now asks you to choose DirectX 8–12, Vulkan, or OpenGL instead of leaving installation blocked.
- The saved renderer choice now controls DLSS 5 review, installation, repair, ReShade installation, and bulk ReShade updates.
- Manual renderer choices now select the matching ReShade hook filename and appear as explicit installation evidence.
- Experimental unified Feeder installs now register the RenoDX DLSS add-on for early loading, fixing sessions where Feeder delivered frames but neural rendering missed the required hooks.

## v2.6.25 — Automatic legacy ReShade relocation

- Fixed DX8/DX9 DLSS 5 repair incorrectly rejecting an existing ReShade `d3d8.dll` or `d3d9.dll` as an unknown wrapper.
- Adas now relocates a positively identified ReShade proxy to `dxgi.dll` automatically before installing dgVoodoo2, preserving the correct loader chain and uninstall ownership.
- Unknown third-party graphics wrappers are still left untouched and blocked with a clear error.

## v2.6.24 — DLSS 5 pipeline ownership repair

- Fixed DirectX 9 Feeder installs accidentally deleting the newly installed dgVoodoo translator when an older ReShade record used `d3d9.dll`.
- ReShade installs and updates now respect the active DLSS 5 pipeline's proxy name and preserve its shader files.
- The ReShade component row now refreshes immediately after DLSS 5 installation instead of incorrectly offering a separate installation.
- Independent ReShade replacement is blocked for DLSS 5 OptiScaler profiles because that pipeline owns the graphics proxy.

## v2.6.23 — Consistent automatic renderer setup

- Uses the same observed renderer for the game card, ReShade installation, and DLSS 5 review. Multi-renderer imports are treated as capabilities, not permission to guess DX11 or the highest DirectX version.
- Keeps verified launch evidence when Adas or ReShade creates its own configuration files, and migrates recent renderer observations from v2.6.22.
- Removes the automatic L.A. Noire ReShade 6.3.3 pin. Normal installation now follows the current Stable channel (6.8.0 in the bundled package); legacy versions remain an explicit user choice only.
- Starts directly launched games in their own executable folder and observes manifest-resolved launches, fixing games that failed only when launched from Adas and ensuring successful launches update the renderer everywhere.

---

## v2.6.22 — Automatic renderer verification

- Uses executable imports, current game-owned runtime logs, and short-lived launch observations to select DirectX 9–12, OpenGL, or Vulkan without choosing the highest version.
- Detects mismatched 32/64-bit add-ons, incorrect ReShade hook names, duplicate ReShade runtimes, damaged ownership records, and DLSS routes that do not match the current renderer.
- Installs ReShade beside the resolved game executable, including games whose real binary lives below the library folder.
- Stops ambiguous installs and asks for one launch from Adas instead of relying on title or engine defaults. OpenXR is detected separately from the rendering API.
- Removed the title-specific L.A. Noire renderer rewrite.

---

## v2.6.21

- Fixed leftover OptiScaler NR files blocking installation when the previous suite record is gone. Review / Install now previews known conflicting components, asks once, archives them, and continues installation automatically.
- Consent is checked against the reviewed file hashes and installation record; changed/new conflicts require a fresh review. Game-native DLSS runtimes and unrelated game-specific RenoDX mods are not cleanup targets.
- Confirmed Vulkan profile switches now perform removal inside Ada before setting up the new route. Shared-layer changes are not falsely presented as locally reversible; a failed new setup can be continued with Repair.
- Uninstall also archives recognized orphan OptiScaler NR/AIO components. Failed tracked removal retains its recovery record and does not run further orphan cleanup.

## v2.6.20

- Added Microsoft Visual C++ runtime presence/architecture checks before DLSS installation. Hosted 32-bit games require both x86 and x64; missing prerequisites link directly to Microsoft.
- Apply now switches game-local DLSS profiles automatically, with separate saved visual settings and a disk-backed recovery journal. Failed switches restore the previous files/settings; interrupted switches recover on the next Repair request. Vulkan/shared-layer changes retain restore-first protection.
- Added 32-bit DirectX 8 through dgVoodoo2's D3D8 wrapper, DX11 ReShade, and the matched x86 Feeder/x64 host. DX9 remains supported. Existing foreign wrappers are not overwritten.
- Added explicit renderer setup for 19 emulator families, including executable selection when multiple builds are present. Ada remembers the chosen executable and renderer; emulator settings themselves are not changed.
- Kept the existing stable, beta, unified, AIO and OptiScaler alternatives. No game performance or crash fix is claimed without in-game verification.

## v2.6.19

- Replaced the optional unified ShortFuse add-on with the supplied September 2 build, deployed as `renodx-dlss.addon64` (no download suffix). Stable RenoDX 4.55/4.70 remain separate.
- Updated the optional matched Feeder set to **0.12.1-beta.1**: protocol-v7 x86/host pair, in-game host controls, FSR 1 expand-back and current runtime-binding/DXVK pacing fixes. Exposed expand-back filter and sharpness in advanced settings.
- Updated the native DX11/Vulkan bridge to **1.4.7**, including the upstream NGX jump-table hook crash fix. Ordinary stale staging no longer takes priority over the packaged bridge; explicit local imports remain supported.
- Updated standalone AIO to **1.7.24** with verified download hashes. Added optional rejection-mask controls, disabled by default because nonzero strengths can suppress NR. Upstream performance measurements remain available in its overlay.
- Added **OptiScaler DLSS-NR 0.1.2** and the **NR-before-SR English fork** as separately selectable experimental profiles. Both packages are bundled, hash-verified, architecture-checked and tracked for reversible removal. Standard NR supports native-DLSS x64 DX12/Vulkan; the split route is native-DLSS x64 DX12 only.
- Standard OptiScaler install/uninstall/config-copy controls cannot overwrite suite-managed NR forks. Switching exclusive pipelines requires removing the previous suite first. Use **Insert**, not ReShade Home, for the forks' complete controls.
- No game-specific performance or black-screen fix is claimed without runtime testing.

## v2.6.18

- Added optional standalone AIO **1.7.17** for 64-bit DX9/DX11/DX12 and Vulkan games. Existing recommended and 32-bit routes are unchanged. Vulkan requires the registered 64-bit ReShade layer.
- Fetches all three author-published AIO assets once, verifies their pinned SHA-256 values, and caches them for reuse; a matching local-folder import is also available. These assets are not redistributed in the installer while redistribution permission is unconfirmed.
- Installs game-local ReShade, standard headers, VORT motion guidance and the companion shader, with hash-aware uninstall. AIO's shaders are loaded but left unchecked in the ordinary effect chain because AIO schedules them itself.
- Prevents stacking AIO with Feeder/Bridge/RenoDX DLSS and protects existing caller/wrapper DLLs. Remove the current suite before changing to or from AIO.
- Added plain-language AIO pipeline, NR, frame-generation, intensity, tone, detail and skin/character controls. New setups leave frame generation and early proxy initialization off.
- Diagnostics distinguish logged processing from a verified visible picture, ignore logs older than the current install, and no longer treat an absent optional NGX C hook alone as a broken runtime.

## v2.6.17

- Updated the packaged native DX11/Vulkan bridge to official DLSS5 Bridge **1.4.1**, including current Vulkan display-mode recovery and delivered-frame diagnostics.
- Updated the optional matched Feeder test route from 0.8.0-beta.4 to **0.11.0-beta.2**. Its x86 add-on and x64 host remain an inseparable protocol-v5 pair; stable Feeder 0.7 remains the default.
- Repair now clears only Adas DLSS entries that ReShade disabled after a crash and removes stale early-load references when compatibility routes change.
- Added final whole-suite file verification to catch incomplete copies, wrong 32/64-bit payloads, changed files, and antivirus quarantine before reporting success.
- Added **Check current setup** with plain-language diagnosis for common ReShade, shader, motion-vector, hosted Feeder, DLSS runtime, and NGX failures.
- Replaced the packaged DLSS-NR entry with the user-supplied RTX 20/30/40 compatibility-patched 310.8.0.0 runtime.

## v2.6.16

- Replaced the bundled native RenoDX DLSS5 v4.60 build with the user-supplied v4.70 release, including its HDR fix, revised sliders, compatibility adjustments, and potential flicker fixes.
- The package is stored versioned for verification but always deploys to games as `renodx-dlss5.addon64` without the browser-added `(3)` suffix.
- Native 64-bit DirectX 11, DirectX 12, and Vulkan profiles now select v4.70 automatically. Stable Feeder remains pinned to its required RenoDX v4.55 build.

## v2.6.15

- Fixed severe hangs when opening or switching games by coalescing card refresh notifications and caching DLSS deployment and executable architecture lookups.
- Moved DLSS 5 installation work off the interface thread so progress remains responsive during large runtime copies and verification.
- Limited background shader and add-on synchronization to two game folders at a time and delayed it briefly until the first screen is responsive.
- Stopped the generic add-on synchronizer from redeploying suite-owned Feeder, Bridge, and RenoDX DLSS files or deleting files owned by the DLSS 5 suite.
- Avoided rewriting unchanged add-ons and replaced per-line synchronous log writes with batched background logging.
- Prevented normal and elevated copies of Adas from running duplicate scans at the same time.

## v2.6.14

- Fixed L.A. Noire repair installing a DirectX 11 ReShade proxy while leaving the game configured for DirectX 9. Adas now switches `Renderer` to DirectX 11 automatically, preserves the original setting, and restores it when the suite is removed.
- Fixed 32-bit Feeder installs leaving x64 NVIDIA DLSS and Streamline DLLs beside the 32-bit game executable. Those runtimes are now staged only under `host64`; Repair migrates and removes older Adas-owned root copies safely.
- Updated DLSS 5 detection and manual runtime import to recognize the hosted 64-bit runtime layout without requesting duplicate files.

## v2.6.13

### Current DLSS 5 upstream integration

- Added the matched DLSS5-Feeder **0.8.0-beta.4** test route as a separate profile without replacing stable 0.7. It includes Smooth Motion synchronization, recoverable GPU timeouts, v4.60 safe defaults, guide probes, and protocol-v3 32-bit Vulkan support. Stable remains the recommended default except where the beta is required for 32-bit Vulkan.
- Native 64-bit Vulkan games with their own DLSS now use RenoDX DLSS5 4.60 + final DLSS5 Bridge 1.3.0, preserving the game's real depth, motion vectors, jitter, and quality preset instead of routing through estimated-motion Feeder.
- Updated the experimental unified ShortFuse package from 0.2 to **0.3** and kept it explicitly separate from the compatibility-tested stable routes.
- Bundled the standard ReShade shader headers, fixed v4.60's safe neural defaults, and retired obsolete RHI 2.5.x DLSS add-on selections so the dedicated per-game suite owns one coherent deployment.

## v2.6.12

### 32-bit cleanup and offline core package

- Fixed 64-bit RenoDX DLSS binaries renamed to `.addon32` being counted as valid suite installations. Removal now archives orphaned suite files even when an older ownership record is missing, so the status and delete button clear correctly.
- Fixed suite-owned RenoDX files being mistaken for game-specific RenoDX mods and producing the misleading **Download from Discord** action. The separate row is now labelled **RenoDX game mod**.
- Bundled the matched NVIDIA Streamline/DLSS runtime package, current ReShade 6.8.0 x86/x64 runtimes, and L.A. Noire's required ReShade 6.3.3 x86/x64 runtimes. The DLSS 5 suite no longer needs a Discord download or an external runtime ZIP for these files.
- L.A. Noire repair/removal now archives the impossible x64 `renodx-dlss5.addon32` payload that caused the 32-bit game to fail during ReShade add-on loading.

## v2.6.11

### Maximum-quality DLSS 5 routing

- Added a clear install-profile choice. **Maximum Quality** is the default; ShortFuse 0.2 remains available as an explicitly experimental unified option with its stability tradeoffs shown before installation.
- Native 64-bit DirectX 12 now uses stable RenoDX DLSS5 4.60. Native 64-bit DirectX 11 uses 4.60 with DLSS5 Bridge 1.3.0. Feeder routes remain pinned to RenoDX DLSS5 4.55 as required by the current released Feeder build.
- DirectX 9 Feeder installs and configures current dgVoodoo2 automatically. DirectX 10 Feeder installs stable DXVK and the correct 32- or 64-bit Vulkan ReShade layer automatically.
- Repair removes incompatible stable, experimental, current-bridge, and obsolete-bridge combinations before deploying one coherent route. Native games also receive reliable early-load configuration when a Streamline interposer is present.
- Vulkan layer management now installs both architectures, fixing repeated 64-bit ReShade deployment into 32-bit translation paths.

## v2.6.10

### Runtime safety repair

- Fixed 64-bit-only RenoDX DLSS being renamed to `.addon32` and deployed into 32-bit games. Add-on and ReShade payloads now pass an executable-architecture check before and after installation.
- DLSS 5 Review/Repair now re-detects the selected game executable instead of trusting stale bitness metadata. Existing invalid `renodx-dlss.addon32` copies are removed from 32-bit games; RenoDX remains correctly isolated in Feeder's 64-bit host.
- Native DLSS games keep their own DLSS and Streamline versions. Automatic runtime import fills only missing files, and Repair restores game-owned runtimes that older Adas builds replaced. This addresses the 007 First Light black screen while retaining the added DLSS NR runtime.

## v2.6.9

### Compatibility-safe DLSS repair

- Review/Repair fixes malformed ReShade `**\**` search paths and canonicalizes case-sensitive ReShade keys without deleting comments or reformatting unrelated INI content.
- Every ReShade setting changed by the suite is journaled before modification. Uninstall restores only suite-owned values and keeps later user edits.
- Components follow a tested compatibility matrix: stable RenoDX v4.55 for native D3D12, v4.55 + DX11 Bridge for native D3D11, and pinned v4.55 for all Feeder transports. The experimental unified build is not deployed by default, and its auto-updates no longer overwrite compatibility-pinned games.

## v2.6.7

### DLSS 5 Suite

- Updated the matched DLSS5-Feeder payload to v0.7.0, including protocol-v2 x86/x64 hosting, OpenGL support, the 32-bit host stability fix, Vulkan color fix, work-resolution control, motion-provider diagnostics, and the optional Vulkan fallback layer.
- Replaced DRME as the automatic provider with upstream's recommended LumeniteFX Kernel. Adas downloads it from the author's official GitHub link, sets `DLSS5_MV_PROVIDER=3`, and enables Kernel above DLSS 5 Feed in the active preset.
- Added local OpenGL ReShade installation as `opengl32.dll` and preserved architecture-aware hosted setup for 32-bit games.
- The unified `renodx-dlss.addon64` remains the only RenoDX neural add-on; the standalone DX11 Bridge and legacy add-on names are retired during repair.

### RHI 2.4.9 Sync

- Added DLSSNR 310.8.SF and SF-v2, including RTX 20/30/40/50 support, and deploys `nvngx_dlss.dll` with the NR runtime while preserving an existing copy as `.original`.
- Added the MOTD status-bar button and per-game add-on How to Use links.
- Added the Space Marine 2 path fix, both AI: The Somnium Files 64-bit name variants, and the ReshadeMotionEstimation library entry.

---

## v2.4.7

### Manifest Updates

- Added DLSS5 DX11 Bridge and DLSS5 Feeder to the addon picker — both enable DLSS 5 Neural Rendering in D3D11 games. Additional setup steps are required; the How To Use button on each addon links to the repo for instructions.
- Added DLSS5 Feeder companion shader to the shader pack library.

### Bug Fixes

- Fixed the Neural Rendering column not showing `nvngx_dlssnr.dll` as installed after deploying it. It now updates immediately without needing a Refresh.
- The Neural Rendering column now clearly shows "Custom" when a custom DLL is active.

---

## v2.4.6

### Bug Fixes

- Fixed RenoDX DLSS5 not auto-updating to games when a new version is released. The addon now deploys the updated file directly from its own staging folder and no longer creates a redundant copy in the addons folder.

### Manifest Updates

- Added CubeLUT3Ddith by aron7awol to the shader pack library — Cube 3D LUT shader with dithering to reduce banding.

---

## v2.4.5

### Bug Fixes

- Fixed RenoDX DLSS5 not deploying to game folders after the addons staging folder was deleted. The addon now deploys directly from its own staging location.

---

## v2.4.4

### New

- **RenoDX DLSS5 addon** — `renodx-dlss5.addon64` is now a first-class addon in the per-game addon picker, listed above RenoDX Upgrade. Enable it per game from the Addons combo → Select. RHI downloads it automatically, keeps it updated silently alongside other components, and deploys `nvngx_dlssnr.dll` to the game folder alongside it if not already present. For 50 Series GPUs only.

---

## v2.4.3

### New

- **DLSS Neural Rendering** — deploy `nvngx_dlssnr.dll` to any game from the Neural Rendering column, swap versions and set presets. Obtain `renodx-dlss5.addon64` from the RenoDX Discord, drop in `%LocalAppData%\RHI\Custom\Addons\`, and select from the addon picker to activate in-game.

### Changes

- "Custom" is now available as a version option in the DLSS & Streamline Defaults dialog, matching the per-game panel.

### Bug Fixes

- Fixed the selected game's detail panel not updating after the background manifest fetch completes on launch. Manifest-driven content (presets, NR column, notes etc.) is now always current after startup.
- Fixed OptiScaler uninstall deleting AMD FidelityFX DLLs and other companion files that the game shipped. RHI now backs up any existing file before overwriting it on install, and restores it on uninstall.
- Fixed Streamline version showing an older number when a release bundles a lower-versioned `sl.interposer.dll` alongside newer DLLs (e.g. 2.12.129 shipping with a 2.12.128 interposer). RHI now picks the highest-versioned DLL in the folder as the display version.
- Fixed Battle.net launcher entries (Battle.net.12345 etc.) appearing in the game library. These are now blocked by prefix so no individual blacklist entries are needed.

### Maintenance

- Internal groundwork for RenoDX Nexus Mods integration.
- Internal infrastructure improvements for upcoming features.

### Manifest Updates

- Added reeShaders by LVutner to the shader pack library — lightweight sharpening (TinySharpen), clarity, and chromatic aberration shaders.
- Fixed Metal Gear Solid 4 and Peace Walker (Master Collection) not detecting the game folder correctly.

---

## v2.4.2

### New

- **Automatic Updates** — new setting in Settings → Component Updates. When enabled, RHI silently installs updates for all components in the background after each update check. Updates are applied one at a time so there's no disruption to the app. Games that are running when an update is ready will be retried automatically once they close. Respects all per-game and global update exclusions. Does not apply to app updates.

### Changes

- Add Game button moved from Settings to the sidebar, next to the Filter games search box.
- Settings "Game Library" section renamed to "Component Updates" and reorganised — Check For Updates on the left, Automatic Updates on the right.
- Peak Nits "Apply to All Games" button moved directly below the nits input, above HDR Auto-Toggle.
- Added labels above the ReShade, Display Commander, and OptiScaler DLL rename dropdowns in the overrides panel.

### Bug Fixes

- Fixed PCGamingWiki links not opening. The links now resolve correctly following a server change on PCGamingWiki's end. The resolution method is now manifest-driven, so if PCGamingWiki make further changes in the future this can be fixed remotely without an app update.
- Fixed Streamline version not displaying correctly for builds that don't include `sl.interposer.dll`. RHI now picks the highest versioned Streamline DLL available as the version source.
- Fixed OptiScaler showing the stable version number after updating a Nightly install.
- Fixed DLSS Defaults render scale reverting to 75% Quality+ on restart after being set to Off.
- Fixed the setup window cutting off — both buttons are now fully visible at all display scales.

---

## v2.4.1

### Bug Fixes

- Fixed DLSS Enabler and DOF Fix not auto-updating when a newer version exists but was released with an older date. Now picks the highest version number rather than the most recently created release.

---

## v2.4.0

### Bug Fixes

- Fixed manifest `renodxIniOverrides` keys being ignored on fresh install when the key already existed from a prior install step. Manifest overrides now always apply on install. Updates preserve user-edited values.

### Manifest Updates

- Added VHOLUME — UE-Extended support with SDR upgrade path.

---

## v2.3.9

### Bug Fixes

- Fixed Game Pass games with exe files directly in the Content folder (e.g. Hades II) being detected at the wrong path.

### Manifest Updates

- Fixed Terminator: Resistance Engine.ini being written to the wrong config folder. Now correctly targets WindowsNoEditor.
- Fixed Hades II on Game Pass using the wrong install path.
- Fixed Resident Evil 4 and Resident Evil Requiem incorrectly detected as DX11. Forced to DX12.
- Fixed Crimson Desert incorrectly detected as DX11. Forced to DX12.

---

## v2.3.8

### Bug Fixes

- Fixed "Configure RTX HDR" button not being clickable on games without a RenoDX mod. Also fixed the button not updating after enabling RTX HDR in the cog dialog.

---

## v2.3.7

### Bug Fixes

- Fixed DXVK showing a perpetual update badge even when set to Off. Selecting a game card now automatically cleans up leftover DXVK files if the variant is set to Off.

### Manifest Updates

- Fixed S.T.A.L.K.E.R. 2: Heart of Chornobyl incorrectly detected as DX11. Forced to DX12.
- Fixed Crimson Desert incorrectly detected as DX11. Forced to DX12.
- Added DLSS RR preset F (value 6) to the Ray Reconstruction dropdown.

---

## v2.3.6

### New

- **Per-shader selection** — each shader pack in the shader picker can now be expanded to show its individual `.fx` files. Tick only the shaders you want rather than deploying the full pack. The pack checkbox shows a dash when a partial selection is active, and a tick when all files are included.
- **Shader profiles** — save, load, rename, and delete named shader selections using the new Profiles panel on the right side of the shader picker. Profiles store both pack selection and per-file exclusions, and can be loaded in the per-game shader picker too.
- **Export shader selection** — the Export button zips your currently selected shader files and copies the archive to your clipboard, ready to paste into Discord or use as a backup.
- **Import shader profile** — the Import button in the shader picker lets you load a profile archive exported by RHI. The profile is added to your profile list and, if you don't already have the shader packs cached, the files are extracted from the archive automatically.
- **Keep ReShade.ini Updated** — new per-game option in the ReShade ⚙ cog. Set to No to prevent RHI from touching that game's reshade.ini automatically — Apply to All Games, ReShade installs, and updates will all skip it, preserving any manual edits you've made.
- **OptiScaler upscaler selector** — the OptiScaler ⚙ cog now has a two-combo upscaler row. Pick the graphics API on the left (DX11, DX12, or Vulkan) and the upscaler on the right. Options update automatically based on the selected API and write directly to `OptiScaler.ini`.
- DOF Fix can now be installed without ReShade being managed by RHI. Unchecking ReShade in the Global Update Inclusion dialog unlocks the DOF Fix install button, consistent with how RenoDX, ReLimiter, and Display Commander already behave.
- RHI will now file your taxes, negotiate your mortgage, walk your dog, and attend your cousin's wedding on your behalf. Results may vary. RHI accepts no liability for family disputes arising from the wedding attendance feature.

### Changes

- On standard refresh, RHI now re-checks games that were previously confirmed to have no DLSS. If DLSS files have since appeared (e.g. a preloaded game that received its content on release), the game is picked up automatically without needing a Full Refresh.
- The shader picker now has Expand All / Collapse All and Deselect All buttons for faster navigation.
- Added "Effect list style" setting in Settings → Screenshots & Hotkeys. Defaults to Tabs, which groups shaders into named tabs in the ReShade overlay instead of a flat tree. Apply to All Games writes the setting to all managed reshade.ini files.
- The Upgrade Path option in the UE-Extended compatibility settings cog now shows "HDR / Off" and "SDR / On" instead of bare numbers, making the setting self-explanatory.

### Bug Fixes

- Fixed enabling the DLL naming override toggle corrupting OptiScaler and ReShade filenames when OptiScaler had renamed ReShade during install. RHI's in-memory record of ReShade's filename was stale, causing the wrong file to be renamed and the other to be deleted.
- Fixed `dxgi.dll` missing from the ReShade filename dropdown when OptiScaler was using it. The name is now always shown — selecting it is blocked only if it would actually conflict with an installed component.
- Fixed turning off DLL naming overrides leaving ReShade stuck at `ReShade64.dll` when OptiScaler occupied `dxgi.dll`. RHI now reverts OptiScaler's filename first, freeing `dxgi.dll` before ReShade tries to reclaim it. When ReShade was at `dxgi.dll` and OptiScaler at a custom name, the revert now moves ReShade aside and restores both to their correct default names.
- Fixed NVIDIA Profile Overrides panel not appearing after installing OptiScaler on a game that previously had no DLSS. RHI now clears the DLSS skip cache immediately on OptiScaler install so the next Refresh detects the new files correctly.
- Fixed DLSS version dropdown showing an incorrect version when a DLSS build not in the manifest is installed. The actual installed version is now shown correctly.
- Fixed Streamline version not being detected when `sl.interposer.dll` is absent. RHI now falls back to `sl.common.dll` for version detection.
- Fixed Streamline restore failing after the in-game backup files were already consumed by a previous restore. RHI now keeps a compressed backup of the game's original Streamline DLLs in AppData as a fallback, so restore always works even after repeated attempts.
- Fixed Streamline being deployed at the wrong version when OptiScaler is installed on a game that never had Streamline before.
- Fixed a race condition where concurrent shader pack updates could fail to save their version cache, causing the same packs to re-download on every launch.
- Fixed DLL naming override toggle incorrectly appearing as enabled for games where OptiScaler had previously been installed and uninstalled, causing ReShade to install under the wrong filename.
- Fixed `CrashReportClient.exe` being picked as the auto-detected launch executable for games that include it in their install folder.

### Manifest Updates

- Fixed Engine.ini config path detection for Satisfactory (uses `FactoryGame` as its AppData folder name).
- Fixed Ball x Pit incorrectly detected as 32-bit.

---

## v2.3.5

### Changes

- **Luma HDR on First Boot** — the HDR setting in the Luma ⚙ cog now has three options: Default (lets Luma manage its own HDR state), Off (sets `EnableHDR=0` and `DisplayMode=0`), and On (sets both to 1). Previously only Off/On were available.
- **Remove game confirmation** — removing a manually-added game now shows a confirmation dialog. If any RHI-managed components (ReShade, RenoDX, Luma, OptiScaler, DXVK, etc.) are installed, a checkbox lets you uninstall them from the game folder at the same time.

### Bug Fixes

- Fixed OptiScaler uninstall deleting a game's own `nvngx_dlss.dll` (and `nvngx_dlssd.dll` / `nvngx_dlssg.dll`) when the game shipped with DLSS in its root folder (e.g. Control). RHI now backs up the game's original DLSS files before deploying its own on install, and restores them on uninstall.
- Fixed Luma uninstall deleting the `reshade-shaders` folder when ReShade was still installed. Empty subdirectories inside `reshade-shaders` are now cleaned up, but the root folder itself is never removed.
- Fixed Luma HDR cog only writing `EnableHDR` — `DisplayMode` is now also written, which is required for HDR to fully enable or disable in Luma.
- Fixed UI becoming unresponsive after removing a manually-added game and restarting. If the last selected game had a stale or deleted install path, selecting the card would throw a `DirectoryNotFoundException` on every interaction, locking up the interface.

### Manifest Updates

- Blacklisted Apple Devices, Apple TV, and iTunes (Microsoft Store / WindowsApps) — these will no longer appear in your game library.

---

## v2.3.4

### New

- **New Luma Mods alert** — the "New Mods Available" notification now also fires when new completed Luma mods are added to the Luma Framework wiki. Works the same as the existing RenoDX and Ultra+ alerts — Dismiss marks them as seen, Close keeps the button visible for later.

### Bug Fixes

- Fixed drag-dropping a bespoke Luma mod archive onto a non-Unreal game (e.g. DX9 titles like The Witcher 2) not showing the Luma row after install. The Luma row now appears immediately after a drag-drop install on any game type, and persists across restarts.

---

## v2.3.3

### New

- **Generic Luma for Unreal Engine games** — RHI now supports Luma HDR for all DX11 Unreal Engine games, not just named titles. Every DX11 UE game in your library shows a Luma row alongside RenoDX. Game-specific Engine.ini tweaks and launch arguments from the Luma wiki are applied automatically on install. ReShade and DLSS are managed by RHI on all Luma games and kept up to date by Update All. Both RenoDX and Luma rows are always visible side by side — no toggle needed.
  - The Luma ⚙ cog has a TAA settings toggle; wiki-recommended TAA Engine.ini keys are applied on install when available.
  - Games supporting both DX11 and DX12 show the Luma row and get `-dx11` set automatically on install.
  - Uninstalling Luma no longer removes ReShade or DLSS — all three are managed independently.
  - UE5 games use DX12 and don't show a Luma row by default. Set the Graphics API override to DirectX11 in Game Overrides first if needed.

- **OptiScaler Nightly channel** — switch between Stable and Nightly per game in the OptiScaler ⚙ cog. The cog has significantly expanded for Nightly installs:
  - **Streamline/DLSS Enabler** toggle deploys both together (Streamline is required for DLSS Enabler). Includes a **Streamline Version** picker to select which version to deploy per game.
  - **Frame Generation settings**: FG Input, FG Output, FG Nvngx Replacement, and HUD Fix — all write directly to the game's OptiScaler.ini and persist per game.
  - **Additional Settings**: DLSS SR Preset (J/K/L/M), DLSS RR Preset (D/E), Render Scale (Off + presets from 33% Ultra Perf to 100% DLAA), Disable Flip Metering, and Framerate Limit.
  - **Engine.ini Settings** (UE games only): Dilated Motion Vectors, FSR Crash Fix, FSR-FG Swapchain, and Upscaler Plugin — each writes the relevant Engine.ini keys immediately.
  - **4 user-configurable preset slots** — save the current cog settings into a named slot and apply them to any game. Slot 1 defaults to a DLSS Enabler preset.
  - DLSS Enabler auto-updates in the background from the RHI GitHub releases.

- **Shader Management** — new global setting in Settings (replaces "Custom Shaders"): **RHI Managed** deploys built-in shader packs to all games, **Custom** uses your own shader directory, **Off** disables all shader deployment globally. Also available as a per-game override in Game Overrides.

- **DXVK ⚙ cog — DXVK as Native** — alongside Prefer DXGI Swapchain, a new Flags setting controls the Vulkan/OpenGL Present Method in the NVIDIA driver profile. Standard (`0x000802A5` — Treat DXVK as Native) is the default and community-recommended value; Alternative (`0x00080004`) adds DirectFlip.

- **Smooth Motion → Low Latency cascade** — enabling Smooth Motion in Driver Profile Settings automatically sets Low Latency to Ultra. When Smooth Motion is turned off, Low Latency is restored to its previous value. Low Latency is locked while Smooth Motion is enabled.

- **First-launch setup window** — a setup screen now appears on a fresh install asking how you want ReShade managed, before the main window opens.

### Changes

- DOF Fix moved to a new Recommended section, above the frame limiters.
- Graphics API badges now show DX11 and DX12 separately with individual override options in Game Overrides.
- The Launch executable section in Game Overrides now shows the currently detected exe name next to the heading.
- RenoDX, ReLimiter, and Display Commander can now be installed without ReShade being managed by RHI. Uncheck ReShade in the Global Update Inclusion settings to unlock installation of dependent components.
- The DXVK variant combo in Game Overrides saves your preference without auto-installing. The Install DXVK button appears when a variant is selected — press it when ready.
- Selecting a DXVK variant while DXVK is already installed uninstalls the old version first — reinstall when ready.
- Lilium HDR DXVK installs write Standard flags (`0x000802A5`) to the NVIDIA profile.
- Switching filter tabs now keeps the selected game highlighted if it appears in the new filter.
- The per-game Shaders and Addons dropdowns now correctly reflect the global Off/Custom setting instead of always showing Global.
- Simple View window is slightly taller.

### Bug Fixes

- Fixed many UE4 DX12 games incorrectly showing as DX11.
- Fixed UE-Extended toggling off after a background refresh for games with both a named wiki mod and a user opt-in.
- Fixed startup scan slowness for games not on Steam or PCGamingWiki — live HTTP lookups are now cached so they only fire once per game.
- Fixed DLSS path cache invalidating every session for games with installation paths containing mixed separators or path casing differences (affected Nioh 3, AC Black Flag Resynced, and others).
- Fixed "Create NVIDIA Profile" registering the wrong executable for games that have multiple exe files — manifest launch exe overrides are now used when available.
- Fixed app update being silently aborted if the MOTD dialog was open when you clicked Update Now.
- Fixed DXVK "Don't show this warning again" checkbox not persisting across sessions.
- Fixed Display Commander update and download failing — updated the GitHub release tag.
- Fixed OptiScaler unnecessarily renaming ReShade to ReShade64.dll when there was no filename conflict.
- Fixed ReShade DLL naming overrides allowing the ReShade name to be set to the same filename OptiScaler is using.
- Fixed DLL naming override toggle not reverting OptiScaler's filename when disabled.
- Fixed DLL naming override "Don't show again" not persisting.
- Fixed Luma archives with newer format (no d3dcompiler_47.dll) being misidentified as addon archives.
- Fixed OptiScalerini marked read-only causing uninstall to abort halfway, leaving DLSS files and the OptiScaler folder behind.
- Fixed OptiScaler updates leaving stale files from the previous version.
- Fixed OptiScaler install creating spurious `.dll.original` backup files for files it deploys.
- Fixed OptiPatcher never receiving automatic updates.
- Fixed Engine.ini writers appending duplicate section headers — all writes now merge into the existing section.
- Fixed Graphics API override to DirectX11 on a UE5 game not showing the Luma row.
- Fixed the first-launch setup window appearing very small at high Windows display scaling.
- Fixed "No RenoDX mod available" button appearing clickable — it is now greyed out.

### Manifest Updates

- Added ELDEN RING NIGHTREIGN install path override.
- Removed Skyrim Creation Kit from the game library.
- Added REANIMAL install warning (native HDR, no mod needed).
- Blacklisted Apple Devices, Apple TV, and iTunes from appearing in the game library.
- Added `componentUrls.dc` — allows the Display Commander download URL to be updated remotely.
- Added Skyrim Creation Kit to the blacklist.
- Various engine tags, API overrides, and game-specific notes added across multiple releases.

## v2.3.2

### Bug Fixes

- Fixed games installed while downloading showing the wrong folder path after a Refresh — a full Refresh now correctly re-detects the game folder once the download is complete.
- Fixed deprecated mods from the RenoDX wiki showing up in RHI — mods in the "Deprecated mods" section are now skipped during wiki parsing.
- Fixed Xbox/Game Pass Unreal Engine 5 games showing DX11 — games with a UE5 version set in the manifest now correctly show DX12.

### Maintenance

- Graphics API badges now show DX11 and DX12 separately instead of combined.
- Graphics API override in the per-game overrides panel now has separate DirectX11 and DirectX12 options.
- Install warnings are now wired up for all components — ReShade, ReLimiter, Display Commander, OptiScaler, RE Framework, and DXVK. Game-specific notes can be shown before installing proceeds.
- Game-specific notes in the ReLimiter and Display Commander info dialogs now appear above the release notes.
- Background work for upcoming generic Luma UE mod support.

### Manifest Updates

- Added Doom Eternal to wiki unlinks (no RenoDX mod available).
- Added The Sinking City Remastered ReShade info note and install warning: amd_FusionFX.dll must be deleted from the game folder for frame generation to work.
- Added LISA — "The Painful" and "The Joyful" Definitive Editions now show as separate installs.
- Added DOOM + DOOM II split — the New rerelease and DOS versions now show as separate installs, with correct Vulkan API and Kex Engine tag.
- Added engine tags for 9 games: 007 First Light (Glacier 2), CrossCode (NW.js), Cry of Fear (GoldSrc), FATAL FRAME: Maiden of Black Water (Katana), Nioh 3 (Katana), Persona 5 Royal (GFD), Project Motor Racing (GIANTS Engine), Assassin's Creed Black Flag Resynced (Anvil).
- Added DOOM 64 Vulkan API override.
- Added Mistfall Hunter engine version (Unreal Engine 5.7.4).
- Added Skyrim Creation Kit to the blacklist — it will no longer show up in your game library.
- Added REANIMAL install warning — the game has native HDR, no RenoDX mod is needed.

---

## v2.3.1

### Bug Fixes

- Fixed per-game shader mode (Global/Custom/Off) reverting when reopening the overrides panel.
- Fixed Reset Overrides not fully resetting Bitness, Graphics API, and ReShade Channel.
- Fixed Reset Overrides not reinstalling ReShade with Stable channel when a channel override existed.
- Fixed per-game Bitness and Graphics API overrides not being applied after Refresh.

---

## v2.3.0

### Bug Fixes

- Fixed several per-game settings not applying correctly after the v2.2.9 multi-store update — folder overrides, custom ReShade auto-redeploy, per-game shader selection, addon selection, preset installs, and DLL naming overrides were all silently using the wrong lookup key. All affected settings now work correctly for both single-store and multi-store setups.
- Fixed DLL naming overrides (e.g. ReShade64.dll) being reset back to dxgi.dll on every Refresh or Update All.

### Changes

- "RS Channel" label renamed to "ReShade Channel" in the per-game overrides panel.

### Manifest Updates

- Fixed Enshrouded showing DX11/12 — now correctly shows Vulkan. Added Holistic engine tag.
- Fixed Guild Wars 2 ReShade install — now deploys as `d3d11.dll`.
- Added Stellar Blade™ — PCGW link, engine version (UE 4.26.2), UE-Extended with SDR upgrade path.
- Added Quake split entries — original and rerelease (`rerelease` subfolder) as separate installs.

---

## v2.2.9

### New

- **New Mods Alert** — a green "New Mods Available" button now appears in the toolbar when new RenoDX mods or Ultra+ mods are added since your last check. Click to see the list, then dismiss (hides until more arrive) or close (keeps the button visible). Checks run on launch, Refresh, Check for Updates, and every 4 hours.
- **Multi-Store Support** — the same game installed from different storefronts (Steam, Xbox, Epic, etc.) now appears as separate entries with independent settings. Each copy gets its own ReShade channel, shader mode, DXVK variant, and other per-game overrides. Store badges tell them apart. Your existing settings carry over automatically.
- **DXVK — Prefer DXGI Swapchain** — new setting in the DXVK ⚙ cog. Improves compatibility and HDR support for DXVK games by setting the correct Vulkan present method in the NVIDIA driver profile. Saved with your profile export/import.

### Changes

- Game Pass games now get Engine.ini deployed to the correct folder (`WinGDK`) — previously it was always written to `Windows`, which some games don't read.
- Engine.ini settings (HDR keys, LUT) now show in the RenoDX ⚙ cog even when a mod update is pending.
- Beast of Reincarnation and Palworld now get UE-Extended installed with only the LUT setting deployed — HDR keys are skipped since both games have their own in-engine HDR option.

### Bug Fixes

- Fixed DXVK uninstall leaving the game stuck on Vulkan ReShade — the Vulkan rendering path is now correctly cleared on uninstall.
- Fixed mod author badges not updating immediately when switching between Luma and RenoDX mode.
- Fixed games like Borderlands 4 installing the wrong RenoDX addon — games marked as native HDR now always get UE-Extended, even if a named mod exists on the wiki.

### Manifest Updates

- Fixed Mass Effect Andromeda not being recognized for Luma/RenoDX compatibility (store detection returned slightly different names).
- Added DristoforColumb donation link.
- Fixed Grand Theft Auto V Legacy incorrectly showing as having a RenoDX mod — the mod is for GTA V Enhanced only.
- Fixed wiki name matching for Grand Theft Auto V Enhanced.
- Added ultrawide fix link for Beast of Reincarnation.
- Fixed Denshattack! installing the wrong addon — now correctly installs UE-Extended.
- Added Monster Hunter Wilds RE Engine tag — RE Framework row now appears correctly.
- Added Beast of Reincarnation engine version (UE 5.4.4) and native HDR flag.
- Added ReShade 6.7.3 to the legacy version picker.

---

## v2.2.8

### Changes

- **Quick Start Guide** ÔÇö improved text readability with lighter text colors, removed extra line spacing in tip boxes
- **RTX HDR Settings improvements**:
  - Peak Brightness now shows an inline warning when set above 600 nits (high values can look unnatural)
  - Middle Grey now shows an inline warning when perceived paperwhite exceeds 203 nits (can look washed out)
  - Added preset buttons (100, 125, 150, 175, 200) next to Auto for quick Middle Grey selection by target paperwhite

### Manifest Updates

- Added `rtxHdrInfoUrl` field linking to the Reddit RTX HDR settings reference guide.
- Added Watch_Dogs2 install path override (covers underscore variant detection).

---

## v2.2.7

### New

- **Quick Start Guide** ÔÇö new "Quick Start" button in the toolbar opens a comprehensive guide covering:
  - Step-by-step setup (select game, install ReShade, install RenoDX, choose shaders)
  - Frame limiters with VRR-friendly FPS targets by refresh rate (60/120/165/240/360Hz)
  - DLSS/Streamline updates and automatic version detection
  - NVIDIA driver settings (VSync, Low Latency, Smooth Motion, ReBAR)
  - RTX HDR for games without RenoDX mods
  - Vulkan game handling and admin requirements
  - Drag-and-drop for adding games, addons, and ReShade presets
  - Full Refresh troubleshooting for missing games or changed install locations
  - System tray features and quick-launching recent games

### Changes

- RTX HDR now defaults to **Saturation -25** when first enabled (previously 0/neutral). Reduces the oversaturation that RTX HDR tends to introduce.
- RTX HDR Middle Grey slider now shows perceived paperwhite nits in parentheses, e.g. "Middle Grey: 26 (121 nits)". Set your Peak Brightness, click Auto, and the Middle Grey + perceived nits are calculated automatically using the ITU formula. Updates when contrast/gamma changes.

### Bug Fixes

- Fixed games with named mods from Discord incorrectly showing "Reinstall UE-Extended" instead of "Download from Discord". When a named addon file is on disk (e.g. from a Discord drag-drop) but no wiki entry exists, the card now correctly shows the Discord link button instead of the generic UE install button.

### Manifest Updates

- Added engine tag for Halo: The Master Chief Collection (`Halo Engine`).
- Added DX11 API override for Grim Dawn.

---

## v2.2.6

### New

- **UE-Extended default for generic UE games** ÔÇö any Unreal Engine game without a named RenoDX mod now installs UE-Extended (`renodx-ue-extended.addon64`) automatically. Switch back to the standard generic addon via the UE-Extended dropdown (set to Off) in the RenoDX ÔÜÖ dialog. Your choice persists across restarts.
- **RTX HDR Settings improvements**
  - Gamma preset buttons (2.0, 2.2, 2.4) below the Contrast slider for quick selection.
  - Middle Grey is now a slider (10ÔÇô100) with an **Auto** button that calculates the ITU-correct value from your Peak Brightness and Gamma settings.
  - **Save as Default** / **Set Default** buttons ÔÇö save your preferred RTX HDR settings once and apply them to any game with one click.
  - Debanding is greyed out when not running as admin (requires elevated privileges to write).
  - RTX HDR now defaults to **Gamma 2.2** with ITU-correct Middle Grey when first enabled, instead of the flat Gamma 2.0 default.

### Changes

- **ReShade Settings cog** ÔÇö added per-game Overlay Key and Screenshot Key pickers. Click the field and press any key to capture it, then hit Apply. Writes to all `reshade*.ini` files in the game folder so all swapchain configs stay in sync (important for Vulkan games that create multiple ReShade instances).
- **Import Profiles confirmation** ÔÇö the Import button in Settings now shows a warning that the operation is irreversible, and notes if admin-only settings (e.g. ReBAR) will be skipped due to insufficient privileges.
- **ReLimiter Settings** ÔÇö Target FPS and DLSS Hooks controls are now greyed out when no `relimiter.ini` exists in the game folder. Deploy the ini first to enable them.

### Bug Fixes

- Fixed RE Framework showing a pending update on every launch. The update check was using the oldest installed record (e.g. an old DMC5 install from a previous version) as the comparison version, causing it to always detect a newer remote. Now uses the highest installed version across all RE Engine games for the comparison.
- Fixed Game Pass games not launching ÔÇö clicking Launch did nothing because there was no Xbox launch path. Game Pass games are now launched via their App User Model ID (`shell:AppsFolder\{AUMID}`), the correct activation method for packaged GDK/UWP games.
- Fixed RTX HDR on/off state not reflecting changes made outside RHI (e.g. via NVIDIA App or after a driver update). The toggle now reads the live driver profile on every refresh and when opening the ÔÜÖ dialog, keeping RHI in sync.
- Fixed RTX HDR settings not being included in the Export Profile backup. All 6 RTX HDR settings are now exported and correctly restored on import, including using raw NVAPI on the import path (NvAPIWrapper silently ignores these setting IDs).
- Fixed ReLimiter Update All silently failing to update `ul_meta.json` after a session restart ÔÇö the update deployed the new binary but the version file kept the old version, causing the update indicator to reappear on every launch.
- Fixed ReShade overlay hotkey and screenshot hotkey being reset to RHI's configured value whenever RHI deployed `reshade.ini`. The root issue was that `ApplyOverlayHotkey` and `ApplyScreenshotHotkey` always unconditionally overwrote the key ÔÇö so any time RHI touched the ini (ReShade update, INI button, etc.) with a non-default hotkey configured in Settings, it stamped over whatever the user had set in-game. These functions now preserve existing values unless the caller explicitly requests an overwrite (used by the "Apply to All Games" actions in Settings).

---

## v2.2.5

### Bug Fixes

- Fixed RenoDX install warnings from the manifest not showing (only Luma warnings were wired up).

### Manifest Updates

- Updated Halo: Campaign Evolved RenoDX install warning to clarify generic UE mod deployment and UE-Extended alternative.

---

## v2.2.4

### New

- **RTX HDR** ÔÇö enable NVIDIA's driver-level HDR for any game, right from the RenoDX ÔÜÖ dialog. Toggle it on, and RHI writes sensible defaults (your peak nits, neutral contrast/saturation) so it works immediately. Click "Configure RTX HDR" to fine-tune Peak Brightness, Contrast, Saturation, Middle Grey, and Debanding. Requires NVIDIA App with Overlay and Game Filters enabled.
- **Custom ReShade auto-redeploy** ÔÇö drop an updated ReShade DLL into the Custom folder and RHI automatically pushes it to every game using that DLL. No more manually copying files. Checked on Refresh and every 4 hours.
- **Start with Windows** ÔÇö new toggle in System Tray settings. RHI launches silently to the tray on boot ÔÇö ready to go without cluttering your desktop.

### Bug Fixes

- Fixed drag-and-drop mod installs on Unreal Engine games not writing the Engine.ini LUT fix.
- Fixed mods appearing "not installed" after switching a game between Steam and Game Pass.
- Full Refresh now cleans up stale install records for games that have been moved or uninstalled.

### Manifest Updates

- Fixed Arma Reforger launching wrong exe.
- Added engine version overrides: Hell is Us (UE 5.5.4), Halo: Campaign Evolved (UE 5.5.4).
- Added PCGW links: Halo: Campaign Evolved, Halo: The Master Chief Collection.
- Fixed Halo: Campaign Evolved install path for Game Pass (WinGDK).
- Removed Halo: Campaign Evolved from native HDR list.
- Added Xenoblade Chronicles 1/2/3 to Ryubing emulator bundle.
- Added RenoDX install warning for Halo: Campaign Evolved (use Discord version only).

---

## v2.2.3

### New

- **Engine version manifest overrides** (`engineHintOverrides`) ÔÇö allows specifying exact UE versions per game remotely (e.g. "Unreal Engine 4.27.2", "Unreal Engine 5.4.3"). Used for Game Pass games where auto-detection fails, and for accurate DOF Fix eligibility without needing `dofFixForceGames`.
- **ReLimiter Target FPS setting** ÔÇö new global FPS limit setting in the Component Settings card. Select a VRR-optimal preset or enter a custom value. Written to all relimiter.ini files on ReLimiter install and when clicking "Apply to All Games". Also available per-game in the ReLimiter cog dialog.

### Changes

- **UE4 UE-Extended games now default to SDR upgrade path** ÔÇö on fresh install, UE4 games get `Set_Path=1` (SDRÔåÆHDR conversion) instead of `Set_Path=0` (native HDR). Engine.ini HDR keys are also skipped by default for UE4. Users can still enable both manually via the RenoDX cog dialog. Existing installs are not touched.
- Per-game MFG Target FPS dropdown now uses VRR presets + "Custom..." dialog instead of listing every value from 60-500.
- Engine badge no longer appears clickable when a specific version is set (manifest override or auto-detection).

### Bug Fixes

- Fixed UE4 games showing Engine.ini HDR as "On" in cog dialog after fresh install despite HDR keys not being deployed.
- Fixed Update All overriding user's manual Engine.ini HDR toggle on UE4 games.
- Fixed `renodx-upgrade` addon being detected as the game's installed RenoDX mod (now excluded from mod scan alongside devkit, dlssfix, and dof fix).

### Manifest Updates

- Fixed BatmanÔäó: Arkham Knight incorrectly showing as DX9 (now DX11).
- Fixed Grim Dawn showing as 32-bit and deploying to wrong path (now 64-bit with `x64` subfolder override).
- Added DragonSword : Awakening to native HDR games.
- Fixed The Town of Light ReShade DLL override (needs `d3d11.dll` instead of default `dxgi.dll`).
- Fixed LEGO┬« Voyagers showing as 32-bit (now 64-bit).
- Added DragonSword : Awakening ultrawide fix URL override.
- Added Final Fantasy XIV Nexus URL override.
- Added empty `engineHintOverrides` field (ready for per-game population).
- Added Avowed engine version override (Unreal Engine 5.3.2).
- Added engine version overrides: Call of the Elder Gods (5.6.1), Denshattack! (5.6.1), Dragon Quest VII Reimagined (4.27.2), Ghostwire: Tokyo (4.27.2), Palworld (5.1.1), Shin Megami Tensei V: Vengeance (4.27.2).
- Added LEGO┬« BatmanÔäó: Legacy of the Dark Knight ultrawide fix URL override.
- Fixed Halo: Campaign Evolved install path (was pointing to DigitalExtras instead of `Meteorite\Binaries\Win64`).
- Added Halo: Campaign Evolved to native HDR games.
- Added Halo: Campaign Evolved Nexus URL override.
- Added `engineHintOverrides` manifest field for remote engine hint corrections.
- Added FINAL FANTASY XIV Online Nexus URL override (covers both detected name variants).

---

## v2.2.2

### Changes

- **DXVK on DX8/DX9 games now uses Vulkan layer mode for all variants** ÔÇö Development and Stable DXVK are no longer deployed as a proxy (`dxgi_dxvk.dll` + `[PROXY]` chain). All variants now deploy as `d3d9.dll` directly and use the global Vulkan ReShade layer, matching the approach previously exclusive to Lilium HDR. Existing proxy-mode installs continue to work ÔÇö reinstall or update DXVK to get the new behavior.
- Installer now gracefully shuts down RHI before updating, even when running in Admin Mode. Takes effect on the next update after this one (2.2.2 plants the listener; 2.2.3+ benefits).

---

## v2.2.1

### Ô£¿ Highlights

- **System Tray & Jump List** ÔÇö RHI can now minimize to the system tray when closed. Right-click the tray icon or pinned taskbar icon to instantly launch your recent games ÔÇö just like Steam. Double-click to restore the window. Optional, Off by default. Configure in Settings ÔåÆ System & Maintenance.
- **Automatic Background Updates** ÔÇö while running (especially in the tray), RHI re-checks all mod and app updates every 4 hours automatically. Manifest changes, new mods, version bumps ÔÇö everything stays current without restart.
- **Global FPS Limit** ÔÇö new driver-level frame rate cap in the NVIDIA Settings card. Pick a VRR-optimal preset (caps at VRR range for your refresh rate) or type any custom value. Installing ReLimiter or Display Commander now automatically disables it per-game to prevent conflicts.

### New

- Added global G-Sync Enable toggle to the NVIDIA Driver Settings card.
- Added global DMFG Defaults (Frame Count + Target FPS) ÔÇö set once, apply per-game with a single Dynamic mode click.
- RenoDX cog Compatibility Settings can now be extended remotely via manifest ÔÇö new toggles appear without app updates.
- Added RenoFX HDR Toolkit shader pack (Recommended category) ÔÇö SDR to HDR conversion, tone mapping, and color grading for games without a RenoDX mod.
- Added `renodx-upgrade` addon ÔÇö ITM and resource upgrades for HDR in DX9+ games. Use alongside the RenoFX shader.
- Window now reopens maximized if it was closed maximized.
- Shader pack dependencies are now automatically resolved at deploy time and during preset import ÔÇö selecting a pack that requires another pack (e.g. for shared `#include` files) will pull in the dependency automatically, even if it wasn't explicitly selected.

### Changes

- **Engine.ini auto-deploy for all UE games** ÔÇö `r.LUT.UpdateEveryFrame=1` is now written automatically for every Unreal Engine game on RenoDX install. Per-game On/Off toggle in the RenoDX cog ÔåÆ Engine.ini Settings section. Engine.ini HDR toggle moved into the same section.
- RenoDX Upgrade keys are now pre-populated on install for UE and Unity games, so Compatibility Settings appear in the cog dialog immediately (no first launch required). Values are left empty to let the addon fill in game-specific defaults.
- Uninstalling RenoDX on Unreal/Unity games now clears the `[renodx]` section from reshade.ini, preventing stale settings when switching between addon types.
- Per-game DMFG Dynamic settings now inherit from global defaults instead of writing "Off" that blocked inheritance.
- Rearranged NVIDIA Driver Settings card layout for better grouping.
- Improved RenoDX cog dialog layout ÔÇö controls properly aligned across all sections.

### Bug Fixes

- Fixed window position restore suppressing Windows taskbar auto-hide.
- Fixed Engine.ini HDR/LUT toggle resetting to On after Update All.
- Fixed Lilium HDR DXVK games showing "ReShade required" on ReLimiter/DC after Refresh despite Vulkan ReShade being active.
- Stub session logs from admin relaunch and second-instance exits are now auto-deleted.

### Manifest Updates

- Added Black Myth: Wukong and Denshattack! to `nativeHdrGames`.
- Added Crysis Remastered install path override (`Bin64`).
- Added Darkest Dungeon┬« 64-bit + install path override.
- Added Lords of the Fallen profile name override.
- Added ARC Raiders to wikiUnlinks (no RenoDX mod).
- Fixed Denshattack! incorrectly matching FF7 Remake mod.
- Updated RenoFX HDR Toolkit shader source to `clshortfuse/renofx` repo.
- Fixed Call of Duty Modern Warfare II split names missing ┬« symbols (sorting fix).
- Added Blender and Mp3tag to blacklist.
- Added `CrosireLegacy` shader pack dependency on `CrosireMaster` (fixes missing `Blending.fxh` for legacy shaders).

---

## v2.2.0

### New

- Added Custom Addons folder (`%LocalAppData%\RHI\Custom\Addons\`) ÔÇö place `.addon64`/`.addon32` files here and they appear in the Addon Manager and per-game Select Addons picker with on/off toggles. No download needed ÔÇö deployed directly from the folder.
- Added per-game G-Sync disable toggle in the driver settings panel ÔÇö force G-Sync off for specific games without changing the global setting.
- Added G-Sync On-Screen Indicator toggle in the DLSS/Streamline Settings card (below the DLSS indicator).
- Added Digital Vibrance control to the Global NVIDIA Driver Settings card ÔÇö adjust color saturation per-display with a slider (0-100). Saved values are automatically restored on app startup.
- Added global Power Mode setting to the Global NVIDIA Driver Settings card (next to VSync).
- Added "Create Missing Profiles" button ÔÇö creates NVIDIA driver profiles for all games that don't have one, ensuring global settings apply everywhere.
- Added Nexus mod summary on the RenoDX Info button for external-only Nexus games.
- Added "Dump LUT Shaders" toggle to the RenoDX cog dialog (Compatibility Settings section).
- Added `lumaNameOverrides` manifest field ÔÇö separate name mapping for Luma wiki matching (independent of RenoDX wiki overrides).

### Changes

- Moved ReBAR controls from the right column to the left column in the Global NVIDIA Driver Settings card (below VSync/Power Mode).
- Reorganized the right column: Digital Vibrance ÔåÆ Create Missing Profiles ÔåÆ Export/Import ÔåÆ Reset/Clear.
- Renamed "Purge Cache" to "Purge Staging Files" with an added description.
- Screenshot path placeholder text changed from "D:\Screenshots" to "Type or choose screenshot folder" for clarity.
- RenoDX cog: "Set_Path" renamed to "Upgrade Path", options renamed from Off/On to HDR/SDR.
- Import NVIDIA Driver Profiles now shows a progress dialog during import.

### Bug Fixes

- Fixed RenoDX update detection failing for addons using rolling release tags (`snapshot`/`latest`) when the file size didn't change between versions. Now uses full download + SHA256 hash comparison instead of HEAD Content-Length for these URLs.
- Fixed ReBAR Size Limit write corrupting the profile on some systems ÔÇö NvAPIWrapper's binary marshalling is broken (produces doubled values or garbage). All ReBAR Size Limit writes now use raw NVAPI with the correct BINARY struct layout (matching NVPI), with PowerShell helper as fallback.
- Fixed ReBAR Size Limit not reading back correctly on some systems ÔÇö now uses raw NVAPI read with binary type awareness, with NvAPIWrapper as fallback. Note: some driver/system combinations still cannot read externally-set values ÔÇö the in-memory cache covers values set within RHI.
- Fixed "Restore DLSS/Streamline Defaults" resetting Render Scale to Performance (50%) instead of clearing it ÔÇö the fallback in DeletePreset was writing 0x00 (Performance) instead of 0x03 (App Controlled) for render scale mode settings.
- Fixed "Apply to All Games" (Screenshots & Hotkeys) not writing overlay and screenshot hotkeys to reshade.ini files ÔÇö only the screenshot path was being applied.
- Fixed Update All re-deploying Engine.ini HDR settings on games where the user had explicitly disabled it via the RenoDX cog. The toggle state is now persisted in installed.json and respected by Update All.
- Fixed HDR Auto-Toggle setting always reverting to "On" on app restart ÔÇö the "Off" state was never persisted to settings.json.
- Fixed Engine.ini HDR combo in the RenoDX cog showing "On" after re-opening even when the user had set it to "Off" ÔÇö now reads from the persisted record instead of checking the file on disk.
- Fixed DLSS Fix INI (`[RENODX-DLSSFIX]` section) not being written to reshade.ini on some systems when toggling the addon ÔÇö added fallback to trusted path cache for DLSS/Streamline path resolution.
- Fixed "Browse" button for launch executable opening System32 instead of the game folder ÔÇö forward slashes in Ubisoft Connect paths weren't compatible with the Win32 file dialog.

### Manifest Updates

- Removed incorrect Luma install warning from "Borderlands GOTY Enhanced" (only applies to Borderlands 2 and The Pre-Sequel).
- Added split games: Call of Duty Modern Warfare II (MP / Campaign), DOOM Eternal (main / Sandbox).
- Added ultrawide fix URL for Echoes of Aincrad.

---

## v2.1.9

### Changes

- Swapped order of "Defaults" and "Batch Deploy" in the DLSS/Streamline Settings card ÔÇö configure first, then deploy.
- Batch Deploy dialog now pre-populates version and preset dropdowns with your saved defaults.
- Luma info dialog now displays feature notes as bullet-point lists instead of a wall of text.
- Settings page global NVIDIA driver settings now refresh automatically after a Refresh without needing to navigate away.

### Bug Fixes

- Fixed ReShade uninstall button doing nothing on GAC symlink games (Terraria) ÔÇö the uninstall path now handles games without an aux record. Shows admin warning if not elevated instead of silently failing.
- Fixed sidebar green update dots not appearing for users who upgraded from pre-2.1.7 ÔÇö version cache could be empty while addons were installed.
- Fixed false green update dots appearing on games with no RenoDX installed (e.g. The Surge with manual DXVK).
- Fixed "Apply to All Games" button in the Screenshots & Hotkeys section not applying screenshot path or ReShade hotkeys ÔÇö it was wired to the wrong handler. Left button now applies screenshots + hotkeys, right button applies peak nits only.
- Fixed Luma wiki scraper not finding any games ÔÇö the wiki moved to a new URL, silently returning 0 mods.

---

## v2.1.8

### New

- **32-bit ReLimiter support** ÔÇö ReLimiter now works on 32-bit games. Automatically downloads and deploys the correct version based on game bitness.

### Changes

- Renamed "Latest Recommended" to "NVIDIA Recommended" in DLSS preset dropdowns (SR, RR, FG).

### Bug Fixes

- Fixed Update All button staying purple after completion ÔÇö now properly notifies the button to re-evaluate and resets Nexus update baselines.
- Fixed Ryubing (emulator) not showing the green update indicator in the sidebar when updates are available.
- Fixed Peak Nits preset checkboxes reverting on restart when returning to all-checked state.
- Added confirmation dialogs to the Mass INI Deployment buttons (reshade.ini, relimiter.ini, DC.ini, OptiScaler.ini) ÔÇö shows target count before proceeding.

---

## v2.1.7

### New

- **Custom ReShade picker** ÔÇö place multiple custom ReShade DLLs in the Custom folder (name them anything). When you select "Custom" as the RS Channel, a picker dialog lets you choose which one to deploy. Selection is saved per-game. Vulkan games still share a single global layer.

### Changes

- **Grid View removed** ÔÇö RHI now has two views: Detail and Simple. Cleaner, faster.
- **Startup faster** ÔÇö UI appears in ~700ms (down from ~1.2s). ReShade and RenoDX version numbers now show instantly instead of waiting for the background scan.
- **Views button** is now a simple toggle (no dropdown menu).

### Bug Fixes

- Fixed ReShade deploying as `dxgi.dll` on DX8 games ÔÇö now correctly deploys as `d3d8.dll`.
- Fixed HDR auto-toggle disabling HDR immediately when launching games via wrappers like SKSE or MO2 ÔÇö now monitors the actual game process.
- Fixed manifest 32/64-bit overrides not applying until background scan ÔÇö games like Trackmania now show correct bitness immediately.

### Manifest Updates

- Ryubing: updated ReShade guidance ÔÇö now uses Nightly ReShade via Vulkan layer (no longer requires RenoVK custom build).

---

## v2.1.6

### New

- **HDR monitor selection** ÔÇö ÔÜÖ button next to HDR Auto-Toggle opens a dialog showing all detected displays. Tick which monitors should have HDR enabled on game launch. Leave all unchecked for primary display only (previous behaviour). Non-HDR displays shown greyed out.
- **Peak Nits preset control** ÔÇö ÔÜÖ button next to the Peak Nits input opens a configuration dialog. Choose Off/On to enable or disable global nits auto-deploy entirely. Tick which presets (1, 2, 3) should receive the global value ÔÇö unchecked presets keep their existing per-preset values untouched.

### Changes

- **ReShadePreset.ini no longer auto-deployed** ÔÇö previously copied on every ReShade install, update, and INI merge. Now only deployed when you explicitly click "Deploy ReShadePreset.ini" in the RS cog dialog.
- Shaders & Addons settings card: ToggleSwitches replaced with side-by-side ComboBoxes for a more compact layout.

### Bug Fixes

- Fixed OptiScaler not updating to newer versions when a cached version already existed ÔÇö the staging guard was preventing re-download even when an update was detected.
- Fixed Peak Nits overwriting custom per-preset ToneMapPeakNits values ÔÇö users who had different nits per preset were losing their custom values on every INI deploy.

---

## v2.1.5

### New

- **HDR Auto-Toggle** ÔÇö automatically enables Windows HDR when launching a game through RHI and disables it when the game exits. Useful if you run your desktop in SDR and are tired of manually enabling HDR every time you game. Global setting (Off/On) in the Display section. Per-game "HDR" button next to Launch ÔÇö purple when active, grey when inactive. Click to flip. Monitors the game process and disables HDR on exit for both direct exe and Steam/Epic protocol launches.
- **Running game indicator** ÔÇö sidebar highlights green when a game launched through RHI is currently running. Returns to normal when the game exits.
- **DLSS / Streamline Auto-Update** ÔÇö new toggles in the DLSS / Streamline Settings card. When enabled, games that are on the previous latest version are automatically swapped to the new latest when a manifest update arrives. Games on manually chosen older versions are left alone. Set and forget.
- **Peak Brightness (nits) global setting** ÔÇö set your monitor's peak nits once and it's automatically written to all reshade.ini files on every deploy. Auto-detect button reads your display hardware. Persists across ReShade installs and mass deploys.
- **Drop Helper toggle** ÔÇö new Off/On combo in the Admin Mode section. Disables the drop helper overlay window for users who don't need Discord drag-and-drop in admin mode.
- **Per-game RenoDX INI overrides** ÔÇö manifest can now specify `[renodx]` INI keys (like Upgrade settings) per game. Applied automatically on RenoDX install. Existing user values are preserved ÔÇö only missing keys are written. Force-applied on reshade.ini redeploy.

### Changes

- **Settings page reorganized** ÔÇö reduced from 11 cards to 9, clearly labelled with bold section headers. All DLSS/Streamline tools unified in one card. Global NVIDIA driver settings get their own dedicated card. ReLimiter and OptiScaler side by side. Shaders and addon watch folder grouped. Update checks and mass INI deployment merged.
- **Global ReShade channel removed** ÔÇö ReShade always defaults to Stable. Per-game overrides (Nightly, Custom, Legacy) remain available. Users who had Nightly globally will have it migrated to per-game overrides automatically.
- **Detail panel header split** ÔÇö game actions (Hide, Favourite, Config, Browse) now in their own bordered box, visually distinct from the Launch/links area.
- Engine badge locked for manifest-forced DOF Fix games (e.g. Clair Obscur, Avowed) ÔÇö can't be toggled off.
- Toggling engine badge OFF now uninstalls DOF Fix addon if it was installed.
- Removed redundant generic mod badges (UE Extended, Generic UE, Generic Unity) from detail panel.
- ListView selection chrome removed for cleaner sidebar visuals.
- Per-game screenshot subfolders converted from toggle switch to compact combo box.

### Bug Fixes

- Fixed ReShade falsely showing "Update Available" on games with OptiScaler installed ÔÇö the update check was comparing OptiScaler's dxgi.dll size against ReShade staging.
- Fixed RenoDX update dot showing on games that never had RenoDX installed ÔÇö snapshot URL content-length changes were flagging updates for uninstalled mods.
- Fixed Luma games falsely showing "Update Available" on launch ÔÇö stale status from a previous session was not being cleared.

---

## v2.1.4

### Bug Fixes

- Fixed ToneMapPeakNits key written with wrong casing (was `toneMapPeakNits`, should be `ToneMapPeakNits`).
- Fixed Max Nits display showing decimal values when reading from INI ÔÇö now truncates to whole number.

---

## v2.1.3

### New

- **Set Maximum Nits** ÔÇö new section in the RenoDX cog. "Auto" button reads your monitor's peak brightness (picks the brightest for multi-display setups). Or type a custom value and press Enter. Writes `toneMapPeakNits` to all RenoDX presets ÔÇö creates the section if it doesn't exist yet.

### Bug Fixes

- Fixed DXVK (Lilium HDR) not updating to new versions even when an update was detected ÔÇö staging skip guard prevented re-download when existing files were cached.

---

## v2.1.2

### Bug Fixes

- Fixed NVIDIA profile name overrides not taking effect on first launch (profile lookup was cached before manifest loaded, causing presets to be applied to the wrong profile ÔÇö e.g. Dead Space original instead of Remake).

### Improvements

- Update All now shows a progress dialog indicating which component is being updated (ReShade, RenoDX, ReLimiter, etc.) so it no longer appears to hang during the process.

### Manifest Updates

- Added System Shock Engine.ini path override (`%USERPROFILE%\Saved Games\Nightdive Studios\...`).

---

## v2.1.1

### Improvements

- Engine.ini path overrides now support full directory paths (with environment variables like `%USERPROFILE%`). Fixes games that store config in non-standard locations like `Saved Games`.

### Manifest Updates

- Added Ghostwire: Tokyo Engine.ini path override (`%USERPROFILE%\Saved Games\TangoGameworks\...`).

---

## v2.1.0

### New

- **Purge Cache** ÔÇö new button in the Data & Custom Files section. Clears cached DLSS, Streamline, and download files to free disk space. Shaders are preserved, installed RenoDX addons are kept, and version metadata is retained so update checks still work correctly. Shows a summary of files deleted and space freed.
- **Engine Version Override** ÔÇö click the Unreal Engine badge to toggle UE 5.0ÔÇô5.6 when version detection fails (common on Game Pass games). Enables DOF Fix eligibility. Persists across restarts.

### Improvements

- Added per-game NVIDIA profile name overrides via manifest ÔÇö fixes games where automatic profile matching picks the wrong profile (e.g. Dead Space original vs remake).
- Added Lazorr as creator of the Universal UE DOF Fix in the About page.

### Manifest Updates

- Added wiki name overrides for the Trails series (Cold Steel III/IV, Daybreak 1/2, Sky 1st Chapter, Beyond the Horizon, From Zero/To Azure).
- Added `profileNameOverrides` entry for Dead Space (routes to the Remake profile instead of the original).
- Added Clair Obscur: Expedition 33 to DOF Fix force list (Game Pass UE version undetectable).

---

## v2.0.9

### Major Fix

- **Patch Notes scrollbar no longer overlaps text** ÔÇö the long-standing visual issue where the scrollbar would cover the right edge of patch notes content has finally been resolved. Every word is now fully visible. This changes everything.

### Bug Fixes

- Fixed drag-and-drop of ReShade presets (.ini files) not working in Admin Mode ÔÇö was incorrectly treated as an archive.
- Fixed Shader Pre-Compile "Off" setting not reflecting in RHI when set externally (NVIDIA App or NVPI). Was incorrectly showing as "Low (Default)".
- Fixed "UE-Extended Settings" heading not showing in the RenoDX cog for games that use UE-Extended by default.

### Improvements

- Added tooltips to UE-Extended, Engine.ini HDR, and Preset Export/Import controls in the RenoDX cog.
- Added horizontal separators between sections in the RenoDX cog dialog for clearer visual separation.

---

## v2.0.8

### New

- **DOF Fix Component** ÔÇö new component row for Unreal Engine 5.0ÔÇô5.6 games in the Optional section. Fixes the common depth-of-field stepping/tiling artifacts. Click Install to deploy. Participates in Update All.

### Improvements

- **ReShade ÔÜÖ´©Å Settings Dialog**
  - Deploy ReShade.ini ÔÇö merges the RHI template into the game folder
  - Deploy ReShadePreset.ini ÔÇö copies your preset file to the game folder
  - Open ReShade.ini ÔÇö opens the game's ini in your default editor
  - Open ReShade.log ÔÇö opens the game's log in your default editor
  - Copy ReShade.log to clipboard ÔÇö pastes as a file named `ReShade.log` on Discord (not `message.txt`)

- **RenoDX ÔÜÖ´©Å Settings Dialog**
  - UE-Extended toggle with Engine.ini HDR on/off (appears instantly when toggling UE-Extended on)
  - Compatibility Settings ÔÇö edit format upgrade overrides (`Upgrade_*` keys) directly with combo boxes. No more manually editing reshade.ini. Options: Off / Output size / Output ratio / Any size.
  - RenoDX Presets ÔÇö Export saves all your presets to a file and copies to clipboard for sharing. Import restores presets from the file back into reshade.ini.

- **ReLimiter ÔÜÖ´©Å Settings Dialog**
  - Deploy relimiter.ini
  - Open ReLimiter log ÔÇö finds the correct `relimiter_*.log` file for the game
  - Copy ReLimiter log to clipboard ÔÇö pastes as a file with the correct name on Discord
  - Per-game DLSS Hooks toggle ÔÇö override the global DLSS Hooks setting for individual games (disable if causing crashes in a specific title)

---

## v2.0.7

### Improvements

- Added ReLimiter DLSS Hooks toggle ÔÇö shows DLSS info on the OSD. Can be disabled if causing crashes in some games.
- Added Clear Shader Cache button in Nvidia Settings ÔÇö deletes NVIDIA DXCache and GLCache to fix shader corruption or stuttering.

### Bug Fixes

- Fixed DLSS preset and render scale "Default"/"Off" selections blocking global profile inheritance. All driver profile settings now properly clear from the per-game profile when set to their default, allowing global settings to apply.
- Fixed DLSS Preset Override setting not clearing when preset is set to Default.

---

## v2.0.6

### New

- **Drag-and-drop in Admin Mode** ÔÇö a non-elevated drop helper runs alongside RHI when in Admin Mode, allowing drag-and-drop from Discord and Explorer to work despite Windows elevation restrictions. Drop target is over the RHI logo (top-left).

---

## v2.0.5

### Bug Fixes

- Fixed Lilium HDR deploying wrong conf files (DX11 instead of DX9) and showing too many preset options for DX9 games.
- Fixed DXVK updates not detecting or correctly applying Lilium HDR variant (was using Development instead).
- Fixed DXVK update not rewriting dxvk.conf alongside DLLs.
- Fixed Vulkan footprint file left behind after uninstalling DXVK on DX10/DX11 games.

---

## v2.0.4

### Improvements

- DLSS preset dropdowns now show model technology (TF1, TF2, CNN) next to each preset name.
- Shader Pre-Compile now has an "Off" option.
- Manifest-added presets now sort alphabetically instead of appending at the end.

---

## v2.0.3

### New

- **Global VSync setting** ÔÇö set VSync mode globally from the Nvidia Settings section on the Settings page. Per-game VSync dropdown now shows a "Global (X)" option to inherit from the global setting.

---

## v2.0.2

### New

- **Check For Updates button** ÔÇö new button in Settings. Fetches the latest manifest, checks all components for updates (bypassing the 4-hour cooldown), and checks for app updates. Progress dialog shown while working.
- **Full Refresh dialogs** ÔÇö confirmation warning before starting (explains what it does, advises normal Refresh first) and progress dialog showing phases while working.
- **Copy Logs button** ÔÇö archives all session logs into a zip and copies to clipboard for easy pasting into Discord.

### Improvements

- DMFG Target FPS labels reformatted: `324 FPS (360Hz VRR Cap)` style for better readability.
- Settings page reorganized: Add Game + Check For Updates top row; Full Refresh + Admin Mode in their own section near the bottom; Downloads folder button added to Data section.
- Full Refresh no longer includes update checking (moved exclusively to Check For Updates).

### Bug Fixes

- Fixed DLSS preset overrides not applying in some games (e.g. Avatar: Frontiers of Pandora).
- Fixed false DXVK and ReShade update indicators on Vulkan/Lilium HDR games.

---

## v2.0.1

### New

- **"Latest Recommended" preset option** ÔÇö selectable from the DLSS preset dropdowns (SR, RR, FG). Overrides the game's developer-defined presets with NVIDIA's recommended per-resolution model selection. Works per-game, via Quick Apply, and in Batch Deploy.
- Hidden games are now excluded from the Batch Deploy list.

### Bug Fixes

- Fixed ReBAR settings not applying for some games where the NVIDIA profile name didn't exactly match the game name in RHI (e.g. Anno 117, Battlefield 6).
- Fixed NVIDIA profile creation failing for games with commas in their name (e.g. Warhammer 40,000: Rogue Trader).

### Manifest Updates

- Anno 117: Pax Romana ÔÇö install path override (Bin\Win64).
- FINAL FANTASY VII REMAKE INTERGRADE ÔÇö added to lumaRenodxCompat + snapshotOverride for Shortfuse's build.

---

## v2.0.0

### New Features

- **Nvidia Profile Overrides** ÔÇö New dedicated panel for per-game NVIDIA driver profile settings:
  - **DLSS / Streamline row** ÔÇö Version, Preset, and Render Scale management for SR, RR, FG, and Streamline. Quick Apply button stamps your configured defaults onto any game in one click (downloads on-demand). Restore DLSS/SL reverts DLLs and resets presets.
  - **Driver Settings row** ÔÇö VSync (Mode, Tear Control, Low Latency), Smooth Motion (Enable, APIs, Flip Pacing), Power Mode, ReBAR (Enable, Mode, Size Limit). All per-game via NVIDIA driver profiles. Requires admin.
- **Admin Mode** ÔÇö Task Scheduler-based persistent elevation (Off/On in Settings). When enabled, RHI silently relaunches elevated on startup ÔÇö no per-operation UAC prompts. Required for ReBAR, Low Latency Ultra, and Smooth Motion writes. Driver settings row greyed out when not elevated.
- **Multi Frame Generation** ÔÇö "Multi Frame Gen" button in the FG column opens a per-game dialog to configure MFG Mode (Fixed/Dynamic), frame count multiplier (2x-6x), and dynamic target frame rate (with VRR cap presets for common monitor refresh rates). RTX 50 Series only (driver 572.16+ for MFG, 595.97+ for DMFG).
- **DLSS & Streamline Defaults** ÔÇö Configure preferred default versions, presets, and render scales in Settings. One-click Quick Apply per game. 4-column configuration dialog.
- **Global Nvidia Settings** ÔÇö Shader Cache Size, Shader Pre-Compile, G-Sync Mode, Preferred Refresh Rate, Global ReBAR (On/Off + Size), DLSS On-Screen Indicator. All write to the global driver profile.
- **Profile Export/Import** ÔÇö Back up all per-game NVIDIA profile settings to JSON. Restore after driver updates ÔÇö recreates profiles, exe associations, and all custom settings in one click. Includes global settings.
- **Global ReBAR** ÔÇö On/Off and Size controls in Global Nvidia Settings. Per-game Enable dropdown shows "Global (On/Off)" when set globally.
- **DLSS Driver Override Detection** ÔÇö Detects when NVIDIA App has "Latest DLL" or "Use recommended preset" active. Greys out affected dropdowns with a warning. Quick Apply respects these.
- **Restore Profile Defaults** ÔÇö Button in the driver settings row resets the game's NVIDIA driver profile to factory defaults.
- **Driver Version Display** ÔÇö Nvidia Profile Overrides header shows installed driver version.
- **UE Version Detection** ÔÇö Engine badge shows exact Unreal Engine version (e.g. "Unreal Engine 5.4.3") when detectable.
- **Manifest-driven Shader Packs** ÔÇö Add, disable, or modify shader packs from the remote manifest without app updates.
- **Manifest-driven DLSS Presets** ÔÇö Preset options updated server-side when NVIDIA introduces new ones.
- **Manifest-driven Addon Packs** ÔÇö Addon entries can be added, modified, or disabled from the manifest.
- **Manifest-driven Component URLs** ÔÇö Base download URLs overridable from the manifest.
- **Lilium HDR DXVK ÔÇö Vulkan layer mode** ÔÇö DX9 games with Lilium HDR DXVK now deploy DXVK as `d3d9.dll` directly with Vulkan layer ReShade, enabling SM5 HDR shaders. Restores local ReShade on uninstall. Per-game HDR preset selector (Safest ÔåÆ Experimental) controls how aggressively render targets are upgraded ÔÇö 6 presets for DX9, 7 for DX10/DX11.
- **Reset All Game Profiles** ÔÇö Button in Global Nvidia Settings resets ALL per-game NVIDIA profile overrides AND global base profile settings to factory defaults with progress feedback.

### Improvements

- Detail panel reorganized into 4 sections: Components, Game Overrides, Nvidia Profile Overrides, Management.
- Simple View (formerly "Compact") now has 3 pages: Components, Game Overrides, Nvidia Profile + Management.
- Fresh installs default to Simple View.
- Rebranded from "ReShade HDR Installer" to "RHI".
- DXVK per-game combo shows Off/Development/Stable/Lilium HDR directly (no global indirection).
- DXVK version text is now a clickable link to the variant's GitHub releases page.
- DLSS/Streamline section hidden for games without DLSS or Streamline files. Driver settings row always visible.
- ReBAR Mode and Size show effective values directly (no "Global" option ÔÇö display inherits from global when no override set).
- DLL naming override available in Luma mode.
- Batch Deploy allows all games to be selected ÔÇö v1.x SR and Streamline are skipped per-component during deployment. FG v1.x can be upgraded freely.
- NVIDIA profile lookup cached per-game for the session (~1s freeze on unmatched games eliminated).
- Vulkan ReShade layer install shows actionable dialog when admin privileges are missing.
- Bitness override change auto-uninstalls all components for clean reinstall.
- All NVIDIA profile dropdowns have sub-labels and tooltips.
- Config button opens the exact Engine.ini folder.

### Bug Fixes

- Fixed Streamline "Custom" selection reverting to a version number after panel rebuild.
- Fixed Luma and RE Framework update status not persisting over restart.
- Fixed DXVK extraction using a random temp folder each time (Windows Defender flagging).
- Fixed overlay and screenshot hotkeys having Ctrl and Shift swapped.
- Fixed wiki exclusion showing "Download from Discord" instead of "No RenoDX mod available".
- Fixed Power Mode showing "Adaptive" after profile restore instead of "Optimal Performance".

### Manifest Updates

- Borderlands 4, Gothic 1 Remake, High on Life 2, Crisol, ROMEO IS A DEAD MAN, S.T.A.L.K.E.R. 2, SILENT HILL f, Split Fiction, Star Trek: Voyager, WUCHANG: Fallen Feathers ÔÇö native HDR.
- Added `dlssSkipGames` for games without DLSS ÔÇö reduces background scan time.
- Stellar Blade ÔÇö install path override.
- Outward ÔÇö split into Outward (original) + Outward Definitive Edition.
- Gothic 1 Remake ÔÇö game note added.
- KINGDOM HEARTS III ÔÇö Unreal Engine override.
- Updated native HDR game notes to reflect auto Engine.ini deployment.
- LEGO Harry Potter Collection ÔÇö split into Years 1-4 and Years 5-7.

---

## v1.9.9

### New Features

- **UE-Extended overhaul** ÔÇö The UE-Extended toggle now appears on ALL Unreal Engine games (including those with named mods). When installing UE-Extended, RHI automatically configures reshade.ini for native HDR (Set_Path=0, all Upgrade keys off) and deploys Engine.ini HDR settings to the game's AppData config folder. A new "Config" button in the detail panel opens the config folder directly. If the game has an in-game HDR setting, enable that too. RHI only adds missing keys to reshade.ini ÔÇö if you previously configured SDR upgrade values (e.g. Upgrade Path on, format upgrades on), those won't be overwritten automatically. To reset: delete reshade.ini from the game folder and click the INI deploy button next to ReShade to generate a fresh one. For users who prefer upgrading SDR instead of native HDR: set "Upgrade Path" to On in RenoDX Advanced Settings and remove the HDR lines from Engine.ini via the Config button.
- **DLSS Render Scale Override** ÔÇö Force a custom DLSS render resolution per-game for both SR and Ray Reconstruction. Choose from named presets (DLAA, Quality, Performance, etc.) or enter any custom percentage. Not compatible with OptiScaler.
- **DLSS Fix auto-configuration (beta)** ÔÇö When the DLSS Fix addon is deployed, reshade.ini is automatically configured with the correct DLSSPath and StreamlinePath for each game. Only activates for games with Streamline detected. Settings are removed when DLSS Fix is uninstalled.
- **Ryubing emulator support** ÔÇö Drag `Ryujinx.exe` into RHI to add Ryubing. Install RenoDX downloads all 9 Souperman9 Switch game addons in one click. Addons self-detect which game is running ÔÇö no swapping needed. Requires RenoVK in the Custom ReShade folder.
- **Luma + RenoDX coexistence** ÔÇö Games in the manifest `lumaRenodxCompat` list can now have both Luma and RenoDX installed simultaneously. Useful for Luma mods that only add DLSS/upscaling but not HDR.

### Improvements

- Shader pack list reorganized: Recommended slimmed to 4 HDR-relevant packs (crosire master, PumboAutoHDR, clshortfuse, MaxG2D Simple HDR), all others moved to Extra, sorted alphabetically. Added crosire reshade-shaders (legacy) pack.
- About page overhauled: updated description, added RE Framework and OptiScaler credits/links, removed outdated disclaimer.
- Uninstall cleanup: ReShade now removes reshade.ini/log files, ReLimiter removes relimiter.ini/log/csv files.
- Anti-cheat warning updated to include DLSS/Streamline modifications.

### Bug Fixes

- Fixed Batch DLSS Deploy hanging indefinitely on stalled downloads (120s timeout added).
- Fixed DLSS On-Screen Indicator toggle causing an infinite UAC prompt loop on cancel.
- Fixed DLSS presets and render scale not applying for profiles matched by title/fuzzy match ÔÇö game exe now auto-registered in NVIDIA profile.
- Fixed OptiScaler uninstall deleting the game's DLSS DLLs when no .original backup existed.
- Fixed RenoDX Info button not showing wiki status badge for games with wiki entries but no notes text.
- Fixed manually added games not detecting DC, OptiScaler, DXVK, or DLSS/Streamline until Refresh.
- Fixed managed addons (DLSS Fix, DevKit) never auto-updating due to rolling "snapshot" tag.
- Fixed INI merge button not applying [renodx] UE-Extended section when mod was already installed.
- Fixed Luma install warning popping up during Update All.

### Manifest Updates

- Added 17+ games to native HDR list (Black Myth Wukong, Avowed, Lies of P, Returnal, Gothic 1 Remake, Star Trek: Voyager, etc.)
- Added Ultra+ HDR toggle notes for 13 games.
- Added engineIniPathOverrides for games with non-standard AppData folders.
- Persona 5 Royal ÔÇö added to lumaRenodxCompat, removed from wikiUnlinks.
- Updated RE Framework game notes ÔÇö removed external download links (now bundled).
- Removed 'set Upgrade Path to Off' from all game notes ÔÇö RHI handles this automatically.
- Neverness To Everness ÔÇö dllNameOverride (ReShade as d3d12.dll).
- Outward ÔÇö split into Outward (original) + Outward Definitive Edition.

---

## v1.9.8

### New Features

- **Batch DLSS & Streamline Deploy** ÔÇö New "Batch Deploy" button in Settings lets you update DLSS SR, RR, FG, and Streamline across multiple games at once. Select games from a checklist, pick versions from dropdowns, and deploy. Originals are backed up automatically. Games already at the selected version or with v1.x DLLs are skipped. Also supports batch DLSS preset selection (SR/RR/FG) and auto-creates NVIDIA driver profiles for games that don't have one. Includes a "Restore" button to revert selected games to their original DLLs and reset presets to default.
- **DLSS On-Screen Indicator Toggle** ÔÇö New setting to enable/disable the DLSS text overlay that NVIDIA shows in the corner of games. Global system setting, requires admin (UAC prompt). Found in the Mass DLSS & Streamline section of Settings.
- **Custom ReShade Channel** ÔÇö New "Custom" option in the RS Channel dropdown. Drop your own ReShade64.dll/ReShade32.dll into the Custom\ReShade folder and select "Custom" per-game to deploy them. Games on Custom are excluded from automatic ReShade updates. Version is read from the DLL's file metadata. Useful for deploying RenoVK or other custom ReShade builds.
- **Unified Custom Folder** ÔÇö DLSS-Custom and Streamline-Custom folders consolidated into `%LocalAppData%\RHI\Custom\` with subfolders: `DLSS\`, `Streamline\`, and `ReShade\`. Existing files are migrated automatically on first launch.
- **Install Warnings** ÔÇö Per-game, per-component install warnings driven from the manifest. When a game has a known requirement (e.g. FF7R needs DX11 mode for Luma), a dialog pops up before install with the warning. User can Continue or Cancel.
- **Message of the Day** ÔÇö RHI can now display announcements to all users on launch. Messages are fetched from GitHub (`motd.md`) and shown once per unique message (tracked by content hash). When the file is empty or unchanged, nothing is shown.
- **Launch Arguments** ÔÇö Set per-game launch arguments from the Overrides panel (next to the launch executable path). Arguments are passed to the game on launch. Steam games use `-applaunch` for reliable argument passing while preserving overlay and playtime tracking.
- **Epic Games Store Launch** ÔÇö Epic games now launch through the Epic protocol URL instead of direct exe, fixing "please launch through the Epic launcher" errors for EOS-protected games. Works silently without bringing the launcher to the foreground.
- **Multi-Game Split** ÔÇö Games that contain multiple titles in one folder (e.g. Mass Effect Legendary Edition) can now be split into separate entries via the manifest. Each sub-game gets its own card with independent ReShade, DLSS, and mod management.

### Bug Fixes

- Fixed RenoDX mod install incorrectly warning about replacing the DLSS Fix global addon (`renodx-dlssfix.addon64`). Global addons are no longer treated as game-specific mods during install.
- DLSS and Streamline version dropdowns are now disabled for games with v1.x DLLs (e.g. Witcher 3). These legacy versions are not compatible with the newer versions available in the manager.
- Full Refresh now clears DLSS scan caches, ensuring newly added DLLs (e.g. game update adds Ray Reconstruction) are detected.
- Fixed DLSS presets not applying for games with custom NVIDIA driver profiles (e.g. GreedFall 2). Custom profiles named after the exe are now matched correctly.
- Fixed DLSS scan cache file contention when the cache phase and background scan write simultaneously. Both `dlss_trusted_paths.json` and `dlss_scan_cache.json` now use a shared lock to prevent concurrent write failures.
- Fixed DLSS detection scanning into sibling game folders for GOG Galaxy installs (e.g. BioShock Infinite falsely showing Fort Solis's DLSS). The search root guard now recognizes `Games` as a library folder.
- Fixed DXVK Update All overwriting per-game Lilium HDR variant with the global Development/Stable variant. Update All now respects per-game DXVK variant overrides.
- Fixed Guide button in the Help menu pointing to an old URL.
- Fixed Nexus update indicator persisting after re-downloading the mod. Clicking the "Update RenoDX" button now resets the baseline immediately ÔÇö the click is treated as acknowledgement that the user is aware of the update. Note: Nexus update detection uses the mod page's last-modified timestamp, which can change for page edits (not just new versions). This may occasionally flag updates when only the description was changed.
- Fixed Update All button not highlighting purple after Refresh when games have pending updates. The button state was only recalculated during the background update check, which could be skipped by the 4-hour cooldown.
- Fixed DLSS presets showing "Default" on app launch instead of the actual configured preset (e.g. "B" for Frame Generation). The preset service initialization was racing with the panel build ÔÇö navigating away and back would show the correct value.

### UI Changes

- RenoDX wiki status badge (Working, In Progress, etc.) moved from the main detail panel into the RenoDX Info button dialog. Cleaner main view, status still accessible.
- Settings page reorganized: "Crash & Error Logs" renamed to "Data & Custom Files" with AppData/Custom folder buttons. Global Update Checks replaced with a compact "Update Inclusion" button + summary line (matching the per-game overrides style). Custom Shaders and Shader Cache merged into one row. Addon Watch Folder moved up next to Global Update Checks. Mass INI Deployment and Mass Preset Install combined into a single "Mass Deployment" section. Verbose Logging and Skip Update Check toggles removed.
- OptiScaler Settings compacted: DLSS input toggle replaced with a Yes/No dropdown next to GPU Type. Hotkey and Apply button placed side by side. Compatibility list link removed.
- Tooltips added to all interactive controls in the detail panel (overrides, management buttons, launch settings, DLSS section, author badges).

### Luma Changes

- **Luma Drag-Drop & File Watcher Install** ÔÇö Drag a Luma mod archive (zip or 7z) from Explorer, Discord, or Nexus onto a game card to install it. The file watcher also auto-detects Luma archives in your Downloads folder and prompts you to pick a game. Handles all variants: full packages with custom ReShade, addon-only mods, and shader-only mods. If the archive doesn't include ReShade, RHI deploys its own cached version automatically. Archives with multiple game folders (e.g. BO3 with Alternatives/Debug/Optional folders) automatically filter out non-game folders and prompt you to pick the correct one if needed.
- Luma toggle button moved to the right side of the Components header. Dynamic info text now explains whether the game is auto-configured for Luma or manually toggleable. Toggle text shortened to "Luma ON" / "Luma OFF".
- Luma installs now deploy shaders using the same global/per-game shader selection as normal ReShade installs (previously hardcoded to Lilium only).
- Fixed Luma uninstall leaving behind a `reshade-shaders-original` folder. The shader folder is now properly deleted instead of renamed.
- Fixed Luma installs not applying screenshot save path, overlay hotkey, or screenshot hotkey to reshade.ini. These settings are now passed through correctly, matching normal ReShade installs.
- RS Channel dropdown is now disabled when Luma mode is active (Luma bundles its own ReShade).

### Manifest Updates

- FINAL FANTASY VII REMAKE INTERGRADE ÔÇö Luma install warning (DX11 mode required).
- SOULCALIBUR VI ÔÇö install path override to `SoulcaliburVI\Binaries\Win64`, Unreal Engine override.
- Gothic II: Gold Classic ÔÇö Nexus Mods game page link.
- Far Cry┬« 2: Fortune's Edition ÔÇö PCGW URL override for GOG version.
- Mass EffectÔäó Legendary Edition ÔÇö split into 3 separate entries (ME1, ME2, ME3) for independent mod management.
- DRAGON QUEST┬« XI S: Echoes of an Elusive AgeÔäó ÔÇö wiki name override for Epic version (was using Generic UE instead of the specific DQ addon).

---

## v1.9.7

### New Features

- **DLSS & Streamline Manager** ÔÇö Full version management for NVIDIA DLSS and Streamline DLLs. Swap DLSS Super Resolution, Ray Reconstruction, and Frame Generation independently to any version. Update or downgrade Streamline as a set. All versions are downloaded on-demand and cached locally. Backups are created automatically with `.original` extension ÔÇö restore anytime with one click. Smart detection finds DLLs regardless of folder structure (Unreal Engine, Unity, CryEngine, WindowsApps). Correctly distinguishes game DLSS files from OptiScaler's bridging copies. Available in Detail and Compact views (not Grid view).
- **DLSS Preset Control** ÔÇö Change DLSS presets per-game directly from RHI. Set SR presets (J, K, L, M), RR presets (D, E), and FG presets (A, B) without needing NVIDIA Profile Inspector. Changes apply instantly to the NVIDIA driver profile.
- **Custom DLSS/Streamline Files** ÔÇö Drop your own DLLs into the Custom folders and select "Custom" from the version dropdown to deploy them.

### Bug Fixes

- Fixed Vulkan ReShade update exclusion not propagating to all Vulkan games. Since all Vulkan games share the same global layer DLL, excluding one now correctly excludes all of them from ReShade updates.
- Fixed update indicators (purple buttons, green dots) disappearing after Refresh. Update statuses are now correctly preserved across manual refreshes.
- Fixed compact view becoming unresponsive when rapidly navigating with arrow keys. Added 150ms selection debounce to prevent UI thread overload from rapid panel rebuilds.
- Fixed unnecessary UAC/admin prompt during Update All for users with Vulkan games. The Vulkan ReShade layer was being recopied to ProgramData on every run even when already up to date.
- Fixed manually-installed RenoDX addons (e.g. from Nexus Mods) not being detected after a normal Refresh. The addon file cache was trusting stale "no addon" entries instead of rechecking.
- Fixed per-game update exclusions not being respected. Games with specific components excluded from Update All (via Update Inclusion dialog) were still showing purple update indicators.

### OptiScaler Integration

- OptiScaler now sources DLSS DLLs from the shared version cache (no more third-party CDN dependency). If you've downloaded a DLSS version via the new manager, OptiScaler will use it automatically.

### Improvements

- Game Report (Copy Report) now includes all collected data: update exclusions, addon selections, DLSS/Streamline versions and paths, and preset values.
- Search bar now filters by DLSS/Streamline presence ÔÇö type "DLSS", "Ray Reconstruction", "Frame Generation", or "Streamline" to find games with those components.

### Manifest Updates

- Zero Parades ÔÇö 64-bit override, DX12 API override.
- Gothic II: Gold Classic ÔÇö install path override to `system\` subfolder (ReShade was deploying to wrong directory).

---

## v1.9.6

### New Features

- **Game Launch** ÔÇö Launch your games straight from RHI! Hit the new green "ÔûÂ Launch" button or double-click any game in the sidebar. Steam games launch through Steam (with overlay and playtime tracking), everything else launches directly. Set a custom exe per game in Overrides if auto-detection picks the wrong one.
- **Nexus Mods Update Alerts** ÔÇö RHI now automatically checks if your Nexus-hosted mods have been updated. When a new version drops, the button turns purple with "Update RenoDX" ÔÇö click it to go straight to the Nexus page. No API key needed, no setup required. Games with both Snapshot and Nexus versions show a handy "Also available on Nexus Mods" link in the Info popup.
- **Overrides Panel Revamp** ÔÇö Complete visual overhaul of the per-game overrides panel. Game name and wiki name are now side by side. Shader/addon toggles replaced with compact ComboBox dropdowns (Global, Custom, Select, Off). DXVK toggle and variant selector merged into a single dropdown (Off, Global, Development, Stable, Lilium HDR). DLL naming boxes are hidden when disabled and shown side by side when enabled. Wiki exclusion is now a dropdown instead of a toggle. The separate "ReShade Without Addon Support" toggle has been merged into the RS Channel selector (No Addons option). Management buttons (Change folder, Remove game, Reset Overrides, Copy Report) are now a single compact row. Compact view combines overrides and management into one page instead of two. Overall layout is tighter and more consistent.
- **Auto-cleanup for downloaded addons** ÔÇö Addon files detected and installed from your Downloads folder are now automatically deleted after successful installation. No more clutter.

### Bug Fixes

- Fixed "Update All" skipping games with DLL overrides enabled (e.g. Neverness To Everness). Games with custom DLL filenames are now correctly included in batch updates.
- Fixed "Update Inclusion" button not opening the dialog on some systems (XamlRoot null at build time, now resolved at click time).
- Fixed update indicators (purple buttons/dots) being lost on app restart. Update statuses are now persisted and restored correctly across sessions.
- Fixed global addon toggle removing manually-placed addon files. Stale removal now only deletes files that RHI itself deployed ÔÇö user-placed addons are never touched.
- Fixed "Add Game" button failing with COMException on some systems. Replaced WinRT FileOpenPicker with Win32 native file dialog to avoid COM threading conflicts during background scanning.
- Fixed LumaBoost (and other single-file shader repos) not deploying to game folders. Shader extraction now handles repos without a `Shaders/` subdirectory.
- Fixed shader packs being downloaded multiple times concurrently, causing file lock errors and potential UI freezes during install. Each pack now has a per-pack download lock.
- Fixed `addon_deployments.json` file contention when deploying addons to multiple games simultaneously.
- Fixed Display Commander Info button showing the raw GitHub release page instead of the actual changelog. The update check was pre-populating the field, preventing the changelog fetch.

### Manifest Updates

- Until DawnÔäó ÔÇö moved from UE-Extended to native HDR games.
- BatmanÔäó: Arkham Knight ÔÇö added PCGW URL override (AppID redirect not working).
- Forza Horizon 6 ÔÇö added PCGW URL override.
- Blacklisted DLC/skin entries: Forza Horizon 5 DLCs, Arkham Knight skins, SkinBatmanInc, SkinBatmanNoel, New 52 Skins Pack.
- Stellar Blade ÔÇö added Unreal Engine override (was not auto-detected).
- Elden Ring: Nightreign ÔÇö redirected to Nexus Mods download.

## v1.9.5

### New Features

- **Legacy ReShade Support** ÔÇö Pin any game to a specific older ReShade version (6.0.0 ÔÇô 6.7.2) from the RS Channel dropdown in Overrides. Select "Legacy..." to open the version picker. The chosen version is downloaded on-demand and cached for reuse. Games on legacy versions are automatically excluded from ReShade update checks. The available version list is managed server-side via the manifest ÔÇö no app update needed when new versions release.
- **LumaBoost shader pack** ÔÇö OLED ABL compensation shader by Valadore added to the shader picker (Extra category).

### Bug Fixes

- Fixed managed addons being deployed with sanitized package names instead of their original filenames. Addons now retain the filename from their download URL (zip-extracted names preserved via versions.json backfill).
- Fixed Luma uninstall not removing the Luma folder from game directories.
- Fixed "Update All" button not turning purple when updates are available from cached state (e.g. after restart within the 4-hour cooldown).
- Fixed update inclusion summary showing "UL" instead of "RL" for ReLimiter.
- Fixed ReLimiter row showing "ReShade required" on 32-bit games instead of "Not supported on 32-bit".
- Fixed addon stale removal not recognising URL-derived or original filenames, causing addons to persist when switching from per-game to global.
- Fixed concurrent addon downloads causing file lock contention on startup.
- Fixed "Collection was modified" error in DeployAllAddons when rapidly toggling global addons.

### Manifest Updates

- Added Avatar: Frontiers of Pandora (AFOP) ÔÇö wiki match, Nexus, PCGW links.
- Added Assassin's Creed ÔÇö DX10 API override, 32-bit bitness, author corrected to Musa.
- Added GreedFall: The Dying World ÔÇö external Nexus link, author RankFTW.
- Added Max Payne 3 ÔÇö external Nexus link, ReShade 6.4.1 forced via legacy, game notes for both RenoDX and ReShade Info buttons, DX11 API override.
- Added Call of Duty: Black Ops III (non-┬« variant) to Luma default games.
- Until DawnÔäó ÔÇö removed from native HDR games, updated note with HDR + upgrade path instructions.
- Added Dragon Age: Inquisition ÔÇö DX11 API override.
- Added Stardew Valley ÔÇö OpenGL API override.
- Added Wartales ÔÇö DX11 API override.
- Added empty placeholders for all per-component Info button fields.

## v1.9.4

### Bug Fixes

- Fixed DXVK staging downloading all 3 variants on every startup, causing GitHub API rate limiting for users with fresh installs. Only the globally selected variant is now downloaded at startup ÔÇö other variants are fetched on-demand when a per-game override needs them.

## v1.9.3

### New Features

- **Per-Game ReShade Channel Override** ÔÇö Override the global ReShade build channel (Stable/Nightly) per game from the Overrides panel. Switching channels instantly reinstalls ReShade ÔÇö no manual update needed. Vulkan games warn that the change applies to all Vulkan games since they share a global layer.
- **Per-Game DXVK Variant Override** ÔÇö Override the global DXVK variant per game from the Overrides panel. The "DXVK Variant" dropdown appears next to the DXVK toggle with options: Global, Development, Stable, Lilium HDR. Switching variants instantly reinstalls DXVK.
- **DXVK Lilium HDR Variant** ÔÇö A third DXVK variant from EndlesslyFlowering. Upgrades the swap chain to scRGB for HDR output on DX8/DX9/DX10 games. The appropriate HDR dxvk.conf settings are deployed automatically when this variant is selected.

### Changes

- Switching the global ReShade channel or DXVK variant in Settings now instantly reinstalls all affected games (respecting per-game overrides) instead of requiring manual update.
- All ReShade and DXVK variants are now downloaded and kept up to date simultaneously in separate folders, enabling instant switching without re-downloading.
- Existing users will have their ReShade and DXVK staging folders migrated automatically on first launch.
- ReShade nightly update detection improved ÔÇö now reliably detects new builds.

### QoL

- The "Save Custom Filter" dialog now pre-populates the filter name with the current search text.
- Toolbar redesigned: "Global Shaders" and "ReShade Addons" combined into a single "Shaders/Addons" dropdown. New "Links" dropdown with RenoDX Wiki, Luma Wiki, RHI GitHub, ReLimiter GitHub, and Display Commander GitHub. View toggle replaced with a "Views" dropdown (Compact, Detail, Grid).
- Games installed via Steam but detected by EA/Ubisoft/etc now correctly show the Steam badge when the install path is in a Steam folder. Requires a Full Refresh.
- "Add Game" flow simplified: click the button, pick the game's exe, then name it. No more confusing name-first workflow. 

### Bug Fixes

- Fixed custom screenshot hotkey not being applied to reshade.ini on fresh game installs.
- Fixed DXVK per-game variant override not deploying the correct DLLs or dxvk.conf when the variant was changed before enabling the toggle.

## v1.9.2

### New Features

- **DXVK Proxy Mode for DX8/DX9 games** ÔÇö DX8 and DX9 games now use a ReShade proxy chain instead of the Vulkan implicit layer when DXVK is enabled. DXVK is deployed as `dxgi_dxvk.dll` and ReShade chains to it via the `[PROXY]` section in reshade.ini. No Vulkan layer install or admin privileges needed. This matches the method recommended by RenoDX mod authors on Nexus Mods.

### Bug Fixes

- Fixed Luma mode toggle disappearing after app restart. The `IsLumaMode` flag was not being set during the cache phase or copied during the background merge, so Luma games lost their mode state until a full refresh.
- Fixed frame limiters (ReLimiter, Display Commander) showing "ReShade required" on Luma games after toggling Luma mode back on. Luma bundles its own ReShade, so `IsRsInstalled` now returns true when Luma is installed in Luma mode.
- Removed the "ÔØô Unknown" wiki status badge ÔÇö games with no wiki match now show no badge instead of a misleading "Unknown" label.
- Fixed update-available statuses (green dots, purple buttons) not persisting across app restarts. Update badges are now saved to the library cache and restored on launch, so they survive the 4-hour update check cooldown.

## v1.9.1

### Highlights

**DXVK Integration (WIP)** ÔÇö DXVK is now a managed per-game component. Enable it from the Overrides panel on DX8/DX9/DX10 games to translate DirectX calls to Vulkan, enabling ReShade compute shaders and potentially reducing CPU-bound stuttering on older titles. This is an advanced feature ÔÇö not all games are compatible. Note: This feature is still a work in progress and has only been tested by the developer. Expect rough edges.

**ReShade Nightly Build Channel** ÔÇö A new "Build Channels" section on the Settings page lets you choose between Stable (reshade.me releases, default) and Nightly (latest GitHub Actions builds from the crosire/reshade repository). Switching channels clears the ReShade staging cache, downloads from the new source, flags all games with ReShade installed as needing an update, and updates the global Vulkan layer DLLs ÔÇö so you can Update All to apply the new build across your entire library.

**Component Changelogs** ÔÇö The Info buttons on the ReLimiter and Display Commander component rows now fetch the project's CHANGELOG.md from GitHub and display the patch notes for the installed version plus the two previous versions, rendered as markdown. The buttons are highlighted blue to indicate content is available.

### New Features

- **DXVK Integration (WIP)** details:
  - Per-game toggle in the Overrides panel (hidden for DX11/DX12/OpenGL/Vulkan)
  - DXVK component row in the Components section (visible only when enabled)
  - Automatic ReShade mode switching: when DXVK is enabled, ReShade switches from DX proxy to Vulkan layer mode; when disabled, it switches back with the correct API-specific filename (d3d9.dll for DX9, etc.)
  - DX8/DX9 proxy mode: DXVK is deployed as `dxgi_dxvk.dll` and ReShade chains to it via the `[PROXY]` section in reshade.ini ÔÇö no Vulkan layer or admin needed. Matches the method recommended by RenoDX mod authors on Nexus.
  - OptiScaler coexistence: filename conflicts are automatically resolved by routing DLLs to the OptiScaler plugins folder
  - Game originals backed up as `.original` and restored on uninstall
  - dxvk.conf deployed with sensible defaults (HDR enabled, borderless fullscreen, latency sleep)
  - Binary signature detection for foreign DLL protection
  - Update All integration via the existing Update Inclusion dialog (DXVK only appears when enabled for a game)
  - Settings page variant selector: Development (nightly builds via nightly.link ÔÇö default) or Stable (tagged releases)
  - Warning dialog with "Don't show again" checkbox ÔÇö explains this is an advanced unsupported feature
  - Dual-API awareness: games with DX12 detected alongside their primary API won't show the DXVK toggle
  - ReShade Install button automatically uses Vulkan layer path when DXVK is active

### Bug Fixes

- Fixed UW Fix tooltip always saying "Lyall" ÔÇö it now shows the correct creator (Lyall, Rose, or p1xel8ted) per game.
- Fixed ReShade DLL being renamed from `d3d9.dll` to `dxgi.dll` on refresh for DX9 games. The default naming reconciliation now respects the game's API ÔÇö DX9 games keep `d3d9.dll`, OpenGL keeps `opengl32.dll`.
- Fixed ReShade `d3d9.dll` being incorrectly backed up as a "foreign" DLL during Update All. The foreign DLL detection now recognises ReShade installed under DXVK-managed filenames (d3d9.dll, d3d10core.dll, etc.).
- Fixed drag-and-dropped addons disappearing after refresh. The drag-and-drop install now saves a persistent record to the database so the addon is detected on subsequent launches and refreshes.
- Fixed addon file watcher triggering duplicate installs when downloading to the watch folder. Browser downloads fire both Created and Renamed events ÔÇö a 5-second deduplication window now prevents the second install.
- Fixed ReLimiter showing "Installed" instead of its version number on launch. The instant-launch cache path had no ReLimiter detection ÔÇö it now checks for the addon file and reads the version from local metadata immediately.
- Fixed component version numbers (ReLimiter, Display Commander, OptiScaler, RE Framework) not updating after the background scan completed. The merge step was copying status fields but not version or filename fields, so versions stayed blank until a manual Refresh.
- Fixed wiki status badge showing "ÔØô Unknown" until switching games or refreshing. The computed badge properties (label, colours, icon) were not being notified when `WikiStatus` changed during the background merge ÔÇö they now update immediately.
- Fixed corrupted ReShade staging file (2.88KB instead of ~5MB) causing false "update available" badges on every game and deploying a broken DLL on update. Added 1MB minimum size validation to ReShade staging so corrupted files trigger a re-download.

### Manifest Updates

- FINAL FANTASY XIII, FINAL FANTASY XIII-2, and FINAL FANTASY XVI wiki-unlinked ÔÇö FFXIII was being falsely matched to FFX, FFXVI to FFXV.
- Added DXVK blacklist for anti-cheat games (Fortnite, Apex Legends, Valorant, etc.)
- Added DXVK game notes for FFXIV

## v1.9.0

### Highlights

**Instant Launch** ÔÇö The game list now appears instantly on startup. On subsequent launches, the app loads your library from cache and displays it immediately ÔÇö no more waiting for game detection and network fetches. The full scan runs silently in the background and merges any changes (new games, updated statuses) into the already-visible list.

### New Features

- Ultrawide fix links now appear on game cards. If a game has an ultrawide/resolution fix available from Lyall, RoseTheFlower, or p1xel8ted, a "UW Fix" button shows next to the Nexus and PCGW buttons. Clicking it opens the fix page directly. All three sources are fetched once and cached for 24 hours. Manifest overrides available for edge cases where automatic name matching fails.
- Ultra+ links now appear on game cards. If a game has an Ultra+ mod on theultraplace.com, an "Ultra+" button shows next to the other link buttons. Clicking it opens the Ultra+ page for that game directly.
- Typing "UW Fix" or "Ultra+" in the search bar now filters to games that have those links, just like searching for engine names or authors.
- Nexus, PCGW, UW Fix, and Ultra+ link buttons are now underlined with a hand cursor on hover.

### Performance

- Update checks now have a 4-hour cooldown. Launching the app multiple times no longer hammers the GitHub API ÔÇö checks are skipped if the last successful check was recent. Full Refresh bypasses the cooldown when you need to force a check.
- GitHub API rate limiting is now detected and handled gracefully. If a 403 is received, all remaining API calls for the session are skipped instead of each one failing independently.
- Shader packs from GitHub Releases (Lilium, PumboAutoHDR) no longer call the API on every startup. If the files are already cached and extracted, the check is skipped entirely.

### Bug Fixes

- Fixed games multiplying in the sidebar until they achieved world domination. Some games (e.g. S.T.A.L.K.E.R. 2, Indiana Jones) could appear up to six times from the same store due to the v1.8.9 dedup change using install path as the uniqueness key. Paths that varied slightly between scans or cache entries were treated as separate games. Deduplication now keys on game name + store instead of game name + path, so each store can only contribute one entry per game. Existing duplicates in the cached library are cleaned up automatically on launch.

### Manifest Updates

- Satisfactory now installs to the correct `Engine\Binaries\Win64` subfolder.
- Indiana Jones DLC blacklisted (Xbox Store dash-variant names).

## v1.8.9

### Bug Fixes

- Fixed games installed on multiple platforms (e.g. Steam and Xbox) only showing once. Both copies now appear in the sidebar with their respective platform icons, so you can manage mods for each install independently.
- Fixed DLC and expansion packs (e.g. X4 DLC, DOOM DLC) appearing as separate entries when they share the base game's install folder. They now collapse to the base game automatically.
- Fixed the "Shared OSD Presets" setting not being applied to newly installed ReLimiter games. New installs now inherit the shared presets toggle immediately.

### Manifest Updates

- Until Dawn added as UE-Extended with native HDR support.
- Grand Theft Auto III, Vice City, and San Andreas Definitive Editions added as individual UE-Extended entries with SDR-to-HDR upgrade support.
- Aphelion added as Unreal Engine.
- Battle.net launcher components blacklisted.
- L.A. Noire and Dying Light 2 RenoDX mods removed ÔÇö both are deprecated. L.A. Noire has a note linking to the older Nexus mod that works with ReShade 6.3.3.

## v1.8.8

### Bug Fixes

- OptiScaler and Luma updates now show the green update dot in the sidebar, matching the behaviour of all other components.
- Luma is now included in the "Update All" button. Games with a newer Luma-Framework build available will be updated automatically alongside ReShade, RenoDX, and other components.
- Fixed OptiScaler updates not actually downloading the new version. The update button now clears the old staging files and downloads the latest release before deploying.
- Fixed OptiScaler updates incorrectly backing up its own companion files (FidelityFX DLLs, FakeNVAPI, libxess, etc.) as `.original`. Only genuine game files are backed up now.
- Fixed OptiScaler version number not updating after an update, causing the update badge to reappear on every refresh.

## v1.8.7

### New Features

- Added "Shared OSD Presets" toggle for ReLimiter on the Settings page. When enabled, all games share the same OSD presets from a central file instead of each game having its own. The "Apply to All Games" button writes both the hotkey and the shared presets setting to all deployed relimiter.ini files, and new installs inherit the setting automatically.

### Bug Fixes

- Fixed games showing a false ReShade update badge after switching between addon and non-addon ReShade. The update check now recognises both ReShade variants so toggling the preference no longer triggers a phantom update.
- Fixed multiple games without cached PCGW data each waiting 5 seconds on startup when PCGamingWiki is down. The first timeout now cancels all other in-flight PCGW requests immediately instead of each waiting independently.

### Manifest Updates

- FINAL FANTASY XIV Online now installs to the correct `game` subfolder.
- Assassin's Creed Origins and Odyssey now correctly detect as DX11 instead of OpenGL.
- Elden Ring install button now links to Nexus Mods instead of the snapshot download.
- Sea of Thieves blacklisted ÔÇö ReShade can cause bans in this game.
- Minecraft Launcher blacklisted (not a game).
- Added direct PCGW links for Alan Wake 2 and Fortnite to avoid slow lookups.

## v1.8.6

### Highlights

**Luma Update Detection** ÔÇö Luma mods now check for updates automatically. When a newer Luma-Framework build is released, your installed Luma games will show an update badge just like RenoDX and ReShade do. The installed build number is also displayed in the component status (e.g. "Build 428").

### New Features

- Luma mods now show update badges and the installed build number in the detail panel. The install button shows "Update Luma" when an update is available.
- RE Engine games can now install ReShade without RE Framework. Uncheck RE Framework in the Update Inclusion dialog and the ReShade install button unlocks immediately ÔÇö no app restart or refresh needed.
- The Update Inclusion dialog now refreshes the detail panel instantly when you save, so changes like enabling or disabling RE Framework take effect without clicking Refresh.

### Bug Fixes

- Fixed preset shader install doing nothing when shader cache was turned off. The app now fetches any missing shader pack info before resolving which packs a preset needs, so presets work regardless of your cache setting.
- Fixed the shader mode not visually switching to "Select" after installing a preset from the right-click flyout or the mass preset deploy in Settings. The overrides panel now updates immediately.
- Fixed the uninstall (red X) buttons for RenoDX, ReLimiter, and Display Commander disappearing when ReShade was uninstalled. You can now always remove installed components even if ReShade isn't present.
- Fixed drag-and-drop mod installs from Discord (and other sources) being silently ignored when a background dialog (like an update check) was still open. Dialogs now queue instead of being skipped.
- Fixed RE Framework failing to download with a 404 error. The nightly releases switched from per-game zips to a single monolithic build ÔÇö the app now downloads `REFramework.zip` which works for all RE Engine games.
- Fixed Luma games (Hollow Knight: Silksong, Metro Redux, etc.) falsely showing a ReShade update badge. Luma bundles its own ReShade version, so the update check now skips games in Luma mode.
- Fixed the app hanging for up to 40+ seconds on startup when PCGamingWiki is down. The PCGW lookup now has a 5-second timeout and automatically disables itself for the session after the first failure.

### Under the Hood

- Major structural cleanup: large service files split into focused modules, duplicated hotkey and dialog code consolidated into shared helpers, and unused legacy code removed. No behavior changes ÔÇö just a cleaner foundation for future features.

## v1.8.5

### Highlights

**On-Demand Shader Downloads** ÔÇö New "Shader Cache" toggle on the Settings page. When disabled, shader packs are no longer bulk-downloaded on startup. Instead, they're fetched only when needed ÔÇö when you select them in the shader picker, install ReShade, or deploy a preset. Existing cached shaders are never deleted by the app, so you can toggle this off without losing anything. The shader selection dialog now shows a green Ô£ô next to each pack that's already cached locally.

### New Features

- RE Framework can now be excluded from Update All, both per-game (via the Update Inclusion dialog in overrides) and globally (via the Settings page toggle). The RE Framework checkbox only appears for RE Engine games.

### Bug Fixes

- Fixed ReShade being installed as d3d9.dll instead of dxgi.dll on games that import both DX9 and DX11 (e.g. Assassin's Creed Unity, Of Ash and Steel, Lies of P). DX11/DX12 now takes priority over legacy DX9 imports when resolving the ReShade DLL filename.
- Fixed the app becoming unresponsive (unable to click anything, but window still movable) caused by two dialogs trying to open at the same time. All dialogs now go through a centralized guard that prevents concurrent opens.
- Update inclusion summary text now wraps instead of clipping when RE Framework is shown.

## v1.8.4

### Bug Fixes

- Fixed the "ReShade Without Addon Support" toggle automatically installing or uninstalling ReShade when flipped. The toggle now only sets the preference ÔÇö use the Install ReShade button to actually install the correct version. Toggling on will uninstall any existing addon ReShade (and its shaders/addons), and toggling off will uninstall any existing normal ReShade, but neither direction auto-installs the replacement.
- Fixed ReShade showing a false "update available" badge on every launch for games using ReShade without addon support when the normal ReShade staging files were missing or hadn't been downloaded yet.

## v1.8.3

### Highlights

**The 2-Pixel Fix** ÔÇö After months of painstaking investigation, we are beyond proud to announce the most significant visual improvement in RHI history. Manually added games in the sidebar were misaligned by exactly two pixels. Two. The wrench icon for custom-added games rendered at a fractionally different width than the Steam, Xbox, Epic, GOG, Ubisoft, Battle.net, and Rockstar icons, causing every single game name after it to sit imperceptibly ÔÇö yet unforgivably ÔÇö out of line. This was the kind of defect that haunts you at 3am. The kind you see every time you open the app. The kind that, once noticed, can never be unseen. It has been fixed. The source icon column now uses a precision-engineered fixed-width container, guaranteeing pixel-perfect alignment across every game in your library, no matter how it was added. Sleep well tonight.

### Changes

- Reduced GitHub API usage with smart caching. Update checks and component downloads no longer fail when launching the app multiple times in a short period.
- Install buttons now show a Ôùä arrow indicator when the Info button has game-specific content, drawing attention to it.
- New "ReShade screenshot key" setting on the Settings page. Set a custom key for taking ReShade screenshots, applied to all managed reshade.ini files. Defaults to Print Screen with a reset button to restore it.
- Display Commander upgraded from the LITE variant to the full version. Existing DC Lite installs are automatically migrated ÔÇö the update badge will appear and clicking Update replaces the lite file with the full version seamlessly.

### Performance

- Game scanning on startup is significantly faster. Most games now load in under 100ms, down from 500msÔÇô1.5s. Total scan time reduced by up to 90%.
- Games with known engines (Assassin's Creed, Battlefield, Metro, Control, etc.) no longer run filesystem-based engine detection on every launch. The engine is read from the manifest instead, skipping expensive directory traversals.
- Fixed two ReShade addons (FreePIE and Screenshot to Clipboard) being re-downloaded on every launch instead of using the cached version.
- Shader pack checks are now instant on normal launches ÔÇö the app skips re-verifying files that haven't changed since the last run.

### Bug Fixes

- Fixed generic RenoDX mods (Unity, Unreal, UE-Extended) not detecting updates and silently reinstalling the old version from cache. Updates are now downloaded fresh and applied correctly.
- Generic Unity and Unreal mods are now sourced directly from GitHub Releases instead of the GitHub Pages CDN, making update detection faster and more reliable.
- Fixed Luma addon files not being placed in the custom addon folder when the user has a custom AddonPath set in reshade.ini. Luma addons now follow the same path rules as all other addons.
- Fixed repeated error messages in the log for games with broken symlinks or missing shader folders (e.g. The Ascent).

## v1.8.2

### New Features

**Per-addon Info buttons**
- Each component (RE Framework, ReShade, RenoDX, ReLimiter, Display Commander, OptiScaler, Luma) now has an "Info" button next to its install button.
- Clicking it opens a dialog with game-specific notes, wiki compatibility data, or a general description of the addon.
- Buttons with game-specific content are highlighted in blue so you can spot them at a glance.
- Works in Detail, Grid, and Compact views.

**OptiScaler wiki compatibility info**
- The OptiScaler Info button now pulls compatibility data directly from the OptiScaler wiki ÔÇö working status, supported upscalers, notes, and links to detailed wiki pages.
- Both the standard and FSR4 compatibility lists are included.

**HDR Gaming Database links**
- The RenoDX and Luma Info buttons now link to the HDR Gaming Database when a game has an HDR analysis entry, giving you quick access to detailed HDR breakdowns.

**Native HDR game guidance**
- Games that use UE-Extended with native HDR now show a clear message in the RenoDX Info button explaining that HDR must be enabled in the game's display settings.

**Luma toggle redesigned**
- The Luma mode toggle is now more visible ÔÇö centered in the Components header with clear "Click to enable/disable Luma" text.

**Luma reshade.ini deploy button**
- The Luma install row now has a ­ƒôï button for copying reshade.ini to the game folder, matching the ReShade row.

**RE Framework required for RE Engine games**
- RE Engine games now require RE Framework to be installed before ReShade can be installed, preventing broken setups. The ReShade button shows "RE Framework required" and is greyed out until RE Framework is in place.

**Notes and Discussion buttons moved**
- The "Ôä╣" and "­ƒÆ¼" buttons from the game header have been replaced by the new per-addon Info buttons. RenoDX notes and wiki links are now on the RenoDX Info button.

### Bug Fixes

- Fixed ReLimiter and Display Commander staying greyed out after installing Luma until a manual refresh.
- Fixed ReShade showing as "Installed" after disabling Luma mode, even though Luma's ReShade was removed.
- Fixed "Skipped ÔÇö unknown dxgi.dll" warning during Update All when OptiScaler is installed.
- Fixed OptiScaler wiki not matching some games due to naming differences (e.g. Resident Evil, S.T.A.L.K.E.R., Borderlands┬« 4, Assassin's Creed).
- Fixed Compact View window briefly appearing in the wrong position on startup before jumping to the saved location.
- Fixed a rare startup crash caused by concurrent access to game lists during parallel card building.
- Fixed ReLimiter OSD hotkey not working when set to Page Up, Page Down, or other multi-word keys.
- Fixed navigating to Settings during initial load causing the main UI to never finish loading. The Settings button is now disabled until games are loaded.

## v1.8.1

### New Features

**Compact View mode**
- New third view mode alongside Detail and Grid. Toggle between views using the toolbar button.
- Compact View shows the same game card, overrides, and management panels as Detail View, split across three navigable pages with left/right arrow buttons.
- The window locks to a fixed size in Compact View and restores your previous size when switching back.
- Your chosen view mode is remembered across app restarts.

**View-specific loading skeletons**
- The loading skeleton now matches your last-used view. Grid View shows a card grid skeleton, Detail and Compact Views show the detail panel skeleton.

**PD-Upscaler REFramework for OptiScaler on RE Engine games**
- When installing OptiScaler on Resident Evil 2, 3, 4, 7, or Village, RHI automatically downloads and installs the pd-upscaler branch of REFramework required for OptiScaler compatibility.
- The standard REFramework is backed up and restored when OptiScaler is uninstalled.
- The RE Framework version display updates in real time to show "PD-Upscaler" while OptiScaler is active.

**Global Update Check toggles**
- New settings section to globally disable update checks for individual components: RenoDX, ReShade, ReLimiter, Display Commander, and OptiScaler.
- When disabled, the component is skipped during startup update checks and Update All.

**Manifest author overrides**
- Mod authors can now be set via the remote manifest for games that aren't on the wiki. Author badges and donation links work the same as wiki-sourced authors.

### Changes

- The toolbar button now shows the name of the current view mode instead of the next mode.
- Manifest `forceExternalOnly` entries pointing to Nexus Mods now show a "Nexus" badge instead of "Discord".
- Nav arrow buttons in Compact View now match the toolbar button styling.

### Bug Fixes

**Xbox / Game Pass games no longer lose mods after game updates**
- When a Game Pass game updates, Windows changes its install folder path. Previously this caused RHI to lose track of installed mods, showing them as needing reinstall. RHI now detects the path change and migrates your installed mods (RenoDX, ReShade, Display Commander, OptiScaler) to the new folder automatically.

**ReShade detection for ReShade64.dll / ReShade32.dll**
- Fixed ReShade not being detected when installed using its own filename (ReShade64.dll or ReShade32.dll) rather than a proxy DLL name like dxgi.dll.

**RE Framework update check false positive with PD-Upscaler**
- Fixed all RE Framework games being flagged as needing an update when one game had the PD-Upscaler version installed.

**Manifest forceExternalOnly badge and label fixes**
- Fixed `forceExternalOnly` entries being skipped when the game was already marked as external-only from wiki matching. The manifest URL and label now always take priority.
- Fixed the Discord badge showing for games whose manifest entry points to Nexus Mods.

**Window position remembered in Compact View**
- Fixed the app not restoring its window position on startup when Compact View was the last-used mode.

**Manifest JSON parse error**
- Removed stray backtick characters in the blacklist that caused the manifest to fail parsing from the GitHub API.

## v1.8.0

### New Features

**OptiScaler integration**
- OptiScaler is now a fully managed component in RHI. One-click install, update, and uninstall for upscaler redirection (DLSS/FSR/XeSS) on 64-bit games.
- New OptiScaler Settings section on the Settings page ÔÇö configure GPU type (NVIDIA/AMD/Intel), DLSS input replacement toggle (AMD/Intel only), and overlay hotkey. Settings are persisted and applied automatically on every install.
- First-time install warning prompts users to configure OptiScaler settings before proceeding.
- All OptiScaler files are deployed from the staging folder, including companion DLLs, INI files, and the `D3D12_Optiscaler` subfolder. Installer scripts, READMEs, and license files are excluded.
- Game-owned files are backed up to `.original` before overwriting and restored on uninstall.
- ReShade coexistence handled automatically ÔÇö ReShade is renamed to `ReShade64.dll` when OptiScaler is installed, and restored to the correct filename on uninstall.
- Vulkan games automatically use `winmm.dll` as the OptiScaler DLL filename. User and manifest overrides still take priority.
- DLL naming override dropdown in the per-game overrides panel. Manifest support for per-game OptiScaler DLL name defaults.
- Per-game OptiScaler update exclusion toggle in the overrides panel.
- OptiScaler status included in Update All, Refresh, game report, skeleton loading screen, and card flyout.
- Binary signature detection for existing OptiScaler installations. Foreign DLL protection recognises OptiScaler DLLs.
- 32-bit games show the OptiScaler row greyed out with strikethrough.
- OptiScaler Compatibility List link on the Settings page, linking to the community-maintained wiki.

**OptiPatcher integration**
- OptiPatcher ASI plugin is automatically downloaded and deployed to the `plugins` folder for AMD/Intel GPU users during OptiScaler install. Enables DLSS/DLSSG inputs without GPU spoofing in supported games.
- Version-tracked and cleaned up automatically on OptiScaler uninstall.

**DLSS auto-download (Super Resolution, Ray Reconstruction, Frame Generation)**
- The latest NVIDIA DLSS Super Resolution (`nvngx_dlss.dll`), Ray Reconstruction (`nvngx_dlssd.dll`), and Frame Generation (`nvngx_dlssg.dll`) DLLs are automatically downloaded and staged on startup. Sourced from the DLSS Swapper manifest.
- Every OptiScaler install deploys all three DLLs directly to the game folder, enabling DLSS upscaling, Ray Reconstruction, and DLSS-FG even for games that don't ship with them. Game originals are backed up and restored on uninstall.
- Each DLL has independent version tracking and auto-updates on each app launch.

**ReShade dependency enforcement**
- RenoDX, ReLimiter, and Display Commander install buttons now require ReShade to be installed first. When ReShade is not installed, buttons show "ÔÜá ReShade required" and the rows are dimmed.

**Mass INI Deployment**
- New section on the Settings page to deploy reshade.ini, relimiter.ini, DisplayCommander.ini, or OptiScaler.ini to all games that have the corresponding component installed with a single button click. Custom hotkey and screenshot path settings are preserved.

**Mass ReShade Preset Install**
- New section on the Settings page. Select presets from your presets folder, choose which games to deploy them to via a checkbox game picker with Select All / Deselect All, and optionally install the required shader packs ÔÇö all in one flow.

### Changes

- Pre-generated OptiScaler INI templates bundled for each GPU configuration (NVIDIA, AMD/Intel with DLSS, AMD/Intel without DLSS). FPS overlay, FPS cycle, and Frame Generation hotkeys are unbound by default to prevent keybind conflicts.
- `LoadReshade=true` and `LoadAsiPlugins=true` are enforced in all OptiScaler INI deployments.
- OptiScaler overlay hotkey written as Windows Virtual Key Code hex values matching OptiScaler's expected format.
- Global update inclusion toggles in the overrides panel replaced with a compact "Update Inclusion" button and a colour-coded summary line.
- Bitness and Graphics API dropdowns in the overrides panel are now side by side instead of stacked vertically.
- Frame limiter separator text updated to "Frame limiters ÔÇö Choose one".
- Manifest `wikiUnlinks` now fully disconnects games from the wiki ÔÇö no mod match, no generic UE/Unity fallback, no Discord badge.
- Single-player warning text updated: "ReShade with addon support and OptiScaler may trigger anti-cheat."
- Skeleton loading screen updated to reflect the current detail panel layout.

### Performance

- Startup time reduced by up to 60% through multiple optimisations:
  - PCGW cache writes debounced ÔÇö ~45 concurrent file lock errors per startup eliminated.
  - OptiScaler detection now scans only the 7 known proxy DLL names instead of every DLL in the game folder.
  - WindowsApps game paths skipped for OptiScaler detection, ReShade proxy scanning, and addon file scanning.
  - DLC content packs (DOOM, Yakuza, Indiana Jones, MWII, Battle.net launcher components) blacklisted from game detection.
- Fixed NNShaders shader pack failing to download every startup (GitHub URL corrected from `main` to `master` branch).

### Bug Fixes

- Fixed "Unknown dxgi.dll Detected" warning appearing when installing ReShade after OptiScaler.
- Fixed ReShade not being deleted when uninstalling after OptiScaler was removed.
- Fixed ReShade being renamed to `opengl32.dll` instead of `dxgi.dll` on OptiScaler uninstall for games with both DX12 and OpenGL detected.

---

## v1.7.8

### Changes

**Deploy button disabled when no presets selected**
- The Deploy button in the preset picker is now greyed out until at least one preset is ticked.

**DLSS Fix addon filename preserved on deploy**
- The DLSS Fix addon now deploys to game folders using its original filename (`renodx-dlssfix.addon64`) instead of being renamed, which is required for it to work correctly.

**Terraria install note updated**
- The game note for Terraria now mentions that admin privileges are required for ReShade installation due to GAC symlink creation.

**Manifest overrides for Nexus and PCGW**
- Added manual Nexus Mods URL overrides for games where automatic name matching fails (Deadly Premonition 2, Dying Light: The Beast, Echoes of the End, Horizon Forbidden West, Morrowind, The Sinking City Remastered, Tunguska: The Visitation, X4: Timelines).
- Added PCGW URL override for The Sinking City Remastered.

## v1.7.7

### New Features

**Nexus Mods and PCGamingWiki links**
- Each detected game now shows Nexus Mods and PCGamingWiki buttons in the detail panel. Links are resolved automatically ÔÇö Nexus Mods via the public game catalogue, PCGW via Steam AppID lookup or wiki search. Games that can't be matched automatically can be overridden in the manifest.

**DLSS Fix addon**
- New managed addon that makes ReShade draw on native game frames instead of frame gen frames, and hides DLSS upscaling from ReShade. Available in the ReShade Addons dialog with automatic update checking.

### Changes

**Detail panel layout refresh**
- Game name and mod author are now displayed above the info card rather than inside it, giving the header more breathing room.
- Info badges (addon file, engine, source, API, status) are grouped in a bordered card with the Nexus and PCGW links on the left, and Browse, Info, and Discussion buttons on the right.
- Hide and Favourite buttons sit on the top row alongside the Nexus/PCGW links. Favourite now shows as a text button that highlights yellow when active, matching the style of the other action buttons.
- Folder management buttons (Change install folder, Reset folder, Reset Overrides, Copy Report) have been moved out of the overrides section into their own dedicated section underneath.
- The ReShade preset selector and Normal ReShade toggle are now side by side with a divider, instead of stacked vertically.
- The Select ReShade Preset button now uses blue accent styling to match Select Shaders.

### Bug Fixes

**Shader toggle not updating after preset install**
- Installing a preset with shaders via drag-and-drop or the preset dialog now immediately updates the Global Shaders toggle in the overrides panel, instead of requiring a manual refresh.

**Preset folder link not clickable when empty**
- The folder path in the "No preset files found" dialog is now a clickable link matching the style used when presets are present.

## v1.7.6

### Highlights

**ReShade preset drag-and-drop with automatic shader install** ÔÇö Drag a preset `.ini` onto RHI and it'll validate it, deploy it to a game, and offer to automatically install the required shader packs. No more hunting for which packs a preset needs. We audited 30 popular presets across Elden Ring, Skyrim, Cyberpunk, GTA V, FFXIV, and more ÔÇö RHI's 41 shader packs cover every freely-available shader out there.

**ReShade Without Addon Support** ÔÇö New per-game toggle to switch from addon-enabled ReShade to standard ReShade. All addons are cleanly removed and the rows dim out. Toggle back to restore everything.

### New Features

**ReShade overlay hotkey configuration**
- New "ReShade UI Hotkey" section on the Settings page lets you capture a key combination (with Ctrl/Shift/Alt modifiers) and apply it to all managed reshade.ini files. The hotkey persists across restarts and is automatically written to newly deployed INI files. When set to the default Home key, RDR2 is skipped to preserve its template END key.

**ReLimiter OSD hotkey configuration**
- New "ReLimiter OSD Hotkey" section on the Settings page lets you set the key combination to toggle the ReLimiter overlay in-game. Uses ReLimiter's native format (e.g. Ctrl+F12, Alt+P). Applies to all relimiter.ini files in game folders and the AppData template so new installs inherit the setting.

**ReShade Without Addon Support (per-game toggle)**
- New toggle in the game overrides panel lets you switch individual games from addon-enabled ReShade to standard ReShade (without addon support). When enabled, all addons (RenoDX, ReLimiter, Display Commander, managed addon packs) are removed from the game folder, addon rows are dimmed and disabled, and the addon override toggle is locked off. Toggling back restores addon ReShade and re-deploys addons. The setting persists per-game across app restarts.

**Automatic INI deploy on first install**
- Installing ReLimiter or Display Commander to a game for the first time now automatically copies your pre-configured `relimiter.ini` or `DisplayCommander.ini` from the AppData INI folder to the game directory. If the INI already exists in the game folder it's left untouched, so per-game customisations are never overwritten. If the source INI doesn't exist or the copy fails, the install continues normally ÔÇö no error, no interruption.

**ReShade preset drag-and-drop with automatic shader install**
- You can now drag and drop a ReShade preset `.ini` file onto the RHI window to install it. RHI validates the file as a genuine ReShade preset, saves it to the presets folder, lets you pick a target game, and copies it to the game directory. After deploying, RHI offers to automatically resolve and install the shader packs required by the preset ÔÇö parsing the `Techniques=` line, matching `.fx` files against known shader packs, switching the game to per-game shader mode, and deploying the matched packs. The same shader install prompt also appears when deploying presets from the existing preset selection dialog. A new Glamarye Fast Effects pack was added after auditing 30 popular presets ÔÇö RHI's 41 shader packs now cover every freely-distributable shader used by real-world presets.

**Mutual-exclusion dimming for ReLimiter / Display Commander**
- When ReLimiter is installed, the Display Commander row is now visually dimmed (and vice versa), making the mutual exclusivity between the two much clearer at a glance.

### Changes

**Settings page layout redesign**
- Settings sections are now grouped into two-column layouts with vertical dividers: Add Game / Full Refresh, Screenshots / ReShade UI Hotkey, and Custom Shaders / Addon Watch Folder. Reduces scrolling and makes the page more compact.

**Preset folder path is now a clickable link**
- The presets folder path in the "Select ReShade Presets" dialog is now a hyperlink that opens the folder in Explorer when clicked.

### Bug Fixes

**Last selected game not restored on launch**
- The "remember last selected game" feature was broken ÔÇö the saved selection was being overwritten by auto-select during init, and the library wasn't being saved on app close. Both issues are now fixed.

**DC, ReLimiter, and RE Framework update status lost on refresh**
- Display Commander, ReLimiter, and RE Framework update indicators weren't surviving a normal refresh ÔÇö only RenoDX and ReShade statuses were being preserved when cards were rebuilt. A Full Refresh was needed to re-detect updates. All five components now carry their update status forward correctly.

**Shader and preset picker dialogs unreadable in dark mode**
- The root content grid was missing `RequestedTheme="Dark"`, so on PCs where Windows uses a non-dark theme, all WinUI controls (text boxes, combo boxes, toggles, checkboxes) inherited the system theme ÔÇö dark text on dark backgrounds, light-colored input fields. Fixed by setting the dark theme on the root element so every control in the app inherits it. This also fixes the shader picker, preset picker, and all other dialogs.

**32-bit-only RenoDX mods showing "No mod available"**
- Games with only a 32-bit addon on the wiki (like Terraria, Sonic Generations, Tomb Raider 2013) were showing "No RenoDX mod available" because the wiki parser stored the `.addon32` URL separately and the UI only checked the 64-bit URL. The 32-bit URL is now promoted to the primary download when no 64-bit variant exists.

---

## v1.7.5

### Changes

**Downloads folder reorganised into subdirectories**
- The `%LocalAppData%\RHI\downloads\` folder is now organised into categorised subdirectories: `shaders/`, `renodx/`, `framelimiter/`, `luma/`, and `misc/`. Existing cached files are automatically migrated on first launch ÔÇö no re-downloads needed. The migration is safe to interrupt and handles locked or duplicate files gracefully.

**Drag-and-drop game auto-selects**
- Dropping an .exe to add a game now automatically selects and scrolls to the new card, so you can interact with it immediately.

**Remember last selected game**
- RHI now remembers which game was selected when you close the app and restores that selection on next launch. If the game is no longer available, it falls back to the first game in the list.

**Copy Report now pastes as a file**
- The Copy Report button now saves a readable `.md` file and places it on the clipboard as a file attachment. Paste directly into Discord ÔÇö no more wall of base64 text. Reports are also saved to `%LocalAppData%\RHI\reports\` for reference.

### Bug Fixes

**Display Commander update detection fixed**
- DC uses a fixed `latest_build` GitHub tag that never changes, so version comparison was always returning "no update". RHI now extracts the real version number (e.g. `0.13.153.3324`) from the release body text, enabling proper update detection.

**DC renamed to dxgi.dll no longer misdetected as ReShade**
- When Display Commander was renamed to `dxgi.dll` via the DLL naming override, the game scan incorrectly identified it as ReShade. The detection logic now checks DC's install record and skips filenames already claimed by DC. This also fixes ReShade not deploying when DC occupies the target filename slot.

**DLL override rename failure on existing files**
- Enabling a DLL naming override could fail with "Cannot create a file when that file already exists" if the target filename was already occupied. The rename now uses a fallback copy-delete-move pattern when the direct delete fails.

---

## v1.7.4

### New Features

**Copy Report button**
- New "Copy Report" button in the overrides panel. Copies a diagnostic code to your clipboard that you can paste in Discord or a GitHub issue. Includes game info, installed components, overrides, and an optional note. A confirmation prompt reminds you to correct overrides before submitting.

**ReShade preset selector**
- New "Select ReShade Preset" button in the overrides panel. Place `.ini` preset files in the `reshade-presets` folder and deploy them to any game with one click.

**Addon lifecycle tied to ReShade**
- Installing ReShade now automatically deploys your selected addons (global or per-game). Uninstalling ReShade removes managed addons from the game folder. Refresh syncs addons to all games with ReShade installed.

### Bug Fixes

**Uninstall buttons appearing grey**
- The Ô£ò remove buttons on component rows could appear grey instead of red until you hovered over them. Now always red when visible.

**Update All tooltip missing Display Commander**
- The Update All button tooltip now includes Display Commander in the list of components.

**Game report showing same values for detected and corrected**
- The Copy Report diagnostic now captures the raw auto-detected bitness and API before user overrides, so the detected vs corrected diff shows actual before/after values.

### Changes

**Overrides panel layout consolidated**
- The separate Manage section has been merged into the overrides panel. Change install folder, Reset folder, Reset Overrides, and Copy Report are now stacked in the left column with a vertical divider. The right column holds the new preset selector. Overall spacing has been tightened to reduce vertical height.

---

## v1.7.3

### Changes

**ReLimiter v3.0.0 ÔÇö new repository**
- ReLimiter is now sourced from [github.com/RankFTW/ReLimiter](https://github.com/RankFTW/ReLimiter/releases). Downloads, updates, and feature guide links all point to the new repo.

**ReLimiter 64-bit only (for now)**
- ReLimiter v3.0.0 is 64-bit only. 32-bit games show the ReLimiter row with strikethrough styling and a disabled install button, matching the mutual exclusion pattern used for Display Commander. The 32-bit code paths remain in place and will activate automatically when a 32-bit release is published.

---

## v1.7.2

### Bug Fixes

**Addon deployment deleting RenoDX mods, ReLimiter, and Display Commander**
- The addon manager's stale file cleanup was removing any `.addon64`/`.addon32` file in game folders that wasn't in the enabled addon set ÔÇö including RenoDX mods, ReLimiter, and Display Commander. Stale removal now only targets files that match a known addon from the official ReShade Addons.ini list. User-placed files and other managed components are never touched.

---

## v1.7.1

### New Features

**ReShade Addon Manager**
- New "ReShade Addons" button in the main header opens a curated addon manager. Browse all available ReShade addons from the official list, toggle them on/off with a single switch. Toggling on downloads the addon automatically, toggling off disables deployment but keeps files cached for later use.

**Global addon deployment**
- Enabled addons are automatically deployed to every game with ReShade installed. When ReShade is installed on a new game, enabled addons are deployed there too. Addons are auto-updated on startup.

**Per-game addon overrides**
- Each game's override panel now has an Addons section with a Global toggle. Switch to per-game mode to pick exactly which addons to deploy for that game, independent of the global set.

**RenoDX DevKit addon**
- The RenoDX DevKit addon is always available in the addon manager alongside the official ReShade addons list.

**First-time addon warning**
- A one-time warning dialog explains that addons are advanced features before opening the addon manager for the first time.

**Override panel layout change**
- Bitness/API moved to the middle row, Shaders and Addons now share the bottom row for a cleaner layout.

**QD-OLED APL Fixer shader pack**
- Added the QD-OLED APL Fixer by mspeedo as a managed shader pack. This shader compensates for the aggressive ABL dimming on QD-OLED screens by applying a measured HDR brightness boost based on real APL behavior.

### Bug Fixes

**Graphics API override not applying on Game Pass and some Steam games**
- The WindowsApps early-return in API detection was running before user overrides and manifest overrides, causing all Game Pass games to show Unknown API regardless of manifest data or user selections. User overrides and manifest overrides now take priority over the WindowsApps filesystem skip. The WindowsApps early-return now falls back to engine-type inference instead of returning Unknown.

**API and bitness override not refreshing detail panel**
- Changing the Graphics API or Bitness override in the overrides panel updated the card properties but didn't rebuild the detail panel. The ReShade install button stayed in DX mode even after switching to Vulkan. Both overrides now trigger a full detail panel rebuild so install buttons reflect the new state immediately.

**Manifest wiki name overrides visible in overrides panel**
- Wiki name mappings injected by the remote manifest (e.g., "Red Dead Redemption 2 (Vulkan)") were showing in the Wiki mod name text box, making it look like the user had set them. The box now only shows user-set mappings. Manifest mappings are hidden from the UI but still work internally for mod matching.

**Reset removing manifest wiki name mappings**
- Clicking Reset Overrides was deleting manifest-injected wiki name mappings from the settings file, breaking mod matching for games like Red Dead Redemption 2 until the next restart. Manifest-origin mappings are now protected from removal and excluded from settings persistence.

**Vulkan ReShade status text styled as link**
- The ReShade status text for Vulkan games showed "Not Installed" with an underline and hand cursor, making it look clickable. Fixed to show "Ready" in plain text matching the style of other components.

### Changes

**Strikethrough on mutually exclusive limiters**
- When ReLimiter is installed, the Display Commander label, status, and install button text are shown with strikethrough styling, and vice versa. This makes it visually clear that only one frame rate limiter can be active per game.

**Removed legacy startup dialogs**
- The one-time Display Commander removal warning and legacy Program Files cleanup dialogs have been removed. These are no longer needed.

**Shader pack dependencies**
- Shader packs can now declare dependencies. Selecting Azen in the shader picker automatically selects smolbbsoop shaders (required by Azen). The dependency is one-way ÔÇö deselecting smolbbsoop independently is still allowed.

**Screenshot path applies to all ReShade INI variants**
- The "Apply to all games" screenshot path button now also writes to reshade2.ini, reshade3.ini, and any other reshade*.ini files in game folders, not just reshade.ini.

---

## v1.7.0

### New Features

**Display Commander reintegrated**
- Display Commander (DC) is back as a frame rate limiter option alongside ReLimiter. Install, reinstall, update, and uninstall DC with one click from the detail panel. DC uses the LITE variant downloaded from GitHub and supports both 32-bit and 64-bit games.

**Mutual exclusion between ReLimiter and DC**
- Only one frame rate limiter can be installed per game at a time. When one is installed, the other's install button is greyed out. Removing one re-enables the other.

**DC update detection and Update All**
- DC is checked for updates on startup alongside other components. When an update is available, the sidebar badge and purple update styling appear. Update All now includes DC for eligible games.

**Automatic archive install from downloads folder**
- The watch folder now detects .zip, .7z, and .rar archives containing "renodx" in the filename. When a matching archive appears (e.g. from a Nexus Mods download), RHI automatically extracts it, finds the addon files inside, and starts the install flow ÔÇö no drag-and-drop needed.

**DC DLL naming override**
- A dedicated DC filename override toggle lets you rename the DC addon file for specific games (e.g. to winmm.dll or d3d9.dll). Works independently from the ReShade override. Each dropdown filters out the other component's current filename to prevent conflicts.

**DC global update exclusion**
- Per-game DC update exclusion toggle in the overrides panel lets you pin a specific DC version on certain games, excluding them from Update All.

**DC detection on game scan**
- RHI detects existing DC installations when scanning game folders, including files with custom DLL override names via tracking records.

**DC INI deploy button**
- Display Commander now has a ­ƒôï button to copy DisplayCommander.ini to the game folder, matching the existing ReShade and ReLimiter INI deploy buttons. The bundled INI is seeded to the app data folder on first launch.

### Bug Fixes

**DLL override disable no longer deletes ReShade**
- Turning off the ReShade DLL naming override now renames the file back to dxgi.dll instead of deleting it.

**Loading overlay stuck after navigating to Settings**
- Going to Settings and back during initial loading no longer leaves the spinner and status text stuck on screen.

### Changes

**Component row order updated**
- The detail panel component order is now: ReShade ÔåÆ RenoDX ÔåÆ ReLimiter ÔåÆ DC. ReLimiter and DC are separated from the rows above by a labeled divider ("Choose one from below").

**Grid view card updated**
- The grid view card flyout now matches the detail panel: same component order (ReShade ÔåÆ RenoDX ÔåÆ separator ÔåÆ ReLimiter ÔåÆ DC), mutual exclusion greying, and "Choose one from below" separator. The overrides flyout now includes DC DLL override, DC update exclusion, bitness override, and API override ÔÇö matching the detail panel feature-for-feature.

**Hand cursor on clickable links**
- Version numbers and author donation links in the detail panel now show a hand cursor on hover when they're clickable.

**DLL naming overrides section updated**
- The section header is now "DLL naming overrides" (plural). The ReShade and DC override toggles are independent ÔÇö each can be enabled without the other. Dropdowns show "Select ReShade DLL name" / "Select DC DLL name" as placeholder text.

**Global update inclusion grid**
- The update inclusion toggles (ReShade, RenoDX, ReLimiter, DC) now use a 2├ù2 grid layout instead of a horizontal row to prevent overflow.

**Faster startup**
- Shader pack checks now run in parallel instead of sequentially, cutting ~10 seconds from launch.
- Game folder shader syncs run in parallel.
- Graphics API detection results are cached to disk ÔÇö subsequent launches skip PE header scanning entirely.
- Xbox/WindowsApps game paths are skipped during API detection (always access-denied, wasted time on retries).
- Full Refresh clears all caches and rescans everything fresh.

**Search box placeholder updated**
- The search box now says "Filter games..." instead of "Search games..." to better reflect its purpose.

**Custom filter auto-select**
- Saving a custom filter now clears the search box and automatically activates the new filter chip.

**Custom filter chips visually distinct**
- Custom filter chips now use a teal color scheme (matching the toolbar buttons) to distinguish them from the built-in filter chips.

**Update button renamed**
- The toolbar Update button now reads "Update All" to clarify its batch operation.

---

## v1.6.9

### New Features

**Skeleton loading screen**
- The app now shows a skeleton loading screen on launch instead of a centered spinner. The sidebar and detail panel areas display animated placeholder shapes that mimic the real layout, so the UI feels responsive from the moment you open it. The placeholders pulse with a subtle shimmer animation and are replaced by real content once loading finishes.

**Universal keyword search**
- The search bar now matches across all game card properties ÔÇö not just game name and maintainer. You can search by store (Steam, GOG, Epic), engine (Unreal, Unity), graphics API (DX11, VLK, DirectX12), bitness (32-bit, 64-bit), mod name, mod author, Luma mod name/author, Vulkan rendering path, and RE Engine/RE Framework games.

**Custom filter chips**
- Save any search query as a named filter chip by clicking the "+" button next to the search bar. Custom chips act as real independent filters ÔÇö click to activate, click again to toggle off, switch between them freely. They combine with the search box and built-in filter chips. Right-click a custom chip to delete it. Custom filters persist across sessions.

**About page**
- All informational content (app description, credits & acknowledgements, disclaimers, and links) has been moved from the Settings page to a dedicated About page. Access it from the Help flyout ÔåÆ About. The Settings page now contains only actionable configuration sections.

### Bug Fixes

**Luma games showing false ReShade update indicator**
- Games in Luma mode no longer show a green update dot on the sidebar card. Luma uses its own bundled ReShade version, so the version mismatch with the latest ReShade is expected and no longer triggers update indicators.

**Luma games included in ReShade global updates**
- Games in Luma mode are now automatically excluded from Update All ReShade operations. This prevents Luma's bundled ReShade from being overwritten by the latest version.

### Changes

**Search now covers display API labels**
- Searching "DX11", "VLK", "OGL", or "DX9" now matches the short labels shown on game cards, in addition to the full enum names like "DirectX11". Dual-API games are also searchable by either API in their detected set.

**RE Engine games searchable**
- Typing "RE Engine" or "RE Framework" in the search bar now finds all RE Engine games.

**Graphics API override simplified to dropdown**
- The six individual API toggle switches have been replaced with a single dropdown selector (Auto, DirectX8, DirectX9, DirectX10, DX11/DX12, Vulkan, OpenGL). "Auto" uses the auto-detected value from PE header scanning.

**Graphics API and Bitness overrides consolidated**
- The Graphics API dropdown is now in the same section as the Bitness dropdown, stacked vertically in the left panel of the overrides row.

**Wiki exclusion moved to wiki section**
- The wiki exclusion toggle has been moved from the right column into the left column, directly below the wiki mod name field. The header text has been removed for a more compact layout.

**Overrides panel condensed**
- The bottom row of the overrides panel has been simplified from a two-column layout to a single stacked section for Bitness and Graphics API.

**Version number in status bar**
- The app version number is now displayed in the bottom-right corner of the status bar, next to the Patch Notes button.

---

## v1.6.8

### Bug Fixes

**DLL naming override dropdown empty**
- The DLL naming override dropdown in the detail panel overrides section was empty after the 1.6.7 layout refactor. The `ItemsSource` binding to the common DLL names list was accidentally dropped when the ComboBox was moved to the top row. The dropdown now shows all available filenames again.

### Changes

**Expanded DLL naming override list**
- Added `d3d10.dll`, `opengl32.dll`, and `ddraw.dll` to the DLL naming override dropdown. The list now covers all standard ReShade API names and common proxy DLLs, ordered by usage: API names first (`dxgi.dll`, `d3d9.dll`, `d3d10.dll`, `d3d11.dll`, `d3d12.dll`, `opengl32.dll`), then proxy names (`dinput8.dll`, `version.dll`, `winmm.dll`, `ddraw.dll`), then edge cases.

**Auto-update now checks /releases/latest first**
- The self-update check now queries the GitHub `/releases/latest` endpoint on the RHI repo as the primary source, supporting the new per-version release tags (e.g. "RHI 1.6.8"). Falls back to the fixed `RHI` tag and then the legacy RDXC repo if needed.

**64-bit badge on detail panel**
- 64-bit games now show a "64-bit" badge on the detail panel info line, matching the existing "32-bit" badge for 32-bit games. The badge updates live when the bitness override is changed.

**Graphics API override tooltip**
- The "Graphics API" label in the overrides panel now has a tooltip explaining the priority rule: only one API drives the ReShade DLL filename at a time (DX11/12 ÔåÆ Vulkan ÔåÆ OpenGL ÔåÆ DX10 ÔåÆ DX9 ÔåÆ DX8), and user overrides take precedence over manifest and auto-detected values.

**Screenshot folder Browse and Open buttons**
- The screenshot path setting now has an inline folder icon inside the text box to open a folder picker, and an "Open" button to launch the configured screenshot folder in Explorer.

---

## v1.6.7

### New Features

**Per-game bitness override**
- You can now manually override the auto-detected bitness (32-bit / 64-bit) for any game. A new Bitness dropdown in the overrides panel lets you choose Auto, 32-bit, or 64-bit. This addresses cases where PE header analysis misidentifies a game's architecture. The override persists across restarts and is cleared by Reset Overrides.

**Per-game graphics API override**
- You can now manually toggle which graphics APIs a game uses. A new Graphics API section in the overrides panel shows toggle switches for DirectX 8, DirectX 9, DirectX 10, DX11/DX12, Vulkan, and OpenGL. This is useful for correcting misdetected APIs or suppressing one API on a dual-API game. Overrides persist across restarts and are cleared by Reset Overrides.

### Bug Fixes

**Update dialog still said "RDXC"**
- The self-update notification dialog now correctly says "A new version of RHI is available" instead of the old RDXC branding.

**RenoDX DevKit addon deleted during mod install**
- Installing a RenoDX mod via drag-and-drop or toggling off Luma mode was deleting `renodx-devkit.addon64` and `renodx-devkit.addon32` from the game folder. DevKit files are now exempt from addon cleanup.

### Changes

**Overrides panel layout condensed to 3 rows**
- The overrides panel has been reorganized from 5 rows down to 3. The DLL naming override has been moved into the top row alongside wiki exclusion. The Rendering Path dropdown has been removed since the new API toggles make it redundant. Bitness and API overrides share a new row between shaders/global updates and the reset button.

**API toggles use horizontal card layout**
- The graphics API toggles are displayed in a horizontal wrapping layout with bordered cards, matching the style of the Global update inclusion toggles.

**DLL naming override text simplified**
- The toggle off-text now reads "Override ReShade filename" instead of "Using default filenames". The "ReShade filename" header above the dropdown has been removed to save vertical space.

---

## v1.6.6

### Bug Fixes

**RE Framework now downloads the correct game-specific build**
- Each RE Engine game now downloads its own RE Framework build (e.g. DMC5.zip for Devil May Cry 5, RE4.zip for Resident Evil 4) instead of using a single generic download for all games. Game names with trademark symbols (e.g. Street FighterÔäó 6) are now matched correctly.

**Drag-and-drop no longer deletes third-party ReShade addons**
- Installing a RenoDX mod via drag-and-drop was deleting all non-ReLimiter `.addon64`/`.addon32` files from the game folder, including third-party addons like `ShaderToggler.addon64`. The cleanup now only removes `renodx-` prefixed files.

**Corrupt cached addon files no longer reused**
- The PE validation check now rejects files under 100 KB, preventing truncated or corrupt downloads (e.g. the 48 KB Unity generic mod) from being cached and reused. Users with a bad cache should delete `%LocalAppData%\RHI\downloads\` to force a fresh download.

**Add Game folder picker crash on some systems**
- The WinUI folder picker could throw a COMException on certain Windows configurations when adding a game manually. The picker now falls back to the native COM file dialog if the standard picker fails.

**Drag-and-drop version number now reads from correct path**
- Version numbers for mods installed via drag-and-drop were not displayed when the game uses a custom `AddonPath` in `reshade.ini`. The version is now read from the actual addon deploy folder.

**Discord/Nexus mod version numbers now displayed**
- External-only games (Discord/Nexus mods) were hardcoded to show "Installed" instead of the version number. They now show the version from PE info when available, matching wiki-installed mods.

**Version fallback for addons without PE version info**
- Addon files that lack embedded PE version resources (common with Discord-distributed mods) now display the file's last-modified date in `YY.MMDD.HHMM` format as a fallback, matching the RenoDX version style.

**ReLimiter version number not shown immediately after install**
- The ReLimiter version number was showing "Installed" instead of the version until a refresh. The install flow now falls back to reading the version from the metadata file when the remote version hasn't been fetched yet.

### Changes

**Component version numbers centered in detail panel**
- The version numbers for RE Framework, ReShade, ReLimiter, RenoDX, and Luma are now horizontally centered in the detail panel, aligning them on a common vertical axis.

**Consistent purple update styling across all components**
- ReLimiter and RE Framework install buttons and status text now turn purple when an update is available, matching the existing ReShade and RenoDX styling. Previously ReLimiter used amber text with blue buttons, and RE Framework used static blue buttons.

**ReLimiter available in Luma mode**
- ReLimiter can now be installed and managed when a game is in Luma mode. The ReLimiter row and status dot are always visible. When switching a game out of Luma mode, ReLimiter is automatically uninstalled alongside the Luma files.

**Status messages auto-fade after 4 seconds**
- Install, update, and removal confirmation messages now automatically disappear after 4 seconds. Error messages remain visible. Multiple messages across different components fade independently.

**Colored status messages**
- Install/update success messages now display in green with a Ô£à icon. Removal messages display in red with a Ô£û icon. Progress and default messages remain blue.

---

## v1.6.5

### New Features

**RE Framework support**
- RHI now detects RE Engine games (Monster Hunter Wilds, Resident Evil series, Devil May Cry 5, Street Fighter 6, Dragon's Dogma 2, Pragmata, etc.) by checking for `re_chunk_000.pak` in the game directory. Detected games display an "RE Engine" badge.
- One-click install, update, and uninstall of RE Framework (`dinput8.dll`) from praydog's GitHub nightly releases. Each game downloads its own game-specific build (e.g. DMC5.zip for Devil May Cry 5, RE4.zip for Resident Evil 4). The DLL is cached per game so reinstalls are instant.
- Version tracking and auto-update checking ÔÇö RHI fetches the latest nightly release tag on startup and flags installed copies that are behind. RE Framework is included in the Update All batch operation.
- RE Framework status dot, install row, and progress indicator appear on game cards and the detail panel for RE Engine games, following the same layout as ReShade and ReLimiter. The version number is a clickable link to the REFramework nightly releases page.
- Install All on RE Engine games now chains: RenoDX ÔåÆ RE Framework ÔåÆ ReShade.

**Screenshot path settings**
- A new Screenshots section in Settings lets you set a global screenshot save path that is written to all managed `reshade.ini` files as `[SCREENSHOT] SavePath=<path>`.
- An optional per-game subfolder toggle appends the game name to the path, so each game's screenshots go to their own folder.
- Click Apply to update all existing `reshade.ini` files at once. Newly deployed INIs also include the configured path automatically.

**URL drag-and-drop install**
- You can now drag a Discord or browser link to an `.addon64`/`.addon32` file directly onto the RHI window to download and install it. RHI validates the URL, downloads the file to the local cache with a progress dialog, verifies it's a valid PE binary, and routes it through the standard game-matching and install flow.
- Also supports dragging `.url` shortcut files ÔÇö RHI parses the URL from inside and processes it the same way.

### Bug Fixes

**RE Engine games not detected on existing installs**
- Games that were cached before the RE Engine detection was added are now re-scanned automatically, so the RE Framework row appears without needing a manual refresh.

**Update All button staying purple after all updates complete**
- The Update All button could remain purple after completing all updates because the "any updates available" check was counting hidden games, games with DLL overrides, games with missing install paths, and Vulkan games (whose ReShade uses the global layer). These cards are skipped by Update All but were still contributing to the button state. The check now uses the same eligibility criteria as the actual update operation.

**Cached addon files corrupted by HTML error pages**
- The download-based update check for GitHub Pages-hosted mods (generic Unity, UE-extended) could save an HTML error page as the cached addon file when the CDN returned a 200 OK with HTML content instead of the binary. This resulted in ~48KB files replacing multi-MB addons in the download cache. Downloads are now validated with a PE signature check before caching, and corrupted cache entries are automatically deleted so a fresh download is triggered.

**RE Framework status color mismatch**
- The RE Framework version text was using a different shade of green than all other components. The installing and update-available colors were also inconsistent. All RE Framework status colors now match ReShade, ReLimiter, and RenoDX.

### Changes

**RenoDX version number now shows full build info**
- The RenoDX version display now includes the hour/minute build number and drops the leading `0.` and century digits from the year. For example, `0.2026.0325.2215` is now shown as `26.0325.2215`.

**Vulkan ReShade button icons**
- The "Reinstall Vulkan ReShade", "Install Vulkan ReShade", and "Install Vulkan Layer" buttons now show the Ôå║ and Ô¼ç action icons matching all other component buttons.

**Toggle switch labels removed in grid view overrides**
- Removed the "Yes"/"No" text from the Global update inclusion toggle switches in the grid view overrides flyout to prevent text overflow.

---

## v1.6.4

### New Features

**32-bit ReLimiter support**
- RHI now installs the correct ReLimiter addon based on game bitness. 32-bit games receive `relimiter.addon32` and 64-bit games continue to use `relimiter.addon64`. Each variant is cached separately so installs don't interfere with each other.

**Automatic OpenGL ReShade DLL naming**
- Games detected with OpenGL as their only graphics API now have ReShade installed as `opengl32.dll` automatically, instead of the default `dxgi.dll`. User and manifest DLL overrides still take priority.

**Automatic DX9 ReShade DLL naming**
- Games detected with DirectX 9 now have ReShade installed as `d3d9.dll` automatically, instead of the default `dxgi.dll`. DX9 takes precedence when multiple APIs are detected. User and manifest DLL overrides still take priority.

---

## v1.6.3

### Bug Fixes

**Author badges now split on "and" separator**
- Games with multiple authors joined by "and" (e.g. "Jon and Forge") were displayed as a single badge with no donation link. Author strings are now split on both "&" and "and", so each author gets their own clickable badge linking to their Ko-fi page.

**Update All button no longer lights up for excluded games**
- The global Update All button was turning purple when updates were available for games excluded from Update All via overrides. The button now only reflects updates that would actually be acted on. Toggling an exclusion also immediately updates the button color.

**Legacy install folder cleanup**
- On first launch, RHI checks for old RenoDXCommander and UPST folders in Program Files from previous installs. If found, a dialog offers to remove them. Choosing "Keep" or "Remove" writes a marker so the prompt only appears once.

**Red Dead Redemption 2 dedicated ReShade INI**
- Red Dead Redemption 2 now uses a dedicated `reshade.rdr2.ini` configuration file instead of the generic `reshade.vulkan.ini`. When ReShade is installed for RDR2, this game-specific INI is deployed as `reshade.ini` in the game folder, ensuring optimal settings for RDR2's Vulkan renderer. The overlay key is set to END to avoid conflicts with RDR2's default keybindings.

---

## v1.6.2

### Highlights

**Rebranded to ReShade HDR Installer (RHI)**
- After much feedback and consideration, the app has been rebranded from Ultra Plus Support Tools (UPST) to ReShade HDR Installer (RHI). The executable, window title, settings directory, and all user-facing references now use the RHI name. Existing `%LocalAppData%\UPST` and `%LocalAppData%\RenoDXCommander` data folders are automatically migrated to `%LocalAppData%\RHI` on first launch ÔÇö no manual action needed.

**Clickable author donation links**
- Mod author badges in the detail panel are now clickable links to the author's Ko-fi donation page. Supported authors: ShortFuse, Jon (oopydoopy), Forge, Voosh (NotVoosh), and Musa. Authors without a known donation page remain as regular non-clickable badges. If your donation link is missing, reach out on Discord and it will be added.

**Ultra Limiter rebranded to ReLimiter**
- All references to "Ultra Limiter" throughout the app have been renamed to "ReLimiter". The addon file is now `relimiter.addon64`. Existing `ultra_limiter.addon64` files in game folders are automatically replaced on update.

### Bug Fixes

**Grid view component row alignment**
- Fixed the "ReLimiter" name and "Installed" / "Update" status text overlapping in the grid view install popout. The name column width has been increased so all three component rows (ReShade, ReLimiter, RenoDX) align consistently.

**Drag-and-drop no longer deletes ReLimiter**
- Installing a RenoDX addon via drag-and-drop or double-click no longer removes `relimiter.addon64` from the game folder. The addon cleanup now excludes ReLimiter files alongside Display Commander files.

### Changes

**Component status text now clickable**
- The "Installed" status text for ReShade now links to [reshade.me](https://reshade.me). The "Installed" status text for RenoDX now links to the game's wiki page (or the mods list if no per-game page exists). ReLimiter's existing link to the feature guide is unchanged. Applies to both detail view and grid view popout.

**Installed filter now shows ReShade games**
- The Installed filter tab now shows games with ReShade installed, rather than games with RenoDX or Luma installed. This better reflects the typical workflow where ReShade is the base component that most users have deployed.

**Auto-update now checks RHI repo**
- The self-update check now looks for `RHI-Setup.exe` at the new `RankFTW/RHI` GitHub repo first, falling back to the legacy `RankFTW/RenoDXChecker` repo with `RDXC-Setup.exe` if the new endpoint has no release.

**ReLimiter global update exclusion toggle**
- A new "ReLimiter" toggle has been added to the per-game Global update inclusion section in both the detail view overrides panel and the grid view overrides flyout, alongside the existing ReShade and RenoDX toggles. When toggled off, the game is excluded from Update All for ReLimiter.
- The update badge (green dot) in the sidebar now respects all three exclusion flags ÔÇö if a component's update is excluded, it no longer contributes to the badge.
- Reset Overrides now also resets the ReLimiter exclusion toggle back to included.

---

## v1.6.1

### Bug Fixes

**ReLimiter update detection fixed**
- Fixed UL updates not being detected when a new version was published on GitHub. The update check was comparing the remote file against a metadata file that had already been overwritten with the new version's hash during a previous check, so it always reported "no update." The check now hashes the actual installed file from a game folder as the ground truth reference, ensuring updates are always detected regardless of metadata state.
- Fixed a file locking bug where the SHA-256 stream was not disposed before the temp file cleanup, causing "file in use" errors in the session log.
- Added a GitHub Releases API pre-check that detects size-only changes instantly without downloading the full file. When sizes match, the full download + hash comparison still runs to catch same-size content changes.
- Cache-busting headers (`Cache-Control: no-cache`) are now sent on both the API and download requests to bypass GitHub CDN caching.

**ReLimiter update badge in sidebar**
- Games with a pending UL update now show the green update dot in the sidebar game list, matching the existing behavior for RenoDX and ReShade updates.

**Update All button includes ReLimiter**
- The global Update button now also updates ReLimiter for all games with a pending UL update. The tooltip has been updated to reflect this.
- The Update All button now turns purple when UL updates are available, not just for RenoDX and ReShade updates.

### Changes

**ReLimiter update visuals**
- The UL status dot stays green when an update is available (previously turned orange), keeping it consistent with the "still installed" state.
- The UL status text now shows "Update" instead of "Update Available" for a cleaner look.
- The UL update no longer overwrites the install metadata during the check ÔÇö metadata is only updated when the user actually installs the update, so the update badge persists across app restarts until acted upon.
- When the update check pre-caches the new file, clicking Update uses the cached file directly instead of re-downloading.

---

## v1.6.0

### Highlights

**Rebranded to UPST**
- The app has been rebranded from RenoDX Commander (RDXC) to Ultra Plus Support Tools (UPST). The executable, window title, settings directory, and all user-facing references now use the UPST name.

**ReLimiter support**
- UPST can now install and manage the ReLimiter addon (`relimiter.addon64`). A new UL component row appears in the install flyout and detail panel alongside RenoDX, ReShade, and Luma, with install, reinstall, and uninstall buttons, status dot, and progress indicator.
- ReLimiter is automatically detected in game folders on refresh.
- The UL row is hidden when a game is in Luma mode.
- ReLimiter is downloaded from GitHub on demand rather than bundled with the app, keeping the install size smaller.
- Update detection compares file size and SHA-256 hash against the remote release. When an update is available, the status dot turns orange and the button shows "Update".
- For a full list of ReLimiter features and settings, see the [ReLimiter Feature Guide](https://github.com/RankFTW/ReLimiter?tab=readme-ov-file#relimiter--comprehensive-feature-guide).
- A bundled `relimiter.ini` is seeded to the UPST inis folder on first launch. A ­ƒôï button on the ReLimiter row copies it to the game folder, matching the existing ReShade INI workflow.

**ReLimiter "Installed" link**
- The green "Installed" text for ReLimiter is now a clickable link that opens the ReLimiter feature guide on GitHub.

**Display Commander removed**
- All Display Commander functionality has been removed from the codebase. DC install/uninstall, DC Mode toggle, DC DLL picker, DC per-game overrides, DC shader deployment, DC update operations, DC status indicators, and all DC-related UI elements have been stripped.
- ReShade is now always installed as the standard filename (`dxgi.dll` or the DLL override name) ÔÇö the DC-mode filenames (`ReShade64.dll` / `ReShade32.dll`) are no longer used.
- The DC Legacy Mode toggle in Settings has been removed.
- A one-time warning dialog appears on first launch of v1.6.0 advising users to manually remove any old Display Commander files from game folders via the Browse button.

### Other Changes

**Lilium shader pack now optional in global selection**
- The Lilium HDR shader pack is no longer locked in the global shader picker. You can now untick it if you don't want it deployed globally. Lilium is still selected by default on fresh installs.

**Overrides layout redesign**
- The Global Shaders toggle and DLL naming override are now displayed side-by-side on the same row with a vertical divider, in both the detail view overrides panel and the grid view overrides flyout.
- The Global update inclusion and Wiki exclusion row now uses equal-width columns so the vertical divider sits centered.

**Version number now from assembly**
- The version displayed in the Settings menu is now read from the assembly version rather than a hardcoded string, ensuring it always matches the build.

**Drag-and-drop game selection fallback**
- When dragging an addon or archive onto UPST and the filename doesn't match any game, the game picker now defaults to the currently selected game in the sidebar instead of showing an empty selection.

**False update detection fix**
- Fixed mods hosted on GitHub Pages falsely showing "Update Available" when the remote file hadn't changed. The update check now compares the remote hash against the stored install-time hash instead of re-hashing the local file, which could differ if the file was touched by the game or ReShade.

**Obsolete specs cleaned up**
- The `dc-legacy-toggle` and `dc-mode-redesign` spec directories have been removed.

---

## v1.5.5

### New Features

**DC Legacy Mode toggle**
- Display Commander is no longer available for new downloads. A new "DC Legacy Mode" toggle in Settings lets existing DC users restore full DC functionality. When off (the default), all DC-related UI, install operations, and update operations are hidden throughout the app. Existing DC installations are preserved ÔÇö toggling off does not uninstall anything.

**Lilium shaders always included in global selection**
- The Lilium HDR shader pack is now always selected and locked (greyed out) in the global shader picker. It can still be deselected in per-game shader overrides. New installs and existing installs that were missing Lilium will have it added automatically.

### Changes

**DC UI hidden by default**
- The DC component row, DC status dot, DC install/uninstall buttons, DC progress indicators, DC Mode settings, DC per-game overrides, DC DLL override filename, DC references in the About section, and DC references in the Update button tooltip are all hidden when DC Legacy Mode is off.

**Refresh on DC Legacy Mode toggle**
- Toggling DC Legacy Mode now triggers a full UI refresh so all cards and panels update immediately.

---

## v1.5.4

### New Features

**Filter mode remembered across sessions**
- Your selected filter tab (e.g. Unity, Installed, Favourites) is now saved and automatically restored when you reopen the app, so you no longer have to reselect it every launch.

**Install button icons**
- The card install button and external download/redownload buttons now show action icons (Ô¼ç install, Ôå║ reinstall/manage, Ô¼å update) matching the per-component buttons in the detail panel.

**DC Mode redesigned ÔÇö toggle + DLL picker**
- DC Mode has been redesigned from a 3-state integer cycle (Off / dxgi.dll / winmm.dll) to a simple On/Off toggle with a DLL filename picker. You can now select any proxy DLL name from a dropdown or type a custom filename.
- Per-game DC Mode overrides have been simplified to three options: Global, Off, and Custom. Custom lets you pick a per-game DLL filename independently of the global setting.
- Legacy settings from previous versions are automatically migrated on first launch.

**Toolbar redesign**
- All toolbar buttons now share a consistent teal accent style. Buttons are grouped into three sections separated by vertical dividers: Refresh and Global Shaders | Update | Help, View toggle, and Settings.
- The Update button is now dim by default and lights up purple when any game has an update available.

**Addon auto-detection**
- UPST now watches your Downloads folder (configurable in Settings) for new `renodx-*.addon64` / `.addon32` files and automatically prompts you to install them.
- Double-clicking an addon file in Explorer opens UPST and triggers the install flow. If UPST is already running, the file is forwarded to the existing instance via named pipe.
- Drag-and-drop, file association, and archive extraction all enforce the `renodx-` filename prefix to avoid triggering on unrelated addon files.

**AddonPath support**
- Addon installs (RenoDX and Display Commander) now respect the `AddonPath` setting in `reshade.ini`. If the `[ADDON]` section contains an `AddonPath=` line, addons are deployed to that folder instead of the game root. Relative paths are resolved against the game directory. Uninstall, update detection, and addon scanning all check the same path.

**Ko-fi link in Help menu**
- The Help flyout now includes a Ko-fi link.

### Bug Fixes

**Grid view install flyout not working**
- Fixed the install/manage flyout on grid view cards being empty when clicked. The flyout now correctly shows all component rows (RS, DC, RDX, Luma) with install, reinstall, and uninstall buttons.

**Grid view overrides flyout missing DC Custom DLL selector**
- The per-game overrides flyout in grid view was missing the DC Custom DLL filename picker, the vertical divider between DC Mode and Shaders, and the three-column layout. These now match the detail view's overrides panel.

**Grid view install flyout crash on external-only games**
- Fixed a crash (`FormatException: Could not find any recognizable digits`) when opening the install flyout on games without a RenoDX wiki mod. The color parser now handles the `"transparent"` keyword.

**ReShade version not shown in DC mode**
- The ReShade status label now shows the installed version number even when DC mode is active, matching the behavior outside DC mode.

**DC DLL picker not waiting for Enter before renaming**
- Fixed the editable DC DLL filename picker triggering file renames on every keystroke while typing a custom name. The picker now only commits the change when you press Enter, select from the dropdown, or leave the field. Applies to both the global and per-game pickers.

**Update detection for clshortfuse.github.io mods**
- Fixed mod update detection for all mods hosted on GitHub Pages (`*.github.io`), including the Generic Unity and Generic Unreal addons. These URLs were falling through to the HEAD Content-Length comparison path, which is unreliable on GitHub Pages because the CDN may return compressed transfer sizes. They are now routed through the download-based comparison path, matching the existing behavior for `marat569.github.io` mods.

**Same-size mod updates not detected**
- Fixed the download-based update check failing to detect updates when the new version of a mod has the same file size as the installed version. The check now compares SHA-256 hashes when file sizes match, so content changes are always detected regardless of size.

**Orphaned temp files in download cache**
- Fixed update check temp files (`.update_check_*.tmp`) accumulating in the download cache folder after crashes or interrupted checks. Stale temp files are now cleaned up automatically at the start of each update check.

---

## v1.5.2

### Bug Fixes

**Update All removing shaders from game folders**
- Fixed the Update All ReShade and Update All DC operations removing managed shaders from every game folder they touched. The batch update was not passing the per-game shader selection to the install methods, causing the shader sync to interpret a null selection as "remove all shaders". Shaders are now preserved correctly during batch updates using each game's global or per-game shader selection.

---

## v1.5.1

### New Features

**Vulkan ReShade lightweight install**
- When the global Vulkan implicit layer is already registered, clicking the ReShade install button on a Vulkan game now performs a fast lightweight deploy (INI + footprint + shaders only) without requiring administrator privileges or reinstalling the layer.
- The install flyout and detail panel show context-aware labels: "Vulkan RS" / "Install Vulkan ReShade" when the layer is present, "Reinstall" / "Reinstall Vulkan ReShade" when already active, and "Install" / "Install Vulkan Layer" when the layer is absent.

**Installed indicator for Nexus/Discord mods**
- External mods downloaded from Nexus Mods or Discord now show a green "Installed" status label next to the Redownload button in the detail panel when the mod is installed.

### Improvements

**Faster startup**
- The app now displays game cards significantly faster by deferring ReShade staging and shader deployment to the background. Previously, the ReShade download/staging task and shader pack sync blocked the UI until they completed. Cards now appear as soon as game detection and mod matching finish, while ReShade staging and shader deployment continue silently in the background.

**"No RenoDX mod available" moved to install button**
- Removed the large purple "No RenoDX mod available" box from game cards. The install button now displays "No RenoDX mod available for this game" inline when no mod exists, keeping the card layout cleaner. If a mod is manually installed, normal install/reinstall labels are shown instead.

### Bug Fixes

**Vulkan ReShade blocked by DC Mode when DC Mode is off**
- Fixed Vulkan games incorrectly showing "ReShade cannot be installed while DC Mode is active" when the global DC Mode was on but the game had no per-game override. The install handler now applies the same Vulkan DC Mode exemption used everywhere else in the app.

**Update All placing dxgi.dll in Vulkan game folders**
- Fixed the Update All ReShade batch operation incorrectly running the standard DX ReShade install path for Vulkan games, which copied a `dxgi.dll` into the game directory. Vulkan games are now excluded from Update All ReShade since they use the global implicit layer and don't have per-game DLLs.

**ReShade "unable to save configuration" error**
- Fixed ReShade showing "Unable to save configuration and/or current preset" errors for `reshade.ini` and `ReShadePreset.ini` in games where these files were deployed by UPST. The INI writer was emitting a UTF-8 BOM (byte order mark) that ReShade's native parser cannot handle. INI files are now written as plain UTF-8 without BOM.

---

## v1.5.0

### Bug Fixes

**Global shader selection not persisting across restarts**
- The global shader deploy mode was not being saved to the settings file, causing the shader selection to reset to empty every time the app was opened. The `SaveSettingsToDict` method now correctly writes the `ShaderDeployMode` key.

**Per-game shader mode overrides resetting on load**
- Per-game shader mode overrides (Off, Minimum, All, User) were being filtered out during settings load, causing them to reset. Only "Select" mode was being preserved. All valid shader modes are now loaded correctly.

**Shader selection not saved after choosing packs**
- Clicking Deploy in the global shader selection picker did not persist the selection to disk. Settings are now saved immediately after the picker closes.

---

## v1.4.9

### New Features

**Graphics API detection**
- UPST now scans game executables using PE header import table analysis to detect which graphics APIs a game uses: DirectX 11, DirectX 12, Vulkan, and OpenGL.
- API badges are displayed on game cards showing detected rendering paths (e.g. DX12, VLK).
- Multi-exe scanning ÔÇö all `.exe` files in the install directory and common subdirectories (bin, binaries, x64, win64, etc.) are scanned, so games like Baldur's Gate 3 with multiple executables are detected correctly.
- Manifest API overrides ÔÇö the remote manifest supports comma-separated API tags (e.g. `"DX12, VLK"`) for games like Red Dead Redemption 2 that load Vulkan dynamically and can't be detected via PE imports alone.

**Multi-API labels on game cards**
- Game cards now show all detected graphics APIs for dual-API games (e.g. "DX11/12 / VLK" for Red Dead Redemption 2) instead of only the primary API.
- Only valid multi-API combos are shown: DX11/12 + VLK. Legacy APIs (OGL, DX9, DX10) only appear alone.

**Vulkan ReShade support**
- Full Vulkan implicit layer support for ReShade. UPST can now install ReShade as a global Vulkan layer via the Windows registry (`HKLM\SOFTWARE\Khronos\Vulkan\ImplicitLayers`), enabling ReShade injection for Vulkan-rendered games.
- Bundled `ReShade64.json` Vulkan layer manifest with correct `device_extensions` and `disable_environment` fields, deployed alongside the ReShade DLL.
- Vulkan layer install/uninstall buttons on game cards for games with detected Vulkan support.
- Dual-API game support ÔÇö games detected with both DirectX and Vulkan show a rendering path toggle, allowing you to choose which path ReShade targets.

**Vulkan-specific ReShade INI**
- A dedicated `reshade.vulkan.ini` configuration file is now bundled and deployed for Vulkan ReShade installs.
- Includes depth buffer preprocessor definitions tuned for Vulkan rendering.
- The ­ƒôï INI button deploys both `reshade.ini` and `reshade.vulkan.ini` when a game has Vulkan support.

**Vulkan ReShade footprint tracking**
- UPST now places a footprint file (`RDXC_VULKAN_FOOTPRINT`) in game folders when Vulkan ReShade is installed, enabling managed shader deployment to Vulkan games the same way it works for DLL-injected ReShade games.
- The footprint is automatically removed when Display Commander is installed and restored when DC is uninstalled from a Vulkan game.

**Per-game Vulkan ReShade uninstall**
- A Ô£ò uninstall button is now shown for Vulkan games that have `reshade.ini` deployed, allowing you to remove Vulkan ReShade artifacts from a specific game folder without affecting the global Vulkan layer.

**Vulkan ReShade status detection**
- Vulkan games now show the ReShade version number with "(Vulkan)" in the detail panel when `reshade.ini` is present, matching the green installed styling of DLL-based ReShade games.

**Vulkan DC Mode defaults**
- Vulkan games now default to DC Mode Off unless explicitly overridden by the user or manifest.
- The DC Mode dropdown in overrides shows "Exclude (Off)" for Vulkan games and updates automatically when switching rendering path to Vulkan.

**Rendering path switch cleanup**
- Switching a dual-API game from DirectX to Vulkan rendering path now automatically uninstalls DX ReShade, Display Commander, reshade.ini, and managed shaders from the game folder.

**Shader selection picker**
- A new shader selection picker lets you choose exactly which shader packs to deploy from all available packs.
- The selection is saved globally and restored across app restarts.
- Per-game shader overrides allow different games to use different subsets of shader packs.

**Auto-save overrides**
- All override controls now save immediately when changed ÔÇö no more Save button. TextBoxes save on Enter, ComboBoxes on selection, ToggleSwitches on toggle.
- The Save Overrides button and hint text have been removed.
- Reset Overrides persists all defaults immediately.

**Per-game shader deploy on confirm**
- Selecting per-game shaders and clicking Deploy now immediately deploys the chosen shaders to the game folder without needing a refresh.
- The Confirm button has been renamed to Deploy to match the global shader workflow.

**Seamless refresh**
- Refresh is now invisible after the initial boot. Pressing Refresh updates everything in the background without showing a loading spinner or blanking the UI. Game cards stay visible throughout.
- Game renames and other actions that trigger a refresh also happen seamlessly.

**DLL name conflict prevention**
- The ReShade and DC filename dropdowns now cross-filter: selecting a name in one box removes it from the other's dropdown, preventing both from being set to the same DLL name.
- Saving is blocked if both names match.

**Startup shader deployment**
- On launch, UPST now ensures shader packs are fully downloaded before syncing shaders to all installed game folders. Games with ReShade or DC installed will have the correct global or per-game shaders deployed automatically, even if they were installed by an older version that didn't deploy shaders.

**Wiki exclusion toggle**
- A new per-game toggle in overrides lets you exclude a game from RenoDX wiki lookups, useful for games that share a name with an unrelated wiki entry.

### Bug Fixes

**Drag-and-drop not working**
- Fixed drag-and-drop of game executables and archives not functioning in the unpackaged WinUI 3 app. Drag-and-drop now uses Win32 shell `WM_DROPFILES` handling.
- Also added UIPI bypass so drag-and-drop works when UPST is running as administrator.

**Shaders not deployed to game folders after Display Commander removal**
- Games with ReShade installed but no Display Commander were left with an empty `reshade-shaders\` folder after DC was uninstalled. Refresh and Deploy Shaders now correctly detect this scenario and deploy shaders to the game folder.

**Name reset button not persisting**
- The Ôå® Reset button next to the game name and wiki name fields now correctly persists the rename back to the original store name and clears the wiki mapping, instead of only resetting the text boxes visually.

### Changes

**DLL override dropdown auto-save**
- Selecting a DLL name from the dropdown now persists immediately, in addition to the existing Enter key save.

**Global update inclusion and wiki exclusion inline layout**
- The Global update inclusion toggles and Wiki exclusion toggle are now displayed in a single inline row with a vertical divider, saving vertical space in the overrides panel.

---

## v1.4.7

### Bug Fixes

**Global shader button not updating in Settings**
- Clicking the shader mode button in the Settings panel cycled the mode internally but the button label and colours did not update visually. The SettingsViewModel was raising PropertyChanged on its own properties, but the XAML bindings target MainViewModel which was not forwarding those notifications. MainViewModel now subscribes to SettingsViewModel.PropertyChanged and re-raises the shader button property changes so the UI reflects the current mode immediately.

**DC install/update overwriting shared shader folder with per-game mode**
- Installing or updating Display Commander for any game was syncing the DC AppData shader folder using that game's per-game shader mode override instead of the global shader mode. Because the DC shader folder is shared across all DC-mode games, a per-game override of "Off" would wipe shaders for every other DC-mode game, and a per-game override of "All" would deploy extra shaders globally. The DC folder sync now always uses the global shader deploy mode, while per-game overrides continue to apply only to standalone ReShade game folders.

### Changes

**Codebase optimisation**
- Shared UI helpers extracted (UIFactory, ResourceKeys) to eliminate duplicated brush/style creation across CardBuilder, DetailPanelBuilder, and DragDropHandler.
- Five new service interfaces introduced (IGameInitializationService, IUpdateOrchestrationService, IDllOverrideService, IGameNameService, ILiliumShaderService) to decouple MainViewModel from concrete implementations.
- Property notification deduplication ÔÇö SetProperty guards added to prevent redundant UI updates.
- String comparisons standardised to OrdinalIgnoreCase across all filter and lookup paths.
- Async best practices applied ÔÇö ConfigureAwait(false) on non-UI awaits, SafeFireAndForget extension for fire-and-forget tasks.
- Error handling normalised ÔÇö ~180 CrashReporter.Log calls standardised with consistent tag format.
- Retry logic added to settings and library file writes to handle file contention.
- Per-platform exception isolation in game detection prevents one platform's failure from blocking others.
- ManifestService null-safety hardened for malformed remote JSON.
- GameDetectionService optimised with configurable max scan depth and engine detection caching.
- Memory management improvements ÔÇö HttpClient lifetime audit, brush caching, PropertyChanged cleanup.
- WrapPanel measure/arrange optimised to reduce layout passes.
- DragDropHandler hardened with upfront extension validation.
- XML documentation added to all new public APIs.
- 11 property-based tests added covering filter correctness, batch collection, drag-drop validation, and property notification.

---

## v1.4.6

### New Features

**Per-component Update All toggles**
- The single "Exclude from Update All" toggle in the Overrides section has been replaced with three separate toggle switches for ReShade, Display Commander, and RenoDX.
- Each toggle independently controls whether the game is included in bulk updates for that component. All three default to On (included).
- Toggles are displayed horizontally under a "Global update inclusion" header, each in its own bordered card for clarity.
- Legacy settings are automatically migrated ÔÇö if you previously excluded a game from Update All, all three toggles will start excluded.
- Applies to both Detail View and Grid View overrides.

**Reset Overrides button**
- A new "Reset Overrides" button in the Overrides section resets all per-game settings back to their defaults in one click: game name, wiki name, DC mode, shader mode, DLL override, all three update toggles, and wiki exclusion.
- Positioned on the left side opposite the Save Overrides button.

**Per-session logging**
- A new session log file is created every time UPST starts, named with a timestamp (e.g. `session_2025-03-14_12-30-00.txt`).
- All activity is logged to the session file automatically ÔÇö no need to enable Verbose Logging first.
- Old session logs are automatically pruned to keep a maximum of 10 on disk.

**DLL override dropdown suggestions**
- The ReShade and DC filename text boxes in the DLL naming override section are now dropdown combo boxes with a clickable arrow that shows a predefined list of common DLL names: `dxgi.dll`, `d3d11.dll`, `dinput8.dll`, `version.dll`, `winmm.dll`, `d3d12.dll`, `xinput1_3.dll`, `msvcp140.dll`, `bink2w64.dll`, `d3d9.dll`.
- Select a DLL from the dropdown or type any custom filename directly.
- Applies to both Detail View and Grid View overrides.

**Mod author badges in Detail View**
- Named mods from the RenoDX wiki now display the mod author as a bordered badge on the detail panel info line, right-aligned next to the existing platform and status badges.
- Multiple authors (e.g. "oopydoopy & Voosh") each get their own badge.
- Generic Unreal Engine mods show "ShortFuse", UE-Extended mods show "Marat", and generic Unity mods show "Voosh".

**Update-available version display**
- The purple update indicator next to ReShade and Display Commander buttons now shows the currently installed version number (e.g. `6.7.3`) instead of just "Update", so you can see which version you're running before updating.
- The text remains purple to indicate an update is available, and switches to the new version number in green once updated.

### Bug Fixes

**ReShade not detected under non-standard filenames**
- ReShade installations using non-standard DLL filenames (e.g. `d3d11.dll`, `dinput8.dll`, `version.dll`) were not detected by UPST, showing the game as "Not Installed" and allowing a second ReShade DLL to be installed alongside the existing one. UPST now scans all DLL files in the game folder using binary signature detection (`IsReShadeFileStrict`) as a fallback when the standard filename checks don't find ReShade.

**Old ReShade DLL not removed on reinstall with non-standard filename**
- Clicking "Reinstall ReShade" on a game where ReShade was detected under a non-standard filename (e.g. `d3d11.dll`) installed a fresh `dxgi.dll` without removing the existing non-standard DLL, leaving two ReShade DLLs in the game folder. The reinstall flow now looks up the existing install record and deletes the old DLL when it differs from the new destination filename.

### Changes

**Code refactor**
- ViewModel partial classes reorganised and split into dedicated files for ReShade, Display Commander, RenoDX, Luma, and UI concerns.
- DetailPanelBuilder and CardBuilder extracted from MainWindow code-behind to reduce file size.
- DragDropHandler extracted into its own class.

---

## v1.4.5

### New Features

**Improved Unity engine detection**
- Unity games that don't have `UnityPlayer.dll` in the base folder are now detected correctly.
- Detection now also checks for `Mono` folder, `MonoBleedingEdge` folder, `il2cpp` folder, and `GameAssembly.dll` ÔÇö all common markers of Unity IL2CPP and Mono builds.

**UE-Extended available for all generic Unreal Engine games**
- The UE-Extended toggle now appears for every Unreal Engine game that does not have a named mod on the RenoDX wiki, not just games explicitly listed in the manifest.
- A compatibility warning dialog now pops up when enabling UE-Extended, advising that not all games are compatible and to check the Notes section for any game-specific information.

**Manifest 32-bit / 64-bit flags**
- The `thirtyTwoBitGames` manifest flag now takes priority over automatic PE header detection, restoring the ability to force-flag a game as 32-bit from the manifest.
- A new `sixtyFourBitGames` manifest flag allows games incorrectly detected as 32-bit by the auto-detection to be force-flagged as 64-bit.

**Remember last view**
- The app now remembers whether it was last in Detail View or Grid View and opens in that same view on next launch.

**Installed filter**
- New filter button between All Games and Favourites that shows only games with RenoDX or Luma installed (DC and ReShade alone do not qualify).

**Manifest engine overrides**
- A new `engineOverrides` manifest field allows the engine for any game to be overridden.
- Setting a game to `"Unreal"` or `"Unity"` changes both its filter category and enables the correct generic mod/addon behaviour (UE-Extended eligibility, generic Unity addon, etc.).
- Setting a game to any other string (e.g. `"Silk"`, `"Source 2"`, `"Creation Engine"`) displays that label in the engine badge but keeps the game in the Other filter.
- Games with no known or overridden engine continue to show as Unknown and filter into Other.

**Manifest DLL name overrides**
- A new `dllNameOverrides` manifest field allows the ReShade and Display Commander install filenames to be set remotely per game.
- Example: `"Mirror's Edge": { "reshade": "d3d9.dll", "dc": "winmm.dll" }`. Either field may be empty to keep the default name.
- User-set per-game DLL overrides in the Manage panel always take priority over manifest values.

**ReShadePreset.ini auto-deploy**
- If a `ReShadePreset.ini` file is placed in `%LOCALAPPDATA%\RenoDXCommander\inis\`, it is automatically copied to the game folder alongside `reshade.ini` on every ReShade or Display Commander install.
- The ­ƒôï INI button on the ReShade row also copies the preset file if present.

**ReShade and Display Commander version display**
- The status label next to the ReShade and Display Commander install buttons now shows the installed version number (e.g. `6.7.3`) instead of just `Installed`.
- Falls back to `Installed` if no version information is available.
- Applies to both Detail View and the grid card Manage popout.

**Custom engine icon**
- Games with a custom engine name set via `engineOverrides` in the manifest now show a dedicated engine icon in the engine badge, rather than no icon.

### Changes

**Filter layout**
- The Other filter has been moved from the top row to the second row, now sitting between Unity and RenoDX.

**Change install folder now opens game folder**
- The folder picker for changing a game's install path now opens directly in the game's current folder instead of the last-used location.

### Bug Fixes

**UE-Extended toggle not applying**
- Clicking the UE-Extended button was silently ignored for games that had not yet been flagged as `IsGenericMod`, even though the button was visible. The eligibility check now matches the same conditions used to show the button.

**Games showing as installed after manual file removal**
- After a full Refresh, games with ReShade, Display Commander, or RenoDX manually deleted from the game folder were still showing as installed in the UI.
- UPST now verifies that the installed file actually exists on disk when loading saved records. Stale records are automatically cleaned up and the correct status is shown immediately on the next Refresh.

**Manifest DLL name override not applying to existing installs**
- The `dllNameOverrides` manifest field was only used as the filename for new installs. Games already installed under a different filename were not renamed when the manifest override was applied.
- UPST now renames existing ReShade and Display Commander files to match the manifest override on every Refresh, matching the behaviour of user-set DLL overrides.

**Manifest DLL name override not visible in UI**
- Games flagged via `dllNameOverrides` in the manifest were silently installing with the correct filename but the DLL naming override toggle in the Overrides section remained off, giving no indication anything was different.
- Games with a manifest DLL override now have the toggle turned on automatically and the filenames pre-filled, identical to a user-set override. The override can be disabled per-game and that preference is remembered across refreshes.

**Change install folder picker opening in Documents**
- The Change Install Folder button was opening the file picker in the Documents folder instead of the game's current install directory.
- The picker now opens directly in the game's folder using the native `IFileOpenDialog` COM interface, which correctly supports arbitrary start paths in WinUI 3 unpackaged apps.

---

## v1.4.4


### New Features

**Drag-and-drop archive extraction**
- Archives (.zip, .7z, .rar, .tar, .gz, .bz2, .xz) can now be dragged directly onto the UPST window. The archive is extracted using the bundled 7-Zip, and any `.addon64` or `.addon32` files inside are automatically found and installed via the existing addon install flow.
- If multiple addon files are found inside an archive, a picker dialog lets you choose which one to install.
- If no addon files are found, a clear message is shown.

**32bit Mod**

- 32bit Mode has been replaced by automatic detection of 32bit game executables. Thanks to Lazorr for implementing this and Jon for the starting point.

### Changes

**Grid view wiki status icon**
- Each game card in grid view now displays the wiki status icon on the same row as the RDX/RS/DC installation dots, right-aligned.
- The wiki status shows only the icon, not the full text label. Hovering shows the full label as a tooltip.
- Ô£à = Working (listed on RenoDX wiki). ­ƒÜº = In Progress (listed on wiki). ÔÜá´©Å = May Work (not on wiki but Unreal/Unity engine detected). ÔØô = Unknown (not on wiki, no known engine). ­ƒÆ¼ = Discord-only.
- Games in Luma mode do not show a wiki status icon on the grid card.

### Bug Fixes

**Wiki parser now handles all table formats**
- The RenoDX wiki splits its game list across multiple tables with varying column layouts (3-column, 4-column, status in different positions). The parser previously only read the first 4-column table, missing ~40% of games. It now detects table structure by examining header text (Name/Maintainer/Links/Status) and parses every mod table on the page regardless of column count or order. This fixes games like Lies of P, Aragami 2, EVERSPACE 2, CODE VEIN, Avatar, Pacific Drive, and many others showing incorrect wiki status.

---

## v1.4.3

### New Features

**Grid View**

- Users now have the option of using a Grid View. Switch between Grid View and Detail View easily with the click of a button. Each game can now be managed from a smaller pop out while in Grid View.

### Changes

**Background Maintainance**

- Removed excess flyouts in Detail View.
- Cleaned up some code.

---

## v1.4.2

### Changes

**UI Tweaks**

- Layout change on game cards 

---

## v1.4.1

### Changes

**Code refinement**

- Additional UI cleanup and redundant code removal.

---

## v1.4.0

### Changes

**New UI design**

- Brand new UI designed by Lazorr as well as multiple background tweaks and fixes. 

---

## v1.3.7

### Changes

**ReShade INI deployed with DC Mode installs**
- Installing Display Commander in DC Mode now automatically deploys the template `reshade.ini` to the game folder using the same merge logic as standalone ReShade installs. If no INI exists, the template is copied; if one already exists, template keys are merged on top while preserving game-specific settings.

### Bug Fixes

**Foreign DLL backup not triggering for OptiScaler and similar tools**
- Fixed `dxgi.dll` files from OptiScaler (and other tools that mention "ReShade" in config comments) being misidentified as ReShade and overwritten instead of backed up to `.original`. The binary scan now only matches on `reshade.me` or `crosire` ÔÇö strings unique to the actual ReShade binary ÔÇö and rejects files over 15 MB as too large to be ReShade.

---

## v1.3.6

### New Features

**Battle.net game detection**
- UPST now automatically detects games installed via the Battle.net (Blizzard) launcher.
- Detection uses Windows Uninstall registry entries (filtering by Blizzard/Activision publisher), the Battle.net config file (`Battle.net.config`) for the default install path, and default folder scanning under `Program Files\Battle.net` and `Blizzard Entertainment`.
- Battle.net games appear with a dedicated platform icon on game cards and in the compact mode game list.
- Drag-and-drop exe detection now recognises Battle.net store markers (`.build.info`, `.product.db`).

**Rockstar Games Launcher detection**
- UPST now automatically detects games installed via the Rockstar Games Launcher.
- Detection uses Windows Uninstall registry entries (filtering by Rockstar publisher), the launcher's `titles.dat` file for install paths, and default folder scanning under `Program Files\Rockstar Games`.
- Rockstar games appear with a dedicated platform icon on game cards and in the compact mode game list.
- Drag-and-drop exe detection now recognises Rockstar store markers (`PlayGTAV.exe`, `socialclub*.dll`).

### Changes

**Compact UI layout rework**
- The top header bar (logo, title, search box) is now completely hidden in compact mode. The filter bar is the topmost bar.
- The search box has been moved to the right-hand toolbar, placed below the About button.
- The UPST logo is displayed below the search box on the right toolbar.
- The "UPST" title text is no longer shown in compact mode.
- The first game alphabetically is now auto-selected when entering compact mode, so the view is never empty on launch.

**About panel version**
- The About panel now correctly displays the current version number.

**Scroll and selection preservation**
- Favouriting or unfavouriting a game no longer resets the scroll position in full UI mode or deselects the game in compact mode.
- Refresh and Full Refresh now restore the previous scroll position in full UI and re-select the previously selected game in compact mode.

**ReShade INI merge**
- Installing ReShade or clicking the ­ƒôï INI button now merges the template `reshade.ini` into the game's existing INI instead of overwriting it. Template keys always take precedence, but any game-specific settings not in the template (e.g. addon configs, effect toggles, custom keybinds) are preserved.

---

## v1.3.5

### Bug Fixes

**Drag-and-drop crash loop (source icon binding)**
- Fixed an infinite crash loop when dragging and dropping a game exe into the window. The platform source icon binding threw `ArgumentException` when the game had no known store source (e.g. manually added games), because WinUI's `ConvertValue` cannot convert `null` to an `ImageSource`. The icon is now bound via an explicit `BitmapImage` with a typed `Uri`, bypassing `ConvertValue` entirely.

**Added games appear in correct alphabetical position**
- Games added via drag-and-drop or the Ô×ò Add Game button now appear in their correct alphabetical position in the game list immediately, instead of being appended to the bottom.

---

## v1.3.4

### New Features

**Ubisoft Connect game detection**
- UPST now automatically detects games installed via Ubisoft Connect (formerly Uplay).
- Detection uses registry keys, the launcher's `settings.yml` configuration, and default install folder scanning.
- Ubisoft games appear with a dedicated platform icon on game cards and in the compact mode game list.
- Drag-and-drop exe detection now recognises Ubisoft store markers (`uplay_install.state`, `uplay_*.dll`).

### Changes

**DLL naming override ÔÇö rename instead of delete**
- Enabling DLL naming override now renames existing ReShade/DC DLLs to the custom filenames instead of uninstalling them, keeping installs tracked without requiring a reinstall.
- When override filenames are changed while already enabled, existing custom-named files are renamed in place to the new names.
- Both Full UI and Compact UI now use the new rename path when DLL overrides are already active and only the filenames change.

**Compact view ÔÇö selection preserved after save**
- After saving overrides in Compact mode, the previously selected game card is automatically re-selected once filtering finishes, preventing the selection from jumping unexpectedly.

**Deploy buttons ÔÇö confirmation dialogs**
- The **­ƒÄ¿ Deploy Shaders** and **ÔÜÖ Deploy DC Mode** buttons now show a confirmation dialog asking to Continue or Cancel before executing bulk operations.

### Bug Fixes

**Search box clear button visibility**
- The search box now consistently shows the Ô£ò clear button as soon as you type the first character, instead of appearing only after further edits.

**Addon download and drag-and-drop ÔÇö extension validation**
- Downloads and drag-and-drop addon installs now validate the resolved filename extension before any network or file activity, rejecting non-`.addon64` / `.addon32` files with a clear error message and skipping the download.

**Luma snapshot security ÔÇö trusted source guard**
- Luma snapshot downloads are now restricted to GitHub URLs under `https://github.com/Filoppi/`. Any other URL is rejected with an error before any network request is made.

---

## v1.3.3

### New Features

**Compact UI Mode**
- Added an alternative "Compact" layout alongside the existing "Full" UI.
- Compact mode shows an alphabetical game list on the left, the selected game's card and overrides in the center, and all toolbar buttons vertically on the right.
- Toggle between modes with the ­ƒôÉ button ÔÇö in the header when in Full mode, or at the top of the right toolbar when in Compact mode.
- The UI mode preference is saved and persists across app restarts.

**Platform source icons**
- Game cards and the compact mode game list now display platform-specific icons (Steam, GOG, Epic, EA App, Xbox) instead of plain text badges.

**Remote manifest system**
- Game-specific overrides (blacklist, install path corrections, wiki status, game notes, shader packs, Luma defaults, native HDR list) are now driven by a remote manifest hosted on GitHub. This allows quick fixes and new game support without requiring an app update.
- The manifest is fetched from the GitHub API on launch with a raw.githubusercontent.com fallback, and cached locally for offline use.

**Wiki unlinks (manifest)**
- The remote manifest can now unlink games from false fuzzy wiki matches. Unlinked games fall through to their generic engine addon (Unreal or Unity) instead of being incorrectly associated with a named wiki mod.

**Luma always enabled**
- Luma Framework support is no longer hidden behind a settings toggle. Luma badges appear on all eligible game cards by default. The "Luma (Experimental)" setting has been removed from About ÔåÆ Settings.

**Luma auto-default for specific games**
- Games listed in the remote manifest automatically start in Luma mode on first detection, without requiring manual toggling.

**Luma-specific game notes**
- The Ôä╣ info popup now shows custom Luma-specific notes (from the remote manifest) when a game is in Luma mode, providing tailored guidance beyond the standard wiki notes.

### Changes

**Filter bar rework**
- Removed the "Installed" and "Not Installed" filter tabs.
- Added a "RenoDX" tab that shows only games with RenoDX wiki mods available.
- The "Luma" tab is now always visible (previously required enabling Luma in Settings).

**Wiki status for unmatched Unity/Unreal games**
- Unity and Unreal Engine games that don't match any wiki entry now display a "­ƒÜº Unknown" status badge with amber colouring instead of being left blank, indicating they may become supported in future.

**Compact list update highlight**
- Games in the compact mode list now show a highlighted border when an update is available.

**Per-mode window size persistence**
- Full UI and Compact UI each remember their own window size independently. Switching modes restores the last-used size for that mode.

**"Extended UE" tag support**
- The remote manifest can now tag Unreal Engine games as "Extended UE", which automatically assigns the UE-Extended addon and marks the game as native HDR.

**Game Info dialog enlarged**
- The Ôä╣ info popup's maximum height increased from 400 to 440 pixels to reduce clipping of longer notes.

### Bug Fixes

**Nexus link icon not appearing**
- Fixed the ­ƒîÉ Nexus/external link button not appearing on game cards where a Nexus URL was available but no snapshot was present.

**Luma badge dimming**
- The Luma toggle badge now uses a dimmer green when active, making it easier to distinguish from the bright "available" state.

**UE-Extended button sizing**
- Fixed the ÔÜí UE-Extended toggle button being taller and wider than adjacent buttons on game cards.
