using MphRead.Mods.MapGen;
using MphRead.Mods.Network;
using Xunit;

namespace ProjectPrime.Server.Shared.Tests;

public sealed class ReplayMapIdentityTests
{
    [Fact]
    public void CustomMapIdentityRoundTripsWithoutRuntimeIdOrArtifactPath()
    {
        var content = new MapContentIdentity(
            new MapIdentity("community.parallax", new MapVersion(1, 4, 0)),
            new string('a', 64));
        var expected = new ReplayMapIdentity("PARALLAX", content,
            new string('b', 64));

        byte[] record = ReplayMapIdentityCodec.WriteRecord(expected);

        Assert.True(ReplayMapIdentityCodec.TryReadRecord(record,
            out ReplayMapIdentity? actual));
        Assert.Equal(expected, actual);
        RoomContentRequirement requirement =
            Assert.IsType<RoomContentRequirement>(
                actual!.ToRoomContentRequirement());
        Assert.Equal(content, requirement.ContentIdentity);
        Assert.Null(requirement.ArtifactHash);
        Assert.Null(requirement.PackageSize);
        Assert.Equal(expected.MatchContentHash,
            requirement.MatchContentHash);
    }

    [Fact]
    public void BaseRoomIdentityRoundTripsWithoutInventingCustomContent()
    {
        var expected = new ReplayMapIdentity("MP1 SANCTORUS", null, null);

        byte[] record = ReplayMapIdentityCodec.WriteRecord(expected);

        Assert.True(ReplayMapIdentityCodec.TryReadRecord(record,
            out ReplayMapIdentity? actual));
        Assert.Equal(expected, actual);
        Assert.Null(actual!.ToRoomContentRequirement());
    }

    [Fact]
    public void MalformedOrIncoherentRecordsFailSafely()
    {
        var identity = new ReplayMapIdentity("MP1 SANCTORUS", null, null);
        byte[] record = ReplayMapIdentityCodec.WriteRecord(identity);

        record[1] = 99;
        Assert.False(ReplayMapIdentityCodec.TryReadRecord(record, out _));

        record = ReplayMapIdentityCodec.WriteRecord(identity);
        record[2] = 1;
        Assert.False(ReplayMapIdentityCodec.TryReadRecord(record, out _));

        Assert.False(ReplayMapIdentityCodec.TryReadRecord(
            [(byte)ReplayRecordKind.MapIdentity, 1, 0, 255], out _));
    }
}
