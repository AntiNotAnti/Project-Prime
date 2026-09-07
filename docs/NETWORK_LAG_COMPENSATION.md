# Server hit history

The authoritative server keeps 32 ticks of player hit-test state in fixed storage
for eight slots. The query includes the target connection ID and life ID. A hit
from a disconnected player or an earlier life cannot resolve against the next
occupant of that slot. Match changes clear the history. Missing history falls
back to the normal current-world collision test; it is never synthesized from
an unrelated tick or another life.

`LagCompensationPolicy.ResolveTick` treats the input's estimated server tick as a
hint for the server time when the input was sampled. The server subtracts the
fixed six-tick (100 ms) remote interpolation delay itself; the client does not
subtract it or supply a configurable delay. The allowed age is the smaller of
15 ticks (250 ms) and half the measured server RTT rounded up to ticks plus six
interpolation ticks and two scheduling ticks. With no measured RTT, only the
known six-tick interpolation allowance is available. Future and ambiguous
half-range timestamps select the current tick. At 100 ms RTT, a sample three
ticks old normally queries targets nine ticks old. The 250 ms ceiling and
two-tick scheduling allowance are initial policy values, not a claim of measured
optimal fairness.

The history stores values used by `BeamProjectileEntity.CheckCollision`:

| Target | Engine collision test preserved |
| --- | --- |
| Biped | Vertical cylinder from `Position + MinPickupHeight` to `MaxPickupHeight`, with the current volume's radius plus beam radius |
| Alternate form | Sphere at the engine volume's center, with its radius plus beam radius |
| Kanden alternate form | Broad sphere around segment 2 with radius 1.6, followed by the original ordered body/segment 1/2/3 sphere tests |
| Weavel halfturret | Separate sphere at the turret position with radius 0.45 plus beam radius |

Alive/spectating state, form, position, facing, and biped height limits travel
with the collider. Historical headshots use the historical form and head-height
threshold. The collision routines are the existing engine routines, including
their original segment ordering and distance calculation. Live entities are not
temporarily moved. Current walls, doors, and force fields retain their normal
nearest-collision priority.

The initial weapon policy enables history only for non-continuous Imperialist
beams. The source weapon tables specify a speed of 819200 fixed-point units and
a range of 819200; the spawn code converts these to 100 world units per 60 Hz
step and 200 world units of range. This is an effectively instant swept beam,
with at most two movement steps to cover its range. A shot's validated timing
belongs to that shot and does not follow later input commands.

Traveling and homing projectiles spawn when the server processes the command.
Their fast-forward budget is zero. Charged Judicator's ice wave uses a different
angular area query and is not included in the Imperialist policy. Continuous
weapons also retain current-world collision. Supporting another weapon requires
tracing its actual collision path and validating that policy with gameplay.

Focused tests cover timing spoofing and wraparound, connection/life isolation,
history expiry, historical movement and form differences, dead/spectating
targets, head-height classification, wall precedence, Kanden segments, Weavel's
turret, and comparison with the engine's existing cylinder/sphere queries.
These tests do not establish Internet hit-registration fairness; rendered
two-client gameplay and asymmetric WAN checks remain separate evidence.

Run the focused tests with:

```sh
dotnet test src/MphRead.Tests/MphRead.Tests.csproj -c Release \
  -p:MphReadServer=true --filter FullyQualifiedName~LagCompensationTests
```
