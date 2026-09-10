using System;
using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network
{
    /// <summary>Client and passive recording adapters implement Game's host policy.</summary>
    public sealed class ClientSceneServices(bool forceSpawn = false) : ISceneServices
    {
        public bool IsReplica => AuthoritativePlay.Active;
        public bool RebuildingRoom => NetRoomChange.Rebuilding;
        public int RoomPlayerCount => NetRoomChange.RoomPlayerCount;
        public PlayerEntity RebuildPlayers(Scene scene, Hunter hunter, int recolor)
            => NetRoomChange.RebuildPlayers(scene, hunter, recolor);
        public void AfterRoomRebuild(Scene scene) => NetRoomChange.AfterRebuild(scene);
        public int LocalSlot => NetHooks.LocalSlot;
        public uint WorldServerTick => AuthoritativePlay.Current?.WorldServerTick ?? DemoPlayback.WorldServerTick ?? 0;
        public bool MayEndOnScore => NetMatchEnd.MayEndOnScore;
        public bool ShouldLeaveAfterMatch => NetMatchEnd.ShouldLeaveAfterMatch;
        public bool KeepSlotAlive(PlayerEntity player) => NetHooks.KeepSlotAlive(player);
        public bool TryApplyRemoteInput(PlayerEntity player, int slot) => NetHooks.TryApplyRemoteInput(player, slot);
        public bool IsRemoteControlled(int slot) => NetSession.Active && slot != NetHooks.LocalSlot;
        public bool TryGetRemoteAim(int slot, out Vector3 aim)
        {
            if (IsRemoteControlled(slot) && NetSession.RemoteIntentValid[slot])
            {
                aim = NetSession.RemoteIntents[slot].Aim;
                return true;
            }
            aim = default;
            return false;
        }
        public bool DesiredSpectating => SpectatorMode.IsSpectating;
        private LocalLookFrame _localLookFrame;
        public LocalLookFrame LocalLookFrame => _localLookFrame;
        public void BeginLocalLookFrame(bool allowAimAssist)
        {
            LocalLookFrame consumed = Input.GamepadInput.LookCoordinator
                .ConsumeForSimulation((float)Render.FrameTiming.StepSeconds);
            _localLookFrame = allowAimAssist
                ? consumed.WithAimAssist(InputSettings.GamepadAimAssistEnabled,
                    InputSettings.GamepadAimAssistStrength)
                : LocalLookFrame.Empty;
        }
        public float ControllerZoomMultiplier => InputSettings.GamepadZoomMultiplier;
        public bool TryGetScriptedAimDelta(int slot, out Vector2 delta)
        {
            if (NetSession.Active && slot == NetHooks.LocalSlot && NetTestScript.Enabled)
            {
                delta = new(NetTestScript.AimDeltaX, NetTestScript.AimDeltaY);
                return true;
            }
            delta = default;
            return false;
        }
        public void NoteCollisionRange(int slot, Vector3 previous, Vector3 current)
        {
            if (NetLog.Enabled && NetSession.Active) { NetLog.CollisionRange(slot, "pre-check", previous, current); }
        }
        public void NoteEvent(string message) => NetLog.Event(message);
        public bool ForceSpawn(PlayerEntity player) => forceSpawn;
        public void AfterInput(Scene scene) => NetHooks.AfterInput(scene);
        public void AfterSimulation(Scene scene) => NetHooks.AfterSimulation(scene);
        public CombatShot CapturePresentationAttribution(EntityBase owner)
            => AuthoritativePlay.Current?.CapturePresentationAttribution(owner) ?? default;
        public void ObserveDamageAttempt(PlayerEntity victim, uint damage, DamageFlags flags,
            Vector3? direction, EntityBase? source)
            => AuthoritativePlay.Current?.ObserveDamageAttempt(victim, flags, direction, source);
        public bool PredictBombJump(PlayerEntity player, BombEntity bomb, float ySpeed)
            => AuthoritativePlay.Current?.PredictBombJump(player, bomb, ySpeed) == true;
        public bool SuppressDamage(PlayerEntity victim) => NetDamage.Suppress(victim);
        public BeamType ReplayBeam => NetDamage.ReplayBeam;
        public void NoteDamage(PlayerEntity victim, PlayerEntity? attacker, BeamType beam, DamageFlags flags, Vector3? direction)
            => NetDamage.Note(victim, attacker, beam, flags, direction);
        public void NoteFired(PlayerEntity shooter, Vector3 shot, Vector3 aim)
        {
            // NoteFired runs immediately after the local scene creates its
            // visual beam. Measurement copies only immutable spawn facts; it
            // never gives the client projectile gameplay authority.
            uint? commandSequence = AuthoritativePlay.Current?
                .ObservePredictedProjectile(shooter);
            NetDamage.NoteFired(shooter, shot, aim);
            if (shooter.SlotIndex == LocalSlot && shot.LengthSquared > .000001f
                && aim.LengthSquared > .000001f)
            {
                float dot = Math.Clamp(Vector3.Dot(shot.Normalized(), aim.Normalized()), -1, 1);
                float error = MathF.Acos(dot) * 180 / MathF.PI;
                Input.InputBalanceTelemetry.RecordShot(
                    Input.GamepadInput.LookCoordinator.ActiveLookDevice,
                    (int)shooter.Hunter, (int)shooter.CurrentWeapon,
                    shooter.EquipInfo.Zoomed, error, commandSequence);
            }
        }
        public void ObserveAimAssist(PlayerEntity player, bool acquiredTarget,
            float acquisitionMilliseconds, float angularErrorDegrees,
            float rotationalDegrees, float frictionMultiplier)
        {
            Input.InputBalanceTelemetry.RecordAssist(LookDeviceKind.GamepadStick,
                (int)player.Hunter, (int)player.CurrentWeapon,
                acquisitionMilliseconds, rotationalDegrees, frictionMultiplier,
                acquiredTarget);
        }
        public void NotePlayerOverlap(EntityBase? owner, PlayerEntity target) => NetDamage.NotePlayerOverlap(owner, target);
        public void CountUnresolvedNode() => NetPlayerBridge.NodeLookupsUnresolved++;
        public void CountPlayerCheck(int slot) { if (NetLog.Enabled) NetDamage.PlayerChecks[slot]++; }
        public void CountPlayerOverlap(int slot) { if (NetLog.Enabled) NetDamage.PlayerOverlaps[slot]++; }
        public void CountPlayerAccepted(int slot) { if (NetLog.Enabled) NetDamage.PlayerAccepted[slot]++; }
    }
}
