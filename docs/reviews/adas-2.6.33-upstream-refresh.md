# Adas 2.6.33 upstream refresh review

## Scope and intent

Refresh Adas's packaged DLSS 5 components to AIO 2.0.4-experimental.1, DLSS5 Bridge 1.4.11, and the complete matched Feeder 0.14.0-beta.1 set. Preserve automatic and reversible installation while adding simple persistent controls for native RenoDX profiles.

## Actionable Findings

None.

## Coverage

- Verified every replaced upstream binary against its reviewed SHA-256 value during source and publish validation.
- Confirmed the published package contains all six matched Feeder 0.14.0-beta.1 files and no Feeder 0.13.1-beta.1 files.
- Reviewed profile normalization, update detection, stable/unified settings keys, invalid-style rejection, diagnostic routing, and installer metadata.
- Full test suite: 262 passed, 0 failed, 0 skipped.
- Published self-contained Windows build and compiled the installer successfully as version 2.6.33.
- Existing compiler warnings remain outside this update's scope; this change introduced no build errors.

## Verdict

Ready to install. The packaged component upgrade and simple native settings path are internally consistent, tested, hash-pinned, and included in the generated installer.
