# DLSS 5 upstream research ledger — 2026-09-04

Audience: Adas maintainers. Scope: current public projects that affect game injection, compatibility, presentation, visual quality, recovery, or setup automation. Evidence is taken from project documentation and release notes rather than copied compatibility lists.

## Verdict on the submitted compatibility map

The map is directionally useful, but it is not accurate enough to drive automatic installation. It mixes mutually exclusive add-ons, treats experimental routes as universal, and describes AIO as a safer default than its own documentation supports.

The corrected rule is: detect the executable architecture and active renderer, then choose one transport and exactly one neural consumer. Preserve originals, remove incompatible managed routes, verify the result, and keep a per-game manual renderer override.

## Corrections that change Adas behavior

1. **Do not combine ShortFuse unified with Feeder.** For 64-bit D3D9, D3D11, or D3D12, current Feeder documentation says `renodx-dlss` performs the whole job and neither needs nor tolerates Feeder. Adas now treats it as a direct one-add-on route.
2. **Feeder remains essential for 32-bit, Vulkan, and higher-quality D3D9 motion handling.** A 32-bit game requires the matched `addon32` plus `host64` pair. Vulkan uses the Feeder layer. D3D9 through Feeder can use real estimated temporal inputs instead of only the final backbuffer.
3. **Install exactly one neural consumer.** Feeder explicitly warns that two consumers can disable or fault the session. The same applies to old versioned copies left beside the current one.
4. **AIO is an optional 64-bit experimental route, not the universal first choice.** Version 2.0 supports D3D9/11/12/Vulkan, but the author warns that presentation compatibility varies by game, recommends windowed mode, preserves v1.7.24 as a fallback, and says never to mix 1.x and 2.x binaries.
5. **Bridge is best when a D3D11 or Vulkan game already supplies DLSS.** It mirrors the game's genuine DLSS data. Its no-DLSS substitute is off by default and documented as lower quality, so Adas must not silently prefer it over a suitable direct route.
6. **OptiScaler DLSSNR is specialized.** The fork is for games that already expose a temporal upscaler input, with Neural Rendering currently centered on DX12. It is not a general injector for games with no DLSS/upscaler path.
7. **D3D10 support is narrow.** Feeder 0.13.1-beta.1 adds a direct 32-bit D3D10 relay. The release states that 64-bit D3D10 still has no backend. This is beta and has been game-tested on only one title.
8. **Old DirectX is not automatically “supported.”** D3D8/9 can be translated, but translation success does not prove the game, ReShade, motion provider, and neural path work together. D3D1–7/DirectDraw/Glide remain manual/unsupported until a complete route is demonstrated.
9. **Vulkan needs a special warning.** Feeder documents NVIDIA Smooth Motion as incompatible with its Vulkan route. The app should keep it off for that game rather than presenting a mysterious stale/flickering picture.
10. **Merserk's visual enhancer is unrelated to injection.** It is an offline image/video processor and should not be installed into games.

## Correct automatic route table

| Detected game | Automatic route | Confidence |
| --- | --- | --- |
| 64-bit D3D9/11/12, no native DLSS | ShortFuse `renodx-dlss` direct; no Feeder or bridge | Best simple default; still experimental per game |
| 64-bit D3D12 with native DLSS | One current neural consumer over the game's DLSS | Preferred native route |
| 64-bit D3D11/Vulkan with native DLSS | Bridge plus one compatible neural consumer | Stronger inputs than synthetic mode; game-dependent |
| 32-bit D3D11/D3D12/OpenGL | Feeder beta, matched `addon32` and `host64`, one consumer | Experimental, with verified examples |
| 32-bit D3D10 | Feeder 0.13.1 beta direct relay | Very new; limited validation |
| 32-bit D3D8/D3D9 | dgVoodoo to D3D11, then Feeder/host | High-risk, per-title fallback |
| 64-bit Vulkan/OpenGL without native DLSS | Feeder and one consumer | Experimental; Vulkan Smooth Motion off |
| 32-bit Vulkan/DXVK | Feeder/host | User-reported/experimental, not a safe universal default |
| D3D1–7, DirectDraw, Glide | No automatic DLSS 5 install | Translation alone is insufficient evidence |
| Any supported 64-bit API where direct route fails | AIO 2.x, then AIO 1.7.24 as isolated fallback | Explicit experimental fallback only |
| Game already exposing DLSS2+/FSR2+/XeSS | Optional OptiScaler DLSSNR route | Specialized advanced route |

## Project decisions

- **DLSS5-Feeder:** core automatic route where it is actually required. Its automatic installer, cache, backup, architecture/API detection, verification, and manual API override validate Adas's simplified workflow.
- **dlss5-bridge:** core native-DLSS bridge for D3D11/Vulkan; synthetic mode stays non-default.
- **DLSS5 ReShade AIO:** retain as an isolated experimental fallback. Never mix with another presentation/neural pipeline.
- **OptiScaler_DLSSNR:** retain as an Advanced experiment and ship its reviewed payload offline, but do not automatically select it for ordinary no-DLSS games.
- **FeedKit:** useful one-click install/backup reference. Its runtime-download model is not adopted because Adas's requested core routes must work offline.
- **DLSS5-Swapper:** useful precedent for automatic detection plus a per-game manual API override. Its Electron overlay and broad controls are not imported into the simple desktop UI.
- **RHI:** underlying library-detection and game-management code remains available internally, but the component-table interface is no longer the visible Adas workflow.
- **dlss5-visual-enhancer:** excluded; it processes media files rather than game frames.

## Packaging decision

Adas is intentionally an experimental suite, so the installer carries every reviewed automatic and Advanced route locally. That includes roughly 214 MB of OptiScaler experiment archives and the roughly 155 MB Streamline/runtime archive. The resulting installer is large by design, but choosing an experiment never turns into a surprise download or manual import. Simplicity is achieved in the interface and route selection, not by silently removing features.

## Primary sources

- [DLSS5-Feeder README](https://github.com/jlrouzies-fr/DLSS5-Feeder)
- [DLSS5-Feeder 0.13.1-beta.1](https://github.com/jlrouzies-fr/DLSS5-Feeder/releases/tag/v0.13.1-beta.1)
- [DLSS5 Bridge README](https://github.com/NIGos/dlss5-bridge)
- [DLSS5 Bridge 1.4.8](https://github.com/NIGos/dlss5-bridge/releases/tag/v1.4.8)
- [DLSS5 ReShade AIO README](https://github.com/kibblerz/DLSS5-Reshade-AIO)
- [DLSS5 ReShade AIO 2.0.3](https://github.com/kibblerz/DLSS5-Reshade-AIO/releases/tag/v2.0.3)
- [OptiScaler DLSSNR README](https://github.com/Dagherbou/OptiScaler_DLSSNR)
- [OptiScaler DLSSNR 0.2.0](https://github.com/Dagherbou/OptiScaler_DLSSNR/releases/tag/v0.2.0-dlssnr)
- [FeedKit](https://github.com/ntqueryinformation/FeedKit)
- [DLSS5-Swapper](https://github.com/rakanki911/DLSS5-Swapper)
- [dlss5-visual-enhancer](https://github.com/Merserk/dlss5-visual-enhancer)

## Limits

These projects are fast-moving experimental software. Repository claims and file verification cannot guarantee a correct image in every engine. Adas can make setup safer and simpler, detect stale/wrong managed files, and roll back failed changes; it cannot turn an unverified route into universal game compatibility.
