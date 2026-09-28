# Advanced match rules

Insta-Gib is now a modifier of the base game mode. The base mode retains its
objective handler, score target, lives and team topology. Low Tier restricts
hunter selection to Kanden, Spire, Noxus and Weavel, with Kanden as the fallback.
No Imp replaces Imperialist resources and prevents acquiring/equipping it,
including through Trace affinity, remote weapon selection and damage claims.
Insta-Gib and No Imp are mutually exclusive. Shadow Freeze and three-second
Spawn Protection default off; explicit saved opt-ins are preserved.

## Authority and persistence

Protocol 28 is the next unused version in this checkout; the original plan's
25 was already assigned to custom-map identity. SessionRules uses the existing
ushort: 1024 = Insta-Gib, 2048 = Low Tier, 4096 = No Imp. SessionState and
MatchState sizes are unchanged. MatchState bits 3 and 7 positively enable
Shadow Freeze and Spawn Protection. Status replies append a ushort rules mask.
The existing SessionState load gate supplies all modifiers before gameplay.

Desktop and Android offline launches carry an immutable MatchDefinition rule
snapshot. Online scenes use the authoritative definition before constructing
resources. Lobby edits sanitize existing humans and bots; Identify and bot
commands cannot bypass Low Tier. Rule bits accompany career result reports.

No Imp uses fixed FNV-1a over the canonical room name and authored entity ID.
Layouts are deliberately stable across rounds on a map. Dynamic drops have no
shared authored ID and use a room-level fallback; they never hash replicated
positions or consume simulation RNG. Trace affinity uses its stable player slot.
The five replacements are Volt Driver, Battlehammer, Judicator, Magmaul and
Shock Coil.

Supported historical replay packets invert the old negative flags only at the
playback boundary. Legacy InstaGib mode data normalizes to Battle plus Insta-Gib.
Session/decoder checkpoints retain all modifiers. World restore reapplies these
values from its decoder component without altering the restored clock. The world
field contract stays unchanged, so adding rules does not invalidate historical
capsules. Historical MatchState flags are adapted inside decoder checkpoints as
well as in replay packet streams.

## Validation

- Desktop and dedicated-server builds.
- `tools/nettest --advanced-rules`: default values, protocol round trips, legacy
  conversion, supported modes, 4,096 random hunter choices, deterministic item
  transformations, decoder checkpoint round trips, real UDP Identify/bot
  enforcement, conflict rejection and late joining.
- `tools/nettest --advanced-rules-scene <data directory>`: real assets in all 12
  base modes, three Imperialist-only respawns per mode, 99 UA, infinite ammo,
  pickup/remote-selection rejection, base objective-handler selection, Low Tier
  construction, No Imp map resources, drops and Trace affinity across lives.
- `tools/nettest --lobby` and `--architecture`.
- `ProjectPrime -replayformatcheck` and `-primeuicheck`, including modifier
  controls, controller activation and mutual exclusion.

The asset-backed checks run headlessly; they do not replace human multiplayer
playtesting, a rendered replay/killcam acceptance matrix or Android device runs.
Android build verification on this machine is blocked by the missing Android
SDK directory (XA5300), despite an installed .NET Android workload.
