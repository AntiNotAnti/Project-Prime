# CW-04l reliable primary custom controls

A custom click is an explicit reliable control request. It is not a claim about a
hardware press or the pose at click time. The server binds it to a subsequent
actually consumed native input. This recovers taps whose native button levels
were entirely lost or coalesced before the engine ran, without reconstructing
missing movement, releases, poses or ACK history.

Protocol 64 adds ordinary reliable/background packets 91 (Control, 76 bytes) and
92 (ControlDenied, 89 bytes). Existing action/result packets remain unchanged.
The request contains only the exact sender bootstrap, manifest, slot/life/runtime,
universal sequence, kind and optic. It cannot choose a source frame, historical
target, ray, damage, ammo, force or proof witness. Both developer flags remain off
by default; no public input/UI is activated here.

Ingress verifies the admitted endpoint and actual connection, native and custom
readiness, current bound active life and full room gate. A request cannot create
a life grant or wait for an invalid initial life to become favorable. Each life
uses either strict source-referenced actions or primary reliable controls; mixing
them is refused. A real new life may select another mode without discarding old
outcome/flight obligations or resetting the occupant clock.

The fixed 16-entry outbox reserves receipt and potential Fire contact capacity
before waiting for source input. First admission pins the accepted-input serial,
world and monotonic wall time. Exact duplicates cannot move these fences, replace content,
refresh the deadline or spend again. Foreign/conflicting/full requests do not
consume a journal sequence. The existing per-peer 64 offers/second ingress budget
also covers controls and application ACKs.

Only a genuinely new engine-consumed source whose serial exceeds that original
fence may bind the contiguous request head. One head binds per new observation;
the next head activates after the prior action settles and requires another new
observation. Previously consumed rows are never searched or reused. Out-of-order
request arrival therefore cannot reverse source time. A Fire never waits for its
cooldown, ammo, frozen state or pose to improve.

On a usable observation, the server materializes the canonical Action using that
row's source frame, actual placed body plus the existing muzzle offset, normalized
accepted aim and exact captured ACK/subframe. Only this trusted callback creates
the full-action control witness. Source admission bypasses native Shoot and the
preceding-release check only for that exact witness. Raw source-referenced actions
retain both checks. Eligibility, bootstrap, source clock, pose/muzzle geometry,
ACK age, native timed modifiers, source-time resources, rewind, capacity, mandatory
trade and canonical contact/payment remain required. Processing still waits until
the observed world has sampled geometry; the request cannot publish an early
world or run native movement again.

The first fresh denied/unavailable source produces an honest typed no-source
denial. It contains the original request, result token, authority world and reason;
it has no Action, dummy ray, certificate, resource delta or contact. A trusted
ordered journal tombstone settles only the contiguous sequence and changes no
resource, source clock or committed source frame. Later controls can proceed.
An unbound request also expires beyond either 64 world ticks or 64/60 wall
seconds, with both exact boundaries inclusive. The wall clock is sampled directly
at binding, so a server stall cannot bypass the world-time limit. The same typed
denial prevents firing an old click after a long input outage.
Room readiness lost before binding is terminal on the first fresh observation.

Normal life retirement publishes old unbound requests as LifeRetired; it does not
manufacture a current-life action or block the next life. An exact duplicate can
recover its retained canonical receipt or no-source tombstone. If its sequence
has settled beyond retained evidence, StaleSequence denies it without replaying
effects. All results use exact full-owner/token/sent application ACKs. Native
transport ACK is not a gameplay verdict. The existing 15-second application result
deadline explicitly closes an unconsumed endpoint; retries do not extend it.

The client retains/retries the original 76-byte request. Its canonical returned
Action must match the full request identity, sequence, kind and optic; source/ray
fields are authority-selected. No-source results retire only the exact pending
request and acknowledge its token. Token contradictions permanently invalidate the
custom lane. A canonical new life resets the local publisher mode and old pending
intent; late old-life results can still be acknowledged without replacing current
presentation. Neither response path applies health or refills authority resources.

This slice addresses lost/coalesced control intent. Full observer replication,
late join/reconnect/spectator baseline, local/bot source parity, final host fairness
and CW-12 custom replay remain separate acceptance gates. No rendered/device or
public user-flow acceptance is implied by headless contracts or UDP fixtures.
