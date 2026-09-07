# Enhanced radar

Classic locator rendering remains the default. Enhanced radar projects a bounded
prepared frame of the same mode-approved contacts into heading-relative or
north-up coordinates. It draws enemy circles, objective letters and Prime Hunter
markers, elevation arrows and clamped-range dots. The contact model also supports
team diamonds and stale fading; this release does not invent team or last-known
reveal rules that the authority has not granted.

`ProcessModeHud` starts each frame and the existing legal locator sink admits
contacts. Rendering reads that frame and never searches world entities for more
information. Survival reveal flags now come from authoritative snapshots instead
of replica hiding timers. Hidden/zero-alpha contacts are omitted, and a fresh
frame cannot retain a previous reveal. The fixed capacity is 64; overflow is
counted and never grows storage. Range is a display scale, not a detection rule.

Seven pure projection/frame tests pass, and the Client Release build passes.
These checks cover orientation, elevation, clamping, finite input, visibility,
capacity and frame reset. They do not establish visual readability at small
resolutions or Android device acceptance; those remain required live checks.
