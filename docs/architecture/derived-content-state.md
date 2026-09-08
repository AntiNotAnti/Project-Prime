# Derived content state (A4)

The worker contract fixes one content generation and language while any scene
lease is active. `Scene.Language` serializes changes on the content lock and
rejects changed values during a lease; assigning its existing backing value is
idempotent. The Korean content override still returns Japanese.

`Weapons.Current` has exactly one observed assignment in the prior source,
`SceneSetup.LoadGame` selecting `WeaponsMP`. It is now a get-only multiplayer
selector, initialized even before scene setup. Weapon definitions themselves
remain the authored metadata table; this change does not claim that arbitrary
reflection or casts cannot mutate every metadata object.

`PlayerEntity.PlayerVolumes` is calculated once from fixed
`Metadata.PlayerValues`. Its indexer returns a collision-volume struct copy and
exposes no mutable array. Repeated `GeneratePlayerVolumes` calls do not write it.
Kanden segment distances are calculated into a local array, then published as a
read-only list once per content generation under `ContentEnvironment.SyncRoot`.
This is a synchronized immutable cache addition: a second scene loading the same
hunter neither clears nor rewrites the first scene's distances.

Weapon/alternate-attack/hunter names are similarly published as read-only lists
under the content lock, keyed by generation and language. Setup invokes this
only for client scenes. Their actual gameplay-file readers format death HUD
messages inside the local-player branch; headless scenes have no local player.
They do not participate in authoritative damage or scoring.

`RuntimeData` contains frozen ROM offset descriptors, not a separate binary
cache. Those public descriptors are now immutable. `Load` writes process-global
font and audio metadata, so it is serialized and performed at most once per
content generation, with cold initialization rejected while a worker lease is
active. Its sole scene-setup caller is non-headless. Worker headless setup does
not require these client font/audio mutations.

`DerivedContentStateTests` verifies the fixed selector, read-only collision copy
boundary and stable warmup, and immutable ROM descriptors. Worker content tests
own admission/lifetime and language-freeze checks. These are focused source and
in-process checks, not deployed-client or performance measurements.
