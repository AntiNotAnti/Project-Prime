# RmlUi performance evidence

The initial controlled Metal comparison found lower managed allocation and no UI bitmap uploads, but higher idle CPU and UI submission cost. This result does **not** pass the measured baseline bounds. Subphase profiling and matched Home screenshots are required before assigning the cause or declaring the performance acceptance gate complete.

## Controlled comparison

Measured on Apple M4 Pro (12 CPU cores, 24 GB), macOS 27.0, .NET 10.0.12, arm64, Metal. Each UI used a complete Release build of the actual project graph; the native client restored, referenced and shipped zero Avalonia assemblies. Both used the same preference-only fixture, real extracted game-data paths, 1280×720 logical window/2560×1440 framebuffer and configured 60 fps cap. The harness alternated 3 fresh processes per UI, discarded 5 seconds of warmup and sampled 20 seconds each. Input, native IME and Cocoa accessibility remained enabled. Explicit diagnostics disabled account/presence start, account authentication/HTTP and automatic updater requests. No authentication/session/ticket files were copied into the final fixture.

| Metric | Avalonia median (range) | RmlUi median (range) | Comparison |
| --- | --- | --- | --- |
| Process start to first successful presentation |931ms (918–3457)|886ms (877–1346)|Native median below observed baseline upper bound; initial cold-cache variance retained|
| Idle CPU, percentage of one CPU core |15.86% (14.02–17.18)|31.08% (29.55–31.79)|Outside observed baseline range|
| p95 UI CPU submission per presented frame |0.237ms (0.196–0.260)|1.246ms (1.146–1.307)|Outside observed baseline range|
| Managed bytes allocated per 20 second sample |368.8MB (356.2–369.0)|60.4MB (60.3–60.4)|83.6% lower median|
| Actual UI bitmap upload bytes per 20 second sample |232.2MB|0|Native draws geometry and font atlases directly|
| UI redraws per second |3.85–3.90|58.62–58.64|Avalonia retains its bitmap between redraws; native submits a UI draw list each presented frame|

CPU percentages use 100% = one core, matching Activity Monitor; divide by 12 for the fraction of this machine's logical CPU capacity. The measured comparison bound for each metric is the largest observed Avalonia value from these 3 runs, not an invented target. The small sample cannot establish a cross-platform release threshold. Timings measure CPU-side update and draw submission, not GPU execution time. Working-set ranges overlap; this report does not claim a resident-memory reduction. macOS reports private bytes as 0 through the .NET API, so that field is unavailable rather than zero memory usage.

The first Avalonia run used `-ui=legacy -rmlui` and physically reported Avalonia, proving the explicit ordinary selection overrides the development alias. Native samples used only `-ui=rmlui` and physically reported RmlUi. First-presentation time can include different startup UI; it does not prove time to a particular interactive Home control.

Raw samples, assembly SHA256 values and comparison: [performance-metal-initial/comparison.json](rmlui-evidence/performance-metal-initial/comparison.json). An earlier exploratory run that triggered an automatic profile request was discarded and is excluded from this evidence. An initial diagnostic harness also resolved an older assembly and entered account code before guard verification; no request trace was captured, so its remote effects are unknown. That probe is excluded. The corrected diagnostic checker preflights the exact binary for both account guards and uses an isolated assembly load context. Its 4 checks confirm local profile display, rejected authentication, rejected HTTP creation and no account/session files.

## Production subphase profile

A separate guarded 20 second sample of the same native client with diagnostic subphase probes identified accessibility capture, context updates and GPU command authoring as the largest measured UI costs. The successful production run used the same fixture/backend/window/cap, produced 1,171 presented frames and reported 27.41% of one CPU core, p95 total UI submission 0.674 ms and 61.10 MB managed allocation. Its lower total p95 than the first three native runs demonstrates why a single profile cannot replace the repeated comparison.

| Subphase | Mean CPU ms | p95 CPU ms |
| --- | --- | --- |
| Accessibility capture | 0.1611 | 0.2282 |
| Context update | 0.0771 | 0.1203 |
| GPU command authoring | 0.0894 | 0.1313 |
| Native context render | 0.0360 | 0.0525 |
| IME candidate refresh | 0.0344 | 0.0562 |
| Managed draw-list capture | 0.0094 | 0.0144 |

Subphase timings overlap the update/render totals and must not be added to those totals. This profile used the eager native bridge, before optional idle scheduling, retained draw-list capture and accessibility idle reuse. Candidate headless checks already verify skipped native updates and zero allocation on unchanged frames. The managed reader passed 15 actual-native assertions covering retained frame identity, zero allocation, text/viewport changes, geometry retirement, generation changes and fail-closed filter support. Improved production performance still requires a new repeated comparison. [Raw subphase profile](rmlui-evidence/performance-metal-phases/rmlui-metal-phases-before.json).

## GPU resource lifetime

Actual native documents for Settings, Hunter, Community, Theatre, Adventure, Offline, Social, News, Setup and HUD were opened, rendered, retired and followed by a Home frame. After one warmup visit to each, 100 replacements produced 333 successful surface presentations on both Metal and Vulkan/MoltenVK. UI and shared renderer resource counts were exactly unchanged. Forced device recovery recreated the current document and atlas. Native shutdown released geometry and atlas resources; the bounded shader/stencil cache remained with the renderer and was released at renderer teardown.

Retained Home capacity after warmup: 59 geometries, 118 vertex/index buffers totaling 123,504 bytes, 34 font/textures totaling 8,355,840 logical RGBA bytes, and one 2560×1440 stencil/depth texture. The stencil estimate 14,745,600 bytes uses the nominal 4 byte depth24/stencil8 format; the driver may allocate a wider depth format. Shared resource accounting reported one surface throughout. These figures track owned logical payload/capacity, not driver residency or total VRAM. MoltenVK emits informational outstanding-memory amounts during forced device destruction; stable logical counters alone cannot prove absence of driver leaks.

[Metal resource evidence](rmlui-evidence/gpu-lifetime-metal.json) · [Vulkan resource evidence](rmlui-evidence/gpu-lifetime-vulkan.json).

## Reproduction

Build both complete project variants, including their actual project references/resources. Use a detached writable fixture containing only `paths.txt`, `launcher.txt`, `controls.txt` and `Savedata/settings.json`; keep valid absolute paths to the extracted game data, set the settings frame cap to 60 and launcher window size to 1280×720. Never copy auth/session/ticket files. The harness rejects such files and rejects assemblies without diagnostic authentication and HTTP guards. Keep the display awake and pause concurrent compilers, emulator work and other graphical clients while sampling.

```sh
python3 tools/rmlui/performance-check.py \
  --legacy /absolute/full-avalonia-build/ProjectPrime.dll \
  --native /absolute/full-native-build/ProjectPrime.dll \
  --data /absolute/preference-only-fixture \
  --out /absolute/performance-results \
  --dotnet /absolute/dotnet --backend metal
```

Use `tools/rmlui-account-diagnostic-check` with `BuildProjectReferences=false` and the exact guarded assembly path to verify the offline account guard. The tool refuses old binaries before invoking account methods.

```sh
dotnet run --project tools/rmlui-compositor-check -c Release \
  -p:BuildProjectReferences=false -- metal --lifetime /absolute/rmlui-assets /absolute/metal-lifetime.json
```

The source-linked retained reader check runs without a GPU:

```sh
dotnet run --project tools/rmlui-drawlist-cache-check -c Release -- \
  /absolute/libProjectPrime.RmlUi.Native.dylib /absolute/rmlui-assets
```

Run the analogous lifetime command with `vulkan`; DX12 requires separate Windows hardware validation. The lifecycle test draws actual page documents, but intentionally excludes controller/network work and game-world rendering. Physical Android, Windows/DX12 and Linux validation is separate from this Mac measurement.
