# G2 audio feedback

The client consumes accepted combat and reliable world events. Feedback never
changes health, weapon behavior, visibility, score, or match state. Live sessions
and modern replays share the event path.

Hit, headshot, kill, critical health, objective pickup/drop/capture, Prime change,
overtime and match point use distinct existing sound identifiers. Confirmation
and acquisition sounds are non-positional. Major pickup respawns use the event's
fixed world position and distance attenuation. A local health, ammo, power-up,
key, or weapon acquisition uses the corresponding original acquisition sound;
duplicate-weapon versus new-weapon conversion remains a cosmetic authority
limitation. Only Double Damage, Cloak, Deathalt and Omega Cannon respawns qualify,
and only when the authority's `PickupRespawnAnnouncements` rule is enabled; its
default is false.

Feedback volume is independent and defaults to 0.7. Global sound mute still
applies. Hit/headshot cues have a four-tick minimum spacing; other generic cue
kinds have a thirty-tick minimum. Acquisition sounds deliberately have no shared
throttle, so rapid health/ammo/weapon pickups are all audible. Reliable-event
deduplication feeds a fixed-capacity 32-notice drop-oldest queue; the scene HUD
drains it once in authority order rather than relying on latest-event state.
Pending notices are transient and are cleared on match/phase/session changes and
replay seek or baseline restore; they are not part of the replay serialized format.

Modern critical-health feedback triggers below 25 health, rearms at 35, and resets
with the full connection/life identity. This replaces the old repeating local
alarm for authoritative sessions and modern replays, reducing constant tones.
Historical playback retains its existing sound behavior. Silent damage does not
produce a hit/headshot confirmation; separately accepted kill confirmation still
follows the user's marker setting.

Source and focused tests do not establish audible quality. Native desktop and
Android listening checks, mix balance and spatial audibility remain required.
