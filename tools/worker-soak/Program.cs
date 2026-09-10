using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using ProjectPrime.Server.Node.Workers;
using ProjectPrime.Server.Node.Reporting;
using ProjectPrime.Server.Node.Lobbies;
using ProjectPrime.Server.Shared;
using MphRead;
using MphRead.Identity;
using MphRead.Mods.Network;
using MphRead.Reporting;
using MphRead.Replay;

namespace ProjectPrime.WorkerSoak;

internal static class Program
{
    private sealed class Hosted(ManagedWorker worker, string directory)
    {
        public ManagedWorker Worker = worker;
        public string Directory = directory;
        public int Created;
        public bool Crashed;
        public bool CrashDeferralLogged;
    }
    private sealed class Running(Hosted host, MatchSpec spec)
    {
        public Hosted Host = host;
        public MatchSpec Spec = spec;
        public MatchPlacement? Placement;
        public SoakClientActor? Actor;
        public MatchCompletionSummary? Summary;
        public Task? Durability;
        public required SoakLobbyDriver.Round Round;
        public double StartedAt;
        public bool RematchEligible;
        public bool RematchAttempted;
        public bool IsRematch;
        public bool RematchRecoveryCounted;
        public bool ReconnectInjected;
        public double ReconnectEarliestAt;
        public long LastReconnectRecovery;
        public int LastReconnectEvidence;
    }
    private sealed record TerminalObservation(string Kind, WorkerId WorkerId, Guid WorkerIncarnation, bool DeliberateCrash);
    public static async Task<int> Main(string[] args)
    {
        try { return await RunAsync(Parse(args)); }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static async Task<int> RunAsync(Dictionary<string, string> args)
    {
        string Required(string key) => args.TryGetValue(key, out var value) ? value : throw new ArgumentException("Missing " + key);
        var scenario = SoakScenarioOptions.From(args);
        var requirements = new SoakRequirements(
            RequireNoFailures: args.TryGetValue("--require-no-failures", out var noFailures) ? bool.Parse(noFailures) : true,
            RequireDrain: args.TryGetValue("--require-drain", out var drain) ? bool.Parse(drain) : true,
            RequireCrashRecovery: args.TryGetValue("--require-crash-recovery", out var crashRecovery) ? bool.Parse(crashRecovery) : scenario.CrashesEnabled,
            RequireReconnectRecovery: args.TryGetValue("--require-reconnect-recovery", out var reconnectRecovery) ? bool.Parse(reconnectRecovery) : scenario.ReconnectsEnabled,
            RequireOutageRecovery: args.TryGetValue("--require-outage-recovery", out var outageRecovery) ? bool.Parse(outageRecovery) : scenario.OutagesEnabled,
            RequireRematchRecovery: args.TryGetValue("--require-rematch-recovery", out var rematchRecovery) ? bool.Parse(rematchRecovery) : scenario.RematchesEnabled);
        requirements.Validate(scenario);
        string assembly = Path.GetFullPath(Required("--worker-assembly")), data = Path.GetFullPath(Required("--data-dir"));
        string output = Path.GetFullPath(Required("--output")); Directory.CreateDirectory(output);
        using var critical = new CriticalEventLog(output);
        try
        {
        var profile = await DescribeAsync(assembly, data);
        using var log = new RotatingLog(output);
        using var trend = new StreamWriter(Path.Combine(output, "trend.jsonl"), false);
        var elapsed = Stopwatch.StartNew();
        await using var manager = new WorkerManager(new(Guid.NewGuid()), Guid.NewGuid(), maximumWorkers: scenario.Workers + 2);
        await using var backend = await SoakBackend.CreateAsync(manager.NodeId, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), output, CancellationToken.None);
        await using var outbox = new MatchReportOutbox(new(Path.Combine(output, "outbox"), MaximumReports: 1024,
            MaximumBytes: 512L * 1024 * 1024, QueueCapacity: 128), backend.Transport);
        await using var ingest = new NodeReportIngestor(outbox, Path.Combine(output, "receipts"), queueCapacity: 256);
        await using var scheduler = new WorkerScheduler(manager, reports: ingest);
        using var signer = new WorkerAdmissionIssuer("soak-key");
        var events = Channel.CreateBounded<(WorkerId WorkerId, WorkerEvent Event)>(4096);
        int eventFailure = 0, eventFailureClaimed = 0;
        Exception? eventFailureException = null;
        void FailEvents(Exception error)
        {
            if (Interlocked.CompareExchange(ref eventFailureClaimed, 1, 0) == 0)
            {
                Interlocked.Exchange(ref eventFailureException, error);
                Volatile.Write(ref eventFailure, 1);
            }
        }
        scheduler.Observed += (worker, value) =>
        {
            if (value is not MatchReportReady && !events.Writer.TryWrite((worker.Id, value)))
                FailEvents(new IOException("Soak observation queue overflowed."));
        };
        scheduler.ReportReady += (assignment, value) =>
        {
            if (!events.Writer.TryWrite((assignment.WorkerId, value))) FailEvents(new IOException("Soak report observation queue overflowed."));
        };
        using var driver = new SoakLobbyDriver(scheduler, manager, signer,
            new NodeContentCatalog(new[] { "MP1 SANCTORUS", "MP2 HARVESTER" }.Select(map => new ContentIdentity(map,
                profile.ContentHash, profile.ContentVersion, profile.BuildVersion, profile.ProtocolVersion))),
            scenario.Roster, backend.RentPlayersAsync, backend.ReleasePlayers);
        var hosts = new List<Hosted>();
        var running = new Dictionary<MatchId, Running>();

        int created = 0, completed = 0, interrupted = 0, failures = 0, generation = 0;
        int rematchAttempts = 0, rematchPlaced = 0, rematchRecovered = 0, rematchFailures = 0, crashAttempts = 0, crashRecovered = 0;
        long inputs = 0, snapshots = 0, observers = 0, reconnects = 0, playing = 0;
        long reconnectAttempts = 0, reconnectRecovered = 0;
        double occupancyIntegral = 0, playingOccupancyIntegral = 0, connectedOccupancyIntegral = 0, occupancySeconds = 0;
        double previousLoop = 0, lowOccupancySince = -1, lastCompletionAt = 0;
        Dictionary<SoakOccupancyPhase, double> phaseSeconds = new();
        Dictionary<MatchId, TerminalObservation> terminals = new();
        bool disruptionObserved = false;
        double nextSample = 0, nextTrend = 0, nextCrash = scenario.CrashSeconds;
        using var end = new CancellationTokenSource();
        critical.Write("start", "Harness+Node+Backend", status: "running");
        log.Write(new { kind = "start", utc = DateTimeOffset.UtcNow, scenario, requirements, profile,
            evidence = "Real Node lobby/coordinator/scheduler, Worker processes, signed UDP actors, and Backend TLS report ingestion with persistent SQLite; HTTP503 outage injection." });
        async Task<Hosted> StartAsync(bool replacement = false)
        {
            string directory = Path.Combine(output, "worker-" + generation++); Directory.CreateDirectory(directory);
            var worker = await scheduler.StartWorkerAsync(new WorkerLaunchOptions
            {
                FileName = "dotnet", WorkingDirectory = Path.GetDirectoryName(assembly), Content = profile,
                ArtifactDirectory = directory, Capacity = new(scenario.MatchesPerWorker, scenario.MatchesPerWorker * scenario.Roster.RosterSeats, 0, 0),
                StartupTimeout = TimeSpan.FromSeconds(60), ShutdownTimeout = TimeSpan.FromSeconds(45),
                Arguments = [assembly, "--content-dir", data, "--content-version", profile.ContentVersion,
                    "--content-hash", profile.ContentHash, "--lanes", scenario.Lanes.ToString(), "--max-matches", scenario.MatchesPerWorker.ToString(),
                    "--max-matches-per-lane", ((scenario.MatchesPerWorker + scenario.Lanes - 1) / scenario.Lanes).ToString(), "--replay-dir", Path.Combine(directory, "replays")]
            });
            var host = new Hosted(worker, directory); hosts.Add(host);
            if (!worker.TrySend(new UpdateNodeSigningKey(signer.KeyId, signer.ExportPublicKey()))) throw new IOException("Worker key command rejected.");
            if (replacement) crashRecovered++;
            return host;
        }
        for (int i = 0; i < scenario.Workers; i++) await StartAsync();
        // Wall-clock duration excludes process startup and includes only the live workload interval.
        elapsed.Restart();
        bool draining = false;
        while (elapsed.Elapsed.TotalSeconds < scenario.Seconds || running.Count > 0)
        {
            if (!draining && elapsed.Elapsed.TotalSeconds >= scenario.Seconds)
            {
                draining = true;
                scheduler.Drain("Soak duration reached");
                backend.SetOutage(false);
            }
            double now = elapsed.Elapsed.TotalSeconds;
            double delta = now - previousLoop; previousLoop = now;
            if (!draining && now > Math.Min(20, scenario.Seconds / 4.0))
            {
                if (running.Count < scenario.TargetMatches)
                {
                    if (lowOccupancySince < 0) lowOccupancySince = now;
                    if (now - lowOccupancySince > 30) throw new TimeoutException("Target match occupancy stalled for more than 30 seconds.");
                }
                else lowOccupancySince = -1;
                if (now - lastCompletionAt > scenario.RoundSeconds + 90) throw new TimeoutException("No completed round within the progress deadline.");
            }
            if (Volatile.Read(ref eventFailure) != 0) throw Volatile.Read(ref eventFailureException) ?? new IOException("Soak event consumer failed.");
            if (!draining && scenario.OutagesEnabled) backend.SetOutage((int)elapsed.Elapsed.TotalSeconds % 120 < 30);
            if (draining && elapsed.Elapsed.TotalSeconds > scenario.Seconds + scenario.RoundSeconds + 90) throw new TimeoutException("Final matches did not drain naturally.");
            while (events.Reader.TryRead(out var item))
            {
                switch (item.Event)
                {
                    case MatchReady ready when running.TryGetValue(ready.Placement.MatchId, out var readyActive):
                        readyActive.Placement = ready.Placement;
                        readyActive.Actor ??= new(readyActive.Spec, ready.Placement, signer);
                        break;
                    case MatchCompleted result:
                        if (!running.TryGetValue(result.Summary.MatchId, out var completedActive))
                            throw new InvalidDataException("Terminal event referenced an unknown or already retired match.");
                        ObserveTerminal(completedActive, "completed", deliberateCrash: false);
                        completed++; lastCompletionAt = elapsed.Elapsed.TotalSeconds; completedActive.Summary = result.Summary;
                        RetireActor(completedActive);
                        break;
                    case MatchReportReady report when running.TryGetValue(report.MatchId, out var reportActive):
                        reportActive.Durability = ingest.WaitForDurabilityAsync(end.Token);
                        break;
                    case MatchReportReady report:
                        throw new InvalidDataException("Report event referenced an unknown or already retired match.");
                    case MatchInterrupted stop:
                        if (!running.TryGetValue(stop.MatchId, out var interruptedActive))
                        {
                            if (terminals.TryGetValue(stop.MatchId, out var priorTerminal)
                                && priorTerminal.Kind == "interrupted") break;
                            throw new InvalidDataException("Terminal event referenced an unknown or already retired match.");
                        }
                        ObserveTerminal(interruptedActive, "interrupted", deliberateCrash: interruptedActive.Host.Crashed);
                        interrupted++; await ReleaseAsync(interruptedActive); break;
                    case MatchFailed failed:
                        if (!running.TryGetValue(failed.MatchId, out var failedActive))
                            throw new InvalidDataException("Terminal event referenced an unknown or already retired match.");
                        ObserveTerminal(failedActive, "failed", deliberateCrash: false);
                        failures++; log.Write(new { kind = "match_failed", failed });
                        critical.Write("match_failed", "Harness+Node+Backend", failedActive.Host.Worker.Id.Value.ToString("N"),
                            failedActive.Host.Worker.Incarnation.ToString("N"), failed.MatchId.Value.ToString("N"), status: "failed");
                        await ReleaseAsync(failedActive);
                        break;
                    case WorkerFault fault:
                        disruptionObserved = true;
                        critical.Write("worker_fault", "Harness+Node+Backend", fault.WorkerId.Value.ToString("N"),
                            fault.WorkerIncarnation.ToString("N"), status: "faulted");
                        log.Write(new { kind = "worker_fault", fault }); break;
                }
            }
            foreach (var active in running.Values.ToArray())
            {
                active.Actor?.Tick();
                ReconcileReconnectRecovery(active);
                if (active.IsRematch && !active.RematchRecoveryCounted && active.Actor?.Snapshot.HasProgress == true)
                {
                    active.RematchRecoveryCounted = true; rematchRecovered++;
                    critical.Write("rematch", "Harness+Node+Backend", active.Host.Worker.Id.Value.ToString("N"),
                        active.Host.Worker.Incarnation.ToString("N"), active.Spec.MatchId.Value.ToString("N"), status: "recovered");
                }
                if (elapsed.Elapsed.TotalSeconds - active.StartedAt > scenario.RoundSeconds + 90)
                    throw new TimeoutException("A match exceeded its creation/play/completion deadline.");
                if (active.Actor?.Snapshot.Failures > 0)
                {
                    critical.Write("actor_failed", "Harness+Node+Backend", active.Host.Worker.Id.Value.ToString("N"),
                        active.Host.Worker.Incarnation.ToString("N"), active.Spec.MatchId.Value.ToString("N"), status: "client_failed");
                    log.Write(new { kind = "actor_failed", active.Spec.MatchId, elapsed = elapsed.Elapsed.TotalSeconds,
                        draining, peers = active.Actor.Failures });
                    throw new IOException("A soak client reported admission or connection failure.");
                }
                if (active.Durability?.IsCompleted == true)
                {
                    await active.Durability;
                    ArchiveArtifacts(active);
                    if (!draining && active.RematchEligible && !active.RematchAttempted)
                        await RematchAsync(active);
                    else
                        await ReleaseAsync(active);
                }
            }
            var occupancySample = SoakOccupancy.Sample(running.Values.Where(active => active.Actor != null)
                .Select(active => active.Actor!.Snapshot).ToArray(), running.Count, scenario.TargetMatches,
                scenario.TargetClientPeers, running.Values.Any(active => active.Summary != null || active.Durability != null), disruptionObserved);
            if (!draining)
            {
                occupancyIntegral += delta * occupancySample.Logical;
                playingOccupancyIntegral += delta * occupancySample.PlayingPeers;
                connectedOccupancyIntegral += delta * occupancySample.ConnectedPeers;
                occupancySeconds += delta;
                phaseSeconds[occupancySample.Phase] = phaseSeconds.GetValueOrDefault(occupancySample.Phase) + delta;
            }
            disruptionObserved = false;
            foreach (var host in hosts.ToArray())
            {
                if (host.Worker.Completion.IsCompleted)
                {
                    if (!host.Crashed) throw new IOException("Unexpected Worker exit: " + host.Worker.Snapshot().FailureReason);
                    disruptionObserved = true;
                    foreach (var active in running.Values.Where(active => active.Host == host).ToArray())
                    {
                        if (SoakRecoveryPolicy.ShouldInterruptOnWorkerExit(active.Summary != null))
                        {
                            ObserveTerminal(active, "interrupted", deliberateCrash: true);
                            interrupted++;
                            await ReleaseAsync(active);
                        }
                    }
                    await manager.RetireAsync(host.Worker.Id); hosts.Remove(host); await StartAsync(replacement: true); continue;
                }
            }
            while (!draining && running.Count < scenario.TargetMatches && ingest.CanAcceptOfficial)
            {
                string map = created % 2 == 0 ? "MP1 SANCTORUS" : "MP2 HARVESTER";
                MatchMode mode = (created % 4) switch { 0 => MatchMode.Defender, 2 => MatchMode.Nodes, _ => MatchMode.Battle };
                SoakLobbyDriver.Round round;
                try { round = await driver.StartAsync(map, mode, scenario.RoundSeconds, end.Token); }
                catch (WorkerPlacementException) { break; }
                var host = hosts.Single(host => host.Worker.Id == round.Placement.WorkerId);
                var active = new Running(host, round.Spec) { Round = round, Placement = round.Placement,
                    Actor = new(round.Spec, round.Placement, signer), StartedAt = elapsed.Elapsed.TotalSeconds,
                    RematchEligible = scenario.RematchesEnabled && (created + 1) % scenario.RematchEvery == 0,
                    ReconnectEarliestAt = elapsed.Elapsed.TotalSeconds + Math.Min(
                        SoakRecoveryPolicy.PreferredReconnectStartSeconds,
                        Math.Max(1, scenario.RoundSeconds / 5.0)) };
                running.Add(round.Spec.MatchId, active); host.Created++; created++;
                log.Write(new { kind = "lobby_started", round.Spec.MatchId, round.LobbyId, round.History });
            }
            Hosted? plannedCrash = null;
            if (!draining && scenario.CrashesEnabled && now >= nextCrash && hosts.Count > 1)
            {
                Hosted candidate = hosts[0];
                var pending = running.Values.Where(active => active.Host == candidate && active.Actor != null)
                    .SelectMany(active => active.Actor!.PendingReconnects.Select(peer => new
                    {
                        MatchId = active.Spec.MatchId, Peer = peer
                    })).ToArray();
                if (pending.Length == 0)
                {
                    candidate.CrashDeferralLogged = false;
                    plannedCrash = candidate;
                }
                else if (!candidate.CrashDeferralLogged)
                {
                    candidate.CrashDeferralLogged = true;
                    log.Write(new { kind = "worker_crash_deferred", candidate.Worker.Id,
                        candidate.Worker.Incarnation, reason = "pending_reconnect_generations", pending });
                    critical.Write("worker_crash_deferred", "Harness+Node+Backend",
                        candidate.Worker.Id.Value.ToString("N"), candidate.Worker.Incarnation.ToString("N"),
                        status: "pending-reconnect");
                }
            }
            if (!draining && scenario.ReconnectsEnabled)
            {
                foreach (var active in running.Values.ToArray())
                {
                    if (active.ReconnectInjected || now < active.ReconnectEarliestAt
                        || active.Actor is not { } actor
                        || !SoakRecoveryPolicy.ShouldReconnect(plannedCrash is not null, active.Host == plannedCrash))
                        continue;

                    SoakReconnectReadiness readiness = actor.ReconnectReadiness();
                    if (!readiness.Ready) continue;
                    SoakReconnectBatch batch = actor.Reconnect(readiness.RecoveryDeadlineSeconds);
                    active.ReconnectInjected = true;
                    reconnectAttempts += batch.Attempts;
                    log.Write(new { kind = "reconnect_injected", match = active.Spec.MatchId,
                        batch.RecoveryDeadlineSeconds, peers = batch.Peers,
                        pending = actor.PendingReconnects });
                    critical.Write("reconnect_injected", "Harness+Node+Backend",
                        active.Host.Worker.Id.Value.ToString("N"), active.Host.Worker.Incarnation.ToString("N"),
                        active.Spec.MatchId.Value.ToString("N"), status: "pending", phase: "Playing",
                        elapsed: now);
                }
            }
            if (plannedCrash is { } victim)
            {
                victim.Crashed = true; crashAttempts++;
                disruptionObserved = true;
                if (victim.Worker.Snapshot().ProcessId is int pid) Process.GetProcessById(pid).Kill(entireProcessTree: true);
                critical.Write("worker_crash", "Harness+Node+Backend", victim.Worker.Id.Value.ToString("N"),
                    victim.Worker.Incarnation.ToString("N"), status: "deliberate", elapsed: elapsed.Elapsed.TotalSeconds);
                log.Write(new { kind = "injected_worker_crash", victim.Worker.Id, victim.Worker.Incarnation, elapsed = elapsed.Elapsed.TotalSeconds });
                nextCrash += scenario.CrashSeconds;
            }
            if (elapsed.Elapsed.TotalSeconds >= nextSample)
            {
                var backendSample = await backend.CountsAsync();
                log.Write(new { kind = "sample", elapsed = elapsed.Elapsed.TotalSeconds, created, completed, interrupted, failures,
                    workers = manager.Snapshot().Select(snapshot => new { snapshot.WorkerId, snapshot.Incarnation, snapshot.Status, snapshot.Capacity, snapshot.Health,
                        Matches = snapshot.Matches.Select(pair => new { MatchId = pair.Key.Value, Status = pair.Value }).ToArray(), snapshot.FailureReason, snapshot.ProcessId }).ToArray(),
                    actors = running.Values.Where(active => active.Actor != null).Select(active => new { active.Spec.MatchId,
                        snapshot = active.Actor!.Snapshot, peers = active.Actor.Peers }).ToArray(),
                    outbox = outbox.Status, ingested = ingest.Ingested, backend = new { backendSample.PersistedReports,
                        backendSample.Attempts, backendSample.OutageResponses } });
                if (elapsed.Elapsed.TotalSeconds >= nextTrend)
                {
                    var processes = new List<object>
                    {
                        new { role = "Harness+Node+Backend", workerId = (string?)null,
                            incarnation = (string?)null, process = ReadProcess(Environment.ProcessId) }
                    };
                    processes.AddRange(manager.Snapshot().Select(snapshot => (object)new
                    {
                        role = "Worker", workerId = snapshot.WorkerId.Value.ToString("N"),
                        incarnation = snapshot.Incarnation.ToString("N"), process = ReadProcess(snapshot.ProcessId)
                    }));
                    var point = new { utc = DateTimeOffset.UtcNow, elapsed = elapsed.Elapsed.TotalSeconds,
                        created, completed, interrupted, failures, active = running.Count, ingested = ingest.Ingested,
                        outbox = outbox.Status, backendSample.PersistedReports,
                        processes = processes.ToArray() };
                    trend.WriteLine(JsonSerializer.Serialize(point)); trend.Flush(); nextTrend += 30;
                }
                nextSample += 1;
            }
            await Task.Delay(8);
        }
        scheduler.Drain("Soak duration reached");
        using var drainDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await scheduler.WaitForDrainAsync(drainDeadline.Token);
        backend.SetOutage(false);
        while (outbox.Status.DurablePending > 0)
        {
            drainDeadline.Token.ThrowIfCancellationRequested();
            await Task.Delay(50, drainDeadline.Token);
        }
        await scheduler.ShutdownAsync("Soak complete", drainDeadline.Token);
        end.Cancel();
        ValidateReconciliation();
        var backendFinal = await backend.StatsAsync();
        double logicalOccupancy = occupancySeconds > 0 ? occupancyIntegral / occupancySeconds : 0;
        double playingOccupancy = occupancySeconds > 0 ? playingOccupancyIntegral / occupancySeconds : 0;
        double connectedOccupancy = occupancySeconds > 0 ? connectedOccupancyIntegral / occupancySeconds : 0;
        bool recoveryRequirements = (!requirements.RequireCrashRecovery || crashAttempts > 0 && crashAttempts == crashRecovered)
            && (!requirements.RequireReconnectRecovery || reconnectAttempts > 0 && reconnectAttempts == reconnectRecovered)
            && (!requirements.RequireOutageRecovery || backend.OutageAttempts > 0 && backend.OutageAttempts == backend.OutageRecoveries)
            && (!requirements.RequireRematchRecovery || rematchAttempts > 0 && rematchAttempts == rematchRecovered && rematchFailures == 0);
        bool passed = (!requirements.RequireNoFailures || failures == 0)
            && (!requirements.RequireDrain || running.Count == 0 && outbox.Status.DurablePending == 0)
            && recoveryRequirements && created == terminals.Count && completed > 0
            && scenario.HasRequiredTraffic(inputs, observers, playing)
            && occupancySeconds > 0 && logicalOccupancy >= .9
            && backendFinal.PersistedReports == ingest.Ingested && backendFinal.PayloadHashMismatches == 0;
        var summary = new { kind = "complete", utc = DateTimeOffset.UtcNow, elapsedSeconds = elapsed.Elapsed.TotalSeconds,
            scenario, requirements, requestedSeconds = scenario.Seconds, workloadSeconds = scenario.Seconds,
            drainSeconds = elapsed.Elapsed.TotalSeconds - scenario.Seconds, created, completed, interrupted, failures,
            inputs, snapshots, observers, reconnects, playing, reconnectAttempts, reconnectRecovered,
            counters = new { crashAttempts, crashRecovered, outageAttempts = backend.OutageAttempts,
                outageRecovered = backend.OutageRecoveries, rematchAttempts, rematchPlaced, rematchRecovered, rematchFailures },
            ingested = ingest.Ingested, backend = backendFinal, outbox = outbox.Status,
            occupancy = new { logical = logicalOccupancy, playingPeers = playingOccupancy,
                connectedPeers = connectedOccupancy, seconds = occupancySeconds, phases = phaseSeconds },
            terminals = terminals.GroupBy(pair => pair.Value.Kind).ToDictionary(group => group.Key, group => group.Count()),
            activeAtEnd = running.Count, passed };
        log.Write(summary); await File.WriteAllTextAsync(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(summary));
        return passed ? 0 : 2;

        void RetireActor(Running active)
        {
            if (active.Actor == null) return;
            ReconcileReconnectRecovery(active, finalPoll: true);
            var actor = active.Actor;
            if (actor.PendingReconnects.Count != 0)
            {
                log.Write(new { kind = "reconnect_pending_at_retirement", match = active.Spec.MatchId,
                    pending = actor.PendingReconnects, attempts = actor.ReconnectAttempts,
                    recovered = actor.ReconnectRecoveries });
                critical.Write("reconnect_pending_at_retirement", "Harness+Node+Backend",
                    active.Host.Worker.Id.Value.ToString("N"), active.Host.Worker.Incarnation.ToString("N"),
                    active.Spec.MatchId.Value.ToString("N"), status: "pending");
            }
            var state = actor.Snapshot;
            inputs += state.InputsSent; snapshots += state.PlayerSnapshots; observers += state.ObserverSnapshots; reconnects += state.Reconnects; playing += state.PlayingSnapshots;
            try { actor.Dispose(); }
            finally { active.Actor = null; }
        }

        void ReconcileReconnectRecovery(Running active, bool finalPoll = false)
        {
            if (active.Actor is not { } actor) return;
            if (finalPoll) actor.Tick();
            long recovered = actor.ReconnectRecoveries;
            if (recovered < active.LastReconnectRecovery)
                throw new InvalidDataException($"Reconnect recovery regressed for {active.Spec.MatchId}.");
            if (recovered > active.LastReconnectRecovery)
            {
                reconnectRecovered += recovered - active.LastReconnectRecovery;
                active.LastReconnectRecovery = recovered;
            }
            var evidence = actor.ReconnectRecoveryEvidence;
            while (active.LastReconnectEvidence < evidence.Count)
            {
                SoakReconnectRecoveryEvidence item = evidence[active.LastReconnectEvidence++];
                log.Write(new { kind = "reconnect_recovered", match = active.Spec.MatchId,
                    injection = item.Injection, final = item.Final });
                critical.Write("reconnect_recovered", "Harness+Node+Backend",
                    active.Host.Worker.Id.Value.ToString("N"), active.Host.Worker.Incarnation.ToString("N"),
                    active.Spec.MatchId.Value.ToString("N"), status: "recovered", phase: item.Final.Phase);
            }
        }
        async Task ReleaseAsync(Running active)
        {
            RetireActor(active); ingest.CancelReservation(active.Spec.MatchId);
            await driver.CompleteAndReturnAsync(active.Round, end.Token);
            log.Write(new { kind = "lobby_returned", active.Spec.MatchId, active.Round.LobbyId, active.Round.History });
            scheduler.ForgetMatch(active.Spec.MatchId); running.Remove(active.Spec.MatchId);
        }
        void ObserveTerminal(Running active, string kind, bool deliberateCrash)
        {
            if (active.Placement is not { } placement)
                throw new InvalidDataException("Terminal event arrived before a Worker placement.");
            if (kind == "interrupted" && !deliberateCrash)
                throw new InvalidDataException("An interrupted match was not caused by a deliberate crashed Worker incarnation.");
            if (!terminals.TryAdd(active.Spec.MatchId,
                    new TerminalObservation(kind, placement.WorkerId, placement.WorkerIncarnation, deliberateCrash)))
            {
                critical.Write("terminal_duplicate", "Harness+Node+Backend", placement.WorkerId.Value.ToString("N"),
                    placement.WorkerIncarnation.ToString("N"), active.Spec.MatchId.Value.ToString("N"), status: kind);
                throw new InvalidDataException("Duplicate or conflicting terminal status for a MatchId.");
            }
            critical.Write("terminal", "Harness+Node+Backend", placement.WorkerId.Value.ToString("N"),
                placement.WorkerIncarnation.ToString("N"), active.Spec.MatchId.Value.ToString("N"), status: kind);
        }
        async Task RematchAsync(Running active)
        {
            active.RematchAttempted = true; rematchAttempts++;
            string map = active.Spec.Content.MapKey == "MP1 SANCTORUS" ? "MP2 HARVESTER" : "MP1 SANCTORUS";
            MatchMode mode = active.Spec.Rules.Mode switch
            {
                MatchMode.Defender => MatchMode.Nodes,
                MatchMode.Nodes => MatchMode.Battle,
                _ => MatchMode.Defender
            };
            try
            {
                SoakLobbyDriver.Round nextRound = await driver.RematchAsync(active.Round, map, mode,
                    scenario.RoundSeconds, end.Token);
                var host = hosts.Single(hosted => hosted.Worker.Id == nextRound.Placement.WorkerId);
                var next = new Running(host, nextRound.Spec)
                {
                    Round = nextRound, Placement = nextRound.Placement,
                    Actor = new(nextRound.Spec, nextRound.Placement, signer),
                    StartedAt = elapsed.Elapsed.TotalSeconds, RematchEligible = false
                };
                running.Remove(active.Spec.MatchId);
                running.Add(next.Spec.MatchId, next); host.Created++; created++;
                next.IsRematch = true; rematchPlaced++;
                critical.Write("rematch", "Harness+Node+Backend", host.Worker.Id.Value.ToString("N"),
                    host.Worker.Incarnation.ToString("N"), next.Spec.MatchId.Value.ToString("N"), status: "placed");
                log.Write(new { kind = "lobby_rematch", previous = active.Spec.MatchId, next = next.Spec.MatchId,
                    next.Round.LobbyId, next.Round.History });
            }
            catch
            {
                rematchFailures++; failures++; running.Remove(active.Spec.MatchId);
                critical.Write("rematch_failed", "Harness+Node+Backend", active.Host.Worker.Id.Value.ToString("N"),
                    active.Host.Worker.Incarnation.ToString("N"), active.Spec.MatchId.Value.ToString("N"), status: "failed");
                throw;
            }
        }
        void ValidateReconciliation()
        {
            if (running.Count != 0) throw new InvalidDataException("Run ended with active matches.");
            if (terminals.Count != created) throw new InvalidDataException($"Terminal reconciliation mismatch: created={created}, terminals={terminals.Count}.");
            foreach (var pair in terminals)
            {
                var terminal = pair.Value;
                if (terminal.Kind == "interrupted" && !terminal.DeliberateCrash)
                    throw new InvalidDataException("Interrupted match was not attributed to a deliberate Worker crash.");
                if (terminal.WorkerId.Value == Guid.Empty || terminal.WorkerIncarnation == Guid.Empty)
                    throw new InvalidDataException("Terminal event is missing Worker identity provenance.");
            }
        }
        void ArchiveArtifacts(Running active)
        {
            if (active.Summary is not { } result) throw new InvalidDataException("Report became durable without a completion summary.");
            foreach (string path in new[] {
                result.ReplayId.HasValue ? Path.Combine(active.Host.Directory, "replays", result.ReplayId.Value + ".fpreplay") : null,
                result.TelemetryId.HasValue ? Path.Combine(active.Host.Directory, result.TelemetryId.Value + ".telemetry.json") : null }.OfType<string>())
            {
                if (!File.Exists(path)) throw new FileNotFoundException("Completed artifact is missing.", path);
                object validation;
                if (path.EndsWith(".fpreplay", StringComparison.Ordinal)) validation = ReplayArtifactValidator.Validate(path);
                else
                {
                    using var telemetry = JsonDocument.Parse(File.ReadAllBytes(path));
                    var root = telemetry.RootElement;
                    if (root.GetProperty("Format").GetInt32() != 1 || !root.GetProperty("Completed").GetBoolean()
                        || root.GetProperty("Id").GetGuid() != result.TelemetryId
                        || root.GetProperty("MatchId").GetUInt32() != active.Placement!.WireMatchId.Value
                        || root.GetProperty("EndTick").GetUInt32() < root.GetProperty("StartTick").GetUInt32()
                        || root.GetProperty("DroppedEvents").GetInt64() != 0
                        || root.GetProperty("Events").GetArrayLength() > 131072)
                        throw new InvalidDataException("Completed telemetry failed structural validation.");
                    validation = new { Events = root.GetProperty("Events").GetArrayLength(), DroppedEvents = 0 };
                }
                using var stream = File.OpenRead(path);
                string hash = Convert.ToHexString(SHA256.HashData(stream));
                log.Write(new { kind = "artifact", active.Spec.MatchId, name = Path.GetFileName(path), stream.Length, hash, validation });
                stream.Dispose(); File.Delete(path); // Only this harness's acknowledged artifact files.
            }
        }
        }
        catch (Exception error)
        {
            critical.TryWriteFatal("Harness+Node+Backend", status: error.GetType().Name);
            throw;
        }
    }
    private static object ReadProcess(int? processId)
    {
        try
        {
            if (!processId.HasValue) return new { missing = true };
            using var process = Process.GetProcessById(processId.Value);
            return new { process.Id, process.WorkingSet64, process.PrivateMemorySize64,
                Threads = process.Threads.Count, process.HandleCount, CpuSeconds = process.TotalProcessorTime.TotalSeconds };
        }
        catch (InvalidOperationException) { return new { exited = true }; }
        catch (ArgumentException) { return new { exited = true }; }
    }
    private static async Task<WorkerContentIdentity> DescribeAsync(string assembly, string data)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in new[] { assembly, "--describe-content", "true", "--content-dir", data, "--content-version", "AMHE1" }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("Cannot start content profiler.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(), stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(1));
        if (process.ExitCode != 0) throw new IOException("Content profiler failed: " + await stderr);
        string line = (await stdout).Split('\n').Last(text => text.StartsWith("{", StringComparison.Ordinal));
        return JsonSerializer.Deserialize<WorkerContentIdentity>(line) ?? throw new InvalidDataException("Missing content profile.");
    }
    private static Dictionary<string, string> Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 0; index < args.Length; index += 2)
            if (index + 1 >= args.Length || !values.TryAdd(args[index], args[index + 1])) throw new ArgumentException("Invalid soak arguments.");
        return values;
    }
    private sealed class RotatingLog(string directory) : IDisposable
    {
        private StreamWriter? _writer;
        private long _bytes;
        private int _index;
        public void Write(object value)
        {
            string line = JsonSerializer.Serialize(value);
            if (_writer == null || _bytes > 4 * 1024 * 1024)
            {
                _writer?.Dispose();
                _writer = new StreamWriter(Path.Combine(directory, $"metrics-{_index++ % 32:D2}.jsonl"), false); _bytes = 0;
            }
            _writer.WriteLine(line); _writer.Flush(); _bytes += line.Length * 2L + 2;
        }
        public void Dispose() => _writer?.Dispose();
    }
}
