using System;
using MphRead.Mods.Network;
using MphRead.Formats;
using MphRead.Formats.Culling;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public partial class BombEntity
    {
        internal bool ModReplica;
        internal uint ModPlacedFrame;
        internal uint ModLink0;
        internal uint ModLink1;
        internal Vector3 ModBombSpeed => _speed;

        private void ModResetPlacement()
        {
            ModReplica = false;
            ModPlacedFrame = ModLink0 = ModLink1 = 0;
            _target = null;
            _speed = Vector3.Zero;
            BombIndex = 0;
            Radius = SelfRadius = 0;
            Damage = EnemyDamage = 0;
            _lockjawVisualTick = 0;
        }

        private bool ModSameOwner(BombEntity other)
            => (other.ModOwnerKey with { ShotId = ModOwnerKey.ShotId }) == ModOwnerKey;

        internal BombState ModCaptureState()
        {
            uint link0 = 0, link1 = 0;
            if (!ModReplica && BombType == BombType.Lockjaw && Owner != null
                && BombIndex >= 0 && BombIndex < Owner.SyluxBombs.Length
                && Owner.SyluxBombs[BombIndex] == this)
            {
                if (BombIndex >= 1 && Owner.SyluxBombs[0] is BombEntity first && ModSameOwner(first))
                    link0 = first.ModOwnerKey.ShotId;
                if (BombIndex >= 2 && Owner.SyluxBombs[1] is BombEntity second && ModSameOwner(second))
                    link1 = second.ModOwnerKey.ShotId;
            }
            else if (ModReplica) { link0 = ModLink0; link1 = ModLink1; }
            return new BombState(ModOwnerKey, BombType, ModPlacedFrame, Position, FacingVector, UpVector,
                _speed, (ushort)Math.Clamp(Countdown, 0, ushort.MaxValue), (byte)BombIndex, link0, link1, (byte)Recolor);
        }

        internal static BombEntity? SpawnReplica(PlayerEntity owner, in BombState state, Scene scene)
        {
            BombEntity? bomb = scene.InitBomb();
            if (bomb == null) return null;
            bomb.ModResetPlacement();
            bomb.Owner = owner;
            bomb.ModReplica = true;
            bomb.BombType = state.Kind;
            bomb.Recolor = state.Recolor;
            bomb.Flags = BombFlags.None;
            bomb.NodeRef = NodeRef.None;
            bomb.ModApplyState(state);
            scene.AddEntity(bomb);
            // Initialize selects resources and default native timers.
            bomb.ModApplyState(state);
            return bomb;
        }

        private void ModEnsurePlacementEffect()
        {
            if (Effect != null || Flags.TestFlag(BombFlags.Exploded)) return;
            int effectId = BombType == BombType.Lockjaw ? Metadata.SyluxBombEffects[Recolor]
                : BombType == BombType.MorphBall
                    ? (_scene.GameState.Multiplayer && _scene.Players.PlayerCount > 2 ? 119 : 9) : 0;
            if (effectId == 0) return;
            // Bootstrap can materialize ghosts between simulation steps. Birth
            // belongs to this step's native effect clock, so zero-age emitters run.
            Effect = _scene.SpawnEffectGetEntry(effectId, Transform);
            Effect?.SetElementExtension(true);
        }

        internal void ModApplyState(in BombState state)
        {
            ModOwnerKey = state.Key;
            ModPlacedFrame = state.PlacedFrame;
            ModLink0 = state.Link0;
            ModLink1 = state.Link1;
            SetTransform(state.Facing, state.Up, state.Position);
            _speed = state.Speed;
            Countdown = state.Countdown;
            BombIndex = state.Index;
            Effect?.Transform(Position, Transform);
        }

        internal void ModPresentDetonation()
        {
            if (Flags.TestFlag(BombFlags.Exploded)) return;
            Flags = (Flags & ~BombFlags.Exploding) | BombFlags.Exploded;
            _target = null;
            _models.Clear();
            if (Effect != null) _scene.UnlinkEffectEntry(Effect);
            Effect = null;
            _soundSource.Update(Position, rangeIndex: 5);
            UpdateNodeRefVolume();
            int effect = BombType == BombType.Stinglarva ? 128 : BombType == BombType.Lockjaw ? 146 : 145;
            _scene.SpawnEffect(effect, Transform);
            Countdown = 0;
            _soundSource.StopSfx(SfxId.KANDEN_ALT_ATTACK);
            _soundSource.StopSfx(SfxId.MORPH_BALL_BOMB_PLACE);
            _soundSource.PlaySfx(SfxId.MORPH_BALL_BOMB);
            // The owner jump is a movement response to the accepted terminal,
            // never a client's independent bomb timer/contact decision.
            if (ModReplica && !_scene.Services.IsReplica && NetSession.IsClient
                && Owner.SlotIndex == NetSession.LocalSlot && Owner.IsAltForm
                && NetBombs.Current(ModOwnerKey)) Owner.CheckHitByBomb(this, halfturret: false);
        }

        internal void ModRemoveReplica()
        {
            if (!ModReplica || Owner == null) return;
            _scene.RemoveEntity(this);
            Destroy();
        }

        private void ModDrawReplicaLinks()
        {
            if (Flags.TestFlag(BombFlags.Exploded)) return;
            foreach (BombEntity bomb in _scene.GetBombEntities())
            {
                if (bomb == this || !ModSameOwner(bomb) || bomb.Flags.TestFlag(BombFlags.Exploded)) continue;
                if (ModLink0 != 0 && bomb.ModOwnerKey.ShotId == ModLink0)
                    DrawLockjawTrail(Position, bomb.Position, Fixed.ToFloat(614), 10, targetBombIndex: 0);
                if (ModLink1 != 0 && bomb.ModOwnerKey.ShotId == ModLink1)
                    DrawLockjawTrail(Position, bomb.Position, Fixed.ToFloat(614), 10, targetBombIndex: 1);
            }
        }
    }
}
