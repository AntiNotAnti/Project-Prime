using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using MphRead.Mods.Network;

internal static class UdpSaturation
{
    private static int checks;
    private static void Check(bool ok, string name)
    { if (!ok) throw new InvalidOperationException(name); checks++; }
    internal static int Run()
    {
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        using var server = new NetTransport(0);
        using var sink = new UdpClient(0);
        sink.Client.ReceiveBufferSize = 256 * 1024;
        sink.Client.ReceiveTimeout = 2000;
        var remote = new IPEndPoint(IPAddress.Loopback, ((IPEndPoint)sink.Client.LocalEndPoint!).Port);
        // Existing transport fixtures use this same maintenance boundary. No
        // game clock, authority, admission, or production test flag is altered.
        typeof(NetTransport).GetField("_nextConnectionService", hidden)!.SetValue(server,
            Stopwatch.GetTimestamp() + 30 * Stopwatch.Frequency);
        server.Send(remote, PacketType.Welcome, new byte[17]);
        var from = new IPEndPoint(IPAddress.Any, 0);
        byte[] welcome = sink.Receive(ref from);
        Check(NetHeader.TryRead(welcome, out var initial) && initial.Type == PacketType.Welcome,
            "actual socket receives existing transport incarnation bootstrap");
        Check(server.ReliableStats(remote) is { Pending: 1, Failed: false }, "unacknowledged bootstrap remains owned");
        byte[] payload = { 1 };
        for (int i = 0; i < 22; i++) server.Send(remote, PacketType.TimedGrant, payload, immediateCopies: 3);
        Check(server.ReliableStats(remote) is { Failed: true }, "expedited65th receipt fails existing reliable channel");
        int copies = 0; uint? eventId = null;
        while (sink.Available > 0)
        {
            byte[] data = sink.Receive(ref from);
            Check(NetHeader.TryRead(data, out var header) && header.Type == PacketType.TimedGrant
                && header.ConnectionId == initial.ConnectionId && header.Flags.HasFlag(NetHeaderFlags.Reliable),
                "physical copy retains unchanged current reliable wire identity");
            uint id = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(NetHeader.Size));
            eventId ??= id;
            Check(id == eventId && data.AsSpan(NetHeader.Size + 4).SequenceEqual(payload),
                "all physical copies retain one application identity and exact payload");
            copies++;
        }
        Check(copies == 64, "actual physical send count stops at64;65th attempt is suppressed");
        Check(server.Telemetry.Capture().PacketsSent == 65, "wire telemetry counts bootstrap plus64 actual copies only");
        typeof(NetTransport).GetMethod("ServiceConnections", hidden)!.Invoke(server, new object[] { true });
        int failures = 0;
        foreach (var packet in server.Drain())
            if (packet.Type == PacketType.Bye && packet.Sender.Equals(remote)) failures++;
        Check(failures == 1, "existing connection-failure boundary reports one local Bye");
        typeof(NetTransport).GetMethod("ServiceConnections", hidden)!.Invoke(server, new object[] { true });
        foreach (var packet in server.Drain()) if (packet.Type == PacketType.Bye) failures++;
        Check(failures == 1 && sink.Available == 0, "failed owner cannot send another copy or duplicate failure");
        Console.WriteLine($"PASS actual UDP saturation {checks} checks,64 physical copies, one bounded failure; ephemeral ports, no session/world/assets/GL/backend");
        return 0;
    }
}
