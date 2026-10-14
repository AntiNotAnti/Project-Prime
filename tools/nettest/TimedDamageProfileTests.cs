using System;
using MphRead.Entities;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

// Constructed immutable victim facts test the native arithmetic contract only.
// These fixtures do not prove a native Record call or authenticated hit admission.
internal static class TimedDamageProfileTests
{
    private static int _checks;
    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException("Timed victim damage: " + label);
        _checks++;
    }
    private static TimedVictimDamageProfile Profile(Effectiveness effectiveness = Effectiveness.Normal,
        float level = 1, byte handicap = 0, Hunter hunter = Hunter.Samus,
        bool balanced = false, bool transitioning = false)
    {
        var values = new Effectiveness[NetTimedDamageProfile.BeamCount];
        Array.Fill(values, effectiveness);
        Check(NetTimedDamageProfile.TryPackEffectiveness(values, out uint packed), "fixture packs known native susceptibility");
        return new(new ShotKey(11, 7, 0, 2, 3, 0), packed, level, handicap, hunter, balanced, transitioning);
    }
    private static void PackingAndMissing()
    {
        Check(NetTimedDamageProfile.Slots == 8 && NetTimedDamageProfile.HistoryFrames == 128
            && NetTimedDamageProfile.BeamCount == 9, "fixed victim history and native beam count");
        var values = new Effectiveness[9];
        for (int i = 0; i < values.Length; i++) values[i] = (Effectiveness)(i % 4);
        Check(NetTimedDamageProfile.TryPackEffectiveness(values, out uint packed), "four native values pack independently across nine beams");
        var profile = new TimedVictimDamageProfile(new(11, 7, 0, 2, 3, 0), packed, 1, 0, Hunter.Samus, false, false);
        for (int i = 0; i < values.Length; i++)
        {
            uint expected = values[i] switch { Effectiveness.Zero => 0u,
                Effectiveness.Half => 5u, Effectiveness.Normal => 10u, _ => 20u };
            Check(profile.CompleteDamage(10, (BeamType)i, false, false) == expected,
                "each beam selects its own two-bit susceptibility, including Omega bit pair");
        }
        values[8] = (Effectiveness)4;
        Check(!NetTimedDamageProfile.TryPackEffectiveness(values, out packed) && packed == 0,
            "unsupported susceptibility value refuses the entire profile without partial bits");
        Check(!NetTimedDamageProfile.TryPackEffectiveness(new Effectiveness[8], out _)
            && !NetTimedDamageProfile.TryPackEffectiveness(new Effectiveness[10], out _),
            "malformed susceptibility count cannot fabricate omitted beam facts");
        NetTimedDamageProfile.Reset();
        Check(!NetTimedDamageProfile.TryGet(0, 2, 3, 0, out var missing) && missing == default,
            "missing frame zero is unavailable rather than a fabricated normal profile");
        foreach (int slot in new[] { -1, 8 })
            Check(!NetTimedDamageProfile.TryGet(slot, 2, 3, 1, out _), "invalid owner slot cannot index history");
        Check(!NetTimedDamageProfile.TryGet(0, 0, 3, 1, out _)
            && !NetTimedDamageProfile.TryGet(0, 2, 0, 1, out _), "zero victim generation/life remains unavailable");
    }
    private static void SerialRoundingAndZero()
    {
        var half = Profile(Effectiveness.Half, 1.25f);
        Check(half.CompleteDamage(7.9f, BeamType.Missile, false, false) == 3,
            "truncate collision7.9 to7, half to3, level1.25 to3; combined multiplier would wrongly pay4");
        Check(Profile(Effectiveness.Half, .75f, 50).CompleteDamage(1, BeamType.Magmaul, false, false) == 1,
            "positive one-HP floor survives susceptibility, level and handicap independently");
        Check(Profile(Effectiveness.Zero, 1.25f).CompleteDamage(200, BeamType.Imperialist, true, false) == 0,
            "zero susceptibility stays immune even for a genuine headshot");
        Check(Profile().CompleteDamage(.9f, BeamType.PowerBeam, false, false) == 0,
            "sub-one collision channel remains zero before positive-scaling minimum");
        Check(Profile().CompleteDamage(0, BeamType.Missile, false, false) == 0,
            "zero channel never becomes a phantom one-HP payment");
        Check(Profile(Effectiveness.Normal, 1.25f, 50).CompleteDamage(7, BeamType.Missile, false, false) == 4,
            "level7→8 then integer handicap8→4 follows native order");
        Check(Profile(Effectiveness.Normal, 1, 255).CompleteDamage(9, BeamType.Missile, false, false) == 4,
            "existing native handicap helper retains invalid-reduction fallback rather than multiplying a guessed percent");
        Check(Profile(Effectiveness.Double, 1.25f).CompleteDamage(72, BeamType.Imperialist, false, false) == 180,
            "captured susceptibility2 and matchlevel1.25 transform a72 channel once");
    }
    private static void BalancedVictimFacts()
    {
        var spire = Profile(hunter: Hunter.Spire, balanced: true);
        Check(spire.CompleteDamage(10, BeamType.PowerBeam, false, false) == 9,
            "Spire ordinary enemy body durability uses captured hunter");
        Check(spire.CompleteDamage(15, BeamType.Missile, false, false) == 14,
            "Spire13.5 rounds to even14, matching existing native helper");
        Check(spire.CompleteDamage(10, BeamType.PowerBeam, true, false) == 10,
            "genuine unequal-channel native headshot flag bypasses body durability");
        Check(spire.CompleteDamage(10, BeamType.Magmaul, false, true) == 10,
            "self splash skips enemy-only Balanced victim armour");
        Check(spire.CompleteDamage(56, BeamType.VoltDriver, false, false) == 50,
            "chargedVolt56/56 channels supply no native headshot flag and retain body armour");
        Check(spire.CompleteDamage(32, BeamType.Missile, false, false) == 29,
            "Missile32/32 equal channels cannot borrow geometric-head armour bypass");
        Check(Profile(hunter: Hunter.Noxus, balanced: true).CompleteDamage(10, BeamType.Missile, false, false) == 10,
            "Noxus9.5 uses midpoint-to-even10, not truncation9");
        var weavel = Profile(hunter: Hunter.Weavel, balanced: true, transitioning: true);
        Check(weavel.CompleteDamage(14, BeamType.Battlehammer, false, false) == 10,
            "captured Weavel transition14×.75 rounds even10");
        Check(weavel.CompleteDamage(14, BeamType.Imperialist, true, false) == 14,
            "genuine headshot bypasses captured Weavel transition armour");
        Check(weavel.CompleteDamage(14, BeamType.Magmaul, false, true) == 14,
            "transition armour does not alter self damage");
        Check((weavel with { Transitioning = false }).CompleteDamage(14, BeamType.Battlehammer, false, false) == 14,
            "immutable transition bit distinguishes ordinary body amount from reduced amount");
        Check((weavel with { Balanced = false }).CompleteDamage(14, BeamType.Battlehammer, false, false) == 14,
            "ordinary rules do not invent Balanced transition armour");
    }
    private static void InvalidArithmeticInputs()
    {
        var profile = Profile();
        foreach (float channel in new[] { float.NaN, float.PositiveInfinity, -1f })
        {
            bool threw = false;
            try { profile.CompleteDamage(channel, BeamType.Missile, false, false); }
            catch (ArgumentOutOfRangeException) { threw = true; }
            Check(threw, "malformed captured channel fails explicitly rather than making a normal damage profile");
        }
        bool invalidBeam = false;
        try { profile.CompleteDamage(10, BeamType.None, false, false); }
        catch (ArgumentOutOfRangeException) { invalidBeam = true; }
        Check(invalidBeam, "no-beam attack cannot borrow a packed beam susceptibility");
    }
    private static void NativeArithmeticVectorsAcrossEveryBeam()
    {
        // Independently tabulated values retain staged truncation, unlike a
        // combined scale or comparison against the same helper under test.
        // .75/1.25 are declared metadata arithmetic variants; the current native
        // GameState getter fixes actual match DamageLevel to its normal index1.
        var vectors = new (float Channel, Effectiveness Susceptibility, float Level,
            byte Handicap, uint Expected)[] {
            (0, Effectiveness.Double, 1.25f, 0, 0),
            (.99f, Effectiveness.Double, 1.25f, 0, 0),
            (1.99f, Effectiveness.Half, .75f, 50, 1),
            (7.9f, Effectiveness.Half, 1.25f, 0, 3),
            (14, Effectiveness.Half, 1.25f, 0, 8),
            (32, Effectiveness.Half, 1.25f, 0, 20),
            (56, Effectiveness.Half, 1.25f, 0, 35),
            (72, Effectiveness.Half, 1.25f, 0, 45),
            (200, Effectiveness.Half, 1.25f, 0, 125),
            (7.9f, Effectiveness.Normal, .75f, 30, 3),
            (14, Effectiveness.Normal, .75f, 30, 7),
            (32, Effectiveness.Normal, .75f, 30, 16),
            (56, Effectiveness.Normal, .75f, 30, 29),
            (72, Effectiveness.Normal, .75f, 30, 37),
            (200, Effectiveness.Normal, .75f, 30, 105),
            (7.9f, Effectiveness.Double, 1.25f, 50, 8),
            (14, Effectiveness.Double, 1.25f, 50, 17),
            (32, Effectiveness.Double, 1.25f, 50, 40),
            (56, Effectiveness.Double, 1.25f, 50, 70),
            (72, Effectiveness.Double, 1.25f, 50, 90),
            (200, Effectiveness.Double, 1.25f, 50, 250),
            (200, Effectiveness.Zero, .75f, 50, 0),
            (200, Effectiveness.Zero, 1, 0, 0),
            (200, Effectiveness.Zero, 1.25f, 10, 0)
        };
        foreach (var vector in vectors)
        {
            var profile = Profile(vector.Susceptibility, vector.Level, vector.Handicap);
            for (int beam = 0; beam < 9; beam++)
                Check(profile.CompleteDamage(vector.Channel, (BeamType)beam, false, false) == vector.Expected,
                    $"tabulated native staged arithmetic beam={beam} channel={vector.Channel} susceptibility={vector.Susceptibility} level={vector.Level} handicap={vector.Handicap}");
        }
        var handicaps = new (byte Reduction, uint Expected)[] {
            (0, 101), (10, 90), (20, 80), (30, 70), (40, 60), (50, 50) };
        foreach (var handicap in handicaps)
        {
            var profile = Profile(handicap: handicap.Reduction);
            for (int beam = 0; beam < 9; beam++)
                Check(profile.CompleteDamage(101, (BeamType)beam, false, false) == handicap.Expected,
                    "all six roster handicap values retain integer-percent behavior for every beam");
        }
        var weavel = Profile(hunter: Hunter.Weavel, balanced: true, transitioning: true);
        foreach (var edge in new (uint Channel, uint Body)[] {
            (2, 2), (6, 4), (10, 8), (14, 10), (18, 14), (22, 16), (30, 22) })
            for (int beam = 0; beam < 9; beam++)
            {
                Check(weavel.CompleteDamage(edge.Channel, (BeamType)beam, false, false) == edge.Body,
                    "Weavel transition half-integer rounds to even, including downward edges");
                Check(weavel.CompleteDamage(edge.Channel, (BeamType)beam, true, false) == edge.Channel,
                    "native headshot flag bypasses transition reduction for every packed susceptibility channel");
                Check(weavel.CompleteDamage(edge.Channel, (BeamType)beam, false, true) == edge.Channel,
                    "self identity bypasses enemy transition armour for every beam");
            }
        foreach (var hunter in new[] { Hunter.Samus, Hunter.Kanden, Hunter.Spire, Hunter.Noxus,
            Hunter.Weavel, Hunter.Trace, Hunter.Sylux })
        {
            var profile = Profile(hunter: hunter, balanced: true);
            uint body = hunter is Hunter.Spire or Hunter.Noxus ? 13u : 14u;
            for (int beam = 0; beam < 9; beam++)
            {
                Check(profile.CompleteDamage(14, (BeamType)beam, false, false) == body,
                    "all seven captured native hunters select the correct Balanced body profile");
                Check(profile.CompleteDamage(14, (BeamType)beam, true, false) == 14,
                    "genuine native head flag preserves precision channel through all hunter profiles");
                Check(profile.CompleteDamage(14, (BeamType)beam, false, true) == 14,
                    "self damage is independent of enemy hunter durability");
            }
        }
    }
    public static int Run()
    {
        try
        {
            _checks = 0; PackingAndMissing(); SerialRoundingAndZero(); BalancedVictimFacts(); InvalidArithmeticInputs();
            NativeArithmeticVectorsAcrossEveryBeam();
            Console.WriteLine($"PASS: {_checks} pure constructed timed victim-damage assertions; no native recording/admission proof");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
