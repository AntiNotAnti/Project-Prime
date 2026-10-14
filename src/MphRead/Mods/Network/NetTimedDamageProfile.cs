using System;
using MphRead.Entities;
using MphRead.Mods.Multiplayer;

namespace MphRead.Mods.Network;

// Immutable victim-side facts from one authority world frame. Packed values are
// the actual nine native Effectiveness entries, never shooter/claim modifiers.
internal readonly record struct TimedVictimDamageProfile(ShotKey Key, uint PackedEffectiveness,
    float DamageLevel, byte Handicap, Hunter Hunter, bool Balanced, bool Transitioning)
{
    internal uint CompleteDamage(float channel, BeamType beam, bool nativeHeadshot, bool self)
    {
        int index = (int)beam;
        if (index is < 0 or >= 9 || !float.IsFinite(channel) || channel < 0
            || channel > int.MaxValue || !float.IsFinite(DamageLevel) || DamageLevel <= 0)
            throw new ArgumentOutOfRangeException(nameof(channel), "Ordinary damage requires a finite channel and known native profile.");
        int effectiveness = (int)((PackedEffectiveness >> (index * 2)) & 3);
        if (effectiveness >= Metadata.DamageMultipliers.Count)
            throw new InvalidOperationException("Unsupported native susceptibility index.");
        // Native collision truncates before TakeDamage. Each following positive
        // susceptibility/level result independently retains a one-HP minimum.
        uint damage = (uint)channel;
        if (damage == 0 || effectiveness == (int)Effectiveness.Zero) return 0;
        damage = Math.Max(1u, (uint)(damage * Metadata.DamageMultipliers[effectiveness]));
        damage = Math.Max(1u, (uint)(damage * DamageLevel));
        if (Handicap != 0) damage = PlayerHandicap.ScaleDamage(damage, Handicap);
        if (Balanced && !self)
        {
            damage = BalancedModeRules.ScaleIncomingDamage(Hunter, damage, nativeHeadshot);
            if (Hunter == Hunter.Weavel)
                damage = BalancedHunterAbilityRules.WeavelTransitionDamage(damage, nativeHeadshot, Transitioning);
        }
        return damage;
    }
}

// 8 owners x 128 fixed value cells; no per-frame objects or dynamic containers.
// Exact stamps and complete identities turn eviction/unknown facts into false,
// never a fabricated susceptibility, armour reduction, or default damage level.
internal static class NetTimedDamageProfile
{
    internal const int Slots = 8, HistoryFrames = 128, BeamCount = 9;
    private struct Cell
    {
        internal bool Live;
        internal uint Frame;
        internal TimedVictimDamageProfile Value;
    }
    private static readonly Cell[,] Cells = new Cell[Slots, HistoryFrames];
    private static bool CurrentStream(in ShotKey key)
        => key.AuthorityEpoch != 0 && key.MatchId != 0
            && key.AuthorityEpoch == NetSession.AuthorityEpoch && key.MatchId == NetSession.CurrentMatchId;

    internal static bool TryPackEffectiveness(ReadOnlySpan<Effectiveness> values, out uint packed)
    {
        packed = 0;
        if (values.Length != BeamCount) return false;
        // Refuse an unexpected enum mapping instead of guessing that its numeric
        // bits still describe the native 0/.5/1/2 susceptibility table.
        if ((int)Effectiveness.Zero != 0 || (int)Effectiveness.Half != 1
            || (int)Effectiveness.Normal != 2 || (int)Effectiveness.Double != 3
            || Metadata.DamageMultipliers.Count != 4
            || Metadata.DamageMultipliers[0] != 0 || Metadata.DamageMultipliers[1] != .5f
            || Metadata.DamageMultipliers[2] != 1 || Metadata.DamageMultipliers[3] != 2) return false;
        uint candidate = 0;
        for (int beam = 0; beam < BeamCount; beam++)
        {
            uint value = (uint)values[beam];
            if (value > 3) return false;
            candidate |= value << (beam * 2);
        }
        packed = candidate; return true;
    }

    internal static void Record(uint frame)
    {
        if (!NetSession.Active || !NetSession.IsAuthority
            || NetSession.AuthorityEpoch == 0 || NetSession.CurrentMatchId == 0) return;
        int index = (int)(frame % HistoryFrames);
        for (int slot = 0; slot < Slots; slot++)
        {
            ref var cell = ref Cells[slot, index];
            // An accidental second recording cannot mutate the first retained
            // facts for this authority frame, even after a same-frame death.
            if (cell.Live && cell.Frame == frame && CurrentStream(cell.Value.Key)) continue;
            cell = default;
            if (slot >= PlayerEntity.Players.Count) continue;
            var player = PlayerEntity.Players[slot];
            if (player == null || player.SlotIndex != slot || player.SceneServices.IsReplica
                || !player.OwningScene.GameState.Multiplayer || !player.ModIsInPlay
                || !player.LoadFlags.TestFlag(LoadFlags.Active)
                || player.Flags2.TestFlag(PlayerFlags2.Spectating)) continue;
            var key = ShotKey.For(slot, 0);
            if (!CurrentStream(key) || key.Generation == 0 || key.LifeId == 0
                || !TryPackEffectiveness(player.BeamEffectiveness, out uint packed)) continue;
            var game = player.OwningScene.GameState;
            if ((uint)game.DamageLevel >= Metadata.DamageLevels.Count
                || slot >= NetSession.SlotDamageReduction.Length) continue;
            float damageLevel = Metadata.DamageLevels[game.DamageLevel];
            if (!float.IsFinite(damageLevel) || damageLevel <= 0) continue;
            cell = new Cell { Live = true, Frame = frame,
                Value = new(key, packed, damageLevel, NetSession.SlotDamageReduction[slot],
                    player.Hunter, game.BalancedMode, player.IsMorphing || player.IsUnmorphing) };
        }
    }

    internal static bool TryGet(int slot, ushort generation, ushort life, uint frame,
        out TimedVictimDamageProfile profile)
    {
        profile = default;
        if ((uint)slot >= Slots || generation == 0 || life == 0
            || unchecked(NetSession.NetFrame - frame) >= HistoryFrames) return false;
        ref readonly var cell = ref Cells[slot, (int)(frame % HistoryFrames)];
        if (!cell.Live || cell.Frame != frame || !CurrentStream(cell.Value.Key)
            || cell.Value.Key.ShooterSlot != slot || cell.Value.Key.Generation != generation
            || cell.Value.Key.LifeId != life || cell.Value.Key.ShotId != 0) return false;
        profile = cell.Value; return true;
    }

    internal static void Reset() => Array.Clear(Cells);
}
