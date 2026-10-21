# CW-12f6 — recorder-owned passive state and accepted clocks

This source contract covers `ReplayCustomWeaponRecording.cs`, the snapshot
path in `ReplayTimeline/ReplayRecorder.cs`, and the Clock extensions in
`ReplayCustomWeaponFacts.cs` and `ReplayCustomWeaponState.cs`. Local qualification is
recorded in [CW-12f6 evidence](evidence/CW-12f6.md). It builds on
[explicit replica inspection](12f5-custom-replica-checkpoint.md), without
enabling custom Demo recording or playback.

The recorder owns a detached `ReplayCustomWeaponState` for its exact current
custom scope. Bootstrap and baseline are validated before either is published.
Each subsequent custom fact is accepted by that owner before being published
to observers. Successful publication advances the recorder sequence/count.
Semantic, owner or observer failure closes custom capture; an observer may
already have seen a prefix, which must not be represented as a complete stream.
Capture failure does not withhold gameplay ACKs or apply gameplay outcomes.
Retirement clears the context, accumulator and publication dedupe state.

`Clock` is kind 7 with fact version 2, the existing tag 250 and 49-byte header,
and an empty body. Existing kinds remain version 1; version/kind combinations
are strict. Clock carries the exact context digest, contiguous fact sequence
and recorded authority world tick, with the separate recording frame supplied
by the recorder record. It supplies no slot, pickup, launch or contact outcome.

The accepted snapshot path asks the active healthy scope to publish a Clock
only when a baseline exists and the snapshot tick is strictly newer than the
accumulator's world tick under wrap-aware comparison. Repeated or older ticks
produce no new clock fact. State acceptance also requires a strictly newer
Clock world after baseline, while normal exact retained retries remain no-ops.
Malformed ordering/gaps continue to invalidate the detached owner.

Clock acceptance advances passive time from original flight anchors and expires
flights through the existing state rules. It does not interpolate a new anchor,
simulate collision, create a launch, pay damage, grant pickups, advance weapon
mechanics or replace a state table. Arbitrary outer recording-frame movement
still does not advance custom world time: the accepted Clock fact is required.

`TryCaptureCustomWeaponState(out descriptor, out checkpoint)` returns false
unless the recorder scope is current and healthy, every authored pickup is
known, and accumulator sequence equals the published custom sequence. Success
returns a fresh CWRD2 inspection descriptor and current canonical CWRS bytes.
It never relabels the original baseline as current, lends its mutable owner,
acquires resources or proves package archive provenance. Callers independently
validate/bind historical resources when that separate contract is needed.

The GL tool requires genuine accepted Clock records and compares this current
snapshot against independently accumulated actual accepted facts. Existing
complete-point restore loops also include Clock suffixes. More snapshot clocks
increase complete-point and fact counts: prefix reconstruction is quadratic
test work, while only a bounded number of large owners are live at once.
Observed counts and local timing are recorded in the linked receipt; they do not establish a production performance budget.

Native archive/world versions and default custom replay refusal remain intact.
This is recorder ownership and accepted passive clock continuity, not full
Scene reconstruction, durable Demo record-close-reopen/seek, resource ownership,
actual contact capture or bot support. No platform or release qualification is
claimed here.
