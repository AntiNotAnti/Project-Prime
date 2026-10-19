# CPP-03/04 presentation completion — October 19, 2026

Status: **implementation complete; macOS validation passes**. Broader release/platform qualification retains the limits below.

The resumed presentation work extends the existing C# owners for completed entity, camera, cannon, animation and effect poses. Simulation remains fixed at 60 Hz. Draw fractions select completed endpoints without extrapolation or gameplay prediction.

## Implementation

- Camera translation, cannon-local offsets and scope FOV share the live presentation-generation fence. Focus/clock resets hold the current canonical pose until both endpoints belong to the new generation. Replay retains its own seek timeline.
- Free particles capture completed position, velocity, color, opacity, scale and rotation in their existing pooled owner. Native draw functions read a temporary sample; submitted packets own their copied geometry. Canonical particle geometry, material values and emitter transforms are restored. Birth/reuse, emitter identity, function/flag changes, scene/seek/clock changes, gaps and uncaptured edits fence carry.
- Projectile heads, backs, direction, velocity, ten trail points and model branches consume the same valid draw interval. Per-step projectile speed permits legitimate fast travel while larger discontinuities rebase. Impact anchors remain authoritative. Continuous beams attach to the displayed local barrel with matching world/viewmodel projection, and offline target attachments follow the selected actor draw position without retargeting combat.
- Alternate-form rotation, Kanden relative segment shapes and Spire attack orientation use completed histories. Shape interpolation is offline; existing network/replay actor placement supplies their base offset. Every changed specialized node returns to canonical state after submission, including translated network/replay Kanden nodes. Spire attack entry resets its orientation history independently of animation-frame availability.

New private history and diagnostic fields are excluded from frozen replay serialization. The historical world/accessor contract remains `A4512223EC0CC49516DB00DD3733168DA6969DEA059E9DDE84B31062ADEA675A`.

## Source and evidence

Evidence lives under `artifacts/cpp03-cpp04-completion-20261019/`. `complete-source-manifest.json` pins the final isolated source snapshot; `complete-production-inputs.json` records the presentation files. The shared checkout contains concurrent UI work. The snapshot uses the previously buildable UI base plus the presentation changes, excluding two unrelated new Studio HUD/overlay hunks in Renderer; `excluded-concurrent-renderer-ui.diff` records the isolation-time differences; `final-root-renderer-ui.diff` records the final two UI-only differences. All presentation changes are present in the shared checkout. Those UI edits remain intact in the shared checkout. No whole-checkout build claim is made for concurrent unfinished UI work.

The first particle/focus-only native attempt is preserved separately in `native-matrix-attempt1/interruption.json`: ten accepted arms, stopped for the required projectile correction. Its passes do not fill the final matrix.

## Verification

Client/nettest, native harness and isolated dedicated-server Release builds pass with zero warnings/errors. Forced recompilation was used for the final client and server; restore used the local NuGet cache. The native runtime is unchanged during the matrix.

| Check | Result |
| --- | --- |
| Frame timing / camera / cannon / hit reaction / focus fence | All cases pass |
| Production transform-history fixture | 77,268 checks; identical canonical stream across all four schedules; warmed reads allocate 0 bytes |
| Actual scene/entity integration | 69 checks; exact historical field-block bytes and schema |
| Animation sampling | 4,916 checks |
| Particle geometry, packet/material cleanup and owner overrides | 425 checks; 20,000 warmed canonical/sample/restore geometry cycles allocate 0 bytes |
| Beam head/trail/model coherence and continuity | 196 checks, including fast travel and 108 projection combinations |
| Specialized Hunter poses and actual Kanden Draw cleanup | 208 checks |
| Architecture / pickup-replay / replay-form history | Pass / 83 checks / 43 checks |
| Python analyzer and source-linked boundaries | 25 pass, no skips |
| Content fidelity, 60 and 144 Hz | 46/47 at each rate; all passing hashes exact across rates and the earlier candidate |
| Known missile splash discrepancy | All 360 checkpoints exact across rates and the earlier clean-reference result; no baseline changes |
| Headless loaded replay | 1,979 exact gameplay/presentation rows; 16,533 animation restores; 7 detached worlds; 1,959 continuation frames; 706 projectile frames |
| Replay seeks / frozen clips | 62 comparisons; 6 checkpoints; 4,431,323 retained bytes |

CPU logs and command/exit/hash receipts are in `complete-regressions/`. `completion-content-summary.json` records the source, binary, content and replay comparisons. The old recording correctly falls back from six incompatible durable checkpoints to its recording baseline; the check separately validates current checkpoint capture/restore. It does not certify the old durable fast path.

The Python boundary fixture originally supplied three reflection arguments to the extended four-argument history capture method. Its compatibility correction accepts either signature; all 25 tests then pass. The initial failure, corrected log and exact test-only patch are retained in `validation-test-receipt.json`. The frozen C# inputs and native runtime were unchanged.

The fresh final native matrix passes **28/28** unique cells: seven Hunters at requested output schedules **60/120/144/240 Hz**, using `native-pose-charge-retreat-v2`. The immutable runtime and all 3,858 asset files pass before/after identity checks. Client SHA-256 is `7c78c4b63591cdca382a77c2b43a8562afc4d56d4973ce8635e545b921a204a2` in native, CPU and replay runs.

| Native denominator | Accepted result |
| --- | ---: |
| Canonical simulation steps | 40,320 |
| Output frames | 94,759 |
| Strict camera entry/exit transactions | 364 |
| Positive gun/charge pixel controls with exact restoration | 56 |
| Exact within-Hunter cross-rate stream comparisons | 21 |
| Zero-step canonical checks | 54,418 |
| Repeated-draw canonical checks | 1,092 |
| Capture file hashes independently verified | 1,120 |

`native-matrix-complete/summary.json` records the serial run. `native-completion-summary.json` independently rechecks every raw result/receipt, exact canonical and prepared/consumed-input streams, strict analyzer output, capture hashes and complete runtime/asset inventory. All 60 Hz beam/particle observations remain current; every higher-rate cell observes positive completed-history sampling. These counters witness consumed native values; isolated beam/free-particle pixel proof is not claimed. Separate gun/charge controls prove positive pixels and exact restoration.

The actual native rendered replay check also exits zero: **seven linear/restored-world PNG pairs match exactly**, and the surviving interleaved world has exact before/after-disposal pixels. It completes the 1,979-frame replica and 62-comparison seek/clip suite. `complete-replay-rendered/receipt.json` pins its command, log, unchanged client/source replay and all images; `rendered-replay-verification.json` independently compares the seven pairs and disposal images. Headless and rendered checks are complementary: the headless run additionally compares presentation hashes during seek reconstruction.

Root visually inspected the unmodified Samus charge, Kanden segment chain and Spire alternate-attack captures. `visual-inspection.json` pins those stills; static inspection does not independently establish temporal smoothness.

## Qualification limits

Acceptance is scoped to the requested macOS implementation and the recorded offline native scenario. The native readback fixture is a correctness test; requested schedules are not physical panel scanout or frame-time measurements. The earlier RML77 performance archive was independently hash-verified and reanalyzed (28 cells, 75,225 completed application frames, 33,600 measured canonical steps), but those timings qualify that historical build. They are not new performance measurements of this source.

OS focus event delivery, impaired live-network play, Windows/Linux and physical Android behavior remain separate platform/runtime qualification. Current CPU fixtures exercise focus-generation recovery, replica seek ownership, continuity boundaries and canonical restoration. The native seven-Hunter scenario and content regression are bounded acceptance, not every weapon/map combination or original DS accuracy.

The content fidelity oracle retains the pre-existing missile-splash baseline discrepancy. Its clean-reference investigation is preserved at `artifacts/cpp04-fidelity-content-20261017/reference-investigation.md`; no baseline or comparison tolerance is rewritten.
