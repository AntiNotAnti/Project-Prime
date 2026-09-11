using System.IO.Pipes;
using ProjectPrime.Server.Shared;

string Get(string key) => args[Array.IndexOf(args, key) + 1];
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
    node, mode == "token" ? new string('0', 64) : secret, "test",
    new("1", mode == "profile" ? "wrong" : "hash", "test", 8)));
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
Task<WorkerMessage?> pending = WorkerIpcCodec.ReadAsync(pipe).AsTask();
while (true)
{
    if (await Task.WhenAny(pending, Task.Delay(50)) != pending)
    {
        if (mode != "silent") await WorkerIpcCodec.WriteAsync(pipe, new WorkerHeartbeat(worker, incarnation, configure.Capacity,
            new WorkerHealth(WorkerStatus.Ready, 1, 0, 1)));
        continue;
    }
    WorkerMessage? command = await pending;
    switch (command)
    {
        case Shutdown:
            if (mode == "ignore-shutdown") break;
            return 0;
        case Drain:
            await WorkerIpcCodec.WriteAsync(pipe, new WorkerDraining(worker, incarnation));
            if (mode == "drain-exit") return 0;
            break;
        case MatchAdminCommand admin when mode == "controlled-completion" && admin.Action == AdminAction.EndMatch:
            if (activeMatches.Remove(admin.MatchId, out var completed))
                await WorkerIpcCodec.WriteAsync(pipe, new MatchCompleted(new(completed.MatchId,
                    completed.LobbyId, MphRead.MatchEndReason.TimeLimit, [], null, null, Guid.NewGuid())));
            break;
        case InstallAdmissionKey admission:
            if (mode == "admission-key-noack") break;
            if (mode == "admission-key-crash") return 44;
            if (mode == "admission-key-delay") await Task.Delay(500);
            if (mode == "admission-key-failure-stale")
            {
                await WorkerIpcCodec.WriteAsync(pipe,
                    new AdmissionKeyInstallFailed(admission.AdmissionId, new MatchId(Guid.NewGuid()), "stale"));
                break;
            }
            await WorkerIpcCodec.WriteAsync(pipe, mode == "admission-key-stale"
                ? new AdmissionKeyInstalled(admission.AdmissionId, Guid.NewGuid(), admission.NodeSessionId,
                    admission.NodeId, admission.NodeIncarnation, admission.MatchId, admission.WireMatchId,
                    admission.WorkerId, admission.WorkerIncarnation, admission.SeatId, admission.JoinNonce, admission.ExpiresAt)
                : new AdmissionKeyInstalled(admission.AdmissionId, admission.TicketId, admission.NodeSessionId,
                    admission.NodeId, admission.NodeIncarnation, admission.MatchId, admission.WireMatchId,
                    admission.WorkerId, admission.WorkerIncarnation, admission.SeatId, admission.JoinNonce, admission.ExpiresAt));
            break;
        case CancelMatch cancel:
            await WorkerIpcCodec.WriteAsync(pipe, new MatchInterrupted(cancel.MatchId, "cancelled")); break;
        case CreateMatch create:
            if (mode == "controlled-completion") activeMatches.Add(create.Spec.MatchId, create.Spec);
            if (mode == "crash-match") return 23;
            if (mode == "create-hang") break;
            await WorkerIpcCodec.WriteAsync(pipe, new MatchReady(new(create.Spec.MatchId, new(nextWire++), worker, mode == "stale-match" ? Guid.NewGuid() : incarnation,
                mode == "wrong-host" ? "192.0.2.42" : "127.0.0.1", 12345)));
            await WorkerIpcCodec.WriteAsync(pipe, new MatchStarted(create.Spec.MatchId));
            if (mode == "crash-running") return 24;
            Guid reportId = Guid.NewGuid();
            var reportNotice = new MatchReportReady(create.Spec.MatchId, reportId, worker, incarnation, new string('A', 64), 100);
            if (mode == "guest-report")
            {
                string path = Path.Combine(Get("--artifact-dir"), "reports", create.Spec.MatchId.Value.ToString("N") + ".json");
                byte[] bytes = await File.ReadAllBytesAsync(path);
                await WorkerIpcCodec.WriteAsync(pipe, new MatchCompleted(new(create.Spec.MatchId, create.Spec.LobbyId,
                    MphRead.MatchEndReason.TimeLimit, [], null, null, create.Spec.MatchId.Value)));
                await WorkerIpcCodec.WriteAsync(pipe, new MatchReportReady(create.Spec.MatchId, create.Spec.MatchId.Value,
                    worker, incarnation, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)), bytes.Length));
            }
            if (mode == "premature-report") await WorkerIpcCodec.WriteAsync(pipe, reportNotice);
            if (mode == "interrupted-report")
            { await WorkerIpcCodec.WriteAsync(pipe, new MatchInterrupted(create.Spec.MatchId, "interrupted")); await WorkerIpcCodec.WriteAsync(pipe, reportNotice); }
            if (mode is "wrong-report-id" or "conflicting-report")
            {
                await WorkerIpcCodec.WriteAsync(pipe, new MatchCompleted(new(create.Spec.MatchId, create.Spec.LobbyId,
                    MphRead.MatchEndReason.TimeLimit, [], null, null, reportId)));
                if (mode == "conflicting-report") await WorkerIpcCodec.WriteAsync(pipe, reportNotice);
                await WorkerIpcCodec.WriteAsync(pipe, mode == "wrong-report-id" ? reportNotice with { ReportId = Guid.NewGuid() }
                    : reportNotice with { PayloadHash = new string('B', 64) });
            }
            if (mode is "completed" or "duplicate-terminal") await WorkerIpcCodec.WriteAsync(pipe, new MatchCompleted(new(create.Spec.MatchId,
                create.Spec.LobbyId, MphRead.MatchEndReason.TimeLimit, [], null, null, Guid.NewGuid())));
            if (mode == "duplicate-terminal") await WorkerIpcCodec.WriteAsync(pipe, new MatchInterrupted(create.Spec.MatchId, "duplicate"));
            break;
        case null: return 0;
    }
    pending = WorkerIpcCodec.ReadAsync(pipe).AsTask();
}
