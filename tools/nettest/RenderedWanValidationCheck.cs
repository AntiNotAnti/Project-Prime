using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ProjectPrime.Server.Node;
using ProjectPrime.Server.Node.Identity;
using ProjectPrime.Server.Node.Lobbies;
using ProjectPrime.Server.Node.Workers;
using ProjectPrime.Server.Shared;
using ProjectPrime.Server.Worker;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MphRead.Entities;
using MphRead.Combat;
using MphRead.Mods.Accounts;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

/// <summary>
/// Rendered, process-local WAN-impairment validation through the production
/// Node control plane and an external Worker process. This is deliberately a
/// developer executable, not a player/admin endpoint.
/// </summary>
internal static partial class RenderedWanValidationCheck
{
    private const string Schema = "project-prime.rendered-wan-validation.v1";

    public static int Run(string[] args)
    {
        Options? options = null;
        try
        {
            options = Options.Parse(args);
            SynchronizationContext? prior = SynchronizationContext.Current;
            using var context = new MainThreadSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                Task<int> operation = RunAsync(options);
                context.Run(operation);
                return operation.GetAwaiter().GetResult();
            }
            finally { SynchronizationContext.SetSynchronizationContext(prior); }
        }
        catch (Exception error)
        {
            string reason = Bounded(error.Message);
            Console.Error.WriteLine($"RENDERED_WAN_VALIDATION result=FAIL error={error.GetType().Name} reason={reason}");
            if (options != null)
            {
                WriteJson(Path.Combine(options.OutputDirectory, "report.json"), new
                {
                    schema = Schema,
                    passed = false,
                    evidenceClass = "rendered-loopback-process-local-impairment",
                    renderedWanProof = false,
                    qz1Accepted = false,
                    qz5Accepted = false,
                    requiresHumanVisualReview = true,
                    dynamicScenarioAvailable = false,
                    run = new RenderedWanRunIdentity(options.RunId,
                        RenderedWanScenarioParser.Format(options.Scenario), options.StartedUtc,
                        new RenderedWanClientIdentity(null, null, null, null, null),
                        new RenderedWanWorkerIdentity(null, null, null),
                        new RenderedWanNodeIdentity(null, null, null), null, null, null),
                    classification = "HARNESS INVALID",
                    failure = new { type = error.GetType().Name, reason }
                });
            }
            NetSession.Stop();
            return 1;
        }
    }

    private static async Task<int> RunAsync(Options options)
    {
        if (!File.Exists(Path.Combine(options.OutputDirectory, ".run-reservation")))
            throw new IOException("The validation output directory was not exclusively reserved.");
        string captureDirectory = Path.Combine(options.OutputDirectory, "captures");
        Directory.CreateDirectory(captureDirectory);

        RenderBackendSelection.ResetForTests();
        if (RenderBackendSelection.Current != RenderBackendKind.Sdl)
            throw new PlatformNotSupportedException("Rendered WAN validation requires the SDL renderer.");
        if (!NetLag.Configure(FormattableString.Invariant($"{options.RoundTripMs}:{options.JitterMs}"))
            || !NetLag.ConfigureLoss(options.LossPercent.ToString(CultureInfo.InvariantCulture)))
            throw new InvalidOperationException("The process-local network impairment could not be configured.");

        ContentEnvironment.Open(options.DataDirectory, "AMHE1");
        (string contentVersion, string contentHash) = ContentEnvironment.GetContentIdentity();
        DeveloperValidationFixtureDescriptor? fixture = options.ValidationFixture == DeveloperValidationFixtureId.None
            ? null : DeveloperValidationFixtures.Require(options.ValidationFixture);
        DeveloperValidationFixtureContentEvidence? fixtureEvidence = fixture == null
            ? null : DeveloperValidationFixtures.ValidateCurrentContent(options.ValidationFixture);
        var workerContent = new WorkerContentIdentity(contentVersion, contentHash,
            WorkerOptions.ActualBuildVersion, NetHeader.Version);

        await using var node = new EphemeralNode(workerContent, options);
        var runIdentity = new RenderedWanRunIdentity(options.RunId,
            RenderedWanScenarioParser.Format(options.Scenario), options.StartedUtc,
            new RenderedWanClientIdentity(null, null, null, null, null),
            new RenderedWanWorkerIdentity(null, null, null),
            new RenderedWanNodeIdentity(node.NodeId, null, null), null, null, null);
        // Host services must not inherit the Cocoa main-thread pump. Their
        // Worker IPC readers/writers remain ordinary thread-pool async work
        // while only this orchestration continuation returns to the UI owner.
        Task nodeStart = StartWithoutSynchronizationContext(() => node.App.StartAsync());
        await nodeStart;
        runIdentity = runIdentity with
        {
            Node = runIdentity.Node with { Port = ParsePort(node.App.Urls.Single()) }
        };
        try
        {
            WorkerSnapshot[] readyWorkers = [];
            await UntilAsync(() =>
            {
                WorkerSnapshot[] workers = node.App.Services.GetRequiredService<WorkerManager>().Snapshot().ToArray();
                if (workers.Any(worker => worker.FailureReason != null))
                    throw new InvalidOperationException("The external Worker failed during startup.");
                return workers.Length == 1 && workers[0].Status == WorkerStatus.Ready;
            }, TimeSpan.FromSeconds(35));
            readyWorkers = node.App.Services.GetRequiredService<WorkerManager>().Snapshot().ToArray();
            if (readyWorkers.Length != 1)
                throw new InvalidOperationException("HARNESS INVALID: expected exactly one owned Worker.");
            WorkerSnapshot ownedWorker = readyWorkers[0];
            runIdentity = runIdentity with
            {
                Worker = new RenderedWanWorkerIdentity(ownedWorker.WorkerId.Value,
                    ownedWorker.Incarnation, ownedWorker.ProcessId)
            };

            await using NodeControlClient control = node.CreateControlClient();
            await control.ConnectAsync(node.Admission());
            await control.SendAsync("lobby.create", new LobbyCreate("Rendered WAN", LobbyVisibility.Unlisted, 2, 0));
            await UntilAsync(() => control.Lobby != null, TimeSpan.FromSeconds(10), control);

            long revision = control.Lobby!.Revision;
            await control.SendAsync("lobby.configure", new LobbyConfigure(revision,
                options.Room, MatchMode.Battle, BotCount: 1,
                TimeLimitSeconds: checked(options.Seconds + 45)));
            await UntilAsync(() => control.Lobby!.Revision > revision, TimeSpan.FromSeconds(10), control);

            revision = control.Lobby!.Revision;
            await control.SendAsync("lobby.hunter.select", new LobbySelectHunter(Hunter.Noxus, revision));
            await UntilAsync(() => control.Lobby!.Revision > revision, TimeSpan.FromSeconds(10), control);

            revision = control.Lobby!.Revision;
            await control.SendAsync("lobby.ready.set", new LobbySetReady(true, revision));
            await UntilAsync(() => control.Lobby!.Revision > revision, TimeSpan.FromSeconds(10), control);
            await control.SendAsync("lobby.start", new LobbyStart(control.Lobby!.Revision));
            await UntilAsync(() => control.Handoff != null, TimeSpan.FromSeconds(35), control);
            NodeMatchHandoff firstHandoff = control.Handoff!;

            WorkerScheduler assignmentScheduler = node.App.Services.GetRequiredService<WorkerScheduler>();
            if (!assignmentScheduler.TryGetAssignment(new MatchId(firstHandoff.MatchId),
                    out WorkerMatchAssignment? assignment)
                || assignment?.Placement is not { } placement
                || placement.MatchId.Value != firstHandoff.MatchId
                || placement.WireMatchId.Value != firstHandoff.WireMatchId
                || placement.WorkerId.Value != ownedWorker.WorkerId.Value
                || placement.WorkerIncarnation != ownedWorker.Incarnation
                || placement.Port != firstHandoff.Port)
                throw new InvalidOperationException("HARNESS INVALID: handoff was not issued by the owned Worker for the expected match.");
            runIdentity = runIdentity with
            {
                WorkerPort = firstHandoff.Port,
                MatchId = firstHandoff.MatchId,
                WireMatchId = firstHandoff.WireMatchId
            };

            if (!await NetLaunch.JoinWorkerAsync(firstHandoff, control.Session!.DisplayName,
                    timeoutMs: 30000))
                throw new InvalidOperationException("The production Worker handoff was rejected: " + Bounded(NetLaunch.LastJoinError));
            AuthoritativePlay play = AuthoritativePlay.Current
                ?? throw new InvalidOperationException("The Worker handoff did not create an authoritative client.");
            runIdentity = runIdentity with
            {
                Client = new RenderedWanClientIdentity(control.Session?.SessionId,
                    control.Session?.PlayerId, play.Client.Connection?.Id, play.LocalSlot,
                    play.LocalUdpPort)
            };
            await UntilAsync(() => play.Client.Roster.Length >= 2, TimeSpan.FromSeconds(5));
            ValidateExpectedParticipant(play, firstHandoff);
            control.MarkGameplayJoined(firstHandoff.MatchId);

            async Task<NodeMatchHandoff> RejoinAsync()
            {
                ulong priorNonce = control.Handoff?.Nonce ?? 0;
                await control.SendAsync("match.rejoin", new NodeMatchRejoin(firstHandoff.MatchId));
                await UntilAsync(() => control.Handoff is { } handoff && handoff.Nonce != priorNonce,
                    TimeSpan.FromSeconds(10), control);
                NodeMatchHandoff handoff = control.Handoff!;
                if (handoff.MatchId != firstHandoff.MatchId || handoff.WireMatchId != firstHandoff.WireMatchId)
                    throw new InvalidOperationException("Node rejoin changed the frozen match identity.");
                return handoff;
            }

            using var debugStop = new CancellationTokenSource();
            var debugRefreshState = new DebugRefreshState();
            WorkerScheduler scheduler = node.App.Services.GetRequiredService<WorkerScheduler>();
            void ObserveWorker(ManagedWorker _, WorkerEvent message) => debugRefreshState.Observe(message);
            scheduler.Observed += ObserveWorker;
            Task debugRefresh = RefreshDebugAsync(node.App.Services.GetRequiredService<NodeMatchCoordinator>(),
                new MatchId(firstHandoff.MatchId), (byte)play.LocalSlot, debugRefreshState,
                debugStop.Token);

            long sentBefore = NetTransport.TotalPacketsSent;
            long droppedBefore = NetTransport.TotalPacketsDropped;
            RenderedClientResult result;
            try
            {
                using IRenderToolHost renderHost = RenderToolHostFactory.Create(
                    new Vector2i(options.Width, options.Height), "Project Prime rendered WAN validation",
                    updateFrequency: 60, visible: true, presentable: true);
                var client = new RenderedClient(renderHost, play, Hunter.Noxus, options,
                    captureDirectory, () => Task.Run(RejoinAsync));
                renderHost.Run(client);
                result = client.Result();
                if (renderHost is SdlRenderToolHost sdl)
                    result = result with { Backend = sdl.BackendInfo };
            }
            finally
            {
                debugStop.Cancel();
                try { await debugRefresh; } catch (OperationCanceledException) { }
                scheduler.Observed -= ObserveWorker;
            }

            WorkerSnapshot[] finalWorkers = node.App.Services.GetRequiredService<WorkerManager>()
                .Snapshot().ToArray();
            bool workerStillOwned = finalWorkers.Length == 1
                && finalWorkers[0].WorkerId == ownedWorker.WorkerId
                && finalWorkers[0].Incarnation == ownedWorker.Incarnation
                && finalWorkers[0].ProcessId == ownedWorker.ProcessId
                && finalWorkers[0].FailureReason == null;
            if (!workerStillOwned && options.Scenario == RenderedWanScenario.Headshot
                && result.Scenario is { } scenarioBeforeWorkerCheck)
            {
                result = result with
                {
                    Scenario = scenarioBeforeWorkerCheck with
                    {
                        ScenarioValid = false,
                        InvalidReasons = scenarioBeforeWorkerCheck.InvalidReasons
                            .Append("worker-identity-contaminated").ToArray()
                    }
                };
            }

            long sent = Math.Max(0, NetTransport.TotalPacketsSent - sentBefore);
            long dropped = Math.Max(0, NetTransport.TotalPacketsDropped - droppedBefore);
            long authoritativeDesiredWeaponShotEvents
                = options.Scenario == RenderedWanScenario.Headshot
                    ? result.Scenario?.AuthoritativeImperialistShots ?? 0
                    : (result.ProjectileBeforeReconnect?.AuthoritativeMissileShotObserved ?? 0)
                        + result.ProjectileAfterReconnect.AuthoritativeMissileShotObserved;
            bool dynamicScenarioAvailable = result.DynamicColliderMaximum > 0;
            bool dynamicScenarioExercised = fixture != null
                && options.LagCompensationMode == WorkerLagCompensationMode.Dynamic
                && dynamicScenarioAvailable
                && result.DynamicDebugPackets > 0 && result.DebugMetrics.DynamicHistoryRecords > 0
                && result.DebugMetrics.DynamicHistoryQueries > 0;
            bool historicalContradictionObserved
                = result.DebugMetrics.HistoricalGeometryChangedOutcome > 0;
            bool scenarioValid = options.Scenario != RenderedWanScenario.Headshot
                || result.Scenario?.ScenarioValid == true;
            bool passed = workerStillOwned && result.RuntimePassed && result.ReconnectCompleted
                && result.DebugPackets > 0 && sent > 0
                && scenarioValid
                && (fixture == null || dynamicScenarioAvailable && result.DynamicDebugPackets > 0
                    && (options.LagCompensationMode != WorkerLagCompensationMode.Dynamic
                        || dynamicScenarioExercised));
            var report = new
            {
                schema = Schema,
                createdUtc = DateTimeOffset.UtcNow,
                passed,
                runId = runIdentity.RunId,
                scenario = runIdentity.Scenario,
                startedUtc = runIdentity.StartedUtc,
                client = runIdentity.Client,
                worker = runIdentity.Worker,
                node = runIdentity.Node,
                evidenceClass = "rendered-loopback-process-local-impairment",
                run = runIdentity,
                renderedWanProof = false,
                qz1Accepted = false,
                qz5Accepted = false,
                requiresHumanVisualReview = true,
                validationFixture = fixture != null,
                productionMultiplayer = fixture == null,
                securityBoundary = new
                {
                    publicDiagnosticEndpointAdded = false,
                    productionNodeControlUsed = true,
                    externalWorkerProcessUsed = true,
                    ephemeralSigningKeyAndCertificate = true,
                    secretsIncluded = false
                },
                content = new
                {
                    version = contentVersion,
                    hash = contentHash,
                    room = options.Room,
                    dynamicScenarioAvailable,
                    dynamicScenarioExercised,
                    historicalContradictionObserved,
                    validationFixture = fixture == null ? null : new
                    {
                        id = fixture.CliValue,
                        mapKey = fixture.MapKey,
                        harnessLocalSyntheticMap = true,
                        productionRegistered = false,
                        contentHash,
                        fingerprint = fixture.Fingerprint,
                        resources = fixture.Resources.Select(resource => resource.Path).ToArray(),
                        expectedContent = fixtureEvidence,
                        expectedRegistry = fixture.ExpectedRegistry
                    },
                    limitation = fixture != null
                        ? "The compiled fixture admits only PlayerSpawn, Door, ForceField, Platform and Object records. Its mutable state is intentionally absent from production world replication; rendered overlays consume authenticated server diagnostics. Query exercise does not establish a historical/current changed outcome."
                        : dynamicScenarioAvailable
                            ? (string?)null
                            : "Current validated room content exposed no registered mutable collision entities; dynamic contradictions were not exercised."
                },
                workerRuntime = new
                {
                    build = WorkerOptions.ActualBuildVersion,
                    protocol = NetHeader.Version,
                    lagCompensationMode = options.LagCompensationMode.ToString().ToLowerInvariant()
                },
                impairment = new
                {
                    kind = "client-process-local-netlag",
                    requestedRoundTripMs = options.RoundTripMs,
                    requestedJitterMsPerDirection = options.JitterMs,
                    requestedLossPercentPerDirection = options.LossPercent,
                    actualRoundTripMs = NetLag.RoundTripMs,
                    actualJitterMsPerDirection = NetLag.JitterMs,
                    actualLossPercentPerDirection = NetLag.LossPercent,
                    packetsSent = sent,
                    packetsDropped = dropped,
                    representativeMatrix = new[]
                    {
                        new { rttMs = 50, lossPercent = 0d, jitter = "low" },
                        new { rttMs = 100, lossPercent = 1d, jitter = "low" },
                        new { rttMs = 150, lossPercent = 2d, jitter = "moderate" },
                        new { rttMs = 200, lossPercent = 3d, jitter = "moderate" },
                        new { rttMs = 250, lossPercent = 3d, jitter = "high" },
                        new { rttMs = 300, lossPercent = 5d, jitter = "high" }
                    }
                },
                renderer = new
                {
                    backend = RenderBackendSelection.CurrentCliValue,
                    driver = result.Backend?.Driver,
                    shaderFormats = result.Backend?.ShaderFormats,
                    swapchainFormat = result.Backend?.SwapchainFormat,
                    presentMode = result.Backend?.PresentMode,
                    finalPresentedFrame = true,
                    width = options.Width,
                    height = options.Height,
                    submittedFrames = result.SubmittedFrames,
                    acknowledgedFrames = result.AcknowledgedFrames,
                    captureFailures = result.CaptureFailures,
                    captures = result.Captures
                },
                gameplay = new
                {
                    scenario = RenderedWanScenarioParser.Format(options.Scenario),
                    scriptedHunter = Hunter.Noxus.ToString(),
                    scriptedDesiredWeapon = (options.Scenario == RenderedWanScenario.Headshot
                        ? BeamType.Imperialist : BeamType.Missile).ToString(),
                    scriptedWeaponConfirmedByReport
                        = authoritativeDesiredWeaponShotEvents > 0,
                    authoritativeDesiredWeaponShotEvents,
                    finalState = result.FinalState.ToString(),
                    snapshots = result.Snapshots,
                    worldState = result.WorldState,
                    combatEvents = result.CombatEvents,
                    damageEvents = result.DamageEvents,
                    rejectedDatagrams = result.RejectedDatagrams,
                    localTravel = result.LocalTravel,
                    movingRemotePlayers = result.MovingRemotePlayers,
                    predictionBeforeReconnect = result.PredictionBeforeReconnect,
                    predictionAfterReconnect = result.PredictionAfterReconnect,
                    projectilePresentationBeforeReconnect = result.ProjectileBeforeReconnect,
                    projectilePresentationAfterReconnect = result.ProjectileAfterReconnect,
                    hitPredictionBeforeReconnect = result.HitPredictionBeforeReconnect,
                    hitPredictionAfterReconnect = result.HitPredictionAfterReconnect,
                    selfImpulseBeforeReconnect = result.SelfImpulseBeforeReconnect,
                    selfImpulseAfterReconnect = result.SelfImpulseAfterReconnect,
                    presentedCollisionBeforeReconnect = result.PresentedCollisionBeforeReconnect,
                    presentedCollisionAfterReconnect = result.PresentedCollisionAfterReconnect,
                    interpolationBeforeReconnect = result.InterpolationBeforeReconnect,
                    interpolationAfterReconnect = result.InterpolationAfterReconnect,
                    measuredRttMs = result.MeasuredRttMs,
                    measuredJitterMs = result.MeasuredJitterMs
                },
                scenarioFacts = result.Scenario,
                classification = passed ? "PASS"
                    : scenarioValid && workerStillOwned ? "NETWORK FAIL" : "HARNESS INVALID",
                harness = new
                {
                    ownedWorker = workerStillOwned,
                    expectedWorkerId = ownedWorker.WorkerId,
                    expectedWorkerIncarnation = ownedWorker.Incarnation,
                    expectedWorkerProcessId = ownedWorker.ProcessId,
                    observedWorkerCount = finalWorkers.Length
                },
                debug = new
                {
                    hostSideRefresh = true,
                    refreshAttempts = debugRefreshState.Attempts,
                    refreshCommandsQueued = debugRefreshState.Queued,
                    workerCommandsApplied = debugRefreshState.Applied,
                    workerCommandsRejected = debugRefreshState.Rejected,
                    lastWorkerRejectionCode = debugRefreshState.LastRejectionCode,
                    packets = result.DebugPackets,
                    historyPackets = result.HistoryDebugPackets,
                    dynamicPackets = result.DynamicDebugPackets,
                    maximumHistoricalPlayers = result.HistoricalPlayerMaximum,
                    maximumDynamicColliders = result.DynamicColliderMaximum,
                    historicalDynamicEnabledReported = result.HistoricalDynamicEnabledReported,
                    registryOverflowReported = result.RegistryOverflowReported,
                    truncatedReported = result.TruncatedReported,
                    metrics = result.DebugMetrics
                },
                reconnect = new
                {
                    requested = true,
                    sameNodeSession = result.ReconnectCompleted,
                    sameMatch = result.ReconnectSameMatch,
                    sameSeat = result.ReconnectSameSeat,
                    connectionIdentityRotated = result.ConnectionIdentityRotated,
                    completed = result.ReconnectCompleted
                },
                limitations = new[]
                {
                    "This run uses a real rendered client, production Node handoff, and an external Worker, but both endpoints are on one host.",
                    "Process-local NetLag is controlled impairment, not measurement of an internet path.",
                    fixture == null
                        ? "Current production multiplayer content exposes no mutable collision entities."
                        : "The compiled developer fixture omits unsupported single-player records and does not production-replicate its mutable entity state.",
                    historicalContradictionObserved
                        ? "A historical/current geometry outcome difference was measured, but QZ1 and rendered-WAN acceptance remain false pending genuine WAN and human visual review."
                        : "No historical/current geometry outcome difference was measured; QZ1 and rendered-WAN acceptance remain false.",
                    "Projectile presentation counters are measurement-only and do not by themselves satisfy QZ5."
                },
                humanReview = new
                {
                    completed = false,
                    questions = new[]
                    {
                        "Does shooting feel immediate?",
                        "Does a predicted marker promote cleanly without a duplicate?",
                        "Are false markers distracting?",
                        "Does Shock Coil feedback avoid flicker?",
                        "Do self jumps feel immediate without pullback?",
                        "Do deaths remain trustworthy?"
                    }
                }
            };
            WriteJson(Path.Combine(options.OutputDirectory, "report.json"), report);
            Console.WriteLine($"RENDERED_WAN_VALIDATION result={(passed ? "PASS" : "FAIL")} mode={options.LagCompensationMode.ToString().ToLowerInvariant()} rtt={options.RoundTripMs} jitter={options.JitterMs} loss={options.LossPercent.ToString("0.##", CultureInfo.InvariantCulture)} frames={result.SubmittedFrames} captures={result.Captures.Count} reconnect={result.ReconnectCompleted} dynamicScenarioAvailable={dynamicScenarioAvailable} renderedWanProof=false qz1Accepted=false");
            return passed ? 0 : 1;
        }
        finally
        {
            NetSession.Stop();
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            Task nodeStop = StartWithoutSynchronizationContext(() => node.App.StopAsync(stop.Token));
            await nodeStop;
        }
    }

    private static Task StartWithoutSynchronizationContext(Func<Task> start)
    {
        SynchronizationContext? current = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try { return start(); }
        finally { SynchronizationContext.SetSynchronizationContext(current); }
    }

    private static async Task RefreshDebugAsync(NodeMatchCoordinator coordinator,
        MatchId matchId, byte seat, DebugRefreshState state,
        CancellationToken cancellationToken)
    {
        bool dynamic = false;
        while (!cancellationToken.IsCancellationRequested)
        {
            Interlocked.Increment(ref state.Attempts);
            if (coordinator.TrySendHistoricalDebug(matchId,
                    dynamic ? AdminAction.LagCompDynamic : AdminAction.LagCompHistory, seat))
                Interlocked.Increment(ref state.Queued);
            dynamic = !dynamic;
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class DebugRefreshState
    {
        public int Attempts;
        public int Queued;
        public int Applied;
        public int Rejected;
        public string? LastRejectionCode;

        public void Observe(WorkerEvent message)
        {
            if (message is not MatchAdminResult result
                || result.Action is not (AdminAction.LagCompHistory or AdminAction.LagCompDynamic)) return;
            if (result.Applied) Interlocked.Increment(ref Applied);
            else
            {
                Interlocked.Increment(ref Rejected);
                Volatile.Write(ref LastRejectionCode, Bounded(result.Code));
            }
        }
    }

    private static async Task UntilAsync(Func<bool> condition, TimeSpan timeout,
        NodeControlClient? control = null)
    {
        using var deadline = new CancellationTokenSource(timeout);
        while (!condition())
        {
            if (control?.Error is { } error)
                throw new InvalidOperationException("Node control failed: " + Bounded(error));
            await Task.Delay(10, deadline.Token);
        }
    }

    private static void WriteJson(string path, object value)
        => File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }) + Environment.NewLine);

    private static string Bounded(string? value)
    {
        string text = string.IsNullOrWhiteSpace(value) ? "Unspecified failure." : value.Replace('\r', ' ').Replace('\n', ' ');
        return text.Length <= 512 ? text : text[..512];
    }

    private static int ParsePort(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? uri)
            || uri.Port is <= 0 or > ushort.MaxValue)
            throw new InvalidDataException("The ephemeral Node did not publish a valid listening port.");
        return uri.Port;
    }

    private static void ValidateExpectedParticipant(AuthoritativePlay play,
        NodeMatchHandoff handoff)
    {
        if (play.Client.Accepted.MatchId != handoff.WireMatchId
            || play.LocalSlot < 0 || play.LocalSlot >= 8)
            throw new InvalidOperationException(
                "HARNESS INVALID: client joined an unexpected match or slot.");
        if (play.Client.Roster.Length != 2)
            throw new InvalidOperationException(
                "HARNESS INVALID: unexpected third participant in the owned match.");
        int local = 0, bots = 0;
        ulong connectionId = play.Client.Connection?.Id ?? 0;
        foreach (NetRosterEntry entry in play.Client.Roster)
        {
            if (entry.IsBot) { bots++; continue; }
            if (entry.Slot != play.LocalSlot || connectionId == 0
                || entry.ConnectionId != connectionId)
                throw new InvalidOperationException(
                    "HARNESS INVALID: roster contains the wrong client identity.");
            local++;
        }
        if (local != 1 || bots != 1)
            throw new InvalidOperationException(
                "HARNESS INVALID: owned match participant set is not one client plus one bot.");
    }

    /// <summary>
    /// Keeps the orchestration continuations on the process main thread until
    /// SDL has created and run its macOS/Cocoa video owner. The queue is
    /// bounded; rejoin and periodic debug refresh explicitly run elsewhere
    /// while the synchronous renderer owns this thread.
    /// </summary>
    private sealed class MainThreadSynchronizationContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue =
            new(new ConcurrentQueue<(SendOrPostCallback, object?)>(), 1024);

        public override void Post(SendOrPostCallback callback, object? state)
        {
            if (!_queue.TryAdd((callback, state)))
                throw new InvalidOperationException("The main-thread continuation queue is full.");
        }

        public override SynchronizationContext CreateCopy() => this;

        public void Run(Task operation)
        {
            while (!operation.IsCompleted)
            {
                if (_queue.TryTake(out var work, millisecondsTimeout: 50))
                    work.Callback(work.State);
            }
            while (_queue.TryTake(out var work)) work.Callback(work.State);
        }

        public void Dispose() => _queue.Dispose();
    }

    private sealed record Options(string DataDirectory, string OutputDirectory,
        WorkerLagCompensationMode LagCompensationMode, int RoundTripMs, int JitterMs,
        double LossPercent, int Seconds, int ReconnectAt, string Room, int Width, int Height,
        DeveloperValidationFixtureId ValidationFixture, RenderedWanScenario Scenario,
        Guid RunId, DateTimeOffset StartedUtc)
    {
        public static Options Parse(string[] args)
        {
            if (args.Length < 3 || (args.Length - 3) % 2 != 0)
                throw new ArgumentException("Expected DATA OUTPUT_DIRECTORY followed by option/value pairs.");
            string data = Path.GetFullPath(args[1]);
            string output = Path.GetFullPath(args[2]);
            if (!Directory.Exists(data)) throw new DirectoryNotFoundException("The content directory does not exist.");
            var flags = new Dictionary<string, string>(StringComparer.Ordinal);
            string[] allowed = ["--mode", "--rtt", "--jitter", "--loss", "--seconds", "--reconnect-at", "--room", "--width", "--height", "--fixture", "--scenario"];
            for (int i = 3; i < args.Length; i += 2)
            {
                if (!allowed.Contains(args[i], StringComparer.Ordinal) || !flags.TryAdd(args[i], args[i + 1]))
                    throw new ArgumentException("Unknown or duplicate rendered WAN validation option.");
            }
            int Number(string name, int fallback) => flags.TryGetValue(name, out string? raw)
                && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                    ? value : flags.ContainsKey(name) ? throw new ArgumentException(name + " requires an integer.") : fallback;
            double Percent(string name, double fallback) => flags.TryGetValue(name, out string? raw)
                && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                    ? value : flags.ContainsKey(name) ? throw new ArgumentException(name + " requires a number.") : fallback;

            int seconds = Number("--seconds", 15);
            int reconnect = Number("--reconnect-at", Math.Max(6, seconds / 2));
            int rtt = Number("--rtt", 0);
            int jitter = Number("--jitter", 0);
            double loss = Percent("--loss", 0);
            int width = Number("--width", 640);
            int height = Number("--height", 360);
            if (seconds is < 12 or > 60 || reconnect < 4 || reconnect > seconds - 3
                || rtt is < 0 or > 2000 || jitter is < 0 or > 1000
                || !double.IsFinite(loss) || loss is < 0 or > 25
                || width is < 320 or > 1920 || height is < 180 or > 1080)
                throw new ArgumentOutOfRangeException(nameof(args), "Rendered WAN validation limits are invalid.");
            RenderedWanScenario scenario = RenderedWanScenarioParser.Parse(
                flags.GetValueOrDefault("--scenario", "general"));
            DeveloperValidationFixtureId fixture = DeveloperValidationFixtures.Parse(
                flags.GetValueOrDefault("--fixture", "none"));
            if (scenario == RenderedWanScenario.Headshot && fixture == DeveloperValidationFixtureId.None)
            {
                if (flags.ContainsKey("--fixture") || flags.ContainsKey("--room"))
                    throw new ArgumentException(
                        "The headshot scenario requires the isolated Unit1 RM1 validation fixture.");
                fixture = DeveloperValidationFixtureId.Unit1Rm1Dynamic;
            }
            if (scenario == RenderedWanScenario.Headshot
                && fixture != DeveloperValidationFixtureId.Unit1Rm1Dynamic)
                throw new ArgumentException(
                    "The headshot scenario requires the isolated Unit1 RM1 validation fixture.");
            if (fixture != DeveloperValidationFixtureId.None && flags.ContainsKey("--room"))
                throw new ArgumentException("A compiled validation fixture cannot accept an arbitrary room.");
            string room = fixture == DeveloperValidationFixtureId.None
                ? flags.GetValueOrDefault("--room", "MP1 SANCTORUS")
                : DeveloperValidationFixtures.Require(fixture).MapKey;
            if (string.IsNullOrWhiteSpace(room) || room.Length > 128)
                throw new ArgumentException("The room key is invalid.");
            WorkerLagCompensationMode mode = WorkerOptions.ParseLagCompensationMode(
                flags.GetValueOrDefault("--mode", "players"));
            Guid runId = Guid.NewGuid();
            DateTimeOffset startedUtc = DateTimeOffset.UtcNow;
            output = RenderedWanRunReservation.ReserveNewDirectory(output);
            return new(data, output, mode, rtt, jitter, loss, seconds, reconnect,
                room, width, height, fixture, scenario, runId, startedUtc);
        }
    }

    private sealed class EphemeralNode : IAsyncDisposable
    {
        private readonly string _temporaryRoot = Path.Combine(Path.GetTempPath(),
            "prime-rendered-wan-" + Guid.NewGuid().ToString("N"));
        private readonly ECDsa _signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly X509Certificate2 _certificate;
        private readonly string _publicKeyPath;
        private readonly Guid _nodeId = Guid.NewGuid();
        public Guid NodeId => _nodeId;
        public WebApplication App { get; }

        public EphemeralNode(WorkerContentIdentity content, Options options)
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
            Directory.CreateDirectory(artifacts);
            Directory.CreateDirectory(replays);

            using var tlsKey = RSA.Create(2048);
            var request = new CertificateRequest("CN=localhost", tlsKey,
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            _certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow.AddHours(1));

            var workerArguments = new List<string>
            {
                typeof(WorkerOptions).Assembly.Location,
                "--content-dir", options.DataDirectory,
                "--content-version", content.ContentVersion,
                "--content-hash", content.ContentHash,
                "--lanes", "1",
                "--max-matches", "1",
                "--max-matches-per-lane", "1",
                "--lag-compensation-mode", options.LagCompensationMode.ToString().ToLowerInvariant(),
                "--replay-dir", replays
            };
            if (options.ValidationFixture != DeveloperValidationFixtureId.None)
            {
                workerArguments.Add("--validation-fixture");
                workerArguments.Add(DeveloperValidationFixtures.Require(options.ValidationFixture).CliValue);
            }
            if (options.Scenario == RenderedWanScenario.Headshot)
            {
                workerArguments.Add("--headshot-validation-scenario");
                workerArguments.Add("true");
                workerArguments.Add("--headshot-scenario-seconds");
                workerArguments.Add(options.Seconds.ToString(CultureInfo.InvariantCulture));
            }
            var launch = new WorkerLaunchOptions
            {
                FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
                Content = content,
                Arguments = workerArguments,
                ArtifactDirectory = artifacts,
                Capacity = new(1, 2, 0, 0),
                StartupTimeout = TimeSpan.FromSeconds(30),
                ShutdownTimeout = TimeSpan.FromSeconds(3)
            };
            var map = new ContentIdentity(options.Room, content.ContentHash,
                content.ContentVersion, content.BuildVersion, content.ProtocolVersion);
            App = NodeApplication.Build([], builder =>
            {
                builder.Logging.ClearProviders();
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Node:Authentication:NodeId"] = _nodeId.ToString("D"),
                    ["Node:Authentication:Issuer"] = "https://rendered-wan.invalid",
                    ["Node:Authentication:Keys:0:KeyId"] = "ephemeral",
                    ["Node:Authentication:Keys:0:PublicKeyPemPath"] = _publicKeyPath
                });
                builder.Configuration.AddJsonStream(new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(new
                {
                    Node = new
                    {
                        Maps = new[] { map },
                        Workers = new
                        {
                            Processes = new[] { launch },
                            DrainTimeout = "00:00:02",
                            ForceAfterDrainDeadline = true
                        }
                    }
                })));
                builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0,
                    listen => listen.UseHttps(_certificate)));
            });
        }

        public NodeControlClient CreateControlClient()
        {
            var socket = new ClientWebSocket();
            string thumbprint = _certificate.Thumbprint;
            socket.Options.RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                certificate?.GetCertHashString() == thumbprint;
            return new NodeControlClient(socket);
        }

        public NodeAdmissionTicket Admission()
        {
            DateTime now = DateTime.UtcNow;
            Guid player = Guid.NewGuid();
            string token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = "https://rendered-wan.invalid",
                Audience = NodeAdmissionValidator.Audience(_nodeId),
                TokenType = NodeAdmissionValidator.TokenType,
                IssuedAt = now,
                NotBefore = now,
                Expires = now.AddMinutes(2),
                SigningCredentials = new(new ECDsaSecurityKey(_signingKey) { KeyId = "ephemeral" }, "ES256"),
                Claims = new Dictionary<string, object>
                {
                    ["sub"] = player.ToString("D"),
                    ["jti"] = Guid.NewGuid().ToString("D"),
                    ["name"] = "WanProbe"
                }
            });
            string endpoint = App.Urls.Single().Replace("https://", "wss://", StringComparison.Ordinal) + "/v1/control";
            return new(token, DateTimeOffset.UtcNow.AddMinutes(2), _nodeId, endpoint);
        }

        public async ValueTask DisposeAsync()
        {
            await App.DisposeAsync();
            _certificate.Dispose();
            _signingKey.Dispose();
            if (Directory.Exists(_temporaryRoot)) Directory.Delete(_temporaryRoot, recursive: true);
        }
    }

    private sealed class RenderedClient : IRenderToolClient
    {
        private const int GraceFrames = 900;
        private const int MaximumCaptures = 64;
        private readonly IRenderToolHost _host;
        private readonly AuthoritativePlay _play;
        private readonly Scene _scene;
        private readonly ScenePresentation _presentation;
        private readonly Options _options;
        private readonly string _captureDirectory;
        private readonly Func<Task<NodeMatchHandoff>> _requestRejoin;
        private readonly Stopwatch _wall = Stopwatch.StartNew();
        private readonly long _simulationPeriod = Stopwatch.Frequency / SimTicks.Hz;
        private readonly Vector3[] _last = new Vector3[8];
        private readonly bool[] _seen = new bool[8];
        private readonly double[] _travel = new double[8];
        private readonly List<CaptureEvidence> _captures = [];
        private readonly HeadshotScenarioFacts _headshotFacts = new();
        private bool _headshotCorrectWeapon;
        private bool _headshotZoomObserved;
        private bool _headshotAmmoAvailable;
        private bool _headshotTargetSeen;
        private Task<NodeMatchHandoff>? _rejoinTask;
        private ulong _connectionBeforeReconnect;
        private uint _matchBeforeReconnect;
        private int _slotBeforeReconnect;
        private bool _reconnectApplied;
        private bool _reconnectCompleted;
        private bool _reconnectSameMatch;
        private bool _reconnectSameSeat;
        private bool _connectionIdentityRotated;
        private int _simulationFrames;
        private long _nextSimulationTimestamp;
        private int _headshotPlayingFrames;
        private int _submittedFrames;
        private int _acknowledgedFrames;
        private int _captureFailures;
        private int _nextCaptureFrame = 60;
        private bool _capturePending;
        private int _debugPackets;
        private int _historyDebugPackets;
        private int _dynamicDebugPackets;
        private int _historicalPlayerMaximum;
        private int _dynamicColliderMaximum;
        private uint _lastHistoryTick;
        private uint _lastDynamicTick;
        private bool _historicalDynamicEnabledReported;
        private bool _registryOverflowReported;
        private bool _truncatedReported;
        private HistoricalCollisionDebugMetrics _debugMetrics;
        private PredictionEvidence? _predictionBeforeReconnect;
        private ProjectilePresentationMeasurementSnapshot? _projectileBeforeReconnect;
        private HitPredictionMetrics? _hitPredictionBeforeReconnect;
        private SelfImpulseMetrics? _selfImpulseBeforeReconnect;
        private PresentedCollisionMetricsSnapshot? _presentedCollisionBeforeReconnect;
        private SnapshotInterpolationMetrics? _interpolationBeforeReconnect;

        public RenderedClient(IRenderToolHost host, AuthoritativePlay play, Hunter hunter,
            Options options, string captureDirectory, Func<Task<NodeMatchHandoff>> requestRejoin)
        {
            _host = host;
            _play = play;
            _options = options;
            _captureDirectory = captureDirectory;
            _requestRejoin = requestRejoin;
            _scene = new Scene(features: ClientMatchFeatures.Capture()) { Services = new ClientSceneServices() };
            CombatFeedbackSettings.Timing = HitMarkerTiming.Instant;
            play.HitPrediction.Enabled = true;
            play.SelfImpulse.Enabled = true;
            _presentation = host.CreatePresentation(_scene);
            play.BuildPlayers(_scene, hunter, 0);
            if (options.Scenario == RenderedWanScenario.Headshot)
            {
                if (play.LocalSlot < 0 || play.LocalSlot >= _scene.Players.Count)
                    throw new InvalidOperationException("HARNESS INVALID: headshot shooter slot is unavailable.");
                // This is a local test fixture grant only. The Worker remains
                // authoritative and must independently emit Imperialist Shot
                // facts for the scenario to become valid.
                _scene.Players[play.LocalSlot].ModArmWeapon(BeamType.Imperialist);
                play.LocalRootShotObserved += ObserveLocalRootShot;
                play.AuthoritativeCombatEventObserved += ObserveAuthoritativeCombatEvent;
                play.PredictedContactObserved += ObservePredictedContact;
            }
            if (options.ValidationFixture == DeveloperValidationFixtureId.None)
            {
                _scene.AddRoom(play.Client.Accepted.Room, play.Client.Accepted.Mode,
                    playerCount: NetConfig.RoomPlayerCount);
            }
            else
            {
                DeveloperValidationFixtureDescriptor descriptor
                    = DeveloperValidationFixtures.Require(options.ValidationFixture);
                if (play.Client.Accepted.Room != descriptor.MapKey)
                    throw new InvalidOperationException("Worker handoff did not retain the validation fixture identity.");
                _scene.AddValidationFixture(options.ValidationFixture, descriptor.Mode,
                    playerCount: NetConfig.RoomPlayerCount);
            }
            play.ScriptInput = Drive;
        }

        public void OnLoad()
        {
            _presentation.Size = _host.Size;
            _presentation.OnLoad();
            _presentation.OnResize();
            _nextSimulationTimestamp = Stopwatch.GetTimestamp();
        }

        public void OnFrame()
        {
            AdvanceReconnect();
            long now = Stopwatch.GetTimestamp();
            int simulationSteps = 0;
            while (now >= _nextSimulationTimestamp && simulationSteps < 4)
            {
                _presentation.OnSimulationFrame();
                _simulationFrames++;
                ObserveHeadshotScenario();
                ObserveDebug();
                _nextSimulationTimestamp += _simulationPeriod;
                simulationSteps++;
            }
            if (simulationSteps == 4 && now >= _nextSimulationTimestamp)
            {
                // A debugger pause or renderer stall may require bounded
                // catch-up, but this evidence client must never run an
                // unbounded simulation burst or outrun the 60 Hz Worker.
                _nextSimulationTimestamp = now + _simulationPeriod;
            }
            if (simulationSteps == 0) ObserveDebug();

            RenderToolCapture? request = null;
            if (!_capturePending && _captures.Count < MaximumCaptures
                && _simulationFrames >= _nextCaptureFrame)
            {
                request = new RenderToolCapture(CaptureTargetKind.FinalPresentedFrame);
                _capturePending = true;
            }
            RenderToolFrameResult frame = _host.Render(_presentation, request,
                acknowledgePresentation: true);
            _captureFailures += frame.CaptureFailures.Count;
            if (frame.CaptureFailures.Count > 0) _capturePending = false;
            foreach (RenderCaptureResult capture in frame.Captures) ConsumeCapture(capture);
            if (frame.Submitted)
            {
                _submittedFrames++;
                if (frame.Acknowledged) _acknowledgedFrames++;
                ObserveTravel();
            }

            int durationFrames = _options.Scenario == RenderedWanScenario.Headshot
                ? _headshotPlayingFrames : _simulationFrames;
            bool durationReached = durationFrames >= _options.Seconds * 60;
            bool boundedGraceReached = durationFrames >= _options.Seconds * 60 + GraceFrames
                || _wall.Elapsed >= TimeSpan.FromSeconds(_options.Seconds + 20);
            if (durationReached && _reconnectCompleted && _captures.Count > 0 || boundedGraceReached)
                _host.Close();
        }

        private void AdvanceReconnect()
        {
            // The headshot arm first needs one uninterrupted correlated-shot
            // population. Exercise the same-session reconnect after that
            // population has completed so connection rotation cannot turn a
            // valid pre-reconnect reliable echo into a false scenario setup
            // failure. General validation keeps its configured mid-run arm.
            bool reconnectDue = _options.Scenario == RenderedWanScenario.Headshot
                ? _headshotPlayingFrames >= _options.Seconds * 60
                : _wall.Elapsed.TotalSeconds >= _options.ReconnectAt;
            if (_rejoinTask == null && reconnectDue
                && _play.Client.State == NetConnectionState.Playing)
            {
                _connectionBeforeReconnect = _play.Client.Connection?.Id ?? 0;
                _matchBeforeReconnect = _play.Client.Accepted.MatchId;
                _slotBeforeReconnect = _play.LocalSlot;
                _predictionBeforeReconnect = PredictionEvidence.Capture(_play.Prediction);
                _projectileBeforeReconnect = _play.ProjectilePresentation.Metrics;
                _hitPredictionBeforeReconnect = _play.HitPrediction.Metrics;
                _selfImpulseBeforeReconnect = _play.SelfImpulse.Metrics;
                _presentedCollisionBeforeReconnect = PresentedCollisionMetricsSnapshot.Capture(
                    _play.PresentedCollision.Metrics);
                _interpolationBeforeReconnect = _play.InterpolationMetrics;
                _play.Client.Close();
                _rejoinTask = _requestRejoin();
            }
            if (_rejoinTask == null || _reconnectApplied) return;
            if (_rejoinTask.IsFaulted)
                throw new InvalidOperationException("Same-session reconnect handoff failed.",
                    _rejoinTask.Exception?.GetBaseException());
            if (!_rejoinTask.IsCompletedSuccessfully) return;
            NodeMatchHandoff handoff = _rejoinTask.Result;
            _play.Client.Reconnect(handoff.Nonce, handoff.Ticket);
            _reconnectApplied = true;
        }

        private void ObserveDebug()
        {
            if (_reconnectApplied && !_reconnectCompleted
                && _play.Client.State == NetConnectionState.Playing
                && _play.Client.HasSnapshot)
            {
                ulong after = _play.Client.Connection?.Id ?? 0;
                _connectionIdentityRotated = _connectionBeforeReconnect != 0
                    && after != 0 && after != _connectionBeforeReconnect;
                _reconnectSameMatch = _play.Client.Accepted.MatchId == _matchBeforeReconnect;
                _reconnectSameSeat = _play.LocalSlot == _slotBeforeReconnect;
                _reconnectCompleted = _connectionIdentityRotated
                    && _reconnectSameMatch && _reconnectSameSeat;
            }

            HistoricalCollisionDebugPacket? debug = _play.Client.HistoricalDebug;
            if (debug == null) return;
            ref uint lastTick = ref debug.Mode == HistoricalCollisionDebugMode.History
                ? ref _lastHistoryTick : ref _lastDynamicTick;
            if (debug.CurrentTick == lastTick) return;
            lastTick = debug.CurrentTick;
            _debugPackets++;
            if (debug.Mode == HistoricalCollisionDebugMode.History)
            {
                _historyDebugPackets++;
                _historicalPlayerMaximum = Math.Max(_historicalPlayerMaximum, debug.Players.Count);
            }
            else
            {
                _dynamicDebugPackets++;
                _dynamicColliderMaximum = Math.Max(_dynamicColliderMaximum, debug.Colliders.Count);
            }
            _historicalDynamicEnabledReported |= debug.HistoricalDynamicEnabled;
            _registryOverflowReported |= debug.RegistryOverflowed;
            _truncatedReported |= debug.Truncated;
            _debugMetrics = debug.Metrics;
        }

        private void Drive(PlayerEntity player, uint tick)
        {
            uint scenarioTick = tick;
            if (_options.Scenario == RenderedWanScenario.Headshot)
                scenarioTick = (uint)_headshotPlayingFrames++;
            Vector3 aim = -Vector3.UnitZ;
            SnapshotPlayer target = default;
            bool hasTarget = false;
            foreach (SnapshotPlayer other in _play.Client.SnapshotPlayers)
            {
                if (other.Slot == _play.LocalSlot || other.Health == 0) continue;
                if (hasTarget || !IsExpectedHeadshotTarget(other.Slot)) continue;
                target = other;
                hasTarget = true;
                Vector3 targetPoint = optionsHead(player, other);
                Vector3 direction = targetPoint - player.Position;
                if (direction.LengthSquared > 0.01f) aim = direction.Normalized();
            }
            if (!hasTarget)
            {
                foreach (SnapshotPlayer other in _play.Client.SnapshotPlayers)
                {
                    if (other.Slot == _play.LocalSlot || other.Health == 0) continue;
                    target = other;
                    hasTarget = true;
                    Vector3 direction = other.Position - player.Position;
                    if (direction.LengthSquared > 0.01f) aim = direction.Normalized();
                    break;
                }
            }
            if (_options.Scenario == RenderedWanScenario.Headshot)
                DriveHeadshot(player, target, hasTarget, ref aim);
            InputButtons movement = ((tick / 120 + (uint)_play.LocalSlot) % 4) switch
            {
                0 => InputButtons.Forward,
                1 => InputButtons.Left,
                2 => InputButtons.Back,
                _ => InputButtons.Right
            };
            InputButtons held = movement;
            InputButtons pressed = tick % 90 == 0 ? InputButtons.Jump : InputButtons.None;
            byte desiredWeapon = (byte)BeamType.Missile;
            if (_options.Scenario == RenderedWanScenario.Headshot)
            {
                movement = InputButtons.None;
                held = InputButtons.None;
                pressed = InputButtons.None;
                desiredWeapon = (byte)BeamType.Imperialist;
                if (player.CurrentWeapon != BeamType.Imperialist)
                    player.ModArmWeapon(BeamType.Imperialist);
                if (!player.EquipInfo.Zoomed)
                    pressed |= InputButtons.Zoom;
                // The single-frame edge is intentional: it records a legal
                // trigger attempt and lets weapon cooldown, ammo and spawn
                // rules decide whether a Shot actually exists.
                // Imperialist's multiplayer cooldown spans 120 render ticks.
                // Two extra ticks avoid boundary-order ambiguity, preserve
                // the 30 Hz input-sample phase, and yield six legal attempts
                // during the twelve-second population.
                uint shotPhase = scenarioTick >= 30
                    ? (scenarioTick - 30) % 122 : uint.MaxValue;
                if (shotPhase < 12)
                    held |= InputButtons.Shoot;
                if (shotPhase == 0)
                {
                    pressed |= InputButtons.Shoot;
                    _headshotFacts.Trigger();
                }
            }
            else
            {
                if (tick % 20 < 6) held |= InputButtons.Shoot;
                if (tick % 20 == 0) pressed |= InputButtons.Shoot;
            }
            player.ApplyNetworkInput(new InputCommand(tick, tick, 0, held, pressed, aim,
                desiredWeapon));
        }

        private Vector3 optionsHead(PlayerEntity player, in SnapshotPlayer target)
            => _options.Scenario == RenderedWanScenario.Headshot
                ? PresentedTargetHead(target)
                : PresentedTargetPosition(target);

        private bool IsExpectedHeadshotTarget(byte slot)
        {
            foreach (NetRosterEntry entry in _play.Client.Roster)
                if (entry.Slot == slot) return entry.IsBot;
            return false;
        }

        private void DriveHeadshot(PlayerEntity player, in SnapshotPlayer target,
            bool hasTarget, ref Vector3 aim)
        {
            if (!hasTarget) return;
            Vector3 targetPoint = PresentedTargetHead(target);
            Vector3 direction = targetPoint - player.Position;
            if (direction.LengthSquared > 0.01f) aim = direction.Normalized();
        }

        private float PresentedTargetHeadHeight(byte slot)
        {
            if (slot < _scene.Players.Count)
            {
                PlayerEntity entity = _scene.Players[slot];
                return Fixed.ToFloat(entity.Values.MaxPickupHeight) - 0.15f;
            }
            return 1.1f;
        }

        private Vector3 PresentedTargetHead(in SnapshotPlayer target)
            => PresentedTargetPosition(target)
                + new Vector3(0, PresentedTargetHeadHeight(target.Slot), 0);

        private Vector3 PresentedTargetPosition(in SnapshotPlayer target)
        {
            if (target.Slot < _scene.Players.Count)
            {
                PlayerEntity presented = _scene.Players[target.Slot];
                if (presented.ModIsInPlay) return presented.Position;
            }
            return target.Position;
        }

        private SnapshotPlayer PresentedTarget(in SnapshotPlayer target)
        {
            SnapshotPlayer presented = target;
            if (target.Slot < _scene.Players.Count)
            {
                PlayerEntity entity = _scene.Players[target.Slot];
                if (entity.ModIsInPlay)
                {
                    presented.Position = entity.Position;
                    presented.Speed = entity.Speed;
                }
            }
            return presented;
        }

        private void ObserveHeadshotScenario()
        {
            if (_options.Scenario != RenderedWanScenario.Headshot
                || _play.LocalSlot < 0 || _play.LocalSlot >= _scene.Players.Count)
                return;
            PlayerEntity shooter = _scene.Players[_play.LocalSlot];
            if (shooter.CurrentWeapon == BeamType.Imperialist)
                _headshotCorrectWeapon = true;
            if (shooter.EquipInfo.Zoomed) _headshotZoomObserved = true;
            (int ua, int missiles) = shooter.ModAmmo;
            _headshotAmmoAvailable |= ua > 0 || missiles > 0 || shooter.CurrentWeapon == BeamType.PowerBeam;
            SnapshotPlayer target = default;
            bool found = false;
            foreach (SnapshotPlayer value in _play.Client.SnapshotPlayers)
            {
                if (value.Slot == _play.LocalSlot || value.Health == 0
                    || !IsExpectedHeadshotTarget(value.Slot)) continue;
                target = PresentedTarget(value);
                found = true;
                break;
            }
            if (!found) return;
            _headshotTargetSeen = true;
            SnapshotPlayer shooterState = new()
            {
                Position = shooter.Position,
                Aim = shooter.ModGunVector
            };
            _headshotFacts.ObserveTarget(target, shooterState,
                HeadshotScenarioStage.At((uint)Math.Max(0, _headshotPlayingFrames - 1),
                    _options.Seconds),
                PresentedTargetHeadHeight(target.Slot));
        }

        private void ObserveLocalRootShot(CombatShot shot)
        {
            if (_options.Scenario != RenderedWanScenario.Headshot
                || _play.LocalSlot < 0 || shot.Actor.Slot != (byte)_play.LocalSlot
                || !shot.Actor.IsValid) return;
            _headshotFacts.Shots.ObserveLocalRoot(shot.Actor, shot.CommandSequence,
                shot.SourceWeapon);
        }

        private void ObserveAuthoritativeCombatEvent(CombatEvent value)
        {
            if (_options.Scenario != RenderedWanScenario.Headshot
                || _play.LocalSlot < 0 || value.Actor.Slot != (byte)_play.LocalSlot
                || !value.Actor.IsValid) return;
            if (value.Kind == CombatEventKind.Shot)
                _headshotFacts.Shots.ObserveAuthoritativeRoot(value);
            else if (value.Kind == CombatEventKind.Damage
                && value.Target.IsValid && IsExpectedHeadshotTarget(value.Target.Slot))
                _headshotFacts.Shots.ObserveAuthoritativeDamage(value);
        }

        private void ObservePredictedContact(CombatShot shot, CombatActor target,
            bool headshot)
        {
            if (_options.Scenario != RenderedWanScenario.Headshot
                || _play.LocalSlot < 0 || shot.Actor.Slot != (byte)_play.LocalSlot
                || !shot.Actor.IsValid || !target.IsValid
                || !IsExpectedHeadshotTarget(target.Slot)) return;
            _headshotFacts.Shots.ObservePredictedContact(shot, target, headshot);
        }

        private void ObserveTravel()
        {
            foreach (PlayerEntity player in _scene.GetPlayerEntities())
            {
                if (!player.ModIsInPlay) continue;
                int slot = player.SlotIndex;
                if (_seen[slot])
                {
                    float distance = (player.Position - _last[slot]).Length;
                    if (distance < 5) _travel[slot] += distance;
                }
                _seen[slot] = true;
                _last[slot] = player.Position;
            }
        }

        private void ConsumeCapture(RenderCaptureResult capture)
        {
            if (capture.Target != CaptureTargetKind.FinalPresentedFrame) return;
            _capturePending = false;
            _nextCaptureFrame += 60;
            double lit = RenderToolCaptureSupport.NonBlackFraction(capture);
            string fileName = $"frame-{_submittedFrames:000000}.png";
            string path = Path.Combine(_captureDirectory, fileName);
            bool saved = RenderToolCaptureSupport.Save(capture, path);
            string? sha256 = null;
            if (saved)
            {
                using FileStream stream = File.OpenRead(path);
                sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            }
            _captures.Add(new(fileName, capture.Width, capture.Height, lit, saved, sha256));
        }

        public void OnCapture(RenderCaptureResult capture) => ConsumeCapture(capture);

        public void OnClosing()
        {
            _play.ScriptInput = null;
            if (_options.Scenario == RenderedWanScenario.Headshot)
            {
                _play.LocalRootShotObserved -= ObserveLocalRootShot;
                _play.AuthoritativeCombatEventObserved -= ObserveAuthoritativeCombatEvent;
                _play.PredictedContactObserved -= ObservePredictedContact;
            }
            _presentation.DoCleanup();
        }

        private bool HasValidParticipant()
        {
            if (_play.Client.State != NetConnectionState.Playing
                || _play.LocalSlot < 0 || _play.LocalSlot >= 8
                || _play.Client.Accepted.MatchId == 0 || _play.Client.Roster.Length != 2)
                return false;
            int local = 0, bots = 0;
            ulong connectionId = _play.Client.Connection?.Id ?? 0;
            foreach (NetRosterEntry entry in _play.Client.Roster)
            {
                if (entry.IsBot)
                {
                    bots++;
                    continue;
                }
                if (entry.Slot != _play.LocalSlot || connectionId == 0
                    || entry.ConnectionId != connectionId)
                    return false;
                local++;
            }
            return local == 1 && bots == 1;
        }

        private bool HasDeterministicTarget()
        {
            // These are observations, not arm labels. A valid run must have
            // enough facts in each scheduled arm to show actual motion and
            // both requested range bands.
            return _headshotTargetSeen && _headshotFacts.FramesObserved >= 120
                && _headshotFacts.TargetMovedFrames >= 30
                && _headshotFacts.TargetAirborneFrames >= 30
                && _headshotFacts.VerticalFrames >= 30
                && _headshotFacts.StrafeFrames >= 30
                && _headshotFacts.CloseFrames >= 30
                && _headshotFacts.LongFrames >= 30
                && _headshotFacts.MaximumVerticalSpeed >= 0.05f;
        }

        private RenderedWanScenarioReport CreateScenarioReport(bool authoritativeWorker)
        {
            int localImperialist = _headshotFacts.Shots.CountLocalWeapon((byte)BeamType.Imperialist);
            int authoritativeImperialist = _headshotFacts.Shots.CountAuthorityWeapon((byte)BeamType.Imperialist);
            bool correctWeapon = _headshotCorrectWeapon
                && localImperialist > 0
                && localImperialist == _headshotFacts.Shots.LocalRootShots;
            bool authorityWeapon = authoritativeImperialist > 0
                && authoritativeImperialist == _headshotFacts.Shots.AuthoritativeRootShots;
            bool minimumDuration = _headshotPlayingFrames >= _options.Seconds * 60;
            bool cleanLink = _options.RoundTripMs == 0 && _options.JitterMs == 0
                && _options.LossPercent == 0;
            ScenarioGateResult gate = ScenarioGateResult.ValidateHeadshot(
                _headshotFacts, correctWeapon, authorityWeapon, _headshotZoomObserved,
                _headshotAmmoAvailable, minimumDuration, HasValidParticipant(),
                authoritativeWorker, HasDeterministicTarget(), cleanLink);
            return RenderedWanScenarioReport.Create(_headshotFacts, gate,
                correctWeapon, authorityWeapon, _headshotZoomObserved,
                _headshotAmmoAvailable, minimumDuration);
        }

        public RenderedClientResult Result(bool authoritativeWorker = true)
        {
            int local = _play.LocalSlot;
            int movingRemotes = 0;
            for (int slot = 0; slot < _travel.Length; slot++)
                if (slot != local && _seen[slot] && _travel[slot] > 1) movingRemotes++;
            double localTravel = local is >= 0 and < 8 ? _travel[local] : 0;
            bool lit = _captures.Any(capture => capture.Saved && capture.NonBlackFraction >= 0.01);
            bool movementEvidence = _options.Scenario == RenderedWanScenario.Headshot
                ? _headshotFacts.TargetMovedFrames > 0
                : localTravel > 1 && movingRemotes > 0;
            bool runtimePassed = _play.Client.State == NetConnectionState.Playing
                && _submittedFrames >= 300 && _acknowledgedFrames > 0 && lit
                && movementEvidence && _play.HasWorldState
                && _play.Client.SnapshotsReceived > 0 && _play.CombatEvents > 0;
            RenderedWanScenarioReport? scenario = _options.Scenario == RenderedWanScenario.Headshot
                ? CreateScenarioReport(authoritativeWorker) : null;
            NetMetrics metrics = _play.Client.Clock.Metrics;
            return new(runtimePassed, _submittedFrames, _acknowledgedFrames,
                _captureFailures, _captures.ToArray(), _play.Client.State,
                _play.Client.SnapshotsReceived, _play.HasWorldState, _play.CombatEvents,
                _play.DamageEvents, _play.Client.Rejected, localTravel, movingRemotes,
                _predictionBeforeReconnect, PredictionEvidence.Capture(_play.Prediction),
                _projectileBeforeReconnect, _play.ProjectilePresentation.Metrics,
                _hitPredictionBeforeReconnect, _play.HitPrediction.Metrics,
                _selfImpulseBeforeReconnect, _play.SelfImpulse.Metrics,
                _presentedCollisionBeforeReconnect, PresentedCollisionMetricsSnapshot.Capture(
                    _play.PresentedCollision.Metrics),
                _interpolationBeforeReconnect, _play.InterpolationMetrics,
                metrics.SmoothedRttMs, metrics.JitterMs,
                _debugPackets, _historyDebugPackets, _dynamicDebugPackets,
                _historicalPlayerMaximum, _dynamicColliderMaximum,
                _historicalDynamicEnabledReported, _registryOverflowReported, _truncatedReported,
                _debugMetrics,
                _reconnectCompleted, _reconnectSameMatch, _reconnectSameSeat,
                _connectionIdentityRotated, scenario, null);
        }
    }

    private sealed record CaptureEvidence(string File, int Width, int Height,
        double NonBlackFraction, bool Saved, string? Sha256);

    private sealed record PredictionEvidence(long Samples, double MeanError,
        double? P95Error, double WorstError, long Corrections,
        long HardCorrections, long HistoryMisses)
    {
        public static PredictionEvidence Capture(ClientPrediction prediction)
        {
            NetSample error = prediction.Error;
            return new(error.Count, error.Mean,
                error.Count == 0 ? null : error.Percentiles.P95, error.Max,
                prediction.Corrections, prediction.HardCorrections,
                prediction.HistoryMisses);
        }
    }

    private sealed record RenderedClientResult(bool RuntimePassed, int SubmittedFrames,
        int AcknowledgedFrames, int CaptureFailures, IReadOnlyList<CaptureEvidence> Captures,
        NetConnectionState FinalState, long Snapshots, bool WorldState,
        long CombatEvents, long DamageEvents, long RejectedDatagrams,
        double LocalTravel, int MovingRemotePlayers,
        PredictionEvidence? PredictionBeforeReconnect, PredictionEvidence PredictionAfterReconnect,
        ProjectilePresentationMeasurementSnapshot? ProjectileBeforeReconnect,
        ProjectilePresentationMeasurementSnapshot ProjectileAfterReconnect,
        HitPredictionMetrics? HitPredictionBeforeReconnect,
        HitPredictionMetrics HitPredictionAfterReconnect,
        SelfImpulseMetrics? SelfImpulseBeforeReconnect,
        SelfImpulseMetrics SelfImpulseAfterReconnect,
        PresentedCollisionMetricsSnapshot? PresentedCollisionBeforeReconnect,
        PresentedCollisionMetricsSnapshot PresentedCollisionAfterReconnect,
        SnapshotInterpolationMetrics? InterpolationBeforeReconnect,
        SnapshotInterpolationMetrics InterpolationAfterReconnect,
        double MeasuredRttMs, double MeasuredJitterMs,
        int DebugPackets, int HistoryDebugPackets, int DynamicDebugPackets,
        int HistoricalPlayerMaximum, int DynamicColliderMaximum,
        bool HistoricalDynamicEnabledReported, bool RegistryOverflowReported,
        bool TruncatedReported, HistoricalCollisionDebugMetrics DebugMetrics,
        bool ReconnectCompleted, bool ReconnectSameMatch,
        bool ReconnectSameSeat, bool ConnectionIdentityRotated,
        RenderedWanScenarioReport? Scenario, RenderBackendInfo? Backend);
}
