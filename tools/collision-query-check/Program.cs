using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Collision;
using MphRead.Mods.Input;
using MphRead.Mods.MapGen;
using MphRead.Utility;
using OpenTK.Mathematics;

// Optional asset-backed replay gate: reuse the production detached-world fixture.
if (args.Length >= 2 && args[0] == "--replay-integration")
{
    Directory.SetCurrentDirectory(Path.GetFullPath(args[1]));
    Paths.UpdatePaths(); Paths.ChooseMphPath();
    string fixture = Path.Combine(Path.GetTempPath(), "prime-collision-replay-" + Guid.NewGuid().ToString("N") + ".ppdemo");
    var assembly = typeof(Scene).Assembly;
    try
    {
        assembly.GetType("MphRead.Mods.Network.ReplayWorldCoverageCheck")!
            .GetMethod("Write", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { fixture, "MP3 PROVING GROUND", GameMode.Battle, new Vector3(0, 1, 0) });
        int result = (int)assembly.GetType("MphRead.Mods.Network.ReplayReplicaCheck")!
            .GetMethod("Run", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object?[] { fixture, null })!;
        if (result != 0) throw new Exception("Detached-world replay collision integration failed.");
        return;
    }
    finally { File.Delete(fixture); }
}

// This fixture runs against the original shared-scratch implementation too.
// --baseline records the observed lifetime hazard without requiring it to pass.
bool baseline = args.Contains("--baseline");
CollisionDetection.Init();
if (args.Contains("--large-allocation-only"))
{
    bool requireZero = !baseline && !args.Contains("--characterize");
    MeasureLargeQuery(requireZero);
    MeasureLargeQuery(requireZero, candidateCount: 8192);
    if (!baseline && !args.Contains("--characterize")) CheckOversizedQuery();
    return;
}
var a = MakeScene("A", Vector3.Zero);
var b = MakeScene("B", new Vector3(20, 3, -8));
if (!args.Contains("--benchmark-only"))
{
var oldQueueField = typeof(CollisionDetection).GetField("_inactiveItems", BindingFlags.Static | BindingFlags.NonPublic);
if (oldQueueField != null)
{
    var oldQueue = (System.Collections.ICollection)oldQueueField.GetValue(null)!;
    int beforeInit = oldQueue.Count;
    for (int i = 0; i < 3; i++) CollisionDetection.Init();
    Console.WriteLine($"REPEATED_INIT before={beforeInit} after={oldQueue.Count} added={oldQueue.Count - beforeInit}");
}
else
{
    long beforeInit = GC.GetAllocatedBytesForCurrentThread();
    for (int i = 0; i < 3; i++) CollisionDetection.Init();
    Console.WriteLine($"REPEATED_INIT bytes={GC.GetAllocatedBytesForCurrentThread() - beforeInit}");
}
var rows = new List<string>();
foreach (var scene in new[] { a, b })
{
    Vector3 translation = scene.Room!.RoomCollision[0].Translation;
    using var owned = Own(CollisionDetection.GetCandidatesForLimits(null, Vector3.Zero, 0,
        translation + new Vector3(-1, -1, -1), translation + new Vector3(13, 2, 5), true, scene));
    rows.Add("limits:" + CandidateSignature(owned.Items));
    using (var withEntities = Own(CollisionDetection.GetCandidatesForLimits(translation, translation, 0,
        translation + new Vector3(-1, -1, -1), translation + new Vector3(13, 2, 5), true, scene)))
        rows.Add("limits-entities:" + CandidateSignature(withEntities.Items));
    foreach (var flags in new[] { TestFlags.None, TestFlags.Players, TestFlags.Beams, TestFlags.Scan })
    {
        CollisionResult tie = default;
        bool found = CollisionDetection.CheckBetweenPoints(translation + new Vector3(6, 1, 2),
            translation + new Vector3(6, -1, 2), flags, scene, ref tie);
        rows.Add("flags-tie:" + flags + ":" + found + ":" + ResultSignature(tie));
    }
    for (int x = -1; x <= 13; x++)
    foreach (float z in new[] { -0.1f, 0.1f, 2f, 4.1f })
    {
        Vector3 p = translation + new Vector3(x + .125f, 0, z);
        CollisionResult hit = default;
        bool found = CollisionDetection.CheckBetweenPoints(p + Vector3.UnitY, p - Vector3.UnitY,
            TestFlags.Players, scene, ref hit);
        rows.Add("line:" + found + ":" + ResultSignature(hit));
        var results = new CollisionResult[16];
        int count = CollisionDetection.CheckSphereBetweenPoints(p + Vector3.UnitY, p - Vector3.UnitY,
            .5f, results.Length, true, TestFlags.Players, scene, results);
        rows.Add("sphere:" + count + ":" + string.Join(";", results.Take(count).Select(ResultSignature)));
        count = CollisionDetection.CheckSpherePenetration(p.AddY(-.1f), .5f, results.Length,
            TestFlags.Players, scene, results);
        rows.Add("penetration:" + count + ":" + string.Join(";", results.Take(count).Select(ResultSignature)));
        count = CollisionDetection.CheckInRadius(p.AddY(.2f), .5f, results.Length, false,
            TestFlags.Players, scene, results);
        rows.Add("radius:" + count + ":" + string.Join(";", results.Take(count).Select(ResultSignature)));
        using var robust = Own(CollisionDetection.GetCandidatesForLimits(p + Vector3.UnitY, p - Vector3.UnitY,
            .5f, null, Vector3.Zero, true, scene));
        count = CollisionDetection.CheckSphereBetweenPointsRobust(robust.Items, p + Vector3.UnitY, p - Vector3.UnitY,
            .5f, results.Length, true, TestFlags.Players, scene, results);
        rows.Add("robust:" + count + ":" + string.Join(";", results.Take(count).Select(ResultSignature)));
    }
}
foreach (var scene in new[] { a, b })
{
    Vector3 t = scene.Room!.RoomCollision[0].Translation;
    foreach (var roomCollision in scene.Room.RoomCollision) roomCollision.Active = false;
    var entities = new List<EntityBase>();
    foreach (var entity in scene.Entities) entities.Add(entity);
    CollisionResult hit = default;
    bool found = CollisionDetection.CheckBetweenPoints(t + new Vector3(2, 1, 2), t + new Vector3(2, -1, 2),
        TestFlags.None, scene, ref hit);
    if (!found || hit.EntityCollision != entities[1].EntityCollision[0])
        throw new Exception("Equal-distance entity contacts no longer choose reverse entity traversal.");
    rows.Add("entity-tie:" + ResultSignature(hit));
    var translated = entities[1].EntityCollision[0]!;
    translated.Transform = Matrix4.CreateTranslation(t.AddY(.5f));
    translated.Inverse1 = Matrix4.CreateTranslation(-t.AddY(.5f));
    translated.CurrentCenter = translated.CurrentCenter.AddY(.5f);
    hit = default;
    found = CollisionDetection.CheckBetweenPoints(t + new Vector3(2, 1, 2), t + new Vector3(2, -1, 2),
        TestFlags.None, scene, ref hit);
    if (!found || hit.Position != t + new Vector3(2, .5f, 2) || hit.Distance != .25f)
        throw new Exception("Transformed entity contact changed.");
    rows.Add("entity-translated-line:" + ResultSignature(hit));
    using (var candidates = Own(CollisionDetection.GetCandidatesForLimits(t + new Vector3(2, 1, 2),
        t + new Vector3(2, -1, 2), .5f, null, Vector3.Zero, true, scene)))
    {
        var results = new CollisionResult[16];
        int count = CollisionDetection.CheckSphereBetweenPointsRobust(candidates.Items, t + new Vector3(2, 1, 2),
            t + new Vector3(2, -1, 2), .5f, results.Length, true, TestFlags.Players, scene, results);
        rows.Add("entity-translated-robust:" + count + ":" + string.Join(";", results.Take(count).Select(ResultSignature)));
    }
    translated.Transform = Matrix4.CreateTranslation(t);
    translated.Inverse1 = Matrix4.CreateTranslation(-t);
    translated.CurrentCenter = translated.CurrentCenter.AddY(-.5f);
    foreach (var roomCollision in scene.Room.RoomCollision) roomCollision.Active = true;
}
string snapshot = string.Join("\n", rows) + "\n";
string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot)));
int snapshotArg = Array.IndexOf(args, "--snapshot");
if (snapshotArg >= 0) File.WriteAllText(args[snapshotArg + 1], snapshot);
int compareArg = Array.IndexOf(args, "--compare");
if (compareArg >= 0 && File.ReadAllText(args[compareArg + 1]) != snapshot)
    throw new Exception("Candidate ordering or collision float bits changed from baseline.");
Console.WriteLine($"SNAPSHOT rows={rows.Count} sha256={digest}");

using (var outer = Own(CollisionDetection.GetCandidatesForLimits(null, Vector3.Zero, 0,
    new Vector3(-1), new Vector3(13, 2, 5), false, a)))
{
    string before = CandidateSignature(outer.Items);
    CollisionCandidate[] items = outer.Items.ToArray();
    // Hold both candidates and an in-progress enumerator across a nested query.
    using var enumerator = outer.Items.GetEnumerator();
    bool first = enumerator.MoveNext();
    bool stable;
    using (var inner = Own(CollisionDetection.GetCandidatesForPoints(new Vector3(22, 4, -6),
        new Vector3(22, 2, -6), 0, false, b)))
    {
        stable = before == CandidateSignature(outer.Items)
            && items.All(c => c.Collision.Name.StartsWith("A"));
        try { while (enumerator.MoveNext()) { } } catch (InvalidOperationException) { stable = false; }
    }
    Console.WriteLine("NESTED_CANDIDATE_LIFETIME " + (stable && first ? "PASS" : "FAIL"));
    if (!baseline && (!stable || !first)) throw new Exception("Nested query invalidates borrowed candidates.");
}

// Reenter through the supported IReadOnlyList overload after the first face was seen.
// Detached candidates isolate deduplication from candidate-list corruption.
var empty = new Scene(new Vector2i(256, 192), SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(),
    _ => { }, () => { }, initializeRuntime: false);
using (var owned = Own(CollisionDetection.GetCandidatesForPoints(new Vector3(2, 1, 2),
    new Vector3(2, -1, 2), 0, false, a)))
{
    var original = owned.Items[0];
    var detached = new CollisionCandidate(original.Collision, original.Entry);
    var duplicates = new[] { detached, detached };
    var results = new CollisionResult[16];
    int expected = CollisionDetection.CheckSphereBetweenPoints(duplicates, new Vector3(2, 1, 2),
        new Vector3(2, -1, 2), .5f, results.Length, true, TestFlags.Players, a, results);
    var nested = new ReentrantCandidates(duplicates, () =>
    {
        CollisionResult ignored = default;
        CollisionDetection.CheckBetweenPoints(Vector3.UnitY, -Vector3.UnitY, TestFlags.None, empty, ref ignored);
    });
    int actual = CollisionDetection.CheckSphereBetweenPoints(nested, new Vector3(2, 1, 2),
        new Vector3(2, -1, 2), .5f, results.Length, true, TestFlags.Players, a, results);
    Console.WriteLine($"NESTED_DEDUPLICATION expected={expected} actual={actual}");
    if (!baseline && actual != expected) throw new Exception("Nested query corrupts face deduplication.");
}

// Force overlap deterministically: each scene acquires candidates before either consumes them.
int failures = 0;
object acquisitionGate = new();
using (var barrier = new Barrier(2))
{
    var workers = new[] { a, b }.Select(scene => Task.Factory.StartNew(() =>
    {
        for (int i = 0; i < 1000; i++)
        {
            Vector3 t = scene.Room!.RoomCollision[0].Translation;
            IReadOnlyList<CollisionCandidate> borrowed;
            lock (acquisitionGate) borrowed = CollisionDetection.GetCandidatesForPoints(t + new Vector3(2, 1, 2),
                t + new Vector3(2, -1, 2), 0, false, scene);
            using var candidates = Own(borrowed);
            barrier.SignalAndWait();
            CollisionResult hit = default;
            bool found;
            lock (acquisitionGate) found = CollisionDetection.CheckBetweenPoints(candidates.Items, t + new Vector3(2, 1, 2),
                t + new Vector3(2, -1, 2), TestFlags.Players, scene, ref hit);
            if (!found || hit.Position != t + new Vector3(2, 0, 2)) Interlocked.Increment(ref failures);
            barrier.SignalAndWait();
        }
    }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
    try { Task.WaitAll(workers); }
    catch (AggregateException ex) when (baseline) { failures++; Console.WriteLine("CONCURRENT_EXCEPTION " + ex.InnerException!.GetType().Name); }
}
Console.WriteLine($"INDEPENDENT_SCENES failures={failures}/2000");
if (!baseline && failures != 0) throw new Exception("Independent simultaneous scenes corrupt queries.");

if (!baseline)
{
    int simultaneousFailures = 0;
    Parallel.For(0, 8, lane =>
    {
        Scene scene = lane % 2 == 0 ? a : b;
        Vector3 t = scene.Room!.RoomCollision[0].Translation;
        var results = new CollisionResult[16];
        for (int i = 0; i < 2000; i++)
        {
            CollisionResult hit = default;
            bool found = CollisionDetection.CheckBetweenPoints(t + new Vector3(2, 1, 2),
                t + new Vector3(2, -1, 2), TestFlags.Players, scene, ref hit);
            int count = CollisionDetection.CheckSpherePenetration(t + new Vector3(2, -.1f, 2),
                .5f, results.Length, TestFlags.Players, scene, results);
            if (!found || hit.Position != t + new Vector3(2, 0, 2) || count <= 0)
                Interlocked.Increment(ref simultaneousFailures);
        }
    });
    Console.WriteLine($"CONCURRENT_QUERY_STRESS failures={simultaneousFailures}/32000");
    if (simultaneousFailures != 0) throw new Exception("Concurrent convenience queries corrupt collision scratch.");

    // Rotation releases collision owners even while pooled candidate objects remain cached.
    CollisionCandidate captured;
    var lease = CollisionDetection.GetCandidatesForPoints(new Vector3(2, 1, 2), new Vector3(2, -1, 2), 0, false, a);
    captured = lease[0];
    ((IDisposable)lease).Dispose();
    ((IDisposable)lease).Dispose();
    if (captured.Collision != null || captured.EntityCollision != null)
        throw new Exception("Returned candidates retain the previous room or entity.");
    bool rejected = false;
    try { _ = lease.Count; } catch (ObjectDisposedException) { rejected = true; }
    if (!rejected) throw new Exception("Disposed candidate access is accepted.");
    Console.WriteLine("RETURN_CLEARS_SCENE_REFERENCES PASS");

    // Many nested queries may allocate while active, but cannot grow the retained pool indefinitely.
    var leases = new List<IReadOnlyList<CollisionCandidate>>();
    for (int i = 0; i < 24; i++) leases.Add(CollisionDetection.GetCandidatesForPoints(
        new Vector3(2, 1, 2), new Vector3(2, -1, 2), 0, false, a));
    if (leases.Distinct(ReferenceEqualityComparer.Instance).Count() != 24)
        throw new Exception("Nested borrows alias an active workspace.");
    foreach (var item in leases) ((IDisposable)item).Dispose();
    var pool = (System.Collections.ICollection)lease.GetType().GetField("_available", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
    if (pool.Count > 8) throw new Exception("Retained workspace pool is unbounded.");
    Console.WriteLine($"RETAINED_POOL count={pool.Count} nested=24");

    using (var failureLease = Own(CollisionDetection.GetCandidatesForPoints(new Vector3(2, 1, 2),
        new Vector3(2, -1, 2), 0, false, a)))
    {
        var item = failureLease.Items[0];
        var duplicate = new CollisionCandidate(item.Collision, item.Entry);
        int cached = pool.Count;
        var throwing = new ReentrantCandidates(new[] { duplicate, duplicate },
            () => throw new InvalidOperationException("injected query failure"));
        try
        {
            var results = new CollisionResult[16];
            CollisionDetection.CheckSphereBetweenPoints(throwing, new Vector3(2, 1, 2),
                new Vector3(2, -1, 2), .5f, results.Length, true, TestFlags.Players, a, results);
            throw new Exception("Injected query failure was not observed.");
        }
        catch (InvalidOperationException error) when (error.Message == "injected query failure") { }
        if (pool.Count != cached) throw new Exception("Query failure leaks the borrowed workspace.");
    }
    Console.WriteLine("EXCEPTION_RETURNS_WORKSPACE PASS");

    MeasureLargeQuery(requireZero: true);
    MeasureLargeQuery(requireZero: true, candidateCount: 8192);
    CheckOversizedQuery();
}

}

// Measure the production convenience path with preallocated result storage.
var scratch = new CollisionResult[16];
void Query()
{
    CollisionResult hit = default;
    CollisionDetection.CheckBetweenPoints(new Vector3(2, 1, 2), new Vector3(2, -1, 2), TestFlags.Players, a, ref hit);
    CollisionDetection.CheckSphereBetweenPoints(new Vector3(2, 1, 2), new Vector3(2, -1, 2), .5f,
        scratch.Length, true, TestFlags.Players, a, scratch);
    CollisionDetection.CheckSpherePenetration(new Vector3(2, -.1f, 2), .5f, scratch.Length, TestFlags.Players, a, scratch);
    CollisionDetection.CheckInRadius(new Vector3(2, .2f, 2), .5f, scratch.Length, false, TestFlags.Players, a, scratch);
}
for (int i = 0; i < 10000; i++) Query();
long beforeAllocation = GC.GetAllocatedBytesForCurrentThread();
for (int i = 0; i < 10000; i++) Query();
long allocation = GC.GetAllocatedBytesForCurrentThread() - beforeAllocation;
Console.WriteLine($"WARM_ALLOCATION bytes={allocation} queries=40000");
if (!baseline && allocation != 0) throw new Exception("Warmed collision queries allocate.");

void BorrowedQuery()
{
    using var candidates = Own(CollisionDetection.GetCandidatesForLimits(new Vector3(2, 1, 2),
        new Vector3(2, -1, 2), .5f, null, Vector3.Zero, true, a));
    CollisionDetection.CheckSphereBetweenPointsRobust(candidates.Items, new Vector3(2, 1, 2),
        new Vector3(2, -1, 2), .5f, scratch.Length, true, TestFlags.Players, a, scratch);
}
for (int i = 0; i < 10000; i++) BorrowedQuery();
beforeAllocation = GC.GetAllocatedBytesForCurrentThread();
for (int i = 0; i < 10000; i++) BorrowedQuery();
allocation = GC.GetAllocatedBytesForCurrentThread() - beforeAllocation;
Console.WriteLine($"BORROWED_WARM_ALLOCATION bytes={allocation} borrows=10000 checks=10000");
if (!baseline && allocation != 0) throw new Exception("Warmed borrowed collision queries allocate.");

if (args.Contains("--benchmark-only"))
{
var samples = new double[9];
for (int run = 0; run < samples.Length; run++)
{
    long start = Stopwatch.GetTimestamp();
    for (int i = 0; i < 10000; i++) Query();
    samples[run] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
}
Console.WriteLine("QUERY_TIMING batchesMs=" + string.Join(",", samples.Select(ms => ms.ToString("F3", System.Globalization.CultureInfo.InvariantCulture))));
}

static void MeasureLargeQuery(bool requireZero, int candidateCount = 4225)
{
    var large = MakeLargeScene(candidateCount);
    const int measuredQueries = 100;
    int count = 0;
    void Query()
    {
        using var lease = Own(CollisionDetection.GetCandidatesForLimits(null, Vector3.Zero, 0,
            new Vector3(-1), new Vector3(1025, 1, 1025), false, large));
        count = lease.Items.Count;
        if (count != candidateCount) throw new Exception("Large query candidates were truncated.");
    }
    for (int i = 0; i < 20; i++) Query();
    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int i = 0; i < measuredQueries; i++) Query();
    long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
    Console.WriteLine($"LARGE_WARM_ALLOCATION candidates={count} queries={measuredQueries} bytes={bytes}");
    var workspaceType = typeof(CollisionDetection).Assembly.GetType("MphRead.Formats.CollisionQueryWorkspace");
    if (workspaceType != null)
    {
        var pool = (System.Collections.ICollection)workspaceType.GetField("_available", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        int cap = (int)workspaceType.GetField("MaxRetainedCandidates", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
        int retainedCandidates = 0, candidateCapacity = 0, pendingCapacity = 0, inactiveCapacity = 0;
        foreach (object workspace in pool)
        {
            var items = (List<CollisionCandidate>)workspaceType.GetProperty("Items", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(workspace)!;
            var pending = (Stack<CollisionCandidate>)workspaceType.GetProperty("PendingItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(workspace)!;
            var inactive = (Queue<CollisionCandidate>)workspaceType.GetField("_inactiveItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(workspace)!;
            candidateCapacity += items.Capacity;
            pendingCapacity += pending.EnsureCapacity(0);
            inactiveCapacity += inactive.EnsureCapacity(0);
            retainedCandidates += inactive.Count;
            if (inactive.Any(c => c.Collision != null || c.EntityCollision != null))
                throw new Exception("Large cached candidates retain their previous scene.");
            if (items.Capacity > cap || pending.EnsureCapacity(0) > cap || inactive.EnsureCapacity(0) > cap)
                throw new Exception("Large query exceeds a retained collection capacity budget.");
        }
        Console.WriteLine($"LARGE_RETAINED_POOL workspaces={pool.Count} candidateCap={cap} objects={retainedCandidates} itemCapacity={candidateCapacity} pendingCapacity={pendingCapacity} inactiveCapacity={inactiveCapacity} maximumObjects={8 * cap}");
        if (pool.Count > 8 || retainedCandidates > 8 * cap) throw new Exception("Retained large scratch is unbounded.");
    }
    if (requireZero && bytes != 0) throw new Exception("Repeated warmed large queries allocate.");
}

static void CheckOversizedQuery()
{
    var large = MakeLargeScene(candidateCount: 8193);
    var workspaceType = typeof(CollisionDetection).Assembly.GetType("MphRead.Formats.CollisionQueryWorkspace")!;
    var pool = (System.Collections.ICollection)workspaceType.GetField("_available", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
    int cachedBefore = pool.Count;
    var lease = CollisionDetection.GetCandidatesForLimits(null, Vector3.Zero, 0,
        new Vector3(-1), new Vector3(1025, 1, 1025), false, large);
    int count = lease.Count;
    CollisionCandidate first = lease[0];
    if (count != 8193) throw new Exception("Oversized query candidates were truncated.");
    ((IDisposable)lease).Dispose();
    if (pool.Count != cachedBefore - 1) throw new Exception("Oversized scratch returned to the retained pool.");
    if (first.Collision != null || first.EntityCollision != null)
        throw new Exception("Discarded oversized candidates retain their scene.");
    Console.WriteLine($"OVERSIZED_QUERY candidates={count} poolBefore={cachedBefore} poolAfter={pool.Count} sceneReferencesCleared=true");
}

static Scene MakeLargeScene(int candidateCount = 4225)
{
    var large = MakeScene("large", Vector3.Zero);
    var face = new CollisionDataEditor { Plane = new Vector4(Vector3.UnitY, 0), LayerMask = 5 };
    int x = candidateCount == 4225 ? 256 : 508;
    int z = candidateCount == 4225 ? 256 : 252;
    face.Points.AddRange(new[] { Vector3.Zero, new Vector3(0,0,z), new Vector3(x,0,z), new Vector3(x,0,0) });
    byte[] bytes = MapCollisionPacker.Pack(new[] { face });
    var info = Collision.ReadMphCollision(Read.ReadStruct<CollisionHeader>(bytes), bytes, -1);
    var roomCollisions = (List<CollisionInstance>)large.Room!.RoomCollision;
    roomCollisions.Clear(); roomCollisions.Add(new CollisionInstance("large", info, false));
    if (candidateCount == 8193)
    {
        var extra = new CollisionDataEditor { Plane = new Vector4(Vector3.UnitY, 0), LayerMask = 5 };
        extra.Points.AddRange(new[] { Vector3.Zero, new Vector3(0,0,1), new Vector3(1,0,1), new Vector3(1,0,0) });
        byte[] extraBytes = MapCollisionPacker.Pack(new[] { extra });
        var extraInfo = Collision.ReadMphCollision(Read.ReadStruct<CollisionHeader>(extraBytes), extraBytes, -1);
        roomCollisions.Add(new CollisionInstance("large-plus-one", extraInfo, false));
    }
    return large;
}

static Owner Own(IReadOnlyList<CollisionCandidate> items) => new(items);
static string CandidateSignature(IReadOnlyList<CollisionCandidate> items) => string.Join(";", items.Select(c =>
    $"{c.Collision.Name}:{c.Entry.DataStartIndex}:{c.Entry.DataCount}:{c.EntityCollision != null}"));
static string ResultSignature(CollisionResult r) => string.Join(",", new[] {
    (int)r.Field0, (int)r.Flags, BitConverter.SingleToInt32Bits(r.Plane.X), BitConverter.SingleToInt32Bits(r.Plane.Y),
    BitConverter.SingleToInt32Bits(r.Plane.Z), BitConverter.SingleToInt32Bits(r.Plane.W), BitConverter.SingleToInt32Bits(r.Field14),
    BitConverter.SingleToInt32Bits(r.Position.X), BitConverter.SingleToInt32Bits(r.Position.Y), BitConverter.SingleToInt32Bits(r.Position.Z),
    BitConverter.SingleToInt32Bits(r.Distance), BitConverter.SingleToInt32Bits(r.EdgePoint1.X), BitConverter.SingleToInt32Bits(r.EdgePoint1.Y),
    BitConverter.SingleToInt32Bits(r.EdgePoint1.Z), BitConverter.SingleToInt32Bits(r.EdgePoint2.X), BitConverter.SingleToInt32Bits(r.EdgePoint2.Y),
    BitConverter.SingleToInt32Bits(r.EdgePoint2.Z), r.EntityCollision == null ? 0 : 1 });
static Scene MakeScene(string name, Vector3 translation)
{
    var scene = new Scene(new Vector2i(256, 192), SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(),
        _ => { }, () => { }, initializeRuntime: false);
    var room = new RoomEntity(scene);
    typeof(Scene).GetField("_room", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(scene, room);
    var faces = new List<CollisionDataEditor>();
    foreach (int x in new[] { 0, 4, 8 })
    {
        var face = new CollisionDataEditor { Plane = new Vector4(Vector3.UnitY, 0), LayerMask = 5,
            Flags = x == 4 ? CollisionFlags.IgnorePlayers : x == 8 ? CollisionFlags.IgnoreBeams : CollisionFlags.None };
        face.Points.AddRange(new[] { new Vector3(x, 0, 0), new Vector3(x, 0, 4),
            new Vector3(x + 4, 0, 4), new Vector3(x + 4, 0, 0) });
        faces.Add(face);
    }
    byte[] bytes = MapCollisionPacker.Pack(faces);
    var info = Collision.ReadMphCollision(Read.ReadStruct<CollisionHeader>(bytes), bytes, -1);
    var collisions = (List<CollisionInstance>)room.RoomCollision;
    collisions.Add(new CollisionInstance(name + "-first", info, false) { Translation = translation });
    foreach (var face in faces) face.Flags |= CollisionFlags.Damaging;
    byte[] tieBytes = MapCollisionPacker.Pack(faces);
    var tieInfo = Collision.ReadMphCollision(Read.ReadStruct<CollisionHeader>(tieBytes), tieBytes, -1);
    collisions.Add(new CollisionInstance(name + "-tie", tieInfo, false) { Translation = translation });
    foreach (int n in new[] { 0, 1 })
    {
        var entity = new FixtureEntity(scene);
        var collision = new CollisionInstance(name + "-entity" + n, info, false);
        entity.EntityCollision[0] = new EntityCollision(collision, entity)
        {
            Transform = Matrix4.CreateTranslation(translation),
            Inverse1 = Matrix4.CreateTranslation(-translation),
            Inverse2 = Matrix4.CreateTranslation(-translation),
            CurrentCenter = translation + new Vector3(6, 0, 2), MaxDistance = 10
        };
        scene.InsertEntity(entity);
    }
    return scene;
}
readonly struct Owner(IReadOnlyList<CollisionCandidate> items) : IDisposable
{
    public IReadOnlyList<CollisionCandidate> Items => items;
    public void Dispose() { if (items is IDisposable disposable) disposable.Dispose(); }
}

sealed class FixtureEntity(Scene scene) : EntityBase(EntityType.Object, scene) { }

sealed class ReentrantCandidates(IReadOnlyList<CollisionCandidate> items, Action nested) : IReadOnlyList<CollisionCandidate>
{
    private bool _triggered;
    public int Count => items.Count;
    public CollisionCandidate this[int index]
    {
        get
        {
            if (index == 1 && !_triggered) { _triggered = true; nested(); }
            return items[index];
        }
    }
    public IEnumerator<CollisionCandidate> GetEnumerator() => items.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
