# CW-12h — phase-preserving custom clips and killcams

Rolling custom replay capture is explicitly enabled with
`PROJECT_PRIME_CUSTOM_WEAPON_ROLLING=1`. Exact prepared resources and the accepted
client lifecycle remain required. Installation never activates a weapon.

The recorder stamps each custom fact once, before publishing it to the rolling
timeline, live reconstruction and asynchronous disk sink. Frozen clips retain
the custom format and phase metadata. Post-world facts keep their original
source identity; application and killcam frame selection use the following world.
An EOF post-world suffix is validated without being applied.

Custom frozen clips restore their exact checkpoint into a private Scene, retain
the package resources and advance through the same typed phase reconstruction
as archive playback. Instant clip saving preserves the package descriptor,
hidden lead-in and phase envelopes. Killcams use the private player directly,
without a temporary archive. Camera selection supports custom first-person
inspection. Custom killcams do not take global native audio ownership; complete
custom audio fidelity remains unfinished.

Desktop Release compilation succeeded with zero errors and two existing warnings
(CS0649 and CS0618), MSBuild 78.48s. Binary SHA-256:
`e3de700caca631af54492012d44a4a2a82ef6b40429f9ae242e4528e19d4eed0`.
Receipt: `artifacts/custom-weapons/CW-12h/compile/desktop.json`.

All new runtime, replay, killcam, visual, security and performance acceptance is
**NOT RUN**, following the user's instruction to skip remaining tests. Android
builds and device qualification remain deferred. CW-12 still requires bot
integration, portable exact-package bundles and remaining presentation fidelity.
