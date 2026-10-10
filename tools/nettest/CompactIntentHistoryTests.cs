using System;
using System.Collections.Generic;
using MphRead.Mods.Network;

namespace MphRead.NetTest;
internal static class CompactIntentHistoryTests
{
    private static FireEvent Fire(uint id, uint frame) => new(id, frame, frame, 0,
        FireEventKind.PressFire, 0, 0, 0);
    private static IntentPacket Carrier(uint frame, params FireEvent[] events)
    {
        var result = new IntentPacket { Frame = frame, HasFireEvents = true, FireEventCount = (byte)events.Length };
        for (int i = 0; i < events.Length; i++) result.FireEvents[i] = events[i];
        return result;
    }
    public static int Run()
    {
        RetentionAndIdentity(); AtomicAdmission(); Wrap(); SustainedDoubleEvents(); Allocation();
        foreach (int rtt in new[] { 0, 100, 250, 400 })
            foreach (int loss in new[] { 0, 1, 2, 5, 10 }) Campaign(rtt, loss);
        return 0;
    }
    private static void RetentionAndIdentity()
    {
        var history = new NetIncomingFireHistory();
        Check(history.Accept(Carrier(10, Fire(2, 10))), "first event admitted");
        history.Consume(2);
        Check(history.Accept(Carrier(9, Fire(1, 9))), "older event survives newer consumed ID");
        Check(history.Accept(Carrier(11)) && history.TryPeek(out var old) && old.ShotId == 1,
            "newest empty carrier retains earlier unconsumed event");
        history.Consume(1);
        Check(history.Accept(Carrier(10, Fire(2, 10))) && history.Pending == 0,
            "consumed newer repeat stays consumed");
        history.Clear();
        Check(history.Accept(Carrier(10, Fire(2, 10))) && history.Pending == 1,
            "identity owner clear removes consumed fence history");
        Check(history.Accept(Carrier(74)) && history.Pending == 1, "source age64 remains retained");
        Check(history.Accept(Carrier(75)) && history.Pending == 0 && history.ExpiredPending == 1,
            "source age65 expires explicitly");
        var outgoing = new NetOutgoingFireHistory();
        Check(outgoing.Add(Fire(1, 1)) && outgoing.Add(Fire(2, 1)), "two sameframe events authored");
        var packet = Carrier(1); outgoing.Fill(ref packet);
        Check(packet.FireEventCount == 2 && packet.FireEvents[0].ShotId == 1 && packet.FireEvents[1].ShotId == 2,
            "two sameframe events retain distinct exact identities");
    }
    private static void AtomicAdmission()
    {
        var history = new NetIncomingFireHistory();
        Check(history.Accept(Carrier(1, Fire(3, 1))), "atomic baseline");
        var conflict = Fire(3, 1) with { Charge = 1 };
        Check(!history.Accept(Carrier(1, Fire(2, 1), conflict)) && history.Pending == 1 && history.Conflicts == 1,
            "conflicting carrier cannot partially admit preceding event");
        Check(history.TryPeek(out var conflictOriginal) && conflictOriginal.ShotId == 3 && conflictOriginal.Charge == 0,
            "conflict rejection preserves previously retained bytes");
        history.Clear();
        Check(history.Accept(Carrier(1, Fire(1, 1))), "overflow baseline");
        Check(!history.Accept(Carrier(1, Fire(129, 1))) && history.Pending == 1 && history.Overflows == 1,
            "ring collision overflow preserves existing pending event");
        Check(history.TryPeek(out var retained) && retained.ShotId == 1 && retained.Charge == 0,
            "failed atomic admission preserves original bytes");
        history.Clear();
        for (uint id = 1; id <= NetIncomingFireHistory.Capacity; id++)
            Check(history.Accept(Carrier(1, Fire(id, 1))), "all bounded incoming cells admit");
        Check(history.Pending == 128 && !history.Accept(Carrier(1, Fire(129, 1))) && history.Pending == 128,
            "incoming history never exceeds128 cells");
        var outgoing = new NetOutgoingFireHistory();
        for (uint id = 1; id <= NetOutgoingFireHistory.Capacity; id++)
            Check(outgoing.Add(Fire(id, 1)), "bounded outgoing cells admit");
        Check(!outgoing.Add(Fire((uint)NetOutgoingFireHistory.Capacity + 1, 1)) && outgoing.Overflows == 1,
            "outgoing overflow rejects without overwriting existing events");
        var offered = new HashSet<uint>();
        for (int tick = 0; tick < NetOutgoingFireHistory.Capacity; tick++)
        {
            var packet = Carrier(1); outgoing.Fill(ref packet);
            for (int i = 0; i < packet.FireEventCount; i++) offered.Add(packet.FireEvents[i].ShotId);
        }
        Check(offered.Count == NetOutgoingFireHistory.Capacity && !offered.Contains((uint)NetOutgoingFireHistory.Capacity + 1),
            "outgoing overflow preserves every existing identity and rotation is fair");
    }
    private static void Wrap()
    {
        var incoming = new NetIncomingFireHistory(); var outgoing = new NetOutgoingFireHistory();
        var a = Fire(uint.MaxValue, uint.MaxValue); var b = Fire(1, 0);
        Check(outgoing.Add(a) && outgoing.Add(b), "outgoing frame and ShotId wrap admitted");
        var packet = Carrier(0); outgoing.Fill(ref packet);
        Check(NetFireEvents.Validate(packet) && packet.FireEvents[0].ShotId == uint.MaxValue
            && packet.FireEvents[1].ShotId == 1, "wire wrap preserves serial order");
        Check(incoming.Accept(Carrier(0, b)), "new wrapped event admitted"); incoming.Consume(1);
        Check(incoming.Accept(Carrier(uint.MaxValue, a)) && incoming.TryPeek(out var old) && old.ShotId == uint.MaxValue,
            "delayed prewrap event recovers after postwrap consumption");
        incoming.Consume(uint.MaxValue);
        Check(incoming.Accept(packet) && incoming.Pending == 0, "wrapped repeats do not execute twice");
    }
    private static void SustainedDoubleEvents()
    {
        var outgoing = new NetOutgoingFireHistory(); var offered = new Dictionary<uint, int>();
        uint id = 0;
        for (uint frame = 0; frame < 664; frame++)
        {
            if (frame < 600) for (int shot = 0; shot < 2; shot++)
                Check(outgoing.Add(Fire(++id, frame)), "twoevents perframe fit retained outgoing capacity");
            var packet = new IntentPacket { Frame = frame }; outgoing.Fill(ref packet);
            Check(NetFireEvents.Validate(packet), "sustained doubleevent carrier remains ordered and valid");
            for (int i = 0; i < packet.FireEventCount; i++)
            {
                uint key = packet.FireEvents[i].ShotId;
                offered[key] = offered.TryGetValue(key, out int count) ? count + 1 : 1;
            }
        }
        Check(offered.Count == 1200 && outgoing.Overflows == 0 && outgoing.UnsentExpired == 0,
            "sustained twoevents perframe offers every exact event without overflow");
        foreach (int count in offered.Values) Check(count >= 2,
            "rotating history repeats every event before its source horizon expires");
    }
    private static void Allocation()
    {
        var outgoing = new NetOutgoingFireHistory(); var incoming = new NetIncomingFireHistory();
        for (int i = 0; i < 200; i++) Step(i);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 200; i < 2200; i++) Step(i);
        Check(GC.GetAllocatedBytesForCurrentThread() == before, "warmed history add/fill/admit/consume allocates zero bytes");
        void Step(int tick)
        {
            var fire = Fire((uint)tick + 1, (uint)tick);
            outgoing.Add(fire);
            var packet = new IntentPacket { Frame = (uint)tick }; outgoing.Fill(ref packet);
            incoming.Accept(packet);
            while (incoming.TryPeek(out var next)) incoming.Consume(next.ShotId);
        }
    }
    private static void Campaign(int rtt, int loss)
    {
        // Virtual packets, never a claim of real UDP or real wall-clock impairment.
        var random = new Random(73013 + rtt * 31 + loss);
        var outgoing = new NetOutgoingFireHistory(); var incoming = new NetIncomingFireHistory();
        var scheduled = new List<(int Due, IntentPacket Packet)>();
        var expected = new Dictionary<uint, FireEvent>(); var observed = new Dictionary<uint, FireEvent>();
        uint id = 0;
        const int authoredFrames = 600;
        for (int frame = 0; frame < authoredFrames + 120; frame++)
        {
            if (frame < authoredFrames)
            {
                Author(); if (frame % 90 == 30) Author();
            }
            var packet = new IntentPacket { Frame = (uint)frame }; outgoing.Fill(ref packet);
            Check(NetFireEvents.Validate(packet), "virtual carrier validates");
            if (random.Next(100) >= loss)
            {
                int due = frame + (int)Math.Ceiling(rtt * 60.0 / 2000) + (random.Next(5) == 0 ? random.Next(1, 5) : 0);
                scheduled.Add((due, packet));
                if (random.Next(7) == 0) scheduled.Add((due + random.Next(1, 4), packet));
            }
            // A250ms receiving stall buffers carriers without simulating/consuming.
            if (frame is >= 120 and < 135) continue;
            for (int index = scheduled.Count - 1; index >= 0; index--)
            {
                var delivery = scheduled[index]; if (delivery.Due > frame) continue;
                incoming.Accept(delivery.Packet); scheduled.RemoveAt(index);
            }
            while (incoming.TryPeek(out var fire))
            {
                Check(!observed.ContainsKey(fire.ShotId), "virtual recovery never consumes a duplicate event");
                observed.Add(fire.ShotId, fire); incoming.Consume(fire.ShotId);
            }
            void Author()
            {
                var fire = Fire(++id, (uint)frame); expected.Add(id, fire);
                Check(outgoing.Add(fire), "sustained60Hz plus sameframe double event does not overflow");
            }
        }
        Check(outgoing.Overflows == 0 && outgoing.UnsentExpired == 0 && incoming.Overflows == 0
            && incoming.Conflicts == 0, "virtual campaign retains bounded event history without silent overflow");
        Check(expected.Count == observed.Count, $"virtual RTT{rtt} loss{loss}% recovers every authored event ({observed.Count}/{expected.Count})");
        foreach (var pair in expected) Check(observed.TryGetValue(pair.Key, out var fire) && fire == pair.Value,
            "virtual recovered event preserves exact authored metadata");
        Console.WriteLine($"VIRTUAL history RTT={rtt}ms loss={loss}% events={observed.Count}/{expected.Count} pending={incoming.Pending}");
    }
    private static void Check(bool condition, string message) => NetArchitectureTests.Check(condition, message);
}
