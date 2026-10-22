# Project Prime map-load qualification

**Report status:** measurement report complete at the 2026-10-22 05:28 UTC cutoff. The compact-storage Windows comparison and separate gameplay CPU diagnostic passed. Required hosted CI remains a separate merge gate; this report does not qualify physical-device rendering, frame pacing, or deployment.
## Executive summary

- The final compact-storage comparison used candidate DLL `d6ddbc4d76c194e1d32124c25ed7305dca2550ce68ee0193c7251207a8ecc54b` (source commit `39253834282e860f56a924e36fadc12d9cf195d0`) and completed 240 timed runs across three published packages, two cache states, and 20 paired baseline/candidate trials per map/cache combination (40 timed processes in each combination). All 240 receipts and all six warm preseeds passed; every timed sample reached the eight-player ready endpoint. Input hashes were unchanged after the campaign.
- The P95 reduction goals are 30% for application-cold and 50% for persistent-cache-warm. None of the three measured packages met either threshold. Guardian v20 improved most: 11.71% cold and 22.85% warm. DM-DECK16 persistent-cache-warm P99 increased 1.07% (2,756.6503 to 2,786.0713 ms); the full tail remains in the records.
- The request-to-constructor-complete segment is the largest part of median ready time in all 12 cells, accounting for 68.7%–96.0% of P50. This identifies where most measured time accumulates, but does not isolate a code-level cause.
- The campaign used a private `.ppmap` catalog copy per cold sample and shared private catalogs for warm trials. All 120 cold snapshots were removed after their runs; campaign temporary catalog directories were gone at completion. The native asset tree was copied once and reused, with matching source/copy hash.


## Implementation coverage and review status

The Windows A/B campaign isolates the remaining texture storage and upload changes. Its baseline already contains the earlier merged conversion, package-transfer, room-preparation, and service-recovery work, plus the common load-receipt instrumentation. It does not measure the complete program against a pre-optimization build. The changes preserve the existing package format and map identity rules. These merged PRs describe what changed; performance claims below come from the separate real-map campaign or are explicitly labeled as historical component results or contract evidence.

| Lifecycle component | Change | Delivery |
|---|---|---|
| Package qualification | Added bounded source-limit and near-limit package qualification. | [PR #1029](https://github.com/AntiNotAnti/Project-Prime/pull/1029), merged |
| Audit and reproducibility | Added the transfer baseline and repaired qualification checks used as the starting point for the focused stack. | [PR #1030](https://github.com/AntiNotAnti/Project-Prime/pull/1030), merged |
| Download acquisition | Hashes successful response bytes while streaming to disk and verifies persisted prefixes before a resumed range; bounded retry and digest checks remain. | [PR #1031](https://github.com/AntiNotAnti/Project-Prime/pull/1031), merged |
| Conversion | Reuses portable conversion artifacts with content/version/generator keys, a bounded persistent cache and the parsed BSP on cache misses. | [PR #1032](https://github.com/AntiNotAnti/Project-Prime/pull/1032), merged |
| Prewarm and loading boundary | Cancels retired room-prewarm waits and prepares only the selected custom room at the loading boundary. | [PR #1033](https://github.com/AntiNotAnti/Project-Prime/pull/1033) and [PR #1035](https://github.com/AntiNotAnti/Project-Prime/pull/1035), merged |
| Conversion-service recovery | Stages durable job metadata and recovers complete jobs after promotion crashes. | [PR #1034](https://github.com/AntiNotAnti/Project-Prime/pull/1034), merged |
| Website upload flow | Handles mixed map uploads and recovers interrupted chunks. | [Website PR #16](https://github.com/projectprimegg/projectprime-website/pull/16), merged |

The remaining engine stack was open at this report cutoff: [PR #1037](https://github.com/AntiNotAnti/Project-Prime/pull/1037) repairs CI evidence and native acceptance drift; [PR #1041](https://github.com/AntiNotAnti/Project-Prime/pull/1041) adds the isolated real-map campaign driver; [PR #1046](https://github.com/AntiNotAnti/Project-Prime/pull/1046) reduces native texture decode and upload allocations; and [PR #1043](https://github.com/AntiNotAnti/Project-Prime/pull/1043) adds bounded finite and suffix byte ranges. The delivery order is #1037 → #1041 → #1046 → #1043. Required CI, including macOS, must be green before any of these are merged.

## Next investigation priorities

The request-to-constructor-complete interval is the dominant measured part of readiness: it accounts for 68.7%–96.0% of median ready time. Across the 12 compact-campaign cells, its per-cell P50 ranged from 1,599.9 to 21,084.8 ms. The next intervals were smaller: constructor completion to `Scene.OnLoad` was 352.4–492.2 ms P50, `Scene.OnLoad` to first simulation was 255.8–262.9 ms, and first simulation to successful present was 109.7–151.8 ms.

The first follow-up is to split the constructor interval into measured sub-stages before choosing another optimization; current receipts cannot attribute that time to archive reads, geometry decode, mesh preparation, or another operation. The separate CPU `-perfcheck` diagnostic at 1920×1080 is recorded below. Next, measure `Scene.OnLoad` and first-simulation costs on representative hardware. GPU upload timing, optional HD texture promotion, physical display scanout, Android lifecycle memory pressure, and CDN fallback remain outside the current ready endpoint or acceptance evidence.

## Endpoint, cache states, and measurement limits

The measured interval starts at the map-audit request and ends at the first successful actual present after all eight players are spawned and alive; the receipt records the post-present player count. The audit window is 320×180. Readiness does not wait for optional HD texture promotions and does not measure physical display scanout. Process startup is excluded.

Application-cold uses a fresh private user-data profile, explicit `PROJECT_PRIME_Q3_CACHE` directory, unique runtime namespace, and fresh private package-catalog copy for every sample. Configured native assets are copied privately once per campaign and reused across samples. Persistent-cache-warm uses one successful preseed per map and arm; those six processes are excluded, and later fresh processes reuse the corresponding profile, Q3 cache, namespace, and private package catalog. The operating-system file cache was not flushed.

Arms ran in deterministic paired order: baseline first on even trial indices and candidate first on odd indices. The design was not randomized. Percentiles use nearest rank; with 20 observations, P99 is the maximum and is exploratory.

Receipt counter definitions matter. `allocatedBytes` is an approximate process-wide delta since request from `GC.GetTotalAllocatedBytes(false)`, not exact retained or allocated-object memory. `processCpuMilliseconds` is process-wide processor-time delta since request, not main-thread time or wall time. `workingSetBytes` is the current process working set sampled at readiness. `peakWorkingSetBytes` is the process-lifetime high-water value sampled at readiness; it is not a request-only peak, retained managed bytes, or VRAM. Exact recorded counter values are in the raw evidence.

## Published package mapping

| Public package | Room ID in raw receipts | SHA-256 | Size (bytes) | Campaign role |
|---|---|---|---:|---|
| MOCKTROID | room-01 | `41d62ef662ebbe26a6cb9ac70ee973e2161742d46400032f18965db480fb7268` | 4,395,222 | timed-load |
| DM-DECK16 | room-02 | `49c08ae61f6c1172dd3d9507de7976c2903626151602eb9bcfb673f6d39fdfe0` | 49,275,230 | timed-load |
| Guardian v20 | room-03 | `59c7cf940a1c6231e7cabd3465f3c7f9d9cc677cae08073efb419b8df497c54d` | 478,782,339 | timed-load |
| MORPHEUS PRIME | — | `6b7cfad3d72e79e627c5c563b6cd87f45a9ee4e2321b565b820e43efcf058c97` | 332,229,349 | catalog-only; compatibility smoke |

The final catalog contained four regular `.ppmap` packages (864,682,140 bytes total). MOCKTROID, DM-DECK16, and Guardian v20 were timed. MORPHEUS PRIME was present in the copied catalog but was not one of the timed rooms; it was covered by the separate published-package compatibility smoke.

## Final compact-storage campaign

The run completed on Windows 11 build 26200, x64, with 16 logical processors and an NVIDIA GeForce RTX 5080 renderer (driver 617.42). The runner was Python 3.14.7. All 240 receipt environments matched. The campaign ran from 2026-10-22 04:25:27.058 UTC to 05:04:23.252 UTC.

Baseline DLL SHA-256: `a0274647251df4e78f8d57707d26089df1f3241ec62d5b1680b91647bf9891f3`. Candidate DLL SHA-256: `d6ddbc4d76c194e1d32124c25ed7305dca2550ce68ee0193c7251207a8ecc54b`. Candidate source archive SHA-256: `2bc99d06f7585cac5ba2d032cc040708e5fbfdbc8e9095e0817f6fa9a735154d`. Runner SHA-256: `2b72a8d3d8ad176d70345de7da8b553b07351873129868d40cf286ac1e097e7b`.

### Ready time

P50 / P95 / P99 in milliseconds. The final receipt file contains each observation and all stage counters.

| Map | Cache state | Arm | P50 / P95 / P99 (ms) | Request→constructor complete P50 (share of ready P50) |
|---|---|---|---:|---:|
| MOCKTROID (`room-01`) | application-cold | baseline | 2,740.7769 / 2,932.9312 / 3,036.8530 | 2,018.481 ms (73.6%) |
| MOCKTROID (`room-01`) | application-cold | candidate | 2,693.2174 / 2,739.0776 / 2,742.0245 | 1,959.802 ms (72.8%) |
| MOCKTROID (`room-01`) | persistent-cache-warm | baseline | 2,385.8045 / 2,462.3573 / 2,500.0056 | 1,663.392 ms (69.7%) |
| MOCKTROID (`room-01`) | persistent-cache-warm | candidate | 2,329.2512 / 2,361.1197 / 2,495.5781 | 1,599.911 ms (68.7%) |
| DM-DECK16 (`room-02`) | application-cold | baseline | 3,636.8477 / 3,697.3117 / 3,747.5574 | 2,903.990 ms (79.8%) |
| DM-DECK16 (`room-02`) | application-cold | candidate | 3,565.5581 / 3,631.0643 / 3,631.1019 | 2,835.678 ms (79.5%) |
| DM-DECK16 (`room-02`) | persistent-cache-warm | baseline | 2,651.7385 / 2,750.5506 / 2,756.6503 | 1,916.245 ms (72.3%) |
| DM-DECK16 (`room-02`) | persistent-cache-warm | candidate | 2,575.1238 / 2,693.8602 / 2,786.0713 | 1,849.125 ms (71.8%) |
| Guardian v20 (`room-03`) | application-cold | baseline | 21,965.0272 / 22,116.8942 / 22,246.7811 | 21,084.817 ms (96.0%) |
| Guardian v20 (`room-03`) | application-cold | candidate | 19,229.5425 / 19,526.6041 / 19,551.5906 | 18,351.463 ms (95.4%) |
| Guardian v20 (`room-03`) | persistent-cache-warm | baseline | 10,816.9680 / 10,950.6427 / 10,992.0961 | 9,958.367 ms (92.1%) |
| Guardian v20 (`room-03`) | persistent-cache-warm | candidate | 8,387.9172 / 8,448.0323 / 8,466.0346 | 7,514.833 ms (89.6%) |

### P95 goal attainment

Reduction is calculated from baseline P95 to candidate P95. The goals are per measured map.

| Map | Application-cold baseline → candidate (ms) | Reduction | 30% goal | Persistent-cache-warm baseline → candidate (ms) | Reduction | 50% goal |
|---|---:|---:|---|---:|---:|---|
| MOCKTROID (`room-01`) | 2,932.9312 → 2,739.0776 | 6.61% | unmet | 2,462.3573 → 2,361.1197 | 4.11% | unmet |
| DM-DECK16 (`room-02`) | 3,697.3117 → 3,631.0643 | 1.79% | unmet | 2,750.5506 → 2,693.8602 | 2.06% | unmet |
| Guardian v20 (`room-03`) | 22,116.8942 → 19,526.6041 | 11.71% | unmet | 10,950.6427 → 8,448.0323 | 22.85% | unmet |

### Resource quantiles at readiness

Each value is P50 / P95 / P99. The full-byte quantiles are in JSON; MiB tables use 1,048,576 bytes per MiB.

**Approximate managed allocated-byte counter delta (MiB)**

| Map | Cache state | Arm | P50 / P95 / P99 (MiB) |
|---|---|---|---:|
| MOCKTROID (`room-01`) | application-cold | baseline | 692.789 / 693.471 / 693.488 |
| MOCKTROID (`room-01`) | application-cold | candidate | 581.384 / 581.404 / 581.410 |
| MOCKTROID (`room-01`) | persistent-cache-warm | baseline | 496.733 / 496.748 / 496.751 |
| MOCKTROID (`room-01`) | persistent-cache-warm | candidate | 389.020 / 389.037 / 389.042 |
| DM-DECK16 (`room-02`) | application-cold | baseline | 1,044.278 / 1,044.369 / 1,044.375 |
| DM-DECK16 (`room-02`) | application-cold | candidate | 933.016 / 933.062 / 933.077 |
| DM-DECK16 (`room-02`) | persistent-cache-warm | baseline | 540.271 / 540.291 / 540.292 |
| DM-DECK16 (`room-02`) | persistent-cache-warm | candidate | 428.959 / 428.980 / 428.985 |
| Guardian v20 (`room-03`) | application-cold | baseline | 23,711.498 / 23,711.787 / 23,749.497 |
| Guardian v20 (`room-03`) | application-cold | candidate | 15,176.079 / 15,214.672 / 15,214.883 |
| Guardian v20 (`room-03`) | persistent-cache-warm | baseline | 12,212.372 / 12,212.503 / 12,212.506 |
| Guardian v20 (`room-03`) | persistent-cache-warm | candidate | 3,682.695 / 3,682.903 / 3,682.958 |

**Process CPU-time delta (ms)**

| Map | Cache state | Arm | P50 / P95 / P99 (ms) |
|---|---|---|---:|
| MOCKTROID (`room-01`) | application-cold | baseline | 2,500.000 / 2,609.375 / 2,843.750 |
| MOCKTROID (`room-01`) | application-cold | candidate | 2,500.000 / 2,640.625 / 2,640.625 |
| MOCKTROID (`room-01`) | persistent-cache-warm | baseline | 2,156.250 / 2,265.625 / 2,265.625 |
| MOCKTROID (`room-01`) | persistent-cache-warm | candidate | 2,093.750 / 2,187.500 / 2,312.500 |
| DM-DECK16 (`room-02`) | application-cold | baseline | 3,375.000 / 3,531.250 / 3,593.750 |
| DM-DECK16 (`room-02`) | application-cold | candidate | 3,375.000 / 3,500.000 / 3,531.250 |
| DM-DECK16 (`room-02`) | persistent-cache-warm | baseline | 2,421.875 / 2,546.875 / 2,578.125 |
| DM-DECK16 (`room-02`) | persistent-cache-warm | candidate | 2,390.625 / 2,453.125 / 2,531.250 |
| Guardian v20 (`room-03`) | application-cold | baseline | 23,578.125 / 23,859.375 / 23,968.750 |
| Guardian v20 (`room-03`) | application-cold | candidate | 20,734.375 / 21,078.125 / 21,171.875 |
| Guardian v20 (`room-03`) | persistent-cache-warm | baseline | 11,812.500 / 11,984.375 / 12,000.000 |
| Guardian v20 (`room-03`) | persistent-cache-warm | candidate | 9,296.875 / 9,515.625 / 9,546.875 |

**Current working set and process-lifetime peak working set (MiB)**

| Map | Cache state | Arm | Working set P50 / P95 / P99 | Peak working set P50 / P95 / P99 |
|---|---|---|---:|---:|
| MOCKTROID (`room-01`) | application-cold | baseline | 465.824 / 467.234 / 471.547 | 465.824 / 467.234 / 471.547 |
| MOCKTROID (`room-01`) | application-cold | candidate | 438.430 / 441.895 / 444.105 | 438.430 / 441.895 / 444.105 |
| MOCKTROID (`room-01`) | persistent-cache-warm | baseline | 409.840 / 410.188 / 411.031 | 409.840 / 410.188 / 411.031 |
| MOCKTROID (`room-01`) | persistent-cache-warm | candidate | 376.203 / 376.543 / 376.727 | 376.203 / 376.543 / 376.727 |
| DM-DECK16 (`room-02`) | application-cold | baseline | 607.465 / 607.984 / 613.922 | 607.465 / 607.984 / 613.922 |
| DM-DECK16 (`room-02`) | application-cold | candidate | 568.809 / 574.773 / 575.000 | 568.996 / 574.773 / 575.000 |
| DM-DECK16 (`room-02`) | persistent-cache-warm | baseline | 447.125 / 448.176 / 448.250 | 447.266 / 448.176 / 448.250 |
| DM-DECK16 (`room-02`) | persistent-cache-warm | candidate | 402.660 / 409.355 / 409.605 | 402.660 / 409.355 / 409.605 |
| Guardian v20 (`room-03`) | application-cold | baseline | 9,536.586 / 9,686.367 / 9,800.719 | 9,983.324 / 10,132.598 / 10,247.840 |
| Guardian v20 (`room-03`) | application-cold | candidate | 3,909.316 / 4,462.625 / 4,490.094 | 3,932.953 / 4,560.902 / 4,569.473 |
| Guardian v20 (`room-03`) | persistent-cache-warm | baseline | 7,197.770 / 7,383.609 / 7,391.637 | 7,524.699 / 7,781.902 / 7,783.332 |
| Guardian v20 (`room-03`) | persistent-cache-warm | candidate | 2,243.543 / 2,254.250 / 2,259.098 | 2,244.117 / 2,254.250 / 2,259.098 |

The stage records show the request-to-constructor-complete segment is the dominant interval in each cell. Later intervals—constructor to `Scene.OnLoad`, first simulation, and first successful present—are individually much shorter. Per-stage P50/P95/P99 intervals are preserved per cell in the compact JSON.

### Campaign integrity

The campaign manifest records the same baseline DLL, candidate DLL, paths file, package catalog, and native asset-tree hashes before and after the run. The native tree copy included 3,543 files / 97,066,399 bytes and matched its source hash. The campaign made 120 per-sample cold catalog copies and six stable warm catalogs; all copied catalog hashes matched the source catalog. Cold copies were removed after their runs, and all campaign temporary catalog directories were absent at completion. No failed or not-ready samples were excluded; the only excluded processes are the six documented warm preseeds.

## Completed D45 comparison: pre-compact and exploratory

This earlier 240-sample campaign measured the frozen D45 pixel-buffer candidate before compact storage. It used the same three public packages and an eight-player ready endpoint, but it passed the source `.ppmap` catalog directly to child processes. Its application-cold package-catalog isolation was therefore not established. The input hashes matched after the campaign. Local builds were running concurrently, and the deterministic order was not randomized. Do not use these results as release qualification for the compact-storage build.

The candidate DLL SHA-256 was `d45f4157e943b5d14f265d53f486755bd9ac25d42014defc6b0806d6624ec85e`; the baseline was `a0274647251df4e78f8d57707d26089df1f3241ec62d5b1680b91647bf9891f3`. The run used Windows x64 and an NVIDIA RTX 5080 (driver 617.42), from 2026-10-22 03:14:36.593 UTC to 03:51:16.637 UTC.

### D45 ready time

P50 / P95 / P99 in milliseconds. Each cell has 20 accepted samples; six warm preseeds are excluded.

| Map | Cache state | Arm | P50 / P95 / P99 (ms) |
|---|---|---|---:|---:|
| MOCKTROID (`room-01`) | application-cold | baseline | 2,808.9484 / 3,172.3089 / 3,194.3058 |
| MOCKTROID (`room-01`) | application-cold | candidate | 2,735.9164 / 2,907.9213 / 2,937.2454 |
| MOCKTROID (`room-01`) | persistent-cache-warm | baseline | 2,427.0757 / 2,715.7665 / 3,548.2406 |
| MOCKTROID (`room-01`) | persistent-cache-warm | candidate | 2,365.3001 / 2,418.0211 / 2,469.2535 |
| DM-DECK16 (`room-02`) | application-cold | baseline | 3,635.0601 / 3,677.5602 / 3,690.8168 |
| DM-DECK16 (`room-02`) | application-cold | candidate | 3,605.2601 / 3,672.6516 / 3,684.6962 |
| DM-DECK16 (`room-02`) | persistent-cache-warm | baseline | 2,657.8513 / 2,687.1370 / 2,695.4690 |
| DM-DECK16 (`room-02`) | persistent-cache-warm | candidate | 2,615.0508 / 2,655.3409 / 2,664.3467 |
| Guardian v20 (`room-03`) | application-cold | baseline | 22,051.0455 / 22,447.6079 / 22,491.4048 |
| Guardian v20 (`room-03`) | application-cold | candidate | 20,477.8492 / 20,681.3408 / 20,891.8511 |
| Guardian v20 (`room-03`) | persistent-cache-warm | baseline | 10,833.7768 / 11,676.9795 / 16,524.2710 |
| Guardian v20 (`room-03`) | persistent-cache-warm | candidate | 9,500.6138 / 12,989.7054 / 24,466.0144 |

### D45 resource quantiles

Approximate managed allocated-byte counter delta and process-lifetime peak working set sampled at readiness, in MiB; P50 / P95 / P99.

| Map | Cache state | Arm | Approx. allocation P50 / P95 / P99 | Peak working set P50 / P95 / P99 |
|---|---|---|---:|---:|
| MOCKTROID (`room-01`) | application-cold | baseline | 692.938 / 693.556 / 693.559 | 466.930 / 468.027 / 473.605 |
| MOCKTROID (`room-01`) | application-cold | candidate | 642.152 / 642.163 / 642.844 | 462.574 / 463.883 / 463.988 |
| MOCKTROID (`room-01`) | persistent-cache-warm | baseline | 496.794 / 496.813 / 496.816 | 409.918 / 412.129 / 471.938 |
| MOCKTROID (`room-01`) | persistent-cache-warm | candidate | 446.104 / 446.119 / 446.129 | 406.227 / 406.551 / 406.688 |
| DM-DECK16 (`room-02`) | application-cold | baseline | 1,044.333 / 1,044.436 / 1,044.443 | 607.844 / 608.723 / 609.426 |
| DM-DECK16 (`room-02`) | application-cold | candidate | 991.774 / 991.877 / 991.882 | 607.789 / 608.559 / 608.891 |
| DM-DECK16 (`room-02`) | persistent-cache-warm | baseline | 540.336 / 540.352 / 540.364 | 447.812 / 448.371 / 448.605 |
| DM-DECK16 (`room-02`) | persistent-cache-warm | candidate | 487.761 / 487.784 / 487.785 | 446.871 / 447.539 / 447.637 |
| Guardian v20 (`room-03`) | application-cold | baseline | 23,711.594 / 23,749.691 / 23,749.701 | 10,003.020 / 10,315.504 / 10,338.508 |
| Guardian v20 (`room-03`) | application-cold | candidate | 19,313.865 / 19,351.951 / 19,351.954 | 5,798.598 / 6,137.277 / 6,570.008 |
| Guardian v20 (`room-03`) | persistent-cache-warm | baseline | 12,212.637 / 12,212.762 / 12,212.768 | 7,599.621 / 7,739.539 / 7,931.430 |
| Guardian v20 (`room-03`) | persistent-cache-warm | candidate | 7,814.944 / 7,815.101 / 7,815.110 | 5,709.250 / 5,792.672 / 5,883.535 |

The D45 Guardian v20 warm tail regression remains visible: candidate P50 was 12.3% lower, P95 11.2% higher, and P99 48.1% higher than baseline. The P99 in every D45 cell is one maximum observation; no tail values were removed. D45 source commit, source archive hash, and exact runner hash were not captured in its campaign manifest.

## Post-load gameplay CPU diagnostic

The existing frozen `-perfcheck` ran Guardian v20 at 1920×1080, 100% render scale, with eight bots and a requested 60 Hz workload. Two excluded five-second simulation preseeds warmed each arm. Each of the four measured 30-second simulation workloads recorded 1,800 draw and simulation samples. Order alternated baseline/candidate then candidate/baseline. Input provenance was unchanged and private temporary data was removed. Local builds were paused throughout.

These are `Stopwatch` wall durations around simulation and draw submission on the render thread. Drawing excludes `SwapBuffers` and may include driver blocking. They are not processor CPU-time counters, presented-frame intervals, or GPU execution times. `-perfcheck` skips general map correctness probes; its eight spawned players means all spawned during the run, not simultaneous liveness. Its legacy `OnePercentLowFps` fields are reciprocals of draw-duration percentiles and are not observed FPS.

| Pair/order | Draw P95 baseline / candidate (ms) | Draw P99 baseline / candidate (ms) | Simulation P99 baseline / candidate (ms) |
|---|---:|---:|---:|
| 1: baseline then candidate | 6.8658 / 6.8619 | 26.8975 / 26.5257 | 7.5190 / 7.5069 |
| 2: candidate then baseline | 7.5117 / 6.7921 | 26.7526 / 26.4342 | 7.8387 / 7.4179 |

The means of the two per-run draw P95 values were 7.189 / 6.827 ms; those are means of percentiles, not pooled percentiles. Allocation stayed near 51,023 bytes per draw in both builds. Two observations per arm are a small diagnostic and do not establish a statistically reliable speedup, frame-pacing improvement, long-session memory behavior, or correctness qualification. The full arrays and both excluded preseeds are retained in the CPU evidence file. The original unsupported GL attempt and the first CPU harness false-positive on a benign “standard error” log line remain preserved separately.

## Compatibility and platform evidence

| Area | Evidence | What it establishes | Remaining limit |
|---|---|---|---|
| Published package compatibility | Four published packages (MOCKTROID, DM-DECK16, Guardian v20, MORPHEUS PRIME); 24 receipts passed (16 samples and 8 warm preseeds). Both DLL slots used the same baseline build. | The four package inputs loaded through the recorded runtime path. | Compatibility smoke only, not an A/B performance comparison. |
| Interrupted remote package resume | One GET was interrupted after a 2,742,025-byte prefix; the resumed request returned HTTP 206 and completed the 4,395,222-byte MOCKTROID package with SHA-256 `41d62ef662ebbe26a6cb9ac70ee973e2161742d46400032f18965db480fb7268`. | Received bytes matched the expected immutable package hash. | CDN configuration stayed disabled; no production deployment or enabled-CDN path is qualified. |
| Linux upload service | 13/13 contract tests passed. A 128 MiB deterministic synthetic fixture completed in 652.67 ms healthy and 631.94 ms with injected interruption, one retry, and one recovery GET. | Local service persisted and verified the synthetic source and recovered its authoritative offset. | Not a valid map package and not public-network throughput. |
| Linux game smoke | Six subprocesses passed for one public package: four one-sample runs and two warm preseeds. Renderer was Mesa llvmpipe software. | The compact build launched and reached the receipt endpoint on Linux. | Smoke only; not GPU performance or statistical comparison. |
| Android map acceptance | `DEBUG MapAcceptanceActivity` package check plus separate pure texture-helper parity/compact-storage checks passed on a headless Android x86_64 API 35 CPU emulator. Final signed APK SHA-256: `EDDA7D61CC1055E7E59640226AB64F417B2F103825F06E6368EFCCAD14E4E9DD` (85,719,010 bytes). | Package load, verification, download, registration, runtime decode, and pure helper parity were checked. | No physical GPU, Android lifecycle, or device memory-pressure claim. |
| Local repository checks | 518 map-editor checks, multiplayer acceptance, selected server build, and six package-streaming checks passed. One selected gameplay smoke reported 8/8 spawned and 6/8 moved, but its burn probe failed. | Focused editor, package, server, and multiplayer checks have local evidence. | That gameplay smoke was not all-green. The attempted GL frame capture is unsupported because `MapAudit` bypasses renderer profiler hooks; a baseline smoke reached readiness with eight alive, recorded 1,800 total simulation frames, and exited 0, which is not a performance-regression comparison. |
| Hosted CI | Earlier work PRs #1029, #1030–#1035, and Website #16 are merged. The remaining open stack is #1037 → #1041 → #1046 → #1043. | The final performance candidate and range work remain reviewable in the open stack. | CI is running with macOS CI still pending; all required checks must be green before any remaining PR merges. |

## Historical component measurements

These are copied from the report at commit `4042fc68cb5d8257470a18b7a6f0811db63ff773`, `docs/performance/map-pipeline-optimization.md`. They are historical component results, not qualification of the current combined build.

| Component | Historical result | Interpretation limit |
|---|---|---|
| Frozen package fixture | 1,448-byte fixture: cold prepare/build/install 181.213 ms; repeated exact reinstall 8.1852 ms. | Tiny compatibility fixture, not representative throughput. |
| 32 MiB download verification, initial 20-sample pair | Baseline P50/P95 64.9242 / 87.6790 ms; streamed verification 32.5084 / 34.0646 ms. Baseline P99 before the change was 89.5752 ms. | Local primed microbenchmark; not map loading or remote bandwidth. |
| 32 MiB download verification, preserved-assembly recheck | Baseline P50/P95 70.2221 / 100.9367 ms; streamed verification 32.2090 / 38.1336 ms. | Sequential, not randomized; OS scheduling and warm file cache remain. |
| Q3 conversion fixture, 2,709,119-byte source | Cold import 313.78 → 295.61 ms; same-thread allocated bytes 142,296,016 → 101,919,560. Relocated repeat 126.47 ms (miss) → 65.26 ms (hit); allocations 132,287,016 → 82,081,184. Warm fingerprint 2.38 → 1.20 ms; allocations 168,056 → 84,408. | One before/after pair; repeat is warm in both arms; not final packaging or game readiness. |
| Local upload service | 32 MiB fixture: healthy 192.82 ms, interrupted 228.22 ms. 128 MiB fixture: healthy 560.57 ms, interrupted 585.70 ms. Each interrupted run used one retry and one offset GET. | Single-run loopback conformance with fake identity and converter stub; not a real archive or remote service. |
| Near-limit package harness | 503,471,463-byte valid archive; six checks passed. | Synthetic PCM payloads; not representative large-geometry gameplay. |
| Earlier renderer smoke | Both builds had identical 1,048-frame summaries, 8/8 players spawned, 10 spawns, and 30 item spawns; both returned exit 0. The burn probe failed in both arms. | Correctness smoke only; no load-time percentile, and the burn failure remains unresolved. |

## Remaining evidence and merge gate

The attempted GL frame capture could not produce profiler evidence because `MapAudit` bypasses renderer profiler hooks. The baseline smoke reached readiness with eight alive, recorded 1,800 total simulation frames, and exited 0; it is not a performance-regression comparison. The separate existing CPU `-perfcheck` diagnostic at 1920×1080 passed the four measured workloads and both excluded preseeds; it makes no frame-pacing or GPU claim. The remaining open PR stack is #1037 → #1041 → #1046 → #1043; wait for all required CI checks to pass before merge. No CDN enablement, live deployment, or production write was performed.

## Evidence files

- [Sanitized final compact campaign summary and 240 timed receipts plus six excluded preseeds](map-pipeline-evidence/compact-windows-runs.json)
- [Sanitized D45 pre-compact summary and all 240 timed receipts plus six excluded preseeds](map-pipeline-evidence/d45-precompact-runs.json)
- [Sanitized gameplay CPU diagnostic, all four measured arrays and two excluded preseeds](map-pipeline-evidence/guardian-cpu-runs.json)
- [Qualification summary with historical and acceptance evidence](map-pipeline-evidence/qualification-summary.json)

The per-run JSON files retain every receipt stage’s elapsed time, process CPU delta, current and peak working set, approximate allocated-byte delta, and GC counts. Their receipt references point to the exact sanitized record in the same file. Original campaign receipts remain in the ignored workspace evidence tree. No source map content, creator assets, screenshots, or machine-specific paths are included.
