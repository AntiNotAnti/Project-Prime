# Authority turret outcomes — G1 prerequisite

Status: implemented candidate; fresh real-UDP and replay acceptance pending. This repairs the two admitted-but-unemitted Weavel offers exposed by the G1 eight-client campaigns. It does not establish native Weavel physics parity or complete G1.

## Contract

Protocol 52 adds packet 72, `TurretFireOutcome`. Its 123-byte record contains the full original ShotKey, the independent authority decision frame, terminal status/reason, original 74-byte FireEvent, and separate native emission position/direction. A single reliable datagram is 151 bytes including the 24-byte network header and four-byte reliable envelope. Batches remain below 1200 bytes.

The authority publishes Emitted only from successful native turret Commit. Target, health, freeze and cooldown gates remain native. Recovery expiry and owner retirement publish Refused with zero emission geometry; the original expiry/retirement evidence remains visible. An expiry is not an emission. The existing pending/retired-source fences continue to prohibit hit-claim rescue for unemitted sources. CombatAck remains the only claim-debit settlement path.

Authority relays remove turret offers from observer intents while preserving other fire history and movement. Native bots use the same outcome publication path. Owners retain immediate local prediction; a refusal destroys only flights and children matching its entire ShotKey. Existing claims still receive ordinary authority receipts. Observers emit from the authority geometry once. An old-life result never creates a turret or splits body health.

Dedicated and legacy host fanout retain 2048 outcomes; transport refusal keeps each cursor stationary for retry, and overrun disconnects the slow peer. The outcome lane uses reliable noncritical delivery and realtime inbox priority. Initial world bootstrap establishes the recipient cursor. Client intake is bounded and authority-endpoint authenticated. Drain includes retained delivery and unapplied outcomes.

## Replay

New recordings omit turret offers from canonical intent copies and retain outcome facts. Protocol 51 and earlier retain their existing offer interpretation. Indexed playback maps authority decision time through recorded server snapshots; it never treats owner source time as authority time. Multiple emissions at one presentation frame are drained individually.

Live replay producers and native-continuation clips apply outcomes at arrival time, preserving the world they could actually capture. Indexed playback owns its separate projected emission path. Replica checkpoint v9 retains pending outcomes and full-key deduplication; world capsule v8 identifies this contract without changing the legacy object graph schema. Old optional capsules without the new contract cannot accelerate protocol-52 playback.

## Validation and remaining gates

Development checks passed: build (33 existing warnings, zero errors), 245 turret codec/delivery/recorder/checkpoint assertions, 25 asset-backed native gate/outcome assertions, 3034 replay format checks, and 409 Python tests (one existing skip). Existing form, bomb, AI, input, compact intent, shot lifecycle, source journal, continuous combat, muzzle and queue fixtures also passed. These working-tree checks are not immutable live acceptance.

The protocol-52 offline audit requires the exact authority terminal payload, native emission or original expiry/retirement observation, matching client receipts, and applied refusals. A refusal cannot coexist with an authority commit or payment. Missing terminals or receipts fail; historical reports retain their prior rules and failed artifacts.

The first immutable candidate, `a0a120b5afa344d2ed6ef79c6be8a01edb56fd65` (engine SHA-256 `d840756ad61d433186f3c2375524cdd46c1adb995731797119c7a78aa15d5817`), passed a 60-second eight-client campaign at 250 ms RTT, 40 ms jitter, 5% loss, 2% duplication and 5% reordering. It observed no turret offers, so turret coverage is NOT_RUN for that run.

The 180-second campaign with the same candidate and fault profile observed 10 native emissions and seven refusals (four owner retirements, three recovery expiries). Every one of the eight peers received all 17 matching terminal payloads and applied all seven refusals. Four emitted source keys were paid; no refused key was committed or paid. All cancellation counts were zero, so cancellation of an active predicted flight remains NOT_RUN. Drain passed, but the campaign is **FAIL**: peer 7 recorded four non-finite position samples. Its 472 component payments and 80 canonical respawns do not override that failure.

Two independent baseline defects were reproduced while validating this candidate. Exact biped edge alignment normalized a zero radial vector, corrupting X/Z while speed and Y remained finite; an actual `HandleCollision` fixture reproduced this and now checks ordinary contacts, contact-plane separation, movement fallbacks and the bottom cap. Replay seeking suppressed alt-form death particles at a resolved lethal frame, producing the first presentation mismatch at frame 937 of the shorter owner recording. Seeking now simulates the death effect while suppressing audio. Working-tree validation passed 51 collision assertions and the previously failing recording's 3,766 per-frame comparisons, all playback rates, cold/cached seeks and clip/restore checks. These are regression repairs, not evidence of native ROM-equivalent physics.

CI on the first candidate had 26 passing jobs and a Linux failure from stale current-protocol assertions. Those assertions are updated to 52; historical version fixtures retain their original contracts, with their physical layouts adjusted for the additional current checkpoint tail when deriving old fixtures.

Further working-tree regression checks passed: the longer owner and observer recordings (11,075 and 11,065 frames) each passed determinism and replica/clip checks; the lobby suite passed 7,746 assertions; 414 Python tests passed with one existing skip. An asset-backed resolved-death fixture checks seek/linear effects, repeated transitions and unchanged authoritative decoder state. A separate synthetic asset-backed cancellation fixture delivers a real refusal packet and applies the production queue to active same-key beams, verifies all six identity-field controls survive, and checks duplicate delivery. It does not claim native child generation or a nonzero live cancellation observation.

Still required: final immutable candidate build and fixture manifest; fresh 2/4/8-client UDP campaigns including impaired Weavel traffic; active-flight cancellation evidence; longer recorded replay linear/seek/restore/clip checks; full CI. The earlier G1 failures remain failures. Protocol 52 requires matching server/client deployment; rollback reverts this whole prerequisite slice, including its protocol and replay producers.
