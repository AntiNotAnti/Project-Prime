using System;

namespace MphRead.Mods.Network;

/// <summary>
/// Marks a partial value type for the bounded binary protocol generator.
/// This MVP is intentionally reserved for protocol 9 additions; protocol 8
/// codecs remain hand-written and byte-compatible.
/// </summary>
[AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class NetPacketAttribute(NetMessageType messageType, byte protocol = 9) : Attribute
{
    public NetMessageType MessageType { get; } = messageType;
    public byte Protocol { get; } = protocol;
}

/// <summary>Constrains a signed or small integral packet member.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class NetRangeAttribute(long minimum, long maximum) : Attribute
{
    public long Minimum { get; } = minimum;
    public long Maximum { get; } = maximum;
}

/// <summary>Constrains an unsigned packet member without narrowing ulong bounds.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class NetUnsignedRangeAttribute(ulong minimum, ulong maximum) : Attribute
{
    public ulong Minimum { get; } = minimum;
    public ulong Maximum { get; } = maximum;
}

/// <summary>Requires finite values for a supported Vector3 member.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class NetFiniteAttribute : Attribute;

/// <summary>
/// Encodes a string into a fixed-width, NUL-terminated UTF-8 field. The
/// maximum includes the terminator, so at least two bytes are required.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class NetUtf8StringAttribute(int maximumBytes) : Attribute
{
    public int MaximumBytes { get; } = maximumBytes;
}

/// <summary>Encodes exactly <paramref name="count"/> elements of a fixed array.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class NetArrayAttribute(int count) : Attribute
{
    public int Count { get; } = count;
}
