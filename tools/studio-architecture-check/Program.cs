using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Buffers.Binary;
using System.Text;
using ProjectPrime.Studio.Protocol;

string[] forbiddenOwners =
[
    "Shell", "StartScreen", "PrimeRoute", "PrimeShell", "UiSurface", "UiTopLevel",
    "LobbyScreen", "NetSession", "NetHostSession", "RenderWindow", "PrimeOverlayHost",
    "DemoPlayback", "ReplayController", "NetConfig", "TcpListener", "HttpListener",
    "LegacyMapStudioHostServices", "HunterLicenseClient", "SupabaseClient",
    "GameStudioBroker", "GameStudioIntegration"
];
string[] forbiddenPublication =
[
    "MapPackageInstaller", "MapRuntimeUsage", "CustomRooms", "RoomPrewarm"
];
var errors = new List<string>();
int checks = 0;

void Require(bool success, string message)
{
    checks++;
    if (!success) errors.Add(message);
}

// Exercise the scanner with real positive and negative cases. Comments and
// displayed diagnostics may name legacy owners; C# identifiers may not.
Require(Identifiers("Shell.Run();").Contains("Shell"), "scanner misses a direct shell dependency");
Require(Identifiers("using Legacy = MphRead.Mods.Network.NetSession;").Contains("NetSession"),
    "scanner misses an aliased gameplay owner");
Require(Identifiers("// Shell\n/* NetSession */ var text = \"UiSurface\"; var c = 'x';")
    .All(token => !forbiddenOwners.Contains(token)), "scanner treats comments or string literals as dependencies");
Require(Identifiers("var x = @Shell;\nvar text = @\"NetSession\"; var n = 1;").Contains("Shell"),
    "scanner misses an escaped identifier");
Require(Identifiers("var x = $\"display: {NetSession.Active}\";").Contains("NetSession"),
    "scanner misses a gameplay owner inside string interpolation");
Require(Identifiers("var x = $$\"\"\"display: {{Shell.Run()}}\"\"\";").Contains("Shell"),
    "scanner misses a shell owner inside raw string interpolation");
Require(!Identifiers("namespace ProjectPrime.Studio.Shell; using ProjectPrime.Studio.Shell;").Contains("Shell"),
    "scanner mistakes the Studio shell namespace for the game Shell type");
Require(Identifiers("using MphRead.Mods.Launcher.Gui; Shell.Run();").Contains("Shell"),
    "scanner permits the game Shell type through an imported namespace");
Require(Identifiers("var x = $\"{1}\"; NetSession.Active; var y = \"label\";").Contains("NetSession"),
    "scanner loses code following an interpolated string");
Require(!Identifiers("var x = \"\"\"raw \"Shell\" display\"\"\";").Contains("Shell"),
    "scanner mistakes a raw string literal for code");
Require(ReferencesCreator("../ProjectPrime.Studio/ProjectPrime.Studio.csproj")
    && ReferencesCreator("..\\ProjectPrime.Studio\\ProjectPrime.Studio.csproj")
    && !ReferencesCreator("../ProjectPrime.Studio.Protocol/ProjectPrime.Studio.Protocol.csproj"),
    "project boundary cannot distinguish creator and shared protocol references");
Require(Identifiers("using MphRead.Mods.MapEditor; MapDocument document;")
    .All(token => !forbiddenOwners.Contains(token)), "scanner blocks authoritative shared engine models");
Require(Identifiers("var text = $\"{Get(\"label\") + Shell.Run()}\";").Contains("Shell"),
    "scanner loses an owner following a quoted interpolation expression");
Require(HasRuntimePublication("MapBuildScheduler /* ownership */ . Publish(result);"),
    "publication scanner misses an invocation separated by a comment");
Require(HasRuntimePublication("using Builder = MphRead.Mods.MapGen.MapBuildScheduler; Builder.Install(result);"),
    "publication scanner misses an aliased installer");
Require(HasRuntimePublication("using static MphRead.Mods.MapGen.MapBuildScheduler; Publish(result);"),
    "publication scanner misses a statically imported publisher");
Require(HasRuntimePublication("var publisher = MapBuildScheduler.Publish;"),
    "publication scanner misses a publishing method group");
Require(!HasRuntimePublication("// MapBuildScheduler.Publish(result)\nvar label = \"MapBuildScheduler.Install\";"),
    "publication scanner treats explanatory comments or labels as runtime writes");
Require(HasCallsInOrder("CustomRooms.GenerateMissing(room); /* handoff */ using var read = MapRuntimeUsage.AcquirePreparation(room); CustomRooms.WhyUnplayable(room); AssetPaths(room);",
    "CustomRooms.GenerateMissing", "MapRuntimeUsage.AcquirePreparation", "CustomRooms.WhyUnplayable", "AssetPaths"),
    "publication contract rejects generation followed by validated shared preparation");
Require(!HasCallsInOrder("using var read = MapRuntimeUsage.AcquirePreparation(room); CustomRooms.GenerateMissing(room); CustomRooms.WhyUnplayable(room); AssetPaths(room);",
    "CustomRooms.GenerateMissing", "MapRuntimeUsage.AcquirePreparation", "CustomRooms.WhyUnplayable", "AssetPaths"),
    "publication contract permits generating under its own shared reader lease");
Require(!HasCallsInOrder("CustomRooms.GenerateMissing(room); using var read = MapRuntimeUsage.AcquirePreparation(room); AssetPaths(room);",
    "CustomRooms.GenerateMissing", "MapRuntimeUsage.AcquirePreparation", "CustomRooms.WhyUnplayable", "AssetPaths"),
    "publication contract permits runtime consumption without validating the writer-reader handoff");
Require(!HasCallsInOrder("// MapRuntimePublication.Acquire(def, outputs);\nInstallOwned(result);",
    "MapRuntimePublication.Acquire", "InstallOwned"), "publication contract accepts a commented-out writer lease");
Require(ConstructsType("new MapStudioScreen();", "MapStudioScreen"), "route scanner misses a direct embedded editor construction");
Require(ConstructsType("using Editor = MphRead.Mods.Launcher.Gui.MapStudioScreen; new Editor();", "MapStudioScreen"),
    "route scanner misses an aliased embedded editor construction");
Require(ConstructsType("new global::MphRead.Mods.Launcher.Gui.@MapStudioScreen();", "MapStudioScreen"),
    "route scanner misses a qualified or escaped embedded editor construction");
Require(!ConstructsType("// new MapStudioScreen();\nvar label = \"new MapStudioScreen()\";", "MapStudioScreen"),
    "route scanner mistakes a diagnostic label for an embedded editor construction");
Require(HasCallsInOrder("if (ModEntry.TryHandleHeadless(args)) return; if (CheckSetup(args)) return;",
    "ModEntry.TryHandleHeadless", "CheckSetup"), "early dispatch contract rejects asset-free Studio forwarding");
Require(!HasCallsInOrder("if (CheckSetup(args)) return; if (ModEntry.TryHandleHeadless(args)) return;",
    "ModEntry.TryHandleHeadless", "CheckSetup"), "early dispatch contract permits game setup before Studio forwarding");
string linkFixture = Path.Combine(Path.GetTempPath(), "studio-source-contract");
string protocolFixture = Path.Combine(linkFixture, "src", "ProjectPrime.Studio.Protocol", "ProjectPrime.Studio.Protocol.csproj");
Require(IsSharedProtocolSource(linkFixture, protocolFixture, "../MphRead/Mods/Update/DesktopInstallationIdentity.cs"),
    "source contract rejects the exact shared BCL installation identity helper");
Require(IsSharedProtocolSource(linkFixture, protocolFixture, "..\\MphRead\\Mods\\Update\\DesktopInstallationIdentity.cs"),
    "source contract rejects the exact shared BCL helper using Windows separators");
Require(!IsSharedProtocolSource(linkFixture, protocolFixture, "../MphRead/Mods/Update/InstallationLifetime.cs"),
    "source contract permits another engine file through the BCL helper exception");
Require(!IsSharedProtocolSource(linkFixture, protocolFixture, "../MphRead/Mods/StudioIntegration/GameStudioBroker.cs"),
    "source contract permits a gameplay broker through the BCL helper exception");
Require(!IsSharedProtocolSource(linkFixture, Path.Combine(linkFixture, "src", "ProjectPrime.Studio", "ProjectPrime.Studio.csproj"),
    "../MphRead/Mods/Update/DesktopInstallationIdentity.cs"), "source contract grants the Protocol-only link to Studio");
Require(!IsSharedProtocolSource(linkFixture, protocolFixture, "$(EngineRoot)/Mods/Update/DesktopInstallationIdentity.cs"),
    "source contract permits an unevaluated shared source path");
Require(!IsStudioEngineProjection(XDocument.Parse("<Project><Import Project='MphRead.csproj'/></Project>")),
    "Studio engine projection accepts a canonical import without isolated output and mode flags");
Require(!IsStudioEngineProjection(XDocument.Parse("<Project><Import Project='../other/engine.csproj'/></Project>")),
    "Studio engine projection accepts an unrelated engine import");

string androidCreatorRemoval = "../MphRead/Mods/Launcher/Gui/MapStudio*.cs;../MphRead/Mods/Launcher/Gui/MapViewport*.cs;../MphRead/AvaloniaShared/IMapStudioHostServices.cs;../MphRead/AvaloniaShared/AvaloniaMapStudioHost.cs";
XDocument AndroidFixture(string removal, string suffix = "") => XDocument.Parse(
    $"<Project><ItemGroup><Compile Include='../MphRead/**/*.cs'/><Compile Remove='{removal}'/>{suffix}</ItemGroup></Project>");
Require(ExcludesAndroidCreatorPresentation(AndroidFixture(androidCreatorRemoval)),
    "Android contract rejects canonical runtime inclusion followed by explicit creator presentation removal");
Require(ExcludesAndroidCreatorPresentation(AndroidFixture(androidCreatorRemoval.Replace('/', '\\'))),
    "Android contract rejects Windows-separated creator presentation removal");
Require(!ExcludesAndroidCreatorPresentation(AndroidFixture(androidCreatorRemoval.Replace("../MphRead/Mods/Launcher/Gui/MapViewport*.cs;", ""))),
    "Android contract permits creator viewports when only editor panels are removed");
Require(!ExcludesAndroidCreatorPresentation(AndroidFixture("../MphRead/Mods/MapEditor/**/*.cs")),
    "Android contract mistakes removal of shared canonical runtime models for creator presentation exclusion");
Require(!ExcludesAndroidCreatorPresentation(XDocument.Parse($"<Project><!-- <Compile Remove='{androidCreatorRemoval}'/> --></Project>")),
    "Android contract accepts a commented-out creator presentation removal");
Require(!ExcludesAndroidCreatorPresentation(AndroidFixture(androidCreatorRemoval,
    "<Compile Include='../MphRead/Mods/Launcher/Gui/MapStudioScreen.cs'/>")),
    "Android contract permits a later source include to restore excluded creator presentation");

Require(OmitsAndroidMapCapture("#if !ANDROID\nyield return (\"map-editor\", new MapStudioScreen(), size);\n#endif"),
    "Android capture guard rejects a desktop-only canonical diagnostic");
Require(!OmitsAndroidMapCapture("yield return (\"map-editor\", new MapStudioScreen(), size);"),
    "Android capture guard permits an unconditional creator diagnostic");
Require(!OmitsAndroidMapCapture("// #if !ANDROID\nnew MapStudioScreen();\n// #endif"),
    "Android capture guard accepts a commented-out preprocessor guard");
Require(!OmitsAndroidMapCapture("#if !ANDROID\nnew MapStudioScreen();\n#endif\nnew MapStudioScreen();"),
    "Android capture guard permits an additional unguarded creator diagnostic");

// Exercise the actual framework-only protocol validators. A lexical graph check
// cannot establish exact identity, path, message bounds or authentication meaning.
var identity = new StudioMapIdentity(Guid.NewGuid(), "creator_test", new string('a', 64), new string('b', 64));
string package = Path.Combine(Path.GetTempPath(), "studio-contract.ppmap");
var install = new StudioGameRequest(Guid.NewGuid(), StudioGameCommand.InstallMapPackage, package, identity);
Require(install.Validate() == null, "protocol rejects a valid exact-identity immutable package request");
Require((identity with { MapId = Guid.Empty }).Validate() != null, "protocol permits an empty map identity");
Require((identity with { PackageHash = "name-only" }).Validate() != null, "protocol permits a package without an exact hash");
Require((identity with { ContentHash = new string('z', 64) }).Validate() != null, "protocol permits a nonhexadecimal content hash");
Require((identity with { RoomKey = "../room" }).Validate() != null, "protocol permits a traversal room identity");
Require((install with { RequestId = Guid.Empty }).Validate() != null, "protocol permits a missing request identity");
Require((install with { Command = (StudioGameCommand)999 }).Validate() != null, "protocol permits an unknown game command");
Require((install with { Identity = null }).Validate() != null, "protocol permits name-only installation");
Require((install with { PackagePath = "relative.ppmap" }).Validate() != null, "protocol permits a relative package path");
Require((install with { PackagePath = package + ".json" }).Validate() != null, "protocol permits a nonpackage source");
Require((install with { StudioVersion = "bad/version" }).Validate() != null, "protocol permits an unbounded or malformed application version");
Require((install with { Options = new(Bots: 8) }).Validate() != null, "protocol permits invalid game options");
Require((install with { Options = new(CommunityAddress: "https://user:secret@example.test") }).Validate() != null,
    "protocol permits credentials in a Community service address");
Require(new StudioGameRequest(Guid.NewGuid(), StudioGameCommand.PlaytestStatus).Validate() != null,
    "protocol permits playtest status without an instance identity");
Require(new StudioOpenRequest(Guid.NewGuid(), StudioOpenKind.Home, package).Validate() != null,
    "protocol permits a document path in an asset-free home request");
string secret = StudioIpcAuthentication.NewSecret(), nonce = StudioIpcAuthentication.NewNonce();
string proof = StudioIpcAuthentication.CreateProof(secret, nonce, "client", StudioProtocol.StudioIpcVersion);
Require(StudioIpcAuthentication.VerifyProof(secret, nonce, "client", StudioProtocol.StudioIpcVersion, proof),
    "protocol rejects a valid local capability proof");
Require(!StudioIpcAuthentication.VerifyProof(secret, nonce, "server", StudioProtocol.StudioIpcVersion, proof),
    "protocol permits a reflected client proof on the server side");
Require(!StudioIpcAuthentication.VerifyProof(secret, nonce, "client", StudioProtocol.StudioIpcVersion + 1, proof),
    "protocol authentication does not bind the IPC version");
Require(!StudioIpcAuthentication.VerifyProof(secret, StudioIpcAuthentication.NewNonce(), "client", StudioProtocol.StudioIpcVersion, proof),
    "protocol authentication permits a replayed proof for another challenge");
Require(await RejectFrame(-1, []), "protocol framing permits a negative length");
Require(await RejectFrame(StudioProtocol.MaximumFrameBytes + 1, []), "protocol framing permits an oversized control message");
byte[] malformed = Encoding.UTF8.GetBytes("{\"version\":1,\"type\":\"request\",\"unexpected\":true}");
Require(await RejectFrame(malformed.Length, malformed), "protocol framing permits unknown payload fields");

if (!args.Contains("--self-test"))
{
    string? explicitRoot = args.FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
    string root = explicitRoot == null ? FindRoot() : Path.GetFullPath(explicitRoot);
    string studioDirectory = Path.Combine(root, "src", "ProjectPrime.Studio");
    string studioProject = Path.Combine(studioDirectory, "ProjectPrime.Studio.csproj");
    string protocolDirectory = Path.Combine(root, "src", "ProjectPrime.Studio.Protocol");
    Require(File.Exists(studioProject), "src/ProjectPrime.Studio/ProjectPrime.Studio.csproj is missing");

    foreach (string directory in new[] { studioDirectory, protocolDirectory }.Where(Directory.Exists))
    {
        foreach (string file in Sources(directory, "*.cs"))
        {
            string source = File.ReadAllText(file);
            var identifiers = Identifiers(source);
            string relative = Path.GetRelativePath(root, file);
            foreach (string owner in forbiddenOwners.Concat(forbiddenPublication))
                Require(!identifiers.Contains(owner), $"{relative} directly references prohibited owner {owner}");
            if (directory == protocolDirectory)
                Require(!identifiers.Contains("MphRead") && !identifiers.Contains("Avalonia"),
                    $"{relative} couples the protocol to engine or presentation implementation");
            Require(!HasRuntimePublication(source),
                $"{relative} publishes into game runtime directories; send a package to the game broker instead");
            Require(!identifiers.Contains("ANDROID"), $"{relative} includes Android-specific Studio code");
            Require(!Regex.IsMatch(source, @"\b(?:access_token|refresh_token)\b", RegexOptions.IgnoreCase),
                $"{relative} includes an application credential in the Studio boundary");
        }
        foreach (string file in Sources(directory, "*.axaml"))
        {
            var identifiers = Regex.Matches(File.ReadAllText(file), @"\b[A-Za-z_][A-Za-z0-9_]*\b")
                .Select(match => match.Value).ToHashSet(StringComparer.Ordinal);
            foreach (string owner in forbiddenOwners)
                Require(!identifiers.Contains(owner), $"{Path.GetRelativePath(root, file)} embeds prohibited owner {owner}");
        }
        foreach (string project in Sources(directory, "*.csproj")) CheckCreatorProject(root, project);
    }

    // These are the engine-side files exposed by the new standalone hosts.
    // Existing game diagnostics/quick viewers elsewhere remain outside this
    // boundary. A filename exception never grants permission to Studio UI.
    var engineSources = new List<string>();
    foreach (string directory in new[] { "AvaloniaShared", "Mods/StudioReplay", "Mods/StudioRendering" })
    {
        string full = Path.Combine(root, "src", "MphRead", directory);
        if (Directory.Exists(full)) engineSources.AddRange(Sources(full, "*.cs"));
    }
    string rendererExtensions = Path.Combine(root, "src", "MphRead", "Mods", "Render");
    if (Directory.Exists(rendererExtensions)) engineSources.AddRange(Directory.EnumerateFiles(rendererExtensions, "*.Studio.cs"));
    string sharedMapUi = Path.Combine(root, "src", "MphRead", "Mods", "Launcher", "Gui");
    if (Directory.Exists(sharedMapUi))
    {
        engineSources.AddRange(Directory.EnumerateFiles(sharedMapUi, "MapStudio*.cs"));
        engineSources.AddRange(Directory.EnumerateFiles(sharedMapUi, "MapViewport*.cs")
            .Where(file => Path.GetFileName(file) != "MapViewportCheck.cs"));
    }
    foreach (string file in engineSources.Distinct())
    {
        string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
        string source = File.ReadAllText(file);
        var identifiers = Identifiers(source);
        string[] exceptions = relative switch
        {
            // Diagnostic compatibility only: MapViewportCheck, UiCapture and
            // -mapstudioshot retain the original embedded presentation oracle.
            // Normal game creator routes launch the independent application.
            "src/MphRead/Mods/Launcher/Gui/MapStudioLegacyHost.cs" =>
                ["Shell", "PrimeOverlayHost", "LegacyMapStudioHostServices", "CustomRooms", "MapPackageInstaller", "MapRuntimeUsage", "HunterLicenseClient"],
            // Existing embedded GPU presentation only. The standalone renderer
            // owns its native surface and never calls these UiSurface methods.
            "src/MphRead/Mods/Launcher/Gui/MapViewportGpu.cs" => ["UiSurface"],
            _ => []
        };
        foreach (string owner in forbiddenOwners)
            Require(exceptions.Contains(owner) || !identifiers.Contains(owner),
                $"{relative} exposes prohibited game owner {owner} through the standalone engine boundary");
        bool sharedAuthoring = relative.StartsWith("src/MphRead/Mods/Launcher/Gui/MapStudio", StringComparison.Ordinal);
        if (sharedAuthoring)
            foreach (string owner in forbiddenPublication)
                Require(exceptions.Contains(owner) || !identifiers.Contains(owner),
                    $"{relative} accesses game runtime publication owner {owner}; inject host services instead");
        bool privateMapRuntime = relative == "src/MphRead/AvaloniaShared/StudioPrivateMapRuntime.cs";
        Require(relative.EndsWith("/MapStudioLegacyHost.cs", StringComparison.Ordinal) || privateMapRuntime || !HasRuntimePublication(source),
            $"{relative} publishes game runtime outputs outside the explicit legacy game adapter");
        if (privateMapRuntime)
        {
            foreach (string isolation in new[] { "_canonicalRoot", "CanonicalizeRuntimeDirectory", "RequirePrivateDestinations", "Paths", "FileSystem", "ThrowIfCancellationRequested" })
                Require(identifiers.Contains(isolation), $"StudioPrivateMapRuntime.cs lost its private-output ownership guard: {isolation}");
            Require(Regex.Matches(Code(source), @"\bRequirePrivateDestinations\s*\(").Count >= 3,
                "StudioPrivateMapRuntime.cs must validate private destinations at construction and immediately before publication");
            Require(!Regex.IsMatch(Code(source), @"\bMapBuildScheduler\s*\.\s*(?:Install|StageInstallation)\b"),
                "StudioPrivateMapRuntime.cs cannot use a low-level game installation API");
        }
        if (relative.EndsWith("/MapStudioLegacyHost.cs", StringComparison.Ordinal))
            foreach (Match access in Regex.Matches(Code(source), @"\bHunterLicenseClient\s*\.\s*([A-Za-z_][A-Za-z0-9_]*)"))
                Require(access.Groups[1].Value is "GetCommunityMapTicketAsync" or "RefreshCommunityMapTicketAsync",
                    $"{relative} reads a broad account API instead of a narrow Community ticket: {access.Groups[1].Value}");
        if (relative == "src/MphRead/AvaloniaShared/StudioGameAssets.cs")
            foreach (Match access in Regex.Matches(Code(source), @"\bCustomRooms\s*\.\s*([A-Za-z_][A-Za-z0-9_]*)"))
                Require(access.Groups[1].Value == "DeferInitialRegistration",
                    $"{relative} accesses global custom-map state instead of only deferring static registration: {access.Groups[1].Value}");
    }

    // The old renderer remains an independent diagnostic oracle. Normal game
    // routes must not reintroduce its embedded editor or the full replay UI.
    string engineDirectory = Path.Combine(root, "src", "MphRead");
    if (Directory.Exists(engineDirectory))
        foreach (string file in Sources(engineDirectory, "*.cs"))
        {
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            string source = File.ReadAllText(file);
            var identifiers = Identifiers(source);
            Require(!identifiers.Contains("ReplayControlsView"), $"{relative} restores the removed full in-game Replay Studio editor");
            Require(!identifiers.Contains("StudioWindow"), $"{relative} restores the game-owned standalone editor window");
            if (relative == "src/MphRead/Mods/Launcher/Gui/UiCapture.cs")
                Require(OmitsAndroidMapCapture(source), "UiCapture must omit its Map creator diagnostic from Android while retaining general captures");
            bool diagnosticOrFacade = relative is "src/MphRead/Mods/Launcher/Gui/MapViewportCheck.cs"
                or "src/MphRead/Mods/Launcher/Gui/UiCapture.cs"
                or "src/MphRead/AvaloniaShared/AvaloniaMapStudioHost.cs";
            string code = Code(source);
            if (relative == "src/MphRead/Mods/Launcher/Gui/MapStudioScreen.cs")
            {
                string capture = MethodBody(source, "internal static int Capture");
                if (capture.Length != 0) code = code.Replace(capture, "", StringComparison.Ordinal);
            }
            Require(diagnosticOrFacade || !ConstructsType(code, "MapStudioScreen"),
                $"{relative} constructs the embedded Map Studio outside its retained diagnostics or injected standalone facade");
            Require(relative == "src/MphRead/Mods/Launcher/Gui/MapStudioLegacyHost.cs"
                || !ConstructsType(source, "LegacyMapStudioHostServices"),
                $"{relative} constructs diagnostic game services outside the exact compatibility adapter");
        }
    string surfacePath = Path.Combine(engineDirectory, "Mods", "Launcher", "Gui", "UiSurface.cs");
    if (File.Exists(surfacePath))
        foreach (string owner in new[] { "MapStudioScreen", "MapStudioState", "EditorFactor", "StudioWindow" })
            Require(!Identifiers(File.ReadAllText(surfacePath)).Contains(owner),
                $"UiSurface restores editor-specific application sizing/ownership: {owner}");
    string programPath = Path.Combine(engineDirectory, "Program.cs");
    if (File.Exists(programPath))
        Require(HasCallsInOrder(MethodBody(File.ReadAllText(programPath), "private static void Run"),
            "ModEntry.TryHandleHeadless", "CheckSetup"), "Studio legacy forwarding must dispatch before ordinary game-file setup");
    string entryPath = Path.Combine(engineDirectory, "Mods", "ModEntry.cs");
    if (File.Exists(entryPath))
        Require(HasCallsInOrder(MethodBody(File.ReadAllText(entryPath), "public static bool TryHandleHeadless"),
            "StudioApplicationLauncher.TryOpen", "SettingsArchiveCommand.Run"),
            "The early mod dispatcher must retain the external Studio launcher before ordinary commands");

    // This privileged adapter belongs only to the game. Its existence is not
    // permission for a standalone host to instantiate it in the Studio process.
    string brokerPath = Path.Combine(root, "src", "MphRead", "Mods", "StudioIntegration", "GameStudioBroker.cs");
    if (File.Exists(brokerPath))
    {
        string source = File.ReadAllText(brokerPath);
        var identifiers = Identifiers(source);
        foreach (string ownership in new[] { "_dispatch", "PrepareAsync", "Commit", "HasExact", "Matches", "CancellationToken" })
            Require(identifiers.Contains(ownership), $"GameStudioBroker.cs lost its publication ownership contract: {ownership}");
        Require(source.Contains("ppm1.", StringComparison.Ordinal), "GameStudioBroker.cs must validate a narrow Community ticket before IPC");
        Require(!Regex.IsMatch(source, @"\b(?:access_token|refresh_token)\b", RegexOptions.IgnoreCase),
            "GameStudioBroker.cs must never return account access or refresh credentials");
    }
    // CLI/startup generation is privileged too: the broker cannot be the only
    // writer which observes another process's live scene. These source contracts
    // complement the actual independent-process map-generation regression.
    string schedulerPath = Path.Combine(root, "src", "MphRead", "Mods", "MapGen", "Build", "MapBuildScheduler.cs");
    if (File.Exists(schedulerPath))
    {
        string source = File.ReadAllText(schedulerPath);
        foreach (string method in new[] { "Publish", "Install" })
            Require(HasCallsInOrder(MethodBody(source, "public static void " + method), "MapRuntimePublication.Acquire", "InstallOwned"),
                $"MapBuildScheduler.{method} must acquire the destination's live-reader publication fence before installing");
        Require(Regex.IsMatch(Code(source), @"\bprivate\s+static\s+void\s+InstallOwned\s*\("),
            "MapBuildScheduler.InstallOwned must remain private to the already-fenced entry points");
    }
    string packerPath = Path.Combine(root, "src", "MphRead", "Mods", "MapGen", "MapPacker.cs");
    if (File.Exists(packerPath))
        Require(HasCallsInOrder(File.ReadAllText(packerPath), "MapRuntimePublication.Acquire", "Directory.CreateDirectory", "File.Delete"),
            "Raw MapPacker output commits must acquire the game runtime fence before changing destination files");
    string prewarmPath = Path.Combine(root, "src", "MphRead", "Mods", "RoomPrewarm.cs");
    if (File.Exists(prewarmPath))
        Require(HasCallsInOrder(File.ReadAllText(prewarmPath), "CustomRooms.GenerateMissing", "MapRuntimeUsage.AcquirePreparation", "CustomRooms.WhyUnplayable", "AssetPaths"),
            "RoomPrewarm must generate before its reader lease and validate exact outputs under that lease before loading");
    string nativeMapPath = Path.Combine(root, "src", "MphRead", "Mods", "StudioRendering", "ViewportRenderGraph.Native.cs");
    if (File.Exists(nativeMapPath))
    {
        var identifiers = Identifiers(File.ReadAllText(nativeMapPath));
        Require(identifiers.Contains("Present") && identifiers.Contains("StudioNativeSurface"),
            "The native map graph must present to a Studio-owned graphics surface");
        foreach (string readback in new[] { "CommandEncoderCopyTextureToBuffer", "BufferMapAsync", "ReadSceneTarget", "ReadPixels" })
            Require(!identifiers.Contains(readback), $"The normal native map presentation path must not perform GPU readback: {readback}");
    }

    if (File.Exists(studioProject))
    {
        var project = XDocument.Load(studioProject);
        Require(project.Descendants("TargetFramework").Any(element => element.Value.Trim() == "net10.0"),
            "Studio must target desktop net10.0");
        Require(project.Descendants("AssemblyName").Any(element => element.Value.Trim() == "ProjectPrimeStudio"),
            "Studio executable assembly must be named ProjectPrimeStudio");
        Require(project.Descendants("Product").Any(element => element.Value.Trim() == "Project Prime Studio"),
            "Studio product must be named Project Prime Studio");
        Require(project.Descendants("OutputType").Any(element => element.Value.Trim() == "WinExe"
            && ((string?)element.Attribute("Condition") ?? "").Contains("win", StringComparison.OrdinalIgnoreCase)),
            "Studio must select the WinExe subsystem for Windows");
        Require(project.Descendants("PackageReference").Any(element => (string?)element.Attribute("Include") == "Avalonia.Desktop"),
            "Studio must own a normal Avalonia Desktop application");
        string combined = string.Join('\n', Sources(studioDirectory, "*.cs").Select(File.ReadAllText));
        Require(Identifiers(combined).Contains("StartWithClassicDesktopLifetime"),
            "Studio must start with Avalonia's desktop application lifetime");
    }

    // The protocol project may be shared by the game. The creator executable
    // and its presentation files must never become a game/Android dependency.
    foreach (string gameDirectory in new[] { "MphRead", "MphRead.Android" })
    {
        string directory = Path.Combine(root, "src", gameDirectory);
        if (!Directory.Exists(directory)) continue;
        foreach (string project in Sources(directory, "*.csproj"))
        {
            var document = XDocument.Load(project);
            if (gameDirectory == "MphRead.Android")
                Require(ExcludesAndroidCreatorPresentation(document),
                    $"{Path.GetRelativePath(root, project)} must exclude desktop Map panels/viewports and host adapters");
            foreach (var element in document.Descendants().Where(element =>
                element.Name.LocalName is "ProjectReference" or "Reference" or "Compile"))
            {
                string value = (string?)element.Attribute("Include") ?? "";
                Require(!ReferencesCreator(value), $"{Path.GetRelativePath(root, project)} depends on Studio presentation: {value}");
            }
        }
    }

    void CheckCreatorProject(string repository, string projectPath)
    {
        var document = XDocument.Load(projectPath);
        string relative = Path.GetRelativePath(repository, projectPath);
        bool protocol = Path.GetFileName(projectPath) == "ProjectPrime.Studio.Protocol.csproj";
        if (protocol)
        {
            Require(!document.Descendants("ProjectReference").Any(), "The Studio protocol cannot depend on another project");
            Require(!document.Descendants("PackageReference").Any(), "The Studio protocol must remain framework-only");
            var identityLinks = document.Descendants("Compile").Where(element =>
                IsSharedProtocolSource(repository, projectPath, (string?)element.Attribute("Include") ?? "")).ToArray();
            Require(identityLinks.Length == 1, "The Studio protocol must compile the one canonical BCL installation identity helper");
            foreach (var link in identityLinks)
                Require(link.Attribute("Condition") == null && (string?)link.Attribute("Link") == "DesktopInstallationIdentity.cs",
                    "The shared BCL installation identity link must be explicit and unconditional");
            string identityPath = Path.Combine(repository, "src", "MphRead", "Mods", "Update", "DesktopInstallationIdentity.cs");
            Require(File.Exists(identityPath), "The canonical shared BCL installation identity helper is missing");
            if (File.Exists(identityPath))
            {
                string source = File.ReadAllText(identityPath), code = Code(source);
                var identifiers = Identifiers(source);
                foreach (string owner in forbiddenOwners.Concat(forbiddenPublication).Concat(["MphRead", "Avalonia"]))
                    Require(!identifiers.Contains(owner), $"The shared installation identity helper references prohibited owner {owner}");
                Require(Regex.IsMatch(code, @"\bnamespace\s+ProjectPrime\s*\.\s*DesktopShared\s*;")
                    && Regex.IsMatch(code, @"\binternal\s+static\s+class\s+DesktopInstallationIdentity\b"),
                    "The shared installation identity helper must remain internal in the desktop BCL namespace");
                foreach (Match import in Regex.Matches(code, @"\busing\s+([A-Za-z_][A-Za-z0-9_.]*)\s*;"))
                    Require(import.Groups[1].Value is "System" or "System.IO",
                        $"The shared installation identity helper imports outside its BCL path scope: {import.Groups[1].Value}");
            }
        }
        foreach (var framework in document.Descendants().Where(element =>
            element.Name.LocalName is "TargetFramework" or "TargetFrameworks" or "RuntimeIdentifier" or "RuntimeIdentifiers"))
            Require(!framework.Value.Contains("android", StringComparison.OrdinalIgnoreCase),
                $"{relative} targets Android; Studio is desktop only");
        foreach (var reference in document.Descendants("ProjectReference"))
        {
            string include = (string?)reference.Attribute("Include") ?? "";
            string normalized = include.Replace('\\', Path.DirectorySeparatorChar);
            string target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectPath)!, normalized));
            string canonicalProtocol = Path.Combine(repository, "src", "ProjectPrime.Studio.Protocol", "ProjectPrime.Studio.Protocol.csproj");
            string canonicalEngine = Path.Combine(repository, "src", "MphRead", "MphRead.csproj");
            string studioEngine = Path.Combine(repository, "src", "MphRead", "MphRead.StudioEngine.csproj");
            // Studio's isolated output/restore projection imports the same canonical
            // engine sources. Accept only that exact project and verify its flags,
            // imports and boundary; do not permit an arbitrary engine fork.
            bool verifiedProjection = target == studioEngine && File.Exists(studioEngine)
                && IsStudioEngineProjection(XDocument.Load(studioEngine));
            Require(!protocol && (target == canonicalProtocol || target == canonicalEngine || verifiedProjection),
                $"{relative} references neither the canonical engine, verified Studio engine projection nor Studio protocol: {include}");
        }
        foreach (var reference in document.Descendants("Reference"))
        {
            string include = (string?)reference.Attribute("Include") ?? "";
            Require(!Regex.IsMatch(include, @"(?:MphRead|ProjectPrime(?:,|$))", RegexOptions.IgnoreCase),
                $"{relative} references a game binary outside the canonical project graph: {include}");
        }
        foreach (var package in document.Descendants("PackageReference"))
        {
            string include = (string?)package.Attribute("Include") ?? "";
            Require(!include.Contains("Android", StringComparison.OrdinalIgnoreCase)
                && !include.Contains("MphRead", StringComparison.OrdinalIgnoreCase)
                && !include.Equals("ProjectPrime", StringComparison.OrdinalIgnoreCase),
                $"{relative} includes a game or Android package: {include}");
        }
        foreach (var compile in document.Descendants("Compile").Where(element => element.Attribute("Include") != null))
        {
            string include = compile.Attribute("Include")!.Value.Replace('\\', Path.DirectorySeparatorChar);
            Require(!include.Contains("$", StringComparison.Ordinal), $"{relative} has an unevaluated compile link: {include}");
            string full = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectPath)!, include));
            Require(full.StartsWith(Path.GetDirectoryName(projectPath)! + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || IsSharedProtocolSource(repository, projectPath, include),
                $"{relative} links source from outside its own boundary: {include}");
        }
    }
}

foreach (string error in errors) Console.Error.WriteLine("STUDIO ARCHITECTURE FAIL: " + error);
Console.WriteLine($"Studio architecture: {checks - errors.Count}/{checks} contracts passed.");
return errors.Count == 0 ? 0 : 1;

static async Task<bool> RejectFrame(int count, byte[] body)
{
    using var stream = new MemoryStream();
    byte[] prefix = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(prefix, count);
    stream.Write(prefix); stream.Write(body); stream.Position = 0;
    try { await StudioIpcFraming.ReadAsync(stream); return false; }
    catch (StudioProtocolException) { return true; }
}

// Standalone Studio must reuse the canonical engine project, but it needs
// separate obj/bin paths and cannot inherit client, RmlUi, or server modes.
static bool IsStudioEngineProjection(XDocument project)
{
    var imports = project.Descendants().Where(x => x.Name.LocalName == "Import").ToArray();
    if (imports.Length != 1 || (string?)imports[0].Attribute("Project") != "MphRead.csproj"
        || imports[0].Attribute("Condition") != null) return false;
    if (project.Descendants().Any(x => x.Name.LocalName is "ProjectReference" or "Reference"
        or "PackageReference" or "Compile")) return false;
    bool Fixed(string name, string value)
    {
        var properties = project.Descendants().Where(x => x.Name.LocalName == name).ToArray();
        return properties.Length == 1 && properties[0].Attribute("Condition") == null
            && properties[0].Value.Trim() == value;
    }
    return Fixed("BaseIntermediateOutputPath", "obj/StudioEngine/")
        && Fixed("BaseOutputPath", "bin/StudioEngine/")
        && Fixed("MphReadStudioEngine", "true")
        && Fixed("MphReadAvalonia", "true")
        && Fixed("MphReadRmlUi", "false")
        && Fixed("MphReadRmlUiPoc", "false")
        && Fixed("MphReadRmlUiEnabled", "false")
        && Fixed("MphReadNativeClient", "false")
        && Fixed("MphReadNativeDefault", "false")
        && Fixed("MphReadServer", "false");
}

static bool ReferencesCreator(string value)
{
    string normalized = value.Replace('\\', '/');
    return normalized.Contains("ProjectPrime.Studio/", StringComparison.OrdinalIgnoreCase)
        || normalized.Contains("ProjectPrime.Studio.csproj", StringComparison.OrdinalIgnoreCase)
        || normalized.Contains("ProjectPrimeStudio", StringComparison.OrdinalIgnoreCase);
}

static bool IsSharedProtocolSource(string repository, string project, string include)
{
    if (include.Contains('$') || include.Contains('*') || include.Contains('?') || string.IsNullOrWhiteSpace(include)) return false;
    string protocol = Path.Combine(repository, "src", "ProjectPrime.Studio.Protocol", "ProjectPrime.Studio.Protocol.csproj");
    if (Path.GetFullPath(project) != Path.GetFullPath(protocol)) return false;
    string target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, include.Replace('\\', Path.DirectorySeparatorChar)));
    return target == Path.GetFullPath(Path.Combine(repository, "src", "MphRead", "Mods", "Update", "DesktopInstallationIdentity.cs"));
}

static string FindRoot()
{
    foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        for (DirectoryInfo? directory = new(start); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "ARCHITECTURE-INVARIANTS.md"))) return directory.FullName;
    throw new DirectoryNotFoundException("Pass the repository root as the first argument.");
}

static IEnumerable<string> Sources(string directory, string pattern) => Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories)
    .Where(file => !Path.GetRelativePath(directory, file).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"));

static HashSet<string> Identifiers(string source) => Regex.Matches(Code(source), @"\b[A-Za-z_][A-Za-z0-9_]*\b")
    .Select(match => match.Value).ToHashSet(StringComparer.Ordinal);

static bool HasRuntimePublication(string source)
{
    string code = Code(source);
    var receivers = new List<string> { "MapBuildScheduler" };
    const string scheduler = @"(?:global\s*::\s*)?(?:[A-Za-z_][A-Za-z0-9_]*\s*\.\s*)*MapBuildScheduler";
    foreach (Match alias in Regex.Matches(code, @"\busing\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*" + scheduler + @"\s*;"))
        receivers.Add(alias.Groups[1].Value);
    const string methods = @"(?:Install|Publish|StageInstallation)\b";
    bool qualified = Regex.IsMatch(code, @"\b(?:" + string.Join('|', receivers.Select(Regex.Escape)) + @")\s*\.\s*" + methods);
    bool imported = Regex.IsMatch(code, @"\busing\s+static\s+" + scheduler + @"\s*;")
        && Regex.IsMatch(code, @"\b" + methods);
    return qualified || imported;
}

static bool ConstructsType(string source, string type)
{
    string code = Code(source);
    string qualified = @"(?:global\s*::\s*)?(?:@?[A-Za-z_][A-Za-z0-9_]*\s*\.\s*)*@?" + Regex.Escape(type);
    var names = new List<string> { "@?" + Regex.Escape(type) };
    foreach (Match alias in Regex.Matches(code, @"\busing\s+(@?[A-Za-z_][A-Za-z0-9_]*)\s*=\s*" + qualified + @"\s*;"))
        names.Add(Regex.Escape(alias.Groups[1].Value));
    return Regex.IsMatch(code, @"\bnew\s+(?:" + qualified + "|" + string.Join('|', names) + @")\s*\(");
}

static bool HasCallsInOrder(string source, params string[] calls)
{
    string code = Code(source); int next = 0;
    foreach (string call in calls)
    {
        string pattern = @"\b" + string.Join(@"\s*\.\s*", call.Split('.').Select(Regex.Escape)) + @"\s*\(";
        Match match = Regex.Match(code[next..], pattern);
        if (!match.Success) return false;
        next += match.Index + match.Length;
    }
    return true;
}

static string MethodBody(string source, string declaration)
{
    string code = Code(source);
    string pattern = @"\b" + string.Join(@"\s+", declaration.Split(' ').Select(Regex.Escape)) + @"\s*\(";
    Match method = Regex.Match(code, pattern);
    if (!method.Success) return "";
    int start = code.IndexOf('{', method.Index + method.Length);
    if (start < 0) return "";
    int depth = 1;
    for (int index = start + 1; index < code.Length; index++)
    {
        if (code[index] == '{') depth++;
        else if (code[index] == '}' && --depth == 0) return code[(start + 1)..index];
    }
    return "";
}

static string Code(string source)
{
    // Preserve interpolation contents conservatively: dependencies in an
    // interpolated expression must remain visible. Static text in interpolated
    // strings can produce a false positive, which callers fix by rephrasing it.
    // This is a source contract scanner, not a C# semantic/IL analyser.
    var code = new System.Text.StringBuilder(source.Length);
    for (int index = 0; index < source.Length;)
    {
        int start = index;
        if (source.AsSpan(index).StartsWith("//"))
        {
            while (index < source.Length && source[index] is not '\n' and not '\r') index++;
            code.Append(' ', index - start);
            continue;
        }
        if (source.AsSpan(index).StartsWith("/*"))
        {
            int end = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
            index = end < 0 ? source.Length : end + 2;
            code.Append(' ', index - start);
            continue;
        }
        int prefix = index;
        while (prefix < source.Length && source[prefix] is '$' or '@') prefix++;
        bool character = source[index] == '\'';
        bool quoted = prefix < source.Length && source[prefix] == '"';
        if (!character && !quoted) { code.Append(source[index++]); continue; }
        bool interpolated = source.AsSpan(index, prefix - index).Contains('$');
        bool verbatim = source.AsSpan(index, prefix - index).Contains('@');
        int quoteStart = character ? index : prefix;
        char quote = source[quoteStart];
        int count = 1;
        if (quoted) while (quoteStart + count < source.Length && source[quoteStart + count] == '"') count++;
        bool raw = count >= 3;
        index = quoteStart + (raw ? count : 1);
        if (raw)
        {
            string ending = new('"', count);
            int end = source.IndexOf(ending, index, StringComparison.Ordinal);
            index = end < 0 ? source.Length : end + count;
        }
        else
        {
            int interpolationDepth = 0;
            while (index < source.Length)
            {
                if (interpolated && source[index] == '{')
                {
                    if (interpolationDepth == 0 && index + 1 < source.Length && source[index + 1] == '{') index += 2;
                    else { interpolationDepth++; index++; }
                    continue;
                }
                if (interpolated && source[index] == '}')
                {
                    if (interpolationDepth > 0) interpolationDepth--;
                    else if (index + 1 < source.Length && source[index + 1] == '}') index++;
                    index++;
                    continue;
                }
                if (source[index] == quote)
                {
                    if (interpolationDepth > 0)
                    {
                        index++;
                        while (index < source.Length)
                        {
                            if (source[index] == '\\' && index + 1 < source.Length) { index += 2; continue; }
                            if (source[index++] == '"') break;
                        }
                        continue;
                    }
                    index++;
                    if (verbatim && index < source.Length && source[index] == quote) { index++; continue; }
                    break;
                }
                if (!verbatim && source[index] == '\\' && index + 1 < source.Length) index += 2;
                else index++;
            }
        }
        if (interpolated) code.Append(source, start, index - start);
        else code.Append(' ', index - start);
    }
    return Regex.Replace(code.ToString(), @"\bProjectPrime\s*\.\s*Studio\s*\.\s*Shell\b", "StudioCreatorShellNamespace");
}

// The Android head intentionally compiles shared canonical engine sources. Keep
// creator presentation out of that glob, and reject explicit later reintroduction.
static bool ExcludesAndroidCreatorPresentation(XDocument project)
{
    string[] required =
    [
        "../MphRead/Mods/Launcher/Gui/MapStudio*.cs",
        "../MphRead/Mods/Launcher/Gui/MapViewport*.cs",
        "../MphRead/AvaloniaShared/IMapStudioHostServices.cs",
        "../MphRead/AvaloniaShared/AvaloniaMapStudioHost.cs"
    ];
    var items = project.Descendants().Where(element => element.Name.LocalName == "Compile").ToArray();
    foreach (string pattern in required)
    {
        int removal = Array.FindLastIndex(items, item => ((string?)item.Attribute("Remove") ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(value => value.Replace('\\', '/') == pattern));
        if (removal < 0) return false;
        string example = pattern.Replace("MapStudio*.cs", "MapStudioScreen.cs").Replace("MapViewport*.cs", "MapViewport.cs");
        foreach (var item in items.Skip(removal + 1))
            foreach (string include in ((string?)item.Attribute("Include") ?? "")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string expression = Regex.Escape(include.Replace('\\', '/'))
                    .Replace(@"\*\*", ".*").Replace(@"\*", "[^/]*").Replace(@"\?", "[^/]");
                if (Regex.IsMatch(example, "^" + expression + "$", RegexOptions.CultureInvariant)) return false;
            }
    }
    return true;
}

// Retain the general Android diagnostic capture helpers, but omit the one
// desktop Map creator construction. Comments cannot manufacture a guard.
static bool OmitsAndroidMapCapture(string source)
{
    string remaining = Regex.Replace(Code(source), @"^\s*#if\s+!ANDROID\s*$.*?^\s*#endif\s*$", "",
        RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.CultureInvariant);
    return !Identifiers(remaining).Contains("MapStudioScreen");
}
