# Adas DLSS 5 compatibility rules

Updated: 2026-09-04

Adas follows four rules:

1. Detect the real game executable, architecture, and renderer.
2. Install one transport and exactly one neural add-on.
3. Back up originals, remove conflicting managed routes, and verify the finished files.
4. If detection is wrong, allow a per-game renderer override under **Advanced**.

## What Adas chooses

| Game | Default |
| --- | --- |
| 64-bit DirectX 9/11/12 without DLSS | ShortFuse direct add-on, no Feeder |
| 64-bit DirectX 12 with DLSS | Native DLSS plus one neural add-on |
| 64-bit DirectX 11 or Vulkan with DLSS | Bridge plus one neural add-on |
| 32-bit DirectX 10/11/12 or OpenGL | Matched Feeder 32-bit add-on and 64-bit helper |
| 32-bit DirectX 8/9 | dgVoodoo translation plus matched Feeder pair |
| 64-bit Vulkan/OpenGL without DLSS | Feeder route |
| Unknown renderer | Stop and ask for one manual renderer choice |

## Never combine

- ShortFuse `renodx-dlss` and DLSS5-Feeder.
- Two neural consumers.
- AIO 1.x and AIO 2.x.
- AIO and another presentation pipeline.
- OptiScaler and Feeder driving the same upscaling route.

## Experimental fallbacks

AIO and OptiScaler DLSSNR remain available as optional experiments, not automatic first choices. DirectX 10, translated DirectX 8/9, Vulkan, OpenGL, and 32-bit paths remain game-dependent. On Vulkan, NVIDIA Smooth Motion must be disabled for the Feeder route.

For evidence and project-by-project notes, see [the research ledger](report-source.md).
