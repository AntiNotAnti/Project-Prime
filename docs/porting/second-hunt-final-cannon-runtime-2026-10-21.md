# Overcharged Dark Samus fixed source cannon

The selected Overcharged Echoes local offline Samus appearance now uses its
own fixed source cannon with native Power Beam. It shares the existing body
owner, source-rig loader, material compiler, Scene graph and native first-person
gun/effect transforms. Other weapons/forms/status and retired contexts retain
complete native fallback. No gameplay, Hunter value, saved appearance, native
intent, network or replay change is introduced. This remains a limited offline
cosmetic slice; the full character and Second Hunt plan are unfinished.

## Independent source policy

Final's `Battle_3` body is CMDL `faa12c05`, CSKR `918e83d3`, with its own
12734 source triangles and nine materials. The complete39-joint rig uses
root0/external parent97, elbow24 `R_elbow`, and locator32
`GUN_Particle_LCTR`. Fixed ordinal32 `A_MissileBarrage_dark_samus` uses original
ANIM `d12fdfa3` at time0, SHA256
`4bdb85ff6b36f10c5b330158fa7968f2af9203f293c00b8fb7796d32306ed197`.
The ANIM and rigid basis happen to match normal; they do not grant normal's
cannon receipt or material authority to Final.

The complete elbow cut has805 whole triangles. The pinned donor
`41452f2d053fcfa679e33c00a73d120a53f59e61` (`Campaign/BodyArmCannon.cs`, MIT,
repository LICENSE retained) removes12 material5 triangles touching raw source
UV `(0.489,0.764)`, with strict axis tolerances `<0.002`, at any corner. The
existing generated GLB retains those raw UVs. A V flip before this selector
would remove zero triangles and is explicitly tested as the wrong domain.

The resulting793 triangles retain original weights, winding, global IDs and
material groups3=392,4=66,5=314,7=21. Whole nine-group selection SHA256 is
`7a91aae9dbe748d23143b8c2ea655435df1db1403ec59fb84a43242577cdd594`;
the removed-ID list SHA256 is
`30715234934fe9b6f379ec456f1d4d5fba7d022ef2b0fcf7d57a65474a110e11`.

Final's new receipt SHA256 is
`46c828de8c0c7f6ec6102216400891c30475036bc2aa7da0f7b6b752abf0752c`.
The closed runtime profile checks its own source character, full six-input
generation, source GLB/provenance/animation bank, strict clip, complete rig,
reproduced basis and exact selected IDs. Normal receipts, geometry, materials,
assets and leases cannot substitute for Final, even with recomputed metadata.

Final bundle7/body6 adds its cannon receipt and a ViewModel entry. Ball3 stays
unchanged. Normal bundle6 and all Trooper bundle6 recipes remain unchanged.
The frozen prior Final bundle6/body5 has23 committed files; the new generation
has24. All21 old files excluding `characters.json` and the versioned parts
index remain identical, including body/ball geometry, rigs, source animation
and raw material bytes. Existing transactions retain complete old generations
and preserve discovery on cancellation before publication.

## Shell rendering and lifecycle

Body and cannon leases share the exact original Final material archive object
and decoded textures. The compilation boundary derives a private cannon
program variant: **only Alpha passes multiply output alpha by0.55**. RGB, TEV
inputs, texture programs, additive and opaque passes remain unchanged. The
raw body programs stay unchanged; the variant shares archive/image identities,
cannot be derived from normal/Trooper materials, and cannot be applied twice.
Its shader source participates in the existing GPU program cache identity.
There is no new render path, global blend state override or draw-time IO.

The cannon uses the existing source body fit times0.7 and the prepared native
gun/muzzle transform exactly once. Native viewmodel FOV, late aim, charge/muzzle
effects, projectiles, weapon selection, ammo and simulation remain authoritative.
The whole39-joint pose is retained with physical batch palettes at most32.
Clock gating, per-player/Scene/context generation and retirement remain owned
by the existing source body implementation. This is a fixed pose; gun skeletal
animation and other source weapon presentation remain engineering.

The renderer retains its established named preview lighting. This slice does
not claim the donor's original actor/room lighting or hardware TEV fidelity.

## Windows evidence

Production freeze `7ebc4e2be` includes main through PR952, with no newer main
change observed at qualification. Desktop core SHA256:
`c49c53ddd0d5c10fd1a558b52a797b33410ad35ad8db02b3450f1fae2323e641`.
Initial external tool:
`fe5aaa2045f678869e2150b875bbc62eb783a75871aebea9597d7842aeeac447`.
Dedicated core:
`c9c4fed7aacb1a03c0146d813bb2453547dca33d81b930235ec722a8778d9d3e`.
Both Release builds pass with one pre-existing readiness warning/zero errors.

- Independent owned CPU proof:13047 checks/52 refusals, actual original source
  805-to793 cut and all12 removed IDs, original ANIM/basis, own nine-program
  archive/TXTR bytes, Alpha-only scale CPU/desktop/ES shader contracts, raw
  program immutability, alias/mutation/lease refusals, file locks and retirement.
  Pure suite retains normal50/Trooper18 checks and adds21 Final trim checks.
- Actual Windows OpenGL4.6 NVIDIA RTX5080 normal/Final/native match:
  244846 checks, each675-step gameplay trace identical with SHA256
  `3afee28e86635d916322bd91f10fb7e9bf59f46a2f1ec90a6b3303a031174a98`.
  Each source appearance records397 source/288 fallback pictures in the main
  script/guards; fractional-rate pictures are separate. Locator error is at
  most0.00000191108; palette error at most0.00000190735. Native fire, charge,
  release, turn, walk, missile/power, morph/unmorph, effects, projection,
  exclusive imported-file locks, pause/repeated draw and retirement pass.
  Simulated90/120/240/540 rate schedules qualify fractional presentation/late
  aim, not achieved physical refresh. Mantle/wallrun/grapple guards use declared
  injected presentation states, not actual controller execution.
- Trooper cannon actual regression:272490 checks on the same core/tool,
  all four675-step traces identical to native Weavel.
- Actual product normal/Final launch lifecycle:58 checks, independent own
  cannon admission, missing/corrupt/repair/Remove/reinstall, retained active
  generation, Begin/End and native recovery.
- Actual normal/Final source viewer:1787 checks/94 frames across two recreated
  contexts on the same c49 core, with test-only tool
  `0df221fa2e436712fb03f0a70131dd5542781f5a25b3ea7e902751f5b5a7b66f`.
  Final's four cannon material groups/793 triangles and private shell programs
  render repeatedly, A/B/A switch stably, and restore the unchanged nine-program
  body. Normal's diagnostic cannon remains unchanged. GPU uniforms match the
  independent CPU matrices bit-for-bit for every reported active row.
  Initial raw attempts are retained: an inherited Mac-only test preflight
  rejected before source access/GL, then a legacy six-row Final uniform-count
  assertion failed on NVIDIA's nine-row active array despite all nine values
  matching CPU exactly. Test-only fixes use the supported desktop witness and
  actual GL array metadata with compiled texgen coverage; no production change
  or numeric tolerance relaxation was needed. No viewer captures taken.
- Source-body2708 desktop/2690 dedicated, root identity24, rig910,
  replay3107, offline character policy971 and architecture checks pass.
- Generated profile CPU:44 checks/34 refusals on the same c49 core/0df tool.
  The complete pure suite passes with final test-only tool
  `18374691e7b4a16227b6b4dd59852e976a2ae6880f5e76452bb64835ceca43b6`.
  Owned bundle migration records3042 passing labels, including actual frozen
  v6 BeforeCommit cancellation with the old discovery pointer/all23 old files
  preserved,21 unchanged components after upgrade, unchanged normal output,
  older v5-to-v7 rollback, reuse, cancellation, removal and cleanup. All123
  frozen baseline dependency hashes remain unchanged. An initial fixture still
  expected the pre-PR950 authoring-only inspection message; its readiness
  checks passed, and the corrected offline/native-fallback text contract passes.
  Its failed raw log is retained; no production change was needed.

Four actual normal/Final idle/fire captures were inspected locally then
individually deleted with hash receipts. They show the distinct Overcharged
shell and native aligned muzzle effect in the native arena/HUD; they are not
an original-game art/lighting comparison. Logs and metadata remain ignored
under `artifacts/sh-integration/final-cannon-*`. Owned Echoes RVZ SHA256
`ba8996256d005749dd6b7ff29ea30e5998451a3741ec87494a6217b01b703529`
and the native AMHE1 inventory stayed unchanged; temporary imports were deleted.
No protected payload or screenshots are committed/uploaded. No CI, external
review, other-platform, Android, online/replay or full character acceptance is
claimed. Further Android testing remains deferred by the user.
