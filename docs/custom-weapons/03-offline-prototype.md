# Offline Riftline M7 developer fixture

The bundled `prime.riftline-m7` definition uses the schema and canonical mechanics
from CW-02. Its stable content GUID is `9a776851-f39f-4a76-8c13-c5d88875ef61`;
its mechanics digest is `3ee854525a9737a3acdaf6f994dd8da75e229b5f87d820c96d98f17c59512228`.

An explicit `Scene.EnableDeveloperRifle(true)` creates the scene-owned offline
adapter. The developer fixture grants it to a living player with `Grant`, then
uses `Fire`, `Reload`, and `Optic`; the existing native inventory remains intact.
The runnable entry point is:

```
dotnet build tools/custom-weapons-check -c Release -p:UseSharedCompilation=false
PROJECT_PRIME_USER_DATA=<isolated-user-data> dotnet tools/custom-weapons-check/bin/Release/net10.0/custom-weapons-check.dll --scene <isolated-content-data>
```

The content-data folder needs a valid private `paths.txt` pointing to legitimately
available game assets. Tests do not copy or redistribute those assets. The fixture
uses the native Proving Ground collision, real player spawn/death, projectile
processing and `TakeDamage`. Two reflection-based dispatch probes isolate dynamic
enemy and turret routing; those probes are distinct from the ordinary ray tests.

Admission requires offline Battle, no replica/demo/replay lab/trainer, and no
Insta-Gib, One in the Chamber, No Imperialist or Balanced Mode. Campaign enemies
already in the scene reject activation; an enemy encountered later blocks the
projectile without accessing native effectiveness tables. Becoming ineligible
retires private projectiles and invalidates resources before the native network
lane can consume them. There is no normal-match default grant or native slot.

Each owner has eight private projectile slots. Capacity and finite geometry are
checked before magazine payment and again before native emitter side effects.
The adapter has `BeamType.None`, an immutable typed custom source, no native ammo
cost and no native muzzle/collision-effect lookup. It never allocates a native
shot ID or submits a native shot certificate. Every accepted emission has one
custom identity, per-life key and projectile component. Hip spread is deterministic
from that shot; scoped spread is zero. Existing collision and authoritative head
height are reused without the native short-range headshot limit. Launch-time
Double Damage/Prime modifiers use the existing shared damage conversion.

Local impact facts report actual body plus turret health removed, with the turret
portion and hit channel separate. Zero-damage protected hits create no damage fact.
The shared damage path retains team protection, spawn protection, health, death,
score and respawn behavior. Custom kills never increment a native weapon counter.
Weapon-specific native semantic events and killfeed are deliberately suppressed
for this developer prototype, since `None` otherwise means alternate-form damage.
The canonical accepted custom semantic/network adapter belongs to CW-04. No
custom online transport, replay recording/reconstruction, prediction, production
input/HUD, or original weapon art is enabled by this slice.

Reload, bolt, magazine and reserve are authority state from CW-02. Morph/swap
cancels scope/reload without clearing the bolt; death invalidates the old state
and retires its beams. A genuine respawn binds a new life and initial resources.
Scene cleanup releases both the adapter and registry. Native-only scenes allocate
neither. To roll back, omit the explicit developer opt-in.
