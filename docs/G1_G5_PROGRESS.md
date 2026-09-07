# Prime Hunters G1–G5 implementation progress

The [supplied implementation plan](PRIME_HUNTERS_G1_G5_IMPLEMENTATION_PLAN.md)
is the scope. Classic balance, the 60 Hz authority, existing binary/package
identities and unrelated working-tree changes are preserved.

| Epic | Implementation | Validation |
|---|---|---|
| G1 | Timing foundation/world-camera migration, local render interpolation/desktop late latch, protocol 8 afflictions, spawn policies and ordering/lifecycle characterization implemented; final timing/input/spawn checks in progress | Frozen baseline: 434 main + 18 imaging + 47 Python pass. Stale harness failures repaired. Focused render 13, affliction/rules 11, timing 41, spawn CLI 10 and input 9 pass. Physical render/device acceptance pending. Long mixed-combat baseline exposed reliable queue exhaustion; reproducibility investigation in progress. |
| G2 | Authoritative assists/kill events, bounded client feedback/recap/markers and Enhanced radar integration in progress | Focused and integrated checks running; no visual/device acceptance claimed |
| G3 | Count-based team allocator and pre-start rebalance implemented; remaining match quality work pending | Exhaustive allocator and lifecycle tests awaiting integration run |
| G4 | Historical rating evidence research only; no backend implementation | Exact original delta table and explicit eight-player policy remain prerequisites |
| G5 | Not started | Pending |

## Acceptance boundaries

Builds and headless/network tests are not rendered play, Android device proof,
external WAN proof or a deployed backend. Mandatory device acceptance and
historical-rating evidence will remain explicit until verified.

The pre-existing LICENSE deletion and maps/Parallax edits are outside this task.
