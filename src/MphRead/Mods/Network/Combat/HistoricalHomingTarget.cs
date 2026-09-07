using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network
{
    /// <summary>Homing reads the same immutable player timeline as catch-up collision.</summary>
    internal static class HistoricalHomingTarget
    {
        internal static bool TryGet(ServerCombat combat, EntityBase entity, uint tick,
            CombatActor expected, out Vector3 position, out CombatActor identity)
        {
            PlayerEntity? player = entity as PlayerEntity ?? (entity as HalfturretEntity)?.Owner;
            if (player == null)
            {
                // World objects use current geometry, just like projectile map
                // collision. No historical world rollback is approximated here.
                identity = default;
                entity.GetPosition(out position);
                return entity.GetTargetable();
            }
            identity = player.ServerCombatIdentity;
            position = default;
            if (!identity.IsValid || expected.IsValid && expected != identity) return false;
            LagCompensationState state;
            if (tick == combat.Tick)
                state = LagCompensationState.Capture(player, identity.ConnectionId, identity.Life);
            else if (!combat.History.TryGet(player.SlotIndex, tick, identity.ConnectionId, identity.Life, out state))
                return false;
            if (!state.CanBeHit) return false;
            if (entity is HalfturretEntity)
            {
                if (!state.HasHalfturret) return false;
                position = state.HalfturretPosition;
            }
            else position = state.Position.AddY(state.AltForm ? 0 : 0.5f); // PlayerEntity.GetPosition
            return true;
        }
    }
}
