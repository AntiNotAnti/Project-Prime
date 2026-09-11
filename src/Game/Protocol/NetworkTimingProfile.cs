using System;
using System.Buffers.Binary;

namespace MphRead.Mods.Network;

public enum NetworkTimingLevel : byte
{
    Excellent,
    Good,
    Normal,
    Unstable,
    Recovery
}

/// <summary>Small server-owned presentation and input playout policy.</summary>
public readonly record struct NetworkTimingProfile(
    uint Revision,
    byte PresentationDelayTicks,
    byte InputPlayoutTicks)
{
    public const byte MinimumPresentationDelayTicks = 2;
    public const byte MaximumPresentationDelayTicks = 6;
    public const byte MinimumInputPlayoutTicks = 1;
    public const byte MaximumInputPlayoutTicks = 3;
    public const byte CompatibilityPresentationDelayTicks = 6;
    public const byte CompatibilityInputPlayoutTicks = 2;

    /// <summary>The pre-adaptation behavior used when the feature is disabled.</summary>
    public static NetworkTimingProfile Compatibility => new(0,
        CompatibilityPresentationDelayTicks, CompatibilityInputPlayoutTicks);

    public bool IsValid => Revision != 0
        && PresentationDelayTicks is >= MinimumPresentationDelayTicks and <= MaximumPresentationDelayTicks
        && InputPlayoutTicks is >= MinimumInputPlayoutTicks and <= MaximumInputPlayoutTicks;

    public static NetworkTimingProfile Create(uint revision, NetworkTimingLevel level)
    {
        if (revision == 0 || !Enum.IsDefined(level)) throw new ArgumentOutOfRangeException();
        return level switch
        {
            NetworkTimingLevel.Excellent => new(revision, 2, 1),
            NetworkTimingLevel.Good => new(revision, 3, 2),
            NetworkTimingLevel.Normal => new(revision, 4, 2),
            NetworkTimingLevel.Unstable => new(revision, 5, 3),
            NetworkTimingLevel.Recovery => new(revision, 6, 3),
            _ => throw new ArgumentOutOfRangeException(nameof(level))
        };
    }

    public static NetworkTimingLevel LevelFor(byte presentationDelayTicks)
        => presentationDelayTicks switch
        {
            <= 2 => NetworkTimingLevel.Excellent,
            3 => NetworkTimingLevel.Good,
            4 => NetworkTimingLevel.Normal,
            5 => NetworkTimingLevel.Unstable,
            _ => NetworkTimingLevel.Recovery
        };
}

public static class NetworkTimingProfilePacket
{
    public const int Size = 6;

    public static void Write(Span<byte> destination, in NetworkTimingProfile profile)
    {
        if (destination.Length < Size || !profile.IsValid) throw new ArgumentException("Invalid timing profile.");
        BinaryPrimitives.WriteUInt32LittleEndian(destination, profile.Revision);
        destination[4] = profile.PresentationDelayTicks;
        destination[5] = profile.InputPlayoutTicks;
    }

    public static bool TryRead(ReadOnlySpan<byte> source, out NetworkTimingProfile profile)
    {
        profile = default;
        if (source.Length != Size) return false;
        profile = new(BinaryPrimitives.ReadUInt32LittleEndian(source), source[4], source[5]);
        return profile.IsValid;
    }
}

public static class NetworkTimingProfileAppliedPacket
{
    public const int Size = 4;

    public static void Write(Span<byte> destination, uint revision)
    {
        if (destination.Length < Size || revision == 0) throw new ArgumentOutOfRangeException(nameof(revision));
        BinaryPrimitives.WriteUInt32LittleEndian(destination, revision);
    }

    public static bool TryRead(ReadOnlySpan<byte> source, out uint revision)
    {
        revision = source.Length == Size ? BinaryPrimitives.ReadUInt32LittleEndian(source) : 0;
        return revision != 0;
    }
}

/// <summary>Quantized, bounded client presentation observations. Never authority.</summary>
public readonly record struct NetworkTimingTelemetry(
    uint ProfileRevision,
    byte CurrentPresentationDelayTicks,
    ushort PresentedFrames,
    ushort SnapshotUnderruns,
    ushort ExtrapolatedFrames,
    ushort AverageSnapshotIntervalTenthsMs,
    ushort SnapshotJitterTenthsMs)
{
    public const int Size = 15;
    public bool IsValid => ProfileRevision != 0
        && PresentedFrames > 0
        && SnapshotUnderruns <= PresentedFrames
        && ExtrapolatedFrames <= PresentedFrames
        && CurrentPresentationDelayTicks is >= NetworkTimingProfile.MinimumPresentationDelayTicks
        and <= NetworkTimingProfile.MaximumPresentationDelayTicks
        && AverageSnapshotIntervalTenthsMs is >= 50 and <= 10_000
        && SnapshotJitterTenthsMs <= 10_000;

    public void Write(Span<byte> destination)
    {
        if (destination.Length < Size || !IsValid) throw new ArgumentException("Invalid timing telemetry.");
        BinaryPrimitives.WriteUInt32LittleEndian(destination, ProfileRevision);
        destination[4] = CurrentPresentationDelayTicks;
        BinaryPrimitives.WriteUInt16LittleEndian(destination[5..], PresentedFrames);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[7..], SnapshotUnderruns);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[9..], ExtrapolatedFrames);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[11..], AverageSnapshotIntervalTenthsMs);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[13..], SnapshotJitterTenthsMs);
    }

    public static bool TryRead(ReadOnlySpan<byte> source, out NetworkTimingTelemetry telemetry)
    {
        telemetry = default;
        if (source.Length != Size) return false;
        telemetry = new(BinaryPrimitives.ReadUInt32LittleEndian(source), source[4],
            BinaryPrimitives.ReadUInt16LittleEndian(source[5..]),
            BinaryPrimitives.ReadUInt16LittleEndian(source[7..]),
            BinaryPrimitives.ReadUInt16LittleEndian(source[9..]),
            BinaryPrimitives.ReadUInt16LittleEndian(source[11..]),
            BinaryPrimitives.ReadUInt16LittleEndian(source[13..]));
        return telemetry.IsValid;
    }
}
