using MphRead.Entities;
using MphRead.Mods.Multiplayer;

namespace MphRead.Mods.Network;

// No late source/owner reconstruction. Missing historical effect state removes
// only this additional effect; the independently admitted base hit is retained.
internal static class NetTimedShotEffects
{
    internal static bool SameEffectClock(in TimedVictimDamageProfile profile,
        uint claimedFrame, uint authorityFrame)
        => profile.EffectStateKnown && profile.EffectFrame == claimedFrame && claimedFrame == authorityFrame;

    internal static bool ApplyDisruption(PlayerEntity victim, in TimedShotCertificate shot,
        in HitClaimPacket claim, TimedVictimDamageProfile? retainedProfile)
    {
        if (!NetTimedShotModifiers.Enforcing || !shot.Damage.Present
            || !shot.TrustedSource || claim.ComponentId == 0 || claim.ComponentId > shot.Components
            || shot.Fire.Weapon != claim.Beam || shot.Key != NetShotProof.Key(shot.Key.ShooterSlot, claim)
            || !NetPlayerLifecycle.Matches(victim.SlotIndex, claim.VictimGeneration, claim.VictimLifeId)
            || victim.SlotIndex != claim.VictimSlot || victim.Health <= 0
            || retainedProfile is not { } effectProfile
            || !SameEffectClock(effectProfile, claim.AckFrame, NetSession.NetFrame)
            || effectProfile.Key != ShotKey.For(victim.SlotIndex, 0)
            || (claim.Flags & HitClaimPacket.FlagHalfturret) != 0
            || (shot.Damage.EffectsFor((claim.Flags & HitClaimPacket.FlagDirect) != 0)
                & claim.Flags & HitClaimPacket.FlagDisrupted) == 0) return false;
        if (shot.Damage.DisruptionPolicy == TimedDisruptionPolicy.Ordinary)
            return victim.ModApplyNativeDisruption(balancedKanden: false);
        if (shot.Damage.DisruptionPolicy != TimedDisruptionPolicy.BalancedKanden
            || !effectProfile.AllowsBalancedDisruption
            || (claim.Flags & HitClaimPacket.FlagDirect) == 0
            || claim.Beam != (byte)BeamType.VoltDriver) return false;
        // Two zero timers do not prove an intervening immunity interval. This
        // path therefore applies only in the same sampled authority frame;
        // late additional afflictions remain unavailable, retaining base damage.
        bool applied = victim.ModApplyNativeDisruption(balancedKanden: true);
        BalancedModeTelemetry.NoteKandenDisrupt(victim, applied);
        return applied;
    }
}
