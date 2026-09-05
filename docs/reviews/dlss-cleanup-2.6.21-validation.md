# Ada 2.6.21: confirmed automatic conflict cleanup

## Outcome

Review / Install now previews orphaned conflicts, asks once to remove them, preserves recovery copies, and continues the selected installation. Shared Vulkan route changes are also handled inside the app after confirmation; the UI no longer directs users to perform a separate manual removal.

## Cause and scope

Read-only inspection of the reported 007 deployment found `nvngx.dll_dlssnr.dll` without a DLSS installation record. The prior profile-switch path depended on that record, and orphan uninstall cleanup omitted this marker. Installation consequently reached the defensive OptiScaler conflict guard.

The fix recognizes explicit suite components and verified OptiScaler loaders. It preserves native DLSS runtimes, unrecognized loaders, and unrelated game-specific RenoDX mods. The actual 007 `dxgi.dll` was identified as ReShade, not OptiScaler. No game folders were modified during this work.

## Safety and behavior

- Approval covers exact paths, file hashes, selected route, and ownership-record hash. New or changed conflicts require a fresh review.
- Cancelling confirmation returns before installation changes.
- Conflicts move into `.adas/preserved`; no broad directory deletion is used.
- Local profile switching retains durable rollback, including orphan cleanup.
- Shared-layer installation begins only after the removal transaction completes. The confirmation explains that shared Vulkan changes are not covered by game-local rollback.
- Uninstall stops before orphan cleanup when tracked removal fails, preserving recovery state.

## Verification

- Regression reproduced before the fix: uninstall left an orphan OptiScaler NR marker in place.
- After the fix: 186 tests passed, zero failed. Receipt: `RenoDXCommander.Tests/TestResults/adas-2.6.21.trx`.
- Coverage includes preview without mutation, absent/stale approval, changed and newly appearing conflicts, reversible cleanup, conflicting originals restored from backups, shared Vulkan disclosure, and alternative-pipeline cleanup without touching unrelated game mods.
- Focused whitespace check passed.
- Targeted manual review covered candidate classification, path containment, approval validation, transaction boundaries, native-file preservation, and UI cancellation.

The debugging workflow drove the regression-first fix and recovery checks. Simplify was skipped for overlapping pre-existing edits. Code review was targeted manual review because unrelated dirty branch work made a branch-wide review inappropriate. No commit or PR was created.

Game rendering, GPU output, and the earlier 007 black-screen issue were not tested or claimed fixed by this change.

## Build

Application publish and pinned payload verification passed. NuGet vulnerability metadata was unavailable in the restricted network; dependencies were already restored. Inno Setup compilation succeeded.

- Installer: `artifacts/installer/Adas-Setup.exe`, version 2.6.21, 785611627 bytes.
- SHA256: `717233049A4052E728A3269E02A7AD1718EDEA9E775E7CDDFED2B322C43B2BD0`.
- Published application: `artifacts/publish/RHI.exe`, file version 2.6.21.0.
