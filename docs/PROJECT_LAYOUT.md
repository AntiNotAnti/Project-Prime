# Project layout

Status: **CURRENT**, evaluated from the working-tree MSBuild graph on 2026-09-12.
Source and project files remain authoritative if this summary drifts.

The official product identity is **Project Prime**. `ProjectPrime` binary names,
Android package IDs, asset filenames, and repository URLs are the clean-break
identity for new builds. The C# namespace remains `MphRead`; physical ownership
does not require a namespace rewrite.

## CURRENT: production graph

The table lists direct runtime project references, not merely the shortest
conceptual dependency. `Game` also consumes `Protocol.Generator` as an
analyzer-only build reference; that generator is not a runtime dependency.

| Project | Responsibility | Direct runtime project references |
|---|---|---|
| `src/Game` | World and Hunter simulation, movement, combat, match rules, content readers, protocol | None |
| `src/MapPlatform` | Platform-neutral map discovery, preparation, validation, and compilation | Game |
| `src/Imaging` | Managed image decoding used by rendering and map preparation | MapPlatform |
| `src/Renderer` | Backend-neutral render contracts plus desktop SDL GPU resources | Game, Imaging, MapPlatform |
| `src/Editor` | Desktop map/content editor | Game, Imaging, MapPlatform, Renderer |
| `src/Client.Core` | Portable accounts, input vocabulary/settings, Node control, and host-independent client state | Game, MapPlatform, Server.Shared, Shared.Replay |
| `src/Client.Presentation` | Shared launcher UI, HUD, audio, client networking, replay, and scene presentation | Audio.Ncsf, Client.Core, Game, Imaging, MapPlatform, Renderer, Server.Shared, Shared.Replay |
| `src/Client` | Desktop entry point, SDL host/input adapters, desktop secure storage, update installation, and rendering tools | Audio.Ncsf, Client.Core, Client.Presentation, Game, MapPlatform, Renderer, Server.Shared, Shared.Replay |
| `src/Android` | Android lifecycle, surfaces, input, secure storage, update installation, and SDK adapters | Audio.Ncsf, Client.Core, Client.Presentation, Game, Imaging, MapPlatform, Renderer, Server.Shared, Shared.Replay |
| `src/Audio.Ncsf` | NCSF/SDAT decoder and playback implementation | None |
| `src/Shared.Replay` | Portable replay contracts and codecs | Game |
| `src/Backend` | Account, Node directory/admission, report ingestion, and career projections | Game, Server.Shared |
| `src/Server.Shared` | Versioned Node/Worker process, placement, admission, and report contracts | Game |
| `src/Server.Node` | Persistent sessions, public lobbies, Worker placement/lifecycle, directory, and report outbox | Server.Shared |
| `src/Server.Worker` | Authoritative `MatchInstance` process, fixed-tick simulation, replication, direct UDP, replay, and telemetry | Game, Imaging, MapPlatform, Server.Shared, Shared.Replay |
| `src/Tools` | Extraction, conversion, sound/image export, map cooking, and content baking | Game, Imaging, MapPlatform |
| `src/EnhancedMaterials.Tool` | Desktop enhanced-material inspection utility | Client, Renderer |

`Protocol.Generator` targets `netstandard2.0`. Android targets
`net10.0-android36.0`; the ordinary production graph targets `net10.0`.
`Game.sln` contains the desktop production graph and tests without Android, so
it does not require the Android workload.

## CURRENT: client ownership

```text
Desktop Client head ----+
                        +--> Client.Presentation --> Renderer
Android head -----------+            |
                                     +--> Client.Core --> Game
```

Android no longer recompiles Client implementation sources. Its explicit
compile list contains Android-owned entry points and adapters only. Shared
presentation C#, Avalonia XAML, UI copy, and branding assets are physically
owned by `src/Client.Presentation`. Desktop native hosts, platform secure
storage, desktop update installation, and diagnostic render hosts remain in
`src/Client`; Android supplies its own equivalents.

`src/Shared` remains a deliberately small source-link area. Current production
links are explicit individual files:

- Client.Core links `Hosting/BuildVersion.cs`.
- Client.Presentation links content import, TGA decoding, UDP transport, and
  runtime-platform helpers.
- Client links `Hosting/ConsoleSetup.cs`.
- Imaging links `Imaging/StbImageDecoder.cs`.
- Server.Worker links `Transport/UdpTransport.cs`.
- Tools links content import and console setup, plus an explicit CPU-only NCSF
  source manifest. It does not reference the playback assembly or ship
  SoundFlow.

No production project uses a cross-project source wildcard.

## TARGET: boundaries

- `Game` and `Client.Core` stay free of native windowing, renderer, desktop UI,
  Android SDK, and platform secure-storage dependencies.
- Both client heads consume shared runtime and presentation through normal
  project references.
- CPU model/animation state needed by collision stays in Game. Render commands,
  GPU resources, and presentation histories stay outside Game.
- Backend and Node own control-plane work. A Node-owned Worker and its
  `MatchInstance` own authoritative gameplay. Client control uses the Node;
  gameplay UDP goes directly to the selected Worker.
- Headless scenes have no presentation subscriber, and renderer sampling must
  not mutate authoritative simulation state.

## TEMPORARY EXCEPTIONS

`Client.Presentation` is deliberately `net10.0` in every ordinary or desktop
evaluation. Android alone opts it into `net10.0-android36.0` through
`PrimeEnableAndroidPresentation=true` on its project reference. This explicit
Android target is a temporary migration boundary for substantial Android
presentation branches, not an ordinary multi-target or RID fan-out.
The Android head restores that opt-in evaluation non-recursively into
`Client.Presentation/obj/android`; keeping it separate from the ordinary
`obj` graph makes desktop-build -> Android-build -> desktop-build ordering safe.

For that Android evaluation, Client.Presentation also links the two guarded
Renderer GLES implementation files and supplies Android GLES/audio aliases.
Those are explicit compatibility exceptions; desktop SDL/native code must not
enter the Android graph, and Android SDK/native-host code must not enter
Client.Core.

Some shared presentation input surfaces still use OpenTK-compatible key and
mouse value types after native events have been translated. Prime-owned input
types are the portable vocabulary; retained compatibility types are not native
window or graphics ownership.

## Tests and build entry points

Focused tests are split by owner under `tests/`. `tests/Imaging` remains a
separate assembly because it links the relevant Shared decoder entry points
without creating duplicate type identities in the all-references test assembly.
`tools/nettest` is the controlled Worker/content/network acceptance harness.

```sh
dotnet build Game.sln -c Release
dotnet test tests/Tests/Tests.csproj -c Release
dotnet test tests/Server.Node.Tests/Server.Node.Tests.csproj -c Release --filter 'RequiresGameContent!=true'
dotnet test tests/Imaging/Imaging.Tests.csproj -c Release
dotnet publish src/Client/Client.csproj -c Release -r linux-x64 --self-contained true
dotnet build src/Tools/Tools.csproj -c Release
dotnet build src/Android/Android.csproj -c Release
```

The desktop client package contains no local server. Server packaging contains
the Backend, one persistent Node, and the managed Worker below `worker/`. The
Node is the only supported gameplay-hosting boundary; there is no Worker
`--standalone` client path.

The graph and target boundary are guarded by:

```sh
python3 tools/check-project-boundaries.py
python3 tools/check-client-presentation-targets.py
python3 tools/check-multiplayer-only.py
python3 tools/check-build-guardrails.py
```

Compilation, native runtime smoke, rendered gameplay, physical-device input,
content-backed acceptance, protected package execution, and WAN/deployed
acceptance are distinct evidence classes. Current results are recorded in
`CURRENT_RELEASE_GATES.md`; unresolved items are tracked in
`.claude/KNOWN-GAPS.md`.
