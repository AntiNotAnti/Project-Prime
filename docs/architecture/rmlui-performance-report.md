# RmlUi performance evidence

The final Home comparison is pending fresh samples with the corrected baseline entry gate. Screenshot review caught a benchmark error: all three Avalonia warmup captures in the second batch remained on the **PRESS START title screen**, while all native captures showed **Home with a live Hunter**. Those comparative CPU, allocation and UI timing results are disqualified. The earlier batch also lacked verified Home readiness and is withdrawn from acceptance evidence. No Home performance pass or regression can be concluded from those batches.

The corrected collector requires an actual Home readiness predicate after successful surface presentation. Controlled diagnostics also suppress background startup maintenance and missing-map preview generation so an empty detached cache cannot introduce a second workload during idle sampling. Its format 2 report declares `workload=Home` and `homePresentationVerified=true`; startup frames cannot start warmup, screenshots or measurement. The legacy diagnostic path must invoke its existing startup continuation and complete the normal reveal before becoming eligible. The harness rejects reports without this gate.

## Disqualified workload evidence

Both batches used complete Release builds of the actual project graph on Apple M4 Pro (12 CPU cores, 24 GB), macOS 27.0, .NET 10.0.12, arm64, Metal. The native client restored, referenced and shipped zero Avalonia assemblies. The fixture contained only detached preferences and extracted game-data paths, at 1280×720 logical/2560×1440 framebuffer and configured 60 fps. Account/presence start, authentication/HTTP and automatic updater requests were disabled; IME and Cocoa accessibility remained enabled. Each process had 5 seconds of warmup and a 20 second sample. Mode/backend/cap equality was insufficient: the active pages differed.

[Disqualification record](rmlui-evidence/performance-metal-invalid-title-home/DISQUALIFIED.json). Both inspected screenshots remain local diagnostic artifacts at `/private/tmp/prime-rmlui-invalid-workload-images/`; their hashes are retained with the evidence. They are not source or release assets. Original raw diagnostic calculations are retained for traceability at [initial batch](rmlui-evidence/performance-metal-initial/comparison.json) and [retained-cache batch](rmlui-evidence/performance-metal-invalid-title-home/comparison.json); neither is acceptance evidence.

The corrected short preflight reached verified Home in both processes, and both warmup screenshots were inspected. Legacy default Home displays the existing News feed and article photographs, without a visible Hunter. Native default Home displays its deployment chamber, stage effects and the real Hunter with cinematic lighting. The primary whole-process comparison retains each actual default product path and reports this workload difference. UI subphase timings exclude chamber/Hunter draws. Scene differences limit attribution and do not waive the whole-product non-regression requirement. The short preflight predates the final background-work guards and is only a workload check, not performance acceptance evidence.

An earlier exploratory run triggered an automatic profile request and was discarded. An initial diagnostic harness also resolved an older assembly and entered account code before guard verification; no request trace was captured, so its remote effects are unknown. That probe is excluded. The corrected diagnostic checker preflights the exact binary for both account guards and uses an isolated assembly load context. Its 4 checks confirm local profile display, rejected authentication, rejected HTTP creation and no account/session files.

## Production subphase profile

A separate guarded 20 second sample of the same native client with diagnostic subphase probes identified accessibility capture, context updates and GPU command authoring as the largest measured UI costs. The successful production run used the same fixture/backend/window/cap, produced 1,171 presented frames and reported 27.41% of one CPU core, p95 total UI submission 0.674 ms and 61.10 MB managed allocation. This valid native-only profile identifies costs; it cannot establish relative Home performance without a verified Avalonia Home workload.

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
