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

Focused tests cover policy admission, orientation, elevation, range, objective
symbols, clamping, finite input, visibility, capacity, frame reset and settings
validation. They do not establish visual readability at small resolutions or
Android device acceptance; those remain required live checks.
