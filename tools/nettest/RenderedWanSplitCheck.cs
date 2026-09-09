using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using FruityPrime.Server.Node;
using FruityPrime.Server.Node.Identity;
using FruityPrime.Server.Node.Lobbies;
using FruityPrime.Server.Node.Workers;
using FruityPrime.Server.Shared;
using FruityPrime.Server.Worker;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MphRead.Entities;
using MphRead.Mods.Accounts;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

internal static partial class RenderedWanValidationCheck
{
    private const string SplitSchema = "project-prime.rendered-wan-split.v1";
    private const string SplitIssuer = "https://rendered-wan-operator.invalid";
    private static readonly JsonSerializerOptions SplitJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static int RunOperator(string[] args)
    {
        SplitOperatorOptions? options = null;
        try
        {
            options = SplitOperatorOptions.Parse(args);
            return RunOperatorAsync(options).GetAwaiter().GetResult();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"RENDERED_WAN_OPERATOR result=FAIL error={error.GetType().Name} reason={Bounded(error.Message)}");
            if (options != null) WriteSplitFailure(Path.Combine(options.OutputDirectory, "server-report.json"), "server", error);
            return 1;
        }
    }

    private static async Task<int> RunOperatorAsync(SplitOperatorOptions options)
    {
        Directory.CreateDirectory(options.OutputDirectory);
        ContentEnvironment.Open(options.DataDirectory, "AMHE1");
        (string contentVersion, string contentHash) = ContentEnvironment.GetContentIdentity();
        RequireOrdinaryRoom(options.Room);
        var content = new WorkerContentIdentity(contentVersion, contentHash,
            WorkerOptions.ActualBuildVersion, NetHeader.Version);
        DateTimeOffset started = DateTimeOffset.UtcNow;
        Guid runId = Guid.NewGuid();
        Guid subject = Guid.NewGuid();

        await using var node = new SplitOperatorNode(content, options);
        await node.App.StartAsync();
        try
        {
            await UntilAsync(() =>
            {
                WorkerSnapshot[] workers = node.App.Services.GetRequiredService<WorkerManager>().Snapshot().ToArray();
                if (workers.Any(worker => worker.FailureReason != null))
                    throw new InvalidOperationException("The external Worker failed during startup.");
                return workers.Length == 1 && workers[0].Status == WorkerStatus.Ready;
            }, TimeSpan.FromSeconds(35));

            WorkerManager manager = node.App.Services.GetRequiredService<WorkerManager>();
            WorkerSnapshot worker = manager.Snapshot().Single();
            WorkerScheduler scheduler = node.App.Services.GetRequiredService<WorkerScheduler>();
            var debug = new DebugRefreshState();
            int unexpectedDebugRejected = 0;
            var ready = new TaskCompletionSource<MatchId>(TaskCreationOptions.RunContinuationsAsynchronously);
            var terminal = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Observe(ManagedWorker _, WorkerEvent message)
            {
                debug.Observe(message);
                if (message is MatchAdminResult { Applied: false } rejected
                    && rejected.Action is AdminAction.LagCompHistory or AdminAction.LagCompDynamic
                    && rejected.Code != "debug")
                    Interlocked.Increment(ref unexpectedDebugRejected);
                switch (message)
                {
                    case MatchReady value: ready.TrySetResult(value.Placement.MatchId); break;
                    case MatchCompleted: terminal.TrySetResult(MatchStatus.Completed.ToString()); break;
                    case MatchFailed: terminal.TrySetResult(MatchStatus.Failed.ToString()); break;
                    case MatchInterrupted: terminal.TrySetResult(MatchStatus.Interrupted.ToString()); break;
                }
            }
            scheduler.Observed += Observe;
            try
            {
                DateTimeOffset issued = DateTimeOffset.UtcNow;
                DateTimeOffset expires = issued.AddSeconds(120);
                started = issued;
                NodeAdmissionTicket admission = node.CreateAdmission(subject, issued, expires);
                var descriptor = new SplitDescriptor(SplitSchema, runId, node.NodeId,
                    manager.NodeIncarnation, worker.WorkerId.Value, worker.Incarnation, subject,
                    issued, expires, contentVersion, contentHash, WorkerOptions.ActualBuildVersion,
                    NetHeader.Version, options.Mode.ToString().ToLowerInvariant(), options.Room,
                    options.NodeControlUri, options.NodeBind.ToString(), options.NodePort,
                    options.WorkerBind.ToString(), options.WorkerHost.ToString(), options.WorkerPort,
                    node.CertificateSha256, node.SpkiSha256, options.Seconds,
                    options.ReconnectAt, ValidationFixture: false, PublicDiagnostics: false);
                var secret = new SplitSecret(SplitSchema, runId, node.NodeId, subject,
                    expires, admission.Ticket);
                string descriptorPath = Path.Combine(options.OutputDirectory, "descriptor.json");
                string secretPath = Path.Combine(options.OutputDirectory, "secret.json");
                WriteSplitJson(descriptorPath, descriptor);
                WriteSecret(secretPath, secret);
                Console.WriteLine($"RENDERED_WAN_OPERATOR ready descriptor={descriptorPath} secretWritten=true expiresUtc={expires:O}");

                using var total = new CancellationTokenSource(TimeSpan.FromSeconds(options.WaitSeconds));
                CancellationTokenSource? refreshStop = null;
                Task? refresh = null;
                try
                {
                    MatchId matchId = await ready.Task.WaitAsync(total.Token);
                    WorkerMatchAssignment assignment;
                    do
                    {
                        if (scheduler.TryGetAssignment(matchId, out WorkerMatchAssignment? candidate)
                            && candidate?.Placement != null)
                        {
                            assignment = candidate;
                            break;
                        }
                        await Task.Delay(10, total.Token);
                    } while (true);
                    RosterSeat seat = assignment.Spec.Roster.Single(value
                        => value.Role == SeatRole.Player && value.PlayerId?.Value == subject);
                    MatchPlacement placement = assignment.Placement!;
                    var binding = new SplitBinding(runId, node.NodeId, manager.NodeIncarnation,
                        assignment.WorkerId.Value, assignment.WorkerIncarnation,
                        assignment.Spec.MatchId.Value, placement.WireMatchId.Value,
                        subject, seat.SeatId, placement.Host, placement.Port,
                        contentVersion, contentHash, WorkerOptions.ActualBuildVersion,
                        NetHeader.Version, options.Mode.ToString().ToLowerInvariant(), options.Room);
                    ValidateBinding(descriptor, binding);
                    refreshStop = CancellationTokenSource.CreateLinkedTokenSource(total.Token);
                    refreshStop.CancelAfter(TimeSpan.FromSeconds(options.Seconds + 15));
                    refresh = RefreshDebugAsync(node.App.Services.GetRequiredService<NodeMatchCoordinator>(),
                        matchId, seat.SeatId, debug, refreshStop.Token);
                    string status = await terminal.Task.WaitAsync(total.Token);
                    refreshStop.Cancel();
                    try { await refresh; } catch (OperationCanceledException) { }
                    DateTimeOffset ended = DateTimeOffset.UtcNow;
                    bool passed = status == MatchStatus.Completed.ToString()
                        && debug.Applied >= 8 && debug.Queued == debug.Applied + debug.Rejected
                        && debug.Attempts >= debug.Queued && debug.Attempts <= 512
                        && debug.Rejected <= 8 && unexpectedDebugRejected == 0
                        && (debug.Rejected == 0 && debug.LastRejectionCode == null
                            || debug.Rejected > 0 && debug.LastRejectionCode == "debug");
                    var report = new SplitServerReport(SplitSchema, "server", binding,
                        started, ended, status, debug.Attempts, debug.Queued, debug.Applied,
                        debug.Rejected, unexpectedDebugRejected, debug.LastRejectionCode, passed, null,
                        RenderedWanProof: false, Qz1Accepted: false, Qz5Accepted: false,
                        RequiresHumanVisualReview: true);
                    WriteSplitJson(Path.Combine(options.OutputDirectory, "server-report.json"), report);
                    Console.WriteLine($"RENDERED_WAN_OPERATOR result={(passed ? "PASS" : "FAIL")} run={runId:D} match={binding.MatchId:D} seat={seat.SeatId} debugApplied={debug.Applied} renderedWanProof=false");
                    return passed ? 0 : 1;
                }
                finally
                {
                    refreshStop?.Cancel();
                    if (refresh != null) try { await refresh; } catch (OperationCanceledException) { }
                    refreshStop?.Dispose();
                }
            }
            finally { scheduler.Observed -= Observe; }
        }
        finally
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await node.App.StopAsync(stop.Token);
        }
    }

    public static int RunClient(string[] args)
    {
        SplitClientOptions? options = null;
        try
        {
            options = SplitClientOptions.Parse(args);
            SynchronizationContext? prior = SynchronizationContext.Current;
            using var context = new MainThreadSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                Task<int> operation = RunClientAsync(options);
                context.Run(operation);
                return operation.GetAwaiter().GetResult();
            }
            finally { SynchronizationContext.SetSynchronizationContext(prior); }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"RENDERED_WAN_CLIENT result=FAIL error={error.GetType().Name} reason={Bounded(error.Message)}");
            if (options != null) WriteSplitFailure(Path.Combine(options.OutputDirectory, "client-report.json"), "client", error);
            NetSession.Stop();
            return 1;
        }
    }

    private static async Task<int> RunClientAsync(SplitClientOptions options)
    {
        Directory.CreateDirectory(options.OutputDirectory);
        string captures = Path.Combine(options.OutputDirectory, "captures");
        Directory.CreateDirectory(captures);
        DateTimeOffset started = DateTimeOffset.UtcNow;
        SplitDescriptor descriptor = ReadSplit<SplitDescriptor>(options.DescriptorPath);
        SplitSecret secret = ReadSecret(options.SecretPath);
        ValidateDescriptor(descriptor, secret, started);
        ContentEnvironment.Open(options.DataDirectory, "AMHE1");
        (string contentVersion, string contentHash) = ContentEnvironment.GetContentIdentity();
        RequireOrdinaryRoom(descriptor.Room);
        if (descriptor.ContentVersion != contentVersion || descriptor.ContentHash != contentHash
            || descriptor.BuildVersion != WorkerOptions.ActualBuildVersion
            || descriptor.ProtocolVersion != NetHeader.Version)
            throw new InvalidOperationException("Client content/build/protocol does not match the descriptor.");
        if (!NetLag.Configure("0:0") || !NetLag.ConfigureLoss("0"))
            throw new InvalidOperationException("Client process-local impairment could not be disabled.");
        RenderBackendSelection.ResetForTests();
        if (RenderBackendSelection.Current != RenderBackendKind.Sdl)
            throw new PlatformNotSupportedException("Rendered WAN client requires the SDL renderer.");

        await using NodeControlClient control = CreateSplitControlClient(descriptor,
            options.AllowLabPin);
        await control.ConnectAsync(new NodeAdmissionTicket(secret.Ticket, secret.ExpiresUtc,
            descriptor.NodeId, descriptor.NodeControlUri));
        if (control.Session?.PlayerId != descriptor.SubjectId)
            throw new InvalidOperationException("Node session subject does not match the split descriptor.");
        await control.SendAsync("lobby.create", new LobbyCreate("Rendered WAN", LobbyVisibility.Unlisted, 2, 0));
        await UntilAsync(() => control.Lobby != null, TimeSpan.FromSeconds(10), control);
        long revision = control.Lobby!.Revision;
        await control.SendAsync("lobby.configure", new LobbyConfigure(revision,
            descriptor.Room, MatchMode.Battle, BotCount: 1,
            TimeLimitSeconds: checked(descriptor.Seconds + 45)));
        await UntilAsync(() => control.Lobby!.Revision > revision, TimeSpan.FromSeconds(10), control);
        revision = control.Lobby!.Revision;
        await control.SendAsync("lobby.hunter.select", new LobbySelectHunter(Hunter.Noxus, revision));
        await UntilAsync(() => control.Lobby!.Revision > revision, TimeSpan.FromSeconds(10), control);
        revision = control.Lobby!.Revision;
        await control.SendAsync("lobby.ready.set", new LobbySetReady(true, revision));
        await UntilAsync(() => control.Lobby!.Revision > revision, TimeSpan.FromSeconds(10), control);
        await control.SendAsync("lobby.start", new LobbyStart(control.Lobby!.Revision));
        await UntilAsync(() => control.Handoff != null, TimeSpan.FromSeconds(35), control);
        NodeMatchHandoff first = control.Handoff!;
        ValidateHandoff(descriptor, first);
        if (!await NetLaunch.JoinWorkerAsync(first, control.Session!.DisplayName, timeoutMs: 30000))
            throw new InvalidOperationException("The production Worker handoff was rejected: " + Bounded(NetLaunch.LastJoinError));
        AuthoritativePlay play = AuthoritativePlay.Current
            ?? throw new InvalidOperationException("The Worker handoff did not create an authoritative client.");
        control.MarkGameplayJoined(first.MatchId);

        async Task<NodeMatchHandoff> RejoinAsync()
        {
            ulong priorNonce = control.Handoff?.Nonce ?? 0;
            await control.SendAsync("match.rejoin", new NodeMatchRejoin(first.MatchId));
            await UntilAsync(() => control.Handoff is { } handoff && handoff.Nonce != priorNonce,
                TimeSpan.FromSeconds(10), control);
            NodeMatchHandoff handoff = control.Handoff!;
            ValidateHandoff(descriptor, handoff);
            if (handoff.MatchId != first.MatchId || handoff.WireMatchId != first.WireMatchId)
                throw new InvalidOperationException("Node rejoin changed the frozen match identity.");
            return handoff;
        }

        var renderOptions = new Options(options.DataDirectory, options.OutputDirectory,
            WorkerOptions.ParseLagCompensationMode(descriptor.Mode), 0, 0, 0,
            descriptor.Seconds, descriptor.ReconnectAt, descriptor.Room,
            options.Width, options.Height, DeveloperValidationFixtureId.None);
        RenderedClientResult result;
        byte clientSeat = checked((byte)play.LocalSlot);
        try
        {
            using IRenderToolHost host = RenderToolHostFactory.Create(
                new Vector2i(options.Width, options.Height), "Project Prime WAN operator validation",
                updateFrequency: 60, visible: true, presentable: true);
            var client = new RenderedClient(host, play, Hunter.Noxus, renderOptions,
                captures, () => Task.Run(RejoinAsync));
            host.Run(client);
            result = client.Result();
            if (host is SdlRenderToolHost sdl) result = result with { Backend = sdl.BackendInfo };
            // Keep the authoritative connection alive through the short match
            // terminal so server-side diagnostics do not turn a successful
            // render run into a disconnect-driven interruption.
            await UntilAsync(() => control.MatchEnded, TimeSpan.FromSeconds(90), control);
        }
        finally { NetSession.Stop(); }

        DateTimeOffset ended = DateTimeOffset.UtcNow;
        var binding = new SplitBinding(descriptor.RunId, descriptor.NodeId,
            descriptor.NodeIncarnation, descriptor.WorkerId, descriptor.WorkerIncarnation,
            first.MatchId, first.WireMatchId, descriptor.SubjectId, clientSeat,
            first.Host, first.Port, descriptor.ContentVersion, descriptor.ContentHash,
            descriptor.BuildVersion, descriptor.ProtocolVersion, descriptor.Mode, descriptor.Room);
        ValidateBinding(descriptor, binding);
        bool passed = result.RuntimePassed && result.ReconnectCompleted
            && result.DebugPackets > 0 && result.Captures.Any(value => value.Saved);
        SplitCapture[] captureReport = result.Captures.Select(value => new SplitCapture(
            value.File, value.Width, value.Height, value.NonBlackFraction,
            value.Saved, value.Sha256 ?? "")).ToArray();
        var report = new SplitClientReport(SplitSchema, "client", binding, started, ended,
            result.FinalState.ToString(), result.RuntimePassed, result.ReconnectCompleted,
            result.ReconnectSameMatch, result.ReconnectSameSeat, result.ConnectionIdentityRotated,
            result.SubmittedFrames, result.AcknowledgedFrames, result.Snapshots,
            result.CombatEvents, result.DebugPackets, result.HistoryDebugPackets,
            result.DynamicDebugPackets, captureReport, passed, null,
            RenderedWanProof: false, Qz1Accepted: false, Qz5Accepted: false,
            RequiresHumanVisualReview: true);
        WriteSplitJson(Path.Combine(options.OutputDirectory, "client-report.json"), report);
        Console.WriteLine($"RENDERED_WAN_CLIENT result={(passed ? "PASS" : "FAIL")} run={descriptor.RunId:D} match={first.MatchId:D} frames={result.SubmittedFrames} captures={captureReport.Length} reconnect={result.ReconnectCompleted} renderedWanProof=false");
        return passed ? 0 : 1;
    }

    public static int RunMerge(string[] args)
    {
        try
        {
            if (args.Length != 6)
                throw new ArgumentException("Expected DESCRIPTOR_JSON SERVER_REPORT CLIENT_REPORT CAPTURE_DIRECTORY OUTPUT_JSON.");
            string descriptorPath = Path.GetFullPath(args[1]);
            string serverPath = Path.GetFullPath(args[2]);
            string clientPath = Path.GetFullPath(args[3]);
            string captureDirectory = Path.GetFullPath(args[4]);
            string outputPath = Path.GetFullPath(args[5]);
            if (File.Exists(outputPath)) throw new IOException("The merge output must not already exist.");
            SplitDescriptor descriptor = ReadSplit<SplitDescriptor>(descriptorPath);
            SplitServerReport server = ReadSplit<SplitServerReport>(serverPath);
            SplitClientReport client = ReadSplit<SplitClientReport>(clientPath);
            SplitMergeManifest manifest = Merge(descriptor, server, client, captureDirectory,
                Sha256File(serverPath), Sha256File(clientPath));
            WriteSplitJson(outputPath, manifest);
            Console.WriteLine($"RENDERED_WAN_MERGE result=PASS run={descriptor.RunId:D} captures={manifest.Captures.Length} renderedWanProof=false requiresHumanVisualReview=true");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"RENDERED_WAN_MERGE result=FAIL error={error.GetType().Name} reason={Bounded(error.Message)}");
            return 1;
        }
    }

    public static int SplitSelfTest()
    {
        string root = Path.Combine(Path.GetTempPath(), "prime-split-self-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            int cases = 0;
            _ = RequireCanonicalIPv4("127.0.0.1", "test", allowWildcard: false); cases++;
            ExpectFailure(() => RequirePort("0", "test")); cases++;
            ExpectFailure(() => RequireCanonicalIPv4("localhost", "test", allowWildcard: false)); cases++;
            ValidateWorkerEndpoint(IPAddress.Loopback, IPAddress.Loopback, 27020); cases++;
            ExpectFailure(() => ValidateWorkerEndpoint(IPAddress.Any, IPAddress.Loopback, 27020)); cases++;
            ExpectFailure(() => ValidateWorkerEndpoint(IPAddress.Parse("10.0.0.2"), IPAddress.Loopback, 27020)); cases++;

            Guid run = Guid.NewGuid(), node = Guid.NewGuid(), worker = Guid.NewGuid(), subject = Guid.NewGuid();
            Guid nodeIncarnation = Guid.NewGuid(), workerIncarnation = Guid.NewGuid(), match = Guid.NewGuid();
            DateTimeOffset issued = DateTimeOffset.UtcNow.AddSeconds(-1), expires = issued.AddSeconds(120);
            string hash = new('a', 64);
            var descriptor = new SplitDescriptor(SplitSchema, run, node, nodeIncarnation, worker,
                workerIncarnation, subject, issued, expires, "AMHE1", hash, "build", NetHeader.Version,
                "players", "MP1 SANCTORUS", "wss://127.0.0.1:27010/v1/control", "127.0.0.1", 27010,
                "127.0.0.1", "127.0.0.1", 27020, hash, new('b', 64), 12, 6, false, false);
            var binding = new SplitBinding(run, node, nodeIncarnation, worker, workerIncarnation,
                match, 42, subject, 3, "127.0.0.1", 27020, "AMHE1", hash, "build",
                NetHeader.Version, "players", "MP1 SANCTORUS");
            ValidateDescriptorShape(descriptor, issued.AddSeconds(2));
            ValidateBinding(descriptor, binding); cases++;
            ExpectFailure(() => ValidateBinding(descriptor, binding with { SubjectId = Guid.NewGuid() })); cases++;
            ExpectFailure(() => ValidateDescriptorShape(descriptor with { ValidationFixture = true }, issued.AddSeconds(2))); cases++;

            using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest("CN=split-self-test", key, HashAlgorithmName.SHA256);
            using X509Certificate2 cert = request.CreateSelfSigned(issued.AddMinutes(-1), expires.AddMinutes(1));
            byte[] certHash = SHA256.HashData(cert.RawData);
            byte[] spkiHash = SHA256.HashData(key.ExportSubjectPublicKeyInfo());
            if (!PinnedCertificateMatches(cert, Convert.ToHexString(certHash).ToLowerInvariant(),
                    Convert.ToHexString(spkiHash).ToLowerInvariant()))
                throw new InvalidOperationException("Exact certificate pin self-test failed.");
            byte[] wrong = certHash.ToArray(); wrong[0] ^= 0xff;
            if (PinnedCertificateMatches(cert, Convert.ToHexString(wrong).ToLowerInvariant(),
                    Convert.ToHexString(spkiHash).ToLowerInvariant()))
                throw new InvalidOperationException("Mismatched certificate pin was accepted.");
            cases += 2;

            var secret = new SplitSecret(SplitSchema, run, node, subject, expires, "header.payload.signature");
            string secretPath = Path.Combine(root, "secret.json");
            WriteSecret(secretPath, secret);
            if (ReadSecret(secretPath) != secret) throw new InvalidOperationException("Secret round-trip failed.");
            ExpectFailure(() => WriteSecret(secretPath, secret));
            cases += 2;

            string captures = Path.Combine(root, "captures"); Directory.CreateDirectory(captures);
            string capturePath = Path.Combine(captures, "frame-000060.png");
            File.WriteAllBytes(capturePath, [1, 2, 3, 4]);
            var capture = new SplitCapture("frame-000060.png", 640, 360, 0.5, true, Sha256File(capturePath));
            DateTimeOffset start = issued.AddSeconds(3), end = issued.AddSeconds(20);
            var server = new SplitServerReport(SplitSchema, "server", binding, start, end,
                MatchStatus.Completed.ToString(), 8, 8, 8, 0, 0, null, true, null, false, false, false, true);
            var client = new SplitClientReport(SplitSchema, "client", binding, start.AddSeconds(1), end.AddSeconds(-1),
                NetConnectionState.Playing.ToString(), true, true, true, true, true, 600, 600, 300,
                1, 3, 1, 1, [capture], true, null, false, false, false, true);
            SplitMergeManifest merged = Merge(descriptor, server, client, captures, hash, hash);
            if (!merged.Passed || merged.RenderedWanProof || merged.Qz1Accepted || merged.Qz5Accepted
                || !merged.RequiresHumanVisualReview || merged.EvidenceClass != "same-host-split-smoke-non-wan")
                throw new InvalidOperationException("Merge boundary self-test failed.");
            SplitDescriptor nonLoopbackDescriptor = descriptor with
            {
                NodeControlUri = "wss://198.51.100.10:27010/v1/control",
                NodeBind = "0.0.0.0", WorkerBind = "0.0.0.0", WorkerHost = "198.51.100.10"
            };
            SplitBinding nonLoopbackBinding = binding with { WorkerHost = "198.51.100.10" };
            SplitMergeManifest nonLoopback = Merge(nonLoopbackDescriptor,
                server with { Binding = nonLoopbackBinding }, client with { Binding = nonLoopbackBinding },
                captures, hash, hash);
            if (nonLoopback.EvidenceClass != "non-loopback-path-candidate-unverified")
                throw new InvalidOperationException("Non-loopback merge label self-test failed.");
            cases++;
            ExpectFailure(() => Merge(descriptor,
                server with { DebugQueued = 17, DebugRejected = 9, LastDebugRejectionCode = "debug" },
                client, captures, hash, hash)); cases++;
            File.WriteAllBytes(capturePath, [9, 9, 9]);
            ExpectFailure(() => Merge(descriptor, server, client, captures, hash, hash)); cases++;
            ExpectFailure(() => SplitOperatorOptions.Parse(["--rendered-wan-operator", root, Path.Combine(root, "new"),
                "--node-bind", "127.0.0.1", "--node-port", "1", "--node-uri", "wss://127.0.0.1:1/v1/control",
                "--tls-pfx", "missing", "--tls-password-file", "missing", "--worker-bind", "127.0.0.1",
                "--worker-host", "127.0.0.1", "--worker-port", "2", "--fixture", "unit1-rm1-dynamic"])); cases++;
            Console.WriteLine($"RENDERED_WAN_SPLIT_SELF_TEST result=PASS cases={cases}");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"RENDERED_WAN_SPLIT_SELF_TEST result=FAIL reason={Bounded(error.Message)}");
            return 1;
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static SplitMergeManifest Merge(SplitDescriptor descriptor, SplitServerReport server,
        SplitClientReport client, string captureDirectory, string serverReportHash, string clientReportHash)
    {
        ValidateDescriptorShape(descriptor, server.StartedUtc);
        if (server.Schema != SplitSchema || server.ReportType != "server"
            || client.Schema != SplitSchema || client.ReportType != "client")
            throw new InvalidDataException("Split report schema/type is invalid.");
        ValidateBinding(descriptor, server.Binding);
        ValidateBinding(descriptor, client.Binding);
        if (server.Binding != client.Binding) throw new InvalidDataException("Client/server binding fields differ.");
        ValidateAcceptanceBoundary(server.RenderedWanProof, server.Qz1Accepted, server.Qz5Accepted,
            server.RequiresHumanVisualReview);
        ValidateAcceptanceBoundary(client.RenderedWanProof, client.Qz1Accepted, client.Qz5Accepted,
            client.RequiresHumanVisualReview);
        if (!server.Passed || server.FailureReason != null || server.MatchStatus != MatchStatus.Completed.ToString()
            || server.DebugAttempts < 1 || server.DebugQueued < 1 || server.DebugApplied < 8
            || server.DebugAttempts < server.DebugQueued || server.DebugAttempts > 512
            || server.DebugQueued != server.DebugApplied + server.DebugRejected
            || server.DebugRejected > 8 || server.UnexpectedDebugRejected != 0
            || !(server.DebugRejected == 0 && server.LastDebugRejectionCode == null
                || server.DebugRejected > 0 && server.LastDebugRejectionCode == "debug"))
            throw new InvalidDataException("Server report contains a failure state.");
        if (!client.Passed || client.FailureReason != null || !client.RuntimePassed
            || !client.ReconnectCompleted || !client.ReconnectSameMatch || !client.ReconnectSameSeat
            || !client.ConnectionIdentityRotated || client.FinalState != NetConnectionState.Playing.ToString())
            throw new InvalidDataException("Client report contains a failure state.");
        ValidateInterval(server.StartedUtc, server.EndedUtc);
        ValidateInterval(client.StartedUtc, client.EndedUtc);
        DateTimeOffset overlapStart = server.StartedUtc > client.StartedUtc ? server.StartedUtc : client.StartedUtc;
        DateTimeOffset overlapEnd = server.EndedUtc < client.EndedUtc ? server.EndedUtc : client.EndedUtc;
        if (overlapStart > overlapEnd.AddSeconds(30)) throw new InvalidDataException("Client/server timestamps do not overlap.");
        if (server.StartedUtc < descriptor.IssuedUtc.AddSeconds(-30)
            || client.StartedUtc < descriptor.IssuedUtc.AddSeconds(-30)
            || server.EndedUtc > descriptor.ExpiresUtc.AddMinutes(15)
            || client.EndedUtc > descriptor.ExpiresUtc.AddMinutes(15))
            throw new InvalidDataException("Report timestamps fall outside the bounded admission/run window.");
        if (client.Captures is null || client.Captures.Length is < 1 or > 64)
            throw new InvalidDataException("Client capture inventory is missing or oversized.");
        string root = Path.GetFullPath(captureDirectory) + Path.DirectorySeparatorChar;
        var verified = new List<SplitCapture>(client.Captures.Length);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (SplitCapture capture in client.Captures)
        {
            if (!capture.Saved || capture.Width is < 1 or > 4096 || capture.Height is < 1 or > 4096
                || !double.IsFinite(capture.NonBlackFraction) || capture.NonBlackFraction is < 0 or > 1
                || Path.GetFileName(capture.File) != capture.File || !names.Add(capture.File)
                || !ValidSha256(capture.Sha256)) throw new InvalidDataException("Invalid capture entry.");
            string path = Path.GetFullPath(Path.Combine(captureDirectory, capture.File));
            if (!path.StartsWith(root, StringComparison.Ordinal) || !File.Exists(path))
                throw new InvalidDataException("Capture path escaped or is missing.");
            if (!FixedHexEquals(Sha256File(path), capture.Sha256))
                throw new InvalidDataException("Capture hash mismatch.");
            verified.Add(capture);
        }
        RequireHash(serverReportHash, "server report"); RequireHash(clientReportHash, "client report");
        bool loopbackSmoke = IPAddress.IsLoopback(IPAddress.Parse(descriptor.WorkerHost))
            || new Uri(descriptor.NodeControlUri).Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || IPAddress.TryParse(new Uri(descriptor.NodeControlUri).Host, out IPAddress? advertisedNode)
                && IPAddress.IsLoopback(advertisedNode);
        return new SplitMergeManifest(SplitSchema,
            loopbackSmoke ? "same-host-split-smoke-non-wan" : "non-loopback-path-candidate-unverified",
            descriptor.RunId,
            server.Binding, server.StartedUtc, server.EndedUtc, client.StartedUtc, client.EndedUtc,
            serverReportHash, clientReportHash, verified.ToArray(), true,
            RenderedWanProof: false, Qz1Accepted: false, Qz5Accepted: false,
            RequiresHumanVisualReview: true,
            Limitations: ["Candidate evidence only; automated merge cannot prove an Internet path.",
                "Every rendered capture requires manual human visual review.",
                "Ordinary production multiplayer content only; developer fixtures and public diagnostics are prohibited."]);
    }

    private static void ValidateAcceptanceBoundary(bool proof, bool qz1, bool qz5, bool human)
    {
        if (proof || qz1 || qz5 || !human)
            throw new InvalidDataException("A split report crossed the non-acceptance/human-review boundary.");
    }

    private static void ValidateInterval(DateTimeOffset start, DateTimeOffset end)
    {
        if (start == default || end < start || end - start > TimeSpan.FromMinutes(15))
            throw new InvalidDataException("Report timestamp interval is invalid.");
    }

    private static void ValidateHandoff(SplitDescriptor descriptor, NodeMatchHandoff handoff)
    {
        if (!String.Equals(handoff.Host, descriptor.WorkerHost, StringComparison.Ordinal)
            || handoff.Port != descriptor.WorkerPort || handoff.Observer || handoff.Ticket.Length is < 1 or > 4096)
            throw new InvalidOperationException("Node handoff does not match the advertised direct Worker endpoint.");
    }

    private static void ValidateDescriptor(SplitDescriptor descriptor, SplitSecret secret, DateTimeOffset now)
    {
        ValidateDescriptorShape(descriptor, now, resolveNodeHost: true);
        if (secret.Schema != SplitSchema || secret.RunId != descriptor.RunId
            || secret.NodeId != descriptor.NodeId || secret.SubjectId != descriptor.SubjectId
            || secret.ExpiresUtc != descriptor.ExpiresUtc || secret.Ticket is not { Length: >= 1 and <= 4096 }
            || secret.Ticket.Any(c => c > 127 || char.IsWhiteSpace(c)))
            throw new InvalidDataException("Secret does not match the public descriptor.");
    }

    private static void ValidateDescriptorShape(SplitDescriptor descriptor, DateTimeOffset now,
        bool resolveNodeHost = false)
    {
        if (descriptor.Schema != SplitSchema || descriptor.RunId == Guid.Empty || descriptor.NodeId == Guid.Empty
            || descriptor.NodeIncarnation == Guid.Empty || descriptor.WorkerId == Guid.Empty
            || descriptor.WorkerIncarnation == Guid.Empty || descriptor.SubjectId == Guid.Empty
            || descriptor.ValidationFixture || descriptor.PublicDiagnostics
            || descriptor.IssuedUtc == default || descriptor.ExpiresUtc <= descriptor.IssuedUtc
            || descriptor.ExpiresUtc - descriptor.IssuedUtc > TimeSpan.FromSeconds(120)
            || descriptor.ExpiresUtc <= now || descriptor.Seconds is < 12 or > 60
            || descriptor.ReconnectAt < 4 || descriptor.ReconnectAt > descriptor.Seconds - 3
            || descriptor.ProtocolVersion != NetHeader.Version
            || descriptor.Mode is not ("off" or "players" or "dynamic")
            || descriptor.Room.Length is < 1 or > 128 || descriptor.Room.Any(char.IsControl)
            || descriptor.ContentVersion.Length is < 1 or > 128
            || descriptor.ContentHash.Length is < 1 or > 128
            || descriptor.BuildVersion.Length is < 1 or > 128)
            throw new InvalidDataException("Public split descriptor is invalid or expired.");
        RequireHash(descriptor.CertificateSha256, "certificate");
        RequireHash(descriptor.SpkiSha256, "SPKI");
        IPAddress nodeBind = RequireCanonicalIPv4(descriptor.NodeBind, "Node bind", allowWildcard: true);
        Uri nodeUri = ValidateNodeEndpoint(nodeBind, descriptor.NodePort, descriptor.NodeControlUri,
            resolveNodeHost);
        IPAddress workerBind = RequireCanonicalIPv4(descriptor.WorkerBind, "Worker bind", allowWildcard: true);
        IPAddress workerHost = RequireCanonicalIPv4(descriptor.WorkerHost, "Worker host", allowWildcard: false);
        ValidateWorkerEndpoint(workerBind, workerHost, descriptor.WorkerPort);
        if (nodeUri.Port != descriptor.NodePort) throw new InvalidDataException("Node URI/port differ.");
    }

    private static void ValidateBinding(SplitDescriptor descriptor, SplitBinding binding)
    {
        if (binding.RunId != descriptor.RunId || binding.NodeId != descriptor.NodeId
            || binding.NodeIncarnation != descriptor.NodeIncarnation || binding.WorkerId != descriptor.WorkerId
            || binding.WorkerIncarnation != descriptor.WorkerIncarnation || binding.SubjectId != descriptor.SubjectId
            || binding.MatchId == Guid.Empty || binding.WireMatchId == 0 || binding.SeatId >= 8
            || binding.WorkerHost != descriptor.WorkerHost || binding.WorkerPort != descriptor.WorkerPort
            || binding.ContentVersion != descriptor.ContentVersion || binding.ContentHash != descriptor.ContentHash
            || binding.BuildVersion != descriptor.BuildVersion || binding.ProtocolVersion != descriptor.ProtocolVersion
            || binding.Mode != descriptor.Mode || binding.Room != descriptor.Room)
            throw new InvalidDataException("Run, Node, Worker, match, subject, endpoint, or content binding mismatch.");
    }

    private static NodeControlClient CreateSplitControlClient(SplitDescriptor descriptor, bool allowLabPin)
    {
        if (!allowLabPin) return new NodeControlClient();
        var socket = new ClientWebSocket();
        socket.Options.RemoteCertificateValidationCallback = (_, certificate, _, _) =>
        {
            if (certificate == null) return false;
            if (certificate is X509Certificate2 candidate)
                return PinnedCertificateMatches(candidate, descriptor.CertificateSha256, descriptor.SpkiSha256);
            using var owned = new X509Certificate2(certificate);
            return PinnedCertificateMatches(owned, descriptor.CertificateSha256, descriptor.SpkiSha256);
        };
        return new NodeControlClient(socket);
    }

    private static bool PinnedCertificateMatches(X509Certificate2 certificate, string certificateSha256,
        string spkiSha256)
    {
        if (!TryDecodeHash(certificateSha256, out byte[]? expectedCertificate)
            || !TryDecodeHash(spkiSha256, out byte[]? expectedSpki)) return false;
        byte[] actualCertificate = SHA256.HashData(certificate.RawData);
        byte[] actualSpki;
        using (RSA? rsa = certificate.GetRSAPublicKey())
        using (ECDsa? ecdsa = certificate.GetECDsaPublicKey())
        {
            if (rsa != null) actualSpki = SHA256.HashData(rsa.ExportSubjectPublicKeyInfo());
            else if (ecdsa != null) actualSpki = SHA256.HashData(ecdsa.ExportSubjectPublicKeyInfo());
            else return false;
        }
        return CryptographicOperations.FixedTimeEquals(actualCertificate, expectedCertificate)
            && CryptographicOperations.FixedTimeEquals(actualSpki, expectedSpki);
    }

    private static SplitSecret ReadSecret(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            UnixFileMode mode = File.GetUnixFileMode(path);
            if ((mode & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
                throw new UnauthorizedAccessException("The secret file must be user-only (chmod 600).");
        }
        return ReadSplit<SplitSecret>(path);
    }

    private static T ReadSplit<T>(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length is < 2 or > 1048576) throw new InvalidDataException("Split JSON is missing or oversized.");
        byte[] bytes = File.ReadAllBytes(path);
        using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        NodeControlCodec.RejectDuplicates(document.RootElement);
        return JsonSerializer.Deserialize<T>(bytes, SplitJson) ?? throw new JsonException("Missing split JSON object.");
    }

    private static void WriteSplitJson(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, value.GetType(), SplitJson) + Environment.NewLine);
    }

    private static void WriteSecret(string path, SplitSecret value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, SplitJson);
        var options = new FileStreamOptions
        {
            Access = FileAccess.Write,
            Mode = FileMode.CreateNew,
            Share = FileShare.None,
            Options = FileOptions.WriteThrough
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using var stream = new FileStream(path, options);
        stream.Write(bytes);
        stream.WriteByte((byte)'\n');
        stream.Flush(flushToDisk: true);
    }

    private static void WriteSplitFailure(string path, string reportType, Exception error)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            WriteSplitJson(path, new
            {
                schema = SplitSchema, reportType, passed = false,
                failureReason = Bounded(error.Message), renderedWanProof = false,
                qz1Accepted = false, qz5Accepted = false, requiresHumanVisualReview = true
            });
        }
        catch { }
    }

    private static string Sha256File(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void RequireHash(string value, string name)
    {
        if (!ValidSha256(value)) throw new InvalidDataException($"Invalid {name} SHA-256.");
    }

    private static bool ValidSha256(string? value) => value is { Length: 64 }
        && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool TryDecodeHash(string value, out byte[]? bytes)
    {
        bytes = null;
        if (!ValidSha256(value)) return false;
        try { bytes = Convert.FromHexString(value); return true; }
        catch (FormatException) { return false; }
    }

    private static bool FixedHexEquals(string left, string right)
        => TryDecodeHash(left, out byte[]? a) && TryDecodeHash(right, out byte[]? b)
            && CryptographicOperations.FixedTimeEquals(a, b);

    private static void ExpectFailure(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is ArgumentException or InvalidDataException
            or IOException or UnauthorizedAccessException or CryptographicException) { return; }
        throw new InvalidOperationException("Expected a fail-closed validation failure.");
    }

    private static void RequireUserOnlyFile(string path, string name)
    {
        if (OperatingSystem.IsWindows()) return;
        UnixFileMode mode = File.GetUnixFileMode(path);
        if ((mode & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
            throw new UnauthorizedAccessException($"{name} must be user-only (chmod 600).");
    }

    private static void RequireOrdinaryRoom(string room)
    {
        if (DeveloperValidationFixtures.Require(DeveloperValidationFixtureId.Unit1Rm1Dynamic).MapKey == room)
            throw new ArgumentException("Developer validation fixtures are prohibited in split mode.");
        (RoomMetadata? metadata, _) = Metadata.GetRoomByName(room);
        if (metadata == null || !metadata.Multiplayer)
            throw new ArgumentException("Split mode requires an ordinary production multiplayer room.");
    }

    private static IPAddress RequireCanonicalIPv4(string value, string name, bool allowWildcard)
    {
        if (!IPAddress.TryParse(value, out IPAddress? address) || address.AddressFamily != AddressFamily.InterNetwork
            || address.ToString() != value || (!allowWildcard && address.Equals(IPAddress.Any))
            || address.Equals(IPAddress.Broadcast) || IsIPv4Multicast(address))
            throw new ArgumentException($"{name} must be a canonical IPv4 address.");
        return address;
    }

    private static ushort RequirePort(string value, string name)
    {
        if (!ushort.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ushort port) || port == 0)
            throw new ArgumentException($"{name} must be a fixed nonzero port.");
        return port;
    }

    private static void ValidateWorkerEndpoint(IPAddress bind, IPAddress advertised, ushort port)
    {
        if (port == 0 || advertised.Equals(IPAddress.Any) || advertised.Equals(IPAddress.Broadcast)
            || (!IPAddress.IsLoopback(bind) && IPAddress.IsLoopback(advertised))
            || (IPAddress.IsLoopback(bind) && !IPAddress.IsLoopback(advertised)))
            throw new ArgumentException("Worker bind/advertised endpoint is unsafe or unreachable.");
    }

    private static Uri ValidateNodeEndpoint(IPAddress bind, ushort port, string raw,
        bool resolveHostname = false)
    {
        if (port == 0 || !Uri.TryCreate(raw, UriKind.Absolute, out Uri? uri) || uri.Scheme != "wss"
            || uri.Port != port || uri.AbsolutePath != "/v1/control" || uri.UserInfo.Length != 0
            || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.HostNameType == UriHostNameType.Unknown)
            throw new ArgumentException("Node endpoint must be an exact advertised wss://host:port/v1/control URI.");
        if (IPAddress.TryParse(uri.Host, out IPAddress? advertised))
        {
            if (advertised.AddressFamily != AddressFamily.InterNetwork || advertised.Equals(IPAddress.Any)
                || advertised.Equals(IPAddress.Broadcast) || IsIPv4Multicast(advertised)
                || (!IPAddress.IsLoopback(bind) && IPAddress.IsLoopback(advertised))
                || (IPAddress.IsLoopback(bind) && !IPAddress.IsLoopback(advertised)))
                throw new ArgumentException("Node advertised address is unsafe or unreachable from its bind.");
        }
        else if (IPAddress.IsLoopback(bind) || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            if (!IPAddress.IsLoopback(bind) || !uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Loopback Node bind and advertisement must agree.");
        }
        else if (resolveHostname)
        {
            IPAddress[] addresses = Dns.GetHostAddresses(uri.DnsSafeHost)
                .Where(value => value.AddressFamily == AddressFamily.InterNetwork).ToArray();
            if (addresses.Length is < 1 or > 64 || addresses.Any(value => IPAddress.IsLoopback(value)
                || value.Equals(IPAddress.Any) || value.Equals(IPAddress.Broadcast) || IsIPv4Multicast(value)))
                throw new ArgumentException("Node advertised hostname does not resolve exclusively to bounded non-loopback IPv4 addresses.");
        }
        return uri;
    }

    private static bool IsIPv4Multicast(IPAddress address)
    {
        byte first = address.GetAddressBytes()[0];
        return first is >= 224 and <= 239;
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record SplitDescriptor(string Schema, Guid RunId, Guid NodeId, Guid NodeIncarnation,
        Guid WorkerId, Guid WorkerIncarnation, Guid SubjectId, DateTimeOffset IssuedUtc,
        DateTimeOffset ExpiresUtc, string ContentVersion, string ContentHash, string BuildVersion,
        byte ProtocolVersion, string Mode, string Room, string NodeControlUri, string NodeBind,
        ushort NodePort, string WorkerBind, string WorkerHost, ushort WorkerPort,
        string CertificateSha256, string SpkiSha256, int Seconds, int ReconnectAt,
        bool ValidationFixture, bool PublicDiagnostics);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record SplitSecret(string Schema, Guid RunId, Guid NodeId, Guid SubjectId,
        DateTimeOffset ExpiresUtc, string Ticket);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record SplitBinding(Guid RunId, Guid NodeId, Guid NodeIncarnation, Guid WorkerId,
        Guid WorkerIncarnation, Guid MatchId, uint WireMatchId, Guid SubjectId, byte SeatId,
        string WorkerHost, ushort WorkerPort, string ContentVersion, string ContentHash,
        string BuildVersion, byte ProtocolVersion, string Mode, string Room);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record SplitCapture(string File, int Width, int Height, double NonBlackFraction,
        bool Saved, string Sha256);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record SplitServerReport(string Schema, string ReportType, SplitBinding Binding,
        DateTimeOffset StartedUtc, DateTimeOffset EndedUtc, string MatchStatus,
        int DebugAttempts, int DebugQueued, int DebugApplied, int DebugRejected,
        int UnexpectedDebugRejected, string? LastDebugRejectionCode, bool Passed, string? FailureReason,
        bool RenderedWanProof, bool Qz1Accepted, bool Qz5Accepted, bool RequiresHumanVisualReview);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record SplitClientReport(string Schema, string ReportType, SplitBinding Binding,
        DateTimeOffset StartedUtc, DateTimeOffset EndedUtc, string FinalState,
        bool RuntimePassed, bool ReconnectCompleted, bool ReconnectSameMatch, bool ReconnectSameSeat,
        bool ConnectionIdentityRotated, int SubmittedFrames, int AcknowledgedFrames, long Snapshots,
        long CombatEvents, int DebugPackets, int HistoryDebugPackets, int DynamicDebugPackets, SplitCapture[] Captures,
        bool Passed, string? FailureReason, bool RenderedWanProof, bool Qz1Accepted,
        bool Qz5Accepted, bool RequiresHumanVisualReview);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record SplitMergeManifest(string Schema, string EvidenceClass, Guid RunId,
        SplitBinding Binding, DateTimeOffset ServerStartedUtc, DateTimeOffset ServerEndedUtc,
        DateTimeOffset ClientStartedUtc, DateTimeOffset ClientEndedUtc,
        string ServerReportSha256, string ClientReportSha256, SplitCapture[] Captures, bool Passed,
        bool RenderedWanProof, bool Qz1Accepted, bool Qz5Accepted,
        bool RequiresHumanVisualReview, string[] Limitations);

    private sealed record SplitOperatorOptions(string DataDirectory, string OutputDirectory,
        IPAddress NodeBind, ushort NodePort, string NodeControlUri, string TlsPfx,
        string TlsPasswordFile, IPAddress WorkerBind, IPAddress WorkerHost, ushort WorkerPort,
        WorkerLagCompensationMode Mode, string Room, int Seconds, int ReconnectAt, int WaitSeconds)
    {
        public static SplitOperatorOptions Parse(string[] args)
        {
            if (args.Length < 3 || (args.Length - 3) % 2 != 0)
                throw new ArgumentException("Expected DATA OUTPUT_DIRECTORY followed by option/value pairs.");
            string data = Path.GetFullPath(args[1]);
            string output = Path.GetFullPath(args[2]);
            if (!Directory.Exists(data)) throw new DirectoryNotFoundException("The content directory does not exist.");
            if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
                throw new IOException("The output directory must be new or empty.");
            var values = ParsePairs(args, 3,
                ["--node-bind", "--node-port", "--node-uri", "--tls-pfx", "--tls-password-file",
                    "--worker-bind", "--worker-host", "--worker-port", "--mode", "--room",
                    "--seconds", "--reconnect-at", "--wait-seconds"]);
            string Required(string name) => values.TryGetValue(name, out string? value) && value.Length > 0
                ? value : throw new ArgumentException($"{name} is required.");
            IPAddress nodeBind = RequireCanonicalIPv4(Required("--node-bind"), "Node bind", allowWildcard: true);
            ushort nodePort = RequirePort(Required("--node-port"), "Node port");
            string nodeUri = Required("--node-uri");
            _ = ValidateNodeEndpoint(nodeBind, nodePort, nodeUri, resolveHostname: true);
            IPAddress workerBind = RequireCanonicalIPv4(Required("--worker-bind"), "Worker bind", allowWildcard: true);
            IPAddress workerHost = RequireCanonicalIPv4(Required("--worker-host"), "Worker host", allowWildcard: false);
            ushort workerPort = RequirePort(Required("--worker-port"), "Worker port");
            ValidateWorkerEndpoint(workerBind, workerHost, workerPort);
            string pfx = Path.GetFullPath(Required("--tls-pfx"));
            string password = Path.GetFullPath(Required("--tls-password-file"));
            if (!File.Exists(pfx) || new FileInfo(pfx).Length is < 1 or > 1048576)
                throw new FileNotFoundException("TLS PFX is missing or oversized.");
            if (!File.Exists(password) || new FileInfo(password).Length is < 1 or > 4096)
                throw new FileNotFoundException("TLS password file is missing or oversized.");
            RequireUserOnlyFile(pfx, "TLS PFX");
            RequireUserOnlyFile(password, "TLS password file");
            int seconds = Number(values, "--seconds", 15);
            int reconnect = Number(values, "--reconnect-at", Math.Max(6, seconds / 2));
            int wait = Number(values, "--wait-seconds", 180);
            if (seconds is < 12 or > 60 || reconnect < 4 || reconnect > seconds - 3
                || wait is < 60 or > 600) throw new ArgumentOutOfRangeException(nameof(args), "Split duration limits are invalid.");
            string room = values.GetValueOrDefault("--room", "MP1 SANCTORUS");
            if (room.Length is < 1 or > 128 || room.Any(char.IsControl)) throw new ArgumentException("Room is invalid.");
            WorkerLagCompensationMode mode = WorkerOptions.ParseLagCompensationMode(values.GetValueOrDefault("--mode", "players"));
            return new(data, output, nodeBind, nodePort, nodeUri, pfx, password,
                workerBind, workerHost, workerPort, mode, room, seconds, reconnect, wait);
        }
    }

    private sealed record SplitClientOptions(string DataDirectory, string DescriptorPath,
        string SecretPath, string OutputDirectory, bool AllowLabPin, int Width, int Height)
    {
        public static SplitClientOptions Parse(string[] args)
        {
            if (args.Length < 5 || (args.Length - 5) % 2 != 0)
                throw new ArgumentException("Expected DATA DESCRIPTOR_JSON SECRET_JSON OUTPUT_DIRECTORY followed by option/value pairs.");
            string data = Path.GetFullPath(args[1]);
            string descriptor = Path.GetFullPath(args[2]);
            string secret = Path.GetFullPath(args[3]);
            string output = Path.GetFullPath(args[4]);
            if (!Directory.Exists(data)) throw new DirectoryNotFoundException("The content directory does not exist.");
            if (!File.Exists(descriptor) || !File.Exists(secret)) throw new FileNotFoundException("Descriptor or secret file is missing.");
            if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
                throw new IOException("The output directory must be new or empty.");
            var values = ParsePairs(args, 5, ["--allow-lab-pin", "--width", "--height"]);
            string rawPin = values.GetValueOrDefault("--allow-lab-pin", "false");
            bool pin = rawPin switch { "true" => true, "false" => false,
                _ => throw new ArgumentException("--allow-lab-pin must be true or false.") };
            int width = Number(values, "--width", 640), height = Number(values, "--height", 360);
            if (width is < 320 or > 1920 || height is < 180 or > 1080)
                throw new ArgumentOutOfRangeException(nameof(args), "Rendered size is invalid.");
            return new(data, descriptor, secret, output, pin, width, height);
        }
    }

    private static Dictionary<string, string> ParsePairs(string[] args, int start, string[] allowed)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = start; i < args.Length; i += 2)
        {
            if (!allowed.Contains(args[i], StringComparer.Ordinal) || !values.TryAdd(args[i], args[i + 1]))
                throw new ArgumentException("Unknown or duplicate split validation option.");
        }
        return values;
    }

    private static int Number(IReadOnlyDictionary<string, string> values, string name, int fallback)
    {
        if (!values.TryGetValue(name, out string? raw)) return fallback;
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            throw new ArgumentException($"{name} requires an integer.");
        return value;
    }

    private sealed class SplitOperatorNode : IAsyncDisposable
    {
        private readonly string _temporaryRoot = Path.Combine(Path.GetTempPath(),
            "prime-rendered-wan-operator-" + Guid.NewGuid().ToString("N"));
        private readonly ECDsa _signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly X509Certificate2 _certificate;
        private readonly string _publicKeyPath;
        public Guid NodeId { get; } = Guid.NewGuid();
        public WebApplication App { get; }
        public string CertificateSha256 { get; }
        public string SpkiSha256 { get; }

        public SplitOperatorNode(WorkerContentIdentity content, SplitOperatorOptions options)
        {
            Directory.CreateDirectory(_temporaryRoot);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(_temporaryRoot, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            _publicKeyPath = Path.Combine(_temporaryRoot, "admission-public.pem");
            File.WriteAllText(_publicKeyPath, _signingKey.ExportSubjectPublicKeyInfoPem());
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(_publicKeyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            string artifacts = Path.Combine(_temporaryRoot, "artifacts");
            string replays = Path.Combine(_temporaryRoot, "replays");
            Directory.CreateDirectory(artifacts); Directory.CreateDirectory(replays);

            string password = File.ReadAllText(options.TlsPasswordFile).TrimEnd('\r', '\n');
            if (password.Length is < 1 or > 4096 || password.Any(char.IsControl))
                throw new InvalidDataException("TLS password file is invalid.");
            X509KeyStorageFlags keyStorage = OperatingSystem.IsMacOS()
                ? X509KeyStorageFlags.DefaultKeySet
                : X509KeyStorageFlags.EphemeralKeySet;
            _certificate = X509CertificateLoader.LoadPkcs12FromFile(options.TlsPfx, password,
                keyStorage);
            DateTimeOffset certificateNotBefore = new(_certificate.NotBefore.ToUniversalTime());
            DateTimeOffset certificateNotAfter = new(_certificate.NotAfter.ToUniversalTime());
            if (!_certificate.HasPrivateKey || DateTimeOffset.UtcNow < certificateNotBefore
                || DateTimeOffset.UtcNow >= certificateNotAfter)
                throw new CryptographicException("TLS certificate is not currently valid or lacks a private key.");
            CertificateSha256 = Convert.ToHexString(SHA256.HashData(_certificate.RawData)).ToLowerInvariant();
            SpkiSha256 = CertificateSpkiSha256(_certificate);

            var workerArguments = new List<string>
            {
                typeof(WorkerOptions).Assembly.Location,
                "--content-dir", options.DataDirectory,
                "--content-version", content.ContentVersion,
                "--content-hash", content.ContentHash,
                "--lanes", "1", "--max-matches", "1", "--max-matches-per-lane", "1",
                "--lag-compensation-mode", options.Mode.ToString().ToLowerInvariant(),
                "--bind", options.WorkerBind.ToString(), "--host", options.WorkerHost.ToString(),
                "--port", options.WorkerPort.ToString(CultureInfo.InvariantCulture),
                "--replay-dir", replays
            };
            var launch = new WorkerLaunchOptions
            {
                FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
                Content = content, Arguments = workerArguments, ArtifactDirectory = artifacts,
                Capacity = new(1, 2, 0, 0), StartupTimeout = TimeSpan.FromSeconds(30),
                ShutdownTimeout = TimeSpan.FromSeconds(3)
            };
            var map = new ContentIdentity(options.Room, content.ContentHash, content.ContentVersion,
                content.BuildVersion, content.ProtocolVersion);
            App = NodeApplication.Build([], builder =>
            {
                builder.Logging.ClearProviders();
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Node:Authentication:NodeId"] = NodeId.ToString("D"),
                    ["Node:Authentication:Issuer"] = SplitIssuer,
                    ["Node:Authentication:Keys:0:KeyId"] = "ephemeral",
                    ["Node:Authentication:Keys:0:PublicKeyPemPath"] = _publicKeyPath
                });
                builder.Configuration.AddJsonStream(new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(new
                {
                    Node = new
                    {
                        Maps = new[] { map },
                        Workers = new { Processes = new[] { launch }, DrainTimeout = "00:00:02", ForceAfterDrainDeadline = true }
                    }
                })));
                builder.WebHost.ConfigureKestrel(server => server.Listen(options.NodeBind,
                    options.NodePort, listen => listen.UseHttps(_certificate)));
            });
        }

        public NodeAdmissionTicket CreateAdmission(Guid subject, DateTimeOffset issued, DateTimeOffset expires)
        {
            string token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = SplitIssuer, Audience = NodeAdmissionValidator.Audience(NodeId),
                TokenType = NodeAdmissionValidator.TokenType, IssuedAt = issued.UtcDateTime,
                NotBefore = issued.UtcDateTime, Expires = expires.UtcDateTime,
                SigningCredentials = new(new ECDsaSecurityKey(_signingKey) { KeyId = "ephemeral" }, "ES256"),
                Claims = new Dictionary<string, object>
                {
                    ["sub"] = subject.ToString("D"), ["jti"] = Guid.NewGuid().ToString("D"), ["name"] = "WanProbe"
                }
            });
            return new NodeAdmissionTicket(token, expires, NodeId, "");
        }

        public async ValueTask DisposeAsync()
        {
            await App.DisposeAsync();
            _certificate.Dispose(); _signingKey.Dispose();
            if (Directory.Exists(_temporaryRoot)) Directory.Delete(_temporaryRoot, recursive: true);
        }
    }

    private static string CertificateSpkiSha256(X509Certificate2 certificate)
    {
        byte[] spki;
        using (RSA? rsa = certificate.GetRSAPublicKey())
        using (ECDsa? ecdsa = certificate.GetECDsaPublicKey())
        {
            if (rsa != null) spki = rsa.ExportSubjectPublicKeyInfo();
            else if (ecdsa != null) spki = ecdsa.ExportSubjectPublicKeyInfo();
            else throw new CryptographicException("TLS certificate public key must be RSA or ECDSA.");
        }
        return Convert.ToHexString(SHA256.HashData(spki)).ToLowerInvariant();
    }
}
