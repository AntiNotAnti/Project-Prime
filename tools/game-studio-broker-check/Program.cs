using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.MapGen;
using MphRead.Mods.StudioIntegration;
using ProjectPrime.Studio.IPC;
using ProjectPrime.Studio.Protocol;

string root = Path.Combine(Path.GetTempPath(), "prime-game-studio-broker-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
string? oldData = Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA");
Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA", Path.Combine(root, "game-user"));
int checks = 0;
try
{
    Headless.Enter();
    Paths.SetPath(Paths.MphKey, Path.Combine(root, "runtime"));
    Directory.CreateDirectory(Paths.FileSystem);
    CustomRooms.UserMapDirectory = Path.Combine(root, "installed");
    CustomRooms.MapDirectory = Path.Combine(root, "maps");
    Directory.CreateDirectory(CustomRooms.MapDirectory);
    using (var texture = new BinaryWriter(File.Create(Path.Combine(root, "tile.tex"))))
    {
        texture.Write(Encoding.ASCII.GetBytes("FPTX")); texture.Write((ushort)1); texture.Write((ushort)1);
        texture.Write((ushort)0); texture.Write((ushort)8); texture.Write((ushort)8); texture.Write((ushort)1);
        texture.Write((ushort)0); texture.Write((ushort)32767); texture.Write(new byte[64]);
    }
    var definition = new MapDefinition { FormatVersion = 2, MapId = Guid.NewGuid(), Name = "STUDIO_BROKER_CHECK", Version = "1", BaseDirectory = root };
    definition.Materials.Add(new() { Texture = "tile.tex" }); definition.Assets.Add(new() { Path = "tile.tex" });
    definition.Geometry.Add(new MapBox { Transform = new() { Position = [0f, -1, 0], Scale = [8f, 1, 8] } });
    definition.Spawns.Add(new() { Position = [0f, 2, 0] });
    string package1 = MapPackageBuilder.Build(definition, Path.Combine(root, "version-one.ppmap"));
    var identity1 = MapContentIdentity.FromPackage(package1);
    definition.Version = "2"; definition.Geometry[0].Transform.Scale = [10f, 1, 8];
    string package2 = MapPackageBuilder.Build(definition, Path.Combine(root, "version-two.ppmap"));
    var identity2 = MapContentIdentity.FromPackage(package2);
    Check(!identity1.Matches(identity2), "fixture package versions have different exact identities");
    using var owner = new OwnerDispatcher();
    int launches = 0;
    Guid currentPlaytest = Guid.Empty;
    bool rawTicket = false;
    bool blockOwner = false;
    var broker = new GameStudioBroker(owner.InvokeAsync,
        () => { Check(owner.IsOwnerThread, "publication policy executes on game owner thread"); return blockOwner ? "Game is busy." : null; },
        (map, options, id, host) =>
        {
            Check(owner.IsOwnerThread, "playtest launch callback executes on game owner thread");
            launches++; currentPlaytest = id;
            return new(true, PlaytestId: id, PlaytestState: StudioPlaytestState.Accepted);
        },
        (id, stop) => id == currentPlaytest
            ? new(true, PlaytestId: id, PlaytestState: stop ? StudioPlaytestState.Ended : StudioPlaytestState.Started)
            : StudioGameResult.Rejected("The playtest response is stale or unknown."),
        (_, _) => Task.FromResult(rawTicket ? "raw-account-token-must-never-cross-ipc" : "ppm1.test-narrow-ticket"));
    string installation = Path.Combine(root, "installation"), data = Path.Combine(root, "ipc-data");
    string directory = LocalIpcEndpointStore.GetDirectory(installation, data);
    LocalIpcEndpointStore.EnsurePrivateDirectory(directory);
    using var lease = LocalIpcEndpointStore.OpenLock(directory, StudioEndpointRole.Game);
    var endpoint = new StudioEndpointDescriptor(1, "ProjectPrime.Game." + Guid.NewGuid().ToString("N")[..16],
        StudioIpcAuthentication.NewSecret(), Environment.ProcessId, StudioEndpointRole.Game);
    await using var server = new LocalIpcServer(endpoint, async (request, token) =>
    {
        var result = await broker.HandleAsync(request.GameRequest!, token);
        return new(1, "Result", request.RequestId, GameResult: result);
    });
    LocalIpcEndpointStore.Write(directory, endpoint); server.Start();
    string gameVersion = typeof(GameStudioBroker).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
    using var client = new StudioGameBrokerClient(installation, data, gameVersion);
    var diagnostics = await client.GetDiagnosticsAsync();
    Check(diagnostics.Accepted && diagnostics.IpcVersion == 1 && diagnostics.GameVersion == gameVersion
        && diagnostics.StudioVersion == gameVersion, "authenticated game diagnostics expose both application builds and independent IPC version");
    var wrongVersion = new StudioGameRequest(Guid.NewGuid(), StudioGameCommand.InstallMapPackage, package1,
        GameStudioBroker.ToWireIdentity(identity1), StudioVersion: "incompatible-release");
    var incompatible = await LocalIpcClient.RequestAsync(endpoint, new(1, "Game", wrongVersion.RequestId, GameRequest: wrongVersion));
    Check(incompatible.GameResult is { Accepted: false, GameVersion: not null, StudioVersion: "incompatible-release" }
        && incompatible.GameResult.Error!.Contains("version mismatch", StringComparison.OrdinalIgnoreCase)
        && !Directory.Exists(CustomRooms.UserMapDirectory), "different official application builds fail explicitly before package mutation");
    var installed = await client.InstallMapPackageAsync(package1, GameStudioBroker.ToWireIdentity(identity1));
    Check(installed.Accepted && CustomRooms.Installed.HasExact(identity1), "real game installer publishes exact submitted immutable package");
    Check(Read.GetRoomModelInstance(definition.Name).Model.Meshes.Count > 0, "published runtime outputs decode through canonical game reader");
    var oldRuntime = HashFiles(Paths.FileSystem);
    var oldLibrary = HashFiles(CustomRooms.UserMapDirectory);
    IDisposable preparation = (IDisposable)typeof(MapRuntimeUsage).GetMethod("AcquirePreparation", BindingFlags.NonPublic | BindingFlags.Static)!
        .Invoke(null, [identity1.RoomKey])!;
    using (preparation)
    {
        var deferred = await client.InstallMapPackageAsync(package2, GameStudioBroker.ToWireIdentity(identity2));
        Check(!deferred.Accepted && deferred.Deferred, "active preparation defers replacement package across authenticated broker");
        Check(SameFiles(oldRuntime, HashFiles(Paths.FileSystem)) && SameFiles(oldLibrary, HashFiles(CustomRooms.UserMapDirectory)), "rejected publication leaves active runtime and package bytes identical");
    }
    var retried = await client.InstallMapPackageAsync(package2, GameStudioBroker.ToWireIdentity(identity2));
    Check(retried.Accepted && CustomRooms.Installed.HasExact(identity2), "released preparation allows exact hash replacement on retry");
    Check(!SameFiles(oldRuntime, HashFiles(Paths.FileSystem)), "successful replacement changes generated runtime outputs");
    var publishedRuntime = HashFiles(Paths.FileSystem);
    string auditRuntime = Path.Combine(root, "studio-audit", "runtime");
    try
    {
        CustomRooms.GeneratedRuntimeRoot = auditRuntime;
        CustomRooms.RuntimeNamespace = Guid.NewGuid().ToString("N");
        var privateDefinition = MapDefinition.Load(package2);
        var privateBuild = await MapBuildScheduler.Shared.BuildAsync(MapBuildSnapshot.Capture(privateDefinition));
        MapBuildScheduler.Publish(privateBuild, privateDefinition, CustomRooms.ArchiveDirectory(privateDefinition),
            CustomRooms.EntityDirectory(), CustomRooms.NodeDirectory());
        RegisterMap(privateDefinition);
        Check(Directory.GetFiles(auditRuntime, "*", SearchOption.AllDirectories).Length >= 6
            && SameFiles(publishedRuntime, HashFiles(Paths.FileSystem)), "isolated audit output root preserves every active game runtime byte");
        Check(Read.GetRoomModelInstance(privateDefinition.Name).Model.Meshes.Count > 0,
            "isolated audit metadata decodes canonical generated resources from private absolute paths");
    }
    finally
    {
        CustomRooms.GeneratedRuntimeRoot = null;
        CustomRooms.RuntimeNamespace = "";
        RegisterMap(MapDefinition.Load(package2));
    }
    var substituted = await client.InstallMapPackageAsync(package2, GameStudioBroker.ToWireIdentity(identity1));
    Check(!substituted.Accepted && CustomRooms.Installed.HasExact(identity2), "substituted map version cannot replace exact requested identity");
    blockOwner = true;
    var blocked = await client.RequestPlaytestAsync(package2, GameStudioBroker.ToWireIdentity(identity2));
    Check(!blocked.Accepted && blocked.Deferred && launches == 0, "busy game rejects playtest before publishing/launching");
    blockOwner = false;
    var play = new StudioGameRequest(Guid.NewGuid(), StudioGameCommand.PlaytestMap, package2, GameStudioBroker.ToWireIdentity(identity2));
    var playResult = await client.RequestAsync(play, false);
    Check(playResult.Accepted && playResult.PlaytestId != Guid.Empty && launches == 1, "playtest broker accepts owner-dispatched canonical launch callback");
    Check((await client.RequestAsync(play, false)).PlaytestId == playResult.PlaytestId && launches == 1, "duplicate playtest request returns same accepted launch without executing again");
    var status = await client.GetPlaytestStatusAsync(playResult.PlaytestId);
    Check(status.Accepted && status.PlaytestState == StudioPlaytestState.Started, "authenticated status preserves playtest ID and state");
    Check(!(await client.StopPlaytestAsync(Guid.NewGuid())).Accepted && currentPlaytest == playResult.PlaytestId, "stale stop cannot affect current playtest");
    Check((await client.StopPlaytestAsync(playResult.PlaytestId)).PlaytestState == StudioPlaytestState.Ended, "current stop reaches game owner callback");
    var ticket = await client.RequestCommunityTicketAsync(false);
    Check(ticket.Accepted && ticket.CommunityTicket == "ppm1.test-narrow-ticket", "only narrow ppm1 Community capability crosses broker");
    rawTicket = true;
    var rejectedTicket = await client.RequestCommunityTicketAsync(false);
    Check(!rejectedTicket.Accepted && rejectedTicket.CommunityTicket == null && !rejectedTicket.Error!.Contains("raw-account", StringComparison.Ordinal), "non-ppm1 account credential is rejected and never returned");
    using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
    Check(!(await client.RequestAsync(new(Guid.NewGuid(), StudioGameCommand.Diagnostics), false, cancellation.Token)).Accepted, "caller cancellation is bounded before game dispatch");
    Check(!MphRead.Mods.Network.NetSession.Active, "broker checking and package work never open gameplay socket");
    using var disconnectedClient = new StudioGameBrokerClient(Path.Combine(root, "missing-installation"), Path.Combine(root, "missing-peer"), gameVersion);
    var missingDiagnostics = await disconnectedClient.GetDiagnosticsAsync();
    Check(!missingDiagnostics.Accepted && missingDiagnostics.Error == "Project Prime is not running or its local broker is unavailable.",
        "missing peer diagnostics report broker availability without publication/sign-in guidance or auto-launch");
    var missingTicket = await disconnectedClient.RequestCommunityTicketAsync(false);
    Check(!missingTicket.Accepted && missingTicket.Error!.StartsWith("Sign in through Project Prime", StringComparison.Ordinal),
        "missing peer ticket request retains narrow game sign-in guidance");
    Console.WriteLine($"Game Studio broker checks passed: {checks}. Actual package compilation/publication and authenticated owner dispatch verified; native gameplay launch belongs to the desktop lifecycle gate.");
    return 0;

    void Check(bool value, string message) { Interlocked.Increment(ref checks); if (!value) throw new InvalidOperationException(message); Console.WriteLine("PASS " + message); }
}
catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
finally
{
    Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA", oldData);
    try { Directory.Delete(root, true); } catch (IOException) { }
}

static Dictionary<string, string> HashFiles(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
    .Where(path => !path.EndsWith(".lock", StringComparison.Ordinal)).ToDictionary(path => Path.GetRelativePath(root, path), path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
static bool SameFiles(Dictionary<string, string> a, Dictionary<string, string> b) => a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out var hash) && hash == pair.Value);
static void RegisterMap(MapDefinition definition) => typeof(Metadata).GetMethod("RegisterDownloadedMap", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [definition]);

internal sealed class OwnerDispatcher : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;
    public bool IsOwnerThread => Thread.CurrentThread.ManagedThreadId == _thread.ManagedThreadId;
    public OwnerDispatcher()
    {
        _thread = new(() => { foreach (Action action in _queue.GetConsumingEnumerable()) action(); }) { IsBackground = true, Name = "Game Studio check owner" };
        _thread.Start();
    }
    public Task<StudioGameResult> InvokeAsync(Func<StudioGameResult> action, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<StudioGameResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(() =>
        {
            if (token.IsCancellationRequested) { completion.TrySetCanceled(token); return; }
            try { completion.TrySetResult(action()); } catch (Exception ex) { completion.TrySetException(ex); }
        }, token);
        return completion.Task.WaitAsync(token);
    }
    public void Dispose() { _queue.CompleteAdding(); _thread.Join(TimeSpan.FromSeconds(2)); _queue.Dispose(); }
}
