# Source first-person weapons

The Mac production pack `sourceio-hd-roster-v3` contains dedicated Source-derived
RigidNodes viewmodels for Noxus, Kanden, Sylux, Trace, Weavel and Spire, plus the
unchanged finished Samus cannon. The 16-entry manifest preserves all ten prior
model entries, including Samus LOD1/alternate form and existing texture tiers.

## Conversion contract

`sourceio-hd/common/viewmodel_pipeline.py` uses hunter-specific
`viewmodel-config.json` with the existing Source-to-native `bone-map.json`.
The fresh native authoring kit exports `viewmodel-native-idle.json`: actual
engine Power Beam idle frames and the gameplay emitter. Compact Blender
animation index 3 corresponds to native animation ID 24.

The converter selects Source forearm or weapon/hand surfaces and bakes one
fit. It preserves radial cross-section aspect, Source UV artwork and split
normals. Native mechanical region cuts retain selected Source surface area;
there is no automatic remesh or decimation. Each section uses native nodes
and the unchanged generated RigidNodes exporter. Independent audit reconstructs
native-frame geometry, normals, UVs, retained effect colors and embedded texels.

Trace uses its selected forearm mesh's principal axis and a 270-degree wrist
roll so its broad blade remains visible. Handheld weapons select Source hands
and the appropriate armed bodygroup. Native interior surfaces use the Source
finish where needed. Native ammunition/core geometry is retained selectively;
overlapping exterior glow shells are omitted. Charge, muzzle and affinity
particles remain engine driven with their original gameplay emitter offset.

The rigid loader supports float VEC3 `COLOR_0` for retained native effect RGB.
Absent colors remain white, preserving older rigid assets. Existing authored
albedo/normal bindings, native material names, team variants, double-sided
culling and advanced-material toggles remain in effect.

## Acceptance and installation

The generalized `-viewmodelacceptancecheck ROOM -hunter HUNTER` runs real native
animation in a Metal bot scene with synthetic input. It checks 3,090 eligible
frames per hunter, including 990 simulated presentation draws at
90/120/240/540 Hz, movement, firing, charge, missiles, affinity weapons,
Imperialist zoom, FOV extremes, material toggles and six suit recolors.
Controlled raised-idle native/Source captures are independently compared with
the authoring frames. `-poseonly` is diagnostic and does not constitute acceptance.

Each final non-Samus weapon passed an individual one-entry pack before merge.
The signed full pack passed all seven hunters (21,630 eligible frames and
6,930 late-presentation draws). The validator accepts all 16 entries. Source
screenshots, team-color samples and mechanical states were reviewed separately.
Spire's clean configuration-driven rebuild is byte-identical. Installation
preserved original model files and preference bytes, and saved whole app/pack
rollback snapshots. Restart an older running game process after installation.

Local evidence: `artifacts/sourceio-hd/viewmodels-v1/VIEWMODELS-RESULT.md`,
`FINAL-CHECKS.json`, `full-roster-checks.json`, `INSTALL-RESULT.json` and
`ALL-WEAPONS-COMPARISON.jpg`. Each hunter freezes authoring, configuration,
converter and acceptance evidence in `releases/first-person-v1/` with per-file
hashes. Previous biped releases remain intact.

These six weapons preserve the existing Source artwork. Final material repaint,
new mobile texture tiers and Android device performance checks remain separate.
Sampled images do not certify every surface against self-intersection; the
simulated schedules do not measure physical display latency.
