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

Still required: immutable candidate build and fixture manifest; fresh 2/4/8-client UDP campaigns including impaired Weavel traffic; emitted/refused/cancellation denominators; recorded replay linear/seek/restore/clip checks; full CI. The earlier G1 failures remain failures. Protocol 52 requires matching server/client deployment; rollback reverts this whole prerequisite slice, including its protocol and replay producers.
