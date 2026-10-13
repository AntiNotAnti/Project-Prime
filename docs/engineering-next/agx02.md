# AGX-02: compiler/draw and lifecycle controls

**Outcome: reproducible driver-origin-associated allocation behavior, not a definitive driver leak.** The tiny GL-only control retains outstanding direct malloc-prefix AGX fragment groups after complete observed application GL-name release and context disposal. Linking without drawing remains near the context baseline; repeatedly drawing newly linked programs grows the groups; reusing one tiny program for 120 draws leaves a much smaller population. This identifies a useful churn hypothesis. It does not establish retaining owners, boundedness, full-scene program reuse or an accepted runtime mitigation.

All six serial controls at source `cdbaa4bc5492bf1876511d6c973ef79a47779fc8` passed immutable evidence validation. They run before game/user-data/Scene/audio initialization. All observed compile/link statuses succeed; every retirement reports zero recognized owned names and no cleanup errors. Drawing arms produce 128 nonblack pixels; repeated same-program observations change 128 pixels. Measured scope: Apple M4 Pro, macOS 27.0 build 26A428, OpenGL 2.1 Metal - 91.7, OpenTK 4.9.4 and .NET 10.0.12.

| Actual control | Links / draws | Direct AGX fragment MiB after final context disposal |
|---|---:|---:|
| Tiny compile-only, 120 iterations | 120 / 0 | 0.018051 |
| Tiny compile/draw/delete, 120 iterations | 120 / 120 | 3.353470 |
| Tiny compile once/rebind/draw, 120 draws | 1 / 120 | 0.024246 |
| Canonical/native/plain/unlit four-prewarm, 120 iterations | 480 / 480 | 48.143539 |
| No application shaders, two contexts | 0 / 0 | 0.018021 |
| Tiny draw in three fresh unshared contexts | 3 / 3 | 0.101822 |

The starting population is approximately 0.018509 MiB. These are current outstanding origin groups, not resident GPU bytes, full allocator-zone totals or retaining-owner proof. The six arms are separate fresh processes, not repeated increasing-load plateau windows. The four-prewarm control initializes only a synthetic untextured draw contract and changing override color; it is not game fidelity validation. Optional define/attribute/sampler permutations and true shared contexts remain NOT_RUN. The tiny fragment has no active sampler.

## Immutable source, operation and release contract

`--minimal-gl` exposes `no-shader`, `compile-only`, `compile-delete`, `fresh-contexts` and `rebind-draw`; sources include `tiny`, canonical/native/plain/unlit and `four-prewarm`. Exact expanded vertex/fragment hashes and explicit attribute/default-link input hashes are separate from limited synthetic draw-state hashes. Identical GLSL is not a complete driver pipeline key. Every shader is detached/deleted after linking; the owning thread/current context is checked; programs are unbound/deleted and queried before window disposal. Partial initialization continues cleanup attempts and retains original and cleanup failures. Native capture acknowledgements hold before-workload, after-workload and after-disposal phases. The serial runner preserves commands, loaded identities, source/binary hashes, complete native stdout and optional lossless gzip hashes.

```sh
python3 tools/native-render-check/run_minimal_gl_controls.py \
  --binary /path/to/native-render-check.dll --output /fresh/path \
  --mode compile-delete --variant tiny --iterations 120 \
  --capture-native-stacks --compress-stacks
```

Profiling changes allocation/CPU behavior; these arms are not FPS evidence. Device-free failure injection covers initialization/binding/draw rollback and malformed inputs. Native failure-injection runs, shared contexts, second-GPU evidence and non-macOS native lifecycle acceptance remain NOT_RUN.

## Real-scene counter correction and accepted scope

The first expanded scene fixture at `cdbaa4bc` passed RGBA/depth parity and recognized-name retirement but double-counted nested facade/owner diagnostics: 64 shader and 36 link events across two loads are **INVALID_FOR_ATTEMPT_ATTRIBUTION**, not actual native operations. The failed attribution receipt is retained with its hash in [compact results](agx02-results.json).

Correction `1866fc2b25e7e19023415d2cfe79c00e22ed5a85` counts actual GraphicsApi CompileShader/LinkProgram attempts and statuses once; owner hooks supply source attribution only. Sources are sampled at each actual compile; source replacement and create/delete boundaries handle repeated compilation and reused names. The separate one-cycle/two-load 120-frame map fixture reports **32 shader compilations / 18 links**, all successful, COMPLETE_OBSERVED status coverage, zero unexpected callbacks/overflow, 14 stage/source keys and 12 source hashes. Exact RGBA/depth hashes match across the two loads, recognized owned names/current program are zero after each unload, and the loaded binary hashes match the corrected candidate. The compact receipt includes the result, client/harness hashes and MVIDs. See [counter correction](agx02-counter-correction.md).

The hosted Ubuntu assets gate builds the harness and runs `-resource-shader-ledger-selftest`, `--minimal-gl-selftest` and `-resource-context-selftest` before native/game initialization. The ledger regression tests use the real callback dispatcher with fake readers and verify nested events, repeated source assignment/compile/link, reused native names, generic routes, failure statuses and disabled no-query behavior. Hosted device-free tests do not replace native acceptance of unexecuted paths.

## Disposition and rollback

**AGX-03: NOT_ELIGIBLE for production leases/caches.** Full real-scene uniform/layout/cache-generation reset, complete link/pipeline inputs, simultaneous foreground/replay/Studio/launcher/side-scene ownership, bounded live-context leases and partial-init/fallback/context-recreation failure contracts remain unproven. Do not share across destroyed or unshared contexts, add a global cache, or change defaults from this diagnostic contrast.

**AGX-04: UNRESOLVED_P0. Program acceptance: NOT_ACCEPTED.** Full-scene 60/300-load evidence continues materially beyond matched control variation; no runtime mitigation is accepted. Restarting a process resets the observed baseline and is an operational observation only. 600/900 same-context windows, another macOS/GPU/OS combination and Windows/Linux/GLES native lifecycle evidence remain NOT_RUN.

Rollback is instrumentation-only: omit `--minimal-gl` and `-resource-shader-source-ledger` for ordinary startup, or revert the diagnostic harness/facade callback additions and their CI/docs. Thread-local callbacks are null during normal startup; no cache, GC policy, shader source, renderer backend or optimization default was introduced. Historical six-arm results retain their original source/binary qualification and are not relabeled as runs of later diagnostic/CI/documentation commits.
