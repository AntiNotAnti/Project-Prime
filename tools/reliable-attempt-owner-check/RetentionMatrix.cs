using System.Net;
using System.Reflection;
using MphRead.Mods.Network;

internal static class RetentionMatrix
{
    private static int checks, failures;
    private static void Check(bool ok, string label)
    { checks++; if (!ok) { failures++; Console.Error.WriteLine("FAIL " + label); } }
    internal static int Run()
    {
        foreach (uint initial in new[] { 0u, uint.MaxValue - 1 })
            foreach (int fanout in new[] { 1, 8 })
                foreach (int traffic in new[] { 511, 512, 1024 })
                    LateAck(initial, fanout, traffic);
        LostAckRetry(); Supersession(); ReplacementAndBounds();
        Console.WriteLine($"{(failures == 0 ? "PASS" : "FAIL")} extended actual owner retention contracts {checks} checks, {failures} failures; controlled time/headers, no UDP/assets/GL/backend/gameplay");
        return failures == 0 ? 0 : 1;
    }
    private static void Flood(NetConnection sender, int count, double start, double end)
    {
        for (int i = 0; i < count; i++) sender.Send((i % 3) switch
        { 0 => PacketType.Snapshot, 1 => PacketType.SlotIntent, _ => PacketType.BombState },
            start + (end - start) * (i + 1) / (count + 1));
    }
    private static ReliableTransmission Queue(NetConnection sender, double now, PacketType type = PacketType.TimedGrant,
        byte payload = 1, bool supersede = false)
    {
        Check(sender.Reliable.TryQueue(type, new byte[] { payload }, now, out _, supersedeState: supersede), "real pending owner admits controlled event");
        Check(sender.Reliable.TrySend(now, out var transmission), "real retry owner returns event");
        return transmission;
    }
    private static NetHeader Deliver(NetConnection sender, NetConnection receiver, ReliableTransmission transmission, double now)
    {
        var header = sender.Send(transmission.Type, now, NetHeaderFlags.Reliable, transmission.EventId);
        receiver.Receive(header, now + 400);
        receiver.Reliable.Receive(transmission.EventId);
        var ack = receiver.Send(0, now + 400, NetHeaderFlags.AckOnly);
        Check(NetReceiveWindow.Acknowledges(header.Sequence, ack.Ack, ack.AckBits), "real exact covering ACK");
        return ack;
    }
    private static void LateAck(uint initial, int fanout, int traffic)
    {
        for (int peer = 0; peer < fanout; peer++)
        {
            var endpoint = new IPEndPoint(IPAddress.Loopback, 32200 + peer);
            var sender = new NetConnection(endpoint, (ulong)(100 + peer), initialSequence: initial);
            var receiver = new NetConnection(endpoint, (ulong)(100 + peer));
            var eventPacket = Queue(sender, 0);
            var ack1 = Deliver(sender, receiver, eventPacket, 0);
            var ack2 = Deliver(sender, receiver, eventPacket, 0);
            var ack3 = Deliver(sender, receiver, eventPacket, 0);
            // Three initial copies are the existing transport maximum. The
            // first ACK is lost; the remaining two arrive in reverse order.
            Flood(sender, traffic, 1, 799);
            sender.Receive(ack3, 800); sender.Receive(ack2, 810);
            Check(receiver.Reliable.Capture(810).Duplicates == 2, "copies apply actual event exactly once");
            Check(sender.Reliable.Capture(810).Pending == 0,
                $"covering lost/reordered ACK survives traffic={traffic} initial={initial} fanout={fanout} peer={peer}");
            long acknowledged = sender.Capture().Acknowledged;
            sender.Receive(ack1, 820);
            Check(sender.Reliable.Capture(820).Pending == 0 && sender.Capture().Acknowledged >= acknowledged,
                "late first ACK cannot reopen settled event");
        }
    }
    private static void LostAckRetry()
    {
        var endpoint = new IPEndPoint(IPAddress.Loopback, 32300);
        var sender = new NetConnection(endpoint, 200); var receiver = new NetConnection(endpoint, 200);
        var original = Queue(sender, 0); var lost = Deliver(sender, receiver, original, 0);
        Flood(sender, 600, 1, 799);
        Check(sender.Reliable.TrySend(800, out var retry) && retry.EventId == original.EventId,
            "lost ACK retries same actual application identity");
        var returned = Deliver(sender, receiver, retry, 800);
        Flood(sender, 600, 801, 1599); sender.Receive(returned, 1600);
        Check(sender.Reliable.Capture(1600).Pending == 0 && receiver.Reliable.Capture(1600).Duplicates == 1,
            "retry's late ACK settles once after another packet-ring wrap");
        sender.Receive(lost, 1610);
        Check(sender.Reliable.Capture(1610).Pending == 0, "original reordered ACK cannot recreate pending event");
    }
    private static void Supersession()
    {
        var endpoint = new IPEndPoint(IPAddress.Loopback, 32301);
        var sender = new NetConnection(endpoint, 201); var receiver = new NetConnection(endpoint, 201);
        var old = Queue(sender, 0, PacketType.SessionState, 1); var staleAck = Deliver(sender, receiver, old, 0);
        Flood(sender, 600, 1, 799);
        var replacement = Queue(sender, 800, PacketType.SessionState, 2, supersede: true);
        var currentAck = Deliver(sender, receiver, replacement, 800);
        sender.Receive(staleAck, 810);
        Check(replacement.EventId != old.EventId && sender.Reliable.Capture(810).Pending == 1,
            "obsolete full-state ACK cannot settle replacement event");
        sender.Receive(currentAck, 1600);
        Check(sender.Reliable.Capture(1600).Pending == 0, "replacement exact ACK settles normally");
    }
    private static void ReplacementAndBounds()
    {
        var endpoint = new IPEndPoint(IPAddress.Loopback, 32302);
        var sender = new NetConnection(endpoint, 202); var receiver = new NetConnection(endpoint, 202);
        var item = Queue(sender, 0); var oldAck = Deliver(sender, receiver, item, 0);
        var replacement = new NetConnection(endpoint, 203);
        Check(!replacement.Accepts(endpoint, oldAck), "new connection incarnation rejects old ACK");
        bool threw = false; try { replacement.Receive(oldAck, 800); } catch (ArgumentException) { threw = true; }
        Check(threw && replacement.Capture().Acknowledged == 0, "actual replacement owner cannot consume retired ACK");
        Check(!sender.Accepts(new IPEndPoint(IPAddress.Loopback, 32303), oldAck), "endpoint identity fence unchanged");
        Check(((Array)typeof(NetConnection).GetField("_attempts", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(sender)!).Length == 512,
            "general telemetry ring retains existing bounded512 contract");
        var bounded = new NetReliableChannel();
        for (int i = 0; i < NetReliableChannel.Capacity; i++)
            Check(bounded.TryQueue(PacketType.Welcome, new byte[] { (byte)i }, 0, out _), "existing critical event capacity");
        Check(!bounded.TryQueue(PacketType.Welcome, new byte[] { 255 }, 0, out _) && bounded.Failed
            && bounded.Capture(0).Pending == NetReliableChannel.Capacity, "event pressure still bounded and fails closed");
        var deadline = new NetReliableChannel(); deadline.TryQueue(PacketType.TimedGrant, new byte[] { 1 }, 0, out _);
        Check(!deadline.TrySend(NetReliableChannel.LifetimeMilliseconds, out _) && deadline.Failed
            && NetReliableChannel.LifetimeMilliseconds == 15000, "unacknowledged lifetime still expires at15seconds");
    }
}
