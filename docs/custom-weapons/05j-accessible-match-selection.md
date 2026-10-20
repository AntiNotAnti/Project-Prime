# CW-05j — accessible match selection

The match menu offers Equip Riftline M7 and Return to Native when the local
human has custom availability. These actions complement the existing physical
bindings; the native wheel, native inventory and native weapon choice retain
their meaning. Availability remains off in ordinary matches.

The simulation publishes an immutable selection snapshot. It includes the exact
Scene lifetime, complete player owner/life, world bootstrap, frozen manifest and
weapon revision, plus predicted selection and pending native state. UI reads
only this copied value; it cannot call mutable inventory projections, advance a
reload, create a client combat runtime, issue a sequence or grant ammunition.

The Scene owns one coalescing desired-selection mailbox. Requests must name the
exact currently published snapshot. The next eligible fixed input sample consumes
the request through the existing input controller and its sole control-request
sink. Native physical intent wins a simultaneous request. Repeated Return to
Native while an Unequip is pending cannot manufacture another nonce, and native
firing remains fenced until the canonical result settles.

Only asynchronous menu resume may hold a request across a blocked sample, for
at most two seconds of monotonic wall time. Other restrictions consume it.
Death, morph, owner/manifest/scene change, loading freeze, replay/replica/server
role, loss of available inventory and unload clear pending work. Recovery does
not retry a denied or stale request. A refused menu action stays visible as a
refusal and does not author gameplay state.

The public offline runtime computes its exact definition manifest once on
activation. Native-only scenes allocate no mailbox, manifest or custom resources.
See [evidence](evidence/CW-05j.md) for completed checks and remaining device and
fallback layout gates. This does not complete the full CW-16 implementation.
