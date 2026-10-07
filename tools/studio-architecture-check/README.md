# Studio architecture contracts

Run from the repository root with .NET 10:

```sh
dotnet run --project tools/studio-architecture-check -c Release
dotnet run --project tools/studio-architecture-check -c Release -- --self-test
```

An optional positional argument selects a different repository root. Exit code
1 means a contract failed; each failure identifies its source file or project.

Studio may reference the canonical `MphRead` engine project and the small
`ProjectPrime.Studio.Protocol` project. That preserves one implementation while
the process boundary is proven before selective library extraction. Studio
and the protocol are scanned for game-window ownership, live gameplay sessions,
foreground replay facades, runtime map publication, HTTP/TCP listeners,
gameplay-protocol coupling and Android-specific code. Shared engine models and
private playback may retain the `MphRead` namespace; the protocol project remains
framework-only and cannot import engine or Avalonia namespaces.
Its one external source link is the exact internal
`MphRead/Mods/Update/DesktopInstallationIdentity.cs` helper, compiled into both
assemblies from the same BCL-only file. This grants no engine assembly reference
or permission to link other engine sources; the helper is scanned too.
The game and Android projects may consume the protocol but cannot depend on the
Studio executable or its presentation sources. The Android source glob explicitly
removes desktop Map panels/viewports and their native host services. General
Android capture helpers remain available, with the Map creator diagnostic
preprocessor-guarded out. Negative contracts reject missing, commented or later
reintroduced exclusions and unguarded creator captures. Studio must start an ordinary
Avalonia Desktop application targeting `net10.0`.

The scanner checks C# identifiers, ignores ordinary comments and string literals,
and preserves interpolated strings to catch code in interpolation expressions.
Its self-checks cover direct/aliased/escaped identifiers, comments, strings and
interpolation. Runtime publication checks cover direct calls, aliases, static
imports and method groups. It also inspects explicit source links and project
references.
The tool also consumes the actual framework-only protocol assembly and exercises
identity/hash/path validation, game-option bounds, playtest IDs, unknown frame
fields, message size limits and challenge/version/side-bound authentication.
`--self-test` runs 66 scanner and protocol assertions without opening a pipe or
starting either application.
The game publication source contracts also require public `Publish`/`Install`
and raw `MapPacker` commits to acquire the runtime destination fence. Prewarm
must generate before acquiring its shared reader and validate exact outputs
under that reader before loading paths. Negative fixtures reject generation
under a reader, missing handoff validation and a commented-out writer fence.
This is a checked-in source boundary, not a semantic compiler or a substitute for
map/replay parity, security review, UI captures, runtime lifecycle tests or builds
on each desktop platform. It does not evaluate arbitrary imported MSBuild targets.

There are no Studio-source compatibility adapter exceptions. The engine-side
`MapStudioLegacyHost.cs` and `MapViewportGpu.cs` exceptions are exact source files
with individual permitted owners, documented in
[`project-prime-studio.md`](../../docs/architecture/project-prime-studio.md).
`StudioPrivateMapRuntime.cs` may publish canonical build outputs only after its
canonical private-root/destination guards; its guard and pre-publication recheck
are source contracts. The app cannot call runtime publication APIs directly.
The public map/replay facades and shared map UI are scanned too. Normal game sources cannot construct the embedded Map editor outside the exact diagnostic/native-facade entry points, restore full ReplayControlsView or StudioWindow, or bring back editor-only UiSurface sizing. Legacy Studio forwarding must precede game-file setup. Adapter exceptions
must identify an exact file, prohibited symbol and migration reason;
they must not permit Studio UI to acquire game shell or live session ownership.
The retained legacy Map adapter exists only for MapViewportCheck, UiCapture and -mapstudioshot diagnostics. Normal routes launch the independent application. Runtime tests separately exercise the canonical private player and deterministic fixtures; a source scan alone does not demonstrate playback.
