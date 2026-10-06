using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using MphRead;
using MphRead.Mods.Network;
using MphRead.Mods.MapGen;

if (args.FirstOrDefault() == "reserve")
{
    using var reservation = new HostedPackageCache(args[1], 256, 64).Reserve(CancellationToken.None);
    File.WriteAllText(args[2], "ready");
    while (!File.Exists(args[3])) Thread.Sleep(20);
    return;
}
const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
void Assert(bool valid, string name) { if (!valid) throw new Exception(name); Console.WriteLine("PASS " + name); }
void Throws<T>(Action action, string name) where T : Exception
{ try { action(); } catch (T) { Console.WriteLine("PASS " + name); return; } throw new Exception(name); }
string root = Path.Combine(Path.GetTempPath(), "prime-map-career-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    string source = Path.Combine(root, "source"), final = Path.Combine(root, "final");
    File.WriteAllText(source, "prepared");
    using (var publication = new MapFilePublication())
    {
        publication.Stage(source, final, CancellationToken.None);
        File.WriteAllText(source, "modified");
        publication.Commit(CancellationToken.None);
        Assert(File.ReadAllText(final) == "prepared", "publication uses private immutable copy");
    }
    using (var publication = new MapFilePublication())
    {
        string staged = publication.Stage(source, final, CancellationToken.None);
        var stamp = File.GetLastWriteTimeUtc(staged);
        File.Move(staged, staged + ".old");
        File.WriteAllText(staged, "tampered"); File.SetLastWriteTimeUtc(staged, stamp);
        Throws<IOException>(() => publication.Commit(CancellationToken.None), "inode fence rejects replaced same-size same-time stage");
        Assert(File.ReadAllText(final) == "prepared", "failed identity fence preserves installed destination");
    }
    using (var publication = new MapFilePublication())
    {
        publication.Stage(source, final, CancellationToken.None);
        string forbidden = Path.Combine(root, "directory-target"); Directory.CreateDirectory(forbidden);
        publication.Stage(source, forbidden, CancellationToken.None);
        Throws<IOException>(() => publication.Commit(CancellationToken.None), "failed later rename rolls back preceding files");
        Assert(File.ReadAllText(final) == "prepared", "rollback restores old destination");
    }
    using (var publication = new MapFilePublication())
    {
        publication.Stage(source, final, CancellationToken.None);
        Throws<OperationCanceledException>(() => publication.Commit(new CancellationToken(true)), "canceled publication preserves old destination");
        Assert(File.ReadAllText(final) == "prepared", "cancellation does not publish");
    }
    File.WriteAllBytes(source, new byte[16 * 1024 * 1024]);
    using (var publication = new MapFilePublication())
    {
        publication.Stage(source, final, CancellationToken.None);
        long before = GC.GetAllocatedBytesForCurrentThread(); var clock = Stopwatch.StartNew();
        publication.Commit(CancellationToken.None); clock.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert(allocated < 128 * 1024, "16 MiB owner commit has bounded allocations");
        Console.WriteLine($"MEASURE commit_ms={clock.Elapsed.TotalMilliseconds:F3} allocated_bytes={allocated}");
    }
    string cachePath = Path.Combine(root, "cache"); var cache = new HostedPackageCache(cachePath, 256, 64);
    var workers = new List<Process>(); string release = Path.Combine(root, "release");
    try
    {
        for (int i = 0; i < 4; i++)
        {
            string ready = Path.Combine(root, "ready" + i);
            var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            if (Path.GetFileNameWithoutExtension(Environment.ProcessPath!).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
            info.ArgumentList.Add("reserve"); info.ArgumentList.Add(cachePath); info.ArgumentList.Add(ready); info.ArgumentList.Add(release);
            workers.Add(Process.Start(info)!);
            var deadline = Stopwatch.StartNew(); while (!File.Exists(ready) && deadline.ElapsedMilliseconds < 10000) { if (workers[^1].HasExited) break; Thread.Sleep(20); }
            Assert(File.Exists(ready), "cross-process reservation worker " + i);
        }
        Throws<IOException>(() => cache.Reserve(CancellationToken.None), "four inflight reservations consume exact cache budget");
        workers[0].Kill(); workers[0].WaitForExit();
        using (cache.Reserve(CancellationToken.None)) Assert(true, "restart reaps dead worker reservation");
    }
    finally { File.WriteAllText(release, "release"); foreach (var process in workers) { if (!process.WaitForExit(5000)) process.Kill(); process.Dispose(); } }
    string hashA = new('a', 64), hashB = new('b', 64);
    string a = Path.Combine(cachePath, hashA + ".ppmap"), b = Path.Combine(cachePath, hashB + ".ppmap");
    File.WriteAllBytes(a, new byte[128]); File.WriteAllBytes(b, new byte[128]);
    File.SetLastWriteTimeUtc(a, DateTime.UtcNow.AddHours(-2)); File.SetLastWriteTimeUtc(b, DateTime.UtcNow.AddHours(-1));
    using (cache.Pin(hashA, CancellationToken.None)) using (cache.Reserve(CancellationToken.None))
        Assert(File.Exists(a) && !File.Exists(b), "eviction skips active pinned archive");
    string orphan = Path.Combine(cachePath, "lobbies", "orphan"); Directory.CreateDirectory(orphan);
    File.WriteAllText(Path.Combine(orphan, ".cache-pins"), hashA); File.WriteAllText(Path.Combine(orphan, ".cache-owner"), "2147483647:1");
    File.SetLastWriteTimeUtc(Path.Combine(orphan, ".cache-owner"), DateTime.UtcNow.AddMinutes(-3));
    using (cache.Reserve(CancellationToken.None)) Assert(!Directory.Exists(orphan), "restart reclaims orphan child library after handoff grace");
    string abandoned = Path.Combine(cachePath, "stale.download"); File.WriteAllBytes(abandoned, new byte[64]);
    using (cache.Reserve(CancellationToken.None)) Assert(!File.Exists(abandoned), "idle restart removes abandoned temporary download");

    var assembly = typeof(DedicatedServer).Assembly;
    var stock = assembly.GetType("MphRead.Mods.Network.StockGameplayIdentity")!;
    var compute = stock.GetMethod("Compute", flags)!;
    string collision = Path.Combine(root, "collision"), entity = Path.Combine(root, "entity"), visual = Path.Combine(root, "visual");
    File.WriteAllText(collision, "collision"); File.WriteAllText(entity, "entities"); File.WriteAllText(visual, "texture1");
    string Identity() => compute.Invoke(null, new object[] { new[] { collision, entity }, "fixture gameplay rules v1" })!.ToString()!;
    string initial = Identity(); File.WriteAllText(visual, "texture2"); Assert(Identity() == initial, "visual-only variant keeps gameplay identity");
    File.WriteAllText(entity, "changed entities"); Assert(Identity() != initial, "entity content change invalidates cached stock identity");
    string entityChanged = Identity(); File.WriteAllText(collision, "changed collision"); Assert(Identity() != entityChanged, "collision content change invalidates cached stock identity");
    File.Delete(entity); try { Identity(); throw new Exception("missing stock file accepted"); } catch (TargetInvocationException ex) when (ex.InnerException is InvalidDataException) { Console.WriteLine("PASS missing stock content fails closed"); }

    var serverType = typeof(DedicatedServer); var matchType = serverType.GetNestedType("CareerMatchState", BindingFlags.NonPublic)!;
    var peerType = serverType.GetNestedType("Peer", BindingFlags.NonPublic)!;
    void Set(object value, string field, object input) => value.GetType().GetField(field, flags)!.SetValue(value, input);
    object Get(object value, string field) => value.GetType().GetField(field, flags)!.GetValue(value)!;
    void Invoke(object server, string name, params object[] inputs) => serverType.GetMethod(name, flags)!.Invoke(server, inputs);
    object NewServer(out object match) { object server = RuntimeHelpers.GetUninitializedObject(serverType); match = Activator.CreateInstance(matchType, true)!; Set(server, "_careerMatch", match); return server; }
    string Ticket(int subject, int refresh = 0) => "pp1." + Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { sub = $"00000000-0000-4000-8000-{subject:D12}", refresh }))).TrimEnd('=').Replace('+','-').Replace('/','_') + ".fixture";
    object Peer(uint id, string ticket, bool spectator = false) { var peer = Activator.CreateInstance(peerType, true)!; Set(peer,"ClientId",id); Set(peer,"SlotIndex",0); Set(peer,"Name","Fixture"); Set(peer,"CareerTicket",ticket); Set(peer,"Spectating",spectator); return peer; }
    object churnServer = NewServer(out var churn);
    for (uint i = 1; i <= 129; i++) { var peer = Peer(i, Ticket((int)i)); Invoke(churnServer,"CareerActivate",peer,i==1); Invoke(churnServer,"CareerPeerLeaving",peer); }
    Assert(((IList)Get(churn,"Participants")).Count == 128 && (bool)Get(churn,"SegmentLimitReached"), "129 cumulative entrants retain bounded 128 segments without discarding report");
    object server = NewServer(out var match); Invoke(server,"CareerActivate",Peer(1,Ticket(1),true),true);
    Assert(((IList)Get(match,"Participants")).Count == 0, "initial spectator authors no career segment");
    var firstPeer = Peer(42,Ticket(1)); Invoke(server,"CareerActivate",firstPeer,true);
    GameState.Kills[0] = 5;
    var longest = (int[])assembly.GetType("MphRead.Mods.Network.CareerMatchStats")!.GetField("LongestKillStreak", flags)!.GetValue(null)!;
    longest[0] = 4;
    Set(firstPeer,"CareerTicket",Ticket(1,1)); Invoke(server,"CareerTicketChanged",firstPeer);
    Assert(((IList)Get(match,"Participants")).Count == 1, "same-account refresh keeps original counter segment");
    Set(firstPeer,"CareerTicket",Ticket(2)); Invoke(server,"CareerTicketChanged",firstPeer);
    var segments = (IList)Get(match,"Participants"); var firstMetrics = Get(segments[0]!,"Metrics");
    Assert(segments.Count == 2 && (long)firstMetrics.GetType().GetProperty("Kills")!.GetValue(firstMetrics)! == 5, "account change freezes previous five kills");
    GameState.Kills[0] = 8; Invoke(server,"CareerPeerLeaving",firstPeer);
    var secondMetrics = Get(segments[1]!,"Metrics"); Assert((long)secondMetrics.GetType().GetProperty("Kills")!.GetValue(secondMetrics)! == 3, "new account receives only subsequent three kills");
    Assert((long)secondMetrics.GetType().GetProperty("LongestKillStreak")!.GetValue(secondMetrics)! == 0, "new account does not inherit old account kill streak");
    var thirdPeer = Peer(42,Ticket(3)); Invoke(server,"CareerActivate",thirdPeer,false);
    Assert(segments.Count == 3 && (Guid)Get(segments[0]!,"ParticipantId") != (Guid)Get(segments[2]!,"ParticipantId"), "reused client ID receives independent admission UUID");
    GameState.Kills[0] = 11; Set(thirdPeer,"ClientId",(uint)99); Invoke(server,"CareerTicketChanged",thirdPeer);
    Assert(segments.Count == 4 && (uint)Get(segments[2]!,"ClientId") == 42 && (uint)Get(segments[3]!,"ClientId") == 99, "changed transport client binding closes frozen signed segment");
}
finally { Directory.Delete(root, true); }
