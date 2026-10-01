using MphRead.Mods.Network.Generated;

namespace MphRead.Mods.Network;

internal enum LobbyQueueWireState : byte { Waiting, NextMatch, Offered, Disconnected, Left, Expired, Rejected }
[NetPacket(PacketType.QueueHello, 35)]
internal readonly partial record struct QueueHelloPacket([NetRange(NetConfig.ProtocolVersion, NetConfig.ProtocolVersion)] byte Protocol,
    [NetRange(1,uint.MaxValue)] uint ClientId, ulong ClientNonce)
{ partial void ValidateSchema(ref bool valid) => valid = ClientNonce != 0; }
[NetPacket(PacketType.QueueWelcome, 35)]
internal readonly partial record struct QueueWelcomePacket([NetRange(NetConfig.ProtocolVersion, NetConfig.ProtocolVersion)] byte Protocol,
    [NetRange(1,uint.MaxValue)] uint ClientId, ulong ClientNonce)
{ partial void ValidateSchema(ref bool valid) => valid = ClientNonce != 0; }
[NetPacket(PacketType.QueueJoin, 35)]
internal readonly partial record struct QueueJoinPacket(ulong ClientNonce)
{ partial void ValidateSchema(ref bool valid) => valid = ClientNonce != 0; }
[NetPacket(PacketType.QueueLeave, 35)]
internal readonly partial record struct QueueLeavePacket(ulong QueueId)
{ partial void ValidateSchema(ref bool valid) => valid = QueueId != 0; }
[NetPacket(PacketType.QueueState, 35)]
internal readonly partial record struct QueueStatePacket([NetRange(1,uint.MaxValue)] uint Revision,
    ulong QueueId, [NetRange(0,256)] ushort Position, [NetRange(0,256)] ushort QueueLength, LobbyQueueWireState State)
{
    partial void ValidateSchema(ref bool valid) => valid = State >= LobbyQueueWireState.Left
        ? Position == 0 && (QueueId != 0 || State == LobbyQueueWireState.Rejected)
        : QueueId != 0 && Position > 0 && Position <= QueueLength;
}
[NetPacket(PacketType.QueueSeatOffer, 35)]
internal readonly partial record struct QueueSeatOfferPacket([NetRange(1,uint.MaxValue)] uint Revision,
    ulong QueueId, ulong OfferId, [NetRange(1,ushort.MaxValue)] ushort MatchId, ulong AuthorityEpoch,
    [NetRange(1,18000)] uint ExpiresInTicks)
{ partial void ValidateSchema(ref bool valid) => valid = QueueId != 0 && OfferId != 0 && AuthorityEpoch != 0; }
[NetPacket(PacketType.QueueAccept, 35)]
internal readonly partial record struct QueueAcceptPacket(ulong QueueId, ulong OfferId,
    [NetRange(1,ushort.MaxValue)] ushort MatchId, ulong AuthorityEpoch)
{ partial void ValidateSchema(ref bool valid) => valid = QueueId != 0 && OfferId != 0 && AuthorityEpoch != 0; }
[NetPacket(PacketType.QueueDecline, 35)]
internal readonly partial record struct QueueDeclinePacket(ulong QueueId, ulong OfferId,
    [NetRange(1,ushort.MaxValue)] ushort MatchId, ulong AuthorityEpoch)
{ partial void ValidateSchema(ref bool valid) => valid = QueueId != 0 && OfferId != 0 && AuthorityEpoch != 0; }
