# GL completion: measured rejection and reproducible controls

**GL-02 is rejected for default enablement; GL-03 has no eligible new merge; GL-04 remains NO_ROLLOUT.** Existing renderer defaults remain in place. This completion package adds a portable verifier and serial native controls; it does not change rendering code or re-run a rejected VBO/procedural proposal.

The existing GL-02 and GL-04 documents already record the completed historical native parity. The [portable evidence manifest](gl-evidence/manifest.json) now makes their raw timing evidence reviewable in the repository: path-sanitized derivative result JSONs and logs, all five accepted samples per arm, the excluded warmup sample and repeat, separate profiles, capture-result receipts, original comparison receipt, and the initial wrong-loaded-assembly campaign. Each receipt binds the original SHA-256/byte length and derivative SHA-256/byte length separately, with equal scoped numeric projections. Private host-path roots become stable `${WORKSPACE}`/`${HOME}` labels; numeric observations and loaded assembly identities remain unchanged. Original payloads remain external. The [new verification receipt](gl-evidence/validation.json) recomputes arrays and medians from derivative JSON with unchanged numerical observations, rather than trusting the prior summary. The native harness does not export individual completed-frame interval arrays; the preserved arrays are per-run percentile observations, and profile logs retain every original complete window.

Source `9104134633ebe0996934260f6b54e448d1ec6195`, client SHA-256 `bba1a0357a94ee9a489aec526bc30c1c78c36d61df9303c9376338d06452b909`, loaded MVID `d846f12d-ce73-4bbb-bb1e-6bb81accec6f`, Apple M4 Pro/macOS, GL 2.1 Metal - 91.7. These identities certify that historical candidate only. A new integration build needs new receipts.

| Metric | Historical OFF | Historical ON | Candidate change |
| --- | ---: | ---: | ---: |
| Median per-run p50 (ms) | 2.2099 | 2.1414 | -3.10% |
| Median per-run p95 (ms) | 4.1257 | 4.3387 | +5.16% |
| Median per-run p99 (ms) | 4.8943 | 5.4449 | +11.25% |
| Recorded exact decoded RGBA comparisons | 18 | 18 | PASS |
| Recorded exact float32 depth comparisons | 11 | 11 | PASS |

Each performance arm has five accepted 12-second samples in one fixed interleaved campaign; the first OFF run was conservatively excluded for overlapping build activity, retained and repeated. Captures and profiled runs are excluded from those timing medians. These are descriptive medians of per-run percentiles, without a significance or cross-device improvement claim. Portable verification validates the original parity receipt and capture-result identities; it does not recompare the large original PNG/depth files, which remain in the external native evidence bundle.

No narrow causal defect has been established that would justify another source edit. The cache adds a bounded 512-float scratch and an `Array.Copy` for every supported request while retaining independent item snapshots. It removes repeated scaling; it does not remove uniform uploads. That extra copy is an observable operation, but the captured palette-collection median window p95 improves from 0.012205 to 0.006998 ms. It cannot honestly be blamed for the total-frame tail deterioration from these measurements. The safe disposition is to reject default enablement and retain the failed timing evidence. `PROJECT_PRIME_GL_PALETTE_PREP=0`, or unset, is the rollback. Forced legacy also disables the experiment.

GL-03's 5,510 exact adjacent object-state reuse pairs out of 2,548,029 procedural item submissions give a 0.216245% necessary-condition frequency bound before pass/stencil/topology/order restrictions. This is not a GPU-time or driver-draw savings bound. Preserve [the source-backed disposition](../audits/engineering-consolidation/gl03-procedural-disposition.md), default-enabled `PROCEDURAL_BATCH`, and default-off `PROCEDURAL_STREAM`.

## Portable verification

From the repository root, these commands perform no native GL work:

```sh
python3 tools/native-render-check/gl_comparison.py validate \
  docs/engineering-next/gl-evidence/manifest.json
python3 tools/native-render-check/sanitize_gl_evidence.py verify \
  docs/engineering-next/gl-evidence
python3 -m unittest discover -s tools/native-render-check -p test_gl_comparison.py
```

The verifier rejects a changed derivative hash, wrong loaded MVID, profile/readback contamination of timing, mixed fixture dimensions/driver/settings, duplicate identities, undocumented exclusions, insufficient accepted sample counts, missing warmup marker, simulation stalls/dropped steps, or retained owned GL names. Its integrity PASS does not mean the performance candidate passed; that result remains REJECTED. The small adversarial suite covers corruption, loading, cleanup, contamination and sample-selection failures.

## Current-integration native recipes

Build/stage one current Release harness and its `ProjectPrime.dll` and runtime dependencies in the same **physical**, immutable directory before the exclusive native slot. Do not symlink a harness to another build. Record its source commit, client/harness hashes and expected client MVID. Keep the source tree clean and extracted assets unchanged. `$GL_ASSET_PATHS` points to a directory containing only the operator's `paths.txt`; the runner copies only asset paths into isolated fresh settings. `$GL_MVID` must be the expected current client assembly identity, not the historical value above unless using that exact historical binary. Set `$GL_BINARY`, `$DOTNET` and fresh absolute output paths for the staged build.

The run command defaults to a dry schedule; **append `--execute` only while native GL, builds, tests, the game and competing profiling work are held exclusively**. Record actual host/idle conditions in `--idle-note`. Every completed arm checks the expected loaded MVID and unchanged binary hashes; a timeout or incomplete arm remains on disk. No result directory is overwritten. The current harness supports macOS and Windows; Linux and physical GLES require another actual harness/device and remain NOT_RUN here.

```sh
python3 tools/native-render-check/gl_comparison.py run \
  --source-root "$PWD" --dotnet "$DOTNET" --binary "$GL_BINARY" \
  --client-mvid "$GL_MVID" --user-data "$GL_ASSET_PATHS" \
  --output "$GL_TIMING_OUTPUT" --kind unprofiled \
  --idle-note 'Game closed; local builds/tests held; idle host; record actual host conditions'
```

The ten-run timing order is `OFF, ON, ON, OFF, OFF, ON, ON, OFF, OFF, ON` (five samples per arm). Both flags run in the same binary, Original preset, fixed RNG12345/98765, eight players, Proving Ground, High shadows, 1920×1080, uncapped and VSync off. The native harness excludes its first 240 simulation steps. Profiling, readback and native allocation tooling are absent. A launch cannot establish input latency, physical panel rates or cap overshoot.

For the focused current-integration correctness/resource smoke, use the same command with a fresh `$GL_REGRESSION_OUTPUT` and `--kind regression`. This schedules only the eight-player High 1080p OFF/ON capture pair, comparing exact decoded world/HUD/repeat images and float32 world/repeat/shadow depth while enforcing zero retired owner/name fields in both contexts. It runs no timing campaign and cannot establish a performance win. Its baseline is PALETTE_PREP OFF in the same integration binary. A deliberately mixed-build historical-vs-integration comparison needs separate source/MVID identities; do not present it as same-binary evidence.

For separate exact captures, run the same command with a fresh `$GL_PARITY_OUTPUT` and `--kind parity`. This uses OFF/ON eight-player High 1080p, ON/OFF solo Kanden shadows Off 720p, then OFF/ON one-bot Spire High 1080p. Both arms advance the same simulation frames240/540/840/1140, stabilize kill-feed lifetime only for capture, compare decoded RGBA at original dimensions and bitwise native-endian float32 world/repeat/shadow depth, and retain every capture/digest. Executed parity needs Python Pillow. Near/far/alpha/stencil/Studio/replay and every hunter are separate remaining gates.

For attribution, use a fresh `$GL_PROFILE_OUTPUT` and `--kind profile` (ON then OFF, 25 seconds each). Summarize each completed log with exactly four completed post-marker windows discarded:

```sh
python3 tools/opengl-modernization/draw_attribution.py \
  "$GL_PROFILE_OUTPUT/00-eight-high-palette-1.log" --warmup-windows 4 \
  --output "$GL_PROFILE_OUTPUT/palette-on-attribution.json"
python3 tools/opengl-modernization/draw_attribution.py \
  "$GL_PROFILE_OUTPUT/01-eight-high-palette-0.log" --warmup-windows 4 \
  --output "$GL_PROFILE_OUTPUT/palette-off-attribution.json"
```

Profiled inclusive CPU scopes are attribution evidence; they must not replace unprofiled frame-tail acceptance. Unsupported native GPU queries stay UNVERIFIED.

For current optimized-renderer resource stress, use the existing AGX-04 runner in another exclusive slot. Run forward and then reverse subsystem order for both palette flags; use fresh output directories for each and the same staged binary. This covers resource attribution, not frame timing or a complete Studio UI/rendered replay campaign:

```sh
PROJECT_PRIME_USER_DATA="$GL_ASSET_PATHS" PROJECT_PRIME_GL_PALETTE_PREP=0 \
  python3 tools/native-render-check/run_resource_arms.py --dotnet "$DOTNET" \
  --binary "$GL_BINARY" --output "$GL_RESOURCE_OFF_FORWARD" \
  --arms combined map-gl studio audio --cycles 60 --warmup-cycles 5 \
  --quiescent-samples 30 --quiescent-ms 1000
PROJECT_PRIME_USER_DATA="$GL_ASSET_PATHS" PROJECT_PRIME_GL_PALETTE_PREP=1 \
  python3 tools/native-render-check/run_resource_arms.py --dotnet "$DOTNET" \
  --binary "$GL_BINARY" --output "$GL_RESOURCE_ON_FORWARD" \
  --arms combined map-gl studio audio --cycles 60 --warmup-cycles 5 \
  --quiescent-samples 30 --quiescent-ms 1000
```

Repeat with `--arms audio studio map-gl combined` and fresh `$GL_RESOURCE_ON_REVERSE`/`$GL_RESOURCE_OFF_REVERSE`, running ON before OFF. Use isolated paths-only user-data directories for each arm as in AGX-04. Validate the existing resource-runner loaded hashes and lifecycle receipts. Do not substitute owner-name retirement or RSS for a native driver-residency plateau proof.

| Release gate | Current disposition |
| --- | --- |
| Historical M4 Pro exact named captures | PASS for historical candidate only |
| Historical M4 Pro frame tails | FAIL; candidate default remains OFF |
| Current integration native timing/parity/resources | NOT_RUN by this portable package |
| Windows GL/Linux GL/physical Android GLES | NOT_RUN |
| Second macOS GPU/OS | NOT_RUN |
| Native GPU timing on recorded M4 Pro | UNVERIFIED |
| Full rendering/presentation/Studio/replay/resource matrix | INCOMPLETE |

[Machine-readable completion receipt](gl-completion-results.json) separates measured rejection, preserved raw evidence and unrun device/workload gates. An unrun gate cannot promote the candidate. GL-04's allowed outcome is defaults staying off; this package provides that concrete policy decision and reproducible future controls.
