# Trooper fixed cannons in local offline matches

Merged in [PR952](https://github.com/AntiNotAnti/Project-Prime/pull/952) at
`65d30788c1af5064041dd3f709b8803e77151641`. The merge tree exactly matches
qualified head `cbf6d179c34d85d29b350d1295664488c2e40cda`.

This slice adds the Power, Ice, Wave and Plasma Trooper's own fixed source
cannon to the selected local offline Weavel body when using native Power Beam.
Hunters keeps the existing four body choices. Native gameplay, weapon logic,
projectiles, charge and muzzle effects remain authoritative. Other weapons,
forms and unsupported presentation states use the complete native gun.
Imported characters and the full Second Hunt plan remain incomplete.

## Import and source ownership

Prime USA Rev2 imports advance from bundle5/body4 to **bundle6/body5** with
13 required components per appearance. A new cannon receipt and a ViewModel
entry join the existing generated body manifest. Each appearance commits its
own generation through the existing transaction; Update-all does not promise
one transaction across four appearances. Old generations remain retained.
All44 non-manifest components across the preceding four v5 imports remain
byte-for-byte identical, including geometry, source animation and materials.
The original Prime recipe and both Echoes recipes remain unchanged.

The source cut follows the pinned donor's elbow rule: all three corners of
each complete triangle must have at least one positive elbow-or-descendant
weight. Original weights, material ordinals and global triangle IDs survive.
Every appearance has206 triangles (material0=10,1=32,2=164), the complete
35-joint/36-node rig, and its own pinned CMDL/CSKR-derived geometry. Equal
counts do not imply equal triangle IDs; independent source probes pin each
appearance's original arrays.

The fixed pose is `A_gunloop_pirate`, ordinal1, ANIM `5528972b`, time0,
SHA256 `5b6540fbfdcbf37edd24133b4475468e0f0199a744e3159df2f0b942c498ccc7`.
Source elbow23 `R_elbow` and locator34 `R_gun_LCTR` determine the rigid
source-to-cannon basis. Root3/external parent2 and all source channels remain
intact. This is a fixed presentation pose, not animated gun/claw support.

| Appearance | Cannon receipt SHA256 |
| --- | --- |
| Power | `2b128462891c9aa85ebae92b68306345386c7dcaeabdee043ff522de69a4ecc5` |
| Ice | `2074a896f47f0bd72402076afa1347287958bfef5682a225a287e4a83afe9363` |
| Wave | `1c1cb397a741ebab61d1a36d785c194b4bad7f764c9d6684d86ec9f50e42c79b` |
| Plasma | `1f8a1dc3eaf7d5a43cdc3d7e2fde28cdff63b95040f066fad57e1eb2d86584c1` |

The closed runtime profiles check the whole recipe and its six source inputs,
then independently reproduce the cut and fixed frame through the existing
rig/animation loader. The cannon captures the same immutable body material
object before leases are published. Each appearance retains its own original
three source programs, textures and C1 state; Power's C1 cannot leak to another
appearance. The viewer can now stage this own cannon with source programs.
Viewer selection and runtime cannon admission remain separate capabilities.

## Match presentation

The existing per-player source body owner admits and retires the cannon at
the existing Scene/context boundaries. Each physical draw batch uses a local
palette of at most32 source joints; the full rig is never truncated. The fixed
raw skin combines once with the source-to-cannon basis, existing source body
scale times0.7, and the prepared native first-person gun transform/muzzle
offset. Native viewmodel projection and late aim remain in use. No new
renderer, simulation clock, decoder, draw-time source read or reload is added.

Native movement, damage/status, mantle, wallrun, grapple, shadow, form and
weapon guards act before partial source submission. The selected body still
requires Native gameplay variant; this slice does not combine imported
cosmetics with wall-cling, Scout Drone or Pirate Blaster practice abilities.
No network/replay format, Hunter value, saved appearance value or native UI
intent changes. Current hardware acceptance is Windows x64 on this machine;
Android testing remains deferred at the user's request.

## Local evidence

Production change `93ec6d916` was first qualified with desktop core
`b52e34081570b3b5d020632509d8f622c3dbd0e94c44bc1604b2b9703b893d8e`
and external tool
`de147c8700fc49b97f182c8fa54e7a5cfa6d84b425faa1ae1b62fea8d6bf1445`.
Integration commit `59fe39421df50dd4cad9b37def6dece938dcecec` then incorporates
main PR951 (`7998a089d`), including its custom-package authority changes.
Combined desktop core:
`9998e6e375f7b8de49aebeeb4a64bfe1fa4c5ab25e009e90a8bc1afafa121f4a`.
The evidence below distinguishes these binaries; no external review or CI
result is claimed.

Combined-main verification passes on that exact core:

- Actual four-cannon match:272,478 checks without optional captures, with the
  same675-step native trace and locator/palette bounds as the initial run.
  Each appearance records397 source and288 fallback pictures in the main
  script/guards; fractional rate pictures are recorded separately.
- Actual normal cannon/Final native-gun regression:77,180 checks, all three
  native gameplay traces identical. Both runs use tool
  `53527f108918c604e47a93bc380d0c35f22d9babceda6546fd62c718778b143d`.
- Actual product launch/import lifecycle:77 checks including missing/corrupt,
  Update/Remove/reinstall, all four admitted cannons, Begin/End and native
  recovery. It uses the test-only viewer extension's tool
  `c4509cccf117cc0dfe597fe0ad755aef535d7e27eb949ae2e60e1a2f28e907e9`.
  The independent137,449-check/160-refusal CPU proof also passes on this tool.
- Actual product viewer on the same c450 tool:1653 checks/24 refusals/104
  frames, all four Trooper body/cannon switches plus both Echoes bodies in
  each of two recreated contexts. Cannons retain35 joints/206 triangles,
  their own three materials and fixed ordinal1/time0. Repeated framebuffer,
  palette and resources are stable; ready-body restoration is exact.
  Twelve deduplicated GPU programs/43 textures cover all six appearances;
  shader sharing gives each three-material cannon two actual program IDs.
  Full retirement and injected link-failure rollback pass. No captures taken.
- Frozen-old-core migration repeats all184,571 checks on the combined core
  and53527 tool. Source-body2708, root identity24, rig910, replay3107,
  offline character policy971 and architecture regressions pass.
- Desktop and dedicated Release builds pass. Dedicated core
  `af08cfd2f08452663b94461d7c2241a767aa36e0e80898f03eeb483100b85c78`
  passes2690 source-body CPU checks; it makes no GL claim. Both builds retain
  one pre-existing readiness probe warning and have zero errors.

- Independent initial owned CPU proof:137,449 checks/160 refusals across all
  four original source cuts, rigs, fixed ANIM, original TXTR/program/C1 bytes,
  cross-profile and rehashed recipe/material/lease mutations, file locks and
  retirement. Pure suite retains the existing50 normal cannon checks and adds
  18 full35-joint/reordered/global-joint-over31/palette/cut/frame checks.
- Initial actual Windows OpenGL4.6 NVIDIA RTX5080 match run:272,487 checks.
  Native Weavel and all four appearances produce the exact same675-step trace,
  SHA256 `8b0886273d4fb47f50e48d07db21e6da4490df38e48bdca8638ebb563f86dc20`.
  It exercises native idle/fire/charge/release/turn/walk/missile/power/morph,
  full source/native fallback, effects and native viewmodel projection, two
  independent contexts, pause/repeated draw, exclusive imported-file locks,
  and context retirement without resurrection. Maximum locator and composed
  palette error are each0.00000190735. Rate schedules90/120/240/540 earn
  fractional pictures and late aim; these are simulated schedules, not claims
  about achieved display refresh. Mantle/wallrun/grapple are declared injected
  presentation guards, not execution of those ability controllers.
- Initial migration:199 pure recipe checks and184,571 owned source checks,
  including actual v3/v4/v5 BeforeCommit cancellation, retained old pointers,
  immutable old generations, missing/corrupt recipe refusal and all44 unchanged
  old v5 components. Pose regression:27,303 checks/68 refusals/21,804 independent
  pose comparisons,392 constructors,60 exclusively locked files and zero
  captured-file reads through sampling. This preserves the previously
  documented floating-point endpoint oracle limits.

Eight initial in-match idle/fire PNGs were locally inspected and individually
deleted after their hashes were recorded. They showed the four colored cannons
in the native arena/HUD with coherent native muzzle effects. Visual inspection
does not establish original-game art/lighting parity. Ignored metadata/logs
remain under `artifacts/sh-integration/trooper-cannon-*`; owned RVZ and AMHE1
inventory hashes remained unchanged, and temporary derived imports were deleted.
No protected payloads or screenshots are committed or uploaded.

Remaining engineering includes animated source guns/claws, other weapons,
Overcharged's own cannon, source balls/transitions, imported ability/drone
geometry and presentation, mixed seats and deliberate online/replay authority.
