# Private replay opaque submission order

Private replica scenes preserve the original opaque packet sequence. Ordinary
live scenes keep their existing opaque state sorting. Scene.Services.IsReplica
selects the policy when Scene captures its retained render world; it does not
change native simulation, replay facts, checkpoints, materials or resource
ownership.

The causal failure was exact native DeathAlt recording/clip seek rendering on
MP6 HEADSHOT. At the first positive native frame (31), linear and seek gameplay,
all depth bytes, full native checkpoints, actor/effect/camera state and all 206
uploaded logical texture sources matched. World/HUD color differed at one RGB
pixel. Retained state keys include allocation-local GL texture handles, so
numeric hash sorting produced a different opaque submission permutation after
reconstruction. Those permutations can change coplanar depth ties without
changing final depth. The old candidate and original A3 client both failed the
same exact check. These failures remain preserved, not accepted with tolerance.

The tool-only original-opaque-order counterfactual changed submission order after
ordinary preparation and rebuilt actual adjacent batches. Recording and clip
seek then both matched exact color/HUD/depth, each with 60 positive DeathAlt
frames. That receipt is explicitly COUNTERFACTUAL_ONLY/INCOMPLETE. The production
fix implements the same scope through the existing scene-owned replica policy;
a fresh fixed-candidate default run must establish acceptance.

The retained Capture API takes an optional ordering policy. Submission ordering
skips opaque reordering but still builds adjacent state batches and eligible
adjacent opaque room groups. Combined geometry still requires the existing full
state equality proof, including transforms, matrix palettes, emissive intensity
and lights. Decal/translucent ordering and their existing barriers are unchanged.
No general material hash or sort architecture is rewritten.

The device-free retained graph check exercises the policy with deliberately
reversed state-key order and relocated texture/display-list handles. Private
captures must keep the original sequence; ordinary captures must continue state
sorting. Adjacent uniform-identical packets remain mergeable; different world
transforms remain forbidden from a combined draw. The existing Linux OpenGL
workflow runs this check with `ProjectPrime -rendergraphcheck`.

Private replay may submit more state changes and smaller room batches because
identical material packets separated by other packets are no longer globally
grouped. Adjacent batch/state reuse, room descriptor caches and eligible adjacent
indexed draws remain available. The change avoids sorting work in private scenes;
its net replay cost depends on the map and view. No measured private performance
claim is made until a fixed candidate is profiled. Ordinary live sorting and its
performance path are unchanged.

Rollback is a revert of the scoped ordering-policy change and graph test. It
restores state sorting for private scenes and reopens the exact DeathAlt seek
fidelity failure. It requires no wire, replay schema or asset migration. Do not
roll back by introducing pixel tolerance or camera/checkpoint normalization.

Native evidence is external to Git under work/net21-reconcile:

- `a3-native-DeathAlt-edb-seek-diagnostics`: exact native checkpoint comparisons;
- `a3-native-DeathAlt-edb-gpu-readbacks-fixed`: exact uploaded texture/sampler and
  prepared item evidence;
- `a3-native-DeathAlt-edb-original-order-counterfactual`: causal positive control;
- `a3-native-authored-DeathAlt-original-control`: preserved original-client failure.

Source changes and tests are prepared; build/graph/native checks must run after
the coordinated timing hold. Their results must be recorded against the exact
fixed source/binary before claiming closure.
