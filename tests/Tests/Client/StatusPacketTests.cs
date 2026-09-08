using System;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests.Client;

public sealed class StatusPacketTests
{
    private static int IdentityV2TailOffset() => ServerStatusPacket.IdentityV2Size - ServerStatusPacket.Size;

    private static byte[] BaseStatus(int length)
    {
        var status = new ServerStatusPacket
        {
            Match = new MatchStatePacket
            {
                Mode = (byte)GameMode.Battle,
                RoomKey = "ROOM",
                NextRoomKey = "ROOM",
                PlayerCount = 1
            },
            MaxPlayers = 8,
            Protocol = NetHeader.Version,
            ServerName = "TEST",
            HasRules = true
        };
        byte[] bytes = new byte[length];
        status.Write(bytes);
        return bytes;
    }

    [Fact]
    public void DiscoveryExtensionPreservesBaseAndRejectsMalformedTail()
    {
        var packet = new ServerStatusPacket
        {
            Match = new MatchStatePacket
            {
                Mode = (byte)GameMode.Battle,
                RoomKey = "ROOM",
                NextRoomKey = "ROOM",
                PlayerCount = 2
            },
            MaxPlayers = 8,
            Protocol = NetHeader.Version,
            HasRules = true,
            FriendlyFire = true,
            PlayerRadar = true,
            SpawnPolicy = (SpawnPolicy)2,
            OvertimePolicy = (OvertimePolicy)1,
            LateJoinPolicy = (LateJoinPolicy)2
        };
        byte[] bytes = new byte[ServerStatusPacket.ExtendedSize];
        packet.Write(bytes);
        Assert.True(ServerStatusPacket.TryRead(bytes, out var read));
        Assert.True(read.HasRules && read.FriendlyFire && read.PlayerRadar);
        Assert.Equal(packet.SpawnPolicy, read.SpawnPolicy);
        Assert.True(ServerStatusPacket.TryRead(bytes.AsSpan(0, ServerStatusPacket.Size), out read));
        Assert.False(read.HasRules);
        Assert.True(ServerStatusPacket.TryRead(bytes.AsSpan(0, ServerStatusPacket.LegacySize), out _));
        for (int length = ServerStatusPacket.Size + 1; length < bytes.Length; length++)
            Assert.False(ServerStatusPacket.TryRead(bytes.AsSpan(0, length), out _));
        foreach (var (offset, value) in new[] { (0, 4), (1, 4), (2, 3), (3, 2), (4, 3), (5, 2), (6, 1), (7, 1) })
        {
            var bad = (byte[])bytes.Clone();
            bad[ServerStatusPacket.Size + offset] = (byte)value;
            Assert.False(ServerStatusPacket.TryRead(bad, out _));
        }
    }

    [Fact]
    public void RulesDiscoveryReadsBackwardCompatibleV1V2AndCurrentV3Tails()
    {
        Guid serverId = Guid.NewGuid();
        ServerStatusPacket current = new()
        {
            Match = new MatchStatePacket
            {
                Mode = (byte)GameMode.Battle,
                RoomKey = "ROOM",
                NextRoomKey = "ROOM",
                PlayerCount = 2
            },
            MaxPlayers = 8,
            Protocol = NetHeader.Version,
            ServerName = "TEST",
            HasRules = true,
            ServerId = serverId,
            RequiresTicket = true,
            FriendlyFire = true,
            PlayerRadar = true,
            SpawnPolicy = SpawnPolicy.Enhanced,
            OvertimePolicy = OvertimePolicy.ModeDefault,
            LateJoinPolicy = LateJoinPolicy.SpectateUntilNextMatch,
            RulesetPreset = RulesetPreset.Competitive,
            Observers = 1,
            MaxObservers = 4,
            ObserverDelaySeconds = 3,
            RankingEligibility = RankingEligibility.VerifiedServerOnly
        };

        byte[] v1 = BaseStatus(ServerStatusPacket.RulesV1Size);
        v1[ServerStatusPacket.Size] = 1;
        v1[ServerStatusPacket.Size + 1] = 3;
        v1[ServerStatusPacket.Size + 2] = (byte)current.SpawnPolicy;
        v1[ServerStatusPacket.Size + 3] = (byte)current.OvertimePolicy;
        v1[ServerStatusPacket.Size + 4] = (byte)current.LateJoinPolicy;
        Assert.True(ServerStatusPacket.TryRead(v1, out ServerStatusPacket decodedV1));
        Assert.True(decodedV1.HasRules);
        Assert.True(decodedV1.FriendlyFire && decodedV1.PlayerRadar);
        Assert.Equal(current.SpawnPolicy, decodedV1.SpawnPolicy);
        Assert.Equal(RulesetPreset.Classic, decodedV1.RulesetPreset);
        Assert.Equal(Guid.Empty, decodedV1.ServerId);
        Assert.False(decodedV1.RequiresTicket);

        byte[] v2 = BaseStatus(ServerStatusPacket.IdentityV2Size);
        v2[ServerStatusPacket.Size] = 2;
        v2[ServerStatusPacket.Size + 1] = 3;
        v2[ServerStatusPacket.Size + 2] = (byte)current.SpawnPolicy;
        v2[ServerStatusPacket.Size + 3] = (byte)current.OvertimePolicy;
        v2[ServerStatusPacket.Size + 4] = (byte)current.LateJoinPolicy;
        v2[ServerStatusPacket.Size + 5] = 1;
        serverId.TryWriteBytes(v2.AsSpan(ServerStatusPacket.RulesV1Size, 16));
        Assert.True(ServerStatusPacket.TryRead(v2, out ServerStatusPacket decodedV2));
        Assert.True(decodedV2.HasRules && decodedV2.FriendlyFire && decodedV2.PlayerRadar);
        Assert.Equal(current.SpawnPolicy, decodedV2.SpawnPolicy);
        Assert.Equal(serverId, decodedV2.ServerId);
        Assert.True(decodedV2.RequiresTicket);
        Assert.Equal(RulesetPreset.Classic, decodedV2.RulesetPreset);
        Assert.Equal(0, decodedV2.Observers);

        byte[] v3 = new byte[ServerStatusPacket.ExtendedSize];
        current.Write(v3);
        Assert.True(ServerStatusPacket.TryRead(v3, out ServerStatusPacket decodedV3));
        Assert.Equal(current.RulesetPreset, decodedV3.RulesetPreset);
        Assert.Equal(current.Observers, decodedV3.Observers);
        Assert.Equal(current.MaxObservers, decodedV3.MaxObservers);
        Assert.Equal(current.ObserverDelaySeconds, decodedV3.ObserverDelaySeconds);
        Assert.Equal(current.RankingEligibility, decodedV3.RankingEligibility);
        Assert.Equal(current.ServerId, decodedV3.ServerId);
        Assert.True(decodedV3.RequiresTicket);
    }

    [Fact]
    public void RulesDiscoveryRejectsMalformedVersionedTailsAndReservedBytes()
    {
        byte[] v3 = new byte[ServerStatusPacket.ExtendedSize];
        new ServerStatusPacket
        {
            Match = new MatchStatePacket
            {
                Mode = (byte)GameMode.Battle,
                RoomKey = "ROOM",
                NextRoomKey = "ROOM",
                PlayerCount = 1
            },
            MaxPlayers = 8,
            Protocol = NetHeader.Version,
            ServerName = "TEST",
            HasRules = true,
            RulesetPreset = RulesetPreset.Competitive,
            MaxObservers = 4,
            RankingEligibility = RankingEligibility.VerifiedServerOnly
        }.Write(v3);

        foreach ((int offset, byte value) in new[]
        {
            (0, (byte)4),
            (1, (byte)4),
            (2, (byte)3),
            (3, (byte)2),
            (4, (byte)3),
            (5, (byte)2),
            (6, (byte)1),
            (7, (byte)1),
            (IdentityV2TailOffset(), (byte)4),
            (IdentityV2TailOffset() + 2, (byte)17),
            (IdentityV2TailOffset() + 3, (byte)31),
            (IdentityV2TailOffset() + 4, (byte)2),
            (IdentityV2TailOffset() + 5, (byte)3)
        })
        {
            byte[] bad = (byte[])v3.Clone();
            bad[ServerStatusPacket.Size + offset] = value;
            Assert.False(ServerStatusPacket.TryRead(bad, out _), $"tail offset {offset} accepted {value}");
        }

        byte[] v1 = BaseStatus(ServerStatusPacket.RulesV1Size);
        v1[ServerStatusPacket.Size] = 1;
        foreach ((int relative, byte value) in new[]
        {
            (0, (byte)2), (1, (byte)4), (2, (byte)3), (3, (byte)2),
            (4, (byte)3), (5, (byte)1), (6, (byte)1), (7, (byte)1)
        })
        {
            byte[] bad = (byte[])v1.Clone();
            bad[ServerStatusPacket.Size + relative] = value;
            Assert.False(ServerStatusPacket.TryRead(bad, out _), $"v1 tail offset {relative} accepted {value}");
        }
    }
}
