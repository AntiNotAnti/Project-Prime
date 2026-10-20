# Traversal movement

Select **Traversal** in Practice or the multiplayer host's Match Rules → Movement. The host admits one shared ruleset; joining clients cannot override it. **Classic remains the default.** Quake III retains its existing horizontal coefficients and strafe-jumping behavior.

Traversal is an original optional ruleset, not a claim of exact Quake physics. It uses the fixed 60 Hz player simulation, native Hunter jumps, gravity, collision, damage, abilities and alternate forms. Native Hunter variants are required; Balanced and retired Enhanced rules cannot be combined with it.

## Techniques

- **Air control:** air acceleration is 2 per second and steering turns the horizontal velocity toward wish input at up to 1.8 radians per second. Steering preserves horizontal speed. Normal directional acceleration can add speed, brake, or build strafe-jump speed; it does not impose a total-speed cap.
- **Held hops:** hold Jump to jump again on a new physical landing. Holding never spends an airborne Space Jump. A fresh press retains native ability behavior.
- **Ramp jumps:** jump while running uphill. A recent walkable collision can retain its positive slope ascent in addition to the native jump. Flat ground, downhill movement, stale contacts, moving platforms and airborne grace flags cannot provide ramp lift. Lift is bounded by incoming horizontal speed.
- **Explosive jumps:** aim a missile or charged Magmaul splash near the feet. Self-blasts test the accepted biped capsule against the native blast radius. Native damage curves, immunity, line of sight and lethal self-damage still apply. Additive three-dimensional impulse uses base weapon splash strength and linear distance attenuation from the capsule, shared with Quake III missile jumps. Health handicaps do not change physical push. The authority derives unpaid self-hit outcomes from its own explosion witness; it never trusts the claimed blast origin or push. Magmaul behavior in Classic and Quake III is unchanged.
- **Crouch and power slide:** hold Left Ctrl (rebindable in Input settings). Crouch lowers the body and eye to 65% height while keeping the feet and radius fixed; walking while crouched uses half normal desired speed. Keep Forward held and press Crouch after reaching running speed to start a 72-tick (1.2-second) low-friction slide. Entry requires 90% of normal running speed (about 6.48 world units/second); slow walking and stationary crouching do not slide. A fresh crouch up to six simulation ticks (100 ms) before physical landing can start one slide on contact. Sliding adds no speed bonus or ground acceleration. Releasing Crouch, a fresh accepted Jump, leaving ground or hitting a lateral wall ends the slide; holding cannot refresh its duration. Release and press again to start another earned slide. An active slide takes precedence over automatic held-Jump hops; pressing Jump freshly still jumps normally. A blocked ceiling keeps the body crouched until standing fits. Alternate forms retain native behavior. Slides blend into a dedicated asymmetric extended/tucked-leg pose, including when Forward stays held. Crouch-walking uses planted support intervals, low foot lift and small pelvis shifts driven by completed horizontal travel. Native upper-body walking, shooting, charge and ability curves keep their own clocks and weapon posture. Rendered camera/cannon walking bob is reduced while crouch-walking and suppressed during glides. The rendered body and first-person eye ease through a six-tick transition; collision and source-time weapon facts change immediately and remain authoritative. Gamepad Crouch is rebindable and initially unbound so existing bindings are preserved.
- **Training-map surf:** use the tagged lane in **TRAVERSAL TRAINING**. Tagged sloped contacts retain airborne steering and gravity and clip inward velocity without ordinary slope speed loss. They do not count as landings or grant hops. Walls, ceilings and moving entities retain native collision behavior. Ordinary walkable slopes use the current grounded surface-speed policy; they do not become surf surfaces.

The training map has an uphill ramp at X=-30, a tagged surf lane at X=-10, an identical untagged control at X=10, a low tunnel at X=30, and a thin side-rail clearance obstacle at X=38.75. Geometry and the map recipe are original; generated assets stay in the user's extracted-data directory.

## Tuning display

Optional movement modes show horizontal and total speed in world units/second, completed AIR/SLIDING/CROUCH/GROUND state, and airborne strafe efficiency. Both speeds and contact/stance come from the body after collision; landing clears air-efficiency text. Efficiency compares the horizontal speed-squared gain that survived that tick with the best directional acceleration gain available at the same speed and input magnitude. `--` means the measurement is unavailable. Open the HUD editor and select **movement.telemetry** to move, scale, tint, hide or change its opacity. Save/use the edited HUD profile to apply it. Both editor previews, undo/redo, reset and profile export/import support it. This display never drives gameplay, packets, or replays and is hidden when movement is ineligible.

## Authoring and compatibility

Map authors must set `traversalTraining: true` and mark individual geometry with `surfSurface: true`. The authoring validator and collision packer reject tags without map opt-in. The tag survives collision optimization and authored OBJ `_surf` metadata. Native unknown collision bits never become authored surf tags on import. Tagging changes map content identity through the existing package/fingerprint path.

Protocol 68 added movement enum value 3 and modifier bit 17 (Traversal v1). Protocol 69 adds accepted crouch stance and the Traversal v2 slide profile, using reserved intent and snapshot bits without enlarging native payloads. The existing movement identities and modifier bits are unchanged. Live peers require an exact protocol match. Pre-68 replay packets cannot claim the new rule bit; passive historical playback continues to use recorded movement, not current physics. Pre-69 replay packets and checkpoint rows clear the former reserved crouch bits. Protocol-68 recordings retain their Traversal v1 identity. Current recordings restore accepted stance, collision bounds and presentation across checkpoints and seeks; passive playback does not run movement physics. Historical combat poses and retained source-time custom weapon origins include accepted stance.

## Performance

Clearance reuses one broadphase for its sphere, sweep and cylinder-edge checks, and owned input no longer repeats the same stance update before movement. Repeated pictures reuse formatted telemetry text. These bounded optimizations preserve native collision checks; passing correctness or allocation checks does not establish a whole-frame timing budget.

See the [first Traversal slice results](traversal-results.md) and [crouch/slide results](crouch-sliding-results.md) for verification scope and remaining tuning limits.

Protocol 71 selects Traversal profile v3: run-speed slide entry and a bounded,
single-use fresh-crouch landing entry. Active sliding suppresses automatic held
hops; a fresh jump still uses native jump acceptance and ends the slide. The
horizontal coefficients remain unchanged. Protocol68 recordings keep v1 and
protocols 69–70 keep v2; protocols 71–73 recordings use v3. Earlier profile classes and
pre-69 stance normalization remain immutable. Classic remains the default.

See [rocket, slide and HUD reliability results](reliability-polish-results.md) for the current verification scope.

Protocol 74 selects Traversal profile v4 with a 72-tick (1.2-second) slide and
friction 0.45. The v3 entry threshold and single-use landing window are retained.
Recorded protocols 74–75 keep v4, 71–73 keep v3, 69–70 keep v2, and 68 keeps v1.
Prior profile classes and native wire widths are unchanged.

See [slide readability and crouch gait results](slide-gait-results.md) for the held-Forward reproduction and verification scope.

Protocol 76 selects Traversal v5 and Quake III v4. Continuing eligible movement
on a fresh, static, walkable support solves acceleration/friction using surface
speed, then redirects travel along the plane. Contact redirection retains only
the velocity entering that contact, after any wall response. First airborne
landings, accepted jumps, separating blast impulses, slippery/damaging contacts,
platforms, finite edges and steep faces keep their native response. Classic and
Momentum are unchanged.

Earned Traversal slides receive a bounded gravity component along supported
slopes once per fixed tick (6 world units/second squared before projection).
Uphill slides slow gradually; downhill gravity counters friction and can build
modest speed. Flat slide coefficients and the non-refreshable 72-tick duration
are unchanged. This force never creates a slide entitlement or runs per triangle.

The dedicated slide pose blends into asymmetric extended/tucked legs and a torso
lean over six fixed ticks, then recovers over six ticks. Its cosmetic heading
follows world travel independently of aim. Native shot/ability curves and
canonical weapon facts remain authoritative. See the [ramp verification](ramp-momentum-results.md)
and [dedicated slide presentation results](dedicated-slide-results.md).

Crouch footwork now follows completed horizontal travel with a 62% support phase,
low swing lift, gradual direction changes and six-tick start/stop blending.
Fresh local walkable support aligns the step to the floor. Remote capture uses
completed playout positions and accepted velocity rather than snapshot-arrival
bursts; passive replicas capture the same cosmetic timelines. Checkpoint restore
clears cosmetic history and rebuilds it from subsequent completed steps.
See [planted crouch footwork results](planted-crouch-results.md) for measured scope.

Supported physical Traversal slides reuse the native terrain slide loop (or its
native rolling loop on floors without a slide entry), with a
bounded amount based on final horizontal speed. Walking emissions are suppressed
while sliding; native sound clocks, shared RNG draws, source ownership and teardown
remain intact. Player volume controls the existing source. See
[movement feedback results](movement-feedback-results.md) for validation scope.


Low-travel presentation now blends native lower animation into its neutral
footwork skeleton using the existing walk/slide envelope. Matching local joint
rotations interpolate before hierarchy composition, while native upper curves,
canonical/source-time poses and all animation clocks retain their original
sampling. Knee-floor correction and reach limiting fade with that same envelope
rather than switching on at the first positive weight. Full walking still keeps
its planted support intervals and floor-clearance constraints.

The swing path matches horizontal foot velocity at toe-off and contact; its low
lift starts and ends with zero vertical velocity. A small airborne follow-through
is bounded below four percent of the cycle length. Fully blended support travel,
cycle length, body speed, jump timing and collision are unchanged.

Remote accepted-speed inference enters a cosmetic glide above 105% of crouch
walking speed and retires it at walking speed. This narrow deadband avoids
walk/slide envelope chatter near the threshold; held positions invent no stride
travel. Discontinuities, invalid speed and leaving the walking level clear the
inference. Local physical slide ownership and local travel classification keep
their existing rules. See [low-travel smoothing results](consistent-low-travel-results.md).
