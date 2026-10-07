using System.Diagnostics;
using System.Text.Json;
using MphRead.Mods.StudioReplay;
using ProjectPrime.Studio.Jobs;
using ProjectPrime.Studio.Replay;
using ReplayExportWorkerIdentity = MphRead.Platform.ProcessLifetimeIdentity;
using ReplayExportWorkerPresence = MphRead.Platform.ProcessLifetimePresence;

internal static class WorkerIdentityChecks
{
    public static async Task Run(string root, Action<bool, string> check)
    {
        const string boot = "01234567-89ab-cdef-0123-456789abcdef";
        string Stat(string state = "R", string start = "12345", int pid = 123)
            => $"{pid} (worker ) with spaces) {state} " + string.Join(' ', Enumerable.Repeat("0", 18)) + " " + start + " 0 0\n";
        check(ReplayExportWorkerIdentity.TryLinuxToken(123, boot, Stat(), out string parsed, out bool exited)
            && parsed == "linux:1:" + boot + ":123:12345" && !exited,
            "Linux identity uses the exact kernel boot UUID and field22 even when comm contains spaces and parentheses");
        foreach (var bad in new[] { Stat(start: "-1"), Stat(start: "18446744073709551616"), Stat(start: "0"), Stat(pid: 124), "123 (worker) R 0" })
            check(!ReplayExportWorkerIdentity.TryLinuxToken(123, boot, bad, out _, out _), "malformed kernel identity cannot establish worker ownership");
        check(!ReplayExportWorkerIdentity.TryLinuxToken(123, "not-a-boot-id", Stat(), out _, out _), "invalid boot IDs cannot establish worker ownership");
        check(ReplayExportWorkerIdentity.TryLinuxToken(123, boot, Stat("Z"), out _, out bool zombie) && zombie,
            "a zombie kernel record is identified as exited rather than a live export writer");
        using var self = Process.GetCurrentProcess();
        string token = ReplayExportWorkerIdentity.Capture(self);
        using var observer = Process.GetProcessById(self.Id);
        check(ReplayExportWorkerIdentity.Matches(observer, token), "the exact process identity survives a separate Process observer");
        string[] fields = token.Split(':');
        fields[^1] = (ulong.Parse(fields[^1]) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        string reused = string.Join(':', fields);
        check(!ReplayExportWorkerIdentity.Matches(observer, reused)
            && ReplayExportWorkerIdentity.Assess(observer, reused) == ReplayExportWorkerPresence.Exited,
            "a reused PID with different start ticks cannot match or retain the recorded worker incarnation");
        check(!ReplayExportWorkerIdentity.Matches(observer, "malformed")
            && ReplayExportWorkerIdentity.Assess(observer, "malformed") == ReplayExportWorkerPresence.UnverifiedAlive,
            "malformed tokens fail ownership checks while conservatively retaining an existing process slot");
        check(ReplayExportWorkerIdentity.Matches(observer, token, 1),
            "stable token matching does not depend on reconstructed UTC timestamps");
        check(ReplayExportWorkerIdentity.AssessUsingIdentityReader(observer, token, _ => throw new FileNotFoundException("boot_id unavailable"))
            == ReplayExportWorkerPresence.UnverifiedAlive, "a missing identity file does not release a still-live process slot");
        check(ReplayExportWorkerIdentity.AssessUsingIdentityReader(observer, token, _ => throw new UnauthorizedAccessException("procfs unavailable"))
            == ReplayExportWorkerPresence.UnverifiedAlive, "an inaccessible kernel identity fails closed while its process still exists");
        if (OperatingSystem.IsLinux())
        {
            fields = token.Split(':'); fields[2] = Guid.NewGuid().ToString("D");
            check(!ReplayExportWorkerIdentity.Matches(observer, string.Join(':', fields)), "a different kernel boot ID rejects a PID reused after reboot");
            check(!ReplayExportWorkerIdentity.Matches(observer, null, self.StartTime.ToUniversalTime().Ticks)
                && ReplayExportWorkerIdentity.Assess(observer, null, self.StartTime.ToUniversalTime().Ticks) == ReplayExportWorkerPresence.UnverifiedAlive,
                "legacy Linux UTC timestamps never establish cross-process identity");
        }
        string directory = Path.Combine(root, "unknown-worker-identity"); Directory.CreateDirectory(directory);
        string[] tickets = new string[3];
        for (int i = 0; i < tickets.Length; i++)
        {
            string jobRoot = Path.Combine(directory, i.ToString()); Directory.CreateDirectory(jobRoot);
            var ticket = new StudioReplayExportTicket(Guid.NewGuid(), "fixture.ppdemo", jobRoot, new(jobRoot, 0, 1),
                Path.Combine(jobRoot, "status.json"), Path.Combine(jobRoot, "cancel"), new Dictionary<string, string>(), [], [], "AMHE1", "AMFE0");
            tickets[i] = Path.Combine(jobRoot, "ticket.json"); File.WriteAllText(tickets[i], JsonSerializer.Serialize(ticket));
            var status = i < 2 ? new StudioReplayExportStatus(ticket.Id, "Complete", 1, 1, null, jobRoot,
                self.Id, null, WorkerIdentity: i == 0 ? null : "malformed") : new StudioReplayExportStatus(ticket.Id, "Queued", 0, 1, null, jobRoot);
            File.WriteAllText(ticket.StatusFile, JsonSerializer.Serialize(status));
        }
        await using var jobs = new StudioJobManager();
        int launched = 0;
        var coordinator = new ReplayExportWorkerCoordinator(jobs, _ => { Interlocked.Increment(ref launched); throw new InvalidOperationException("Unverifiable live processes must reserve their slots."); });
        coordinator.RestorePersisted(directory);
        try
        {
            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < TimeSpan.FromSeconds(5) && (jobs.Jobs.Count != 3
                || jobs.Jobs.Count(job => job.Progress.Detail?.StartsWith("Worker identity unavailable") == true) != 2
                || jobs.Jobs.Count(job => job.Progress.Detail?.StartsWith("Queued") == true) != 1)) await Task.Delay(20);
            check(jobs.Jobs.Count == 3 && jobs.Jobs.Count(job => job.Progress.Detail?.StartsWith("Worker identity unavailable") == true) == 2
                && jobs.Jobs.Count(job => job.Progress.Detail?.StartsWith("Queued") == true) == 1 && Volatile.Read(ref launched) == 0,
                "missing and malformed identities on existing processes reserve both slots and defer a queued export without claiming ownership");
        }
        finally
        {
            coordinator.DetachForShutdown();
            await Task.WhenAll(jobs.Jobs.Select(job => job.Task!)).WaitAsync(TimeSpan.FromSeconds(5));
        }
        check(tickets.All(path => !File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "cancel"))) && Volatile.Read(ref launched) == 0,
            "detaching an uncertain retained-process monitor does not signal either process or start deferred work");
    }
}
