# CW-05e evidence — scopes, canonical HUD and reversible feedback

Base SHA: `3dd5a0b833e67e43d840997c6a30b475fad5e017`, latest main at final
integration, including upstream campaign clock/results timing and credits model,
bitmap and CPU text-layout changes. Protocol69 is inherited. Head: accompanying slice
commit. See [contract](../05e-scope-hud-feedback.md).

Environment: macOS arm64, .NET10.0.401, `DOTNET_PROCESSOR_COUNT=2`.
Main build: `/Users/jarrett/.dotnet/dotnet build src/MphRead/MphRead.csproj -c Release -m:1 -p:UseSharedCompilation=false`.
Sources remain frozen during compilation. Tools build serially with
`BuildProjectReferences=false` against the completed main DLL; no mapped DLL is
overwritten while its process is active. Exact commands, environment, timings,
raw exits and before/after main/copied hashes are retained in
`artifacts/custom-weapons/CW-05e/`.

Final integrated main build PASS63.99s, zero warnings/errors. DLL SHA256:
`6008f75dc71d662b2b43078c5b1c4161d8f7892a319e86ce8e55cc41ac8bba8c`.
Fresh custom/nettest/credits tools build PASS3.340s/5.518s/4.361s with zero
warnings/errors. The integration was clean; its temporary checkpoint is retained
until this slice is committed, then only that exact checkpoint is removed.

| Final integrated acceptance | Result | Metadata |
| --- | --- | --- |
| Pure custom / actual headless scope | PASS14527/37 | 05e-integrated-custom-results.json |
| Actual OpenGL scopes/cadence/bounds/fire feedback | PASS4859;22 composite PNGs | same;captures-integrated-window |
| Existing local/offline/public client | PASS28/46/24 | same |
| Native selection / client projection | PASS180/145 | same |
| Replica/readiness/admission/source/authority/rewind scenes | PASS173/127/249/318/985/156 | same |
| Focused controller enhancements | PASS75 | same |
| Actual client runtime / native isolation UDP | PASS75/680 | 05e-integrated-nettest-results.json |
| Campaign clock/results / native tables / native admitted / Imperialist | PASS61;unchanged;343/949 | same |
| Native fidelity | 46/47;exact known divergence, raw exit1 | same |
| Pointer / launcher layout captures | PASS1790/34 layouts | 05e-integrated-main-credits-results.json |
| Upstream credits CPU source/duration/timeline/model/binding/decode/mutation/bitmap/text | PASS239/87/89/158/139/48/2000/798/104 | same |

The16 final custom commands all pass in34.166s. All final focused nettest commands
pass except the exact established fidelity divergence. SHA hashes match before
and after every command. Broad transport/Q/P/M/L/K/I/J qualification remains the
separate9529 candidate below; final changes after that qualification are optics/HUD
presentation and upstream credits decoding, with final engine/native coverage above.

The first qualified candidate (`9529ee52f2cb6a1d5b590e550c070aa149b8b814eade5c0da5787efca161e72d`)
built in81.74s with zero warnings/errors. Pure14527, scope headless30 and actual
GL4558 passed. Full custom scenes and public client24/controller75 passed.
Candidate UDP coverage includes authority acceptance/reconnect/public replicas,
atomic input/reliable controls/action delivery/readiness/source-boundary suites;
see `05e-candidate-nettest-results.json`. Campaign61, touch155, crouch75, actual
client runtime75 and native isolation680 passed. Native tables/343/949 passed.

The next candidate (`a714379d62959cf4be97404d0e81cb72b129204de6dbfbbeed5e055bb4419808`)
built in58.24s with zero warnings/errors. It fixes meter threshold colors, gauge
geometry/orientation/independent layout and scope cleanup on role replacement.
Its final fresh tool suite passed16/16 commands in25.080s: pure14527, actual
scope37, GL4558, and prior local28/offline46/public24/selection180/projection145/
replica173/readiness127/admission249/source318/authority985/rewind156/controller75.
See `05e-final-role-fixture-results.json`. Its focused fresh nettest build and
campaign61/runtime75/native680/343/949/tables pass in
`05e-final-nettest-results.json`. These hashes are distinct from the subsequent
HUD layout correction; they are not relabeled as final layout results.

The real GL fixture uses the Apple native OpenGL renderer, real scene input and
rendering and virtual60/120/240Hz presentation schedules over240 fixed steps.
Canonical state, cached HUD and FOV traces match across schedules. It inspects
the actual renderer perspective matrix at user FOV60/78/90/120 for both2.5x/4x
lenses. Repeated draws cannot advance resource/reload state, HUD snapshot or
simulation FOV. This qualifies synthetic cadence on the actual renderer, not
achieved display rates, a physical input device or an online source engine.

The headless fixture preserves native Headless/Roam mode and exercises actual
keyboard/mouse input, both optics, sim/render aim scaling, scoped camera/gun
alignment, actual ammo spend, denied fire, canonical reload, hold release,
AmmoNotFull visibility, death and respawn. Role cleanup uses explicitly declared
reflection seams for authority and replica policy; it does not claim an actual
role-switch transport or reconstruct a custom replay. Resource snapshots before
and after that policy seam remain identical.

Pure tests include independent lens goldens and perspective ratios, safe finite
bounds, real isolated controls save/load under a non-English culture, preference
validation, native setting preservation, hold transitions, bounded result queues,
identity/reorder/denial/overflow and fixed-step feedback. Warmed100,000-iteration
math, controller, result-consumption, feedback and projection probes record0B.
No tests write the user's controls file or commit extracted game content.

Corrections remain visible. Two main build attempts failed on the new HUD's
missing `MphRead.Hud.Align` import; the initial attempted namespace was wrong.
A scope fixture initially required Player camera in intentionally Headless/Roam
mode; an attempted forced Player camera exposed missing headless HUD textures.
The final headless fixture preserves the native mode and tests the actual local
owner separately. An authority-policy fixture initially changed Role without
its independent IsAuthority flag; both declared fields are now set/restored.
No runtime policy or assertion tolerance was weakened for these fixture fixes.
An asset-backed nettest command initially omitted per-run content paths and was
refused before simulation; configured runs pass and the failed raw log remains.

Initial image capture used the world-only offscreen target. Those18 images are
lens/world evidence, not HUD evidence. Later pre-swap visible-window captures
include the real composite HUD. Visual inspection of those found numeric ammo
clipped in Classic and Custom; the typed panel now uses the established58-by20
ammo footprint and bounded Classic origin, with style-aware gauge placement.
Earlier captures remain retained separately; final corrected captures and bounds
validation qualify the committed geometry. In the clean default Custom profile,
the actual transformed panel is1055,877.5–1272.5,952.5 within1280-by960. Both
Classic and Custom captures visibly include the full04/15, optic label and gauge.
A later review also repaired the generic-crosshair early return that skipped
predicted pulses in Project Prime/Custom; separate real fire captures and
reduced-motion pixel comparisons now qualify those paths. These are scene
composite captures, while launcher34-layout capture validates construction/layout
and does not claim physical focus or click interaction.

All qualified native fidelity runs retain46/47, raw exit1, with the exact known
`missile.splash` tick177 `victim.position4096.z` expected52814/actual52815 mismatch.
This is the same CW-05d baseline, not a new regression or47/47 result. Native
Imperialist headshotF2 remains
`6fc157d2c71568e56a37675a590c04c764f29e2d8e2fc5f1cda394b3c3d78351`.

Physical controller/Android, WAN, custom replay and final release acceptance are
NOT_RUN. Public activation/loadout selection remains the next CW-05 slice;
original rifle assets/rig/audio/impact presentation remain CW-06. Full CW-05 and
the requested CW-16 sequence are not complete. CI/review are not awaited or
claimed passing, as the user requested.
