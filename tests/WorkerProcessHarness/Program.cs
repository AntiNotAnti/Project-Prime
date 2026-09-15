using System.IO.Pipes;
using ProjectPrime.Server.Shared;

string Get(string key) => args[Array.IndexOf(args, key) + 1];
string? Optional(string key)
{
    int index = Array.IndexOf(args, key);
    return index < 0 ? null : args[index + 1];
}
string mode = Get("--mode");
if (mode == "snapshot-rate" && Get("--snapshot-rate-hz") != "60") return 30;
if (mode == "adaptive-timing"
    && (Get("--adaptive-timing") != "True" || Get("--adaptive-input-playout") != "True")) return 31;
if (mode == "network-flags"
    && (Get("--transport-queue-v2") != "True"
        || Get("--transport-critical-reserve-enabled") != "False"
        || Get("--critical-transport-reserve") != "16"
        || Get("--worker-global-network-budget-enabled") != "False"
        || Get("--max-datagrams-per-pump") != "256"
        || Get("--reliable-adaptive-rto") != "True")) return 32;
if (mode == "ack-coalescing" && Get("--ack-coalescing") != "True") return 33;
var activeMatches = new Dictionary<MatchId, MatchSpec>();
long acceptedMatches = 0;
if (mode == "environment"
    && (Environment.GetEnvironmentVariable("PRIME_NODE_DIRECTORY_SECRET") != null
        || Environment.GetEnvironmentVariable("PRIME_DATA_DIRECTORY") != Get("--expected-data-directory"))) return 29;
if (mode == "exit") return 17;
if (mode == "hang") { await Task.Delay(60000); return 0; }
var node = new NodeId(Guid.Parse(Get("--node-id")));
var worker = new WorkerId(Guid.Parse(Get("--worker-id")));
Guid incarnation = Guid.Parse(Get("--worker-incarnation"));
// Bound stdin to the exact token size; never copy it into output or args.
char[] token = new char[65];
int count = 0;
while (count < token.Length)
{
    int read = await Console.In.ReadAsync(token.AsMemory(count, token.Length - count));
    if (read == 0) break;
    count += read;
}
if (count != 65 || token[64] != '\n') return 18;
string startupSecret = new(token, 0, 64);
if (mode == "secret-canary" && Environment.GetCommandLineArgs().Any(argument => argument.Contains(startupSecret, StringComparison.Ordinal))) return 29;
if (mode == "secret-output")
{
    Console.WriteLine("A24-lifecycle-output-canary");
    Console.Error.WriteLine("A24-lifecycle-output-canary");
    return 17;
}
using var pipe = new NamedPipeClientStream(".", Get("--node-pipe"), PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
await pipe.ConnectAsync(5000);
string secret = startupSecret;
Array.Clear(token);
await WorkerIpcCodec.WriteAsync(pipe, new WorkerHello(worker, mode == "identity" ? Guid.NewGuid() : incarnation,
    node, mode == "token" ? new string('0', 64) : secret,
    Optional("--test-content-build") ?? "test",
    new(Optional("--test-content-version") ?? "1",
        Optional("--test-content-hash") ?? (mode == "profile" ? "wrong" : "hash"),
        Optional("--test-content-build") ?? "test",
        byte.Parse(Optional("--test-content-protocol") ?? "8"))));
secret = "";
if (mode is "token" or "identity") { await Task.Delay(10000); return 0; }
if (await WorkerIpcCodec.ReadAsync(pipe) is not WorkerConfigure configure) return 19;
await WorkerIpcCodec.WriteAsync(pipe, new WorkerReady(worker, mode == "stale-ready" ? Guid.NewGuid() : incarnation,
    mode == "bad-capacity" ? new WorkerCapacity(configure.Capacity.MatchLimit + 1, configure.Capacity.PlayerLimit, 0, 0) : configure.Capacity));
if (mode == "duplicate") { await WorkerIpcCodec.WriteAsync(pipe, new WorkerHello(worker, incarnation, node, "already-consumed", "test")); await Task.Delay(10000); return 0; }
if (mode == "malformed") { await pipe.WriteAsync(new byte[] { 255, 255, 255, 127 }); await Task.Delay(10000); return 0; }
if (mode == "disconnect") { pipe.Close(); await Task.Delay(10000); return 0; }
if (mode == "duplicate-ready") await WorkerIpcCodec.WriteAsync(pipe, new WorkerReady(worker, incarnation, configure.Capacity));
uint nextWire = 1;
int exitCode = 0;
using var lifetime = new CancellationTokenSource();
using var sendGate = new SemaphoreSlim(1, 1);

async ValueTask SendAsync(WorkerMessage message, CancellationToken cancellationToken)
{
    await sendGate.WaitAsync(cancellationToken);
    try { await WorkerIpcCodec.WriteAsync(pipe, message, cancellationToken); }
    finally { sendGate.Release(); }
}

async Task RunHeartbeatsAsync(CancellationToken cancellationToken)
{
    if (mode is "silent" or "heartbeat-stall") return;
    bool stopAfterReady = mode == "heartbeat-stop-after-ready";
    while (true)
    {
        await Task.Delay(50, cancellationToken);
        long accepted = Interlocked.Read(ref acceptedMatches);
        WorkerDiagnostics? diagnostics = mode == "completed-history"
            ? new WorkerDiagnostics([new WorkerLaneHealth(0, 0, 0, 0, 0,
                0, 0, 0, 0)], 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                Matches: [],
                LifetimeMatchesAccepted: accepted,
                IdentityHistoryUsed: checked((int)accepted),
                IdentityHistoryCapacity: 4096)
            : null;
        await SendAsync(new WorkerHeartbeat(worker, incarnation, configure.Capacity,
            new WorkerHealth(WorkerStatus.Ready, 1, 0, 1, diagnostics)), cancellationToken);
        if (stopAfterReady) return;
    }
}

async Task RunCommandsAsync(CancellationToken cancellationToken)
{
    ValueTask ReplyAsync(WorkerMessage message) => SendAsync(message, cancellationToken);
    while (await WorkerIpcCodec.ReadAsync(pipe, cancellationToken) is { } command)
    {
        switch (command)
        {
        case UpdateNodeSigningKey signingKey:
            if (mode == "signing-key-noack") break;
            await ReplyAsync(new NodeSigningKeyUpdated(
                worker, incarnation, signingKey.KeyId));
            break;
        case Shutdown:
            if (mode == "ignore-shutdown") break;
            return;
        case Drain:
            await ReplyAsync(new WorkerDraining(worker, incarnation));
            if (mode == "drain-exit") return;
            break;
        case MatchAdminCommand admin when (mode is "controlled-completion" or "cancel-hang-after-end")
            && admin.Action == AdminAction.EndMatch:
            if (activeMatches.Remove(admin.MatchId, out var completed))
                await ReplyAsync(new MatchCompleted(new(completed.MatchId,
                    completed.LobbyId, MphRead.MatchEndReason.TimeLimit, [], null, null, Guid.NewGuid())));
            break;
        case InstallAdmissionKey admission:
            if (mode == "admission-key-noack") break;
            if (mode == "admission-key-crash") { exitCode = 44; return; }
            if (mode == "admission-key-delay") await Task.Delay(500, cancellationToken);
            if (mode == "admission-key-failure-stale")
            {
                await ReplyAsync(
                    new AdmissionKeyInstallFailed(admission.AdmissionId, new MatchId(Guid.NewGuid()), "stale"));
                break;
            }
            if (mode == "admission-key-failure")
            {
                await ReplyAsync(
                    new AdmissionKeyInstallFailed(admission.AdmissionId, admission.MatchId, "admission_route_capacity"));
                break;
            }
            await ReplyAsync(mode == "admission-key-stale"
                ? new AdmissionKeyInstalled(admission.AdmissionId, Guid.NewGuid(), admission.NodeSessionId,
                    admission.NodeId, admission.NodeIncarnation, admission.MatchId, admission.WireMatchId,
                    admission.WorkerId, admission.WorkerIncarnation, admission.SeatId, admission.JoinNonce, admission.ExpiresAt,
                    admission.HandoffGeneration)
                : new AdmissionKeyInstalled(admission.AdmissionId, admission.TicketId, admission.NodeSessionId,
                    admission.NodeId, admission.NodeIncarnation, admission.MatchId, admission.WireMatchId,
                    admission.WorkerId, admission.WorkerIncarnation, admission.SeatId, admission.JoinNonce, admission.ExpiresAt,
                    admission.HandoffGeneration));
            break;
        case CancelMatch cancel:
            await ReplyAsync(new MatchCancelAccepted(worker, incarnation,
                cancel.MatchId, cancel.OperationId));
            if (mode is "cancel-hang" or "cancel-hang-after-end") break;
            await ReplyAsync(new MatchInterrupted(cancel.MatchId, "cancelled")); break;
        case CreateMatch create:
            Interlocked.Increment(ref acceptedMatches);
            if (mode is "controlled-completion" or "cancel-hang-after-end")
                activeMatches.Add(create.Spec.MatchId, create.Spec);
            if (mode == "crash-match") { exitCode = 23; return; }
            if (mode == "create-hang") break;
            await ReplyAsync(new MatchReady(new(create.Spec.MatchId, new(nextWire++), worker, mode == "stale-match" ? Guid.NewGuid() : incarnation,
                mode == "wrong-host" ? "192.0.2.42" : "127.0.0.1", 12345)));
            await ReplyAsync(new MatchStarted(create.Spec.MatchId));
            if (mode == "crash-running") { exitCode = 24; return; }
            Guid reportId = Guid.NewGuid();
            var reportNotice = new MatchReportReady(create.Spec.MatchId, reportId, worker, incarnation, new string('A', 64), 100);
            if (mode == "guest-report")
            {
                string path = Path.Combine(Get("--artifact-dir"), "reports", create.Spec.MatchId.Value.ToString("N") + ".json");
                byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken);
                await ReplyAsync(new MatchCompleted(new(create.Spec.MatchId, create.Spec.LobbyId,
                    MphRead.MatchEndReason.TimeLimit, [], null, null, create.Spec.MatchId.Value)));
                await ReplyAsync(new MatchReportReady(create.Spec.MatchId, create.Spec.MatchId.Value,
                    worker, incarnation, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)), bytes.Length));
            }
            if (mode == "official-unavailable")
            {
                Guid officialReportId = create.Spec.MatchId.Value;
                await ReplyAsync(new MatchCompleted(new(create.Spec.MatchId,
                    create.Spec.LobbyId, MphRead.MatchEndReason.TimeLimit, [], null, null, officialReportId)));
                await ReplyAsync(new MatchReportUnavailable(
                    create.Spec.MatchId, officialReportId, worker, incarnation,
                    ArtifactFailureCode.WorkerLost));
            }
            if (mode == "premature-report") await ReplyAsync(reportNotice);
            if (mode == "interrupted-report")
            { await ReplyAsync(new MatchInterrupted(create.Spec.MatchId, "interrupted")); await ReplyAsync(reportNotice); }
            if (mode is "wrong-report-id" or "conflicting-report")
            {
                await ReplyAsync(new MatchCompleted(new(create.Spec.MatchId, create.Spec.LobbyId,
                    MphRead.MatchEndReason.TimeLimit, [], null, null, reportId)));
                if (mode == "conflicting-report") await ReplyAsync(reportNotice);
                await ReplyAsync(mode == "wrong-report-id" ? reportNotice with { ReportId = Guid.NewGuid() }
                    : reportNotice with { PayloadHash = new string('B', 64) });
            }
            if (mode is "completed" or "completed-history" or "duplicate-terminal") await ReplyAsync(new MatchCompleted(new(create.Spec.MatchId,
                create.Spec.LobbyId, MphRead.MatchEndReason.TimeLimit, [], null, null, Guid.NewGuid())));
            if (mode == "duplicate-terminal") await ReplyAsync(new MatchInterrupted(create.Spec.MatchId, "duplicate"));
            break;
        }
    }
}

Task commandLoop = RunCommandsAsync(lifetime.Token);
Task heartbeatLoop = RunHeartbeatsAsync(lifetime.Token);
try
{
    Task completed = await Task.WhenAny(commandLoop, heartbeatLoop);
    if (completed == heartbeatLoop && heartbeatLoop.IsFaulted) await heartbeatLoop;
    await commandLoop;
}
finally
{
    lifetime.Cancel();
    try { await Task.WhenAll(commandLoop, heartbeatLoop); }
    catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
}
return exitCode;
