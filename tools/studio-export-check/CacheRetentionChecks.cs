using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead.Mods.MapGen;
using MphRead.Mods.StudioReplay;
using ProjectPrime.Studio.Jobs;
using ProjectPrime.Studio.Replay;

internal static class CacheRetentionChecks
{
    internal static async Task Run(string fixtureRoot, Action<bool, string> check)
    {
        string root = Path.Combine(fixtureRoot, "retention"); Directory.CreateDirectory(root);
        string Entry(string cache, char key, int bytes, DateTime last)
        {
            string directory = Path.Combine(cache, new string(key, 64)); Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "bytes"), new byte[bytes]);
            File.WriteAllText(Path.Combine(directory, "cache.json"), "{}");
            File.SetLastWriteTimeUtc(Path.Combine(directory, "cache.json"), last); Directory.SetLastWriteTimeUtc(directory, last);
            return directory;
        }
        string age = Path.Combine(root, "age"), expired = Entry(age, 'a', 100, DateTime.UtcNow.AddDays(-3)), recent = Entry(age, 'b', 100, DateTime.UtcNow);
        MapDiskCache.Prune(age, long.MaxValue, TimeSpan.FromDays(2));
        check(!Directory.Exists(expired) && Directory.Exists(recent), "idle cache age eviction removes old immutable entries and preserves recent bytes");
        string budget = Path.Combine(root, "budget"), oldest = Entry(budget, 'a', 100, DateTime.UtcNow.AddDays(-2)), middle = Entry(budget, 'b', 100, DateTime.UtcNow.AddDays(-1)), newest = Entry(budget, 'c', 100, DateTime.UtcNow);
        MapDiskCache.Prune(budget, 204, TimeSpan.FromDays(10));
        check(!Directory.Exists(oldest) && Directory.Exists(middle) && Directory.Exists(newest), "byte-budget pruning reclaims oldest idle entries without deleting newer entries after the budget is met");
        string shared = Path.Combine(root, "shared"), live = Entry(shared, 'a', 100, DateTime.UtcNow.AddDays(-3)), hash = Path.GetFileName(live);
        IDisposable first = MapDiskCache.Pin(shared, hash), second = MapDiskCache.Pin(shared, hash);
        try
        {
            MapDiskCache.Prune(shared, 0, TimeSpan.Zero);
            check(Directory.Exists(live), "two independent shared cache pins coexist and prevent exclusive pruning");
            first.Dispose(); MapDiskCache.Prune(shared, 0, TimeSpan.Zero);
            check(Directory.Exists(live), "closing one shared pin leaves the remaining owner protected");
        }
        finally { first.Dispose(); second.Dispose(); }
        MapDiskCache.Prune(shared, long.MaxValue, TimeSpan.FromDays(1));
        check(Directory.Exists(live), "last-use retention begins when the final active pin closes");
        MapDiskCache.Prune(shared, 0, TimeSpan.Zero);
        check(!Directory.Exists(live), "an unpinned entry becomes eligible for byte-budget eviction");

        string crashed = Path.Combine(root, "crash"), held = Entry(crashed, 'd', 100, DateTime.UtcNow.AddDays(-3)), ready = Path.Combine(root, "pin-ready");
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, RedirectStandardError = true };
        if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        foreach (string argument in new[] { "--pin-child", crashed, Path.GetFileName(held), ready }) start.ArgumentList.Add(argument);
        using (var child = Process.Start(start)!)
        {
            try
            {
                var deadline = Stopwatch.StartNew();
                while (!File.Exists(ready) && !child.HasExited && deadline.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(10);
                check(File.Exists(ready), "separate process acquires the production shared immutable-cache pin");
                MapDiskCache.Prune(crashed, 0, TimeSpan.Zero);
                check(Directory.Exists(held), "a different process cannot prune immutable bytes held by the shared OS pin");
                child.Kill(entireProcessTree: true); await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                MapDiskCache.Prune(crashed, 0, TimeSpan.Zero);
                check(!Directory.Exists(held), "process termination releases the OS cache pin without a managed disposal callback");
            }
            finally { if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); } }
        }

        string cache = Path.Combine(root, "replay"), sources = Path.Combine(cache, "sources"), packages = Path.Combine(cache, "packages");
        string external = Path.Combine(root, "recording.ppdemo"); File.WriteAllBytes(external, [1, 4, 9, 16]);
        string source = StudioReplaySnapshotCache.Capture(external, sources, default);
        File.WriteAllBytes(external, [2, 5, 10, 17]); string unused = StudioReplaySnapshotCache.Capture(external, sources, default);
        byte[] mapBytes = [23, 42, 76]; string packageHash = Convert.ToHexString(SHA256.HashData(mapBytes)).ToLowerInvariant();
        string packageDirectory = Path.Combine(packages, packageHash); Directory.CreateDirectory(packageDirectory); File.WriteAllBytes(Path.Combine(packageDirectory, "source.ppmap"), mapBytes);
        string Ticket()
        {
            Guid id = Guid.NewGuid(); string directory = Path.Combine(cache, "exports", id.ToString("N")); Directory.CreateDirectory(directory);
            var ticket = new StudioReplayExportTicket(id, source, Path.Combine(directory, "cache"), new(Path.Combine(root, "outputs", id.ToString("N")), 0, 1),
                Path.Combine(directory, "status.json"), Path.Combine(directory, "cancel"), new Dictionary<string, string>(), [], [packages], "AMHE1", "AMFE0", packageHash);
            string path = Path.Combine(directory, "ticket.json"); File.WriteAllText(path, JsonSerializer.Serialize(ticket, Json)); return path;
        }
        string persisted = Ticket(); var persistedTicket = Read(persisted); Status(persistedTicket, "Queued");
        Func<string, bool> protect = StudioReplayCachePins.ProtectedDirectories(cache);
        MapDiskCache.Prune(sources, 0, TimeSpan.Zero, protectedDirectory: protect);
        MapDiskCache.Prune(packages, 0, TimeSpan.Zero, protectedDirectory: protect);
        check(File.Exists(source) && !File.Exists(unused) && File.Exists(Path.Combine(packageDirectory, "source.ppmap")), "persisted nonterminal export protects only its exact source and package while unrelated idle entries are pruned");
        check(protect(Path.Combine(packageDirectory, "runtime", new string('a', 64))), "persisted exact package reference also protects its private generated runtime descendants");
        Status(persistedTicket, "Complete");
        string malformedRoot = Path.Combine(root, "malformed"); Directory.CreateDirectory(malformedRoot);
        int malformedIndex = 0;
        foreach (var invalid in new[] { persistedTicket with { Request = null! }, persistedTicket with { PackageDirectories = null! }, persistedTicket with { ReplayPath = null! } })
        {
            bool rejected = false;
            try { using var ignored = StudioReplayCachePins.PinReferences(invalid); } catch (InvalidDataException) { rejected = true; }
            check(rejected, "deep-null export descriptors are rejected before resource pinning");
            string directory = Path.Combine(malformedRoot, (++malformedIndex).ToString()); Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "ticket.json"), JsonSerializer.Serialize(invalid, Json));
        }
        await using (var badJobs = new StudioJobManager())
        {
            var badCoordinator = new ReplayExportWorkerCoordinator(badJobs, _ => throw new InvalidOperationException("Malformed job must not launch."));
            badCoordinator.RestorePersisted(malformedRoot);
            check(badJobs.Jobs.Count == 0, "restart skips malformed nested export descriptors without crashing or launching a worker");
        }
        string corrupt = Path.Combine(cache, "exports", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(corrupt);
        File.WriteAllText(Path.Combine(corrupt, "ticket.json"), JsonSerializer.Serialize(persistedTicket with { PackageDirectories = null! }, Json));
        check(protect(Path.Combine(sources, new string('f', 64))), "a malformed persisted reference fails pruning closed instead of throwing a null-reference exception");
        Directory.Delete(corrupt, recursive: true);

        var completions = new Dictionary<Guid, TaskCompletionSource>();
        var childPins = new List<IDisposable>(); var childSources = new List<string>();
        var gate = new object();
        async Task CaptureWorker(string path)
        {
            var ticket = Read(path);
            string own = StudioReplaySnapshotCache.Capture(ticket.ReplayPath, Path.Combine(ticket.CacheRoot, "sources"), default);
            IDisposable pin = StudioReplayCachePins.PinFile(own);
            var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (gate) { childPins.Add(pin); childSources.Add(own); completions.Add(ticket.Id, finish); }
            Status(ticket, "Rendering", captured: true);
            await finish.Task; Status(ticket, "Complete", captured: true);
        }
        await using (var jobs = new StudioJobManager())
        {
            var coordinator = new ReplayExportWorkerCoordinator(jobs, CaptureWorker);
            string[] tickets = [Ticket(), Ticket(), Ticket()]; Task[] pending = tickets.Select(coordinator.LaunchAsync).ToArray();
            try
            {
                var deadline = Stopwatch.StartNew();
                while (true)
                {
                    int count; lock (gate) count = completions.Count;
                    if (count == 2 || deadline.Elapsed > TimeSpan.FromSeconds(5)) break;
                    await Task.Delay(10);
                }
                await Task.Delay(150);
                MapDiskCache.Prune(sources, 0, TimeSpan.Zero); MapDiskCache.Prune(packages, 0, TimeSpan.Zero);
                check(File.Exists(source) && File.Exists(Path.Combine(packageDirectory, "source.ppmap")) && jobs.Jobs.Count(j => j.Progress.Detail?.StartsWith("Queued") == true) == 1,
                    "queued third export retains origin OS pins after both running workers acknowledge private capture");
                lock (gate) foreach (var finish in completions.Values) finish.TrySetResult();
                deadline.Restart();
                while (true)
                {
                    int count; lock (gate) count = completions.Count;
                    if (count == 3 || deadline.Elapsed > TimeSpan.FromSeconds(5)) break;
                    await Task.Delay(10);
                }
                await Task.Delay(150);
                MapDiskCache.Prune(sources, 0, TimeSpan.Zero, protectedDirectory: protect);
                MapDiskCache.Prune(packages, 0, TimeSpan.Zero, protectedDirectory: protect);
                check(!File.Exists(source) && !File.Exists(Path.Combine(packageDirectory, "source.ppmap")), "origin references become evictable only after every queued worker has acknowledged its independent immutable capture");
                lock (gate)
                {
                    foreach (string own in childSources)
                    {
                        MapDiskCache.Prune(Path.GetDirectoryName(Path.GetDirectoryName(own)!)!, 0, TimeSpan.Zero);
                        check(File.Exists(own), "each worker's private snapshot remains pinned after origin eviction");
                    }
                    foreach (var finish in completions.Values) finish.TrySetResult();
                }
                await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally
            {
                coordinator.DetachForShutdown();
                lock (gate) { foreach (var finish in completions.Values) finish.TrySetResult(); foreach (var pin in childPins) pin.Dispose(); }
                try { await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
            }
        }

        File.WriteAllBytes(external, [6, 7]); source = StudioReplaySnapshotCache.Capture(external, sources, default);
        Directory.CreateDirectory(packageDirectory); File.WriteAllBytes(Path.Combine(packageDirectory, "source.ppmap"), mapBytes);
        string waiting = Ticket();
        using (var owner = new FileStream(Path.Combine(sources, ".owners.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        await using (var waitingJobs = new StudioJobManager())
        {
            bool launched = false;
            var waitingCoordinator = new ReplayExportWorkerCoordinator(waitingJobs, _ => { launched = true; return Task.CompletedTask; });
            var response = Stopwatch.StartNew(); Task observation = waitingCoordinator.LaunchAsync(waiting);
            check(response.Elapsed < TimeSpan.FromSeconds(.5), "export submission stays responsive while a different owner holds the pruning gate");
            await Task.Delay(100); waitingJobs.Jobs.Single().Cancel();
            try { await observation.WaitAsync(TimeSpan.FromSeconds(2)); } catch (OperationCanceledException) { }
            check(!launched && waitingJobs.Jobs.Single().State == StudioJobState.Cancelled,
                "queued cancellation interrupts cache-pin acquisition before worker launch without waiting for the pruning gate");
        }
        string cancelledBeforeLaunch = Ticket(); var cancelledTicket = Read(cancelledBeforeLaunch);
        File.WriteAllText(cancelledTicket.CancelFile, "cancel");
        await using (var cancelledJobs = new StudioJobManager())
        {
            bool launched = false;
            var cancelledCoordinator = new ReplayExportWorkerCoordinator(cancelledJobs, _ => { launched = true; return Task.CompletedTask; });
            try { await cancelledCoordinator.LaunchAsync(cancelledBeforeLaunch).WaitAsync(TimeSpan.FromSeconds(2)); } catch (OperationCanceledException) { }
            var state = JsonSerializer.Deserialize<StudioReplayExportStatus>(File.ReadAllText(cancelledTicket.StatusFile))!;
            check(!launched && state.State == "Cancelled" && state.Error == null && cancelledJobs.Jobs.Single().State == StudioJobState.Cancelled,
                "persisted facade cancellation before process launch remains Cancelled rather than being overwritten as Failed");
        }
    }
    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true };
    private static StudioReplayExportTicket Read(string path) => JsonSerializer.Deserialize<StudioReplayExportTicket>(File.ReadAllText(path), Json)!;
    private static void Status(StudioReplayExportTicket ticket, string state, bool captured = false)
    {
        string stage = ticket.StatusFile + ".fixture";
        File.WriteAllText(stage, JsonSerializer.Serialize(new StudioReplayExportStatus(ticket.Id, state, 0, 1, null, ticket.Request.Directory, OriginCaptured: captured)));
        File.Move(stage, ticket.StatusFile, overwrite: true);
    }
}
