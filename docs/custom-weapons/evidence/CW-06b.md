# CW-06b evidence — original held-rifle presentation

Initial complete validation base: `2771fda3bfb7996315cc1d6754fcd7fc76a18638` (main, protocol76).
Final integration base: `33a0de54083bfa4d04927adddc35baa99816a20a` (main, protocol76).
Design began at merged CW-06a `0c3a281b683ac062848bc21a7fe86ef24e194cc5`.
Head: completed-slice commit containing this file.
[Implementation](../06b-rifle-presentation.md).
Initial desktop game SHA-256:
`55e4fa7e7c47606210fa661d868ad3dbf4db79591e5a6e6fc7e2ae4d62f488c2`.

The implementation is complete with the release blockers below. Android's
ordinary pickup/fire/scope/empty/leave workflow passed on the identified APK;
reload and fresh re-entry did not establish acceptance. Latest-main integration
validation is recorded separately below.

Commands use .NET10.0.401/macOS arm64, DOTNET_PROCESSOR_COUNT=2,
UseSharedCompilation=false; desktop/server/tools Release, Android Debug arm64.
Tool builds use BuildProjectReferences=false. Each asset process has an isolated
PROJECT_PRIME_USER_DATA/paths.txt. Sources/assets are frozen during target builds.
Wrappers record game and fixture hashes before/after execution. Android/server
retain the exact desktop DLL above; their separate outputs cannot overwrite it.

| Check | Result |
| --- | --- |
| Desktop Release | PASS186.500s,1 existing warning,0 errors |
| Isolated server Release | PASS546.047s,1 existing warning,0 errors |
| Android default arm64, embedded assemblies | PASS658.060s,26 existing warnings,0 errors |
| Custom fixture build | PASS4.06s,0 warnings,0 errors |
| CPU identity/mechanics/network/payment/presentation | PASS23,511 assertions,46.423s |
| Seven actual native rigs and original held model GL | PASS1,335 assertions,99 captures,314.666s wrapper |
| Original asset/pickup GL | PASS133,22.134s |
| Public pickup / acquired wheel / acquisition during native hold GL | PASS79/143/152 |
| Serial custom scope native GL | PASS4,859,19.055s |
| Actual offline / local input / public client input / online pickup scenes | PASS46/28/24/22 |
| Original packet registry and MP+1P tables | PASS;78 original names/IDs and native tuning exact |
| Native fidelity | Known46/47,raw exit1; same missile-splash baseline |
| Serial network acceptance | PASS22/22 commands, including actual UDP public pickup/combat; public lane202.062s |
| Private Android emulator ordinary pickup workflow | PARTIAL: normal touch pickup4/16, fire debit, scope2.5X, empty and Leave; reload unverified, re-entry driver crash |

The CPU suite includes4,252 strict original-model assertions,289 original PCM
format/hash/signal assertions,325 cosmetic context/clock/mount assertions and173
PCM voice assertions. Independent integer waveform vectors cover all four cues.
Full context substitution, accepted/predicted dedupe, superseded refusal,
silent baseline, bolt/reload/raise curves, seven rigid mount transforms,
finite guards and actual authored vertex clearance are checked. The declared
CPU scene/actor/HUD seam exercises real feedback/synchronize/step dispatch to
prove current morph/alt/unmorph/death/deselection/new-life cancellation before a
due bolt cue. It does not claim actual morph simulation or audible output.
100,000 warmed samples/mounts and PCM Play/Render/Stop cycles allocate0B;
separate concurrent command/callback stress proves finite bounded output and
coherent atomic cue/gain pairs, without an allocation or device claim.

The seven-rig fixture uses actual native scenes, PlayerDraw and synthetic
keyboard fire/reload/scope/native selection. Each fixture explicitly grants a
developer presentation owner; genuine public acquisition is validated by the
separate pickup/online lanes. Native LOD0 submits16 original boxes. Declared
LOD1 bone/render seams add6 beside the normal16; the22-box seam is not a claim
that automatic distance LOD1 alone was pictured. Side/front views use only a
presentation camera. Repeated display-alpha pictures preserve canonical
resources, clocks, actor state, both RNG streams, camera FOV, aim/gun/muzzle
vectors and native body matrices. Samus's first-fire world/viewmodel projection
error is0 pixels; exact owner/weapon is required, foreign life/native identity
refused, and canonical projectile spawn/back/front remain unchanged.

All seven records show8 initial and8 post-rewarm PCM voices. Scene cleanup and
actual visual release retire both owners to0 and clear model ownership; fallback
pictures submit3 original boxes without loading. The next fixed step rewarms.
Headless public scenes never attempt a model or construct audio. These are
scene/resource ownership checks, not subjective audible quality or a physical
Android surface-loss measurement. One fixed voice per slot intentionally
coalesces/restarts overlapping cues; attenuation is not stereo positioning.

All seven FP idle/fire/reload/scope and TP side/front captures were inspected.
Idle mounts are coherent in these poses, scope hides the custom FP module,
and traces meet the displayed muzzle. Trace's fire capture retains a small red
native-cannon sliver through the lower module during recoil; this minor art-fit
limitation remains recorded. Stills do not establish continuous animation
smoothness, extreme-pose clearance, remote-motion behavior or achieved Hz.
The native arm/cannon remains the chosen mount support.

The first fixture used O before entering scope; it correctly failed. Controls
were corrected to Z then O, and receiver clearance/scale was corrected after
initial captures exposed clipping. Source review also found the bolt cue clock
ran before form reconciliation; it now advances after synchronization, with
current-form guarding during earlier launch ingress. Capture review found a
body-origin tracer streak; the first local flight segment now joins the exact
prepared barrel in world projection. Online joining additionally requires the
exact current local bootstrap and a recent authority world, not receipt age.
None of these fixes changes projectile, emitter, aim, health or authority data.

Integration initially caught an Android-only unmatched parenthesis in main's
GL binding cache initializer. The2771 validation used one added opening
parenthesis. New main33a0de540 independently fixes it by removing one closing
parenthesis. The final branch retains the upstream fix exactly, with no
CW-06b change to that file. Main's protocol76 movement update is retained; historical
custom-pickup boundary75 and native payloads remain unchanged. Early protocol75
build/results are retained separately and are not presented as final76 evidence.
One obsolete pre-integration APK build was owner-cancelled during BuildArchive;
its raw exit0 is explicitly classified CANCELLED, not successful APK evidence.

Initial contended76 network execution failed a combined resend/count/health
assertion; immediate retries also exceeded the client-admission deadline while
the server was still warming resources. Concurrent scope GL exceeded its120s
wall-clock bound. Their complete logs remain retained. The unchanged serial
scope suite passed19.055s; the unchanged serial network run passed all22 commands. Public availability
reported918 configuration/client,8,397 action and4,226 control assertions;
standalone action/control/input UDP lanes also passed. Retry-dependent assertion
counts can vary with actual datagram delivery.
Network and scope assertions were unchanged between contended and serial runs.

APK SHA-256:
`c9dc8d28114d42a9850a281239889e8cf6fd399ffc86c99a6d394abd78762953`,
76,905,893 bytes, arm64-v8a only. Android DLL SHA-256
`ea76251bed1b266f091a9d5a16da8c1a2f5341689cf806b7f973d46571760975`
is byte-exact inside the APK's ELF assembly wrapper at offset16,384. The exact
original model bytes/hash are present in that assembly. Private API36 arm64
AVD5582 was created for this task, network disabled and supplied only owned
AMHE1 data; the existing5580 emulator was untouched. The initial shell-owned external asset seed was inaccessible to the app. The
fixture was moved into package-owned internal storage through run-as; the
app's normal internal paths then resolved the owned files without broader
permissions. A fresh Debug launch hit an existing automatic map-preview assert
(`effectId == 162`, missing effect during ThumbnailMode) before gameplay. Both
crash logs were retained. For gameplay testing only,56 actual host-rendered map
thumbnail PNGs were copied into this private app's thumbnail cache with hashes.
This is a declared cosmetic fixture workaround, not clean-first-launch
acceptance; the Debug preview crash remains a CW-16 release blocker. Normal Home → Offline Battle FFA (zero bots) → Advanced Match Rules enabled
availability through the ordinary UI. Actual touch movement entered the authored
world station: it disappeared and HUD changed to4/16. Touch Fire reduced4→3;
Zoom showed2.5X; later fire reached0/48 and EMPTY. Standing at the station during
long evidence capture naturally refilled reserve16→32→48 at its real respawns;
no grant, resource write, position injection or tick warp was used. Parent
inspected the actual pickup/fire/scope/empty pictures. SwiftShader world speckle
is visible, so these pictures establish workflow, not visual polish.

The hidden Reload/Optic controls were enabled through Settings → Controls →
Touch and Apply. A300ms Reload press and3.2s real wait still showed0/48. No
successful Android reload is claimed. Leave returned to the launcher in the
same PID5205, but the next normal match load failed with SIGSEGV on the
`hunter preview` thread, native top `GL2Encoder::calcIndexRange`/`s_glDrawElements`
in the emulator GLES driver. Earlier PID5017 had the same native crash class;
only PID4797 was the confirmed Debug effect assertion. Re-entry/cleanup is a
FAIL, not a context-loss pass. Complete actions, captures, provisioning hashes,
crash buffer and limitations are retained in
`device-private/device-acceptance-final.json`. Audio was disabled at emulator
startup; audibility is unverified. No emulator/SDK/APK/game files or captures enter Git.

Native fidelity remains weapon.missile.splash tick177 victim.position4096.z
expected52814 actual52815. Imperialist F2 remains
`6fc157d2c71568e56a37675a590c04c764f29e2d8e2fc5f1cda394b3c3d78351`.
The original model remains CC0 with documented provenance. New PCM is original
integer synthesis with no third-party/cartridge sample copies in Git.
CI/review NOT_WAITED per user instruction; no CI pass is claimed.
Physical controls/device audio, Android surface-loss, rendered online/WAN and
60/120/240Hz qualification remain CW-16 release gates. Custom replay stays CW-12.
Rollback removes the cosmetic binding/voices/VFX and restores existing fallback
without a wire/save migration. Local full logs/JSON/captures:
`artifacts/custom-weapons/CW-06b/`.

## Final main integration

Main33a0de540 adds replay owner/director draw scopes, Traversal slide poses,
server pacing/admission and Community/character tooling. All are retained.
Source review confirms custom FP still shares the prepared native transform;
replay owner sampling activates only in replicas where custom rendering is
gated. TP remains inside the presented skeleton window before canonical
muzzle restoration. New dynamic slide/module clearance is unqualified; upstream
slide tests establish native pose behavior, not M7 mesh fit.

Final desktop DLL SHA-256:
`692b4390d4a5baecd90ca7c3d6e46e3a7eb18e33e4d037723c330b0996eadd04`.
Final custom fixture SHA-256:
`f75ffe1e9a3ad901a6fe79ee40895dc69b79ce8a35aa25c83c4b4f207691b1e3`.
Desktop Release97.941s, server Release65.290s and default embedded arm64
Android129.449s all succeeded. Custom fixture build3.65s,0 warnings/errors;
23,511 pure assertions13.345s and1,335 seven-rig GL assertions/99 captures
38.806s passed. Packet registry and native MP/1P tables passed; native fidelity
remains the identical46/47 missile-splash exception with unchanged ImperialistF2.
The private emulator APK above belongs to the initial2771 build, and does not
qualify this newer APK. Final-build evidence is under `latest-main/`,
`latest-main-pure/`, `latest-main-rig-gl/`, `latest-main-ancillary/` and
`latest-main-network/`. Wrappers verify exact stable DLL hashes during tests;
server/Android compilation overlapped desktop replacement only during builds,
not while fixtures mapped the desktop DLL.

Final integrated serial network acceptance passed24/24 commands, including the
22-command prior suite plus replay owner-track and director-shot checks. Real
UDP public pickup/combat passed in205.221s. Actual Traversal stance/earned-slide
scene checks passed4.597s; this does not establish M7 sliding mesh clearance.
