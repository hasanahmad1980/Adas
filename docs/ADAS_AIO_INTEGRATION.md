# Standalone AIO in Adas 2.6.18

## Decision

Offer AIO as an experimental alternative, not an upgrade over every native/Feeder route.
The recommended setup and 32-bit hosting remain unchanged. No claim is made that this
fixes 007 First Light, L.A. Noire or any other game's black screen.

## Two-pass research, checked 2026-09-02

First pass checked the tagged release, README, runtime requirements and configuration
in the author's source. Second pass checked release assets against those instructions,
an unmerged pull request, shader loading rules and redistribution constraints.

| Finding | Applied decision | Primary evidence |
| --- | --- | --- |
| Release 1.7.17 needs three files, not the two in the Discord post. | Pin and verify add-on, caller bridge and companion shader as one set. | [Release](https://github.com/kibblerz/DLSS5-Reshade-AIO/releases/tag/v1.7.17) |
| AIO is a separate Present-time pipeline with independent NR and FG. | Separate profile; no concurrent Feeder/Bridge; clear switches and character slider. | [Tagged source](https://github.com/kibblerz/DLSS5-Reshade-AIO/blob/v1.7.17/addon/src/nr-standalone.cpp) |
| 64-bit DX9/11/12/Vulkan support does not mean equal image guidance or mature FG. | Preserve native routes; mark reduced legacy guidance and pacing risk. | [Tagged README](https://github.com/kibblerz/DLSS5-Reshade-AIO/blob/v1.7.17/README.md) |
| Early initialization is opt-in, D3D12-specific, and restart-dependent. An open PR is not a released fix. | Default off; no background-Present workaround transplanted. | [Release](https://github.com/kibblerz/DLSS5-Reshade-AIO/releases/tag/v1.7.17), [PR 1](https://github.com/kibblerz/DLSS5-Reshade-AIO/pull/1) |
| ReShade can skip unchecked shaders; AIO schedules its guide techniques itself. | Set `SkipLoadingDisabledEffects=0`, load VORT + companion; leave their ordinary techniques disabled. | [ReShade 6.8 source](https://github.com/crosire/reshade/blob/v6.8.0/source/runtime.cpp), [VORT](https://github.com/vortigern11/vort_Shaders/blob/main/Shaders/vort_Motion.fx) |
| AIO has no confirmed repository redistribution licence. | Official download plus verified reusable cache/local import, not embedded release assets. | [Repository](https://github.com/kibblerz/DLSS5-Reshade-AIO), [runtime terms](https://github.com/kibblerz/DLSS5-Reshade-AIO/blob/v1.7.17/runtime/README.md) |

## Using it

Close the game. Remove its existing DLSS suite with the row's × button, then open
Review & Install → Advanced compatibility profile → Standalone AIO. Confirm offline use.
Adas obtains and places the files, preserves backups and verifies the installation.
Existing `nvngx.dll` or non-ReShade wrappers block installation rather than being overwritten.
Shared external add-on folders/presets must be changed to game-local paths first.

AIO 2.0.3 and its reviewed VORT motion bundle are packaged with Adas. Installation is offline and the local cache is populated from the verified installer payload.
Vulkan needs an already-registered 64-bit ReShade layer. Adas does not silently register or
remove a system-wide layer as part of this AIO profile.

In the game, disable built-in DLSS, frame generation and AA. Native resolution gives DLAA;
upscaling needs a genuinely smaller backbuffer. Check the mode in ReShade, changing display
mode if necessary. F10 compares complete processed/original presentation, not NR alone.
Use the settings cog for simple controls while the game is closed, or ReShade for live tuning.

## Validation and limits

Install-time hashes and architecture checks validate files, not rendered output. The shared
AIO log is deliberately not attributed to a selected game. Processing counters cannot prove
that the picture is visible, correct, stable or faster.

After installation, the user should check a gameplay scene for several minutes, compare F10,
then try NR off/on separately. Leave FG off until the base pipeline works. Stop on black output,
crashes, queue-quarantine messages or unstable pacing; close the game and uninstall AIO using
the same × button. Modified managed files are preserved by the existing rollback journal.
No game was launched or visually validated during this implementation.
