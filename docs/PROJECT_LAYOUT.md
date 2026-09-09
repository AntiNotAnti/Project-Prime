# Project layout

The official product name is **Prime Hunters**. Existing `FruityPrime` binary
names, Android package IDs, asset filenames and repository URLs remain stable
for installation and update compatibility. The C# namespace remains `MphRead`.

The simulation is a platform-neutral .NET 10 library. Client and the online
hosting path are separate executables: the persistent Server Node owns control,
sessions and public lobbies, while its managed Server Workers own authoritative
matches and direct gameplay UDP. Client control traffic uses the Node connection;
gameplay traffic uses the selected Worker.

| Project | Responsibility | Project references |
|---|---|---|
| `src/Game` | World, Hunter simulation, movement, combat, match rules, content readers, protocol | None; only OpenTK.Mathematics package |
| `src/Client` | Desktop launcher, rendering, HUD, input, client networking, demos, sound devices | Game, Audio.Ncsf |
| `src/Backend` | Account, Node directory/admission, report ingestion and career projections | Game |
| `src/Server.Shared` | Versioned Node/Worker process, placement, admission and report contracts | Game, Shared.Replay |
| `src/Server.Node` | Persistent control authority: sessions, public lobbies, Worker placement/lifecycle, directory and report outbox | Server.Shared |
| `src/Server.Worker` | Node-owned authoritative `MatchInstance` processes, fixed-tick simulation, replication, lag compensation, direct UDP, replay, telemetry and report artifacts | Game, Server.Shared, Shared.Replay |
| `src/Android` | Android lifecycle, touch/gamepad, graphics/audio adapters, shared client presentation | Game, Audio.Ncsf |
| `src/Audio.Ncsf` | Original NCSF/SDAT music decoder and playback | None |
| `src/Tools` | Extraction, conversion, sound/image exports, map cooking and content baking | Game |
| `tests/Tests` | Focused game, match, protocol, client, Worker, content and integration tests | Production projects |
| `tests/Server.Node.Tests` | Node, Worker lifecycle, IPC, placement, lobby and package-boundary checks | Server.Node, Server.Shared |
| `tests/Imaging` | Isolated managed TGA and map-image decoding tests | Linked Shared imaging sources only |
| `tools/nettest` | Controlled Worker content, authority and impaired-network acceptance harness | Production projects |
| `tools/worker-soak` | Node/Worker lifecycle, crash, report and bounded capacity harnesses | Server.Node, Server.Shared |

`Game.sln` builds the desktop projects and tests without requiring the Android
workload. Build Android directly with its project. The C# namespace remains
`MphRead`; physical ownership does not require a namespace rewrite.

## Shared platform capabilities

Android references Game instead of recompiling it. Its enumerated source list
includes only the shared client implementation required by its platform head.
Desktop entry points, Node control, Worker simulation, directory services and
tool commands are excluded. GL/OpenAL aliases remain Android adapters.

`src/Shared` contains explicitly linked capabilities needed by more than one
executable: internal UDP transport, process hosting, content import and map
preparation. The map compiler still prepares custom rooms from the user's own
extracted files, including on Android. These files do not introduce a Game-to-
platform dependency. Android uses its own bitmap/TGA and PNG adapters.

The tests are organized by the behavior they exercise under
`tests/Tests/{Game,Match,Protocol,Client,Server,Integration,Content}`. The
namespace remains `MphRead.Tests`; the folders are only an ownership and
discovery aid. Imaging tests live in their own `tests/Imaging` assembly because
the desktop Client and Tools projects each own their linked map-image and
`RgbImage` source/API identity. Linking the decoder into the all-references
test assembly would create duplicate type identities, so the imaging project
links the Shared decoder and map-image entry point directly. It is a test-only
project and does not add another production assembly or project reference.

Tools explicitly links the CPU NCSF serialization sources it needs for extraction.
It does not reference the playback assembly or ship SoundFlow. Shared source
lists contain individual files, with no cross-project wildcard.

Game sends presentation requests through scene/player interfaces and an audio
request stream. CPU models and animation needed for collision stay in Game;
GPU bindings, rendering queues, playback handles and devices stay in Client.
Headless scenes have no presentation subscriber. Host services supply networking
and combat authority through interfaces rather than client/server singletons. The
client never launches a Worker directly; private or unlisted local hosting has
been retired from the client cutover, and there is no Worker `--standalone`
option.

## Build and publish

```sh
dotnet build Game.sln -c Release
dotnet test tests/Tests/Tests.csproj -c Release
dotnet test tests/Server.Node.Tests/Server.Node.Tests.csproj -c Release --filter 'RequiresGameContent!=true'
dotnet test tests/Imaging/Imaging.Tests.csproj -c Release
dotnet publish src/Client/Client.csproj -c Release -r linux-x64 --self-contained true
dotnet build src/Tools/Tools.csproj -c Release
dotnet build src/Android/Android.csproj -c Release

# Packages the Backend, one persistent Node, and its bundled Worker below worker/.
tools/package-server.sh --rid linux-arm64 --output publish/server-linux-arm64
```

Client output contains no local server executable. The server package contains
the Backend apphost below `backend/`, the persistent Node apphost
`FruityPrimeServer` at its root, and the managed Worker apphost below `worker/`
(with `.exe` on Windows). The Node resolves and supervises Workers from
`server.example.json`; it is the only supported gameplay hosting boundary. The
Backend binary is included for same-host development and remains separately
configured with its database and operator secrets for production. Package smoke
is the extracted-bundle WSS → public-lobby → Worker → routed-UDP process check.
Tools uses `FruityPrimeTools`.

The repository guards enforce the project graph, package budget, explicit source
links and absence of retired campaign runtime code:

```sh
python3 tools/check-project-boundaries.py
python3 tools/check-multiplayer-only.py
```

Native publish checks, Android APK checks, content audit and impaired-network
acceptance remain separate evidence from compilation. Missing First Hunt assets
and rendered/device testing limits remain recorded in the refactor progress log.
