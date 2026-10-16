# PORT-13: tuned Echoes kinetic orb-cannon playtests

The upstream source repository **BountyHunterKanden/SecondHunt** at
`c16a907b3658c2fd4079e394a73711596a85caee` contains human-tuned kinetic orb cannon launch positions and
velocities in its six arena JSON recipes. This slice ports the twelve
non-dropped pads: four Sidehopper Station, three Spires, two Spider Complex,
and three Shooting Gallery. Crossfire Chaos and Pipeline contain none.

`ProjectPrime -importechoescannons "/path/to/owned/Echoes.iso"`

Creates four *additional* local-only `CANNON ECHOES ...` arenas using the
existing visual source geometry and unchanged authoritative MREA collision.
Their distinct deterministic MapIds deliberately preserve the original
`LOCAL ECHOES ...` and `VISUAL ECHOES ...` map/replay identities.

Launch vectors, positions, lock times and native map-pad behavior reuse
Project Prime's existing MapJumpPad compilation and simulation. A short
cooldown prevents continuous retriggering. These are experimental:
original source cannon target arrivals have not been verified with the
Project Prime physics build, especially alternate forms. Full physics
acceptance, real-game-media validation, Android testing and 8-player
matchmaking are still pending.

Single-player Battle only. Extracted source geometry is not transferred
or packaged in Community maps. This is not the complete playable Echoes
mechanics port: breakables, water, Gravity Boost, native assets, and
dynamic geometry remain future work.
