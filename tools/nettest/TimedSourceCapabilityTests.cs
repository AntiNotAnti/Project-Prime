using System;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Constructed evidence tests classification, not emission, collision or payout.
internal static class TimedSourceCapabilityTests
{
    private static int _checks;
    private static void Check(bool ok, string label)
    { if (!ok) throw new InvalidOperationException("Timed source capability: " + label); _checks++; }
    private static TimedShotCertificate Shot(bool granted = false, bool hint = false)
    {
        var key = new ShotKey(11, 7, 0, 2, 3, 1);
        var fire = new FireEvent(1, 102, 100, 0, FireEventKind.PressFire,
            (byte)BeamType.PowerBeam, 0, hint ? FireEvent.DoubleStateBit : 0, FireEvent.FlagPose,
            Vector3.Zero, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, SourceLife: key.LifeId);
        var source = new ShotSourceEvidence(ShotProofReason.Verified, ShotProofReason.Verified, !hint, 0, 0, 10);
        var knowledge = granted ? TimedGrantKnowledge.Granted : TimedGrantKnowledge.NotGranted;
        var answer = new TimedGrantAnswer(knowledge, granted ? 55UL : 0);
        TimedShotFunding? funding = granted ? new(55, new(11, 7, 3, -1, 4),
            key with { ShotId = 0 }, TimedPowerUp.DoubleDamage, 100, 100, 1900, 102) : null;
        var damage = new TimedShotDamageTemplate(12, 16, 0, Vector3.Zero, Vector3.UnitZ,
            .1f, 3, 0, 2, false, 0, true);
        return new(key, fire, 3, 102, TimedShotSource.NativeInput, source, answer,
            knowledge, funding, TimedShotUnavailable.None, 1, damage);
    }
    private static void OrdinaryTruth()
    {
        var absent = Shot(hint: true);
        var known = NetTimedSourceCapabilities.Classify(absent, ShotProofFamily.Projectile);
        Check(known.Modifier == TimedSourceCapability.SUPPORTED && !absent.AuthorizedDouble,
            "known absence supports declaration while forged hint authorizes no multiplier");
        var granted = Shot(granted: true);
        known = NetTimedSourceCapabilities.Classify(granted, ShotProofFamily.Projectile);
        Check(known.Modifier == TimedSourceCapability.SUPPORTED && granted.AuthorizedDouble,
            "funded source declaration is independent of a false Double hint");
        Check(known.ConstantDirectBodyChannel && known.ImpactExact == TimedSourceCapability.UNAVAILABLE,
            "constant arithmetic is not promoted into physical impact proof");
        var unsupported = NetTimedSourceCapabilities.Classify(granted, ShotProofFamily.Projectile, timedScope: false);
        Check(unsupported.Modifier == TimedSourceCapability.NOT_APPLICABLE
            && unsupported.ImpactExact == TimedSourceCapability.NOT_APPLICABLE, "Off context does not activate policy");
        var variants = new[]
        {
            granted with { Funding = null },
            granted with { LedgerAnswer = new(TimedGrantKnowledge.Granted, 99) },
            granted with { Funding = granted.Funding!.Value with { Life = granted.Key with { LifeId = 4, ShotId = 0 } } },
            granted with { Funding = granted.Funding!.Value with { Spawn = granted.Funding.Value.Spawn with { Room = 4 } } },
            granted with { Source = granted.Source with { Source = ShotProofReason.SourceHistoryIncomplete } },
            granted with { Source = granted.Source with { Pose = ShotProofReason.InvalidRay } },
            granted with { Knowledge = TimedGrantKnowledge.Unavailable, Unavailable = TimedShotUnavailable.ObservationMissing },
            granted with { Components = 0 },
            granted with { Fire = granted.Fire with { SourceLife = 4 } },
            granted with { Key = granted.Key with { Generation = 0 } },
            absent with { Funding = granted.Funding }
        };
        foreach (var value in variants)
            Check(NetTimedSourceCapabilities.Classify(value, ShotProofFamily.Projectile).Modifier
                == TimedSourceCapability.UNAVAILABLE, "missing/conflicting evidence never declares supported funding");
        var falloff = absent with { Damage = absent.Damage with { Reach = 10 } };
        Check(!NetTimedSourceCapabilities.Classify(falloff, ShotProofFamily.Projectile).ConstantDirectBodyChannel,
            "distance-dependent launch channel requires impact facts");
        var bad = absent with { Damage = absent.Damage with { Body = float.NaN } };
        Check(!NetTimedSourceCapabilities.Classify(bad, ShotProofFamily.Projectile).ConstantDirectBodyChannel,
            "nonfinite channel is not arithmetic evidence");
    }
    private static void DependentFamilies()
    {
        var shot = Shot(granted: true);
        foreach (var family in new[] { ShotProofFamily.Child, ShotProofFamily.Continuous,
            ShotProofFamily.Turret, ShotProofFamily.BombOrMelee })
        {
            var value = NetTimedSourceCapabilities.Classify(shot, family);
            Check(value.Modifier == TimedSourceCapability.SHADOW_ONLY
                && value.ImpactExact == TimedSourceCapability.SHADOW_ONLY,
                "known originating grant cannot invent dependent-family completeness " + family);
        }
        foreach (byte weapon in new[] { (byte)BeamType.Judicator, (byte)BeamType.OmegaCannon, (byte)BeamType.ShockCoil })
            Check(NetTimedSourceCapabilities.Classify(shot with { Fire = shot.Fire with { Weapon = weapon } },
                ShotProofFamily.Projectile).Modifier == TimedSourceCapability.SHADOW_ONLY,
                "special native weapon stays shadow only " + weapon);
        foreach (var family in new[] { ShotProofFamily.Homing, ShotProofFamily.Ricochet })
        {
            var value = NetTimedSourceCapabilities.Classify(shot, family);
            Check(value.Modifier == TimedSourceCapability.SUPPORTED
                && value.ImpactExact == TimedSourceCapability.SHADOW_ONLY,
                "originating modifier truth does not prove downstream impact " + family);
        }
        var oldLife = shot.Key;
        uint child = NetShotProof.ComponentId(1, 1);
        var native = NetTimedSourceCapabilities.NativeValue(oldLife, child, ShotProofFamily.Child,
            300, timedScope: true, playerSource: true, (byte)BeamType.PowerBeam);
        Check(native.Key == oldLife && native.Component == child
            && native.Capability.Modifier == TimedSourceCapability.SHADOW_ONLY,
            "child telemetry retains native inherited full key without rebinding the owner's newer life");
        Check(NetTimedSourceCapabilities.NativeValue(oldLife, 1, ShotProofFamily.Projectile,
            300, true, false, (byte)BeamType.PowerBeam).Capability.Modifier == TimedSourceCapability.NOT_APPLICABLE,
            "non-player native source cannot become a player grant declaration");
        Check(NetTimedSourceCapabilities.Family(false, false, true, true, true, (byte)BeamType.PowerBeam)
            == ShotProofFamily.Child, "actual parent dominates homing/ricochet family classification");
        Check(NetTimedSourceCapabilities.Family(true, true, true, true, true, (byte)BeamType.ShockCoil)
            == ShotProofFamily.Continuous, "continuous flag preserves its distinct pulse family");
        Check(NetTimedSourceCapabilities.Family(false, true, false, false, false, (byte)BeamType.Battlehammer)
            == ShotProofFamily.Turret, "native turret owner stays distinct from ordinary projectile");
    }
    internal static int Run()
    {
        _checks = 0;
        try
        {
            OrdinaryTruth(); DependentFamilies();
            Console.WriteLine($"PASS: {_checks} pure timed source capability assertions.");
            Console.WriteLine("SCOPE: constructed values and classifier; NOT_RUN: native emission, impact, UDP, replay, special-family Enforce.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
