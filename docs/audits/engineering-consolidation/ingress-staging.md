# Updater staging cancellation and cleanup

## Summary

- Goal: discard rejected or cancelled staged updates before a later handoff can use their files.
- Baseline: `91b1c0c6c44e8c90effd250729709f791a84765e`; branch `codex/consolidation-ingress-bounds`.
- Dependencies: Phase 0 baseline PR #489 and protocol/documentation PR #488. This is an independent Phase 9 failure-handling slice.
- Changed boundary: `DesktopUpdate.Stage` delegates its post-download archive preparation to `PrepareArchive`. The existing framework ZIP/TAR extractors and `ReleaseInstallation.ValidateIncoming` still define paths, executable modes, legacy manifest generation, and incoming compatibility.
- GitHub release SHA-256 verification still precedes preparation. These fixtures model bad or cancelled staging of trusted release payloads; this report does not establish an arbitrary attacker-controlled update path.

## Evidence

Environment: macOS 27.0, Darwin arm64, .NET SDK 10.0.401 (`/Users/jarrett/.dotnet/dotnet`), Release configuration. Fixtures contain no retail game data, require no renderer/audio startup, and use an isolated temporary installation with an existing executable and user settings.

To test the baseline, the original post-download Stage body was moved unchanged into the internal seam (apart from reporting missing-binary failure with an exception caught by Stage). It still used synchronous framework extraction, ignored the cancellation argument, and retained rejected staged files. Before adding cleanup/cancellation, the full harness reported **67 PASS / 8 FAIL**, exiting unsuccessfully. The eight failing assertions were:

| Format | Failure condition | Baseline result |
|---|---|---|
| ZIP | Required executable absent | Rejected, extracted file retained |
| ZIP | Paired Game/Studio metadata absent | Rejected, extracted files retained |
| ZIP | Traversal entry after valid entries | Framework rejected traversal; prior extracted files/archive retained |
| ZIP | Token cancelled after download, before extraction | Extraction/validation succeeded despite cancellation |
| TAR.GZ | Required executable absent | Rejected, extracted file retained |
| TAR.GZ | Paired Game/Studio metadata absent | Rejected, extracted files retained |
| TAR.GZ | Traversal entry after valid entries | Framework rejected traversal; prior extracted files/archive retained |
| TAR.GZ | Token cancelled after download, before extraction | Extraction/validation succeeded despite cancellation |

Candidate: .NET 10 framework async extractors receive the existing token; cancellation is also checked before extraction and around compatibility validation. A `finally` removes the downloaded archive and, unless preparation completed, the staged directory. Cleanup failures are logged without masking the original error. The same eight assertions pass. An additional Unix TAR symlink fixture confirms that existing release link rejection now removes the staged link/payload while preserving installed data.

The final harness reports **76 PASS / 0 FAIL**, exiting 0. Its valid ZIP/TAR fixtures preserve nested and hidden assets, generate the legacy release manifest, preserve executable installation behavior, and never mutate installed executable/user settings. TAR fixtures use the release workflow's `./` root and relative entry forms. Existing updater transaction, crash recovery, digest, server publication, and process handoff checks also pass.

## Validation

```sh
/Users/jarrett/.dotnet/dotnet run --project tools/updatecheck/updatecheck.csproj -c Release
/Users/jarrett/.dotnet/dotnet build src/MphRead/MphRead.csproj -c Release --nologo
git diff --check
```

| Check | Status | Evidence / limitation |
|---|---|---|
| Before-fix regression run | FAIL, expected | 67 PASS / 8 FAIL using original extraction/cleanup semantics |
| Candidate full updater harness | PASS | 76 PASS / 0 FAIL; deterministic local ZIP/TAR and child-process fixtures |
| Desktop Release compilation | PASS | SDK 10.0.401; 30 warnings / 0 errors (warnings in unchanged sources) |
| Diff whitespace | PASS | `git diff --check` |
| Windows/Linux installation and relaunch on physical hosts | NOT_RUN | macOS host; synthetic archives and process handoff tests do not establish device installation coverage |
| Live GitHub release download / genuine published archive upgrade | NOT_RUN | No external release executed for this slice |
| Cancellation midway through a large entry | NOT_RUN | Regression covers cancellation latched after download; framework cancellation propagation is implemented, but no timed large-entry fixture is claimed |
| Android APK installer / macOS signed app replacement | NOT_RUN | This changes shared desktop/server archive staging; Android uses its existing APK installer and macOS does not replace its signed app in place |

## Risk and compatibility

Protocol, replay, settings, package digests, rollback transactions, and user-owned data rules are unchanged. Successful preparation still leaves the validated payload for the existing handoff. The synchronous Stage API remains unchanged and runs away from the launcher UI; framework async extraction is synchronously awaited at that boundary.

Filesystem permissions or sharing locks can prevent cleanup. Such failures are logged and leave Stage unsuccessful; the existing next-start/next-stage `Clean` remains a retry. This is not a claim of guaranteed cleanup despite OS errors. No process-crash staging journal was added.

No expansion-budget or download-size guarantee is claimed. Stage still authenticates the release digest but has no explicit expanded archive budget; that separate audit needs measured published-package compatibility before setting limits. No FPS, memory slope, game simulation, or rendering improvement is claimed.

Rollback: revert this commit; the format and successful staged payload layout are unchanged.

## Existing ingress protections inspected

- `MapPackageReader`: 512 MiB archive, 1 GiB expanded total, 256 MiB entry, 32 MiB project, 2,048 entries; declared and actual stream lengths are checked. Portable canonical paths, manifest/project identity, references, and content digest are validated. It reads archive bytes without filesystem link extraction.
- `Pk3DiscoveryService`: 512 MiB archive, 1 GiB expanded total, 128 MiB entry, 20,000 entries; expansion ratio, duplicate/unsafe paths, and Unix symlink entries are rejected. Q3 reads enforce a streaming asset limit.
- `ReplayFormatV3`: header/chunk/footer/checkpoint/event/count budgets and checked remaining lengths precede bounded reads; decompression rejects expansion beyond declared raw size. No replay parser changes were justified by this inspection.
- `ReleaseInstallation`: bounded manifests/journals, portable owned paths, existing reparse-point rejection, payload hashes, recoverable publication, and installed-data preservation remain in use.

This is a scoped source inspection and regression result, not a full ingress security certification. Remaining full Phase 9/10 platform and malformed-input gates are not declared complete.
