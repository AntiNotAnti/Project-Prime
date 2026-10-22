using System;
using MphRead.Formats;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network
{
    internal enum BombAdmissionReason : byte { None, Form, Inventory, Limit, Placement, NoBombs, PoolEmpty, Stale }
}

namespace MphRead.Entities
{
    public partial class PlayerEntity
    {
        internal Matrix4 ModBombTransform()
        {
            if (Hunter == Hunter.Kanden)
            {
                Matrix4 segment = _kandenSegMtx[4];
                return GetTransformMatrix(segment.Row2.Xyz, segment.Row1.Xyz, _kandenSegPos[4]);
            }
            return GetTransformMatrix(Vector3.UnitZ, Vector3.UnitY, Position.AddY(Fixed.ToFloat(-1000)));
        }

        internal BombAdmissionReason ModTryAdmitBomb(BombAction action, out BombEntity? bomb)
        {
            bomb = null;
            if (!LoadFlags.TestFlag(LoadFlags.Active) || Health <= 0 || !ModIsInPlay
                || Flags2.TestFlag(PlayerFlags2.Spectating)) return BombAdmissionReason.Stale;
            if (!IsAltForm || IsMorphing || IsUnmorphing || !_abilities.TestFlag(AbilityFlags.Bombs)
                || _field35C != null) return BombAdmissionReason.Form;
            if (_bombAmmo == 0 || _bombCooldown != 0) return BombAdmissionReason.Inventory;
            ushort generation = NetPlayerLifecycle.Generation(SlotIndex), life = NetPlayerLifecycle.Get(SlotIndex);
            if (action == BombAction.Detonate)
            {
                if (Hunter != Hunter.Sylux) return BombAdmissionReason.Form;
                if (SyluxBombCount != 3) return BombAdmissionReason.NoBombs;
                foreach (var placed in SyluxBombs)
                    if (placed == null || placed.Owner != this || placed.BombType != BombType.Lockjaw
                        || placed.Flags.TestFlag(BombFlags.Exploded)
                        || placed.ModOwnerKey.Generation != generation || placed.ModOwnerKey.LifeId != life
                        || NetSession.Active && !NetSession.MatchesStream(placed.ModOwnerKey.MatchId, placed.ModOwnerKey.AuthorityEpoch))
                        return BombAdmissionReason.NoBombs;
                foreach (var placed in SyluxBombs) placed!.Countdown = 0;
                return BombAdmissionReason.None;
            }
            if (action != BombAction.Place) return BombAdmissionReason.Stale;
            int active = 0;
            foreach (var placed in _scene.GetBombEntities())
                if (placed.Owner == this && !placed.Flags.TestFlag(BombFlags.Exploded)
                    && placed.ModOwnerKey.Generation == generation
                    && (!NetSession.Active || NetSession.MatchesStream(placed.ModOwnerKey.MatchId, placed.ModOwnerKey.AuthorityEpoch))) active++;
            if (active >= 3) return BombAdmissionReason.Limit;
            Matrix4 transform = ModBombTransform();
            static bool Finite(Vector4 row) => float.IsFinite(row.X) && float.IsFinite(row.Y)
                && float.IsFinite(row.Z) && float.IsFinite(row.W);
            if (!Finite(transform.Row0) || !Finite(transform.Row1) || !Finite(transform.Row2) || !Finite(transform.Row3))
                return BombAdmissionReason.Placement;
            Vector3 position = transform.Row3.Xyz;
            if ((position - Position).LengthSquared > 9f) return BombAdmissionReason.Placement;
            CollisionResult wall = default;
            if (CollisionDetection.CheckBetweenPoints(Position, position, TestFlags.Players, _scene, ref wall))
                return BombAdmissionReason.Placement;
            bomb = SpawnBomb();
            return bomb != null ? BombAdmissionReason.None : BombAdmissionReason.PoolEmpty;
        }
    }
}
