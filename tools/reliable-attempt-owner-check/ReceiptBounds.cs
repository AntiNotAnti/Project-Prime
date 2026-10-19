using System.Net;
using MphRead.Mods.Network;

internal static class ReceiptBounds
{
    private static int checks, failures;
    private static void Check(bool ok, string label)
    { checks++; if (!ok) { failures++; Console.Error.WriteLine("FAIL " + label); } }
    internal static int Run()
    {
        var endpoint = new IPEndPoint(IPAddress.Loopback, 32400);
        var sender = new NetConnection(endpoint, 300); var receiver = new NetConnection(endpoint, 300);
        sender.Reliable.TryQueue(PacketType.TimedGrant, new byte[] { 1 }, 0, out var id);
        sender.Reliable.TrySend(0, out var tx);
        var original = sender.Send(tx.Type, 0, NetHeaderFlags.Reliable, tx.EventId);
        receiver.Receive(original, 400); receiver.Reliable.Receive(id);
        var ack = receiver.Send(0, 400, NetHeaderFlags.AckOnly);
        for (int i = 0; i < 2048; i++) sender.Send(PacketType.Snapshot, 799.0 * (i + 1) / 2049);
        Check(sender.Reliable.RetainedAttemptCount == 1, "unreliable pressure never evicts reliable receipt");
        sender.Receive(ack, 800); var settled = sender.Capture();
        Check(sender.Reliable.Capture(800).Pending == 0 && sender.Reliable.RetainedAttemptCount == 0,
            "ACK retires event and all retained receipts");
        Check(settled.Acknowledged == 1 && settled.RttMilliseconds == 800 && settled.RttJitterMilliseconds == 400,
            "retained ACK feeds existing exact RTT and jitter estimator after telemetry alias");
        sender.Receive(ack, 900); var repeated = sender.Capture();
        Check(repeated.Acknowledged == settled.Acknowledged && repeated.RttMilliseconds == settled.RttMilliseconds
            && repeated.RttJitterMilliseconds == settled.RttJitterMilliseconds,
            "duplicate ACK cannot resample RTT or increment acknowledged count");

        var normal = new NetConnection(endpoint, 301); normal.Reliable.UpdateRto(0, 0);
        normal.Reliable.TryQueue(PacketType.Welcome, new byte[] { 1 }, 0, out _);
        int rounds = 0, peak = 0; bool earlyFailure = false;
        for (int now = 0; now < NetReliableChannel.LifetimeMilliseconds; now++)
        {
            if (normal.Reliable.TrySend(now, out var next))
            {
                rounds++;
                for (int copy = 0; copy < 3; copy++) normal.Send(next.Type, now, NetHeaderFlags.Reliable, next.EventId);
                peak = Math.Max(peak, normal.Reliable.RetainedAttemptCount);
            }
            earlyFailure |= normal.Reliable.Failed;
        }
        Check(rounds == 16 && peak == 48 && !earlyFailure && peak < NetReliableChannel.MaximumReceiptsPerEvent,
            "normal75ms exponential15s schedule x3copies uses exactly48 receipts below64");
        Check(!normal.Reliable.TrySend(15000, out _) && normal.Reliable.Failed
            && normal.Reliable.RetainedAttemptCount == 0, "expiry retires retained receipts with unchanged deadline");

        var pressure = new NetConnection(endpoint, 302);
        pressure.Reliable.TryQueue(PacketType.Welcome, new byte[] { 1 }, 0, out var pressureId);
        for (int i = 0; i < NetReliableChannel.MaximumReceiptsPerEvent; i++)
            pressure.Send(PacketType.Welcome, i, NetHeaderFlags.Reliable, pressureId);
        Check(!pressure.Reliable.Failed && pressure.Reliable.RetainedAttemptCount == 64,
            "expedite pressure retains all64 live receipts without overwrite");
        pressure.Send(PacketType.Welcome, 65, NetHeaderFlags.Reliable, pressureId);
        Check(pressure.Reliable.Failed && pressure.Reliable.FailureReason!.Contains("receipt capacity")
            && pressure.Reliable.RetainedAttemptCount == 0,
            "65th expedited copy fails closed and retires receipts; physical suppression separately source/full-DLL tested");

        var bounded = new NetConnection(endpoint, 303);
        for (int eventIndex = 0; eventIndex < NetReliableChannel.Capacity; eventIndex++)
        {
            bounded.Reliable.TryQueue(PacketType.Welcome, new byte[] { (byte)eventIndex }, 0, out var eventId);
            for (int copy = 0; copy < NetReliableChannel.MaximumReceiptsPerEvent; copy++)
                bounded.Send(PacketType.Welcome, copy, NetHeaderFlags.Reliable, eventId);
        }
        Check(!bounded.Reliable.Failed && bounded.Reliable.RetainedAttemptCount == 2560,
            "40existing pending events x64 receipts is the exact hard bound");
        bounded.Reliable.Acknowledge(1);
        Check(bounded.Reliable.Capture(66).Pending == 39 && bounded.Reliable.RetainedAttemptCount == 2496,
            "original public EventId ACK retires exactly its receipt set");

        var state = new NetConnection(endpoint, 304);
        state.Reliable.TryQueue(PacketType.SessionState, new byte[] { 1 }, 0, out var oldId);
        state.Send(PacketType.SessionState, 0, NetHeaderFlags.Reliable, oldId);
        state.Reliable.TryQueue(PacketType.SessionState, new byte[] { 2 }, 1, out var newId, supersedeState: true);
        Check(oldId != newId && state.Reliable.RetainedAttemptCount == 0 && state.Reliable.Capture(1).Pending == 1,
            "full-state supersession retires old receipts without retiring replacement");
        Check(new NetConnection(endpoint, 305).Reliable.RetainedAttemptCount == 0,
            "new connection owner starts without prior receipts");
        Console.WriteLine($"{(failures == 0 ? "PASS" : "FAIL")} receipt bounds/RTT contracts {checks} checks, {failures} failures");
        return failures == 0 ? 0 : 1;
    }
}
