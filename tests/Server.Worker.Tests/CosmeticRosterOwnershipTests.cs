using System.Net;
using System.Reflection;
using MphRead;
using MphRead.Cosmetics;
using MphRead.Mods.Network;
using Xunit;

namespace ProjectPrime.Server.Worker.Tests;

public sealed class CosmeticRosterOwnershipTests
{
    [Fact]
    public void ReconnectingHumanPublishesFrozenSlotLoadout()
    {
        using var transport = new SilentTransport();
        var network = new ServerNetwork(transport,
            new MatchRules(MatchMode.Battle, "unit", maxPlayers: 1));
        CosmeticLoadoutIds frozen = new(1, 2, 3);
        network.FrozenRosterCosmetics = slot => slot == 0 ? frozen : default;
        var endpoint = new IPEndPoint(IPAddress.Loopback, 40000);
        var first = new JoinPacket
        {
            Protocol = NetHeader.Version,
            Name = "Player",
            Hunter = Hunter.Samus,
            Nonce = 1
        };
        Assert.True(network.SubmitLegacyJoinForTesting(endpoint, first));
        NetRosterEntry initial = Assert.Single(PublishAndRead(network));
        Assert.Equal(frozen, new CosmeticLoadoutIds(initial.SkinId,
            initial.ArmorEffectId, initial.DeathEffectId));

        var reconnect = first with
        {
            Nonce = 2,
            PreviousConnectionId = network.Peers[0]!.Connection.Id
        };
        Assert.True(network.SubmitLegacyJoinForTesting(endpoint, reconnect));
        NetRosterEntry restored = Assert.Single(PublishAndRead(network));
        Assert.NotEqual(initial.ConnectionId, restored.ConnectionId);
        Assert.Equal(frozen, new CosmeticLoadoutIds(restored.SkinId,
            restored.ArmorEffectId, restored.DeathEffectId));
    }

    [Fact]
    public void BotRosterAlwaysPublishesDefaultCosmetics()
    {
        using var transport = new SilentTransport();
        var network = new ServerNetwork(transport,
            new MatchRules(MatchMode.Battle, "unit", maxPlayers: 1));
        network.BotRosterEntry = slot => new NetRosterEntry((byte)slot, 99,
            Hunter.Samus, 0, "BOT", 0, true, 1, 2, 3);

        NetRosterEntry bot = Assert.Single(PublishAndRead(network));

        Assert.True(bot.IsBot);
        Assert.Equal(CosmeticLoadoutIds.Default, new CosmeticLoadoutIds(bot.SkinId,
            bot.ArmorEffectId, bot.DeathEffectId));
    }

    private static NetRosterEntry[] PublishAndRead(ServerNetwork network)
    {
        typeof(ServerNetwork).GetMethod("PublishRoster",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(network, null);
        byte[] payload = (byte[])typeof(ServerNetwork).GetField("_rosterPayload",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(network)!;
        int length = (int)typeof(ServerNetwork).GetField("_rosterLength",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(network)!;
        var entries = new NetRosterEntry[RosterPacket.MaxSlots];
        Assert.True(SessionRosterPacket.TryRead(payload.AsSpan(4, length - 4), entries,
            out _, out int count));
        return entries[..count];
    }

    private sealed class SilentTransport : INetTransport
    {
        public int LocalPort => 0;
        public long PacketsDropped => 0;
        public int QueuedPackets => 0;
        public int HeldIncomingPackets => 0;
        public int HeldOutgoingPackets => 0;
        public NetTrafficMetrics Metrics { get; } = new();
        public void Dispose() { }
        public void SetKeepAlive(IPEndPoint? target, ReadOnlySpan<byte> datagram = default) { }
        public void SetKeepAlives(ReadOnlySpan<NetKeepAlive> entries) { }
        public void AnswerPingsImmediately() { }
        public IEnumerable<ReceivedPacket> Drain() => [];
        public void EnqueueForPlayback(byte[] data, int length) => throw new NotSupportedException();
        public void Send(IPEndPoint target, PacketType type, ReadOnlySpan<byte> payload,
            long extraHoldTicks = 0) { }
        public void SendDatagram(IPEndPoint target, ReadOnlySpan<byte> datagram,
            long extraHoldTicks) { }
        public void SendDatagram(IPEndPoint target, ReadOnlySpan<byte> datagram) { }
    }
}
