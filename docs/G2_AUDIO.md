# G2 audio feedback

The client consumes accepted combat and reliable world events. Feedback never
changes health, weapon behavior, visibility, score, or match state. Live sessions
and modern demos share the event path.

Hit, headshot, kill, critical health, objective pickup/drop/capture, Prime change,
overtime and match point use distinct existing sound identifiers. Confirmation
and match announcements are non-positional. Major pickup respawns use the event's
fixed world position and distance attenuation. Only Double Damage, Cloak,
Deathalt and Omega Cannon qualify, and only when the authority's
`PickupRespawnAnnouncements` rule is enabled; its default is false.

Feedback volume is independent and defaults to 0.7. Global sound mute still
applies. Hit/headshot cues have a four-tick minimum spacing; other cue kinds have
a thirty-tick minimum. Reliable-event deduplication and presentation sequence
tracking prevent repeated draw frames from replaying a notification.

Modern critical-health feedback triggers below 25 health, rearms at 35, and resets
with the full connection/life identity. This replaces the old repeating local
alarm for authoritative sessions and modern demos, reducing constant tones.
Historical playback retains its existing sound behavior. Silent damage does not
produce a hit/headshot confirmation; separately accepted kill confirmation still
follows the user's marker setting.

Source and focused tests do not establish audible quality. Native desktop and
Android listening checks, mix balance and spatial audibility remain required.
