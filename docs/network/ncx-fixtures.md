# NCX emulated open-box fixtures

These explicit harness fixtures are **not the historical PR #109 glass wells** or a completed comparison. Prime defaults and production combat/movement policy remain unchanged. The [hashed contract](../../tools/nettest/fixtures/ncx-emulated-contract.json) records the exact definitions under [ncx-emulated](../../tools/nettest/fixtures/ncx-emulated/). The controller verifies every definition hash before launch.

Both engines use the same ordinary six solid visible box brushes, with no central pedestal, items, invisible geometry or beam-transparent walls. Spawns are X=-6/+6, Y=0.1, Z=0, yaw90/270. Hunters are Samus and the weapon is stock uncharged Power Beam. Assets and map definitions are copied into private run outputs before generated-map writes.

| Scenario / room | Prime explicit HitRig | Pinned native existing HitRig | Scheduled motion |
| --- | --- | --- | --- |
| `ncx-static` / `NCX EMULATED STATIC` | `ncx-static` | `wells:powerbeam` | No movement buttons; native knockback remains |
| `ncx-lateral` / `NCX EMULATED LATERAL` | `ncx-lateral` | `lanes:powerbeam` | Left/right alternate every40 local frames |
| `ncx-jump` / `NCX EMULATED JUMP` | `ncx-jump` | `wells:powerbeam` | Native vertical pads; no Jump input |

The native script comes from pinned source `12ff677b9700bc8dabde37eb9a3fdd10c62b87b6`, `HitRig.cpp:513–580`. Prime emulates those harness controls in `HitRig.NcxFixtures.cs`; no comparator combat implementation is imported. Both actual assigned slots0/1 fire, so source/victim are per-shot views. The previous generic Prime `powerbeam` Volley, with one shooter and a moving victim, remains a different workload.

Participants alternate aim height0.95/0.30 every300 frames, use native convergence with maximum6-degree turns and a2.5-degree firing cone, and tap for3 frames every `ShotCooldown*2+3`. Existing native ammo-cap refill is declared workload setup. On death, native Shoot input requests respawn while aiming down; incidental respawn shots remain measured. If X differs from the cell by more than2, the script places its own actor at `(cellX,0.6,0)`; record those placements. This is explicit test-actor positioning, not movement enforcement. Static means no scheduled movement, not frozen coordinates.

The jump variant adds pads at both spawns: vectors `(0,1,0)`, speeds0.094/0.058, sizes `(2.2,2.2,2.2)`, cooldown20 and control-lock40. Actual launch velocities and apexes must be measured, never inferred from those settings.

Optional slot2 is a connected no-fire Samus body, not an invisible spectator. Prime dispatches `ncx-observe`; native dispatches `observe`. It is placed near `(0,0.6,9)` when outside the declared bounds, retaining its actual native camera. Never move that camera to manufacture visibility. The two participants target each other, excluding the observer. Observer death behavior is not qualified; Prime never presses observer fire, including while dead.

Use the existing controller only:

```text
--scenario ncx-static --rooms "NCX EMULATED STATIC" --players 3
--fixture-maps tools/nettest/fixtures/ncx-emulated
```

Substitute the declared lateral/jump scenario and room together. Prime pixel runs add `--rendered --shot-timeline --shot-visibility --presentation-hz 60`. The requested rate is not an achieved rate. Intrusive readbacks disqualify CPU timing comparison.

Native controls add `--engine fruity-native --protocol 34`, the qualified `.app`/smoke manifest and exact commit-plus-profile-adapter source label. Native has no qualified presentation-rate, Prime timeline, exact-pixel exclusion or frontier-drain adapter; unsupported options are refused. Text/UDP control results do not become oracle, drain, pixel or cross-engine PASS claims.

Identical recipes and emulated scripts declare a workload; they do not prove identical collision geometry or trajectories. Measure assigned roles, accepted inputs, native motion, pad launches, placements, shot keys and contacts. Independent contact evidence, actual-view pixels, both-engine controls and A/A variance remain required.
