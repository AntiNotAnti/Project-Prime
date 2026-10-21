# Imported drone burst presentation

Alpha/Gamma solo Scout Drone practice now uses source unfold, firing-loop and
fold animation alongside Ready. Existing native burst state/frame owns the
phase; imported bones never emit shots or set native muzzle, timing, movement,
damage, sound, beam pool or RNG state. Laser and unsupported status/morph/alpha/
shadow pictures retain complete native fallback. The native laser draw remains
unconditional after the mobile-model choice.

Admission is the same explicit solo Weavel Battle/playtest with zero bots and
native movement as [PR967](https://github.com/AntiNotAnti/Project-Prime/pull/967).
There are no new preference values, Hunter IDs, intents, wire/replay formats,
source geometry, material archives or bundle versions. Both v1 imports already
contain these strict raw primitives. The load worker validates all four before
Scene creation; each initialized actor privately derives them over its own full
33-joint source rig. All1,390 triangles and both opaque material groups remain
whole, with one fixed Ready0 pivot and scale0.3 throughout every transition.

## Closed source motion and phase

The raw source bank and shared rig remain immutable. Exact ordinal/resource/SHA
pins admit only these four original primitives to the actor-local RootPin policy:

| State | Ordinal / ANIM | Duration | Raw SHA256 |
|---|---|---|---|
| Ready |13 / `b45b6445`|1.2999999523162842|`d714b3f01328ad897a82ed67de2f3e20e0922283bd9e453c269939684cc46cb0`|
| Unfold |75 / `dbd60f49`|0.5333330035209656|`eb724be315c52be55b7d7213cbfe386f886c497fd86ad18bfce8912bc7643cce`|
| Fire loop |73 / `90d23d02`|0.1333329975605011|`1ed47ddcdb8105a7f914c2b5de87658c32b9bf4daf8aec34dee19dbcbcd2991d`|
| Fold |74 / `83618e64`|0.4000000059604645|`707500e064576b053e04d18c63ee5007ffea8810dc979a105df9ec5dcdebe35a`|

Clip names/selection, Snap entries and held return blend are adapted from pinned
MIT SecondHunt DroneBody/NativeBodyAnimator at
`41452f2d053fcfa679e33c00a73d120a53f59e61`, Copyright2020 NoneGiven; LICENSE is
retained. The existing sampled-key RootPin math and source sampler are reused,
including its1e-7-second offset. Donor serialized float playback is not claimed
bitwise equivalent.

For native **post-step** fire frame `f`, unfold samples
`D * min((f+1)/32,1)`, fire samples `((f+1)/60d) % D`, and fold samples
`D * min((f+1)/24,1)`. Valid counters are0–31,0–149 and0–23 respectively.
The first observation advances one authored phase on entry, and the last unfold/
fold picture reaches its original duration. The fire loop uses original duration;
its nominal eight-step wrap yields approximately3.3577e-7seconds rather than
silently substituting an exact eight-tick duration.

All three burst entries Snap. Return to Ready restarts its own elapsed counter
at1/60second and blends from the held previous source pose over0.12seconds:
`min(n/(0.12*60),1)`, completing on the eighth supported step. Local rotations
and translations use the unchanged existing body Mix helper. At weight1 that
helper still reconstructs the target quaternion; exact float results are tested
separately from raw target matrices.

Unsupported simulation steps clear held blend history. Re-entering a running
burst samples the current native frame, so an already-firing native attack cannot
restart imported unfold. An interrupted Ready state preserves its Ready clock;
return from an observed burst restarts it. Life resets, pause and extra pictures
retain their existing owner boundaries. `CompletedSteps`/material time now count
all supported source states, while Ready elapsed phase is separately owned by
the same actor. Draw never calls the stateful pose sampler.

## Windows qualification

Production freeze `dac883b40e80dd533eafd9e77f0f8c9f36b264c6`, SDK10.0.401,
Windows x64/NVIDIA RTX5080. Tests first on frozen Ready core `b14badf4…a5de`
refuse the missing burst pose contract before owned/native asset I/O, Scene,
rendering or gameplay activation. The new source burst smoke then passes191,983
actual runtime checks on the candidate core.

Native, Alpha and Gamma retain the same1,418-step native-state trace SHA256 as
the Ready slice:
`aa44ee6f8e9a3a78fe7ccf1b58b7e79db1d5cdf3fa39731a5218e43bd25acefc`.
The grain records actor/body state, selected drone/laser counters/endpoints,
40 beam state columns, native registered animation frames and Scene/global RNG.
Each import replaces412 former native burst observations with two complete
source material packets and zero native mobile-model packets. Native laser
fallback remains268 observations; the earned full burst still emits38 shots.
The full33 raw CPU pose/palettes are checked in each source picture. Actual GL
uniform readback checks the final batch per shared program at12 selected native
phase/fade keys; this is not a simultaneous33-uniform-slot observation. Material
residency, locked input files, repeated
pictures, pause/fractional schedules and import/context/native-rule lifecycle
are exercised through the existing real product fixture. There are no opponents
or opponent-kill claims in this solo script.

A separate420-step genuine-input script covers quick release with zero shots,
cooldown, another full38-shot burst and a labeled cloak interruption. Native,
Alpha and Gamma match its independent native-state trace SHA256:
`8a738ab661fdb308a8e140142feca62602e83a4e995bb9fb666671d89bea299f`.
Source stays native through five cloak steps and two further native opacity
recovery steps, then reacquires the current firing phase at native frame48.
Fire-phase pause, draw guards and return-fade history are also checked separately
from the fixed1,418-step counters.

The independent frozen83fe oracle passes twice with identical metadata:
2,038 full-rig samples,67,254 joint-pose sets and3,389,194 source-position
evaluations. Per appearance it covers649 Ready,108 unfold,178 fire and84 fold
samples, including all native burst phases. The independent raw sampler error
is zero. Separate252-row policy traces per appearance add2,034 assertions for
Snap, quick release, held return fade, interruption and current-frame recovery.
OpenTK float reproduction hashes remain distinct from independent double math;
the measured local blend difference up to9.59e-5 is a precision diagnostic,
with no acceptance-tolerance change.

Desktop source-body2708, dedicated2690, replay-format3107, replay-compatibility694,
offline-character971, architecture and Rev2 recipe199 checks pass. Existing
actual normal/Final launch66 and four-Trooper launch83 checks also pass.
The actual Windows native Hunters contract passes469 checks each at1280x720
and640x360 with the updated burst hint. Native bridge `a66fdcfa…902c` remains
verified against its unchanged source fingerprint; no native ABI/build change
is introduced by this slice.

| Binary | SHA256 |
|---|---|
| Desktop core |`8480e22d7ff049950e91cde9c7ce3fe83c1357d2199cd44fff32b18c59e6914d`|
| Dedicated core |`7281db44d1bfe01fb824034707c34b5cafb610858ea6a5aa89d02b226eb583fe`|
| Initial burst/regression tool |`74bfbf8166efd54663150f88ca11c6541b564706a8c934be5e328a5ea8fc21d2`|
| Final burst tool |`9fe2693b2cd8cfa8ca83c140d4354efe3a430f1e3df8e89824baf09d108309a6`|
| Nettest |`0d6c3ce10833962e63c9f7215d905d5365b291719cefb4c9da6ad098dfaf4127`|

Oracle receipt SHA256 is
`28569429952309ce91bd92d7ee54e8b256e71826e9aed7afcc68b21a3fea7d11`;
metadata `1ca06a6d7869581613011a8ab076c190429e391773763a61d080203876ec9ad9`,
algorithm note `0fdb511ae263da4efc68bd311077c5b9ab7dc7ef80280eb029465ac9cf103881`
and focused UI receipt `2d41ff038e302d095f96e4aef588142420b8d606bf4b536f6a85f8f9eec58a2c`.
Receipts/raw logs remain ignored and contain metadata only. Owned sources remain
local and unchanged; disposable imports are removed after qualification.

Final qualification passes244,385 checks; the optional local capture run passes
244,390. The raw final log SHA256 is
`7c760fc1f45cd04b62a7a2e7c8fae382b28c59f1f2de4b8ed27201453723736a`;
compact receipt `source-drone-burst-qualified-receipt.json` SHA256 is
`c02863bbaffe16204b99cf670b4702afe88d3bb5e6f4989eebad0551c0f27cdb`.
Per source appearance the combined fixture observes4,316 source pictures,
300 native pictures,2,845 repeated draws,2,158 complete33-joint CPU pose checks,
1,405 phase checks and24 held-return fade steps. Counts include separately
labeled guards and schedules, not just the canonical1,418-step trace.
Alpha/Gamma source firing
arms and distinct material colors are visible in the real map/HUD alongside
native projectile glow. The small actor and HUD overlap limit detailed lighting
and muzzle acceptance. Both final captures and two earlier failed-run Alpha
captures were inspected, hashed and individually deleted. Capture inspection
receipt SHA256 is `5378b892b407d2a88f01b74efb3b5bb26c782a662e96753768136d067eb8538b`.

Two retained fixture failures caused no production changes: the first incorrectly
expected immediate source recovery after clearing cloak, before native opacity
had recovered; the second mixed added guard pictures into the fixed-script412
burst observation count. The corrected checks preserve the existing opacity
fallback and keep extra pictures separate from fixed-script evidence.

Laser, movement and hit source animation, source barrel/native emitter visual
alignment, combat/arsenal appearance admission, independent multiplayer seats,
broader visual/performance acceptance and the rest of the integration plan remain
unfinished or unqualified. Android work remains deferred under the user's
Windows-only direction. No external CI/review approval is claimed.
