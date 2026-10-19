# CW-04d nominal custom projectile provenance

The offline prototype originally carried its custom sequence in a native-shaped
`ShotKey`. That shape is unsafe for multiplayer: native proof, payment, rescue,
acknowledgement and presentation joins can pair equal owner/sequence values without
an independent custom domain. `BeamType.None` does not supply that domain.

Custom projectiles now carry `CustomProjectileIdentity`: exact persistent weapon
identity, complete owner match/epoch/slot/generation/life and nonzero custom sequence.
There is no conversion to native `ShotKey`. The projectile's native shot ID, key and
launch match/epoch/generation/life fields stay cleared. Its generic launch-world
stamp remains timing, not identity. Local impact facts retain the nominal custom
identity directly.

`CustomShotSource.ForOffline` explicitly creates offline provenance without an action
or certificate. `FromLaunch` retains paired immutable custom action identity and
source certificate alongside the exact definition and one-round resource transition.
It validates consistency, not authority: it cannot establish historical pose,
bootstrap readiness, trade eligibility, emission ownership or payment. The rewind
adapter requires its private equipment to match that complete launch provenance.

The source is an immutable reference object shared by equipment and its projectile.
Native carriers store only a nullable reference. Full action/certificate data must
not be embedded inline in every native equipment/projectile allocation. Source
construction occurs only for an accepted custom emission; matching existing launch
metadata does not construct another object. This is separate from the journal's
allocation-free resource processing and is measured separately in the check tool.

Native-only producers explicitly exclude custom sources: damage sequence/ledger
attribution, claim prediction, replay shot facts, confirmed native impacts, native
impact witnesses, native payment reservations, native weapon report statistics,
authority-hit completion and native weapon-specific enhanced effects. Generic health,
team/protection, death, score and ordinary actor damage accounting remain shared in
the offline developer fixture. Native head/death/killfeed weapon semantics remain
suppressed until typed custom producers exist.

Until the custom authority/payment adapter is implemented, custom live damage is
refused before native lifecycle/rescue/prediction/replay fallbacks, independently of
optional native proof policy. The native payment helper also refuses live custom
sources. Custom replay cannot bypass this by claiming the native replay scope.
The existing offline developer admission and live Spawn guard remain intact.

The remaining CW-04 payment adapter must reserve using full `CustomActionIdentity`,
victim generation/life, component and impact channel; capture body/turret health
before native splitting and record actual removal afterward; and emit typed outcomes paired with canonical
victim damage-event identity. It must preserve canonical launch-world trade timing
without filing custom shots in native claim/rescue ledgers. It must never reconstruct
a custom outcome as `NoBeam`/alternate-form feedback or enqueue it as a native ACK.
The v1 nonpiercing component settles one terminal contact, including a protected
contact; a changed target/channel must not revive it. Admitted flight and current
shooter-life lifetimes remain separate.

No live protocol, native packet or replay layout changes occur in this slice.
CW-04 is still in progress; this boundary does not enable custom multiplayer.
