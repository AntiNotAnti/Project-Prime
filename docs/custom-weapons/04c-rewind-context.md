# CW-04c explicit rewind context

Custom admission must not manufacture a native `FireEvent` or temporarily replace
a player's native equipment. The internal `NetUnlagged.TryBeginCustomShot` adapter
accepts a journal launch, exact source certificate and private equipment directly.
It remains dormant until the rest of CW-04 supplies real admission and transport.
Calling it does not prove source ownership, kill-trade eligibility or indexed payment.

The adapter checks launch identity, stream/occupant, finite source certificate,
bounded observation/target age and the one-round state transition. The private
equipment must carry the exact custom source and use distinct scene-owned projectile
objects within the definition's capacity. Aliasing any player's native pool or the
scene's enemy/platform pools is refused. The trusted emitter owner must additionally
retain exclusive ownership of its custom pool across custom loadout slots/players.
An old admitted owner life is not rejected merely because the shooter has respawned;
the mandatory canonical trade/lifecycle adapter must judge that eligibility first.

Requested launch world and effective collision world are separate. The existing
`LagCompensationPolicy` applies its configured ceiling to the accepted target time;
the source-resource frame never substitutes for that target. Fractional time is
retained as a double at exact 1/256-frame resolution. Launch attribution retains the
requested whole frame, including when collision time clamps. Scope timing also
retains the requested subframe for future typed outcome attribution.

Historical custom scopes preflight both fractional endpoint samples and every
catch-up frame before any emission. Missing player or dynamic-geometry history
refuses the scope, allowing the emitter to report a definite pre-emission no-spawn.
There is no silent current-world fallback for an unavailable historical scope.
The final catch-up step uses the current world, whose snapshot has not yet been
published. Native target generation/life and active/form collision rules remain in
the shared reconciler. If the configured rewind switch is off, collision remains
current-world while the accepted launch stamp is retained.

The scope captures the supplied pool once. Shared catch-up processes only new
projectiles in that retained pool, even if current equipment changes during emission.
Native `BeginShot(acceptedEquip)` likewise retains its supplied equipment's pool;
ordinary native calls still use the player's native equipment. No weapon table,
native packet or replay layout changes.

The emitter owns `EndShot`/`AbortShot` in `finally`. Nested scopes are refused without
overwriting the active one. Abort, reset and snapshot recording restore historical
collision and release retained context. This is a scene-owner-thread contract;
network callbacks must not enter it directly.

CW-04 still requires accepted input/pose observation and recovery, canonical trade
admission, distinct payment identity through every shared join, typed authoritative
outcomes, bootstrap/readiness, lifecycle/snapshot delivery and actual transport tests.
The offline developer rifle's live guard remains in force. Scene scope tests use
explicitly supplied certificates; they do not claim live custom combat acceptance.
