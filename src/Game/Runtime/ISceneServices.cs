using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead
{
    /// <summary>Execution policy supplied by the scene's host. Game code owns no session singleton.</summary>
    public interface ISceneServices
    {
        bool IsReplica => false;
        bool RebuildingRoom => false;
        int RoomPlayerCount => 0;
        PlayerEntity RebuildPlayers(Scene scene, Hunter hunter, int recolor)
            => throw new System.InvalidOperationException("This scene host does not rebuild session players.");
        void AfterRoomRebuild(Scene scene) { }
        int LocalSlot => 0;
        uint WorldServerTick => 0;
        bool MayEndOnScore => true;
        bool ShouldLeaveAfterMatch => true;
        ICombatAuthority? Combat => null;
        bool KeepSlotAlive(PlayerEntity player) => false;
        bool TryApplyRemoteInput(PlayerEntity player, int slot) => false;
        bool IsRemoteControlled(int slot) => false;
        bool TryGetRemoteAim(int slot, out Vector3 aim) { aim = default; return false; }
        bool DesiredSpectating => false;
        Vector2 ControllerAimDelta => Vector2.Zero;
        bool TryGetScriptedAimDelta(int slot, out Vector2 delta) { delta = default; return false; }
        void NoteCollisionRange(int slot, Vector3 previous, Vector3 current) { }
        void NoteEvent(string message) { }
        bool ForceSpawn(PlayerEntity player) => false;
        void AfterInput(Scene scene) { }
        void AfterSimulation(Scene scene) { }
        bool SuppressDamage(PlayerEntity victim) => false;
        BeamType ReplayBeam => BeamType.None;
        void NoteDamage(PlayerEntity victim, PlayerEntity? attacker, BeamType beam, DamageFlags flags, Vector3? direction) { }
        void NoteFired(PlayerEntity shooter, Vector3 shot, Vector3 aim) { }
        void NotePlayerOverlap(EntityBase? owner, PlayerEntity target) { }
        void CountUnresolvedNode() { }
        void CountPlayerCheck(int slot) { }
        void CountPlayerOverlap(int slot) { }
        void CountPlayerAccepted(int slot) { }
    }

    public sealed class SceneServices : ISceneServices
    {
        public static ISceneServices Local { get; } = new SceneServices();
        private SceneServices() { }
    }

    /// <summary>Authority queries and notifications; mutable history and queues remain host-owned.</summary>
    public interface ICombatAuthority
    {
        uint Tick { get; }
        uint? CollisionTick { get; }
        CombatShot CaptureAttribution(EntityBase owner);
        CombatShot CaptureShot(EntityBase owner, in BeamMechanics mechanics);
        uint NextSpreadSeed();
        LagCompensationMode GetMode(in BeamMechanics mechanics);
        bool TryGetPlayerCollider(PlayerEntity player, in CombatShot shot, out LagCompensationState state);
        bool TryGetHomingTarget(EntityBase entity, uint tick, CombatActor expected, out Vector3 position, out CombatActor identity);
        void EnqueueCatchUp(BeamProjectileEntity beam, bool inherited);
        bool IsStaleActor(in CombatActor actor);
        bool IsStaleSource(EntityBase? source);
        void NoteShot(in CombatShot shot, BeamType weapon, bool charged, Vector3 position, Vector3 direction,
            ushort chargeLevel = 0, bool affinity = false, uint spreadSeed = 0);
        void NoteBomb(in CombatShot shot, BombType type, Vector3 position, Vector3 facing);
        void NoteSpawn(PlayerEntity player);
        void NoteDamage(PlayerEntity victim, EntityBase? source, PlayerEntity? attacker, BeamType weapon,
            DamageFlags flags, Vector3? direction, int previousHealth, ushort frozen, ushort burn,
            ushort disrupt, bool afflictionChanged);
    }
}
