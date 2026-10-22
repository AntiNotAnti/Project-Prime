namespace MphRead.Mods.Network;
internal sealed partial class ReplayReplicaState
{
    // Detached packet analysis has no Scene/BombWorld presentation consumer.
    // Packets were validated on acceptance; spatial analytics do not construct a
    // bomb world. Shot facts, pickup history and objective authority remain intact.
    internal void DiscardDetachedDrawPackets() => _bombPackets.Clear();
}
