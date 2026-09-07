# Multiplayer weapon timing policies

The server classifies the mechanics that `BeamProjectileEntity.Spawn` actually
resolves after charge and affinity selection. The weapon name alone does not
determine whether a shot can use history or projectile catch-up.

The source is the 18 player entries in
[`Weapons.WeaponsMP`](../src/MphRead/Metadata/Weapons.cs), the charge and projectile
resolution in [`Spawn`](../src/MphRead/Entities/BeamProjectileEntity.cs), and
[`LagCompensationPolicy.GetMode`](../src/MphRead/Mods/Network/Server/LagCompensationPolicy.cs).
Enemy, platform and boss tables do not define player multiplayer policy.

## Classification

The table covers all nine weapons, both metadata variants, and three input charge
levels. A partial attempt uses `MinCharge + FullCharge` simulation ticks; a full
attempt uses `FullCharge * 2`. Weapons without `CanCharge` remain uncharged even
when given a full-charge input level.

- **Projectile:** ordinary travel, with up to 15 catch-up steps through the
  existing movement and collision path. Gravity, speed changes, splash damage,
  bouncing and child projectiles retain their existing mechanics.
- **Trace:** Imperialist's fast swept projectile uses historical target colliders.
- **Homing:** traveling projectile with historical player target acquisition
  and steering during the same bounded catch-up interval.
- **Continuous:** current-state continuous targeting; no projectile catch-up.
- **Area:** immediate angular area collision; no traveling projectile is queued.

| Weapon | Variant | Uncharged | Partial attempt | Full attempt |
|---|---|---|---|---|
| Power Beam | Normal | Projectile | Projectile | Projectile |
| Volt Driver | Normal | Projectile | Projectile | Projectile, splash |
| Missile | Normal | Projectile, splash | Projectile, splash | Projectile, splash |
| Battlehammer | Normal | Projectile, gravity, splash | Same | Same |
| Imperialist | Normal | Trace | Trace | Trace |
| Judicator | Normal | Projectile, ricochet child | Same | Projectile, three pellets, ricochet child |
| Magmaul | Normal | Projectile, gravity, bounce, splash | Same | Projectile, gravity, splash |
| Shock Coil | Normal | Continuous, homing | Same | Same |
| Omega Cannon | Normal | Projectile, splash | Same | Same |
| Power Beam | Affinity | Projectile | Homing | Homing |
| Volt Driver | Affinity | Projectile | Projectile | Homing, splash |
| Missile | Affinity | Projectile, splash | Projectile, splash | Homing, splash |
| Battlehammer | Affinity | Projectile, gravity, splash | Same | Same |
| Imperialist | Affinity | Trace | Trace | Trace |
| Judicator | Affinity | Projectile, ricochet child | Same | Area |
| Magmaul | Affinity | Projectile, gravity, bounce, splash | Same | Projectile, gravity, splash |
| Shock Coil | Affinity | Continuous, homing | Same | Same |
| Omega Cannon | Affinity | Projectile, splash | Same | Same |

“Affinity” identifies the second set of nine metadata entries. It does not imply
that every entry has a distinct hunter affinity or supports charging.

## Charge and collision boundaries

Power Beam is the only player weapon with partial-charge interpolation. At charge
level 36, the charged flag is set but the interpolation fraction is zero, so
numeric fields still use their uncharged values: speed 1.5 units per simulation
tick and homing zero. At level 37, speed becomes 0.75. The affinity variant also
acquires positive homing, `81 / 24 / 8192`, at level 37; it must not be classified
as a non-homing projectile using an arbitrary small-value threshold. At full
charge, level 60, its homing value is `81 / 8192`.

Other chargeable player weapons switch at full charge. Affinity Volt Driver
changes to homing at level 120, with `40 / 8192`; affinity Missile changes at
level 90, with `81 / 8192`. Their lower charge attempts retain the uncharged
mechanics. Missile starts at 0.125 units per tick and accelerates toward 0.75
using its existing interpolation function.

Normal Judicator changes from one projectile to three at level 120. Each can
spawn ricochet child entry 0; this is separate from a same-entity bounce flag.
The child has one projectile, no spread or homing, speed 1 and a half-second
lifespan. Affinity Judicator instead applies its immediate angular area at level
120, with range 3.75 and angle parameter 60 degrees. The existing area geometry
is preserved; it is not a traveling ice projectile. Magmaul uses same-entity
bouncing while uncharged and removes that bounce flag at full charge.

Imperialist is physically swept, rather than literal hitscan. Its metadata
resolves speed `819200 / 4096 / 2 = 100` units per simulation tick, maximum range
`819200 / 4096 = 200`, and lifespan `2 / 30` seconds. It can take two steps to
cover that range. Treating it as an effective instant trace is an explicit
network timing policy; the ordinary swept collision implementation remains.

Shock Coil always combines continuous and homing behavior. The continuous path
takes precedence over projectile classification. Omega Cannon affinity contains
a nonzero charged-homing field, but has no `CanCharge` flag: even input level 600
resolves homing zero. Its ordinary splash radius is 3, compared with the normal
variant's radius 25. Both use the existing projectile and splash collision paths.

## Regression check

[`WeaponPolicyCheck`](../tools/nettest/WeaponPolicyCheck.cs) invokes the actual
`Spawn` implementation with retail game data in a headless scene. Its expected
values are independently specified from the source table, not generated from
`GetMode`. It checks 54 weapon/variant/charge combinations and seven additional
charge boundaries, including resolved speed and homing, pellet counts, bounce
and child flags, immediate-area disposal, and the policy stored on the shot.

From the repository root:

```sh
dotnet build tools/nettest/nettest.csproj -c Release -p:MphReadServer=true
dotnet tools/nettest/bin/Release/net9.0/nettest.dll --weapon-policy /path/to/AMHE1
```

The check accepts an optional version argument after the data directory. It opens
no network sockets and creates no renderer. Success prints
`WEAPONPOLICY PASS cases=61 variants=18 source=actual-Spawn`. This checks weapon
classification and spawn integration; the separate catch-up and A/B checks
exercise historical collision and impaired-network behavior.

## Historical homing

Power Beam affinity from charge level 37, fully charged Volt Driver affinity,
and fully charged Missile affinity use `HomingProjectileCatchUp`. Each was checked
through its actual resolved metadata and projectile physics. Shock Coil remains
continuous and charged affinity Judicator remains an immediate area attack.

Acquisition ranks the target direction and range at the validated action tick.
Each catch-up step steers toward that tick's immutable player target point:
`Position + 0.5 Y` upright, or `Position` in alternate form. Weavel turrets use
their recorded turret position. The selected connection and life remain bound
to the shot's target. Missing, dead, spectating, mismatched or absent-turret
history cannot fall back to a current player. Losing that target does not trigger
reacquisition. Once catch-up reaches the present, steering reads current geometry.

Doors, platforms and other world objects retain current geometry, matching
the existing map-collision policy. The original acquisition rule has no separate
line-of-sight test; catch-up retains normal projectile collision with current
walls. No live player is moved or rewound to perform these queries.
The enabled variants are root actions. Retail multiplayer ricochet children have
no homing; any future homing child remains excluded until its acquisition timing
has separate coverage.

`nettest --homing /path/to/AMHE1` checks bit-exact position, velocity, age and
lifespan against a timely control for five charge variants and a moving target.
It also checks historical-only and current-only acquisition, invalid history,
connection/life replacement, form/turret target points, and zero-rewind ON/OFF
parity. Its deterministic fixture controls target motion directly and opens no
sockets; real UDP soak and strict impaired-input A/B are separate checks.
