using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.Mods.EnhancedHunters;
internal static class NoxusEnhancement
{
    private static bool Valid(PlayerEntity owner, PlayerEntity target)
    {
        var s = owner.EnhancedState; int slot = target.SlotIndex;
        return s.FrostExpiry[slot] > (int)owner.OwningScene.FrameCount
            && s.FrostLives[slot] == EnhancedHunters.Life(target)
            && s.FrostGenerations[slot] == EnhancedHunters.Generation(target);
    }
    private static void Stamp(PlayerEntity owner, PlayerEntity target, int frames)
    {
        var s = owner.EnhancedState; int slot = target.SlotIndex;
        s.FrostExpiry[slot] = (int)owner.OwningScene.FrameCount + frames;
        s.FrostLives[slot] = EnhancedHunters.Life(target);
        s.FrostGenerations[slot] = EnhancedHunters.Generation(target);
    }
    internal static void Hit(PlayerEntity owner, PlayerEntity target, bool charged, bool wasFrozen)
    {
        var s = owner.EnhancedState;
        int slot = target.SlotIndex;
        bool owned = Valid(owner, target);
        if (owned && wasFrozen && s.Brittle[slot])
        {
            int tier = s.BrittleTiers[slot];
            s.FrostExpiry[slot] = 0; s.Brittle[slot] = false; s.FrostStacks[slot] = 0;
            s.ClearTarget(); target.ModSetFrozen(false);
            if (charged)
            {
                foreach (var other in owner.OwningScene.Players.Items)
                {
                    if (other == target || !EnhancedHunters.Hostile(owner, other)
                        || (other.Position - target.Position).LengthSquared > 16
                        || !EnhancedHunters.Visible(owner.OwningScene, target.Position, other.Position)) continue;
                    EnhancedHunters.Bonus(owner, other, 6, Vector3.Zero);
                    Frost(owner, other);
                }
                EnhancedHunterTelemetry.Event(owner.Hunter, "flash-freezes");
            }
            else
            {
                var delta = target.Position - owner.Position;
                EnhancedHunters.Bonus(owner, target, 8 + tier,
                    (delta.LengthSquared > .001f ? delta.Normalized() : Vector3.UnitY) * .15f);
                EnhancedHunterTelemetry.Event(owner.Hunter, "shatters");
            }
        }
        else if (charged && target.ModFrozen)
        {
            byte tier = owned ? s.FrostStacks[slot] : (byte)0;
            Stamp(owner, target, 180); s.Brittle[slot] = true; s.BrittleTiers[slot] = tier;
            s.ClearTarget(); EnhancedHunters.Mark(owner, target, 180);
            s.Flags = 1; s.ValueB = tier;
        }
        else if (!charged) Frost(owner, target);
    }
    internal static void Frost(PlayerEntity owner, PlayerEntity target)
    {
        var s = owner.EnhancedState;
        if (EnhancedHunters.Target(owner) != target) s.ClearTarget();
        EnhancedHunters.Mark(owner, target, EnhancedHunterTuning.FrostFrames);
        int slot = target.SlotIndex;
        byte previous = Valid(owner, target) ? s.FrostStacks[slot] : (byte)0;
        Stamp(owner, target, EnhancedHunterTuning.FrostFrames);
        s.Brittle[slot] = false;
        s.ValueA = s.FrostStacks[slot] = (byte)System.Math.Min(3, previous + 1);
        EnhancedHunterTelemetry.Event(owner.Hunter, "frost-stacks");
    }
    internal static void AltHit(PlayerEntity owner, PlayerEntity target)
    {
        if (!Valid(owner, target) || !target.ModFrozen || !owner.EnhancedState.Brittle[target.SlotIndex]) return;
        owner.EnhancedState.FrostExpiry[target.SlotIndex] = 0;
        owner.EnhancedState.Brittle[target.SlotIndex] = false;
        owner.EnhancedState.ClearTarget(); target.ModSetFrozen(false);
        EnhancedHunters.Bonus(owner, target, 6, Vector3.UnitY * .22f);
        EnhancedHunterTelemetry.Event(owner.Hunter, "cryo-launches");
    }
}
