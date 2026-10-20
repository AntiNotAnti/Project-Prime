# Slide readability and crouch gait

Date: 2026-10-20. Classic remains the default. Traversal profile v4 uses live
protocol 74. Quake III remains profile v3. Historical Traversal protocols 71–73
retain v3; earlier profile classes remain unchanged. The final integration base
is main `790f8e1283694a5a243ce6bae68180af7d69e076`, including private custom
knowledge carriage (73), explicit ownership absence (72), and custom archive
refusal from protocol 70.

## Reproduction and change

The previous ordinary-run test released Forward when Ctrl was pressed. The
reported held-Forward case needed its own reproduction. On the untouched
protocol-72 main `8cbc4c0779b87a5f802e57c64a03b8c4f1a5cfd5`, actual input tests for
all seven Hunters showed identical physical slides with Forward held or released:
48 ticks (0.8 seconds), 4.21759 world units, starting at 7.19971 units/second and
ending at 3.78004. No speed or slide state was fabricated. Holding Forward kept
the native WalkForward animation active throughout, matching ordinary crouch
walking; releasing Forward selected Idle. The baseline passed 89,013 checks.
This established a presentation defect rather than Forward cancelling slide
physics on clear ground. Walls, lost support and existing native ability
exclusions can still legitimately end a slide.

Traversal v4 extends the earned slide to 72 fixed simulation ticks (1.2 seconds)
with friction 0.45. Entry threshold and the six-tick single-use landing buffer
are unchanged. No speed impulse or ground steering acceleration is added.
Holding Ctrl cannot refresh the slide. Fresh accepted Jump, release, wall contact,
lost support and native exclusions retain their cancellation rules.

The visual slide uses a stable native idle leg pose while the body glides,
including when Forward remains held. Crouch-walking shortens the native stride
to 65% and swing-foot lift to 40%, around each Hunter's native neutral stance.
A separate cosmetic phase follows accepted horizontal travel. Walking upper-body
clips follow the same phase; shot/charge/ability clips keep their native clocks.
Two-bone solving preserves leg lengths, and ankle descendants move with their
adjusted foot target. This is not a world-space foot-lock system.

Camera/cannon walking bob uses 30% of its native contribution during crouch
walking and none during a glide. These corrections are render-only. Camera bob
is captured where the native camera actually applies it, before the later native
bob tick; reconstructing it afterward would subtract the wrong sample. Native
CameraInfo, animation cursors, collider, aim and source-time muzzle facts remain
unchanged. The canonical six-argument biped helper explicitly bypasses the
cosmetic gait, and draw cleanup restores canonical nodes even at full crouch.

Native idle foot anchors are sampled once per cached skeleton and all temporary
nodes are restored. Repeated pictures cannot advance gait phase. Timing resets,
teleports and missing completed ticks reseed presentation history. Replica glide
classification is visual only and grants no slide/jump/input entitlement.

## Verification scope

All builds, asset-backed gameplay and rendering run on Windows. The baseline
uses actual keyboard input under native simulation, with one explicitly labeled
initial lane placement. Test logs, exact source/binary hashes and sanitized
selected results are recorded with this slice. Extracted assets and rendered
screenshots remain local.

The new coverage includes held-Forward/released-Forward/cold-crouch-walk controls,
full directional native and retimed gait cycles for all seven Hunters at both
LODs, leg lengths, stride/lift/drift measurements, canonical source restoration,
repeated draw purity, bob/history fences, current/historical replay attribution,
and unchanged native movement goldens. Native GL checks separately exercise
actual forward/backward/strafe crouch walks and held-Forward slides.

Whole-frame timing and physical keyboard/controller feel are not established by
warmed allocation loops or scripted image capture. Other-platform playtesting
remains outside this Windows run. Hosted CI status is reported on the PR; merge
does not wait for CI or review, as requested.

## Recorded results

- Normal Windows Release build: zero warnings/errors; exact source and binaries
  remained unchanged through the runs.
- Nine focused gates passed, including 89,569 stance scene assertions, 1,926
  stance/gait rule assertions, all directional gait probes, native 56-case golden
  trajectories, profile math, metadata, replay compatibility, architecture and HUD
  telemetry. Detailed stance-network and protocol-58 archive checks also passed.
- Latest custom contracts: 16,907 passed. HUD/runtime/menu and static source
  contract checks passed.
- Samus held-Forward slide: 72 ticks, 6.6445 world units; speed 7.19971 → 4.18709
  units/second. It remained above ordinary crouch walking (3.59985) throughout,
  added no speed impulse, and matched released-Forward physics. All seven Hunters
  passed this comparison and non-retrigger checks.
- Real 40-second UDP authority/two-independent-owner/native-AI-bot cohort passed.
  Its protocol-74/profile-v4 recording has 2,400 frames and passed 116,975 passive
  replay checks, including 7,200 stance comparisons and 401 crouched frames.
  Genuine protocol-61/62/68/69/71 recordings also passed with unchanged input
  archive hashes. Earlier Traversal profile attribution remains intact.
- Actual NVIDIA OpenGL rendering passed full forward/backward/left/right crouch
  cycles and held-Forward slides. Fifty-five positive body/hidden/repeat groups
  produced 165 local PNGs. Sliding samples use steady rendered Idle frame 0 while
  native WalkForward continues canonically. Repeated RGB is identical, and the
  continuous presentation generation remains stable. Selected images were
  inspected locally; extracted assets and PNGs are not published.
- All owned QA tasks were removed and runtime process count returned to zero.

The [sanitized evidence](evidence/slide-gait-selection.json) records exact hashes
and scope. Retained failed attempts include an ambiguous GL enum in the test
helper (qualified explicitly), an initial native room whose forward path left
its platform (the unchanged fixture passed in MP3 PROVING GROUND), and a stale
provenance filename in collection after successful cohort exits (collection-only
correction). No movement/visual acceptance assertion was weakened.
