# PR-09b — actual multiplayer acceptance tools

This slice commits real UDP owner/combat fixtures, bounded collision diagnostics,
and the recorded network evidence. It completes this tooling slice, not the
PR-09 release gate. Classic remains the default and production physics, damage,
collision, packet formats and match authority are unchanged.

The existing online scene fixture now supports genuine spectators, human/bot
cohorts, reconnect/slot reuse, respawn, match rebuild and different-map transitions.
Every simulation call remains 1/60 second. Deadline pacing may process fewer calls
under load; snapshot age means local frames since reception, not network RTT.
Extended jump checks use actual same-frame native `LegalJump` acceptance before
gravity/collision, with unchanged native Y limits. Bounded traces distinguish
solver velocity from later native body-position corrections. Older insufficiently
instrumented failures remain explicitly unclassified.

Combat fixtures use production AI to drive genuine human-owner controls in
independent processes. They do not grant equipment, move bodies, write readiness,
assign bot roles or directly apply damage. The verifier joins full owner shot
identities to authority certificates, claims and unique committed native damage
payments. Positive served authority rewind is required and is an aggregate,
not proof of every payment's rewind depth.

On preserved main `3de8887a8b04bd85db8b60ea14da9add15c1972a` plus the recorded
test overlay, both eight-human cohorts pass: all seven Hunters plus a second
Samus, 200 ms configured RTT / 20 ms jitter, 1% loss, 5% reordering and 1%
duplication, with independent fault seeds. All 18 client/server processes exit
0. Both full-cohort proofs pass 58 assertions; Native/Momentum yield 51/59
joined payments and 343/414 served rewinds. Every owner observes all seven
remotes; all evidence/join overflows are zero. Twenty corrupted copied-input
proofs are rejected, including incomplete and duplicated manifests. Original
input hashes are preserved. This is process-based testing, not eight devices or
a cross-model hit-registration balance estimate.

Separate four-human/four-bot baseline, impaired, lifecycle and map-change cases
pass, as do complete spectator cohorts. See [network evidence](evidence/pr09-network-draft.json)
and [combat evidence](evidence/pr09-owner-combat-draft.json) for exact source leases,
process outcomes, earlier retained failures and limitations. The staged C# subset
builds separately with zero warnings/errors. The full macOS main-91f validation
build and Python compatibility tests also pass. A Windows main-91f source/guard
and combat compatibility refresh now passes: 56 Native golden trajectories,
18,489 rewind assertions, 3,291 custom contract checks and both impaired two-owner
combat proofs (22 assertions each), all six processes exiting 0. Native/Momentum
have 13/6 joined payments and 59/55 served rewinds in that fresh91f cohort. This
does not relabel the earlier eight-human experiment as a new revision.

For an asset-backed run, each process needs its own user-data directory containing
only the operator's `paths.txt`. Set `PROJECT_PRIME_USER_DATA` accordingly and use
a distinct `PRIME_NET2_EVIDENCE` output per server/client. The recorded
eight-owner campaign sets `PRIME_NET2_EVIDENCE_ROW_LIMIT=262144`. Start
`--momentum-online-server <assets> <port> <model> <stop-file> 8 continuous 120`,
wait for actual authority readiness, then start the required independent
`--momentum-combat-online` workers; the CLI help lists duration, Hunter and fault
arguments. Wait for every worker's exit code, create the server stop file, and
allow up to 60 seconds for orderly evidence flush. Use `--momentum-combat-proof`
for two reports or `--momentum-combat-proof-many` with a JSON array of all eight
report paths. A positive worker activity report alone is not combat acceptance.

The strict integrated performance budget still fails; hosted CI is queued and
device/input gaps remain. Latest-main replay packet-index compatibility is a
separate repair. No result in this PR closes those release gates or asserts
unmeasured physical-input, Internet, sustained-load or all-mode compatibility.
