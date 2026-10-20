# CW-05a — canonical client request production

Player controls can submit a custom action kind and optic through
`NetSession.TrySubmitCustomWeaponControl`. The client derives the entire request
identity from its current private authority projection. Callers do not supply a
slot, life, bootstrap, manifest, weapon ID, sequence, source frame, ray or ACK.
The wrapper requires the committed public replica baseline, current native
lifecycle and endpoint-owned custom readiness before queuing intent.

`CustomClientCombat` owns the next sequence. It uses the highest submitted value,
which may be ahead of `State.SettledSequence` while responses or state projections
are delayed. It publishes the result only after the existing bounded16-entry
queue accepts it. Invalid intent, a full queue, wrong publisher mode, unavailable
readiness and sequence exhaustion return false with a default output. A rejected
submission cannot consume a sequence; successful queuing cannot spend ammo or
change canonical state. A terminal no-source response frees its pending entry
without reusing that action's sequence.

The existing reliable control91 and atomic native input carrier93 transport the
exact retained request. Neither transport ACKs nor retries produce a new action.
Canonical new-life state resets the publisher; an old projection or late old-life
result cannot restore its prior intent stream. The explicit lower-level submit
APIs remain for protocol/transport fixtures, with the same validation and mode
fences as before.

This is the request-producing part of CW-05. Public selection, keyboard/pad/touch,
pending selection/native-fire suppression, HUD, optics and presentation prediction
are still pending. In particular, adding a local fire guard alone would leave the
authority's accepted-native-fire path unguarded during custom selection; that
source-time ownership work must precede enabling public controls.
