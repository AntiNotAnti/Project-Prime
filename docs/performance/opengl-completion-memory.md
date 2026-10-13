# Completion resource-memory diagnostic

The sixty-cycle same-context test passed exact fallback rendering and tracked resource-release assertions. **Process/native memory boundedness remains unverified:** RSS continued rising through the last measured blocks and stayed approximately 1.04 GiB after the final diagnostic drain. This is evidence of unresolved growth, not proof of a leak.

The authoritative [memory report](opengl-completion-memory.json) identifies client MVID `680c26eb-c1eb-411a-aaaa-1d5434de33bd`, SHA-256 `5c4eb5ad548b7678800e8e72e483790369f71811ef2390ab770c359dcbd97446`, immutable harness/result/log hashes and invocation. The run took 368.52 seconds. It predates the later per-program palette-layout correction; observations apply to this captured binary.

There were 120 scene arms at exactly 120 simulation steps each, rotating two real maps within one GL context. All five requested allocation-failure stages fired and preserved exact RGBA/depth fallback output. Tracked scene-owned resources returned to zero after every unload, and retained generations invalidated. Sixty Studio renderer sessions each rebuilt geometry/material caches three times, preserved exact pixels, and released their tracked objects. The dedicated live-D24S8 allocation fixture tests snapshot partial allocation/fallback without claiming the chosen rooms qualify for cached replay. Studio renderer/cache coverage does not represent full editor UI interaction.

Ten-arm block means, in MiB:

| Arms | RSS | GC committed | Live managed estimate | GC fragmented |
| --- | ---: | ---: | ---: | ---: |
| 0–9 | 759.21 | 309.89 | 224.23 | 140.74 |
| 10–19 | 830.27 | 277.74 | 192.69 | 144.15 |
| 20–29 | 860.53 | 268.23 | 196.35 | 132.09 |
| 30–39 | 899.10 | 270.63 | 197.49 | 134.02 |
| 40–49 | 934.47 | 271.18 | 201.25 | 132.40 |
| 50–59 | 971.42 | 272.75 | 201.70 | 134.04 |
| 60–69 | 1014.66 | 276.89 | 206.03 | 135.69 |
| 70–79 | 1046.67 | 274.82 | 206.89 | 133.02 |
| 80–89 | 1086.86 | 282.10 | 210.53 | 138.58 |
| 90–99 | 1118.22 | 277.73 | 211.05 | 133.81 |
| 100–109 | 1151.92 | 277.39 | 214.68 | 131.42 |
| 110–119 | 1183.47 | 277.52 | 215.28 | 131.42 |

Committed GC memory stabilized after startup while RSS kept growing. Live managed estimates rose modestly from the post-startup trough; they do not explain the several-hundred-MiB RSS increase. RSS includes managed pages, native/runtime allocations, mapped code and driver behavior; subtracting GC committed bytes is not a direct measurement of a particular native subsystem. Mac private-memory accounting was unavailable and reported null.

The final drain retained and examined all 120 audio shutdown tasks, rather than just the latest task:

| Drain phase | RSS MiB | GC committed MiB | Live managed estimate MiB | Audio tasks remaining |
| --- | ---: | ---: | ---: | ---: |
| Before drain | 1185.52 | 274.64 | 224.45 | 1 |
| After audio wait | 1185.55 | 274.64 | 224.48 | 0 |
| After full GC, LOH compaction, finalizers and GL completion | 1042.39 | 131.89 | 150.59 | 0 |

The audio wait completed in 5.51 ms, with all tasks completed and none faulted/canceled. RSS barely changed during that wait. Full GC and the remaining diagnostic drain released approximately 143 MiB of RSS and committed GC memory, while GC fragmentation fell to 0.08 MiB. These immediate samples do not establish a long quiescent plateau or distinguish driver caches from native allocator retention. GC heap-size/fragmentation values describe the recorded collection; live estimates and RSS are separate samples.

A preserved process stack sample caught `alcCreateContext` waiting on an OpenAL mutex while a worker destroyed an old context and stopped CoreAudio output. This demonstrates overlap-related audio load stalls. It does **not** establish that audio caused the residual RSS growth; no audio-task backlog remained after the final drain.

Further attribution needs native allocation/VM-category evidence or controlled subsystem-isolation runs, with the same tracked-resource checks. Full UI coverage, other devices and a bounded native-memory conclusion remain outstanding. All readbacks, reloads, audio waits, forced GC/finalizers and `GL.Finish` here are diagnostic work and provide no FPS evidence.
