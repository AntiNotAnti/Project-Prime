using MphRead.Mods.Render;

int checks = 0;
void Check(bool value, string description)
{
    if (!value) throw new InvalidOperationException(description);
    Console.WriteLine("GPUTIMING PASS " + description);
    checks++;
}
void Map(GpuTimingReadbackLease lease, nint token)
{
    lease.MarkSubmitted();
    lease.BeginMapping(token);
}

Check(GpuTimingSamplePolicy.TryMilliseconds(100, 1100, 1000, out double ms) && ms == 1,
    "valid timestamp period converts ticks to milliseconds");
Check(GpuTimingSamplePolicy.TryMilliseconds(100, 101, 2.5, out ms) && ms == 0.0000025,
    "fractional timestamp period retains sub-millisecond units");
Check(GpuTimingSamplePolicy.TryMilliseconds(ulong.MaxValue - 1000, ulong.MaxValue, 1000, out ms) && ms == 1,
    "unsigned delta is computed before floating-point conversion");
Check(GpuTimingSamplePolicy.TryMilliseconds(50, 50, 1, out ms) && ms == 0,
    "equal valid ticks remain an explicit zero sample");
foreach (double invalid in new[] { 0d, -1, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
    Check(!GpuTimingSamplePolicy.TryMilliseconds(1, 2, invalid, out ms) && ms == 0,
        "invalid timestamp period rejected: " + invalid);
Check(!GpuTimingSamplePolicy.TryMilliseconds(50, 49, 1, out ms), "reversed/disjoint clock rejected");
Check(!GpuTimingSamplePolicy.TryMilliseconds(0, ulong.MaxValue, double.MaxValue, out ms), "nonfinite converted duration rejected");

var lease = new GpuTimingReadbackLease();
Check(lease.TryReserve(10) && !lease.TryReserve(11), "recording reservation cannot be overwritten");
lease.MarkSubmitted();
Check(!lease.TryReserve(11), "submitted GPU work keeps its slot busy");
lease.BeginMapping((nint)101);
Check(!lease.TryReserve(11) && !lease.TryGetCompletion(out _, out _), "pending mapping cannot be reused or read");
Check(!lease.TryComplete((nint)102, 0) && !lease.TryComplete(0, 0), "callback must present the exact nonzero token");
Check(lease.TryComplete((nint)101, 0) && !lease.TryComplete((nint)101, 0), "map callback completes exactly once");
Check(lease.TryGetCompletion(out long frame, out int status) && frame == 10 && status == 0,
    "completion retains its original frame identity");
Check(!lease.TryReserve(11), "completed map stays busy until owner consumes and unmaps it");
lease.Finish();
Check(lease.MapToken == 0 && lease.TryReserve(11), "finished readback slot is reusable");
Map(lease, (nint)103);
Check(!lease.TryComplete((nint)101, 0), "old callback cannot complete a newer reservation");
Check(!lease.Cancel((nint)101) && lease.MapToken == (nint)103, "old cancellation cannot erase the newer map");
Check(lease.Cancel((nint)103) && !lease.TryComplete((nint)103, 0), "map-start failure unregister/cancel invalidates late callback");
Check(lease.TryReserve(12), "map-start failure does not consume a slot forever");
Map(lease, (nint)104);
lease.Cancel();
Check(lease.MapToken == 0 && !lease.TryComplete((nint)104, 0), "shutdown clears token before native release");

var unmapLease = new GpuTimingReadbackLease();
Check(!unmapLease.CancelAndTakeUnmap(0), "free native buffer never requests unmap");
unmapLease.TryReserve(20);
Check(!unmapLease.CancelAndTakeUnmap(0), "recording query without a map never requests unmap");
unmapLease.TryReserve(21); unmapLease.MarkSubmitted();
Check(!unmapLease.CancelAndTakeUnmap(0), "submitted copy without a map never requests unmap");
unmapLease.TryReserve(22); Map(unmapLease, (nint)301);
Check(unmapLease.CancelAndTakeUnmap(0) && unmapLease.MapToken == 0
    && !unmapLease.TryComplete((nint)301, 0), "accepted pending map requests abort-unmap after invalidating its token");
unmapLease.TryReserve(23); Map(unmapLease, (nint)302); unmapLease.TryComplete((nint)302, 0);
Check(unmapLease.CancelAndTakeUnmap(0), "completed successful map requests native unmap");
unmapLease.TryReserve(24); Map(unmapLease, (nint)303); unmapLease.TryComplete((nint)303, 1);
Check(!unmapLease.CancelAndTakeUnmap(0), "completed failed map skips idle native unmap");
unmapLease.TryReserve(25); Map(unmapLease, (nint)304);
Check(!unmapLease.CancelAndTakeUnmap(0, pendingMapAccepted: false, expectedToken: (nint)304)
    && unmapLease.TryReserve(26), "map-start throw before acceptance skips idle unmap and resets reservation");
Map(unmapLease, (nint)305); unmapLease.TryComplete((nint)305, 0);
Check(unmapLease.CancelAndTakeUnmap(0, pendingMapAccepted: false, expectedToken: (nint)305),
    "synchronous successful callback proves mapped storage even when map-start reports a throw");
unmapLease.TryReserve(27); Map(unmapLease, (nint)306); unmapLease.TryComplete((nint)306, 1);
Check(!unmapLease.CancelAndTakeUnmap(0, pendingMapAccepted: false, expectedToken: (nint)306),
    "synchronous failed callback never requests an idle unmap");
unmapLease.TryReserve(28); Map(unmapLease, (nint)307);
Check(!unmapLease.CancelAndTakeUnmap(0, expectedToken: (nint)306)
    && unmapLease.MapToken == (nint)307, "old unmap cancellation cannot invalidate a newer map");
unmapLease.Cancel();

var ring = Enumerable.Range(0, 8).Select(_ => new GpuTimingReadbackLease()).ToArray();
for (int i = 0; i < ring.Length; i++)
{
    Check(ring[i].TryReserve(100 + i), "reserve ring slot " + i);
    Map(ring[i], (nint)(200 + i));
}
Check(ring.All(slot => !slot.TryReserve(999)), "full ring drops work without waiting or overwriting frame IDs");
ring[7].TryComplete((nint)207, 0);
ring[2].TryComplete((nint)202, 1);
Check(ring[7].TryGetCompletion(out frame, out _) && frame == 107,
    "out-of-order later-frame completion preserves identity");
Check(ring[2].TryGetCompletion(out frame, out status) && frame == 102 && status == 1,
    "failed out-of-order map preserves its frame/status");
ring[7].Finish(); ring[2].Finish();
Check(ring.Count(slot => slot.TryReserve(999)) == 2, "only consumed slots can reenter the ring");
foreach (var slot in ring) slot.Cancel();
Check(ring.Select((slot, i) => !slot.TryComplete((nint)(200 + i), 0)).All(value => value),
    "shutdown invalidates all outstanding tokens");

var completed = new GpuTimingSampleQueue<long>(64);
int drops = 0;
for (int i = 0; i < 10000; i++) if (completed.Enqueue(i)) drops++;
Check(completed.Count == 64 && drops == 9936, "undrained completed samples stay bounded and record exact drops");
Check(completed.TryDequeue(out long first) && first == 9936, "bounded results preserve the latest frame window");
long previous = first;
bool ordered = true;
while (completed.TryDequeue(out long next)) { ordered &= next == previous + 1; previous = next; }
Check(ordered && previous == 9999 && completed.Count == 0, "completed samples drain in enqueue order");
completed.Enqueue(7); completed.Clear();
Check(!completed.TryDequeue(out _), "renderer teardown clears completed results");

// Exercise actual cross-thread callback/cancellation synchronization, while the
// order is intentionally unspecified. After teardown neither result is visible.
for (int i = 0; i < 1000; i++)
{
    var raced = new GpuTimingReadbackLease();
    raced.TryReserve(i); Map(raced, (nint)(10000 + i));
    await Task.WhenAll(Task.Run(() => raced.TryComplete((nint)(10000 + i), 0)),
        Task.Run(() => raced.Cancel()));
    if (raced.TryGetCompletion(out _, out _) || !raced.TryReserve(i + 1))
        throw new InvalidOperationException("Callback/shutdown race retained the old reservation.");
}
Check(true, "1000 callback/shutdown races leave reusable managed leases without stale completion");
checks += QueuedFramePacingChecks.Run();
Console.WriteLine($"GPUTIMING {checks} checks PASS");
