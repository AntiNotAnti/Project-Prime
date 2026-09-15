# Enhanced radar

Classic locator rendering remains available. Enhanced radar projects a bounded
prepared frame of the same mode-approved contacts into heading-relative or
north-up coordinates. It draws enemy circles, objective letters and Prime Hunter
markers, elevation arrows and clamped-range dots. The contact model also supports
team diamonds and stale fading; this release does not invent team or last-known
reveal rules that the authority has not granted.

The HUD settings expose style, orientation, anchor, scale, offsets, display range,
opacity and optional elevation markers. Range only changes the projection and map
crop of contacts already approved by match policy; it never expands detection.
Objective markers retain distinct flag, base, node and defender labels. Survival
also rejects inactive replicas before applying its authoritative reveal/pulse rules,
so a disconnected slot cannot leave a stale marker behind.

`ProcessModeHud` starts each frame and the existing legal locator sink admits
contacts. Rendering reads that frame and never searches world entities for more
information. Survival reveal flags now come from authoritative snapshots instead
of replica hiding timers. Hidden/zero-alpha contacts are omitted, and a fresh
frame cannot retain a previous reveal. Viewer poses and contacts are checked for
finite coordinates. The fixed capacity is 64; overflow is
counted and never grows storage. Range is a display scale, not a detection rule.

Enhanced player origins use the transformed active collision-volume center. This
keeps biped and alternate-form markers horizontally stable and avoids adding a
second, presentation-only elevation cue during morphing. Classic locator
positions remain screen-space positions and are kept separate from the radar
anchor.

Resource contacts are authority-controlled by `MatchRules.ResourceRadarPolicy`:
disabled, active spawn locations, live resources, or live resources plus active
missing spawners with authoritative respawn ticks. Unknown item types fail
closed, live item/spawner duplicates are collapsed, and the resource category
remains below players and objectives in the bounded frame. Profile flags can
hide resource categories but cannot grant contacts that the match policy did
not authorize. Resource profile fields are serialized with the existing radar
profile and older profiles retain their defaults.

Room radar geometry is invalidated by an explicit room identity/revision rather
than a per-frame collision-list scan. Geometry is rebuilt only after a room
mutation or revision change. Prepared CPU geometry is shared through a bounded
identity/fingerprint cache; GPU textures remain presentation/device scoped.

Focused tests cover policy admission, orientation, elevation, range, objective
symbols, clamping, finite input, visibility, capacity, frame reset and settings
validation, transformed anchors for all playable hunters, resource policy and
profile behavior, wire round trips, and room revision invalidation. They do not
establish visual readability at the supported resolution/preset matrix, Android
device acceptance, or live client/scene-authority behavior; those remain
required external physical and live visual gates.
