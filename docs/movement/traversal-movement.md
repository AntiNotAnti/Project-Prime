# Traversal movement

Select **Traversal** in Practice or the multiplayer host's Match Rules → Movement. The host admits one shared ruleset; joining clients cannot override it. **Classic remains the default.** Quake III retains its existing profile and strafe-jumping behavior.

Traversal is an original optional ruleset, not a claim of exact Quake physics. It uses the fixed 60 Hz player simulation, native Hunter jumps, gravity, collision, damage, abilities and alternate forms. Native Hunter variants are required; Balanced and retired Enhanced rules cannot be combined with it.

## Techniques

- **Air control:** air acceleration is 2 per second and steering turns the horizontal velocity toward wish input at up to 1.8 radians per second. Steering preserves horizontal speed. Normal directional acceleration can add speed, brake, or build strafe-jump speed; it does not impose a total-speed cap.
- **Held hops:** hold Jump to jump again on a new physical landing. Holding never spends an airborne Space Jump. A fresh press retains native ability behavior.
- **Ramp jumps:** jump while running uphill. A recent walkable collision can retain its positive slope ascent in addition to the native jump. Flat ground, downhill movement, stale contacts, moving platforms and airborne grace flags cannot provide ramp lift. Lift is bounded by incoming horizontal speed.
- **Explosive jumps:** aim a missile or charged Magmaul splash near the feet. Native self-damage, immunity, line of sight, radius and death still apply. Additive three-dimensional impulse uses the same bounded calculation as Quake III missile jumps. The authority derives unpaid self-hit outcomes from its own explosion witness; it never trusts the claimed blast origin or push. Magmaul behavior in Classic and Quake III is unchanged.
- **Training-map surf:** use the tagged lane in **TRAVERSAL TRAINING**. Tagged sloped contacts retain airborne steering and gravity and clip inward velocity without ordinary slope speed loss. They do not count as landings or grant hops. Walls, ceilings, moving entities, untagged slopes and ordinary maps retain native collision behavior.

The training map has an uphill ramp at X=-30, a tagged surf lane at X=-10, an identical untagged control at X=10, and a low tunnel at X=30. Geometry and the map recipe are original; generated assets stay in the user's extracted-data directory.

## Tuning display

Optional movement modes show horizontal speed in world units/second and airborne strafe efficiency. Speed comes from the body after collision. Efficiency compares the horizontal speed-squared gain that survived that tick with the best directional acceleration gain available at the same speed and input magnitude. `--` means the measurement is unavailable. This display never drives gameplay, packets, or replays and is hidden when movement is ineligible.

## Authoring and compatibility

Map authors must set `traversalTraining: true` and mark individual geometry with `surfSurface: true`. The authoring validator and collision packer reject tags without map opt-in. The tag survives collision optimization and authored OBJ `_surf` metadata. Native unknown collision bits never become authored surf tags on import. Tagging changes map content identity through the existing package/fingerprint path.

Protocol 68 adds movement enum value 3 and modifier bit 17. The existing Momentum and Quake III identities and profiles are unchanged. Live peers require an exact protocol match. Pre-68 replay packets cannot claim the new rule bit; passive historical playback continues to use recorded movement, not current physics. Snapshot and session widths are unchanged in this slice.

## Remaining slice

Crouch/power sliding requires an accepted stance across movement clearance, visible Hunter pose, combat hitboxes, source-time weapon origins and replay snapshots. It is tracked separately so this slice does not advertise an unimplemented crouch action. Performance optimization remains deferred as requested; passing correctness checks does not establish a frame-time budget.
