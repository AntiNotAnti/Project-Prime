# Echoes cannon bot routes (experimental)

Ported the twelve authored landing coordinates from Second Hunt at
`c16a907b3658c2fd4079e394a73711596a85caee`. The new `ROUTED ECHOES` local-only tier preserves
original LOCAL/VISUAL/CANNON map IDs and gameplay contracts.

Each source cannon is linked one-way from a pad trigger to its grounded
arrival. Both endpoints must have walkable **source collision** within
a bounded floor snap. A visual-only mesh or ungrounded landing is not
accepted. At install time, Project Prime's own node compiler must accept
every authored route before the private JSON becomes visible.

Run `ProjectPrime -importechoesroutes /path/to/Echoes.iso` to install
the four routed playtests. They remain **offline solo Battle only**.
Actual bot traversal, cannon landing, real media and hardware
performance acceptance are still needed; these routes do not grant
extra matchmaking privileges or distribute any commercial assets.
