# CW-12f2 — accepted package weapon replay facts

The existing `ReplayRecorder.Accepted` stream now receives versioned custom
facts after an ordinary client has consumed authenticated public replica and
pickup updates. Native record kinds keep their numeric values; `CustomWeapon`
is appended at8. No second combat recorder, health authority, inventory grant
or custom damage path is introduced.

Each file-only semantic value has a49-byte header: tag250, schema1, kind byte,
body lengthu16, per-context sequenceu64, original authority worldu32 and SHA-256
of its exact CWRB bootstrap. Multibyte scalars are little-endian. Recording
frame remains the separate outer `ReplayTimelineRecord.RecordingFrame`; delayed
accepted facts retain their actual authority/source clocks. Total value size
is bounded to16KiB. The parser rejects unknown kinds, versions, lengths,
contexts, zero sequences, pre-bootstrap worlds and invalid typed values.

| Kind | Value | Body |
| --- | --- | --- |
| Bootstrap | 1 | Exact CWRB bytes; sequence1 only |
| Baseline | 2 | Eight41-byte public slot rows, flight-count byte and up to64 bounded219-byte flights |
| State | 3 | Eight41-byte public slot rows |
| Launch | 4 | Accepted171-byte source certificate/origin/direction projection |
| Contact | 5 | Accepted108-byte semantic outcome; no delivery token |
| Pickup | 6 | Index/count, spawn ID, incarnation, ready time, availability and revision;25 bytes |

The typed context checks the exact mechanics definition and frozen pickup
layout against CWRB before using shared bounded row/flight/source validators.
Slot rows preserve ownership absence, generation/life, selected runtime,
settled/committed source frontier, ammunition, optic, reload and accepted-shot
state. Flight baselines preserve original source certificates, action identity,
launch/payment clocks and physical position/velocity/age. Contact outcomes
retain canonical observed health and full source/victim life identity but never
apply health. The live116-byte contact payload keeps its existing nonzero
delivery-token requirement and unchanged bytes; only its108-byte semantic
prefix is eligible for a file fact.

Recipient delivery IDs, ACK envelopes, retry queues and private acquisition
knowledge are absent. Accepted source certificates still contain the source
action's acknowledged target frame/subframe, which is semantic source-time
evidence rather than transport ACK state. The codec is pinned by independent
literal vectors; future shared primitive layout changes require deliberate
file-version compatibility work.

Capture occurs only after the existing endpoint/connection/readiness and
consumer checks. Initial baseline installation creates bootstrap and baseline
facts. Exact baseline retries do not reinstall or republish them. State capture
requires a changed accepted revision; launch/contact capture requires a changed
delivered-event count. Public pickup capture compares accepted item values,
including initially received items that precede the combat baseline. Repeated
ACK-producing deliveries cannot manufacture additional semantic facts.

Recorder match/epoch and both custom availability flags fence publication.
Malformed input is validated before bootstrap/baseline publication. Recorder
reset clears the custom scope/counters; readiness/connection retirement clears
the scope. A recorder losing its scope after consumer baseline installation
fails capture explicitly instead of advertising that original baseline as a
current flight checkpoint. Observer failure closes capture without interrupting
gameplay ACKs. An observer may have seen a prefix before throwing; that failed
stream is not a complete recording and cannot be resumed as one.

This slice does not make custom facts durable in a Demo archive. Native replay
decoders explicitly refuse tag250 until a supported exact-package archive
contract exists. Current native writers remain at their existing formats,
checkpoint shapes and protocol83. Custom recording, killcam, clip and export
admission stay closed. Test-only length-prefixed fact dumps are diagnostic
artifacts, not Demo files or record-close-reopen/seek evidence.

Subsequent CW-12 work must build a detached accumulator for current resources,
pickup incarnations, active flights and accepted outcomes; serialize complete
custom bootstrap/layout/checkpoints; bind independently retained historical
resources to passive scenes; and qualify genuine recording, reopening and
forward/backward seeks before enabling custom playback. Dedicated-authority
capture, two-client actual contact capture, custom bots and final release gates
also remain open. Android testing remains deferred by user instruction.
