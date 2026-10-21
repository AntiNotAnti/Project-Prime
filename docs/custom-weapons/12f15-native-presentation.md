# CW-12f15 — native presentation in explicit custom archive inspection

Historical v2 base: `b406d1a129cf7c09345ac5c7f02ac5efc4889fad`.
Current integrated source is `f514e4f04b618e5393502dcdf5ca956979ee74e9`,
with protocol85 retained after main integration. Integrated local qualification
and historical84 evidence are recorded in the [evidence ledger](evidence/CW-12f15.md).

The explicit custom file factory owns the existing pose/fire cursor and optional
owner-presentation worker alongside its Scene and session reader. Its private
lookahead decoder restores the admitted custom checkpoint, then uses the same
native pose rewind while retaining the exact custom admission descriptor.
Lookahead consumes native match, roster, snapshots and intents. It does not feed
custom combat, semantic markers or world packets into that presentation decoder.
Historical protocol83/84 presentation fields use explicit historical
normalization where required; protocol85 is current. Retained historical schemas
and native archive conversion/admission remain unchanged.

The saved world's `IndexedFirePresentation` selects the cursor's firing policy.
Opening an unindexed producer never changes it to indexed firing. Newly captured
custom metadata states the actual world policy using the existing optional
format7 continuation field. Explicit metadata/world disagreement is refused;
durable worlds must also agree with their original archive baseline, including
historical metadata without that optional field. No packet ID, replica/world
layout or creator mechanics changes.

The existing independent readers retain absolute recording clocks, pose and
intent histories and per-advance limits (8,192 pose records and 65,536 shot
lookahead records). Explicit custom lookahead refuses reader integrity failures
and malformed native shot facts. Its retained native shot facts have a lifetime
budget of 8,192 distinct identities. Duplicates consume no additional capacity;
the next distinct identity stops inspection. Identities are never evicted to
make a delayed duplicate eligible for delivery-time impact fallback. This is an
explicit inspection size limit, not qualification of unlimited match durations.
Preparation and each explicit custom step check
cursor failure before Scene simulation and retire the failed session as corrupt.
Native historical best-effort behavior is unchanged.

Validation must preserve an actual unindexed source, then separately construct
and capture an indexed source before stepping its genuine native/custom records.
Changing a saved marker byte cannot qualify indexed production. Both sources
require strict cold restore, repeated draws and actual suffix continuation.
Native fire and impact evidence must come from the ordinary clients and actual
authority. First-person/audio and all-Hunter/form qualification are reported only
when observed. Inspection remains silent without an explicit replay audio owner.

Ordinary custom recording, theatre, killcam and export admission remain closed.
Installation remains separate from gameplay acquisition. Android is deferred.
