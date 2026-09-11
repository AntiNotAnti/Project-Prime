using System.IO.Pipes;
using System.Net;
using System.Threading.Channels;
using ProjectPrime.Server.Shared;
using MphRead;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;

namespace ProjectPrime.Server.Worker;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        bool prepareContent = false;
        try
        {
            var flags = ParseArguments(args);
            ApplyMapDirectory(flags);
            string Required(string name) => flags.TryGetValue(name, out string? value) ? value : throw new ArgumentException("Missing " + name);
            int Number(string name, int fallback) => flags.TryGetValue(name, out string? value) ? int.Parse(value) : fallback;
            bool Boolean(string name, bool fallback) => flags.TryGetValue(name, out string? value) ? bool.Parse(value) : fallback;
            bool describeContent = flags.TryGetValue("--describe-content", out string? describe)
                && bool.Parse(describe);
            prepareContent = flags.TryGetValue("--prepare-content", out string? prepare)
                && bool.Parse(prepare);
            if (describeContent && prepareContent)
                throw new ArgumentException("--prepare-content and --describe-content cannot be enabled together.");
            if (prepareContent)
            {
                if (flags.ContainsKey("--validation-fixture"))
                    throw new ArgumentException("Content preparation cannot enable a developer validation fixture.");
                PrepareContent(Required("--content-dir"), Required("--content-version"));
                return 0;
            }
            if (describeContent)
            {
                if (flags.ContainsKey("--validation-fixture"))
                    throw new ArgumentException("Content description cannot enable a developer validation fixture.");
                ContentMapDescriptor[] maps = DescribeContent(Required("--content-dir"), Required("--content-version"));
                using var view = ContentEnvironment.AcquireContent();
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
                {
                    ContentVersion = view.Content.Version,
                    ContentHash = view.Content.ContentHash,
                    BuildVersion = WorkerOptions.ActualBuildVersion,
                    ProtocolVersion = NetHeader.Version,
                    Maps = maps
                }));
                return 0;
            }
            var options = new WorkerOptions
            {
                WorkerId = new(Guid.Parse(Required("--worker-id"))), Incarnation = Guid.Parse(Required("--worker-incarnation")),
                AdvertisedHost = flags.GetValueOrDefault("--host", "127.0.0.1"),
                BuildVersion = flags.GetValueOrDefault("--build-version", WorkerOptions.ActualBuildVersion),
                SimulationLanes = Number("--lanes", 1), MaxMatches = Number("--max-matches", 1),
                MaxMatchesPerLane = Number("--max-matches-per-lane", 1),
                SnapshotRateHz = Number("--snapshot-rate-hz", SnapshotCadence.DefaultRateHz),
                AdaptiveTimingEnabled = Boolean("--adaptive-timing", false),
                AdaptiveTimingV2Enabled = Boolean("--adaptive-timing-v2", false),
                AdaptiveInputPlayoutEnabled = Boolean("--adaptive-input-playout", false),
                TransportQueueV2Enabled = Boolean("--transport-queue-v2", false),
                TransportCriticalReserveEnabled = Boolean("--transport-critical-reserve-enabled", true),
                CriticalTransportReserve = Number("--critical-transport-reserve", 32),
                WorkerGlobalNetworkBudgetEnabled = Boolean("--worker-global-network-budget-enabled", true),
                MaximumDatagramsPerPump = Number("--max-datagrams-per-pump", WorkerNetworkHub.DefaultMaximumDatagramsPerPump),
                ReliableAdaptiveRtoEnabled = Boolean("--reliable-adaptive-rto", false),
                AckCoalescingEnabled = Boolean("--ack-coalescing", false),
                UdpAuthenticationEnabled = Boolean("--udp-authentication", true),
                LagCompensationMode = WorkerOptions.ParseLagCompensationMode(
                    flags.GetValueOrDefault("--lag-compensation-mode", "players")),
                ValidationFixture = WorkerOptions.ParseValidationFixture(
                    flags.GetValueOrDefault("--validation-fixture", "none")),
                HeadshotValidationScenario = Boolean("--headshot-validation-scenario", false),
                HeadshotScenarioSeconds = Number("--headshot-scenario-seconds", 15),
                ReplayDirectory = flags.GetValueOrDefault("--replay-dir"), ArtifactDirectory = flags.GetValueOrDefault("--artifact-dir")
            };
            options.Validate();
            NodeId node = new(Guid.Parse(Required("--node-id")));
            if (node.Value == Guid.Empty) throw new ArgumentException("Node identity is required.");
            ContentEnvironment.Open(Required("--content-dir"), Required("--content-version"));
            MapImageDecoding.Decoder = global::MphRead.Imaging.StbImageDecoder.Decode;
            CustomRooms.ContentRoot = Path.GetFullPath(Required("--content-dir"));
            await CustomRooms.RefreshAsync();
            using WorkerContentLease content = ContentEnvironment.AcquireContent();
            if (flags.TryGetValue("--content-hash", out string? expectedHash) && expectedHash != content.Content.ContentHash)
                throw new ArgumentException("Worker content hash does not match launch configuration.");
            int port = Number("--port", 0);
            if (port is < 0 or > ushort.MaxValue) throw new ArgumentException("Invalid Worker port.");
            IPAddress bindAddress = IPAddress.Parse(flags.GetValueOrDefault("--bind", "127.0.0.1"));
            if (options.ValidationFixture != DeveloperValidationFixtureId.None
                && !IPAddress.IsLoopback(bindAddress))
                throw new ArgumentException("Developer validation fixture requires a loopback bind address.");
            var physical = new UdpTransport(port, bindAddress);
            var hub = new WorkerNetworkHub(physical, options.Incarnation, new RoutedMatchDatagramRouter(),
                options.MaxMatches, options.MaximumDatagramsPerPump,
                options.WorkerGlobalNetworkBudgetEnabled, options.UdpAuthenticationEnabled);
            await using var runtime = new WorkerRuntime(options, content.Content, hub);
            using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            string token = await ReadStartupTokenAsync(Console.In, startup.Token);
            using var pipe = new NamedPipeClientStream(".", Required("--node-pipe"), PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(startup.Token);
            await WorkerIpcCodec.WriteAsync(pipe, new WorkerHello(options.WorkerId, options.Incarnation, node, token,
                options.BuildVersion, new WorkerContentIdentity(content.Content.Version, content.Content.ContentHash,
                    options.BuildVersion, options.ProtocolVersion)), startup.Token);
            token = string.Empty;
            WorkerMessage? first = await WorkerIpcCodec.ReadAsync(pipe, startup.Token);
            if (first is not WorkerConfigure configure || configure.NodeId != node)
                throw new InvalidDataException("Expected configuration from the bound Node.");
            await WorkerIpcCodec.WriteAsync(pipe, runtime.Configure(configure), startup.Token);
            await ServeAsync(pipe, runtime);
            return 0;
        }
        catch (Exception error)
        {
            // Never print launch arguments, token, or a received IPC payload.
            Console.Error.WriteLine(prepareContent
                ? "Worker content preparation failed: " + error.Message
                : "Worker stopped: " + error.GetType().Name);
            return 1;
        }
    }

    private static async Task ServeAsync(Stream pipe, WorkerRuntime runtime)
    {
        using var stop = new CancellationTokenSource();
        var outbound = Channel.CreateBounded<WorkerEvent>(new BoundedChannelOptions(256) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
        void Publish(WorkerEvent message)
        {
            if (!outbound.Writer.TryWrite(message)) stop.Cancel();
        }
        runtime.Event += Publish;
        Task writer = WriteAsync();
        Task heartbeat = HeartbeatsAsync();
        var pending = new HashSet<Task>();
        object pendingGate = new();
        try
        {
            while (!stop.IsCancellationRequested)
            {
                WorkerMessage? command = await WorkerIpcCodec.ReadAsync(pipe, stop.Token);
                if (command == null) break;
                switch (command)
                {
                    case CreateMatch create:
                        Dispatch(CreateAsync(create));
                        break;
                    case MatchAdminCommand admin: Dispatch(AdminAsync(admin)); break;
                    case UpdateNodeSigningKey key: await runtime.UpdateSigningKeyAsync(key); break;
                    case InstallAdmissionKey admission: Dispatch(InstallAdmissionAsync(admission)); break;
                    case CancelMatch cancel: Dispatch(runtime.CancelAsync(cancel.MatchId)); break;
                    case Drain: runtime.Drain(); break;
                    case Shutdown:
                        await runtime.DisposeAsync();
                        return;
                    default: throw new InvalidDataException("Unexpected or unsupported Worker command.");
                }
            }
        }
        finally
        {
            // IPC loss ends this incarnation. Dispose emits terminal interruptions before an orderly shutdown closes the pipe.
            await runtime.DisposeAsync();
            runtime.Event -= Publish;
            stop.Cancel();
            try { await heartbeat; } catch (OperationCanceledException) { }
            outbound.Writer.TryComplete();
            await writer.WaitAsync(TimeSpan.FromSeconds(5));
        }
        void Dispatch(Task operation)
        {
            lock (pendingGate)
            {
                pending.RemoveWhere(task => task.IsCompletedSuccessfully);
                if (pending.Count >= 128) throw new InvalidDataException("Worker control queue is full.");
                pending.Add(operation);
            }
            _ = ObserveAsync(operation);
        }
        async Task ObserveAsync(Task operation)
        {
            try { await operation; }
            catch { stop.Cancel(); }
            finally { lock (pendingGate) pending.Remove(operation); }
        }
        async Task AdminAsync(MatchAdminCommand admin) => await outbound.Writer.WriteAsync(await runtime.AdminAsync(admin), stop.Token);
        async Task InstallAdmissionAsync(InstallAdmissionKey admission)
            => await outbound.Writer.WriteAsync(await runtime.InstallAdmissionKeyAsync(admission), stop.Token);
        async Task CreateAsync(CreateMatch create)
        {
            WorkerEvent response = await runtime.CreateAsync(create.Spec);
            await outbound.Writer.WriteAsync(response, stop.Token);
            if (response is MatchReady) await outbound.Writer.WriteAsync(new MatchStarted(create.Spec.MatchId), stop.Token);
        }
        async Task WriteAsync()
        {
            try { await foreach (var message in outbound.Reader.ReadAllAsync()) await WorkerIpcCodec.WriteAsync(pipe, message); }
            catch { stop.Cancel(); throw; }
        }
        async Task HeartbeatsAsync()
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(stop.Token)) Publish(runtime.Heartbeat());
        }
    }

    internal static async Task<string> ReadStartupTokenAsync(TextReader input, CancellationToken cancellationToken)
    {
        char[] buffer = new char[67];
        int length = 0;
        while (length < buffer.Length)
        {
            int read = await input.ReadAsync(buffer.AsMemory(length, 1), cancellationToken);
            if (read == 0) break;
            length += read;
        }
        // Exactly 64 hex characters and an optional single LF; no unbounded ReadLine allocation.
        if (length is not (64 or 65 or 66) || length == 65 && buffer[64] != '\n'
            || length == 66 && (buffer[64] != '\r' || buffer[65] != '\n')
            || !buffer.AsSpan(0, 64).ToArray().All(Uri.IsHexDigit))
            throw new InvalidDataException("Invalid Worker startup token.");
        return new string(buffer, 0, 64);
    }

    internal static void ApplyMapDirectory(IReadOnlyDictionary<string, string> flags)
    {
        if (!flags.TryGetValue("--map-dir", out string? directory)) { return; }
        if (String.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Map directory is required.");
        CustomRooms.SetBuildMapDirectory(Path.GetFullPath(directory));
    }

    internal static int PrepareContent(string directory, string version)
    {
        string contentDirectory = Path.GetFullPath(directory);
        if (File.Exists(Path.Combine(contentDirectory, ServerContentPackage.ManifestName)))
        {
            throw new ProgramException("Content preparation requires an extracted game directory; "
                + "baked server content packages are read-only.");
        }

        // Set the image decoder at the Worker boundary. Imported maps may
        // carry a texture archive rather than a pre-baked texture pack, and
        // preparation must not depend on a client process being present.
        MapImageDecoding.Decoder = global::MphRead.Imaging.StbImageDecoder.Decode;
        ContentEnvironment.Open(contentDirectory, version);
        CustomRooms.ContentRoot = contentDirectory;

        CustomRooms.RefreshAsync().AsTask().GetAwaiter().GetResult();
        int generated = 0;
        foreach (InstalledMap map in CustomRooms.Catalog.Snapshot.Maps
            .Where(map => map.BuildState is MapBuildState.NeedsBuild or MapBuildState.Ready)
            .OrderBy(map => map.ContentIdentity.Identity.StableId, StringComparer.Ordinal)
            .ThenBy(map => map.SourcePath, StringComparer.Ordinal))
        {
            try
            {
                MapBuildResult result = MapPreparation.CompileAsync(map.Project, force: false,
                    verbose: true, CancellationToken.None).GetAwaiter().GetResult();
                if (!result.CacheHit) generated++;
                ((MapCatalog)CustomRooms.Catalog).PublishBuildState(map.ContentIdentity,
                    MapBuildState.Ready, result.Statistics, result.Diagnostics);
            }
            catch (Exception error) when (error is MapCompilationException or MapValidationException
                or MapDependencyException or InvalidDataException or IOException or ArgumentException)
            {
                Console.Error.WriteLine($"[map] {map.DisplayName} could not be prepared: {error.Message}");
                CompilationFailureKind kind = error switch
                {
                    MapDependencyException => CompilationFailureKind.MissingDependency,
                    MapCompilationException compilation when compilation.Diagnostics.Count != 0
                        => CompilationFailureKinds.FromDiagnostics(compilation.Diagnostics),
                    IOException => CompilationFailureKind.IOFailure,
                    _ => CompilationFailureKind.InvalidSource
                };
                ((MapCatalog)CustomRooms.Catalog).PublishBuildState(map.ContentIdentity,
                    kind.ToBuildState(), null,
                    [new MapDiagnostic("MAP-CMP-001", MapDiagnosticSeverity.Error,
                        error.Message, SourcePath: map.SourcePath)]);
            }
        }
        return generated;
    }

    private static ContentMapDescriptor[] DescribeContent(string directory, string version)
    {
        // CustomRooms reports ignored/broken optional map files to stdout. Keep
        // the descriptor itself as one JSON document and preserve diagnostics
        // on stderr for callers that want to inspect them.
        TextWriter output = Console.Out;
        using var diagnostics = new StringWriter();
        Console.SetOut(diagnostics);
        try { return ServerContentValidation.DescribeMaps(directory, version); }
        finally
        {
            Console.SetOut(output);
            if (diagnostics.GetStringBuilder().Length > 0) Console.Error.Write(diagnostics.ToString());
        }
    }

    internal static Dictionary<string, string> ParseArguments(string[] args)
    {
        string[] names = ["--describe-content", "--prepare-content", "--node-pipe", "--node-id", "--worker-id", "--worker-incarnation", "--content-dir", "--content-version", "--content-hash",
            "--build-version", "--host", "--bind", "--port", "--lanes", "--max-matches", "--max-matches-per-lane", "--snapshot-rate-hz", "--adaptive-timing", "--adaptive-timing-v2", "--adaptive-input-playout", "--transport-queue-v2", "--transport-critical-reserve-enabled", "--critical-transport-reserve", "--worker-global-network-budget-enabled", "--max-datagrams-per-pump", "--reliable-adaptive-rto", "--ack-coalescing", "--udp-authentication", "--lag-compensation-mode", "--validation-fixture", "--headshot-validation-scenario", "--headshot-scenario-seconds", "--replay-dir", "--artifact-dir", "--map-dir"];
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 0; index < args.Length; index += 2)
            if (index + 1 == args.Length || !names.Contains(args[index], StringComparer.Ordinal) || !result.TryAdd(args[index], args[index + 1]))
                throw new ArgumentException("Invalid, missing or duplicate Worker option.");
        return result;
    }
}
