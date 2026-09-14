# Project Prime server operations

Project Prime has one supported multiplayer server architecture:

```text
Backend
  |
Persistent Server Node
  |-- sessions, lobbies, chat, readiness, Worker placement and match handoff
  |
Managed Worker pool
  |-- MatchInstance A: isolated authoritative 60 Hz simulation + gameplay UDP
  `-- MatchInstance N: isolated authoritative 60 Hz simulation + gameplay UDP
```

The Node owns the reliable control plane. Workers own gameplay, and each
`MatchInstance` owns all of its mutable state. The Node starts and supervises
Workers. Gameplay Workers are not separate operator-managed services.

Server packages contain no cartridge data, credentials, database, or persistent
state. Operators must supply those outside the package.

## Local development stack

The supported checkout launcher starts the Backend, Node, and Node-managed
Workers together. It uses extracted AMHE1 content and keeps generated
development credentials, logs, artifacts, replays, and database state beneath a
separate state directory.

```bash
./start-server.sh --content-dir /absolute/path/to/AMHE1
./start-server.sh --status
./start-server.sh --stop-only
```

With no explicit shared-Backend credentials, the launcher starts the local
development Backend. Use `--no-backend` only when a shared Backend and the
required Node credentials are already configured. Run `./start-server.sh
--help` and `tools/start-dev.sh --help` for the current options.

The Windows convenience launcher `Start-ProjectPrime.cmd` starts the game only.
It does not operate the server stack.

## Build a combined server package

The package boundary publishes all three server components and preserves their
ownership:

```bash
tools/package-server.sh --rid linux-x64 --output publish/server-linux-x64
tools/package-server.sh --rid linux-arm64 --output publish/server-linux-arm64
tools/package-server.sh --rid win-x64 --output publish/server-win-x64
```

Release packages are self-contained .NET applications. The layout is:

```text
ProjectPrimeServer[.exe]                 persistent Server Node
backend/ProjectPrime.Backend[.exe]       account and directory Backend
worker/ProjectPrime.Server.Worker[.exe]  Node-managed gameplay Worker
server.example.json                      credential-free Node template
maps/*.fpmap                             cooked custom-map bundles
```

The portable source template names `worker/ProjectPrime.Server.Worker`.
Packaging rewrites that field to `worker/ProjectPrime.Server.Worker.exe` for
`win-x64`; do not remove the extension from a packaged Windows configuration.

## Configure the Node

Copy `server.example.json` to `appsettings.json` in an operator-owned state or
configuration directory. The example is intentionally incomplete. At minimum,
configure the fields described in
[`src/Server.Node/README.md`](src/Server.Node/README.md):

- persistent `Node:Authentication:NodeId`;
- the exact HTTPS Backend ticket issuer and one or more ES256 public keys;
- the bounded `Node:Maps` catalog matching Worker content;
- each Worker process's content identity, content/map/replay arguments,
  artifact directory, public UDP host/bind, and capacity;
- the optional Backend directory publication settings and credential.

Keep private keys, database connections, directory credentials, TLS passwords,
cartridge content, artifacts, and replays outside release directories. Public
control connections require TLS/WSS. Gameplay uses UDP directly with Workers;
do not proxy gameplay through the Node control connection.

## Run an extracted Linux bundle

`start-dev.sh` inside a Linux bundle starts its Node and Worker against an
already configured shared Backend:

```bash
cd /srv/project-prime/package
./start-dev.sh \
  --content-dir /srv/project-prime/AMHE1 \
  --state-dir /srv/project-prime/state
```

Set the required `PRIME_NODE_*` values in `dev.env` beside the script or in a
file selected by `PRIME_DEV_ENV_FILE`. The script validates content, generates
`appsettings.json` in the state directory, and launches
`ProjectPrimeServer --contentRoot <state-directory>`.

`start-stack-dev.sh` is the Linux all-in-one development entry point included
in the bundle. It also starts the packaged Backend. Production database and
secret configuration remain operator-owned.

## Run an extracted Windows bundle

There is currently no Windows whole-stack supervisor. Configure and operate the
packaged Backend and Node as separate supervised processes; the Node then owns
the Worker lifecycle.

1. Copy `server.example.json` to an operator-owned `appsettings.json` and fill
   in the Node authentication, map, Worker content, artifact, replay, endpoint,
   and Backend publication settings.
2. Configure and start `backend\ProjectPrime.Backend.exe` with its operator-owned
   database and secrets.
3. Start the Node with the directory containing `appsettings.json` as its
   content root:

```powershell
.\ProjectPrimeServer.exe --contentRoot C:\ProjectPrime\state
```

The packaged Windows template must retain this Worker filename:

```text
worker/ProjectPrime.Server.Worker.exe
```

The Worker is not started manually. Node readiness requires compatible map and
Worker capacity, and Backend discovery requires the Node to be ready.

## Production deployment

The maintained production deployer targets the complete Linux Backend + Node +
Worker stack. It validates a candidate before downtime, stages releases beneath
the remote release root, atomically switches the active release, health-checks
Backend and Node, and rolls back the prior pointer and service when activation
fails.

```bash
./deploy-server.sh --help
./deploy-server.sh \
  --host HOST --user USER \
  --deploy-dir /srv/project-prime \
  --data /srv/project-prime/AMHE1 \
  --bundle publish/server-linux-x64 \
  --rid linux-x64 \
  --preflight-only
```

Run the same command without `--preflight-only` only after reviewing the
preflight. See [`docs/RENDERED_WAN_OPERATOR_RUNBOOK.md`](docs/RENDERED_WAN_OPERATOR_RUNBOOK.md)
for the public TLS, DNS, Cloudflare, firewall, and WAN acceptance procedure.

## Validation boundaries

Structural package validation is content-free:

```bash
tools/check-dedicated-server.sh publish/server-linux-x64
```

The full package smoke copies the bundle into a fresh directory and exercises
WSS control, lobby start, Node-managed Worker launch, UDP admission, match end,
artifact/replay writes, and graceful drain. It requires private extracted
AMHE1 content:

```bash
tools/package-smoke.sh \
  --bundle publish/server-linux-x64 \
  --content-dir /absolute/path/to/AMHE1
```

Release CI can start the packaged Windows Worker in its explicit content-free
runtime-smoke mode and verify that `server.example.json` resolves to that exact
`.exe`. That proves the Windows apphost and package path load. It does not
replace the content-backed WSS-to-Worker-to-UDP smoke, deployed Backend/database
validation, or live Windows multiplayer acceptance.

Before publishing a server change, run the relevant Node, Worker, and Backend
tests plus the repository boundary checks. Architecture acceptance must prove
that simultaneous matches cannot influence each other's state, RNG, queues, or
resources—not merely that two matches can start.
