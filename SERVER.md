# Running a Prime Hunters server

Prime Hunters online matches use authoritative wire family 2, protocol 8. The
server owns one headless simulation: movement, combat, pickups, objectives,
score and match transitions. Clients send input and receive authoritative
snapshots, world updates and reliable gameplay events; the local player is a
normal client too. The simulation runs at 60 Hz, publishes player snapshots at
30 Hz and complete world state at 5 Hz.

The server waits for eligible players, runs a shared three-second countdown, then
starts the match clock. Players cannot move, shoot or collect items while waiting
or counting down. Countdown start resets players and scores against the unchanged
initial world. Losing the required players during countdown returns the match to
waiting. After completion, the server owns three seconds of ending presentation
and five seconds of intermission before rotating. Full rules arrive before room
loading; client settings cannot override them. Team assignments come from the server.

The server directory is only for discovery and optional hosted matches. It does
not relay gameplay packets. A directory-only instance needs no game files, but
every match server needs content from the operator's own cartridge dump.
Release packages contain no cartridge data.

## Build the server package

The server build omits the launcher, UI toolkit and audio dependencies. Build a
self-contained package with .NET 10:

```bash
dotnet publish src/Server/Server.csproj -c Release -r linux-x64 \
  --self-contained true -p:PublishSingleFile=true \
  -o publish/linux-x64-server
```

Use `linux-arm64` for a Raspberry Pi and `win-x64` for Windows. The server
executable is `FruityPrimeServer` on Unix and `FruityPrimeServer.exe` on Windows.
The server is a separate project and references only Game; it has no launcher,
rendering or audio dependencies.

For a development checkout, the equivalent is:

```bash
dotnet run --project src/Server/Server.csproj -c Release \
  -- -server -data /path/to/files/AMHE1
```

For a local Windows one-click launcher, run `Start-FruityPrime.cmd` from the
repository root. It offers **Game**, **Dedicated server** and **Server
directory** in a menu, and accepts the same choices without a menu:

```powershell
.\Start-FruityPrime.cmd game
.\Start-FruityPrime.cmd server
.\Start-FruityPrime.cmd directory
```

The script assumes `AMHE1` is beside it, passes that directory to the server,
and creates the game's `paths.txt` automatically when needed. It uses a
published executable when one is present; otherwise it builds a local copy
under `.fruity-launcher/`. The server starts unlisted on UDP 27888, while the
directory starts discovery-only on UDP 27889. Enable hosted matches with, for
example, `powershell -File .\Start-FruityPrime.ps1 directory -HostPorts
27900-27919 -PublicAddress games.example.com`.

Client builds and publishes include their standalone server in `server/`. The
Host action starts that child executable. `FRUITY_SERVER_PATH` can select a
specific server apphost or DLL; it does not change the network protocol. A
standalone server download can also run independently.

The repository's `global.json` targets .NET SDK 10. Install .NET 10 for local
development and release builds; a published binary beside the launcher avoids
the build step entirely.

## Game content

The **game data directory** is the extracted version folder, for example
`files/AMHE1`, containing `_bin/arm9.bin`, `models/` and `levels/`. An `.nds`
file, or the directory that merely contains the `.nds`, is not server content.

An extracted directory can be supplied directly. The server accepts the
supported extracted revisions `AMHE0`, `AMHE1`, `AMHP0`, `AMHP1`, `AMHJ0`,
`AMHJ1` and `AMHK0`:

```bash
./FruityPrimeServer -server -data /srv/fruity-content -dataversion AMHE1
```

Content baking belongs to Tools. Build it with `dotnet build src/Tools/Tools.csproj -c Release`,
or use `dotnet run --project src/Tools --` before the same arguments. For a smaller
headless package, bake the dependencies into a new directory:

```bash
./FruityPrimeTools -servercontent /srv/fruity-content-amhe1 \
  -data /path/to/files/AMHE1 -dataversion AMHE1 -allrooms
```

The baker currently supports only the USA revision 1 (`AMHE1`). Use repeated
`-room "ROOM KEY"` arguments instead of `-allrooms` to select rooms. The bake
records supported room/mode combinations and SHA-256 file hashes in
`server-content.json`, validates the package before use, and never changes the
source extraction. Keep the output outside the repository; neither binaries
nor `deploy-server.sh` transfer cartridge assets. See
[`docs/NETWORK_SERVER_CONTENT.md`](docs/NETWORK_SERVER_CONTENT.md) for the
package contract and coverage rules.

## Start an authoritative match server

`-server`, `-authoritative-server` and `-dedicated` select the same authoritative server.
The room argument is optional; without a rotation file the default is
`MP1 SANCTORUS`, Battle, with a ten-minute limit and no point-goal limit.

```bash
# Linux: extracted directory or baked package
./FruityPrimeServer -server "MP1 SANCTORUS" \
  -data /srv/fruity-content -dataversion AMHE1 \
  -port 27888 -players 8 -servername "My server"

# Windows: use the console server binary
FruityPrimeServer.exe -server "MP1 SANCTORUS" \
  -data "C:\\FruityPrime\\content" -dataversion AMHE1 \
  -port 27888 -players 8 -servername "My server"
```

The process prints a line such as
`[server] listening on UDP 27888` when it is ready. Ctrl+C and SIGTERM stop
the simulation cleanly. With `-parent-stdin`, closing the parent stdin also
stops it. A server must have exactly one simulation process; run separate
processes for separate matches.

| Option | Meaning |
|---|---|
| `-data DIRECTORY` | Required extracted directory or validated server-content package. |
| `-dataversion VERSION` | Content revision; defaults to `AMHE1`. |
| `-port N` | UDP match port; default `27888`; `0` asks the OS for an ephemeral port. |
| `-players N` | Capacity from 2 through 8; default `8`. |
| `-mode MODE` | Mode for the single default match when no rotation file is supplied. |
| `-servername "NAME"` | Name shown by discovery; `-name` is an alias. |
| `-rotation FILE` | Map/mode/time/score/objective cycle. The file is read at startup; it is not created automatically. |
| `-friendlyfire` / `-friendlyfire false` | Enable or disable team damage. |
| `-spawnpolicy classic\|enhanced\|duel` | Spawn selection policy; default `classic` preserves retail selection. |
| `-cancelspawnprotection true\|false` | End protection on an accepted offensive action; default `false`. |
| `-overtime disabled\|mode` | Optional mode-specific overtime; default `disabled`. Overtime remains in the Playing phase. |
| `-latejoin immediate\|next\|disabled` | Admission policy; without an override Survival waits until the next match and other current modes admit immediately. |
| `-master HOST:PORT` | Opt in to directory listing. `-masterport N` can supply the port separately. |
| `-nomaster` | Disable listing, even if `-master` is present. |
| `-mapdir DIRECTORY` | Use an external custom-map directory. |
| `-nolagcomp` | Disable all historical shot compensation for this server, including projectile catch-up. |
| `-noprojectilecatchup` | Disable projectile fast-forward while retaining historical Imperialist traces. |
| `-autoupdate` | Opt in to staged, idle-only updates from the explicitly configured authoritative fork. |
| `-update-repository OWNER/REPO` | Trusted authoritative release repository; required with `-autoupdate`. Relay upstream is refused. |
| `-noupdate` | Disable automatic updating, including when `-autoupdate` is present. Child servers always use this. |
| `-parent-stdin` | Stop when the supervising parent closes stdin; used by directory-owned child servers. |

Historical compensation is enabled by default and remains capped at 15 ticks
(250 ms), using 32 stored history frames. These server-owned settings persist
across map rotation. The diagnostic log reports the effective settings, shot
rewind samples, historical misses, projectile catch-up steps and queue drops.
Catch-up advances player collision against immutable history while using current
map geometry. See [the weapon timing policies](docs/NETWORK_WEAPON_POLICIES.md)
for variant coverage and [the comparison harness](docs/NETWORK_LAGCOMP_COMPARISON.md)
for reproducible ON/OFF checks.

## Optional automatic server updates

Use a stamped dedicated-server release that publishes authoritative update
manifests. Local development builds and desktop packages cannot auto-update.
For example, add these arguments to your existing server or directory command:

```text
-autoupdate -update-repository YOUR/AUTHORITATIVE-FORK
```

Checks run at startup and every 15 minutes. Downloads are staged beside the
installation, outside live files. Before any downloaded executable runs, the
updater checks the release repository, authoritative family/protocol, platform,
archive hash and every file hash. The staged **new binary** then validates the
currently configured content, rotation and custom maps without opening a port.
Failed checks keep the existing server running and log the reason.

Installation waits until all admitted players have left, including loading and
spectating players. A directory waits for every owned or starting child match;
external server listings do not block it. Locally hosted children never update
independently. Standalone processes restart with their exact original arguments
and working directory. Under systemd, installation finishes before exit and the
supervisor restarts the service; configure `Restart=always`. Standalone Windows
uses the verified staged helper after the original process exits. Automatic
Windows service-supervisor handoff is not supported by this initial updater.

The installation parent must be writable. Replacement uses atomic file renames
with backups and rollback on ordinary installation errors. It is not a whole
directory switch or power-failure recovery journal. Operator files absent from
the release inventory remain in place. Failed rollback keeps admission closed
and logs retained recovery files. Four retained staging directories suspend
further downloads until an operator reviews them.

To check content manually, run the candidate dedicated binary:

```sh
./FruityPrimeServer -authoritative-server-validate -data /srv/fruity-content \
  -dataversion AMHE1 -rotation /srv/fruity/rotation.txt -mapdir /srv/fruity/maps
./FruityPrimeServer -authoritative-server-validate -masterserver
```

Success requires exit code 0 and the `authoritative-server-validation` JSON
report with `Success: true`. Directory-only validation needs no game data.
Content packages validate their manifest, profile and hashes; extracted cartridge
directories have no trusted manifest and are checked through the candidate's
parsers and gameplay probes. Relative data/rotation paths follow the installation
directory; an explicit relative map directory follows the original launch directory.

Release maintainers must set the repository variable
`AUTHORITATIVE_RELEASE_REPOSITORY` to their exact authoritative `OWNER/REPO` to
generate the three dedicated update ZIPs and manifests in the existing draft
release workflow. Missing configuration leaves ordinary release packaging in
place. Hash integrity trusts that configured HTTPS publisher; it is not code
signing. No public update or deployment was performed by the local checks.

## Map rotation

Create a plain text file with one match per line. `#` starts a comment:

```text
MP1 SANCTORUS      | Battle | 7 | 7
MP3 PROVING GROUND | Battle | 7 | 7
MP1 SANCTORUS      | Defender | 15 | 0 | 90
```

The fields are `ROOM KEY | mode | minutes | points | objective seconds`. Only the room key is
required; omitted rotation values default to Battle, 7 minutes and 7 points.
The fifth field sets the hold-time goal for Defender and Prime Hunter separately
from points; omitting it retains the mode's 90-second goal. In Survival, the
fourth field means extra lives after the initial spawn. Zero minutes means an
unlimited match clock. Invalid modes, negative goals and non-finite durations
are rejected with the rotation file's line number.
Every selected room/mode must be present in the supplied content package. Use
`-rooms` on a machine with configured extracted game files to print the room
keys. After a match ends, the server advances to the next rotation entry.

## Ports, listing and direct joins

Gameplay is UDP only. Allow inbound UDP on the configured match port (27888 in
the examples) and forward it through the router when the server is behind NAT.
The server does not open firewall or router ports for you.

Listing is opt-in:

```bash
./FruityPrimeServer -server -data /srv/fruity-content \
  -master net.livetek.fr:27889 -servername "My server"
./FruityPrime -servers
```

The directory receives a heartbeat approximately every 15 seconds and removes
silent entries after about 50 seconds. `-servers` asks the directory and then
probes each listed match server directly, so the displayed latency is the
client's path to the game server. An unlisted server can still be joined when
its address is known; listing does not change reachability.

## Run your own server directory

A directory-only process serves discovery without loading content:

```bash
./FruityPrimeServer -masterserver -port 27889 -hostports none
```

To let the directory start authoritative matches for players who cannot open a
port, configure content and a UDP port range for child servers:

```bash
./FruityPrimeServer -masterserver -port 27889 \
  -data /srv/fruity-content -dataversion AMHE1 \
  -hostports 27900-27919 -public games.example.com
```

Open UDP 27889 and every port in the selected host range. A range may contain
at most 64 ports. `-hostports none` disables hosted matches explicitly.
`-public` (or `-publicaddress`) is the externally reachable address to publish
when match heartbeats arrive over loopback or a private LAN. Without content,
directory queries continue to work but host requests are refused.

Each hosted match is a separate child server process. The directory reclaims a
match that never gets a player after three minutes, and an empty match after
roughly 45 seconds once it has been played; it also stops owned children during
shutdown. The master sees discovery traffic only, not gameplay.

The command-line host flow is:

```bash
./FruityPrime -hostgame "MP1 SANCTORUS" -mode Battle \
  -master games.example.com:27889
```

This asks the directory to start a match and joins it through the normal
authoritative client, so the hosting player's router needs no inbound rule.
The graphical launcher uses the same path for **Host → Where: Online**.

## Services and deployment

Templates are in `tools/systemd/`. The match unit requires `-data`; the
directory-only unit uses `-hostports none` unless you explicitly add content
and a host range. Fill in the placeholders with
`tools/render-server-unit.py`, then install the rendered unit. systemd stop
requests and Ctrl+C both shut down the simulation cleanly.

`deploy-server.sh` builds and uploads only the ARM64 server binary. Install the
content on the remote machine first and provide its existing absolute path:

```bash
MPH_SERVER_HOST=games.example.com MPH_SERVER_USER=gameuser \
MPH_SERVER_DIR=/home/gameuser/fruityprime-server \
MPH_SERVER_DATA=/srv/fruity-content \
MPH_SERVER_MASTER=games.example.com:27889 ./deploy-server.sh
```

`MPH_SERVER_DATA` is required. `MPH_SERVER_DATA_VERSION` defaults to `AMHE1`;
omit `MPH_SERVER_MASTER` to leave the match unlisted; and set
`MPH_DEPLOY_MASTER=0` to leave the directory service untouched. Deployment
validates the remote content before stopping services, preserves existing unit
rules, and never uploads extracted files or a server-content package.

## Compatibility and verification

Live clients, match servers and directories use authoritative wire family 2,
protocol 8, and should be updated together. Both family and protocol must match.
Discovery identifies upstream protocol-5 relays as online but incompatible;
the client sends no authoritative join to them. There is one live networking
implementation. Protocol-4 relay and protocol-5/6/7 authoritative demo files remain
readable through passive playback without opening a gameplay socket.

Protocol 8 carries authoritative afflictions, assists, kill attribution, reliable
objective events and complete immutable result statistics. The browser's optional
status extension advertises actual match policies while retaining the base status
response. Remote interpolation remains fixed at six ticks: the tested adaptive
candidate failed asymmetric-network quality gates and is not enabled.

Late-join waiting sessions currently reserve a player slot but have no gameplay
body. Reconnect grace lasts 1,800 simulation ticks for an existing participant
returning from the same endpoint with the prior session identity; rotation clears
it. This is session continuity, not an authenticated account login. See
[late joining](docs/G3_LATE_JOIN.md), [overtime](docs/G3_OVERTIME.md), and
[spawn policies](docs/G1_SPAWNING.md) for the detailed rules.

The server's status query is read-only and safe for browser polling. A normal
CI smoke check verifies the required-content error, directory query and
authoritative connection fixture. It does not prove a playable match without
separately supplied game data. For headless, content-bake and real UDP
validation commands, see
[`docs/NETWORK_MODERNIZATION.md`](docs/NETWORK_MODERNIZATION.md).

## Custom map directories

Pass `-mapdir /path/to/maps` when map definitions live outside the executable's
default `maps` directory. Locally hosted child servers inherit the launcher's
configured map directory. The generated collision/entity/model files must
already exist in the selected extracted data directory; server startup does not
regenerate or overwrite them. The retail content baker covers the retail room
table only; custom definitions and their generated data must be supplied and
validated separately.

### Optional registered-account tickets

Guest servers require no backend. To accept registered accounts, configure the dedicated server environment:

- `PRIME_TICKET_BACKEND`: HTTPS backend base URL (loopback HTTP is allowed for development).
- `PRIME_TICKET_ISSUER`: exact configured JWT issuer.
- `PRIME_SERVER_ID`: operator-registered nonempty UUID.
- `PRIME_SERVER_SECRET`: the server's backend API credential; keep it in the service environment, not command-line arguments or logs.
- `PRIME_REQUIRE_TICKETS`: optional `true` to refuse guests; defaults to `false`.

Each startup registers a new incarnation with `PUT /v1/server/session` and periodically renews registration. Admission and match reports share that incarnation. The server fetches public verification keys from `/v1/game-ticket-keys`; it never receives an account password or JWT signing private key. Credential-bearing HTTP does not follow redirects. Verification and bounded key fetching run on a background worker, outside the 60 Hz owner loop. An initial backend/key failure rejects authenticated joins; short outages may use cached keys for at most three minutes, and tickets themselves expire within 120 seconds. Explicit registration authorization failures close ticket admission. Unknown keys fail closed, with refresh attempts throttled to five seconds.

The Backend registration must include the operator-owned canonical public IPv4 address and UDP port (`PublicAddress`/`PublicPort`); use the external NAT destination when applicable. Ticket issuance without that destination fails closed. The client resolves its selected hostname once, requires that the Backend-returned address and port match that resolved destination, and pins the verified IP before sending credentials. A UDP advertisement alone cannot authorize a ticket destination.

A signed ticket binds account, server, startup incarnation, canonical name and client nonce. Repeated initial joins are idempotent for the same endpoint/nonce/ticket. Reconnect requires a fresh ticket; a matching account can recover its reserved participant slot from a new endpoint during the existing grace period. A guest or different account cannot reclaim that slot using its public connection ID. There are at most64 pending admission decisions and4096 unexpired replay entries; saturation rejects new authentication work rather than growing without bound.

## Accounts, career reports, and ranking status

The account service is a separate PostgreSQL-backed application. Follow
[src/Backend/README.md](src/Backend/README.md) for its explicit migrations,
identity/email configuration, signing keys, and registered server credentials.
The launcher's **Hunter License** screen supports registration, confirmation,
sign-in, profile changes, career totals, match history and leaderboards. Account
tokens stay in memory; the launcher saves only the chosen backend address.

For durable result delivery, configure `PRIME_REPORT_DIRECTORY` to an operator-owned
spool directory and `PRIME_REPORT_URL` to the Backend's HTTPS `/v1/server/matches`
endpoint. Reports use `PRIME_SERVER_ID` and `PRIME_SERVER_SECRET` (an explicit
`PRIME_REPORT_CREDENTIAL` overrides the latter). Keep credentials in private service
environment configuration. The bounded outbox reserves capacity before play,
atomically spools immutable reports, retries in order and verifies the exact
match-ID/hash receipt. Authentication failures and quarantined files require
operator attention; a generic HTTP 200 is not acceptance. See
[docs/G4_REPORTING.md](docs/G4_REPORTING.md).

The Backend assigns trust from its server registry. A server's own report claim
does not promote it to official status. Career data keeps practice/community
scopes separate from official data. Ranking Points are currently **pending policy
approval**, not calculated; empty RP boards do not mean a player has zero RP.

## Rulesets, bots, and spectators

Use `-ruleset classic`, `-ruleset competitive`, `-ruleset duel`, or `-ruleset custom`.
Classic retains the default mechanics. Competitive applies enhanced spawns,
offensive-action cancellation of spawn protection, overtime, pre-start team
balancing, next-match participation for late joins and disabled player radar.
Duel uses Battle mode with two active players and Duel spawn scoring; all rotation
entries must use Battle. Both retain original damage, movement, charge and pickup
timings. Competitive presets use their complete policy bundle; individual spawn,
overtime and late-join switches apply to Classic/Custom configurations.

`-spectators N` configures 0–16 separate observer connections;
`-spectatordelay SECONDS` configures a 0–30 second historical stream. Spectators
consume no player slot and send no gameplay input. The launcher offers **Join as
Spectator**; extra Duel joins use observers when capacity is available. A delayed
observer waits for a complete historical baseline and receives no live fallback.
Only the Backend's explicit trusted-observer capability can bypass delay.

Optional `PRIME_BOT_FILL` sets a target participant count and `PRIME_BOT_SKILL`
selects 0–2. Bots use normal authoritative entities without network connections;
human joins retire them at safe boundaries and release objectives. Duel requires
bot fill disabled. The launcher's **Practice** action starts an unlisted,
loopback-only authoritative server with bots. See [docs/G5_BOTS.md](docs/G5_BOTS.md).

## Map telemetry

Set `PRIME_TELEMETRY_DIRECTORY` to enable bounded, compressed per-match local
exports. Analysis commands and interpretation limits are in
[docs/G5_TELEMETRY.md](docs/G5_TELEMETRY.md). Telemetry is optional, samples routes
once per second, and includes no account or network identity. Dropped or incomplete
telemetry is explicitly marked and must not be treated as a complete balance
baseline.

## Intermission voting and tournament controls

`-votepolicy public` offers only entries from the configured rotation, plus rematch
and next-map choices. `-votepolicy private` offers rematch, next map and return to
lobby; Duel and Practice default to that policy. `PRIME_VOTE_POLICY` is the service
environment equivalent. The server owns the option IDs, eligibility, deadline and
deterministic tie break. Each human gets one confirmed vote; bots and observers
cannot vote. No client-provided map path is accepted. During the ballot, use number
keys 1–8, click/tap a row, or cycle selection and fire to confirm. Android offers
NEXT, PREV and VOTE controls. A returned lobby waits for a new match choice.

Optional authenticated tournament management binds only loopback. See
[docs/G5_TOURNAMENT.md](docs/G5_TOURNAMENT.md) for the admin credential hash,
request UUID/expiry contract, ready checks, allowlisted map/rules selection,
between-round holds, participant controls and recording. Remote operators use an
authenticated tunnel. Pausing between rounds never freezes an active simulation.
The Backend signs accepted immutable result exports; that signature cannot serve
as a game admission ticket.

Set `PRIME_SERVER_REPLAY_DIRECTORY` to an operator-owned directory to record every
round automatically. Authenticated or reported Duel requires this setting. The
server waits for recording readiness before starting and reports recording failures
explicitly. A report ReplayId identifies the artifact; it does not by itself attest
that the file finished writing successfully.

Indexed recording and camera controls are documented in
[docs/G5_REPLAY.md](docs/G5_REPLAY.md). The map-balance report workflow is in
[docs/G5_BALANCE_REPORTS.md](docs/G5_BALANCE_REPORTS.md); it separates comparable
cohorts and flags insufficient data. It does not apply balance changes.
