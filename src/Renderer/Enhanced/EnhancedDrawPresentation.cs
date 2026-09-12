using System;

namespace MphRead;

/// <summary>Immutable presentation-only beam styling captured with one trail draw.</summary>
public readonly record struct EnhancedBeamDrawState
{
    public EnhancedBeamDrawState(BeamVisualProfile profile,
        TimeSpan presentationTime, ulong stableSourceKey)
    {
        BeamVisualSample sample = profile.Sample(presentationTime, stableSourceKey);
        Profile = profile;
        Sample = sample;
    }

    public BeamVisualProfile Profile { get; }
    public BeamVisualSample Sample { get; }
}

/// <summary>Immutable presentation-only force-field styling captured with one mesh draw.</summary>
public readonly record struct EnhancedForceFieldDrawState
{
    public EnhancedForceFieldDrawState(ulong stableSourceKey,
        ForceFieldVisualProfile profile, TimeSpan presentationTime)
    {
        ForceFieldVisualSample sample = profile.Sample(presentationTime,
            stableSourceKey);
        StableSourceKey = stableSourceKey;
        Profile = profile;
        Sample = sample;
    }

    public ulong StableSourceKey { get; }
    public ForceFieldVisualProfile Profile { get; }
    public ForceFieldVisualSample Sample { get; }
}

/// <summary>
/// Derives deterministic subresource keys without consulting object hashes.
/// The owning presentation supplies a reset-scoped or authored base key.
/// </summary>
internal static class EnhancedPresentationSourceKey
{
    public static ulong ForSubresource(ulong baseKey, int nodeIndex,
        int meshIndex)
    {
        if (nodeIndex < 0) throw new ArgumentOutOfRangeException(nameof(nodeIndex));
        if (meshIndex < 0) throw new ArgumentOutOfRangeException(nameof(meshIndex));
        ulong hash = Append(14695981039346656037ul, baseKey);
        hash = Append(hash, unchecked((uint)nodeIndex));
        return Append(hash, unchecked((uint)meshIndex));
    }

    private static ulong Append(ulong hash, uint value)
    {
        hash = Append(hash, (byte)value);
        hash = Append(hash, (byte)(value >> 8));
        hash = Append(hash, (byte)(value >> 16));
        return Append(hash, (byte)(value >> 24));
    }

    private static ulong Append(ulong hash, ulong value)
    {
        hash = Append(hash, unchecked((uint)value));
        return Append(hash, unchecked((uint)(value >> 32)));
    }

    private static ulong Append(ulong hash, byte value)
        => (hash ^ value) * 1099511628211ul;
}
