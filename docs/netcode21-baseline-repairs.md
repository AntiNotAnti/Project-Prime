# Netcode 2.1 baseline repairs

This prerequisite follows the evidence-only G0 audit in `netcode21-g0.md`.
It repairs failures reproduced on gameplay baseline
`ed51bc90abb9d29b019620ce70146c8833d590d1`; it does not implement G1 or the
authority/touch feature slices. Live protocol remains 51.

## Shock Coil events at an obstructed muzzle

The baseline eight-client LAN tour offered 22 Shock Coil pulses with phase zero.
Native muzzle collision returned before homing target capture, but event commit
used that capture's cleared tick as the pulse phase. Production compact decoding
then refused the malformed event carriers and the bounded campaign drain failed.

`NetFireEvents.Begin` now owns the phase at the pulse's source frame. Commit keeps
that phase and uses a selected target only when capture belongs to the same
source frame. A blocked muzzle carries an explicit no-target identity. Admission,
damage payment, remote accepted playback and packet layout are unchanged.

The native Sylux fixture exercises owner and accepted remote wall obstruction,
stale target capture, an unobstructed Coil target, Power Beam wall/open/lateral
controls, and native Lockjaw placement. Its expanded 52 assertions fail five
times against the frozen baseline engine and pass with the repair. The owner
fixture uses the host role so the real target selector sees a local owner;
dedicated-server role deliberately has no local slot.

Raw before/after logs and immutable engine/fixture hashes are retained under
`artifacts/net21-baseline-repairs/`. An initial symlink-based control staging run
loaded dependencies beside the original entry assembly; it is explicitly excluded
from before/after evidence. The regular-file control reproduced the failures.

Frame zero remains an existing unsupported continuous phase. This repair does
not claim uint frame-wrap acceptance.

## Replay particle pool state

The original protocol-51 recording diverged in presentation at frame 901 despite
equal gameplay hashes. A particle without a rotation action inherited the previous
pooled particle's angle. Particle birth now resets rotation to zero. Checkpoint
restore also normalizes historical active particles without a rotation action
after rebinding their effect definition; authored rotation remains intact.

The asset-backed fixture exercises actual pool reuse and the production world
checkpoint codec, including preservation of an authored 73-degree angle. This
corrects the original 901 divergence. Historical image hashes involving the
unowned pooled angle may change intentionally.

## Replay pose cursor and bomb facts

After the particle repair, replay continuation still diverged. The independent
pose cursor passed bomb facts into its private decoder without consuming them.
At the decoder's 2,048-packet bound, that cursor failed, stopped indexing accepted
fire events, and fell back to held-input firing. A fresh seek cursor had not
reached the bound, so it emitted different shots.

The pose cursor now accepts only match, roster, snapshot and intent packets,
matching its existing in-memory clip path. The simulation decoder still consumes
bomb-world facts; shot facts keep their separate lookahead. Queue bounds and
canonical event identities remain unchanged. Replay verifiers now fail directly
on cursor errors, including the underlying reason.

The sustained regression uses an actual recorded bomb packet for 4,096 deliveries
and exercises the production simulation consumer as well as the pose cursor.
The regression preserves 180 subsequent gameplay/presentation frames on the
immutable repair build.

## Saved-world firing semantics

Live canonical capture has no future-event index and reconstructs native input
at arrival time. File playback indexes accepted fire events using retained future
carriers. A structurally compatible live capsule therefore cannot safely replace
an indexed file-playback world midway through a match.

World capsule version 6 records whether its producer used a healthy indexed-fire
cursor. For protocol 43 and later, optional seek capsules without that contract
fall back to reconstruction from the recording's exact initial baseline. The
baseline itself remains readable. Rejected offsets are retained and warnings
explain the fallback. Fresh indexed capsules still accelerate file seeks.

An explicit native v6 clip continues its producer's native reconstruction policy,
including when nested. Full-match file playback retains indexed firing. Extracted full-match clips record their indexed consumer policy separately from
the unchanged producer capsule. Nested clips preserve that consumer policy.
Older unmarked clip adapters retain their existing policy. This distinguishes producer
semantics; it is not a rewrite of the live capture producer or a new claim of
authority-verified clip shots.

The explicit continuation field uses replay container version 5. Versions 1–4
remain readable; older applications intentionally reject version 5 instead of
misinterpreting its policy. Capsule version 6 and replay container version 5 are
separate from live network protocol 51.

The initial seek through older/live-produced capsules can be slower, and may
reject several earlier capsules before completing. Work per update stays bounded
at 120 simulation steps. Long-recording seek performance and actual historical
protocol-41/50 files remain unmeasured; format fixtures are separate evidence.
Live UDP protocol remains 51. No release, device, or performance acceptance is
claimed by this prerequisite.

## Validation and rollback

The Python evidence suite passes 358 tests with one existing optional local
campaign fixture skipped. The immutable repair source is `ab9a7c8a248a5d90a9191982fc7265dd25d3befe`;
its engine SHA-256 is
`e0a8cd889db6556975a45bdf2d9c1dcc87de208fb5513982789bbe9315b2a1bf`.
It passes 52 native muzzle assertions, 667 continuous combat assertions,
compact intents, shot lifecycle, source journal and queue budgets. Replay format
validation passes 3,031 checks, including v5 compatibility and malformed inputs.

The original failed recording passes all 1,664 gameplay/presentation frames,
14 cold/cached targets and five playback rates. Replica checks cover 1,363
continuation frames, six world restores and 737 projectile frames. Durable
seeks, corruption fallback/recovery, full-match extraction and native/nested
clip fidelity pass. Both fresh duel recordings also pass 2,008/1,994 frame determinism checks;
the fresh replica check preserves 1,963 continuation frames across seven world
restores. These are correctness checks, not performance comparisons.
Fresh 30-second real-UDP campaigns pass with 2/4/8 clients on LAN. All
27/372/553 offered shots, respectively, reach authority acceptance, with
3/6/19 component payments. No continuous shot row has phase zero. The
four- and eight-client runs use the native hunter tour; the two-client run uses
the duel and records both peers. These counts do not cover separate bomb or
continuous damage payment paths.

The eight-client 150 ms added RTT, 0–20 ms extra hold per direction and 5% random
loss run accepts all 533 offered shots and records 16 component payments, but
fails its 20-second drain. Two admitted Lockjaw bombs have native 1,800-frame
lifetimes; their exact countdowns continue normally, and one detonates exactly
at placement +1,800 frames. The second still needs 77 frames at the deadline,
then terminal delivery and claim grace. The original failure remains retained;
a same-profile repeat with a 30-second drain passes with all 544
offered shots accepted, 29 component payments, empty authority combat
work and a completed reliable/frontier barrier. This is not symmetric
±20 ms jitter and is not a performance comparison.

G0's build workflow completed successfully on
`de3d80fc03b09fe5e80754daf0e1666747977dc8`: all 25 jobs passed in
[run 38183438512](https://github.com/AntiNotAnti/Project-Prime/actions/runs/38183438512).
Those checks precede these gameplay repairs and are not their CI result.

The asset exclusion guard passes. The workspace OpenGL guard detects an
existing ignored `tools/modern-shaders` directory; that directory is preserved.
The same guard passes against an exact tracked-source export of the repair SHA.

Machine-readable commands, hashes, counts, exclusions and failed attempts are
in `tools/nettest/baselines/net21-baseline-repairs-20261011.json`. Raw evidence is
retained locally under `artifacts/net21-baseline-repairs/`.

Retain the original failed campaigns and recordings as controls. Rollback reverts
the repair commits together with their regression tests; this prerequisite does
not change a live packet format or require mismatched client/server deployment.
