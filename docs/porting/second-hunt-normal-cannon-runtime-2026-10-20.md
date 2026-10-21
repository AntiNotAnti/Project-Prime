# Normal Echoes first-person cannon runtime

This follow-up to the offline body slice adds the normal Dark Samus cannon to
actual local Samus first-person Power Beam presentation. Select **Dark Samus**
in Hunters and equip the loadout; it uses the same opt-in offline launch
identity. Defaults remain native. Overcharged retains its native first-person
gun because its import has no independent cannon capability. Ball, other
weapons, gun switch/raise/missile states and unsupported body/status/shadow
states retain complete native presentation.

This is a fixed source cannon cut with native aiming/fire/charge effects. It
does not complete imported cannon animation, all weapons, ball transitions,
Overcharged's cannon, imported Trooper actors, multiplayer or the full plan.
The source uses the existing floating TEV profile and named lighting policy;
this is not a claim of original hardware arithmetic or original room lighting.

## Exact source and resource ownership

The normal cannon's existing generated recipe preserves392 original triangles
(material2=66, material5=326), full39 source joints, original corner attributes,
fixed ANIM ordinal32/resource `d12fdfa3` at time zero, and the independently
qualified source tip/barrel frame. The importer, files, recipes, hashes and
component versions are unchanged. The original authoring receipt still
describes its authoring scope; runtime authority comes from the new explicitly
admitted actor and exact current Scene, rather than modifying that receipt to
self-assert gameplay acceptance.

The body and cut come from the same normal CMDL/CSKR/materialset0 and generated
GLB. No new archive or Final borrowing is required. Product-only
`LoadForLocalBody` captures both exact assets against one immutable normal
material archive before issuing either live lease. This capture requires the
pinned generated normal body/recipe, identical model path/hash/appearance,
same pack and source identity. The diagnostic preview's existing `Load` path
is preserved. The compiler admits only that verified cut and preserves
original material ordinals2/5 when selecting source programs; ordinary bodies
still require their complete material inventory.

The existing per-player body owner also owns a separate native gun anchor,
source ViewModel instance, GPU geometry and fixed palette. Native clips and
simulation continue to run. Source drawing requires Power Beam, a supported
native gun state, ready source body, current native models, immutable pack
generation and resident context. It fails to the whole native gun before any
source segment. Initialization failure, model replacement, owner destruction,
context retirement and Scene teardown retire both instances. Step/Draw never
perform source IO or reload a retired binding.

## Placement and world passes

The recipe maps source locator32 to zero and barrel direction to+Z.
Presentation uses positive body scale times0.7, native muzzle offset, then the
already prepared native first-person gun transform. The existing palette
preparation applies `ToCannon` exactly once. Its matching raw-normal texgen
transform includes that basis; geometry never receives it a second time.
Diagnostic centering and recoil translation are excluded, keeping the source
tip at the native late-latched muzzle/charge effect point. The native gun's
animation, effect lifetime, projectile spawn, hit logic and muzzle state are
not assigned by this owner.

The immutable GX record carries a ViewModel flag only when the current cannon
owner submits inside the native `BeginViewModelItems` scope. The Scene checks
that scope again at render, selects the native authored ViewModel projection,
and uses the existing source packet collection, opaque/depth/alpha passes and
GL state transaction. No second renderer or render clock is added. Source
packets stay outside native retained batching and color replay.

Sizing/composition follows the pinned Second Hunt `BodyArmCannon` behavior at
`41452f2d053fcfa679e33c00a73d120a53f59e61` (MIT, Copyright2020 NoneGiven;
repository LICENSE retained). Its optional recoil translation/light tuning is
not adopted as established behavior.

## Frozen verification

Production source is `de12eda2b`; fixture source starts at `8a0b397d7` on main
through body/ledger merges940/941 (`66795c487`). Desktop and Windows x64
dedicated Release pass with one existing CS0649 warning and zero errors each.
The initial compile found a duplicate local variable name; it was corrected
before this freeze and the failed log remains local.

The actual owned-source Windows/OpenGL fixture passes27,501 assertions:
native, normal and Final each match675 native gameplay/effect/RNG snapshots
exactly, SHA256
`3afee28e86635d916322bd91f10fb7e9bf59f46a2f1ec90a6b3303a031174a98`.
Normal has394 complete cannon observations including20 firing,110 charging
and40 charge-release steps. Final has no source cannon. Every gameplay draw
is repeated with no intervening simulation and preserves native state,
fixed source palettes and material clock.

The fixture independently resolves full source bone globals/inverse binds,
checks the original triangle/material inventory and source programs, and
compares every submitted palette against raw skin times the fixed basis times
the expected native presentation. Maximum palette error is1.90735e-6; maximum
source-tip/native-effect error is1.91108e-6 world units. Actual GPU projection
uniforms equal the authored ViewModel matrix and differ from the120-degree
world matrix. Active muzzle/charge draw overrides remain on that native tip.
Explicit cloak/damage/shadows/native-weapon/morph cases select native geometry;
context retirement deletes source lists and never resurrects them.

The unchanged actual `MatchStart.Begin/LoadScene/EndScene` body lifecycle probe
also passes54 assertions with the extra cannon resident: normal third-person
body remains seven packets and Final nine. Missing/corrupt/removed imports,
repair, reinstall, native recovery and retired-owner checks still pass.
Synthetic actual GL passes417 pixel checks including50 source GX assertions;
941 CPU source-body/gun policy,24 physical identity,910 rig,971 offline launch
policy,3,050 replay, network architecture, timing, desktop shader newline,
native ABI/runtime and all-seven-Hunter192-publication checks pass.

| Frozen output | SHA256 |
| --- | --- |
| Desktop core | `da446a8b6dac327ac6900c180f4bf4bbbd132e33910a8585accabe4548e824ce` |
| Initial cannon/launcher tool | `d5b6679a2052a14cfa375fae17e1bc1c1f43e09107e2cd27e915e7516b0c6034` |
| Windows x64 dedicated core | `52997750ccc0ee29d1755bb01bf986e664210844aaf9de590a7518173e20c447` |

Local receipts/logs live under ignored `artifacts/sh-integration`. Owned RVZ
and AMHE1 inventories are identical before/after both probes. Temporary imports
are deleted. Two local idle/fire PNGs were inspected and individually deleted;
their retirement receipt contains names/hashes only. No protected payload,
capture, user profile or source edit is committed. No Android/Mac execution,
physical-control/device, achieved refresh/latency, broad GPU/art fidelity, CI
or external-review pass is claimed. The completed-step probe alone does not
qualify fractional late-latch schedules; any later timing acceptance is
recorded separately below.

## Fractional native presentation

The extended fixture separately adds a settled normal Power Beam phase after
the675-step parity trace. It uses the existing `FrameTiming.Advance` owner,
responsive fixed-crosshair/static-weapon settings and `ModSetLateAim` before
each picture. The native/normal/Final gameplay trace remains identical. The
extended run passes77,018 assertions on the same desktop core; its tool hash
is `b2868803e70c7d11d2d84798043b3f64d34a2546e9ca9b9b54e3d2d5eba4f35b`.

| Simulated schedule | Pictures / complete source draws | Completed steps | Fractional-alpha pictures | Changed prepared orientations |
| --- | --- | --- | --- | --- |
|90 Hz|90|60|60|89|
|120 Hz|120|60|60|119|
|240 Hz|240|60|180|239|
|540 Hz|540|59|480|539|

All990 pictures repeat the independent palette/tip/effect/projection checks
and draw invariance. Clocks advance exactly by returned completed steps,
including59/60 at540 Hz because of the schedule's floating-point remainder;
the fixture does not force or assume60 steps. Source fallback count is zero
in this supported phase. The initial fractional fixture selected the legacy
moving-reticle camera, which intentionally bypasses late pointer aim; that
failed run remains local. The corrected phase explicitly selects/restores the
same responsive settings as the existing native viewmodel acceptance owner.
Other camera modes and achieved physical refresh remain unqualified.

## Final combined main qualification

The clean rebase incorporates package-manifest PR943 and Parkour mantle PR942
through `2b63538acc559ad1d82398c0d62417d2871d6364`. Final production freezes at
`0eacfd1356d44ff6f82ec4ee5658471489f0f183`. The shared body/cannon predicate now
explicitly selects native presentation during active mantle or wallrun.
Protocol80 and the Parkour v4 replay profile are inherited main changes; this
slice adds no wire or replay field. The earlier protocol79 body/cannon proofs
remain scoped to their own freezes.

Final combined desktop/dedicated builds and every regression listed above
pass, plus36,965 custom-weapon contracts. The final owned cannon probe passes
**77,124 assertions**, preserving the675-step three-way gameplay trace and all
990 fractional pictures. Added declared mantle/wallrun flag fixtures verify
complete native ViewModel geometry, unchanged native state/raw palette/clock
and complete source resume without simulation after restoring each flag.
They qualify presentation guards, not actual ability execution. The real
launcher lifecycle again passes54 checks and all owned inputs remain unchanged;
temporary derivatives are deleted and no new captures are written.

| Final combined output | SHA256 |
| --- | --- |
| Desktop core | `0599a74b5d56d8fe2aa04194107dcc38224e5bfe3d5db4590f0851dd812adfe7` |
| Cannon/launcher tool | `2ef7fa25e20422f533edb1ee865107908bca4f9b80a09ee65bfeec37c386c76a` |
| Windows x64 dedicated core | `14285649131013d867907b530d738f03ed8805bc700d015db4f1537804b5f312` |

The local `cannon-final-combined-receipt.json` pins both owned probes, logs,
source and binaries; earlier binaries remain separately retained in
`cannon-first-freeze`. Subsequent test-only/documentation commits do not change
qualified production bytes. This is not a qualification of arbitrary later
main changes, CI, external review or other platforms.

## Merge confirmation

[PR944](https://github.com/AntiNotAnti/Project-Prime/pull/944) merged at
`67fc370548c7fec4b59327e37208bedd1f07fcac`. Its tree exactly matches head
`f8ea46dad1e7abc65d74e5df6e9356de0f5bffcc`. Production matches freeze
`0eacfd135` exactly; later commits contain only the explicit guard fixtures
and documentation. This confirms source identity, not a new build of later main.

## Rollback and remaining work

Equip **Native** in Hunters. Other gun states already use native fallback.
No import removal or migration is required. Complete source gun animation,
other weapons, Overcharged's independent cannon, ball and source transitions,
Trooper materials/actors, independent extra-character identities and network/
replay authority remain separate engineering. Original seven Hunter IDs stay
unchanged. Protocol80 is inherited from main's Parkour v4; this feature introduces no
network/replay fields and preserves historical replay readers.
