# Cross-transition authored texture decode lifetime

R16 now admits asynchronous world-material decodes through process-shared `TextureDecodeBudget.Shared`: at most two accounted reservations / 256 MiB on desktop, one / 64 MiB on Android. Admission never waits for a slot. Existing scene queue/promotion pacing and oversized-source fallback remain in effect. The reservation uses the existing source width × height × 12 estimate, covering accounted decode/prepared image work. It does **not** bound opaque codec-private scratch, allocator overhead, process RSS, unrelated synchronous uploads, or other processes.

`TextureDecodeJob<T>` holds that reservation through active decode, completed data awaiting promotion, and the render owner's promotion. Completion exposes no prepared payload through `Task.Result`. Retirement drops prepared ownership and requests cancellation without waiting for worker completion or cancellation callbacks. A non-interruptible native codec retains its reservation until it actually finishes; it then drops its late result. Old Scene unload therefore cannot create a fresh unaccounted admission budget for the next Scene.

`Scene.PumpModernTextureStreamingCore` retires stale requests before checking their task completion. `Scene.DoCleanup` clears queued/versioned requests and retires pending jobs before player retirement and the CleanupStarted lifetime snapshot; `UnloadGl` repeats retirement idempotently. Worker delegates do no GL work and capture no Scene. The obsolete decode-only unfenced-callback latch no longer blocks foreground release after fenced retirement; existing room-transition/worker, NetAi, completed resource cleanup and role/lease checks remain. Lifetime snapshots still mark unknown external callback counts as untracked. Shared current/peak reservation bytes and active jobs are exposed separately from the scene-local pending estimate. Cancellation is cooperative around the existing decoder; this change does not claim to interrupt a native codec already inside its decode routine.

The first integrated server build exposed a leftover `decode.Task` reference in the cleanup diagnostic (preserved in the parent's `build-server-v3.log`). The cleanup-boundary/obsolete-latch correction above fixes that source reference and its semantics. The offline PASS below predates this application-only cleanup integration correction; the parent owns its coordinated application/server rebuild.

Windows-only source-linked validation (SDK 10.0.401, Release):

```powershell
$env:DOTNET_ROOT='C:\Users\Jarrett\AppData\Local\ProjectPrime\dotnet-10.0.401'
$env:PATH="$env:DOTNET_ROOT;$env:PATH"
dotnet run --project tools/texture-decode-budget-check -c Release
```

Result: **PASS 1225**, Windows=True, Release. Full output: [texture-decode-lifetime-check.log](texture-decode-lifetime-check.log). The linked production helper is exercised with a deliberately blocked non-interruptible decoder, immediate retirement, new-owner refusal, observed cancellation, late-result discard, prepared-data ownership through simulated promotion, fault/null results, idempotent release, 400 completion/retirement interleavings and concurrent admission. A labeled old per-scene-budget negative control admits twice the intended cross-transition reservation limit. These are ownership/correctness checks, not frame-time or RSS measurements.

The integrated Windows GL fixture `tools/material-pack/StreamingDecodeLifetimeGpuCheck.cs` injects the actual production job/packet into a real Scene's pending list, invokes both the real stale pump and `UnloadGl` while its codec is blocked, and checks that the list clears without waiting while the full shared reservation prevents new admission. It releases the worker and checks late prepared data is discarded without an upload/GL error. Command after the coordinated application/tool Release build:

```powershell
dotnet tools/material-pack/bin/Release/net10.0/material-pack.dll --gpu
```

At the initial source handoff, integrated validation was pending. Its completed Windows result follows below; Android-device acceptance remains unqualified.

Final integrated Windows native validation PASS in `windows-material-default-v4.log` (also BC7 and forced-legacy modes), client MVID53589a2c-2a2a-423c-94cc-24d50b7f6355, SHA25626906006a69c2275eb385e58ad1c1007167c137ec43b3eb541c74c2b73cbe729. The v5 clean build reproduces the entire v4 bundle. The actual Scene stale-pump/unload fixture passes; no late publication or GL error occurs. Windows-hosted Android recompilation/AOT also passes; device memory/codec peaks and large-pack tail acceptance remain unqualified.
