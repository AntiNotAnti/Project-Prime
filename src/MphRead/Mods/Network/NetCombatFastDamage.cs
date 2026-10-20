using System;
using MphRead.Formats;
using MphRead.Mods.Multiplayer;

namespace MphRead.Mods.Network;

internal enum FastDamageCapturePhase : byte { Unknown, NativePreDamage }

// A caller must capture both actors before the native contact's TakeDamage call.
// Life is a full lifecycle key with ShotId zero; Frame is an equality stamp,
// including wrapped zero, rather than a source of cross-process time.
internal readonly record struct FastNativeDamageGates(ShotKey Life, uint Frame,
    FastDamageCapturePhase Phase, bool Complete, int Health, bool Active, bool Biped,
    bool Bot, bool SpawnProtected, bool MatchSpawnProtected, bool DamageInvulnerable,
    bool CameraBlocked, bool SuppressDamage, bool AimTrainer, bool ExistingStatus,
    bool Enhanced, bool Teams, bool FriendlyFire, bool InstaGib, bool OneInChamber,
    int Team, GameMode Mode, int TeamCount = 0);

internal enum FastDamageAvailability : byte { Unavailable, Available, Blocked }
internal enum FastDamageReason : byte
{
    Unknown, Incomplete, InvalidLife, LifeMismatch, CapturePhase, FrameMismatch,
    InactiveOrDead, UnsupportedPaymentFamily, UnsupportedContext, ContextMismatch,
    InvalidTeam, ProfileMismatch, InvalidProfile, ChannelOutOfRange,
    SpawnProtected, MatchSpawnProtected, CameraBlocked, FriendlyFireBlocked,
    ZeroChannel, ZeroEffectiveness, NativeDamage
}
internal readonly record struct FastNativeDamageResult(FastDamageAvailability Status,
    FastDamageReason Reason, uint NominalDamage = 0, uint HealthDebit = 0,
    bool Lethal = false, bool Head = false);

// Pure arithmetic for a separately qualified native Imperialist first-segment,
// human biped contact. This does not prove emission, geometry, ordering, source
// resources, policy admission, or absence of an earlier payment. It performs no
// gameplay calls, reservations, random draws, claims, or global-state reads.
internal static class NetCombatFastDamage
{
    private static FastNativeDamageResult Unknown(FastDamageReason reason)
        => new(FastDamageAvailability.Unavailable, reason);
    private static FastNativeDamageResult Blocked(FastDamageReason reason)
        => new(FastDamageAvailability.Blocked, reason);
    private static bool ValidLife(in ShotKey key)
        => key.AuthorityEpoch != 0 && key.MatchId != 0 && (uint)key.ShooterSlot < 8
            && key.Generation != 0 && key.LifeId != 0 && key.ShotId == 0;
    private static bool SupportedContext(in FastNativeDamageGates value)
        => value.Biped && !value.Bot && !value.SuppressDamage && !value.AimTrainer
            && !value.ExistingStatus && !value.Enhanced && !value.InstaGib && !value.OneInChamber
            && (value.Mode == GameMode.Battle || value.Mode == GameMode.BattleTeams);
    private static bool ValidTeam(in FastNativeDamageGates value)
        => value.Teams
            ? value.Mode == GameMode.BattleTeams && value.TeamCount is >= 2 and <= 4
                && value.Team >= 0 && value.Team < value.TeamCount
            : value.Mode == GameMode.Battle && (value.TeamCount == 0 || value.TeamCount is >= 2 and <= 4)
                && value.Team is >= TeamRules.NoTeam and < 8;

    internal static FastNativeDamageResult Evaluate(in ShotPaymentKey payment,
        in FastNativeDamageGates source, in FastNativeDamageGates victim,
        in TimedVictimDamageProfile profile, uint nativeChannel, bool nativeHead)
    {
        if (!source.Complete || !victim.Complete) return Unknown(FastDamageReason.Incomplete);
        if (!ValidLife(source.Life) || !ValidLife(victim.Life)
            || payment.Shot.ShotId == 0 || payment.Victim >= 8 || payment.Generation == 0 || payment.Life == 0)
            return Unknown(FastDamageReason.InvalidLife);
        var sourceLife = payment.Shot with { ShotId = 0 };
        var victimLife = new ShotKey(payment.Shot.AuthorityEpoch, payment.Shot.MatchId,
            payment.Victim, payment.Generation, payment.Life, 0);
        if (source.Life != sourceLife || victim.Life != victimLife
            || source.Life.ShooterSlot == victim.Life.ShooterSlot)
            return Unknown(FastDamageReason.LifeMismatch);
        if (source.Phase != FastDamageCapturePhase.NativePreDamage
            || victim.Phase != FastDamageCapturePhase.NativePreDamage)
            return Unknown(FastDamageReason.CapturePhase);
        if (source.Frame != victim.Frame) return Unknown(FastDamageReason.FrameMismatch);
        if (!source.Active || !victim.Active || source.Health <= 0 || victim.Health <= 0)
            return Unknown(FastDamageReason.InactiveOrDead);
        if (payment.Component != 1 || payment.Kind != 1 || payment.Phase != 0)
            return Unknown(FastDamageReason.UnsupportedPaymentFamily);
        if (!SupportedContext(source) || !SupportedContext(victim))
            return Unknown(FastDamageReason.UnsupportedContext);
        if (source.Mode != victim.Mode || source.Teams != victim.Teams
            || source.TeamCount != victim.TeamCount || source.FriendlyFire != victim.FriendlyFire)
            return Unknown(FastDamageReason.ContextMismatch);
        if (!ValidTeam(source) || !ValidTeam(victim)) return Unknown(FastDamageReason.InvalidTeam);
        if (profile.Key != victimLife || !profile.EffectStateKnown || profile.EffectFrame != victim.Frame)
            return Unknown(FastDamageReason.ProfileMismatch);
        if (profile.Balanced || profile.Transitioning) return Unknown(FastDamageReason.UnsupportedContext);
        bool levelKnown = false;
        for (int i = 0; i < Metadata.DamageLevels.Count; i++)
            levelKnown |= profile.DamageLevel == Metadata.DamageLevels[i];
        if (!levelKnown || !float.IsFinite(profile.DamageLevel) || profile.DamageLevel <= 0
            || !PlayerHandicap.IsValid(profile.Handicap) || (uint)profile.Hunter > (uint)Hunter.Weavel
            || (profile.PackedEffectiveness >> 18) != 0 || Metadata.DamageMultipliers.Count != 4
            || Metadata.DamageMultipliers[0] != 0 || Metadata.DamageMultipliers[1] != .5f
            || Metadata.DamageMultipliers[2] != 1 || Metadata.DamageMultipliers[3] != 2)
            return Unknown(FastDamageReason.InvalidProfile);
        // CompleteDamage takes float: refuse integer channels outside its exact
        // integer range instead of silently rounding a constructed uint input.
        if (nativeChannel > 16_777_216u) return Unknown(FastDamageReason.ChannelOutOfRange);

        if (victim.SpawnProtected) return Blocked(FastDamageReason.SpawnProtected);
        if (victim.MatchSpawnProtected) return Blocked(FastDamageReason.MatchSpawnProtected);
        if (victim.CameraBlocked) return Blocked(FastDamageReason.CameraBlocked);
        if (TeamRules.AreAllies(source.Teams, source.TeamCount, source.Team, victim.Team)
            && !source.FriendlyFire) return Blocked(FastDamageReason.FriendlyFireBlocked);
        if (nativeChannel == 0) return Blocked(FastDamageReason.ZeroChannel);
        if (((profile.PackedEffectiveness >> ((int)BeamType.Imperialist * 2)) & 3) == 0)
            return Blocked(FastDamageReason.ZeroEffectiveness);

        // Native stock beam contacts derive NoDmgInvuln. The ordinary timer is
        // intentionally not a rejection or a reason to refresh that timer.
        uint nominal = profile.CompleteDamage(nativeChannel, BeamType.Imperialist, nativeHead, self: false);
        uint debit = Math.Min(nominal, (uint)victim.Health);
        return new(FastDamageAvailability.Available, FastDamageReason.NativeDamage,
            nominal, debit, nominal >= (uint)victim.Health, nativeHead);
    }
}
