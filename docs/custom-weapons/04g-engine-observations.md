# CW-04g engine source observations

The explicit developer authority adapter now retains bounded observations of remote
inputs that the engine consumes. This is an engine observation producer, not action
admission, a network certificate, package readiness or authenticated custom transport.
The developer adapter still uses its explicitly supplied source admission oracle.

Existing void placement/control APIs delegate to implementations returning positive
success. Their native side effects and rejection paths remain unchanged. The custom
observer requires the complete current stream/occupant/life, fresh accepted input,
successful actual placement and controls, finite geometry and nonzero accepted aim.
It reads that input's aim explicitly: the cached gun vector is updated later in the
native biped simulation and cannot prove this phase's input aim.

Only the explicit developer custom authority room starts the input barrier. Ordinary
rooms retain immediate native accepted-shot draining. In that developer room all
remote input placement/controls run before native accepted-shot drains. After merged
input and AfterInput lifecycle handling, the adapter freezes every qualified actor's
observation, then drains native events in slot order. Finally cleanup releases the
barrier even if controls fail, without publishing partial observations. Scene disposal
discards staged references. This changes ordering only in the explicit developer room;
it does not claim ordinary native simulation phase equivalence for that room.

The observation phase is **post-control, pre-accepted-projectile**. ApplyIntent's
owner-reported self-death reconciliation may already have happened. The observer
neither defers nor bypasses that canonical decision and does not call this a phase
before all damage. Entity movement and native firing continue afterward.

A read-only player helper captures accepted body/aim and explicit eligibility:
active/spawned/alive/nonspectator, stable biped, no freeze/input-disable, scan visor,
attached enemy, gun transition, disruption, blocking camera, pause or ended match.
Native ammo, cooldown, charge, selected native weapon and owner modifier hints do not
establish custom eligibility. The source journal continues to own custom cadence and
resources. This initial observation contract does not reproduce native disruption
spread, so disruption explicitly denies custom use.

Each retained owner life has 128 source cells sharing the occupant's fixed source
clock with its resource journal. Every key contains complete owner identity, exact
source frame, original reported body/aim, ACK/subframe and input bits. The first
observed world, accepted body/aim and permission are immutable. Reconsumption cannot
refresh age or recapture engine state. A contradictory valid raw repeat tombstones
the source and retires the input journal. Old-life history follows the same bounded
retention/occupant retirement as its payment store.

Rejected placement/control for a valid fresh current-life raw input records a
negative permission observation with **no accepted pose**. This cancels reload/scope
through the existing journal interruption path. It cannot authorize firing or be
promoted to a positive pose after thawing. Reconsuming an earlier positive frame
cannot revise that earlier permission based on a later freeze. Invalid raw geometry,
invalid lifecycle, stale arrival or accelerated source clocks cannot advance positive
source proof. Native controls remain governed by their existing rules.

Lookup is exact, read-only, at most 64 authority ticks old, with inclusive retention
and serial uint timing. Missing/evicted/conflicted/negative frames are unavailable;
there is no nearest-frame or current-pose fallback. No action timestamp can seed this
history. The latest-intent receive slot can omit older source frames, including when
several intents arrive in one simulation step. This slice does not recover those
missing poses or claim loss/reorder fairness.

Remaining admission gates include readiness/bootstrap ownership, historical modifier
provenance, ACK/world and muzzle/aim joins, local/bot source-phase parity and recovery
policy for missing inputs. They must be established before these observations can
replace the developer fixture oracle or enable custom networking. No protocol or
native packet/replay layout changes in this slice.
