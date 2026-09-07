# Project layout

The simulation is a platform-neutral .NET 10 library. Client and Server are
separate executables and communicate through the protocol in Game.

| Project | Responsibility | Project references |
|---|---|---|
| `src/Game` | World, Hunter simulation, movement, combat, match rules, content readers, protocol | None; only OpenTK.Mathematics package |
| `src/Client` | Desktop launcher, rendering, HUD, input, client networking, demos, sound devices | Game, Audio.Ncsf |
| `src/Server` | Authoritative simulation host, replication, lag compensation, directory, server updater | Game |
| `src/Android` | Android lifecycle, touch/gamepad, graphics/audio adapters, shared client presentation | Game, Audio.Ncsf |
| `src/Audio.Ncsf` | Original NCSF/SDAT music decoder and playback | None |
| `src/Tools` | Extraction, conversion, sound/image exports, map cooking and content baking | Game |
| `tests/Tests` | Focused game, match, protocol, client, server, content and integration tests | Production projects |
| `tests/Imaging` | Isolated managed TGA and map-image decoding tests | Linked Shared imaging sources only |
| `tools/nettest` | Controlled content, authority and impaired-network acceptance harness | Production projects |

`Game.sln` builds the desktop projects and tests without requiring the Android
workload. Build Android directly with its project. The C# namespace remains
`MphRead`; physical ownership does not require a namespace rewrite.

## Shared platform capabilities

Android references Game instead of recompiling it. Its enumerated source list
includes only the shared client implementation required by its platform head.
Desktop entry points, server simulation, directory services and tool commands
are excluded. GL/OpenAL aliases remain Android adapters.

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
and combat authority through interfaces rather than client/server singletons.

## Build and publish

```sh
dotnet build Game.sln -c Release
dotnet test tests/Tests/Tests.csproj -c Release
dotnet test tests/Imaging/Imaging.Tests.csproj -c Release
dotnet publish src/Client/Client.csproj -c Release -r linux-x64 --self-contained true
dotnet publish src/Server/Server.csproj -c Release -r linux-arm64 --self-contained true
dotnet build src/Tools/Tools.csproj -c Release
dotnet build src/Android/Android.csproj -c Release
```

Client build output includes a framework-dependent Server under `server/` for
local hosting. Client publish output includes a Server payload matching its RID
and deployment options. This is a packaging target, not an assembly reference.
A dedicated server release uses `FruityPrimeServer` on every operating system
(`.exe` on Windows). Tools uses `FruityPrimeTools`.

The repository guards enforce the project graph, package budget, explicit source
links and absence of retired campaign runtime code:

```sh
python3 tools/check-project-boundaries.py
python3 tools/check-multiplayer-only.py
```

Native publish checks, Android APK checks, content audit and impaired-network
acceptance remain separate evidence from compilation. Missing First Hunt assets
and rendered/device testing limits remain recorded in the refactor progress log.
