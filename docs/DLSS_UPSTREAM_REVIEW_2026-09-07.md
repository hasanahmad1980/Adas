# DLSS upstream review — 2026-09-07

This review re-checks the DLSS-related GitHub projects tracked by Adas against
their current upstream releases and records the payload updates applied in this
change. It follows [DLSS_UPSTREAM_REVIEW_2026-09-06.md](DLSS_UPSTREAM_REVIEW_2026-09-06.md).
It is an audit, not a claim of live-game rendering compatibility.

## Payloads updated in this change

| Component | Repo | Was | Now | Notes |
| --- | --- | --- | --- | --- |
| DLSS5-Feeder beta | jlrouzies-fr/DLSS5-Feeder | 0.14.0-beta.4 | **0.14.0-beta.5** | Matched-set refresh. Only the add-ons and 32-bit host changed; `DLSS5_Feed.fx` and both Vulkan fallback layers are byte-identical to beta.4. |
| Standalone AIO | kibblerz/DLSS5-Reshade-AIO | 2.0.7-experimental.1 (prerelease) | **2.0.9** (stable) | Only `standalone-dlssnr.addon64` and the proxy `nvngx.dll` changed; `DLSS5_AIO_Feed.fx` is byte-identical. |
| DLSS5oneclick | faisalkindi/DLSS5oneclick | 0.11.15 | **0.11.23** | Verified-launch helper: pinned download URL + SHA-256 updated. |

### Feeder 0.14.0-beta.5

Upstream beta.5 (<https://github.com/jlrouzies-fr/DLSS5-Feeder/releases/tag/v0.14.0-beta.5>)
is a bug-fix refresh of the matched beta set:

- The installer disables all versioned RenoDX copies so two neural consumers
  cannot run simultaneously.
- NGX failures now report specific process-blocked / GPU-architecture
  diagnostics instead of blaming hardware.
- Adds a UAV-less 64-bit shared-output texture fallback for devices that reject
  unordered-access binds at certain formats.
- Adds GPU-hang frame-phase diagnostics and hardens the Vulkan teardown path.
- Improves the verifier's game-executable and dgVoodoo2 detection.

The shader (`DLSS5_Feed.fx`) and both Vulkan fallback layers verified byte-identical
to beta.4, so only `dlss5-feed.addon64/.addon32` and `dlss5-feed-host64.exe` were
replaced. Protocol handling is unchanged from beta.4.

### Standalone AIO 2.0.9

Upstream promoted past the pinned `2.0.7-experimental.1` prerelease to a stable
`2.0.9` release (<https://github.com/kibblerz/DLSS5-Reshade-AIO/releases/tag/v2.0.9>).
It folds in the adaptive GPU-pressure governor from the 2.0.7 experimental build
and adds a source-resolution override, DPI virtualization correction, live
color-profile switching, and selectable neural-rendering pass counts. Adas still
defaults these off / unforced. The AIO feed shader is byte-identical to
2.0.7-experimental.1, so only the add-on and its proxy `nvngx.dll` were replaced and
re-pinned.

### DLSS5oneclick 0.11.23

Fixes Red Dead Redemption 2 being misdetected as DirectX 9 and improves renderer
detection using modern-API markers (`ffx_fsr2_api_dx12_x64.dll` AMD FSR2,
`nvlowlatencyvk.dll` NVIDIA Reflex for Vulkan). Adas pins the helper's verified
download; the URL and SHA-256 were updated to v0.11.23.

## Current — no change required

| Component | Repo | Pinned | Status |
| --- | --- | --- | --- |
| DLSS5 Bridge | NIGos/dlss5-bridge | 1.4.12 | Latest **stable**. `1.4.13-pre2` exists but the maintainer states 1.4.12 remains the stable release; kept opt-out per the exclude-prereleases policy. |
| Neural Upstream | matiasLombo/neural-upstream | 0.3.0 | Latest release. |
| OptiScaler DLSS-NR | Dagherbou/OptiScaler_DLSSNR | 0.2.0 | Current stable line. `v0.2.0-patch1` is an upstream prerelease and remains opt-out. |
| MFG Ada Unlock | mavismmg/MFGAdaUnlock-RenoDx | (dynamic) | Adas fetches the latest release at use time; upstream is `0.6.1`. No pin to bump. |
| DLSSNR signature repair | kayle2203/dlssnr-signature-repair | 1.1.1 | Source workflow with no GitHub release binaries; Adas reimplements it natively. |

## Repository housekeeping

`tools/build-adas.ps1` carried a stale payload manifest (it still listed the
removed `0.14.0-beta.2` files and an out-of-date `dlss5-bridge.addon64` hash).
Its `$requiredDlss5Payload` list and `$expectedDlss5Hashes` table were regenerated
from the shipped assets so the payload gate passes again, including the corrected
bridge hash (`4F2ACECC…`, matching the code's `BridgeSha256` and the reviewed 1.4.12
asset) and the new beta.5 / AIO 2.0.9 hashes.

## Deliberately not bundled (unchanged from 2026-09-06)

- DLSS5 Bridge `1.4.13-pre2`, OptiScaler DLSS-NR `v0.2.0-patch1`: upstream
  prereleases, kept opt-out until validated broadly.
- Manager tools whose ideas are useful but which are separate installers:
  DLSS5-Autopilot (now `1.7.1`) and DLSS5-Swapper (now `2.2.3`). Their in-game
  overlay / detection ideas remain candidates for Adas's own detection layer, not
  bundled payloads.
- Aggregate repacks and the Deep Fried Chicken archive remain user-imported /
  unbundled per license, as before.

## Verification

- Full test suite (net8.0, Release/x64): **310 passed, 0 failed** (was 308; two
  new "superseded version" dashboard cases added).
- `tools/build-adas.ps1` payload gate (`Assert-Dlss5Payload`) passes against the
  source tree: every listed file is present, non-empty, and hash-matched.
- The bundled beta.5 x64 Vulkan layer contains `VkLayer_feed_vk.dll` and
  `VkLayer_feed_vk.json`; every re-pinned SHA-256 was computed from the downloaded
  official release asset.
