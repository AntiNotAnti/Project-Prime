using System;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

// Constructed frozen facts exercise native arithmetic and gate contracts. They
// are not UDP evidence, native capture proof, or fast-path policy certification.
internal static class CombatFastDamageTests
{
    private static int _checks;
    private static readonly ShotKey SourceLife = new(11, 7, 0, 2, 3, 0);
    private static readonly ShotKey VictimLife = new(11, 7, 1, 4, 5, 0);
    private static readonly ShotPaymentKey Payment = new(SourceLife with { ShotId = 19 }, 1, 4, 5, 1, 1);
    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException("Fast frozen damage: " + label);
        _checks++;
    }
    private static FastNativeDamageGates Gates(ShotKey life)
        => new(life, 99, FastDamageCapturePhase.NativePreDamage, Complete: true,
            Health: 100, Active: true, Biped: true, Bot: false, SpawnProtected: false,
            MatchSpawnProtected: false, DamageInvulnerable: false, CameraBlocked: false,
            SuppressDamage: false, AimTrainer: false, ExistingStatus: false, Enhanced: false,
            Teams: false, FriendlyFire: false, InstaGib: false, OneInChamber: false,
            Team: TeamRules.NoTeam, Mode: GameMode.Battle);
    private static TimedVictimDamageProfile Profile(Effectiveness effectiveness = Effectiveness.Normal,
        float level = 1, byte handicap = 0)
    {
        uint packed = 0;
        for (int beam = 0; beam < 9; beam++) packed |= (uint)effectiveness << (beam * 2);
        return new(VictimLife, packed, level, handicap, Hunter.Samus, false, false,
            EffectStateKnown: true, EffectFrame: 99);
    }
    private static void Unavailable(in ShotPaymentKey payment, in FastNativeDamageGates source,
        in FastNativeDamageGates victim, in TimedVictimDamageProfile profile,
        FastDamageReason reason, string label, uint channel = 72)
    {
        var actual = NetCombatFastDamage.Evaluate(payment, source, victim, profile, channel, true);
        Check(actual.Status == FastDamageAvailability.Unavailable && actual.Reason == reason
            && actual.NominalDamage == 0 && actual.HealthDebit == 0 && !actual.Lethal && !actual.Head, label);
    }
    private static void Blocked(in FastNativeDamageGates source, in FastNativeDamageGates victim,
        in TimedVictimDamageProfile profile, FastDamageReason reason, string label, uint channel = 72)
    {
        var actual = NetCombatFastDamage.Evaluate(Payment, source, victim, profile, channel, true);
        Check(actual.Status == FastDamageAvailability.Blocked && actual.Reason == reason
            && actual.NominalDamage == 0 && actual.HealthDebit == 0 && !actual.Lethal && !actual.Head, label);
    }
    private static void NativeFormulaAndHealthDebit()
    {
        var source = Gates(SourceLife); var victim = Gates(VictimLife); var profile = Profile();
        var body = NetCombatFastDamage.Evaluate(Payment, source, victim, profile, 72, false);
        Check(body.Status == FastDamageAvailability.Available && body.Reason == FastDamageReason.NativeDamage
            && body.NominalDamage == 72 && body.HealthDebit == 72 && !body.Lethal && !body.Head,
            "native Classic Imperialist body channel retains its nominal and actual debit");
        var head = NetCombatFastDamage.Evaluate(Payment, source, victim, profile, 200, true);
        Check(head.Status == FastDamageAvailability.Available && head.NominalDamage == 200
            && head.HealthDebit == 100 && head.Lethal && head.Head,
            "native head channel overkill is distinct from actual available-health debit");
        var one = NetCombatFastDamage.Evaluate(Payment, source, victim with { Health = 1 }, profile, 200, true);
        Check(one.NominalDamage == 200 && one.HealthDebit == 1 && one.Lethal && one.Head,
            "one-health victim does not turn nominal damage into an invented 200-health payment");
        var largeBody = NetCombatFastDamage.Evaluate(Payment, source, victim, profile, 200, false);
        Check(largeBody.NominalDamage == 200 && !largeBody.Head,
            "damage size never fabricates a native head flag");
        var rounded = NetCombatFastDamage.Evaluate(Payment, source, victim,
            Profile(Effectiveness.Half, 1.25f, 50), 7, false);
        Check(rounded.Status == FastDamageAvailability.Available && rounded.NominalDamage == 1
            && rounded.HealthDebit == 1, "native serial truncation: seven to three to three to one");
        var minimum = NetCombatFastDamage.Evaluate(Payment, source, victim,
            Profile(Effectiveness.Half, .75f, 50), 1, false);
        Check(minimum.NominalDamage == 1 && minimum.HealthDebit == 1,
            "each positive susceptibility, damage-level and handicap scale preserves minimum one");
        var doubleDamage = NetCombatFastDamage.Evaluate(Payment, source, victim,
            Profile(Effectiveness.Double, 1.25f, 10), 7, false);
        Check(doubleDamage.NominalDamage == 15,
            "native double effectiveness seven to fourteen to seventeen then integer handicap fifteen");
        var wrapping = NetCombatFastDamage.Evaluate(Payment with { Shot = Payment.Shot with { ShotId = uint.MaxValue } },
            source with { Frame = 0 }, victim with { Frame = 0 }, profile with { EffectFrame = 0 }, 72, false);
        Check(wrapping.Status == FastDamageAvailability.Available && wrapping.HealthDebit == 72,
            "full uint shot identity and wrapped legal frame zero remain valid exact stamps");
    }
    private static void VerifiedBlocksAndOrdinaryTimer()
    {
        var source = Gates(SourceLife); var victim = Gates(VictimLife); var profile = Profile();
        Blocked(source, victim with { SpawnProtected = true }, profile, FastDamageReason.SpawnProtected,
            "verified native spawn immunity blocks");
        Blocked(source, victim with { MatchSpawnProtected = true }, profile, FastDamageReason.MatchSpawnProtected,
            "verified effective match-spawn immunity blocks");
        Blocked(source, victim with { CameraBlocked = true }, profile, FastDamageReason.CameraBlocked,
            "verified native scripted-camera block rejects damage");
        Blocked(source, victim, profile, FastDamageReason.ZeroChannel, "native zero channel remains zero", 0);
        Blocked(source, victim, Profile(Effectiveness.Zero), FastDamageReason.ZeroEffectiveness,
            "native zero susceptibility cannot become minimum-one damage");
        var ordinary = NetCombatFastDamage.Evaluate(Payment, source with { DamageInvulnerable = true },
            victim with { DamageInvulnerable = true }, profile, 72, false);
        Check(ordinary.Status == FastDamageAvailability.Available && ordinary.HealthDebit == 72,
            "derived native NoDmgInvuln ignores ordinary timers without modifying them");
    }
    private static void FrozenTeamRules()
    {
        var source = Gates(SourceLife); var victim = Gates(VictimLife); var profile = Profile();
        Check(!TeamRules.AreAllies(false, 2, -1, -1) && !TeamRules.AreAllies(true, 2, -1, -1)
            && TeamRules.AreAllies(true, 2, 0, 0) && !TeamRules.AreAllies(true, 2, 2, 2),
            "pure shared team rule excludes NoTeam and out-of-layout values");
        Check(NetCombatFastDamage.Evaluate(Payment, source with { TeamCount = 2 },
            victim with { TeamCount = 2 }, profile, 72, false).Status == FastDamageAvailability.Available,
            "native FFA retains unused default team-count metadata without allying minus-one slots");
        Check(NetCombatFastDamage.Evaluate(Payment, source with { TeamCount = 2, Team = 0 },
            victim with { TeamCount = 2, Team = 1 }, profile, 72, false).HealthDebit == 72,
            "native FFA slot-assigned teams remain opponents when Teams is false");
        Check(NetCombatFastDamage.Evaluate(Payment, source with { Team = 7 },
            victim with { Team = 7 }, profile, 72, false).HealthDebit == 72,
            "even equal valid FFA team metadata never enables friendly-fire blocking");
        source = source with { Teams = true, Mode = GameMode.BattleTeams, TeamCount = 2, Team = 0 };
        victim = victim with { Teams = true, Mode = GameMode.BattleTeams, TeamCount = 2, Team = 0 };
        Blocked(source, victim, profile, FastDamageReason.FriendlyFireBlocked, "same-team friendly-fire off blocks");
        Check(NetCombatFastDamage.Evaluate(Payment, source with { FriendlyFire = true },
            victim with { FriendlyFire = true }, profile, 72, false).HealthDebit == 72,
            "complete friendly-fire-on facts permit same-team ordinary damage");
        Check(NetCombatFastDamage.Evaluate(Payment, source, victim with { Team = 1 }, profile, 72, false)
            .Status == FastDamageAvailability.Available, "different valid teams permit damage");
        Unavailable(Payment, source, victim with { Team = -1 }, profile, FastDamageReason.InvalidTeam,
            "unassigned team cannot be treated as an opponent");
        Unavailable(Payment, source with { TeamCount = 0 }, victim with { TeamCount = 0 }, profile,
            FastDamageReason.InvalidTeam, "default team count is incomplete in a team match");
        Unavailable(Payment, source, victim with { FriendlyFire = true }, profile,
            FastDamageReason.ContextMismatch, "inconsistent friendly-fire facts cannot certify team damage");
    }
    private static void MissingAndMismatchedFacts()
    {
        var source = Gates(SourceLife); var victim = Gates(VictimLife); var profile = Profile();
        Unavailable(default, default, default, default, FastDamageReason.Incomplete, "defaults cannot certify damage");
        Unavailable(Payment, source with { Complete = false }, victim, profile, FastDamageReason.Incomplete,
            "partial source capture remains unavailable");
        Unavailable(Payment, source, victim with { Complete = false }, profile, FastDamageReason.Incomplete,
            "partial victim capture remains unavailable");
        Unavailable(Payment, source with { Life = SourceLife with { ShotId = 1 } }, victim, profile,
            FastDamageReason.InvalidLife, "gate requires a lifecycle key rather than an emitted-shot key");
        Unavailable(Payment, source with { Life = SourceLife with { AuthorityEpoch = 0 } }, victim, profile,
            FastDamageReason.InvalidLife, "zero authority epoch never supplies known identity");
        Unavailable(Payment, source with { Life = SourceLife with { LifeId = 4 } }, victim, profile,
            FastDamageReason.LifeMismatch, "source respawn cannot borrow another life snapshot");
        Unavailable(Payment, source, victim with { Life = VictimLife with { Generation = 5 } }, profile,
            FastDamageReason.LifeMismatch, "victim occupant replacement cannot borrow old generation");
        Unavailable(Payment with { Shot = Payment.Shot with { MatchId = 8 } }, source, victim, profile,
            FastDamageReason.LifeMismatch, "payment from another match cannot join same numeric lives");
        Unavailable(Payment, source, victim with { Phase = FastDamageCapturePhase.Unknown }, profile,
            FastDamageReason.CapturePhase, "unknown or post-contact capture is not pre-damage evidence");
        Unavailable(Payment, source with { Phase = (FastDamageCapturePhase)2 }, victim, profile,
            FastDamageReason.CapturePhase, "unknown phase enum does not default to native pre-damage");
        Unavailable(Payment, source, victim with { Frame = 100 }, profile, FastDamageReason.FrameMismatch,
            "same lifecycle at a different legal frame is unavailable");
        Unavailable(Payment, source, victim, profile with { EffectFrame = 98 }, FastDamageReason.ProfileMismatch,
            "pre-contact gates cannot join stale susceptibility/effect state");
        Unavailable(Payment, source, victim, profile with { EffectStateKnown = false }, FastDamageReason.ProfileMismatch,
            "unknown effect state cannot be repaired by default susceptibility");
        Unavailable(Payment, source, victim, profile with { Key = VictimLife with { LifeId = 6 } },
            FastDamageReason.ProfileMismatch, "damage profile requires the exact victim lifecycle");
        Unavailable(Payment, source, victim with { Health = 0 }, profile, FastDamageReason.InactiveOrDead,
            "dead victim is not an alive human contact candidate");
        Unavailable(Payment, source with { Active = false }, victim, profile, FastDamageReason.InactiveOrDead,
            "inactive source is outside the qualified candidate scope");
    }
    private static void UnsupportedAndInvalidProfiles()
    {
        var source = Gates(SourceLife); var victim = Gates(VictimLife); var profile = Profile();
        foreach (var other in new[] { Payment with { Component = 2 }, Payment with { Kind = 2 },
            Payment with { Kind = 5 }, Payment with { Phase = 1 } })
            Unavailable(other, source, victim, profile, FastDamageReason.UnsupportedPaymentFamily,
                "child/splash/turret/continuous dimensions do not inherit first-segment direct proof");
        foreach (var other in new[] { victim with { Biped = false }, victim with { Bot = true },
            victim with { SuppressDamage = true }, victim with { AimTrainer = true },
            victim with { ExistingStatus = true }, victim with { Enhanced = true },
            victim with { InstaGib = true }, victim with { OneInChamber = true },
            victim with { Mode = GameMode.Survival } })
            Unavailable(Payment, source, other, profile, FastDamageReason.UnsupportedContext,
                "unsupported actor/mode/effect context remains unavailable rather than fabricated blocked");
        Unavailable(Payment, source with { Enhanced = true }, victim, profile, FastDamageReason.UnsupportedContext,
            "unproven source enhanced effects are excluded too");
        foreach (var other in new[] { profile with { Balanced = true }, profile with { Transitioning = true } })
            Unavailable(Payment, source, victim, other, FastDamageReason.UnsupportedContext,
                "Balanced or transition-specific durability is outside initial Classic scope");
        foreach (var other in new[] { profile with { Handicap = 51 }, profile with { Handicap = 1 },
            profile with { DamageLevel = float.NaN }, profile with { DamageLevel = 2 },
            profile with { PackedEffectiveness = profile.PackedEffectiveness | (1u << 18) },
            profile with { Hunter = Hunter.Random } })
            Unavailable(Payment, source, victim, other, FastDamageReason.InvalidProfile,
                "invalid native profile cannot silently normalize into accepted damage");
        Unavailable(Payment, source, victim, profile, FastDamageReason.ChannelOutOfRange,
            "unrepresentable uint native channel does not silently round through float", uint.MaxValue);
        var self = new ShotPaymentKey(Payment.Shot, 0, 2, 3, 1, 1);
        Unavailable(self, source, source, profile, FastDamageReason.LifeMismatch,
            "self contact does not inherit distinct-enemy human damage scope");
    }
    public static int Run()
    {
        try
        {
            _checks = 0;
            NativeFormulaAndHealthDebit(); VerifiedBlocksAndOrdinaryTimer(); FrozenTeamRules();
            MissingAndMismatchedFacts(); UnsupportedAndInvalidProfiles();
            Console.WriteLine($"PASS: {_checks} constructed frozen native-damage assertions; no UDP/capture/policy certification");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
