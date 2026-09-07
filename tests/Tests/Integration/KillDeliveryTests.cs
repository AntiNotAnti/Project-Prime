using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Net;
using System.Threading;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

public sealed class KillDeliveryTests
{
    [Fact]
    public void StructuredKillPassesServerAdmissionReliableWireAndClientValidation()
    {
        using var transport = new NetTransport(0);
        using var socket = new NetTransport(0);
        var server = new ServerNetwork(transport, "MP1 SANCTORUS", GameMode.Battle);
        using var client = new NetClient(socket, new IPEndPoint(IPAddress.Loopback, transport.LocalPort), "KILL", Hunter.Samus);
        void Pump(Func<bool> complete)
        {
            var clock = Stopwatch.StartNew();
            while (!complete() && clock.ElapsedMilliseconds < 3000)
            {
                server.Poll((uint)(clock.Elapsed.TotalSeconds * 60));
                client.Poll();
                Thread.Sleep(1);
            }
            Assert.True(complete(), "Timed out waiting for loopback kill delivery.");
        }
        Pump(() => client.HasRoster);
        ServerPeer peer = server.Peers[client.Accepted.Slot]!;
        Assert.True(client.Ready(client.Accepted.MatchId));
        Pump(() => peer.Connection.State == NetConnectionState.Ready);
        peer.Connection.StartPlaying();
        var kill = new KillEvent(10, 100, server.MatchId, 1,
            new(1, 101, 1), new(0, peer.Connection.Id, 1), 4, KillEventFlags.Headshot,
            ImmutableArray.Create(new CombatActor(2, 102, 1)));
        byte[] bytes = new byte[KillEvent.Size]; kill.Write(bytes);
        Assert.True(server.TrySendEvent(peer, ReliableEventType.Kill, bytes));
        bool delivered = false;
        Pump(() =>
        {
            while (client.TryDequeueEvent(out NetApplicationEvent item))
            {
                if (item.Type != ReliableEventType.Kill) continue;
                Assert.Equal(server.MatchId, item.MatchId);
                Assert.True(KillEvent.TryRead(item.Payload.Span, out KillEvent received));
                Assert.Equal(kill.Id, received.Id);
                Assert.Equal(kill.Assists[0], received.Assists[0]);
                delivered = true;
            }
            return delivered;
        });
        var world = new WorldEvent(11, 101, server.MatchId, 1, WorldSubjectKind.Match,
            WorldSignalKind.PrimeChanged, 1, 0, kill.Killer, default);
        bytes = new byte[WorldEvent.Size]; world.Write(bytes);
        Assert.True(server.TryBroadcastEvent(ReliableEventType.WorldEvent, bytes));
        bool worldDelivered = false;
        Pump(() =>
        {
            while (client.TryDequeueEvent(out NetApplicationEvent item))
            {
                if (item.Type != ReliableEventType.WorldEvent) continue;
                Assert.True(WorldEvent.TryRead(item.Payload.Span, out var received));
                Assert.Equal(world, received); worldDelivered = true;
            }
            return worldDelivered;
        });
        Assert.Equal(1, server.Count);
    }
}
