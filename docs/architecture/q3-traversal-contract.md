# Imported Quake traversal

New Q3 imports use source/project and portable package format 3. Formats 1 and 2
continue to compile imported push brushes as native Prime jump pads. A missing
`q3Triggers` collection selects that legacy path; it is not silently upgraded.
Native authored jump pads and native teleporter entities retain their behavior.
Old builds reject format 3 instead of silently applying incompatible mechanics.
There is no network protocol version change.

`q3Triggers` stores typed push/teleport bounds, feet destinations, source entity
indices, destination yaw, source gravity converted to Prime world units, and
cooldown measured in fixed 60 Hz steps. Quake target names are case sensitive;
the first usable matching entity in source order resolves duplicate names on
every machine. Invalid bounds, nonfinite coordinates, nonpositive push apexes,
and unsupported indices are rejected before a runtime trigger is authored.
Scripted `target_teleporter` activation remains unsupported and is diagnosed.

Push velocity is solved to the source target apex with source gravity, then
converted from world units per second to Prime's 30 Hz speed representation.
The native movement kernel integrates at 60 Hz. Q3 flight retains this gravity
through form changes, avoids native pad control locks and normal horizontal
drag/capping, and ends on landing, death/respawn, room transition, a subsequent
boost/alt attack, a native jump pad/teleporter transition, or the bounded flight timeout. Contact uses feet (the bottom
of the morph sphere for alt form) and a swept segment to catch thin triggers.
A pad cancels existing boost acceleration before assigning its launch.

Teleport destinations are imported feet positions with the source 24-unit
origin offset removed. Alt body origins retain their form's feet offset.
Native collision penetration and kill-height checks refuse unsafe arrivals.
Repositioning applies destination yaw, clears momentum and old launch locks,
resets sweep origins, and fences all destination gates for the fixed cooldown.
Trigger metadata is cloned and bound to the loaded scene; catalog refresh does
not change an active scene's traversal definitions.

The network architecture keeps same-life movement with its owner. Q3 uses
that existing contract rather than introducing server corrections that would
be overwritten by the next owner position report. The authority observes
accepted owner reports for source contact/destination proof, propagates the
existing jump-pad audio sequence, and supplies movement-shadow exemptions.
This observation is advisory; it does not make owner movement cheat proof or
introduce authoritative teleport acknowledgements.

Replay world capsules use version 5's bounded traversal appendix for launch
gravity/time, trigger cooldowns and the last observed owner-report frame.
The gameplay graph field contract/fingerprint is unchanged. Capsule versions
1–4 remain readable and initialize traversal state through the old default
path. Passive replay/spectator positions continue to follow the authoritative
pose stream rather than re-running imported trigger contact.

Regression commands:

- `map-editor-check`: typed extraction, source-order pairing, malformed input,
  project/package round trips, and explicit legacy native-pad fallback.
- `nettest --q3-traversal-scene <assets> [room]`: real native player movement
  and form volumes for seven biped/alt hunters over 60 fixed steps, swept feet,
  boost interruption, teleport orientation/cooldowns/reset, and traversal
  appendix state restoration. This fixture exercises the movement kernel;
  it does not claim end-to-end network or full replay-seek coverage.
