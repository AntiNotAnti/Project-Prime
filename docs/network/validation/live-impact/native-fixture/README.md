# Authority-issued native weapon fixtures

The opt-in `-hitrigloadout WEAPON` is accepted only with a dedicated, unlisted
server and issues inventory only to loopback peers. Existing native ammo spending,
cadence, FireEvent admission and historical hit proof remain active. Ordinary
servers have no fixture. Omit the flag to roll back.

`--fixture-loadout --impacts --require-combat` now requires a server authority fact
for the intended weapon, not just a trigger or a hit from a default Power Beam.
`-netchecklobbyplayers N` explicitly readies scripted clients and lets the owner
request StartMatch through normal revision/map/start-barrier checks. It has a
90-second deadline. Custom maps require a validated immutable package. Lobby
teardown exports opt-in diagnostics before clearing the match, as continuous
server teardown already did.

The final arena campaign passes 4/4 native UDP arms: 20 seconds, two players,
Imperialist and Shock Coil at 0 RTT/no loss and 250 ms ±40 ms RTT/2% loss,
with 2% reorder and 1% duplicates in the impaired arms. Authority facts name the
intended weapons: Imperialist 4/3; Shock Coil 188/113. Full source/runtime hashes,
commands, raw logs, diagnostics and joins are in `arena/`. Builds passed with
124 existing warnings, zero errors; early-settlement/fixture contracts passed
574 assertions. This is headless simulation and delivery evidence, not rendered
acceptance. CI review is skipped at the user's request.

Retained failures: `stock-pilot/` has one 0-hit sniper arm where stock-map walls
blocked shots; 3/4 arms passed. `arena-missing-export/` exercised actual combat but
all arms correctly failed the intended-weapon evidence gate because persistent
lobby teardown cleared server diagnostics before export. The final run includes
the teardown repair. No failed arm is counted as passed.

Reproduce with `tools/hitrig/run-networking-slices.py --map "TEST ARENA" --mapdir
/path/to/packages --modes sniper,shockcoil --profiles rtt0-loss0,rtt250-loss2
--seconds 20 --players 2 --impacts --fixture-loadout --require-combat
--server-scratch --claim-mode enabled --jitter-ms 40`, plus runtime/data/output
paths. Build the repository arena recipe with `-mapbundle "TEST ARENA"` and use
its resulting `.ppmap` in an isolated package directory. No extracted game assets
or screenshots are included in this evidence directory.
