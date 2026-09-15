using System;
using System.Buffers.Binary;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

public sealed class CosmeticRosterProtocolTests
{
    [Fact]
    public void Protocol19RosterCarriesSixPresentationBytesPerSeat()
    {
        Assert.Equal(25, NetHeader.Version);
        Assert.Equal(36, SessionRosterPacket.EntrySize);
        Assert.Equal(293, SessionRosterPacket.MaxSize);
        NetRosterEntry expected = new(7, 99, Hunter.Guardian, 3, "COSMETIC", 42, true,
            SkinId: 0x1234, ArmorEffectId: 0x5678, DeathEffectId: 0x9ABC);
        byte[] wire = new byte[SessionRosterPacket.MaxSize];
        int length = SessionRosterPacket.Write(wire, 8, [expected]);
        var entries = new NetRosterEntry[8];
        Assert.True(SessionRosterPacket.TryRead(wire.AsSpan(0, length), entries,
            out uint revision, out int count));
        Assert.Equal(8u, revision);
        Assert.Equal(1, count);
        Assert.Equal(expected, entries[0]);
    }

    [Fact]
    public void ProtocolEightThroughEighteenGoldenRosterDefaultsCosmetics()
    {
        // Frozen 30-byte protocol 8..18 roster entry, written independently of
        // the compatibility decoder: revision 7, slot 2, connection 44,
        // Trace, team 1, ASCII name HISTORY, ping 18, bot=true.
        byte[] wire =
        [
            0x07, 0x00, 0x00, 0x00, 0x01,
            0x02, 0x2C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0x01,
            0x48, 0x49, 0x53, 0x54, 0x4F, 0x52, 0x59, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x12, 0x00, 0x01
        ];
        var entries = new NetRosterEntry[8];
        Assert.Equal(Protocol18ReplayRoster.HeaderSize + Protocol18ReplayRoster.EntrySize,
            wire.Length);
        Assert.True(Protocol18ReplayRoster.TryRead(wire, entries,
            out uint revision, out int count));
        Assert.Equal(7u, revision);
        Assert.Equal(1, count);
        Assert.Equal(new NetRosterEntry(2, 44, Hunter.Trace, 1, "HISTORY", 18, true), entries[0]);
        Assert.Equal(0, entries[0].SkinId);
        Assert.Equal(0, entries[0].ArmorEffectId);
        Assert.Equal(0, entries[0].DeathEffectId);
        Assert.False(SessionRosterPacket.TryRead(wire, entries, out _, out _));
    }

    [Theory]
    [InlineData(8, false)]
    [InlineData(18, false)]
    [InlineData(19, true)]
    public void ReplayStateSelectsTheRosterCodecForItsRecordedProtocol(
        byte protocol, bool preservesCosmetics)
    {
        var state = new ModernReplayState();
        state.Reset(protocol);
        Assert.True(state.Receive(ReplayPlaybackTests.Match(42, protocol)));
        NetRosterEntry expected = new(3, 77, Hunter.Samus, 1, "REPLAY", 21,
            SkinId: 1, ArmorEffectId: 4, DeathEffectId: 2);
        byte[] record;
        if (preservesCosmetics)
        {
            byte[] body = new byte[4 + SessionRosterPacket.MaxSize];
            BinaryPrimitives.WriteUInt32LittleEndian(body, 42);
            int rosterSize = SessionRosterPacket.Write(body.AsSpan(4), 5, [expected]);
            record = ReplayPlaybackTests.Record(ReplayRecordKind.Roster,
                body.AsSpan(0, 4 + rosterSize));
        }
        else
        {
            // Frozen replay record: kind=Roster, match=42, revision=5, one
            // protocol-18 seat with slot=3, connection=77, Samus, team=1,
            // name=REPLAY, ping=21, bot=false.
            record =
            [
                0x04, 0x2A, 0x00, 0x00, 0x00, 0x05, 0x00, 0x00, 0x00, 0x01,
                0x03, 0x4D, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01,
                0x52, 0x45, 0x50, 0x4C, 0x41, 0x59, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x15, 0x00, 0x00
            ];
        }

        Assert.True(state.Receive(record));
        NetRosterEntry actual = Assert.Single(state.Roster.ToArray());
        Assert.Equal(expected with
        {
            SkinId = preservesCosmetics ? expected.SkinId : (ushort)0,
            ArmorEffectId = preservesCosmetics ? expected.ArmorEffectId : (ushort)0,
            DeathEffectId = preservesCosmetics ? expected.DeathEffectId : (ushort)0
        }, actual);
    }
}
