# Ada 2.6.19 — update validation

Date: September 3, 2026. Scope: supplied ShortFuse replacement, upstream DLSS payload refresh, and explicitly requested beta/experimental profiles. Existing unrelated work was preserved. No game folders were modified, installer launched, commit created, or public release published.

## Result

- Release tests: **156 passed, 0 failed, 0 skipped** (`RenoDXCommander.Tests/TestResults/adas-2.6.19.trx`). The final changes after this test run were profile-label/help text only; the final app was compiled again during publish.
- Release publish: succeeded. All required source and published payload hashes passed `tools/build-adas.ps1` checks.
- Installer: Inno Setup succeeded; version resource **2.6.19**. App version **2.6.19.0**.
- Installer path: `artifacts/installer/Adas-Setup.exe`, **785607210 bytes**.
- Installer SHA-256: `67E53DC50410F872BF06DB30BECC1A8CE68AD27C3380DE1BBD08E6247359241F`.
- Supplied `renodx-dlss(1).addon64` and published canonical `renodx-dlss.addon64` both have SHA-256 `85EAE478F1E733E85B247C32469C2B2CC1A1C0DD2AB4AFD7DAC240E619201CEE`.
- Scoped `git diff --check`: passed. Existing compiler warnings remain (nullable annotations, unused variables, async methods, Windows resource qualification). NuGet vulnerability-feed access was unavailable; this was not a dependency security audit.

## Review and manual diff scan

Code review: skipped (ce-code-review unavailable) — the loaded review workflow requires independent reviewers; this session's AGENTS instructions require sequential main-thread work, so no independent completed review receipt is claimed.

The implementation workflow's permitted inline simplification/manual checks covered the new integration, settings and packaging paths. They resulted in reusing an already-loaded profile record and adding safeguards against pipeline mixing and updater downgrades. This is a main-thread review, not a substitute claim of independent peer review.

Checked: enum values appended without changing existing stored values; architecture/API eligibility; unsupported options disabled; owned-file backup and install journaling reuse; foreign-file conflicts rejected before writes; archive extraction bounds/path handling; no upstream setup scripts installed or executed; x86 Feeder and x64 host shipped as a matched protocol-v7 set; pinned hashes and local ShortFuse date pin; standard ReShade/OptiScaler mutation paths blocked from replacing suite-owned OptiScaler NR; post-copy verification; safe first-install OptiScaler settings and preservation of user tuning during repair. Both pinned archives were inspected for their actual `Enabled=auto` defaults.

No game/GPU performance, visual-quality, driver-compatibility, launch-crash or black-screen resolution claim is made. UI was compiled, not visually exercised in a running app. Optional frame generation and rejection masks remain off initially. AIO requires one upstream download or a verified local import; its binaries are not bundled. All applicable routes retain their upstream controls.

## Post-Deploy Monitoring & Validation

Owner: user performing the first game launch after installation; window: first launch and several minutes of gameplay, with only one selected rendering pipeline.

1. Close the game, install Ada, and use **DLSS 5 → Review / Repair → Rendering profile**. Remove the current suite with its × button before switching exclusive AIO/OptiScaler routes. Do not force a disabled architecture/API choice.
2. Check Ada's current-setup diagnostic for missing/changed files. Healthy file status is not proof that NR has processed frames.
3. For OptiScaler NR, enable native game DLSS and use **Insert**; inspect `OptiScaler.log` for successful initialization and active NR. For AIO, disable native game DLSS/FG/AA and check its ReShade status and timing counters. For Feeder, inspect `dlss5-feed.log` and, for x86, `host64/dlss5-feed-host.log` for a ready feature and delivered frames.
4. Watch for `stopped`, `not available`, `Failed`, architecture error 193, repeated host restarts, no processed frames, black output, or visible pacing/quality regression. No automated background monitoring was scheduled.
5. If any of those persist, close the game and remove the selected suite through Ada to restore tracked originals. Reinstall a known-working profile rather than stack another pipeline. Previous packaged binaries are backed up under `artifacts/backups/before-2.6.19`.

See `docs/DLSS_UPSTREAM_REVIEW_2026-09-03.md` for the reviewed sources, feature decisions, and excluded projects.
