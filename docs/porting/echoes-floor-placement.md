# PORT-13 source-grounded spawn and pickup placement

Before the first accepted cannon playtest variant, align its starts and
pickup points to the highest walkable **source collision-only triangle**
within three MPH units underneath each feet location.

This replicates Second Hunt's short raised downward floor ray, with
up-facing normal threshold 0.7 and XZ barycentric interpolation. No
original collision vertices or visual surfaces are modified.

The original LOCAL/ VISUAL Echoes map drafts preserve all their MapIds,
their source placements and their historical behavior. Placement occurs
only after the separate CANNON ECHOES variant is cloned, before its v1
identifier is published. Visual-only meshes, ceilings, non-walkable
surfaces and remote lower floors never supply a valid landing.

The algorithm is deterministic and CPU-only. It is intentionally
bounded to a handful of spawn/pickup queries at explicit local import
time, not on the scene render thread. No raw game files are shared.
