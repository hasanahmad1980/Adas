# DLSS update review — September 3, 2026

## Included in Ada 2.6.19

| Option | Reviewed version | What changed |
| --- | --- | --- |
| ShortFuse unified | User-supplied September 2 build | Replaced canonical `renodx-dlss.addon64`; SHA-256 `85EAE478F1E733E85B247C32469C2B2CC1A1C0DD2AB4AFD7DAC240E619201CEE`. Embedded build date 2026-09-02T19:48:14; no verified semantic version, so not labelled 0.4/0.5. |
| [DLSS5-Feeder](https://github.com/jlrouzies-fr/DLSS5-Feeder/releases/tag/v0.12.1-beta.1) | 0.12.1-beta.1, optional | Includes 0.12.0's interactive in-game host panel and FSR 1 expand-back. Latest beta fixes runtime rebinding, feature-level-10 shared textures and DXVK host pacing. Upstream explicitly needs affected-game testing for several fixes. x86 add-on and x64 helper replaced together, protocol v7. Stable 0.7 remains available. |
| [NIGos Bridge](https://github.com/NIGos/dlss5-bridge/releases/tag/v1.4.7) | 1.4.7 | Former `dlss5-dx11-bridge` URL redirects here. Fixes hooks overwriting adjacent NGX jump-table entries. Used only on existing native DX11/Vulkan bridge routes, never stacked with Feeder. |
| [AIO](https://github.com/kibblerz/DLSS5-Reshade-AIO/releases/tag/v1.7.24) | 1.7.24, experimental | Updated all three asset hashes. Adds asynchronous performance telemetry and mask fixes. Rejection-mask toggle/strength exposed in Ada but off by default: upstream says nonzero strength may hide NR. Full upstream controls remain available. Earlier Streamline FG safety-disable was removed upstream in 1.7.21; do not claim it remains active. |
| [OptiScaler DLSS-NR](https://github.com/Dagherbou/OptiScaler_DLSSNR/releases/tag/v0.1.2-dIssnr) | 0.1.2, experimental | New independent profile, packaged offline with licenses/dependencies. Native-DLSS 64-bit DX12 and Vulkan only. Exposure-aware color mapping, working-resolution/appearance controls available through Insert. DX11's FSR/XeSS bridge trade-off is not automatically configured in Ada. |
| [NR-before-SR fork](https://github.com/Markxiao94/OptiScaler-DLSSNR-NR-before-SR/releases/tag/v0.1.2-nr-before-sr-english) | 0.1.2 English x64, experimental | New separate DX12 native-DLSS option. Enables split pipeline; optional RR supersampling stays off initially. Internal SR presets/sharpness, ratios, model and comparison controls remain in the upstream overlay. This is not a universal speed/quality improvement; higher processing resolutions cost more GPU time. |

Choose profiles under **DLSS 5 → Review / Repair → Rendering profile — stable, beta and experimental**. An unavailable option is disabled for incompatible renderer/architecture. OptiScaler NR uses **Insert**; AIO/RenoDX/Feeder use ReShade. The original game's DLSS must stay **on** for OptiScaler NR and native RenoDX, but **off** for AIO. Remove the existing suite before switching independent pipelines.

The private local installer includes the supplied runtimes and both OptiScaler archives. AIO remains an official-download-once/cache or local-import route because its redistribution license is not established. Archive hashes are fixed; mutable latest/nightly downloads are not silently substituted. No downloaded setup scripts are executed. NVIDIA runtime licensing is separate; bundling for this requested local build does not establish public redistribution rights.

## All other previously shared GitHub links checked

| Project | Current result | Decision |
| --- | --- | --- |
| [FeedKit](https://github.com/ntqueryinformation/FeedKit/releases/tag/v1.4.0) (including its separate releases link) | 1.4.0: updater, OpenGL, Source-engine root/bin placement, cached download fallback, new Feeder ZIP layout | Ada already has OpenGL, ZIP handling, ownership-based removal and hash verification. Retain Ada's installer, not another nested installer. Source-engine multi-folder deployment is a follow-up compatibility case, not blindly copied into every game. |
| [RHI](https://github.com/RankFTW/RHI/releases/tag/RHI-2.5.4) | 2.5.4: update-preference persistence, profile labels, ReBAR Auto, NR uninstall ownership, independent addon-download failures | Ada's suite uses stronger per-file backup/hash ownership instead of copying the NR sentinel approach. General RHI update/preferences/ReBAR UI changes were reviewed, not merged as unrelated changes in this DLSS payload update. |
| [Signature repair](https://github.com/kayle2203/dlssnr-signature-repair) | Latest source commit `0b353bb2730bb0f6f7bfc753f68e21761d60b715`, August 28, v1.1.1 | Existing narrowly scoped repair retained. No indiscriminate signature repair or replacement of the user-selected patched runtime. |
| [ReshadeMotionEstimation](https://github.com/JakobPCoder/ReshadeMotionEstimation) | Latest source commit `5fc3f434ba158bfc380a71b60aa5a2bddf2242d6`, January 2023; no releases | No newer package to apply. Existing provider support retained. LumeniteFX remains the existing managed Feeder provider; AIO uses VORT. |

## Additional discoveries and limits

- [DLSS5-Autopilot](https://github.com/Kizzuwatnaa/DLSS5-Autopilot): useful route-aware diagnostics, cached API fallback and explicit distinction between an alternative build and an outdated build. Ada keeps separate named profiles and never claims that newer means compatible with every game.
- [DLSS5oneclick](https://github.com/faisalkindi/DLSS5oneclick), [DLSS5-Swapper](https://github.com/rakanki911/DLSS5-Swapper), and [Easy AIO installer](https://github.com/shTNT/Easy-AIO-Installer-DLSS5-NR): overlapping installation workflows, not additional rendering engines. No nested installer execution or unverified aggregate repack adopted.
- [Ada runtime patcher](https://github.com/dev-camo/dlssnr-ada-patcher): new source-only hardware-enablement patcher; not an image-quality upgrade. Do not patch the already user-selected runtime again or infer malware safety from a hash.
- [UnityDLSSNR](https://github.com/Kuan-Mi/UnityDLSSNR): developer/native Unity integration requiring project-level work, not a drop-in option for every existing Unity game.
- Deep Fried Chicken and Alex's Toolkit appear in [Feeder's interoperability notes](https://github.com/jlrouzies-fr/DLSS5-Feeder/blob/main/DEPLOY-DEV.md). No independently identified current public GitHub release was established in this pass. They are not silently bundled. The notes warn that multi-pass histories can worsen motion smearing, and DFC must replace RenoDX rather than stack with it.
- Video, image, webcam and Blender DLSS projects found in the search are outside this game-installer task.

## Verification and rollback

Ada tests cover profile isolation, x86 exclusion, archive path handling, not running upstream scripts, foreign-file conflict protection, defaults, and standard-OptiScaler protection. Supplied dated ShortFuse builds are pinned so a differently numbered mirror release cannot automatically downgrade them; a future explicit import or Ada package can replace the pin. Standard ReShade/OptiScaler install, update and removal paths cannot overwrite the suite-owned OptiScaler NR route. Build-time checks compare every required packaged payload to its expected SHA-256. No actual game/GPU execution was performed; successful copying does not verify rendering, frame pacing, driver compatibility, or fix 007's black screen.

Old ShortFuse/Bridge binaries and the replaced optional Feeder payload are recoverable under `artifacts/backups/before-2.6.19`. Installed games are not modified by building this installer. After installing Ada, close the target game and run its Review / Repair to deploy updates. Use the suite's × button to restore tracked originals when switching routes.
