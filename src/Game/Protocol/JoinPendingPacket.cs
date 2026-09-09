namespace MphRead.Mods.Network;

/// <summary>
/// Protocol 9 bot-admission heartbeat. The generated codec is deliberately
/// tiny and fixed-width; protocol 8 and all legacy demo codecs are unchanged.
/// </summary>
[NetPacket(NetMessageType.JoinPending, protocol: 9)]
public readonly partial record struct JoinPendingPacket(
    [property: NetUnsignedRange(1, ulong.MaxValue)] ulong Nonce);
