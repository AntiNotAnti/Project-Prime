# Replay Studio null-element validation

## Purpose and contract
Baseline `91b1c0c6c44e8c90effd250729709f791a84765e`, branch `codex/consolidation-replay-sidecar-validation`. Phase 0 #489 evidence baseline; independent of queue #503. No code dependency or native startup.

User-authored `.studio.json` and `.reel.json` loaders already fall back on absent/malformed/future-version documents and normalize null lists. JSON permits null elements inside otherwise valid lists; the existing record filters dereferenced these and threw an uncaught NullReferenceException. Public bookmark/highlight/reel reads are called by Studio controls/library; edits also load through the same boundary.

Three short-circuit guards reject null elements before the existing name/frame/range filters. Valid siblings retain record identity, names, timestamps, ranges and cameras. Bookmarks/highlights retain their existing sorted presentation, reels retain authored order. Loading does not rewrite sidecars or the recording; editing retains version 1 and logical source identity. No new content-size/count constraint, schema change, broad catch or loader redesign.

## Evidence and verification
Device-free real files in a uniquely owned temporary directory; recording fixture contains synthetic bytes and is never decoded or played. No game assets, audio, GL or external encoder.

- Before: **7 PASS / 5 FAIL**, all failures uncaught NullReferenceException for bookmarks, highlights, reels, all-null arrays and editing a mixed document.
- After: **12 PASS / 0 FAIL**, exit 0.
- Tool covers null entries at beginning/middle/end, valid siblings/order/IDs/cameras/ranges/timestamps, existing invalid record filtering, null lists, future versions, null document, malformed JSON, organization normalization, edits and unchanged recording bytes. Read checks compare original sidecar bytes.
- CI: Linux-x64 publication job runs the device-free tool alongside existing replay timeline/format checks. No launcher diagnostic flag is added.

Commands:
```sh
/Users/jarrett/.dotnet/dotnet run --project tools/replay-sidecar-check -c Release
/Users/jarrett/.dotnet/dotnet build src/MphRead/MphRead.csproj -c Release --nologo
git diff --check
```

Desktop source compilation through the tool: PASS (0 errors; 30 warnings in unchanged source). Final explicit incremental build: 0 warnings / 0 errors. File-only `-replayformatcheck`: 3,034 PASS (v2/v3/v4, order, metadata, CRC, recovery, extraction, malformed files). Workflow YAML and diff checks PASS.

Physical Studio UI acceptance, real replay playback/export and Windows/Android runtime: NOT_RUN. No throughput/RSS claim. Remaining arbitrary JSON document size is audited separately; no unvalidated valid-content limit is introduced here.

Rollback: revert this commit; no migration or persisted state rewrite.
