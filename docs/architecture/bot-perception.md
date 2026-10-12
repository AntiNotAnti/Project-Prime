# Bot AI Phase 2: sight memory and imperfect hearing

This stage sits above the legacy navigation/personality tree and preserves the
Phase 1 authoritative input motor. The bot does not receive new network state.

## Perception boundaries

Every bot now maintains up to eight per-slot contact records with a last **observed**
position, last observed velocity, sensory source, and a timer. Sight updates exact
position/velocity only with valid geometric visibility and cloak/radar rules.

After line of sight breaks, the last visible position may be extrapolated for at
most twelve 60-Hz simulation ticks. It then stops at that remembered position.
Gunfire from a hidden target is only audible within the difficulty's hearing
radius, recorded at most once every thirty ticks, and coarsely quantized to a
22.5-degree direction sector and six-unit distance band. Damage may also create
a directional investigation cue, but does not re-enable through-wall precision
tracking or firing.

On death, respawn, bot removal and life reset the cached contacts are cleared.
Invisible targets can be selected as investigations only when a nonexpired record
exists; choosing one never passes its current hidden transform into the new
tactical steering or combat weapons.

## Movement and combat

The bot turns toward a recalled location and may try a collision/floor-safe
investigation step, with native navigation remaining the obstacle-route fallback.
A hidden remembered target cannot be fed to the active combat aim/fire routine.
Visible targets continue through ordinary weapon, tactical footwork and combat
physics.

## Tests

```sh
dotnet run --project tools/nettest/nettest.csproj -c Release -- --bot-ai
dotnet run --project tools/nettest/nettest.csproj -c Release -- --bots-scene /path/to/game-config 'MP1 SANCTORUS' 7
```

Added pure checks for auditory position coarsening, finite memory duration and
confidence. Asset-backed acceptance must verify that bots do not track a player
around solid corners, that sight reacquisition works, and that bot vs bot
training performance does not regress.
