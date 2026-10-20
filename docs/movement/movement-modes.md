# Classic and Momentum movement

**Classic is the default.** It preserves original Hunter movement. Missing old
settings and supported recordings from before Momentum select Classic; its
configuration name remains `Native`.

**Momentum** adds Source-inspired biped acceleration, air-strafing, manual
bunny-hop momentum and counter-strafing at the game's fixed 60 Hz simulation
rate. It does not add automatic jumping, buffered jumps or extra jumps. Native
gravity, jump abilities, alternate forms, Spire climbing, collision, pads,
teleports and external impulses keep their existing ownership.

In a lobby, the owner opens Match Rules, chooses **Movement: Classic / Momentum**
and applies the draft. Guests can review the accepted selection. Pending edits
do not change the live session; stale or unauthorized updates are refused. The
server freezes the choice at match start. A local control preference cannot
override another match's movement rules.

Offline practice exposes the same choice in Match Rules and saves it with the
other practice rules. Cancel discards edits. Campaign continues to use Classic
without discarding the saved multiplayer/practice selection. Momentum requires
Native Hunter tuning and cannot be combined with Balanced or Enhanced variants.

For a standalone server use `-server -movement Momentum`; use `Native` or omit
the option to retain Classic. [Server instructions](../../SERVER.md#movement-rules)
describe operator selection and rollback. Returning to Classic before the next
match needs no emergency packet-format change.

Lobby/session review and replay details display the effective movement choice.
Replay labels come from recorded rule facts, including after cache reload and
seeking. A current recording whose usable rule facts are missing is labeled
Unknown, rather than silently described as Classic. Historical archive
compatibility remains versioned; metadata labels do not enable movement on
replica bodies or change the recording format.

Momentum remains an experimental opt-in. The [v1 closeout](pr09-closeout.md)
records completed four/eight-player and impaired-combat evidence, the accepted
performance tradeoff, pending hosted CI and explicit platform/input limits.
Mac and Windows hardware are available; Windows focus recovery/achieved refresh,
macOS OS-event input, physical controllers and physical Android remain
unverified or blocked as stated there. A passing CPU or synthetic input fixture
is not a physical-device claim. A separate [Quake III strafe-jump tuning draft](quake3-practice-tuning.md)
is available through `-momentumq3dev`; it is offline-only and preserves recorded v1 behavior.

The original implementation uses calibrated native units and an immutable v1
profile; see [tuning](tuning-v1.md) for coefficients and limits. These are
Source-inspired mechanics, not Valve Source Engine code. No external movement
implementation or new runtime dependency was imported. See the
[reference and licensing notes](reference-attribution.md) for provenance.
