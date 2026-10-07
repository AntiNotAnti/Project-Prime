using MphRead.Mods.Render;
using MphRead.Sound;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

// A CPU cleanup failure or a failing error sink must not skip native release.
var cleanupOrder = new List<string>();
OwnerCleanup.Release(() => { cleanupOrder.Add("cpu"); throw new Exception("cpu fault"); },
    () => cleanupOrder.Add("gpu"), _ => throw new Exception("log fault"));
Require(cleanupOrder.SequenceEqual(new[] { "cpu", "gpu" }), "Cleanup failure skipped native resource release");
OwnerCleanup.Release(() => cleanupOrder.Add("cpu2"),
    () => { cleanupOrder.Add("gpu2"); throw new Exception("gpu fault"); }, _ => { });
Require(cleanupOrder.TakeLast(2).SequenceEqual(new[] { "cpu2", "gpu2" }), "Normal cleanup phases reordered");

// Every source texel, including a far-edge clear-depth texel, must reach the
// root. Check odd/tall/one-wide chains using WebGPU's actual floor extents.
foreach ((int w, int h) in new[] { (1365, 767), (801, 603), (7, 5), (1, 9), (9, 1), (4, 4) })
{
    foreach ((int edgeX, int edgeY) in new[] { (w - 1, h - 1), (w / 2, h / 2), (0, 0) })
    {
        float[,] source = new float[h, w]; source[edgeY, edgeX] = 1;
        int sw = w, sh = h;
        while (sw > 1 || sh > 1)
        {
            int dw = Math.Max(1, sw / 2), dh = Math.Max(1, sh / 2);
            var destination = new float[dh, dw];
            var visited = new bool[sh, sw];
            for (int y = 0; y < dh; y++) for (int x = 0; x < dw; x++)
            {
                var fy = ConservativeHiZ.Footprint(y, sh, dh);
                var fx = ConservativeHiZ.Footprint(x, sw, dw);
                for (int sy = fy.Start; sy < fy.End; sy++) for (int sx = fx.Start; sx < fx.End; sx++)
                { visited[sy, sx] = true; destination[y, x] = Math.Max(destination[y, x], source[sy, sx]); }
            }
            foreach (bool value in visited) Require(value, $"HiZ omitted texel in {sw}x{sh}");
            source = destination; sw = dw; sh = dh;
        }
        Require(source[0, 0] == 1, $"HiZ lost depth hole at {edgeX},{edgeY} of {w}x{h}");
    }
}
Require(!ConservativeHiZ.TemporalOcclusionEnabled, "Untrusted moving-world history must not reject geometry");
// Verify normalized, clamped lookup against independent rational pixel probes.
// An odd source texel crossing a destination boundary belongs to both cells.
foreach(int size in new[] {1,2,3,5,7,15,31,63,127,255,1080,1440,1920,2560,3840})
{
    int current=size;
    do
    {
        int next=Math.Max(1,current/2);
        for(int cell=0;cell<next;cell++)
        {
            var footprint=ConservativeHiZ.Footprint(cell,current,next);
            Require(footprint.Start==(int)Math.Floor(cell*(double)current/next)
                && footprint.End==(int)Math.Ceiling((cell+1)*(double)current/next),"HiZ normalized footprint disagrees with independent interval oracle");
        }
        for(int source=0;source<current;source++)for(int quarter=0;quarter<4;quarter++)
        {
            int cell=Math.Clamp((int)((4L*source+quarter)*next/(4L*current)),0,next-1);
            var footprint=ConservativeHiZ.Footprint(cell,current,next);
            Require(footprint.Start<=source && source<footprint.End,"HiZ normalized lookup lost its source texel");
        }
        current=next;
    }while(current>1);
}

// Bounded atlas churn with a surviving shared lease. Randomized rent/return
// checks disjointness and capacity against a separate occupancy bitmap.
var atlas = new RetainedAtlasRanges(4096);
uint shared = atlas.Rent(128);
var random = new Random(0x5052494D);
var occupied = new bool[4096]; Array.Fill(occupied, true, (int)shared, 128);
var live = new List<(uint Start, uint Count)>();
for (int iteration = 0; iteration < 20000; iteration++)
{
    if (live.Count > 0 && random.Next(3) == 0)
    {
        int index = random.Next(live.Count); var range = live[index]; live.RemoveAt(index);
        atlas.Return(range.Start, range.Count);
        for (uint unit = range.Start; unit < range.Start + range.Count; unit++) occupied[unit] = false;
    }
    else
    {
        uint count = (uint)random.Next(1, 200);
        if (!atlas.CanRent(count)) continue;
        uint start = atlas.Rent(count); live.Add((start, count));
        for (uint unit = start; unit < start + count; unit++)
        { Require(!occupied[unit], "Atlas reused a live/shared span"); occupied[unit] = true; }
    }
    Require(atlas.LiveUnits == (uint)occupied.Count(value => value), "Atlas live accounting diverged");
}
foreach (var range in live) atlas.Return(range.Start, range.Count);
Require(atlas.LiveUnits == 128 && atlas.CanRent(4096 - 128), "Returned spans did not coalesce around shared lease");
atlas.Return(shared, 128); Require(atlas.Rent(4096) == 0, "Whole empty page was not reusable");

var bindings = new Dictionary<string, int> { ["shared"] = 7 };
var pins = new HashSet<int> { 7 };
var released = new List<int>();
var transaction = new TextureUploadTransaction(bindings, pins, released.Add);
bindings["new-albedo"] = 9; bindings["new-map"] = 11; bindings["alias"] = 9;
pins.Add(9); transaction.Dispose(); transaction.Dispose();
Require(bindings.Count == 1 && bindings["shared"] == 7 && pins.SetEquals(new[] { 7 }), "Failed compile retained admissions/pins or released shared owner");
Require(released.Order().SequenceEqual(new[] { 9, 11 }), "Failed compile did not release each unique new texture exactly once");
using (var committed = new TextureUploadTransaction(bindings, pins, released.Add))
{ bindings["good"] = 13; pins.Add(13); committed.Commit(); }
Require(bindings["good"] == 13 && released.Count == 2, "Committed texture owner was rolled back");
Require(TextureStorageMath.Bytes(7, 5, 3, 4, false) == 168, "Odd RGBA texture accounting");
Require(TextureStorageMath.Bytes(7, 5, 3, 16, true) == 96, "Compressed block texture accounting");
Require(TextureStorageMath.Bytes(4, 4, 3, 8, false) == 168, "RGBA16f texture accounting");

// The first constructor intentionally ignores cancellation while blocked.
// Request 2 must wait for its owner to release, reject result 1, and remain
// Loading after worker 1 finishes. Shutdown must drain both, even the stale one.
var queue = new LatestResourceQueue<Payload>();
using var firstEntered = new ManualResetEventSlim(); using var releaseFirst = new ManualResetEventSlim();
using var secondEntered = new ManualResetEventSlim(); using var releaseSecond = new ManualResetEventSlim();
int constructing = 0, maxConstructing = 0;
var published = new List<int>(); var disposed = new List<int>(); var errors = new List<Exception>();
Payload Build(int id, ManualResetEventSlim entered, ManualResetEventSlim release)
{
    int concurrent = Interlocked.Increment(ref constructing); maxConstructing = Math.Max(maxConstructing, concurrent);
    entered.Set(); Require(release.Wait(5000), "Queue test constructor timed out");
    Interlocked.Decrement(ref constructing); return new(id);
}
void Publish(Payload value) { lock (published) published.Add(value.Id); }
void DisposePayload(Payload value) { lock (disposed) disposed.Add(value.Id); }
queue.Request(_ => Build(1, firstEntered, releaseFirst), Publish, DisposePayload, errors.Add);
Require(firstEntered.Wait(5000), "First music request did not start");
queue.Request(_ => Build(2, secondEntered, releaseSecond), Publish, DisposePayload, errors.Add);
Require(!secondEntered.IsSet && queue.Loading, "Music constructors overlapped or newest Loading cleared");
releaseFirst.Set(); Require(secondEntered.Wait(5000), "Newest music request did not start");
Require(queue.Loading, "Superseded worker cleared newest Loading");
releaseSecond.Set(); Require(queue.Pending.Wait(5000), "Music requests did not drain");
Require(maxConstructing == 1 && published.SequenceEqual(new[] { 2 }) && disposed.SequenceEqual(new[] { 1 }), "Music generation/publication ownership failed");
Require(!queue.Loading && errors.Count == 0, "Successful music queue did not settle");
queue.Request(_ => throw new InvalidDataException("decode fault"), Publish, DisposePayload, errors.Add);
Require(queue.Pending.Wait(5000) && errors.Count == 1 && !queue.Loading, "Decode fault did not settle");
using var cancelEntered = new ManualResetEventSlim(); using var releaseCancelled = new ManualResetEventSlim();
queue.Request(_ => Build(6, cancelEntered, releaseCancelled), Publish, DisposePayload, errors.Add);
Require(cancelEntered.Wait(5000), "Cancelled request did not start");
queue.Cancel(); Require(!queue.Loading, "Teardown cancellation did not revoke current loading state");
releaseCancelled.Set(); Require(queue.Pending.Wait(5000), "Cancelled constructor did not drain");
Require(!published.Contains(6) && disposed.Contains(6), "Teardown allowed late publication or leaked local resources");
using var lastEntered = new ManualResetEventSlim(); using var releaseLast = new ManualResetEventSlim();
queue.Request(_ => Build(3, lastEntered, releaseLast), Publish, DisposePayload, errors.Add);
Require(lastEntered.Wait(5000), "Shutdown request did not start");
queue.Request(_ => new(4), Publish, DisposePayload, errors.Add);
Task closing = queue.Close(); Require(!closing.IsCompleted, "Shutdown forgot superseded active constructor");
releaseLast.Set(); Require(closing.Wait(5000), "Shutdown did not drain chain");
Require(!published.Contains(3) && !published.Contains(4) && disposed.Contains(3), "Shutdown published or leaked cancelled request");
queue.Request(_ => new(5), Publish, DisposePayload, errors.Add);
Require(!published.Contains(5), "Closed music queue accepted work");
Console.WriteLine("PASS: odd HiZ coverage, bounded/shared atlas churn, texture rollback/capacity, music generation/cancel/fault/shutdown ownership");
internal sealed record Payload(int Id);
