using System;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

public sealed class NetworkTimingProfileTests
{
    [Theory]
    [InlineData(NetworkTimingLevel.Excellent, 2, 1)]
    [InlineData(NetworkTimingLevel.Good, 3, 2)]
    [InlineData(NetworkTimingLevel.Normal, 4, 2)]
    [InlineData(NetworkTimingLevel.Unstable, 5, 3)]
    [InlineData(NetworkTimingLevel.Recovery, 6, 3)]
    public void ProfilesRoundTripWithBoundedValues(NetworkTimingLevel level,
        byte presentation, byte input)
    {
        NetworkTimingProfile profile = NetworkTimingProfile.Create(7, level);
        Assert.Equal(presentation, profile.PresentationDelayTicks);
        Assert.Equal(input, profile.InputPlayoutTicks);
        Span<byte> bytes = stackalloc byte[NetworkTimingProfilePacket.Size];
        NetworkTimingProfilePacket.Write(bytes, profile);
        Assert.True(NetworkTimingProfilePacket.TryRead(bytes, out NetworkTimingProfile decoded));
        Assert.Equal(profile, decoded);
    }

    [Fact]
    public void ProfileAndAcknowledgementRejectMalformedOrUnversionedValues()
    {
        Assert.False(NetworkTimingProfile.Compatibility.IsValid);
        Assert.False(NetworkTimingProfilePacket.TryRead(new byte[5], out _));
        Assert.False(NetworkTimingProfilePacket.TryRead(new byte[6], out _));
        byte[] invalidDelay = { 1, 0, 0, 0, 7, 2 };
        Assert.False(NetworkTimingProfilePacket.TryRead(invalidDelay, out _));
        Assert.False(NetworkTimingProfileAppliedPacket.TryRead(new byte[3], out _));
        Assert.False(NetworkTimingProfileAppliedPacket.TryRead(new byte[4], out _));
    }

    [Fact]
    public void TelemetryRoundTripsAndRejectsOutOfRangeFields()
    {
        var telemetry = new NetworkTimingTelemetry(9, 4, 60, 2, 3, 333, 75);
        Span<byte> bytes = stackalloc byte[NetworkTimingTelemetry.Size];
        telemetry.Write(bytes);
        Assert.True(NetworkTimingTelemetry.TryRead(bytes, out NetworkTimingTelemetry decoded));
        Assert.Equal(telemetry, decoded);
        bytes[4] = 1;
        Assert.False(NetworkTimingTelemetry.TryRead(bytes, out _));
        Assert.False(NetworkTimingTelemetry.TryRead(bytes[..^1], out _));
        Assert.False((telemetry with { PresentedFrames = 0 }).IsValid);
        Assert.True((telemetry with { PresentedFrames = 1, SnapshotUnderruns = 0, ExtrapolatedFrames = 0 }).IsValid);
        Assert.True((telemetry with { PresentedFrames = 29 }).IsValid);
        Assert.True((telemetry with { PresentedFrames = 30 }).IsValid);
        Assert.False((telemetry with { SnapshotUnderruns = 61 }).IsValid);
        Assert.False((telemetry with { ExtrapolatedFrames = 61 }).IsValid);
    }

    [Fact]
    public void CurrentProtocolRecognizesTheBoundedTimingDatagram()
    {
        Assert.Equal(16, NetHeader.Version);
        Span<byte> datagram = stackalloc byte[NetHeader.Size];
        new NetHeader(NetMessageType.TimingTelemetry, NetHeaderFlags.None, 1, 2, 0, 0)
            .Write(datagram);
        Assert.True(NetHeader.TryRead(datagram, out NetHeader decoded));
        Assert.Equal(NetMessageType.TimingTelemetry, decoded.Type);
    }
}
