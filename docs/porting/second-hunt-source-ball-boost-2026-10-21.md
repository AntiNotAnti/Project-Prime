# Dark Samus source ball boost tint

Merged in [PR965](https://github.com/AntiNotAnti/Project-Prime/pull/965) at
`11c94e1342129784d0d20cbb08801cdcbd02693a`; its complete tree equals qualified
head `61fc1ead0d6c36a4a18da960b06cb4a18d054770`.

Normal and Overcharged source balls tint their own native Samus boost effect136
blue at the native sampled brightness. This is renewed only after the complete
source ball submits in the current picture. Native morph/furl30, unsupported
states and disabled cosmetics keep native effects. Defaults remain native;
this does not grant an ability, alter physics or complete either character.

## Ownership and source

Production freeze `7430abd27be1ee932f7ee693588a6ddd48fdeb3d` changes three
existing owners: `PlayerDraw.cs`, `Renderer.cs` and `Formats/Effects.cs`.
The player targets only its own `_boostEffect` reference with EffectId136.
Scene stores a transient per-picture tint map and clears it with the existing
presentation override boundary, `ClearEffects`, effect unlink and pooled entry
reuse. No effect/particle replay fields are added. Invalid/nonfinite/out-of-unit
tints are refused before changing the map; zero is a valid tint.

`EffectParticle.AddRenderItem` reads its already-computed sampled `Color`.
RGB becomes tint times maximum native RGB component; alpha stays native. A
mesh's optional diffuse override changes only `RenderItem.Diffuse`; a billboard
uses its existing packet color argument. Shared material diffuse, alpha,
RenderMode and canonical sampled-cache restoration retain the original path.
Retained/procedural state reuse already includes packet diffuse. Untinted
callers retain the original behavior. No new renderer, emitter, clock or shader
is introduced. The native trail shares the same RGB90/200/255 constant.

This adapts the hue/brightness policy from the MIT donor
`41452f2d053fcfa679e33c00a73d120a53f59e61`,
`src/MphRead/Formats/Effects.cs` (`HostColorFor`) and player draw, alongside
`src/MphRecomp.Core/Campaign/DarkSamusBody.cs` (`Trail`). Existing MIT
attribution/LICENSE is retained. Unlike the donor's retained host color, this
slice deliberately leaves native furl/morph fallback untinted.

## Tests first and binary scope

The old PR963 core
`481e4cd0ec6370e1b3db6195f0d12c60dc9b80b3845e2b8f6614d361f37da12e`
with test tool
`2b1a775612aa1938c0f334a28deb02b25490bc41c3113a632eacdc4b9e44e684`
fails after30,943 checks on a real normal source-ball boost-release packet:
RGB `[1,.6999512,0]`, expected `[.3529412,.78431374,1]`; alpha
`.033332825` matches native. The native baseline completes first. Raw failure
remains under ignored `artifacts/sh-integration/source-ball-boost-before.log`.

The fixture binds the real native Boost control, holds32 completed steps based
on the actor's own charge values, then releases for60 steps. Only the new
`--probe-character-source-ball-boost-runtime` flag expands the script from
1,415 to1,507 steps. Historical flags/scripts remain unchanged. Native baseline
expanded gameplay SHA256:
`a4703ded543681f69043ab391dafc64159f5fba8d695553ea0c5d6d13676fcbf`.
Native canonical effect trace SHA256:
`4208d3cad61cd4e206bd9d785bc53c69cfa8f72238538bcaa4b913519626db50`.
The trace includes native effect lifetime, particle state and RNG, rather than
using recolored packet values as simulation evidence.

Actual owned136 inventory is one unlit native `bubble` mesh particle, native
display list4 / GL list62 in that Scene, with no COLOR or DIF_AMB commands.
These list IDs are diagnostic instance IDs, not runtime admission keys.
Mesh display lists can otherwise overwrite current GL color, so packet
diffuse alone is insufficient evidence for arbitrary effect models. This slice
does not make an arbitrary-mesh tint fidelity claim.

The initial production-only desktop core is
`0a398e5353bffc7d18cbca57fc9780112c6828692129e4c7876a8990e09fdf7e`.
It passes2,708 source-body CPU checks and3,107 replay-format checks;
nettest `7059f433a0e80b07089b41140f0d0d4343d723c26c2ed8ea8295b03ee360424b`
passes694 historical replay compatibility,971 offline policy and architecture
checks. These are earlier checks, not proof of later combined binaries.

Concurrent PR964 adds custom-weapon accepted replay facts in17 unrelated
files; no overlap with the three production files, protocol or effect schema.
It is merged at combined source `07580e37c250d46829064131020133ee66f3077e`.
Combined desktop Release passes with the existing unused readiness fixture
field warning. Desktop core SHA256:
`e0a47b84083540f3465560f6c7c36ca716cdcf110e3f45718e190cba7b3b4b2f`.
Combined nettest SHA256:
`1a3b96d06e41e2853a14be939e15f8befa111ce92aa9bee1db437c4e4a95e23c`.
The combined build also passes2,708 CPU source-body,3,107 replay-format,
694 historical replay compatibility,971 offline policy and architecture checks.

## Combined Windows qualification

SDK10.0.401 / Windows x64 / RTX5080 / OpenGL4.6 NVIDIA617.42.
Final test fixture `8d040c3e2162405c1266d9682a769b706d1c43c4` uses tool SHA256
`05b52ae413ceb46cd17ea8e6a3e0f79ae92f9b1eb7a2766ad87166dd13100c15`
and the combined `e0a47b84…b4b2f` desktop core above. Dedicated Release passes;
dedicated core SHA256:
`d0e72db6247fe5d2944651d649513ec20893e78c01bcd881b4d3b97f035f5059`.
Its2,690 CPU source-body assertions pass.

The actual owned Windows boost Scene/GX probe passes393,753 checks. All three
1,507-step runs have exactly the expanded gameplay and canonical effect hashes
listed above. Native observes62 boost pictures/60 native packets/244 furl
pictures. Each source appearance observes69 boost pictures/65 packets:
61 tinted and4 native fallback, plus499 native furl pictures. Picture totals
include separately counted guards beyond the trace. There are32 genuine
bound-key charge steps; no synthetic emitter is used for the gameplay proof.

Transient-map tests separately check null/nonfinite/out-of-unit rejection,
atomic preservation of prior valid tint, valid black, two distinct entries
with the same136 ID, foreign furl isolation, repeated assignment, picture reset,
unlink and pooled reuse. The exact original pool order/capacity is restored
before gameplay. An initial test compared a default0 entry against foreign136;
the final fixture explicitly sets both to136. The earlier pass is retained but
not used to claim that stronger same-ID condition.

Actual live boost checks cover pause, repeated draws, shadow/cloak/cosmetics
fallback and restoration, source context retirement and native effect lifetime.
No override survives a new picture without fresh admission. Deferred native
effect work can run on the first draw; canonical native/source traces must
match, while no-extra-advance assertions use repeated draws after owed native
work is drained. Canonical particle/material/cache state is compared separately
from tinted packets.

The owned136 mesh is submitted through the actual existing native GL draw path
under saved state and rasterizer discard; current GL color readback matches
the expected native or sampled blue value. Native exercises one color mode;
each source appearance exercises both. The real mesh has no list color commands
to overwrite that value. This is bounded GL state/packet proof, not a screenshot
or visible-pixel claim. No captures are created.

Owned source and AMHE1 inventories remain unchanged; all generated imports are
deleted. Echoes RVZ SHA256 is
`ba8996256d005749dd6b7ff29ea30e5998451a3741ec87494a6217b01b703529`;
AMHE1 inventory is
`c1c59fc4c406145310a324cd92a8a65cdfa01bd9bd783d1aa4f72629d1ba33f0`.
Raw metadata-only logs/receipts remain under ignored
`artifacts/sh-integration/source-ball-boost-*`.

The same final core/tool pass66 product-launch lifecycle checks and589,289
legacy trail runtime checks. All three legacy1,415-step gameplay traces remain
`f427384c4531686dfaef0d11cb0f35fce029e55dc4823bea0d85e11f136d6ca6`;
active trail/RNG traces remain
`f7dae29fdb934da32955c6f0e04352d3a9f974d39c2b3b6a160ce5d7b813e945`.
Native retains103 yellow trail pictures; each source form retains591 blue and3
native fallback trail pictures. This verifies the shared renderer's untinted
default path and the unchanged historical script alongside the new boost path.
Final ignored qualification receipt SHA256:
`2f0bb2e66eb752a0f99a71aaaee01cd306f3223c8af7e98952714054ad9e39fa`.
Raw boost log SHA256:
`2c4f0d53d3324c76426b5bfadb02f8f8da142d11f48e4feb86012a60020f20c3`;
launch `6428d2ff877e95e16aabe5288599b887478590049198a9a804cd68ca6e30bb8f`;
legacy trail `fab7600f3b7c1634606137a21fb58ab9b36a938e9ef2b109bc7c88a6e5a99e2c`.

Use the named SDK and final reference-disabled tool build, existing AMHE1 via
`PRIME_BODY_TEST_ASSETS`, null audio and isolated user data:

```text
dotnet tools/external-import-check/bin/Release/net10.0/external-import-check.dll --probe-character-source-ball-boost-runtime <owned-Echoes-RVZ>
dotnet tools/external-import-check/bin/Release/net10.0/external-import-check.dll --probe-character-body-launch <owned-Echoes-RVZ>
dotnet tools/external-import-check/bin/Release/net10.0/external-import-check.dll --probe-character-source-ball-trail-runtime <owned-Echoes-RVZ>
```

## Remaining engineering

Normal procedural core, native furl tint through supported source transitions,
animated guns, imported drones/abilities, mixed-seat authority and online/replay
admission remain engineering. Source recipes, payloads, protocol83 and historical
replay shapes are unchanged. Existing cosmetic disable restores native drawing.
Other platforms/physical controls are not requalified; the user requests this
Windows machine going forward and defers Android testing.
