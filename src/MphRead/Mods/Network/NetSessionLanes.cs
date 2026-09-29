using System;
namespace MphRead.Mods.Network;
public static partial class NetSession
{
    private static readonly NetReplicationReceiver _laneReceiver = new();
    private static readonly byte[] _laneCanonical = new byte[4097]; // bootstrap/offline canonical assembly
    private static readonly NetReplicationLanes _hostLanes = new();
    private static void HandleLane(ReceivedPacket packet)
    {
        if (packet.Type != PacketType.SnapshotFast)
        {
            _laneReceiver.Receive(packet.Type, packet.Payload, CurrentMatchId, AuthorityEpoch);
            return;
        }
        if (FreezeGameplay) return;
        HandleSnapshot(packet);
    }
    private static void SendHostLanes(System.Net.IPEndPoint endpoint)
    {
        void Send(PacketType type, byte[] bytes, int length)
        { _transport!.Send(endpoint, type, bytes.AsSpan(0, length)); NetReplicationLanes.Count(type, length); }
        Send(PacketType.SnapshotFast, _hostLanes.Fast, _hostLanes.FastLength);
        if (_hostLanes.SendSlow) Send(PacketType.PlayerSlowState, _hostLanes.Slow, _hostLanes.SlowLength);
        if (_hostLanes.SendWorld) Send(PacketType.WorldState, _hostLanes.World, _hostLanes.WorldLength);
    }
}
