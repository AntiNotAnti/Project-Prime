# CW-05j evidence — accessible match selection

Base `0ccee5caf6a43f4d3db41cf5b83843a4ecf533e6`, including upstream PR842
(native Home bindings), which is not authored here. [Contract](../05j-accessible-match-selection.md).
CW-05 remains in progress.

macOS arm64, .NET10.0.401, `DOTNET_PROCESSOR_COUNT=2`. Final desktop Release
build PASS70.930s, zero warnings/errors. Runtime SHA256:
`97375d268516903c2a1482cfbc82826e5a1c7b76211f99497f9898b8c4c7af01`.
Android Debug experiment=true PASS89.292s,26 warnings; default PASS68.050s,
25 warnings; both zero errors. These compile the actual head/shared sources and
produce APKs; device execution and packaged native RmlUi rendering are not implied.
Isolated dedicated-server Release PASS79.131s, zero warnings/errors; subsequent
incremental PASS0.855s confirms the desktop DLL stays unchanged.
Sources were frozen during compilation. Tools use `BuildProjectReferences=false`;
all final tool copies match the accepted desktop SHA256 before/after execution.

| Acceptance | Result | Metadata under artifacts/custom-weapons/CW-05j/ |
| --- | --- | --- |
| Pure mailbox/context/input/resource contracts | PASS14852 | 05j-reviewed-custom-results.json |
| Actual offline menu-selection Scene | PASS30 | same |
| Actual ordinary-client menu-selection/UDP boundary | PASS27 | same |
| Existing public offline availability/window launch | PASS18/17 | same |
| Scope/local/offline/public-client | PASS37/28/46/24 | same |
| Selection/projection/replica | PASS180/145/173 | same |
| Readiness/admission/source/authority/rewind | PASS127/249/318/1014/156 | same |
| Focused synthetic controller | PASS75 | same |
| Actual NetSession/native isolation/native accepted shots/Imperialist | PASS75/680/343/949 | 05j-final-nettest-results.json |
| Architecture/protocol/replay/movement/pickup/targets/retirement/chat/reliable | All14 remaining commands PASS | same |
| RmlUi runtime/offline rules/live-menu/Avalonia pause/restoration | All5 build/test pairs PASS; restoration124 | ui-results.json |
| Actual Avalonia pause controls/callbacks/captures | PASS46;12 PNGs | pause-selection-ui/pause-selection-ui-evidence.json |
| Registry/native tables | PASS78; exact old IDs/names/tuning | final-ancillary-ancillary-results.json |
| Native fidelity | Known46/47, raw exit1; exact established divergence | same |

All19 custom and19 nettest driver commands pass, including their builds. The final unchanged-binary continuous-target run passes. The offline fixture uses
actual assets, public availability, fixed stepping and the sole input sink. It
checks native-selected4/16, one Equip/Return, pending native fencing, native key
priority,100,000 side-effect-free UI reads, menu-resume hold, morph/freeze/text
restrictions, death/respawn and cleanup. Native ammo neutrality is checked before
respawn, whose native lifecycle legitimately owns its ammo reset.

The client fixture runs actual NetHooks and control91/result93 UDP through a
transparent proxy. Authority baselines/results and native life adoption are
explicit synthetic facts. It verifies full request identity, no automatic retry
after a denied native return, explicit retry and retirement before Life2 can
consume an old choice. It is not a DedicatedServer combat E2E qualification.

Review caught an old displayed menu click adopting a new life/bootstrap/manifest/
Scene publication with identical visible booleans. InGameFacts now carries the
immutable witness, Dispatch compares reference identity before backend execution,
and Refresh advances the revision on replacement. The Scene independently checks
the exact publication again at enqueue. The70 new restoration assertions cover
five replacement variants, both actions and seven checks each;124 total pass.
These witness contexts are fake immutable fixtures; actual Scene/mailbox behavior
is covered separately. Unchanged publications and warmed reads allocate zero;
full input-sink capacity consumes a request once and does not create frame retries.

All12 production PauseMenuView captures (native/custom/pending/refused at940×528,
800×360 and360×800) were visually inspected. Desktop presentation is acceptable.
Landscape20-point buttons/tiny text and portrait clipped Quit/scroll region remain
**mobile usability failures**, to fix in CW-05k. The fixture records actual Skia
images/geometry and synthetic events; it does not include PrimeShell/UiScaleHost,
physical touch/controller input or an Android device. Physical/platform and WAN
acceptance remain open. Scope feedback so far is reversible reticle presentation;
original weapon recoil/animation remains to be implemented/qualified.

Earlier candidate/witness-fix hashes cfe6e538… and9a3f984c… are not final
latest-main attribution. The first server path override failed90 generator errors;
a later late-props build passed with3MSB3539 warnings but reused desktop restore
metadata. It caused an Avalonia dependency failure in one fidelity invocation.
The corrected early `DirectoryBuildPropsPath` gives every project separate bin/obj
paths; a fresh desktop build repaired dependency metadata without changing its
accepted DLL hash. Corrected server and fidelity results above supersede that
failed setup; raw logs/results remain under `pre-final-retry/`.

A loaded concurrent run failed the existing continuous-target warmed-allocation
assertion once. An isolated unchanged-binary retry passed; final suite attribution
is recorded separately. No production change or weakened assertion masks it.
Native fidelity still differs only at `weapon.missile.splash`, tick177,
`victim.position4096.z` expected52814/actual52815. Imperialist headshot F2 stays
`6fc157d2c71568e56a37675a590c04c764f29e2d8e2fc5f1cda394b3c3d78351`.

Complete commands, raw exits, times and hashes remain in ignored local artifacts.
No extracted assets, captures, SDK/JDK archives, APKs or credentials enter Git.
CI/review are neither awaited nor reported passing. CW-05 and CW-16 are incomplete.
