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
using ProjectPrime.Studio.Map;
using ProjectPrime.Studio.Settings;

string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "project-prime-tests", "broker-" + Guid.NewGuid().ToString("N"));
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
    Check(StudioPackagePathPolicy.Validate(package1, root) == package1, "frozen package under allowed staging root is accepted");
    RejectPath(Path.Combine(root, "maps", "..", "version-one.ppmap"), "parent traversal is rejected");
    try { StudioPackagePathPolicy.Validate(package1, CustomRooms.MapDirectory); throw new Exception("Untrusted root accepted"); }
    catch (UnauthorizedAccessException) { Check(true, "package outside approved staging roots is rejected"); }
    string alias = Path.Combine(root, "alias.ppmap");
    File.CreateSymbolicLink(alias, package1);
    try { RejectPath(alias, "symbolic-link package is rejected"); } finally { File.Delete(alias); }
    using (var cancelled = new CancellationTokenSource())
    {
        int calls = 0;
        var queued = new GameStudioOwnerWork(() => { calls++; return new(true); }, cancelled.Token);
        cancelled.Cancel();
        queued.Execute();
        Check(queued.Completion.IsCanceled && calls == 0, "cancelled pending owner work never mutates game state");
    }
    using (var cancelled = new CancellationTokenSource())
    using (var started = new ManualResetEventSlim())
    using (var finish = new ManualResetEventSlim())
    {
        var queued = new GameStudioOwnerWork(() => { started.Set(); finish.Wait(); return new(true); }, cancelled.Token);
        var execution = Task.Run(queued.Execute);
        Check(started.Wait(TimeSpan.FromSeconds(5)), "owner action started");
        cancelled.Cancel();
        Check(!queued.Completion.IsCompleted, "executing owner work keeps prepared resources owned until mutation finishes");
        finish.Set(); await execution;
        Check((await queued.Completion).Accepted, "owner action returns real completion after concurrent cancellation");
    }
    void RejectPath(string path, string message)
    {
        try { StudioPackagePathPolicy.Validate(path, root); throw new Exception("Unsafe package path accepted"); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { Check(true, message); }
    }
    using var owner = new OwnerDispatcher();
    int launches = 0;
    Guid currentPlaytest = Guid.Empty;
    bool rawTicket = false;
    bool blockOwner = false;
    bool stagingOnly = false;
    bool lastHost = false;
    int brokerRequests = 0;
    string? lastPackage = null;
    string installation = Path.Combine(root, "installation"), data = Path.Combine(root, "ipc-data");
    string stagingRoot = Path.Combine(data, "staging");
    var broker = new GameStudioBroker(owner.InvokeAsync,
        () => { Check(owner.IsOwnerThread, "publication policy executes on game owner thread"); return blockOwner ? "Game is busy." : null; },
        (map, options, id, host) =>
        {
            Check(owner.IsOwnerThread, "playtest launch callback executes on game owner thread");
            launches++; currentPlaytest = id; lastHost = host;
            return new(true, PlaytestId: id, PlaytestState: StudioPlaytestState.Accepted);
        },
        (id, stop) => id == currentPlaytest
            ? new(true, PlaytestId: id, PlaytestState: stop ? StudioPlaytestState.Ended : StudioPlaytestState.Started)
            : StudioGameResult.Rejected("The playtest response is stale or unknown."),
        (_, _) => Task.FromResult(rawTicket ? "raw-account-token-must-never-cross-ipc" : "ppm1.test-narrow-ticket"),
        path => StudioPackagePathPolicy.Validate(path, stagingOnly ? stagingRoot : root));
    string directory = LocalIpcEndpointStore.GetDirectory(installation, data);
    LocalIpcEndpointStore.EnsurePrivateDirectory(directory);
    using var lease = LocalIpcEndpointStore.OpenLock(directory, StudioEndpointRole.Game);
    var endpoint = new StudioEndpointDescriptor(1, "ProjectPrime.Game." + Guid.NewGuid().ToString("N")[..16],
        StudioIpcAuthentication.NewSecret(), Environment.ProcessId, StudioEndpointRole.Game);
    await using var server = new LocalIpcServer(endpoint, async (request, token) =>
    {
        Interlocked.Increment(ref brokerRequests);lastPackage=request.GameRequest!.PackagePath;
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
    var stableRuntime = HashFiles(Paths.FileSystem);
    var stableLibrary = HashFiles(CustomRooms.UserMapDirectory);
    var substituted = await client.InstallMapPackageAsync(package2, GameStudioBroker.ToWireIdentity(identity1));
    Check(!substituted.Accepted && substituted.Error?.Contains("identity or package hash") == true
        && CustomRooms.Installed.HasExact(identity2), "substituted version is rejected with exact identity diagnostic");
    Check(SameFiles(stableRuntime, HashFiles(Paths.FileSystem)) && SameFiles(stableLibrary, HashFiles(CustomRooms.UserMapDirectory)),
        "identity-mismatch rejection leaves live runtime and library byte-identical");
    string damagedPackage = Path.Combine(root, "damaged.ppmap");
    File.WriteAllText(damagedPackage, "not a map package");
    var damaged = await client.InstallMapPackageAsync(damagedPackage, GameStudioBroker.ToWireIdentity(identity2));
    Check(!damaged.Accepted && SameFiles(stableRuntime, HashFiles(Paths.FileSystem))
        && SameFiles(stableLibrary, HashFiles(CustomRooms.UserMapDirectory)),
        "malformed archive rejection leaves live runtime and library byte-identical");
    blockOwner = true;
    var blocked = await client.RequestPlaytestAsync(package2, GameStudioBroker.ToWireIdentity(identity2));
    Check(!blocked.Accepted && blocked.Deferred && launches == 0, "busy game rejects playtest before publishing/launching");
    Check(SameFiles(stableRuntime, HashFiles(Paths.FileSystem)) && SameFiles(stableLibrary, HashFiles(CustomRooms.UserMapDirectory)),
        "busy-owner rejection leaves live runtime and library byte-identical");
    blockOwner = false;
    var play = new StudioGameRequest(Guid.NewGuid(), StudioGameCommand.PlaytestMap, package2, GameStudioBroker.ToWireIdentity(identity2));
    var playResult = await client.RequestAsync(play, false);
    Check(playResult.Accepted && playResult.PlaytestId != Guid.Empty && launches == 1, "playtest broker accepts owner-dispatched canonical launch callback");
    var freshRetryEnvelope = new StudioIpcEnvelope(1, "Game", play.RequestId,
        GameRequest: play with { StudioVersion = gameVersion }) { CreatedUtc = DateTimeOffset.UtcNow.AddSeconds(1).ToUnixTimeMilliseconds() };
    var freshRetry = await LocalIpcClient.RequestAsync(endpoint, freshRetryEnvelope);
    Check(freshRetry.GameResult?.PlaytestId == playResult.PlaytestId && launches == 1,
        "duplicate playtest request with fresh valid envelope timestamp returns original result without relaunch");
    Check((await client.RequestAsync(play, false)).PlaytestId == playResult.PlaytestId && launches == 1, "duplicate playtest request returns same accepted launch without executing again");
    var alteredDuplicate = play with { Command = StudioGameCommand.HostMap, StudioVersion = gameVersion };
    var duplicateResult = await LocalIpcClient.RequestAsync(endpoint,
        new(1, "Game", alteredDuplicate.RequestId, GameRequest: alteredDuplicate));
    Check(duplicateResult.Error != null && launches == 1, "duplicate request ID with altered command is rejected");
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
    stagingOnly = true;
    using (var integration = new GameBrokerMapIntegration(new StudioPaths(installation, data),
        new StudioGameBrokerClient(installation, data, gameVersion)))
    {
        string externalPackage = Path.Combine(Path.GetTempPath(), "prime-import-" + Guid.NewGuid().ToString("N") + ".ppmap");
        File.Copy(package2, externalPackage);
        try
        {
            byte[] originalBytes = File.ReadAllBytes(externalPackage);
            var imported = await integration.InstallAsync(externalPackage, identity2, default);
            Check(imported.MapId == identity2.MapId && lastPackage!.StartsWith(stagingRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal),
                "Studio import installs arbitrary temporary package through private frozen staging");
            Check(File.ReadAllBytes(externalPackage).SequenceEqual(originalBytes) && !Directory.EnumerateFileSystemEntries(stagingRoot).Any(),
                "successful import preserves caller package and cleans staged snapshot after acknowledgement");
            int beforeHost = launches;
            await integration.HostAsync(externalPackage, identity2, MapCommunityClient.DefaultAddress, default);
            Check(launches == beforeHost + 1 && lastHost && lastPackage!.StartsWith(stagingRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal),
                "Studio Community host stages temporary publication package before strict Game path validation");
            int beforeMalformed = brokerRequests;
            File.WriteAllText(externalPackage, "damaged");
            try { await integration.InstallAsync(externalPackage, identity2, default); throw new Exception("Malformed adapter package accepted"); }
            catch (InvalidDataException) { Check(true, "Studio adapter rejects malformed package before broker dispatch"); }
            Check(brokerRequests == beforeMalformed && !Directory.EnumerateFileSystemEntries(stagingRoot).Any(),
                "failed snapshot verification leaves no staged bytes and sends no broker request");
            File.Copy(package1, externalPackage, true);
            try { await integration.HostAsync(externalPackage, identity2, MapCommunityClient.DefaultAddress, default); throw new Exception("Substituted adapter package accepted"); }
            catch (InvalidDataException) { Check(true, "Studio adapter rejects substituted package identity before Community host dispatch"); }
            Check(brokerRequests == beforeMalformed && SameFiles(stableRuntime, HashFiles(Paths.FileSystem))
                && SameFiles(stableLibrary, HashFiles(CustomRooms.UserMapDirectory)),
                "invalid adapter requests preserve exact active runtime and library bytes");
        }
        finally { File.Delete(externalPackage); }
    }
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
