# Replay Studio library scan and file-export admission

Baseline `91b1c0c6c44e8c90effd250729709f791a84765e`; branch `codex/consolidation-theatre-storage-producers`. Phase 6/9, evidence baseline #489. Independent main-based compile; no code dependency. Queue #503 addresses video-export manifests, while this slice addresses the separate library/raw-file storage producers. Sidecar #507 and UI retirement #500/#509 are conceptual integration dependencies, not prerequisites to compile this change.

## Purpose and source ownership
`TheatreWorkspace` favorite, rename and organization callbacks each invoke Reload. Reload previously queued an entire ScanLibrary operation into ReplayStorageJobs on every request. Its generation check suppressed stale publication but could not remove queued work. The EXPORT callback similarly accepted every press, creating another unique destination/copy operation. ReplayStorageJobs bounds two active operations; its semaphore waiters have no global admission cap.

The library now owns one scan loop with one latest request tuple. Requests retain their synchronous favorite/rename/tag side effects; the loop discards an obsolete result and scans the latest requested state without scheduling every intermediate request. Selection generation and preserve/fallback semantics remain. ScanLibrary and its metadata-index updates are unchanged. Disposal cancels queued scan admission and prevents publication; already-running synchronous disk work may finish. Request, cancellation and running state clear in finally.

File export now admits one operation per workspace, shows busy state/explicit duplicate feedback, and restores controls in finally. Accepted source/destination logic is unchanged. An accepted export may finish after disposal, preserving the destination; completion then does not repaint the disposed workspace. No arbitrary destination is deleted. Android's existing selected IStorageFile path remains inside the same admission/finally scope.

The fields belong to the existing UI callback/continuation owner: button handlers set admission before await, and awaits retain the UI synchronization context. Workers still perform existing private scan/copy operations. This is not a generalized thread-safe public storage API or a global ReplayStorageJobs capacity change. No blocking disposal, audio/GL callback or renderer hot path is changed.

## Exact baseline and candidate evidence
Tool invokes actual installed HubNavButton callback delegates on the drawing-disabled Avalonia headless backend. Headless.Enter runs before UI feedback references; native picker availability is suppressed and no picker is invoked. All replay/index/favorite/copy files use a uniquely owned synthetic Paths.Export. No Window, scene, GL/audio device, encoder, game assets or external account call is started.

Two deterministic fake operations occupy the existing storage slots. Tests count the .NET 10 semaphore's pending task-node graph via reflection (test-only, not production). Every blocker is released in finally and both active slots/dispatcher are drained before deleting the fixture directory.

- Original production source, same final tool: **1 PASS / 6 FAIL**, exit 1. 128 favorite callbacks retain 128 scan waiters; 128 EXPORT callbacks retain 128 copy waiters; disposing the scan workspace leaves 128 waiters. Valid single export I/O failure/retry behavior already passed.
- Candidate: **7 PASS / 0 FAIL**, exit 0. Each 128-request burst retains one waiter; disposal removes queued scan ownership and clears running/cancellation/request state. Latest selection/favorite publication, exact one-copy bytes/control restoration, preexisting sentinel preservation, I/O failure/retry and accepted export completion after disposal pass.
- The early three-case characterization reported 0 PASS/3 FAIL; the final seven-case baseline above adds compatibility/failure cases against the exact same source change.

Full-workspace WeakReference death is **INCONCLUSIVE**: even an unused, never-attached, immediately disposed headless control remains alive in this fixture. It is therefore not valid to attribute that whole-control retention to storage or claim full UI graph collection/native RSS improvement. Direct waiter and cleared-owner-state assertions establish this slice's queue ownership. UI frame/style/attachment roots require a separate lifecycle fixture.

## Validation
```sh
/Users/jarrett/.dotnet/dotnet run --project tools/theatre-storage-check -c Release
/Users/jarrett/.dotnet/dotnet build src/MphRead/MphRead.csproj -c Release --nologo
/Users/jarrett/.dotnet/dotnet build src/MphRead.Android/MphRead.Android.csproj -c Release -t:Compile -m:1 -p:AndroidSdkDirectory=/Users/jarrett/Library/Android/sdk -p:AndroidEnableProfiledAot=false -p:RunAOTCompilation=false
git diff --check
```

| Gate | Status |
|---|---|
| Device-free producer/compatibility/failure tests | PASS, 7 assertions |
| Desktop Release | PASS, final incremental 0 warnings/0 errors; source rebuild 30 warnings in unchanged files |
| Android shared-source Compile | PASS, 51 warnings in unchanged source / 0 errors |
| YAML and diff checks | PASS; new device-free CI step in renderer lifecycle policy job |
| Full-workspace GC/native RSS | INCONCLUSIVE / NOT_RUN |
| Actual GL/audio/media export/visual identity and physical UI | NOT_RUN |
| Actual Android storage-provider picker/cancel/destination | NOT_RUN; existing destination code unchanged, common guard/finally compiled |

No replay/checkpoint/protocol serializer, annotation identity, camera/HUD/HD arm-cannon asset or playback timing change. No launcher CLI registration: invoke the standalone tool by project path rather than an unknown diagnostic flag.

## Remaining boundary, risk and rollback
Constructor storage policy remains one existing job per cached Theatre route (PrimeWorkspaceHost.Get); it is not covered by the manageStorage:false producer fixtures. Active scans and accepted exports may retain one workspace until work completes. The global semaphore, thumbnail attach producers, generic JSON size budgets, inherited encoder pipes and other scope retirement remain separate gates. No blanket claim that all ReplayStorageJobs callers or whole UI graphs are bounded.

Repeated intermediate scans are intentionally coalesced, and duplicate file-export requests receive busy feedback. Accepted file identities and destination choices remain; no content-count/size limit or migration is introduced. Independent workflow changes share an anchor and must all survive integration.

Rollback: revert this commit. Existing replay/sidecar files need no migration; in-memory admission state disappears on process exit.
