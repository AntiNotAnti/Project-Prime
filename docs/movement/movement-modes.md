# Movement modes

**Classic is the default.** It preserves original Hunter movement. Missing old
settings and supported recordings from before Momentum select Classic; its
configuration name remains `Native`.

**Momentum** adds Source-inspired biped acceleration, air-strafing, manual
bunny-hop momentum and counter-strafing at the game's fixed 60 Hz simulation
rate. It does not add automatic jumping, buffered jumps or extra jumps. Native
gravity, jump abilities, alternate forms, Spire climbing, collision, pads,
teleports and external impulses keep their existing ownership.

**Quake III** uses a separately identified fixed profile for Quake III-style
strafe-jump speed building: full ground-speed air projection, air acceleration 1,
ground acceleration 10, ground friction 6 and no artificial total-speed ceiling.
It retains native Hunter jumps and gravity, adds contact-only held hops,
and does not reproduce every Quake Live physics detail. [Quake III guide](quake3-movement.md).

**Traversal** adds stronger air control, earned ramp lift, Magmaul self-blast jumps, crouch/power sliding,
and explicitly tagged training-map surfing. It has a separate ruleset identity
and keeps Quake III tuning unchanged. See the [Traversal guide](traversal-movement.md).

**Parkour** is a separate optional profile that inherits Traversal v5 and adds
an airborne crouch tuck, bounded wallstrafe acceleration, wallrun and wall kick.
Hold Ctrl in flight to raise the low capsule without
adding jump force or airborne velocity. Its accepted stance follows multiplayer
snapshots and replay checkpoints. For wallrun, hold Forward and strafe along a
safe vertical wall while airborne; release Jump and press it again during the run
to kick away. See the [Parkour guide](parkour-movement.md) for controls and limits.

In a lobby, the owner opens Match Rules, chooses **Movement: Classic / Momentum / Quake III / Traversal / Parkour**
and applies the draft. Guests can review the accepted selection. Pending edits
do not change the live session; stale or unauthorized updates are refused. The
server freezes the choice at match start. A local control preference cannot
override another match's movement rules.

Offline practice exposes the same choice in Match Rules and saves it with the
other practice rules. Cancel discards edits. Campaign continues to use Classic
without discarding the saved multiplayer/practice selection. All optional movement profiles require
Native Hunter tuning and cannot be combined with Balanced or Enhanced variants.

For a standalone server use `-server -movement Momentum` or `-server -movement Quake3` or `-server -movement Traversal` or `-server -movement Parkour`; use `Native` or omit
the option to retain Classic. [Server instructions](../../SERVER.md#movement-rules)
describe operator selection and rollback. Returning to Classic before the next
match needs no emergency packet-format change.

Lobby/session review and replay details display the effective movement choice.
Replay labels come from recorded rule facts, including after cache reload and
seeking. A current recording whose usable rule facts are missing is labeled
Unknown, rather than silently described as Classic. Historical archive
compatibility remains versioned; metadata labels do not enable movement on
replica bodies. Current peers use protocol 79; older recordings are converted
at the playback boundary while retaining their recorded movement semantics.
Current Quake III recordings select profile v4, Traversal recordings select profile v5, and Parkour recordings select profile v3. Quake III supports
held landing hops, circle/strafe-jump chains and self-missile rocket jumps; see the [technique guide](quake3-movement.md).
Protocol-60/61 Quake III recordings retain profile v1; 62–70 retain v2, and 71–75 retain v3.
Traversal protocol 68 recordings retain v1, 69–70 retain v2, 71–73 retain v3, and 74–75 retain v4. Protocol 76 selects the new ramp/slope profiles; protocol 77 explicitly introduces Parkour v1; protocol 78 recordings retain Parkour v2 wallstrafe, and protocol 79 selects Parkour v3 wallrun and wall kick. Native payload
widths and the protocol 69 stance boundary remain unchanged.

Momentum remains an experimental opt-in. The [v1 closeout](pr09-closeout.md)
records completed four/eight-player and impaired-combat evidence, the accepted
performance tradeoff, pending hosted CI and explicit platform/input limits.
Mac and Windows hardware are available; Windows focus recovery/achieved refresh,
macOS OS-event input, physical controllers and physical Android remain
unverified or blocked as stated there. A passing CPU or synthetic input fixture
is not a physical-device claim. Quake III is now available through normal
practice and multiplayer rules. The separate [editable JSON tuning draft](quake3-practice-tuning.md)
remains an offline experiment and cannot change a match's fixed profile.

The original implementation uses calibrated native units and an immutable v1
profile; see [tuning](tuning-v1.md) for coefficients and limits. These are
Source-inspired mechanics, not Valve Source Engine code. No external movement
implementation or new runtime dependency was imported. See the
[reference and licensing notes](reference-attribution.md) for provenance.
