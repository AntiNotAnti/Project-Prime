# PR-05: authoritative movement model and protocol 59

Native remains the default for absent preferences, new matches, dedicated servers
and campaign play. Momentum is an explicit match rule bound to the immutable v1
profile. Offline launch can select `MenuSettings.MovementModel = "Momentum"`;
dedicated startup accepts `-movement Native|Momentum`. Unknown names and enum
values fail before configuration mutation. Public lobby controls follow in
PR-08; existing editors preserve the selected rule while changing other fields.

## Wire and ownership contract

The implementation-time main uses protocol 58 and reserves bit 14 for the
rendering-only third-person camera. Momentum therefore uses bit 15 (`32768`),
which fits both the session's `uint` modifier field and MatchState's `ushort`.
Session decoding accepts the low sixteen defined bits and refuses bit 16 or
higher. Live protocol increases from 58 to 59 because simulation semantics have
changed. Packet widths, nine-byte intent reports, compact intent semantics,
replay checkpoint schema and event schema remain unchanged.

Only the authenticated lobby owner can update the mode, before start and against
the current revision. Update invalidates readiness; start validates and freezes
the complete definition. Starting/InMatch updates are refused. Client adoption
also refuses a contradictory model for the same frozen epoch/match, including
MatchState arriving before the first session definition. Mutable lobby drafts
remain editable. A new match/epoch follows the existing reset policy; dedicated
rotation derives its next definition from the server's configured model.

The scene receives the authoritative definition before loading players. Local
online preferences do not override it. Momentum runs on eligible owning human
bipeds, or authoritative bots; remote human puppets never run the local solver.
The existing owner-reported-position architecture, native vertical/ability and
collision processing, fixed 60 Hz steps and readiness gate remain authoritative.
No prediction, reconciliation, movement ACK or new per-frame packet is added.
Existing movement shadow validation remains advisory; this is not a new
server-side anti-cheat or server resimulation implementation.

Balanced/retired Enhanced abilities cannot be combined with Momentum. Native
combinations retain their existing validation. Replay/replica/side scenes,
campaign, native special-state exclusions and unavailable local input remain
outside local biped simulation. Replay playback support is audited in PR-06.

## Historical compatibility

The explicit archival protocol allowlists now include 59 alongside 56/57/58;
unknown future 60 remains refused. Historical recordings without the new bit
resolve to Native. Protocols before 59 cannot smuggle formerly reserved bit 15
through MatchState, MapChange, session conversion or checkpoint adapters: those
records are refused rather than silently selecting Momentum. Protocol 58 keeps
its independent third-person camera semantics. The existing
`--protocol58-replay` command name is retained as a compatible test alias.

## Executed validation

Base: merged PR-04 on main `d726c766a10affaa0d8f95bc25bce9d178528215`.
Host: macOS 27 ARM64, Apple M4 Pro, SDK 10.0.401/runtime 10.0.12.
The managed harness builds with zero warnings/errors. Numeric scene evidence is
in `evidence/pr05-match-scenes.json` and `evidence/pr05-online-scenes.json`.
Test-local data directories copy only the existing extraction's `paths.txt`;
independent processes use separate `PROJECT_PRIME_USER_DATA` directories.
No extracted game data, account state or runtime caches are committed.

| Check | Result |
| --- | --- |
| Managed harness / dedicated-server builds | Both passed, zero warnings/errors |
| Two-mode rules and production command/adoption paths | 146 assertions; authorization, revision, both packet-order freeze guards, unknown values and uint/ushort high-bit round trips |
| Saved/offline/editor rule preservation | 29 assertions passed |
| Actual offline rule selection, all seven Hunters | 10,223 assertions; rule trajectory/flags match the private v1 path; Native/default run zero solver steps |
| Final Native UDP clients | 6,103 / 6,028 assertions; slots 0/1; zero solver steps; 538 / 606 moving remote samples |
| Final Momentum UDP clients | 7,517 / 7,462 assertions; slots 0/1; 681 / 676 solver steps; 584 / 615 moving remote samples |
| Reviewed archive/protocol fixtures | 204 assertions; protocol 59 and future refusal covered |
| Timed-shot / pickup / grant archives | 4,668 / 83 / 51 assertions passed |
| Lifecycle admission | 3,735 assertions, including real UDP protocol-58 refusal by the protocol-59 authority |
| Complete lobby lifecycle | 7,752 assertions, including mixed-protocol refusal without allocating a peer or slot |
| Native golden movement | All 56 unchanged against pinned `dc7f097` data, tolerance `1e-5` |
| PR-04 collision / specials / abilities / carrier | 378,667 / 98,556 / 14,442 / 1,051 assertions passed |
| Offline / jump regressions | 57,346 / 187,405 assertions, unchanged counts |
| Equations / profile / admission | 104 / 20,046 / 21 assertions; profile zero allocations over 100,000 warmed steps |
| Warmed scalar benchmark | 819,200 player steps, zero allocations; shared-host timing is diagnostic, not whole-scene acceptance |
| Other focused network compatibility | Architecture, compact intent, continuous targets, protocol17, advanced rules, pickups, names/chat and Enhanced retirement passed |
| Portable browser/party source-linked fixture | 107 assertions passed against the current production DLL |

The UDP workers connect through actual Hello/Welcome, roster, Identify,
SessionState, MatchState, loaded-world bootstrap and readiness. They create real
CPU scenes and issue keyboard input. They check local ownership, non-simulation
of remote puppets, native Y-bit preservation at the solver seam, finite state,
legal jumping/landing, immutable authoritative identity and model, actual remote
movement and no disconnect. The second client joins after the first; no test
sets a fake role, playback admission, ready flag or remote snapshot. The server
does not advertise through a listing reporter or launch hosted children.

Both modes passed once before and again after the final packet-ordering guard;
both trials are retained in the numeric evidence. The final trials ran both
servers and all four clients concurrently with compilation on the shared host.
Each simulation step remains fixed at 1/60 second; wall-clock scheduling produced
different total tick counts across clients. This is not render-cadence or
real-time performance acceptance.

Representative reproduction after building `tools/nettest/nettest.csproj -c
Release`:

```sh
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --momentum-rules
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --momentum-offline-rules
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --momentum-match-scene "$ASSET_CONFIG" match-scenes.json
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --protocol58-replay
```

For each of Native and Momentum, start `--momentum-test-server "$SERVER_DATA"
PORT MODEL` and two separate `--momentum-online-scene "$CLIENT_DATA" PORT MODEL
output.json 12` processes, with unique user-data directories and a free local UDP
port. Start client two after client one's admission. The test server exits after
45 seconds. Each client writes machine-readable evidence and exits nonzero on
failure. This is an asset-backed local acceptance recipe, not an asset-free CI
claim.

## CI and remaining work

The three-OS Momentum workflow now runs the asset-free two-mode and saved/offline
rule suites as well as equations, retention and allocations. Archival CI runs
the expanded protocol fixtures. Publication and merge do not wait for CI or
review, as requested; no all-green CI result is claimed here. PR-04's completed
movement/replay jobs passed. Its broader build job failed in an unrelated
source-linked community fixture and campaign-ending native-GL audio assertion;
those failures are being repaired separately rather than weakening movement
checks.

PR-06 through PR-09 remain mandatory: deterministic replay/seek/rewind evidence,
bot and device/input parity, complete lobby editing, latency/hit acceptance,
whole-scene performance and release gates. Local two-client UDP success does not
prove WAN, loss/jitter, device, renderer, bot or hit-registration acceptance.
The feature is not complete and Native remains the default.
