using System;

namespace MphRead.Mods.Network.Generated;

/// <summary>Opt-in schema for new packets. The byte packet ID is encoded first;
/// protocolVersion is schema metadata, not another wire field or live version bump.</summary>
[AttributeUsage(AttributeTargets.Struct, AllowMultiple = false)]
internal sealed class NetPacketAttribute(object packetType, int protocolVersion) : Attribute
{
    public object PacketType { get; } = packetType;
    public int ProtocolVersion { get; } = protocolVersion;
}

/// <summary>Inclusive numeric bounds. Applied to positional constructor parameters.</summary>
[AttributeUsage(AttributeTargets.Parameter)]
internal sealed class NetRangeAttribute(long minimum, long maximum) : Attribute
{
    public long Minimum { get; } = minimum;
    public long Maximum { get; } = maximum;
}

/// <summary>Maximum encoded UTF-8 bytes, excluding the unsigned 16-bit length prefix.</summary>
[AttributeUsage(AttributeTargets.Parameter)]
internal sealed class NetStringAttribute(int maximumBytes) : Attribute
{
    public int MaximumBytes { get; } = maximumBytes;
}

/// <summary>Reserved schema marker. Arrays are rejected until their value ownership
/// and element contracts are implemented; the generator never silently ignores it.</summary>
[AttributeUsage(AttributeTargets.Parameter)]
internal sealed class NetFixedArrayAttribute(int length) : Attribute
{
    public int Length { get; } = length;
}
