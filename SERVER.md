# Running a Project Prime server

## Quick development start

From the repository root, the supported all-in-one command is:

```bash
./start-server.sh
```

It identifies the host RID, reuses or creates a matching server bundle, safely
stops only a previously verified stack in the same state directory, and starts
the local Backend, persistent Server Node, and Node-managed Workers. Extracted
`AMHE1` content is used by default from `./AMHE1`; override it with
`--content-dir` or `PRIME_CONTENT_DIRECTORY`.

Use `./start-server.sh --status` for a read-only check and
`./start-server.sh --stop-only` to stop without restarting. Runtime logs,
generated development credentials, lock metadata, replays, and artifacts live
under `${PRIME_DEV_STATE_DIR:-${TMPDIR:-/tmp}/project-prime-dev}` unless
`--state-dir` is supplied. Shutdown drains the Node first. If the overall grace
period expires (`--grace-seconds`, default 60), remaining verified processes
are forced down and any active matches can be interrupted.

For all deployable clients and servers, run `./build-all.sh`. Android is built
by default and requires the Android workload, JDK, and SDK; pass
`--skip-android` only when intentionally omitting it. Outputs are written to a
new timestamped directory under `publish/` unless `--output` is provided. The
packages contain custom `.fpmap` bundles but never AMHE1-derived room binaries
or other proprietary game assets; those room binaries are prepared at runtime
from operator-provided extracted content.

The G6 UI/lobby update has been rolled back. Rebuild and deploy matching client
and server binaries; do not mix G6 builds with the restored match admission.
The original launcher and automatic match countdown are restored. Backend
security and stored ratings are retained, but these servers emit schema 1 match
reports, which are not eligible for new rating awards. See [rollback details](docs/G6_ROLLBACK.md).

Project Prime online matches use authoritative wire family 2, protocol 8. The
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
executable is `ProjectPrimeServer` on Unix and `ProjectPrimeServer.exe` on Windows.
The server is a separate project and references only Game; it has no launcher,
rendering or audio dependencies.

For a development checkout, the equivalent is:

```bash
dotnet run --project src/Server/Server.csproj -c Release \
  -- -server -data /path/to/files/AMHE1
```

For a local Windows one-click launcher, run `Start-ProjectPrime.cmd` from the
repository root. It offers **Game**, **Dedicated server** and **Server
directory** in a menu, and accepts the same choices without a menu:

```powershell
.\Start-ProjectPrime.cmd game
.\Start-ProjectPrime.cmd server
.\Start-ProjectPrime.cmd directory
```

The script assumes `AMHE1` is beside it, passes that directory to the server,
and creates the game's `paths.txt` automatically when needed. It uses a
published executable when one is present; otherwise it builds a local copy
under `.project-prime-launcher/`. The server starts unlisted on UDP 27888, while the
directory starts discovery-only on UDP 27889. Enable hosted matches with, for
example, `powershell -File .\Start-ProjectPrime.ps1 directory -HostPorts
27900-27919 -PublicAddress games.example.com`.

Client builds and publishes include their standalone server in `server/`. The
Host action starts that child executable. `PROJECT_PRIME_SERVER_PATH` can select a
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
./ProjectPrimeServer -server -data /srv/project-prime-content -dataversion AMHE1
```

Content baking belongs to Tools. Build it with `dotnet build src/Tools/Tools.csproj -c Release`,
or use `dotnet run --project src/Tools --` before the same arguments. For a smaller
headless package, bake the dependencies into a new directory:

```bash
./ProjectPrimeTools -servercontent /srv/project-prime-content-amhe1 \
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
./ProjectPrimeServer -server "MP1 SANCTORUS" \
  -data /srv/project-prime-content -dataversion AMHE1 \
  -port 27888 -players 8 -servername "My server"

# Windows: use the console server binary
ProjectPrimeServer.exe -server "MP1 SANCTORUS" \
  -data "C:\\ProjectPrime\\content" -dataversion AMHE1 \
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
./ProjectPrimeServer -authoritative-server-validate -data /srv/project-prime-content \
  -dataversion AMHE1 -rotation /srv/project-prime/rotation.txt -mapdir /srv/project-prime/maps
./ProjectPrimeServer -authoritative-server-validate -masterserver
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
./ProjectPrimeServer -server -data /srv/project-prime-content \
  -master rebooty.xyz:27889 -servername "My server"
./ProjectPrime -servers
```

The directory receives a heartbeat approximately every 15 seconds and removes
silent entries after about 50 seconds. `-servers` asks the directory and then
probes each listed match server directly, so the displayed latency is the
client's path to the game server. An unlisted server can still be joined when
its address is known; listing does not change reachability.

## Run your own server directory

A directory-only process serves discovery without loading content:

```bash
./ProjectPrimeServer -masterserver -port 27889 -hostports none
```

To let the directory start authoritative matches for players who cannot open a
port, configure content and a UDP port range for child servers:

```bash
./ProjectPrimeServer -masterserver -port 27889 \
  -data /srv/project-prime-content -dataversion AMHE1 \
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
./ProjectPrime -hostgame "MP1 SANCTORUS" -mode Battle \
  -master games.example.com:27889
```

This asks the directory to start a match and joins it through the normal
authoritative client, so the hosting player's router needs no inbound rule.
The graphical launcher uses the same path for **Host → Where: Online**.

## Services and deployment

Templates are in `tools/systemd/`. The deployed
`projectprime-stack.service` owns the Backend, persistent Server Node and
Node-managed Workers through `start-stack-dev.sh`. The standalone match and
directory templates remain available for manually managed processes. Fill in
placeholders with `tools/render-server-unit.py`, then install the rendered
unit; systemd stop requests and Ctrl+C both shut down the simulation cleanly.

`deploy-server.sh` transactionally deploys the complete Backend + Node +
managed Workers stack. Its defaults are a `linux-x64` bundle rooted at
`/srv/project-prime`, with operator content in
`/srv/project-prime/AMHE1` and persistent generated state in
`/srv/project-prime/state`. Without `--bundle` it builds a fresh package. A
complete build can be deployed without recompiling:

```bash
./build-all.sh --skip-android --output publish/deploy
./deploy-server.sh --host 51.161.113.128 --user ubuntu \
  --deploy-dir /srv/project-prime \
  --data /srv/project-prime/AMHE1 \
  --bundle publish/deploy/servers/linux-x64 --rid linux-x64 --preflight-only
./deploy-server.sh --host 51.161.113.128 --user ubuntu \
  --deploy-dir /srv/project-prime \
  --data /srv/project-prime/AMHE1 \
  --bundle publish/deploy/servers/linux-x64 --rid linux-x64
```

The default target is `ubuntu@51.161.113.128` with root `/srv/project-prime`.
The production Node
advertises `rebooty.xyz`. Its public host and control URI are persisted in
`state/dev.env`, and the deployer validates that state against the advertised
endpoint while SSH deployment targets `ubuntu@51.161.113.128`.

The existing `MPH_SERVER_HOST`, `MPH_SERVER_USER`, `MPH_SERVER_DIR`,
`MPH_SERVER_CONFIG`, `MPH_SERVER_DATA`, `MPH_SERVER_DATA_VERSION`, and
`MPH_SERVER_PASS` environment names remain supported. Copy
`.env.deploy.example` to ignored `.env.deploy` for local settings. Explicit CLI
options win. `PRIME_DEPLOY_BUNDLE` selects a compiled bundle;
`MPH_SERVER_BUNDLE` remains an alias. `MPH_SERVER_CONFIG` and `--config`
are accepted but ignored: production configuration is generated and persisted
in `<deploy-dir>/state/dev.env`, and is neither read nor uploaded from the
operator machine. `--preflight-only` performs local bundle/RID/content checks
and read-only remote architecture, protected-path, ownership, state, public
endpoint, process-layout and disk validations without an upload or service stop.

In the generated stack configuration, each packaged Worker uses
`<deploy-dir>/current` as its `WorkingDirectory` and
`<deploy-dir>/current/maps` as its `--map-dir`; its `--content-dir` must
equal `MPH_SERVER_DATA`. The deployer validates these paths before upload or
downtime. The protected `app`, `state` and `AMHE1` roots remain outside
release retention: `app` is the direct-bundle handoff/rollback root, `state`
holds generated configuration and credentials, and `AMHE1` is operator
content.

Releases are staged under `<deploy-dir>/releases/<release-id>.staging`, hash
verified, and then moved to `<deploy-dir>/releases/<release-id>`. The atomic
`<deploy-dir>/current` symlink selects the active release. The uploaded
release and rendered unit are verified before either a verified pre-rename
supervisor or `projectprime-stack` is stopped. A narrow pre-rename handoff
accepts only one identity- and ancestry-verified supervisor; ambiguous or
parallel processes fail closed.

Activation switches `current`, installs and enables `projectprime-stack`,
and starts the complete Backend + Node + Worker tree. It is health-gated on
both Backend and Node endpoints and verifies that the managed process tree
contains all three component types. A failed full-stack activation restores
the prior pointer and unit, reactivates the prior stack state, and retains
release and journal evidence when recovery is uncertain. The deployment lock is
`<deploy-dir>/releases/.deploy-lock`; an interrupted activation deliberately
retains it until the operator verifies that no activation or rollback remains,
so the next deployment fails closed rather than overlapping an uncertain update.

## Compatibility and verification

Live clients, match servers and directories use authoritative wire family 2,
protocol 8, and should be updated together. Both family and protocol must match.
Discovery identifies upstream protocol-5 relays as online but incompatible;
the client sends no authoritative join to them. There is one live networking
implementation. Protocol-4 relay and protocol-5/6/7 authoritative replay files remain
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
