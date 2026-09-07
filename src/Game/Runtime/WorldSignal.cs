using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead
{
    public enum WorldSubjectKind : byte { Item = 1, Spawner, Flag, Node, Match }
    public enum WorldSignalKind : byte
    {
        PickupConsumed = 1, PickupRespawned, FlagPickedUp, FlagDropped, FlagReset,
        FlagCaptured, NodeCaptured, NodeContested, PrimeChanged, DefenderStateChanged, OvertimeStarted, MatchPoint
    }
    /// <summary>Resolved game transition, translated synchronously by the scene host.</summary>
    public readonly record struct WorldSignal(WorldSignalKind Kind, WorldSubjectKind Subject,
        EntityBase? Entity, PlayerEntity? Actor, byte Team, Vector3 Position, uint A = 0, uint B = 0, uint C = 0);
}
