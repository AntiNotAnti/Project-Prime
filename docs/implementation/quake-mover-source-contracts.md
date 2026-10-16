# Quake moving-brush import contracts

PR-09 source-stage implementation only. Q3 brush-model movers can now be parsed and checked with bounded, deterministic 60Hz reference trajectories for func_door, func_bobbing, func_rotating and func_pendulum. The importer identifies how many valid mover descriptors are present while reporting that dynamic runtime movement is **not** yet supported.

No mover metadata is serialized into .ppmap or added to the authoritative world. No hidden format/protocol change, incomplete collision, or ghost passenger simulation is enabled. Before runtime release, a single native entity must own mesh transforms, swept collision, passenger/alt-form contact, lifecycle, replay/late join and network state. The draft Radiant editor PR #561 remains outside this change.

CI: nettest --q3-mover-source verifies bounds, type/axis, source world scale, representative trajectories, malformed input and deterministic results. Real Quake map geometry, collision and replay acceptance are still required.

This is intentionally **not** full PR-09 runtime completion. Enabling source parsing alone would not make a Quake moving platform playable, and no production setting should pretend otherwise.
