using System;
using System.Collections.Generic;
using MphRead.Entities;
using MphRead.Mods.Multiplayer;

namespace MphRead.Mods.Network;

internal enum TimedSourceCapability { SUPPORTED, SHADOW_ONLY, UNAVAILABLE, NOT_APPLICABLE }
internal readonly record struct TimedSourceCapabilities(TimedSourceCapability Modifier,
    TimedSourceCapability ImpactExact, bool ConstantDirectBodyChannel);
internal readonly record struct TimedNativeCapability(ShotKey Key, uint Component,
    ShotProofFamily Family, uint AuthorityFrame, uint? SourceFrame, TimedSourceCapabilities Capability, uint Room = 0);

// Describes evidence; never authorizes damage, changes source history or creates
// a grant. Constant launch arithmetic is distinct from a physical impact witness.
internal static class NetTimedSourceCapabilities
{
    internal const int NativePerSlot = 256;
    private static readonly TimedNativeCapability[,] Native = new TimedNativeCapability[8, NativePerSlot];
    private static readonly int[] Next = new int[8];
    private static readonly long[] Families = new long[Enum.GetValues<ShotProofFamily>().Length];
    private static readonly long[] Modifiers = new long[4], Impacts = new long[4];
    private static long _observed, _completed, _replaced;

    internal static ShotProofFamily Family(bool continuous, bool turret, bool child,
        bool homing, bool ricochet, byte weapon)
        => continuous ? ShotProofFamily.Continuous : turret ? ShotProofFamily.Turret
            : child ? ShotProofFamily.Child : homing ? ShotProofFamily.Homing
            : ricochet ? ShotProofFamily.Ricochet : weapon == (byte)BeamType.Imperialist
                ? ShotProofFamily.Straight : ShotProofFamily.Projectile;

    private static bool Dependent(ShotProofFamily family)
        => family is ShotProofFamily.Child or ShotProofFamily.Continuous
            or ShotProofFamily.Turret or ShotProofFamily.BombOrMelee;
    private static bool ValidKey(ShotKey key) => (uint)key.ShooterSlot < 8
        && key.AuthorityEpoch != 0 && key.MatchId != 0 && key.Generation != 0
        && key.LifeId != 0 && key.ShotId != 0;

    internal static TimedNativeCapability NativeValue(ShotKey key, uint component,
        ShotProofFamily family, uint frame, bool timedScope, bool playerSource, byte weapon, uint room = 0)
    {
        var state = !timedScope || !playerSource ? TimedSourceCapability.NOT_APPLICABLE
            : Dependent(family) || !NetTimedShotModifiers.Ordinary(weapon)
                ? TimedSourceCapability.SHADOW_ONLY : TimedSourceCapability.UNAVAILABLE;
        return new(key, component, family, frame, null, new(state, state, false), room);
    }

    internal static TimedSourceCapabilities Classify(in TimedShotCertificate shot,
        ShotProofFamily family, bool timedScope = true)
    {
        if (!timedScope) return new(TimedSourceCapability.NOT_APPLICABLE,
            TimedSourceCapability.NOT_APPLICABLE, false);
        if (Dependent(family) || shot.Fire.Kind is FireEventKind.ContinuousTick or FireEventKind.TurretFire
            || !NetTimedShotModifiers.Ordinary(shot.Fire.Weapon)
            || shot.Unavailable == TimedShotUnavailable.Unsupported)
            return new(TimedSourceCapability.SHADOW_ONLY, TimedSourceCapability.SHADOW_ONLY, false);
        bool completed = ValidKey(shot.Key) && shot.Fire.ShotId == shot.Key.ShotId
            && shot.Fire.SourceLife == shot.Key.LifeId && shot.Components > 0 && shot.Damage.Present;
        bool truth = shot.LedgerAnswer.Knowledge == shot.Knowledge
            && (shot.Knowledge == TimedGrantKnowledge.NotGranted
                ? shot.Funding == null && shot.LedgerAnswer.Sequence == 0
                : shot.Knowledge == TimedGrantKnowledge.Granted && shot.Funding is { } funding
                    && funding.Sequence != 0 && funding.Sequence == shot.LedgerAnswer.Sequence
                    && funding.Type == TimedPowerUp.DoubleDamage
                    && funding.Life == (shot.Key with { ShotId = 0 })
                    && funding.Spawn.Epoch == shot.Key.AuthorityEpoch
                    && funding.Spawn.Match == shot.Key.MatchId && funding.Spawn.Room == shot.Room
                    && funding.Spawn.Generation != 0);
        var modifier = completed && truth && shot.TrustedSource && shot.Unavailable == TimedShotUnavailable.None
            ? TimedSourceCapability.SUPPORTED : TimedSourceCapability.UNAVAILABLE;
        bool constantBody = completed && float.IsFinite(shot.Damage.Body) && shot.Damage.Body >= 0
            && float.IsFinite(shot.Damage.Reach) && shot.Damage.Reach <= 0
            && (!shot.Damage.Balanced || !BalancedModeRules.HasRangeDamageCurve((BeamType)shot.Fire.Weapon));
        var impact = family is ShotProofFamily.Homing or ShotProofFamily.Ricochet
            ? TimedSourceCapability.SHADOW_ONLY : TimedSourceCapability.UNAVAILABLE;
        // Neither a constant amount nor a client body-origin HitPoint proves an
        // impact. Native/paid witnesses remain owned by existing payment paths.
        return new(modifier, impact, constantBody);
    }

    internal static void ObserveEmission(BeamProjectileEntity beam, BeamProjectileEntity? parent)
    {
        if (!NetCampaignEvidence.Enabled || !NetSession.Active || !NetSession.IsAuthority
            || beam.OwningScene.Services.IsReplica) return;
        var family = Family(beam.Flags.TestFlag(BeamFlags.Continuous), beam.Owner is HalfturretEntity,
            parent != null, beam.Homing > 0, beam.Flags.TestFlag(BeamFlags.Ricochet)
                || beam.RicochetWeapon != null || beam.EnhancedBounceCount > 0, (byte)beam.Beam);
        bool player = beam.Owner is PlayerEntity || beam.Owner is HalfturretEntity;
        var value = NativeValue(beam.ModLaunchKey, beam.ModComponentId, family, NetSession.NetFrame,
            NetTimedShotProof.Active && beam.OwningScene.GameState.Multiplayer, player, (byte)beam.Beam,
            NetPowerUpLedger.Ledger?.Room ?? 0);
        Families[(int)family]++; _observed++;
        if (!ValidKey(value.Key) || value.Component == 0) return;
        int slot = value.Key.ShooterSlot, at = Next[slot];
        if (Native[slot, at].Component != 0) _replaced++;
        Native[slot, at] = value; Next[slot] = (at + 1) % NativePerSlot;
    }

    internal static void Completed(in TimedShotCertificate shot)
    {
        if (!NetCampaignEvidence.Enabled || !ValidKey(shot.Key)) return;
        _completed++;
        var ordinary = Classify(shot, ShotProofFamily.Projectile);
        Modifiers[(int)ordinary.Modifier]++; Impacts[(int)ordinary.ImpactExact]++;
        for (int i = 0; i < NativePerSlot; i++)
        {
            ref var value = ref Native[shot.Key.ShooterSlot, i];
            if (value.Component == 0 || value.Key != shot.Key || value.Room != shot.Room
                || value.Component > shot.Components) continue;
            value = value with { SourceFrame = shot.Fire.SourceFrame, Capability = Classify(shot, value.Family) };
        }
    }

    internal static object Capture()
    {
        var retained = new List<object>(); // report-time only, never per frame
        foreach (var value in Native)
            if (value.Component != 0) retained.Add(new { value.Key, value.Component, value.Room,
                Family = value.Family.ToString(), value.AuthorityFrame, value.SourceFrame,
                Modifier = value.Capability.Modifier.ToString(), ImpactExact = value.Capability.ImpactExact.ToString(),
                value.Capability.ConstantDirectBodyChannel });
        var modifiers = new Dictionary<string, long>(); var impacts = new Dictionary<string, long>();
        foreach (var status in Enum.GetValues<TimedSourceCapability>())
        { modifiers[status.ToString()] = Modifiers[(int)status]; impacts[status.ToString()] = Impacts[(int)status]; }
        return new { Scope = "observed native terminals and completed originating receipts; no impact proof",
            NativePerSlot, Observed = _observed, Completed = _completed, ReplacedTelemetry = _replaced,
            Families = (long[])Families.Clone(), CompletedModifiers = modifiers, CompletedImpacts = impacts, Native = retained };
    }
    internal static void Reset()
    { Array.Clear(Native); Array.Clear(Next); Array.Clear(Families); Array.Clear(Modifiers); Array.Clear(Impacts);
      _observed = _completed = _replaced = 0; }
}
