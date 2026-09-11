using System;
using System.Buffers.Binary;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

public enum GeneratorFixtureKind : byte
{
    None = 0,
    Door = 1,
    Platform = 2
}

[NetPacket(NetMessageType.World, protocol: 9)]
public readonly partial record struct GeneratorFixturePacket(
    [property: NetRange(0, 100000)] int Sequence,
    bool Enabled,
    GeneratorFixtureKind Kind,
    Guid RequestId,
    [property: NetFinite] Vector3 Position,
    [property: NetArray(2)] ushort[] Values,
    [property: NetUtf8String(32)] string Label);

/// <summary>
/// Applies the wire-invariant checks shared by every fixed-width generated
/// packet without requiring the production packet types to implement a test
/// interface or changing their generated surface.
/// </summary>
internal sealed class GeneratedCodecConformance<TPacket> where TPacket : struct
{
    internal delegate void PacketWriter(TPacket packet, Span<byte> destination);
    internal delegate bool PacketReader(ReadOnlySpan<byte> source, out TPacket packet);

    private readonly int _size;
    private readonly Func<TPacket, bool> _validate;
    private readonly PacketWriter _write;
    private readonly PacketReader _tryRead;
    private readonly Action<TPacket, TPacket> _assertEqual;

    public GeneratedCodecConformance(int size, Func<TPacket, bool> validate,
        PacketWriter write, PacketReader tryRead, Action<TPacket, TPacket>? assertEqual = null)
    {
        _size = size;
        _validate = validate;
        _write = write;
        _tryRead = tryRead;
        _assertEqual = assertEqual ?? AssertPacketsEqual;
    }

    public byte[] AssertRoundTrip(TPacket expected)
    {
        Assert.True(_validate(expected));
        byte[] bytes = new byte[_size];
        _write(expected, bytes);
        Assert.True(_tryRead(bytes, out TPacket actual));
        _assertEqual(expected, actual);
        return bytes;
    }

    public void AssertRejectsEveryTruncationAndOversizePayload(ReadOnlySpan<byte> canonical)
    {
        Assert.Equal(_size, canonical.Length);
        for (int length = 0; length < canonical.Length; length++)
        {
            Assert.False(_tryRead(canonical[..length], out _));
        }
        byte[] trailing = new byte[_size + 1];
        canonical.CopyTo(trailing);
        trailing[^1] = 0xA5;
        Assert.False(_tryRead(trailing, out _));
    }

    public void AssertRejectsInvalidValue(TPacket invalid)
    {
        Assert.False(_validate(invalid));
        Assert.Throws<ArgumentException>(() => _write(invalid, new byte[_size]));
    }

    public void AssertRejectsMalformedWire(ReadOnlySpan<byte> malformed)
        => Assert.False(_tryRead(malformed, out _));

    private static void AssertPacketsEqual(TPacket expected, TPacket actual)
        => Assert.Equal(expected, actual);
}

public sealed class GeneratedProtocolTests
{
    private static readonly GeneratedCodecConformance<JoinPendingPacket> JoinPendingCodec = new(
        JoinPendingPacket.Size,
        static packet => packet.Validate(),
        static (packet, destination) => packet.Write(destination),
        static (ReadOnlySpan<byte> source, out JoinPendingPacket packet)
            => JoinPendingPacket.TryRead(source, out packet));

    private static readonly GeneratedCodecConformance<MatchAwardPacket> MatchAwardCodec = new(
        MatchAwardPacket.Size,
        static packet => packet.Validate(),
        static (packet, destination) => packet.Write(destination),
        static (ReadOnlySpan<byte> source, out MatchAwardPacket packet)
            => MatchAwardPacket.TryRead(source, out packet));

    private static readonly GeneratedCodecConformance<MatchSemanticEventPacket> MatchSemanticCodec = new(
        MatchSemanticEventPacket.Size,
        static packet => packet.Validate(),
        static (packet, destination) => packet.Write(destination),
        static (ReadOnlySpan<byte> source, out MatchSemanticEventPacket packet)
            => MatchSemanticEventPacket.TryRead(source, out packet));

    private static readonly GeneratedCodecConformance<GeneratorFixturePacket> FixtureCodec = new(
        GeneratorFixturePacket.Size,
        static packet => packet.Validate(),
        static (packet, destination) => packet.Write(destination),
        static (ReadOnlySpan<byte> source, out GeneratorFixturePacket packet)
            => GeneratorFixturePacket.TryRead(source, out packet),
        static (expected, actual) =>
        {
            Assert.Equal(expected.Sequence, actual.Sequence);
            Assert.Equal(expected.Enabled, actual.Enabled);
            Assert.Equal(expected.Kind, actual.Kind);
            Assert.Equal(expected.RequestId, actual.RequestId);
            Assert.Equal(expected.Position, actual.Position);
            Assert.Equal(expected.Values, actual.Values);
            Assert.Equal(expected.Label, actual.Label);
        });

    private static MatchAwardPacket ValidAwardPacket() => new(
        AwardId: 7,
        SourceEventId: 8,
        MatchId: 1,
        PhaseRevision: 2,
        ServerTick: 99,
        Kind: MatchAwardKind.Interceptor,
        SubjectSlot: 0,
        SubjectConnectionId: 11,
        SubjectLife: 3,
        TargetSlot: 1,
        TargetConnectionId: 12,
        TargetLife: 4,
        Count: 1,
        Value: 0);

    private static MatchSemanticEventPacket ValidSemanticPacket() => new(
        EventId: 7,
        ServerTick: 99,
        MatchId: 1,
        PhaseRevision: 2,
        Kind: MatchEventKind.PlayerSpawned,
        SubjectSlot: 0,
        SubjectConnectionId: 11,
        SubjectLife: 3,
        TargetSlot: 255,
        TargetConnectionId: 0,
        TargetLife: 0,
        EntityId: 0,
        Team: 255,
        Value: 0,
        Flags: (ushort)MatchEventFlags.None);

    private static GeneratorFixturePacket ValidFixturePacket() => new(
        42, true, GeneratorFixtureKind.Platform,
        Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"),
        new Vector3(1.5f, -2.25f, 3.75f), new ushort[] { 7, 9 }, "Prime ✓");

    [Fact]
    public void ProductionGeneratedPacketsRetainTheirIntroductionProtocol()
    {
        Assert.Equal(15, NetHeader.Version);
        Assert.Equal(9, JoinPendingPacket.Protocol);
        Assert.Equal(9, MatchAwardPacket.Protocol);
        Assert.Equal(9, MatchSemanticEventPacket.Protocol);
        Assert.Equal(NetMessageType.JoinPending, JoinPendingPacket.MessageType);
        Assert.Equal(NetMessageType.Event, MatchAwardPacket.MessageType);
        Assert.Equal(NetMessageType.Event, MatchSemanticEventPacket.MessageType);
    }

    [Fact]
    public void JoinPendingConformsForRoundTripAndGoldenBytes()
    {
        var packet = new JoinPendingPacket(0x1020304050607080UL);
        byte[] bytes = JoinPendingCodec.AssertRoundTrip(packet);

        Assert.Equal(JoinPendingPacket.MinimumSize, JoinPendingPacket.MaximumSize);
        Assert.Equal(new byte[] { 0x80, 0x70, 0x60, 0x50, 0x40, 0x30, 0x20, 0x10 }, bytes);
    }

    [Fact]
    public void JoinPendingConformsForLengthAndRangeRejection()
    {
        byte[] bytes = JoinPendingCodec.AssertRoundTrip(new JoinPendingPacket(1));
        JoinPendingCodec.AssertRejectsEveryTruncationAndOversizePayload(bytes);
        JoinPendingCodec.AssertRejectsInvalidValue(new JoinPendingPacket(0));
        Array.Clear(bytes);
        JoinPendingCodec.AssertRejectsMalformedWire(bytes);
    }

    [Fact]
    public void MatchAwardConformsForRoundTripAndExactLength()
    {
        byte[] bytes = MatchAwardCodec.AssertRoundTrip(ValidAwardPacket());
        MatchAwardCodec.AssertRejectsEveryTruncationAndOversizePayload(bytes);
    }

    [Fact]
    public void MatchAwardConformsForBadEnumAndBadRangeRejection()
    {
        MatchAwardPacket valid = ValidAwardPacket();
        MatchAwardCodec.AssertRejectsInvalidValue(valid with { Kind = (MatchAwardKind)0xFF });
        MatchAwardCodec.AssertRejectsInvalidValue(valid with { AwardId = 0 });

        byte[] bytes = MatchAwardCodec.AssertRoundTrip(valid);
        const int kindOffset = sizeof(uint) * 5;
        bytes[kindOffset] = 0xFF;
        MatchAwardCodec.AssertRejectsMalformedWire(bytes);

        bytes = MatchAwardCodec.AssertRoundTrip(valid);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0);
        MatchAwardCodec.AssertRejectsMalformedWire(bytes);
    }

    [Fact]
    public void MatchSemanticEventConformsForRoundTripAndExactLength()
    {
        byte[] bytes = MatchSemanticCodec.AssertRoundTrip(ValidSemanticPacket());
        MatchSemanticCodec.AssertRejectsEveryTruncationAndOversizePayload(bytes);

        MatchEvent combinedFlags = new(8, 100, 1, 2, MatchEventKind.PlayerKilled,
            new CombatActor(0, 11, 3), new CombatActor(0, 11, 4),
            Flags: MatchEventFlags.Suicide | MatchEventFlags.Bot);
        MatchSemanticEventPacket combinedPacket = MatchSemanticEventPacketConversion.FromEvent(combinedFlags);
        MatchSemanticCodec.AssertRoundTrip(combinedPacket);
        Assert.True(MatchSemanticEventPacketConversion.TryToEvent(combinedPacket, out MatchEvent restored));
        Assert.Equal(combinedFlags, restored);
    }

    [Fact]
    public void MatchSemanticEventConformsForBadEnumAndBadRangeRejection()
    {
        MatchSemanticEventPacket valid = ValidSemanticPacket();
        MatchSemanticCodec.AssertRejectsInvalidValue(valid with { Kind = (MatchEventKind)0xFF });
        MatchSemanticCodec.AssertRejectsInvalidValue(valid with { Flags = 128 });
        MatchSemanticCodec.AssertRejectsInvalidValue(valid with { EventId = 0 });

        byte[] bytes = MatchSemanticCodec.AssertRoundTrip(valid);
        const int kindOffset = sizeof(uint) * 4;
        bytes[kindOffset] = 0xFF;
        MatchSemanticCodec.AssertRejectsMalformedWire(bytes);

        bytes = MatchSemanticCodec.AssertRoundTrip(valid);
        const int flagsOffset = MatchSemanticEventPacket.Size - sizeof(ushort);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(flagsOffset), 128);
        MatchSemanticCodec.AssertRejectsMalformedWire(bytes);

        bytes = MatchSemanticCodec.AssertRoundTrip(valid);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0);
        MatchSemanticCodec.AssertRejectsMalformedWire(bytes);
    }

    [Fact]
    public void GeneratedFixtureConformsForEverySupportedValidationCategory()
    {
        GeneratorFixturePacket packet = ValidFixturePacket();
        byte[] bytes = FixtureCodec.AssertRoundTrip(packet);
        FixtureCodec.AssertRejectsEveryTruncationAndOversizePayload(bytes);

        // Fixed schema offsets: sequence 0, bool 4, enum 5, Guid 6, Vector3 22.
        bytes[4] = 2; // bool wire values are exactly 0 or 1.
        FixtureCodec.AssertRejectsMalformedWire(bytes);

        bytes = FixtureCodec.AssertRoundTrip(packet);
        bytes[5] = 0xFF;
        FixtureCodec.AssertRejectsMalformedWire(bytes);
        FixtureCodec.AssertRejectsInvalidValue(packet with { Kind = (GeneratorFixtureKind)0xFF });

        bytes = FixtureCodec.AssertRoundTrip(packet);
        BinaryPrimitives.WriteInt32LittleEndian(bytes, -1);
        FixtureCodec.AssertRejectsMalformedWire(bytes);
        FixtureCodec.AssertRejectsInvalidValue(packet with { Sequence = -1 });

        bytes = FixtureCodec.AssertRoundTrip(packet);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(22), float.NaN);
        FixtureCodec.AssertRejectsMalformedWire(bytes);
        FixtureCodec.AssertRejectsInvalidValue(packet with { Position = new Vector3(float.NaN, 0, 0) });

        FixtureCodec.AssertRejectsInvalidValue(packet with { Label = new string('x', 32) });
    }
}
