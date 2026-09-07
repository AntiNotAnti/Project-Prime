using System;
using System.Linq;
using MphRead.Mods.Launcher;
using MphRead.Mods.Network;
using Xunit;
namespace MphRead.Tests.Client;
public class ServerBrowserTests
{
    private static int IdentityV2TailOffset() => ServerStatusPacket.IdentityV2Size - ServerStatusPacket.Size;

    private static byte[] BaseStatus(int length)
    {
        var status = new ServerStatusPacket
        {
            Match = new MatchStatePacket { Mode = (byte)GameMode.Battle,
                RoomKey = "ROOM", NextRoomKey = "ROOM", PlayerCount = 1 },
            MaxPlayers = 8, Protocol = NetHeader.Version, ServerName = "TEST", HasRules = true
        };
        byte[] bytes = new byte[length];
        status.Write(bytes);
        return bytes;
    }

    private static ServerBrowserEntry Entry(string address, int ping, int players, int protocol = NetHeader.Version)
        => new(new MasterListing { Address = address, Port = 27015 })
        { Status = new ServerStatus { Online = true, Protocol = protocol, Family = NetWireFamily.Authoritative,
            Mode = GameMode.Battle, Players = players, MaxPlayers = 8, Latency = ping } };
    [Fact]
    public void BotFilledServersRemainJoinableAndBotCountIsValidated()
    {
        byte[] bytes = BaseStatus(ServerStatusPacket.ExtendedSize);
        bytes[ServerStatusPacket.IdentityV2Size + 5] = 1;
        Assert.True(ServerStatusPacket.TryRead(bytes, out var packet));
        Assert.Equal(1, packet.Bots);
        bytes[ServerStatusPacket.IdentityV2Size + 5] = 2;
        Assert.False(ServerStatusPacket.TryRead(bytes, out _));
        var entry = Entry("bots", 20, 8);
        entry.Status = new ServerStatus { Online = true, Protocol = NetHeader.Version,
            Family = NetWireFamily.Authoritative, Mode = GameMode.Battle,
            Players = 8, Bots = 7, MaxPlayers = 8, Latency = 20 };
        Assert.False(ServerBrowser.Full(entry.Status));
        Assert.Same(entry, ServerBrowser.QuickJoin(new[] { entry }, GameMode.Battle));
    }
    [Fact]
    public void QuickJoinExcludesFullIncompatibleAndUnknownPingThenPrefersPopulated()
    {
        var populated = Entry("populated", 35, 3);
        var entries = new[] { Entry("full", 1, 8), Entry("old", 1, 3, 0), Entry("unknown", -1, 3), Entry("empty", 10, 0), populated };
        Assert.Same(populated, ServerBrowser.QuickJoin(entries, GameMode.Battle));
        var filter = new ServerBrowserFilter(null, true, true, 40, ServerSort.Population, ServerGroup.All);
        Assert.Equal(new[] { "populated", "empty" }, ServerBrowser.Select(entries, filter, new()).Select(e => e.Listing.Address));
    }
    [Fact]
    public void PreferencesAreBoundedAndRecentOrderIsUnique()
    {
        var prefs = new ServerBrowserPreferences();
        for (int i = 0; i < 50; i++) { prefs.ToggleFavorite($"host{i}:1"); prefs.Visited($"host{i}:1"); }
        Assert.Equal(32, prefs.Favorites.Count);
        Assert.Equal(16, prefs.Recent.Count);
        prefs.Visited("host49:1");
        Assert.Equal(16, prefs.Recent.Count);
        Assert.Equal("host49:1", prefs.Recent[0]);
    }
    [Fact]
    public void DiscoveryExtensionPreservesBaseAndRejectsMalformedTail()
    {
        var packet = new ServerStatusPacket { Match = new MatchStatePacket { Mode = (byte)GameMode.Battle,
            RoomKey = "ROOM", NextRoomKey = "ROOM", PlayerCount = 2 }, MaxPlayers = 8, Protocol = NetHeader.Version,
            HasRules = true, FriendlyFire = true, PlayerRadar = true, SpawnPolicy = (SpawnPolicy)2,
            OvertimePolicy = (OvertimePolicy)1, LateJoinPolicy = (LateJoinPolicy)2 };
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
            var bad = (byte[])bytes.Clone(); bad[ServerStatusPacket.Size + offset] = (byte)value;
            Assert.False(ServerStatusPacket.TryRead(bad, out _));
        }
    }

    [Fact]
    public void RulesDiscoveryReadsBackwardCompatibleV1V2AndCurrentV3Tails()
    {
        Guid serverId = Guid.NewGuid();
        ServerStatusPacket current = new()
        {
            Match = new MatchStatePacket { Mode = (byte)GameMode.Battle,
                RoomKey = "ROOM", NextRoomKey = "ROOM", PlayerCount = 2 },
            MaxPlayers = 8, Protocol = NetHeader.Version, ServerName = "TEST",
            HasRules = true, ServerId = serverId, RequiresTicket = true,
            FriendlyFire = true, PlayerRadar = true, SpawnPolicy = SpawnPolicy.Enhanced,
            OvertimePolicy = OvertimePolicy.ModeDefault, LateJoinPolicy = LateJoinPolicy.SpectateUntilNextMatch,
            RulesetPreset = RulesetPreset.Competitive, Observers = 1, MaxObservers = 4,
            ObserverDelaySeconds = 3, RankingEligibility = RankingEligibility.VerifiedServerOnly
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
            Match = new MatchStatePacket { Mode = (byte)GameMode.Battle,
                RoomKey = "ROOM", NextRoomKey = "ROOM", PlayerCount = 1 },
            MaxPlayers = 8, Protocol = NetHeader.Version, ServerName = "TEST", HasRules = true,
            RulesetPreset = RulesetPreset.Competitive, MaxObservers = 4,
            RankingEligibility = RankingEligibility.VerifiedServerOnly
        }.Write(v3);

        foreach ((int offset, byte value) in new[]
        {
            (0, (byte)4), // tail version
            (1, (byte)4), // flags
            (2, (byte)3), // SpawnPolicy
            (3, (byte)2), // OvertimePolicy
            (4, (byte)3), // LateJoinPolicy
            (5, (byte)2), // RequiresTicket
            (6, (byte)1), // v3 reserved byte
            (7, (byte)1), // v3 reserved byte
            (IdentityV2TailOffset(), (byte)4), // RulesetPreset
            (IdentityV2TailOffset() + 2, (byte)17), // MaxObservers
            (IdentityV2TailOffset() + 3, (byte)31), // observer delay
            (IdentityV2TailOffset() + 4, (byte)2), // RankingEligibility
            (IdentityV2TailOffset() + 5, (byte)3) // bots exceed total players
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
