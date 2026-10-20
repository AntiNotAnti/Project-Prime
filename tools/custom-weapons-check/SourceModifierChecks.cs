using System.Reflection;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

// Pure retained-ownership fixtures. Grants use the real ledger state machine;
// these checks do not establish actual pickup contact or authenticated admission.
internal static class SourceModifierChecks
{
    private sealed class Fixture
    {
        internal readonly WeaponOwnerContext Owner = new(7,0x123456789abcdef0UL,3,5,9);
        internal ShotKey NativeLife => new(Owner.AuthorityEpoch,Owner.MatchId,Owner.Slot,Owner.Generation,Owner.Life,0);
        internal readonly NetTimedGrants Ledger;
        internal Fixture(uint room = 17)
        {
            Ledger = new(Owner.AuthorityEpoch,Owner.MatchId,room);
            Ledger.BeginLife(NativeLife,100);
        }
        internal TimedGrantRecord Grant(uint world = 100,uint? source = 5000)
        {
            if (!Ledger.RegisterSpawn(42,TimedPowerUp.DoubleDamage,out var spawn)
                || Ledger.TryGrant(spawn,NativeLife,world,source,out var grant) != TimedGrantResult.Granted)
                throw new InvalidOperationException("Ledger grant fixture refused.");
            return grant!;
        }
        internal void Observe(uint source,uint world) => Ledger.ObserveInput(NativeLife,source,world);
        internal CustomSourceModifierEvidence Capture(uint source = 5001,uint world = 101,bool primeAbsent = true)
            => CustomSourceModifierEvidence.Capture(Owner,source,world,primeAbsent,Ledger);
    }
    internal static void Run(Action<bool,string> check)
    {
        TypedReadParity(check); ContactAndFrozenFunding(check); UnknownOwnership(check);
        FundingIdentity(check); ObservationImmutability(check); WrapAndReadPurity(check); Allocation(check);
    }
    private static void TypedReadParity(Action<bool,string> check)
    {
        var f = new Fixture(); f.Observe(5000,100); var grant = f.Grant(); f.Observe(5001,101);
        foreach (var type in Enum.GetValues<TimedPowerUp>())
        foreach (uint source in new uint[] { 5000,5001,5002 })
        {
            bool native = f.Ledger.TryReadObservation(f.NativeLife with { ShotId = uint.MaxValue },source,type,out var a,out uint aw);
            bool typed = f.Ledger.TryReadObservation(f.Owner,source,type,out var b,out uint bw);
            check(native == typed && a == b && aw == bw,"typed lifecycle read matches native normalized-life read for each timed effect and missing input");
        }
        check(f.Ledger.TryReadObservation(f.Owner,5000,TimedPowerUp.DoubleDamage,out var atContact,out uint first)
            && atContact == new TimedGrantAnswer(TimedGrantKnowledge.NotGranted) && first == 100,
            "typed contact input is retained known absence rather than missing or granted");
        check(f.Ledger.TryReadObservation(f.Owner,5001,TimedPowerUp.DoubleDamage,out var next,out uint second)
            && next == new TimedGrantAnswer(TimedGrantKnowledge.Granted,grant.Sequence) && second == 101,
            "typed next-input lookup binds exact real ledger grant sequence and first world");
        foreach (var bad in new[] { (TimedPowerUp)3,(TimedPowerUp)255 })
            check(!f.Ledger.TryReadObservation(f.Owner,5001,bad,out var unavailable,out uint world)
                && unavailable == new TimedGrantAnswer(TimedGrantKnowledge.Unavailable) && world == 0,
                "typed invalid effect lookup returns unavailable and default world");
        f.Ledger.RetireSlot(f.Owner.Slot,102,TimedGrantEnd.Death);
        check(f.Ledger.TryReadObservation(f.Owner,5001,TimedPowerUp.DoubleDamage,out var retained,out uint retainedWorld)
            && retained == next && retainedWorld == second,"typed old-life observation remains readable after native retirement");
    }
    private static void ContactAndFrozenFunding(Action<bool,string> check)
    {
        var f = new Fixture(); f.Observe(5000,100); var first = f.Grant();
        var contact = f.Capture(5000,100);
        check(contact.DoubleKnowledge == TimedGrantKnowledge.NotGranted && contact.Funding == null
            && contact.Matches(f.Owner,5000,100),"contact cannot fund its own immutable source input");
        f.Observe(5001,101); var paid = f.Capture();
        var expected = new CustomGrantFunding(first.Sequence,first.Spawn,100,5000,1900);
        check(paid.DoubleKnowledge == TimedGrantKnowledge.Granted && paid.Funding == expected
            && paid.Matches(f.Owner,5001,101),"C+1 capture copies complete exact funding incarnation and chronology");
        var renewal = f.Grant(102,5002); f.Observe(5003,103); var renewed = f.Capture(5003,103);
        check(first.EndReason == TimedGrantEnd.Renewed && first.EndFrame == 103
            && renewed.Funding == new CustomGrantFunding(renewal.Sequence,renewal.Spawn,102,5002,1902),
            "renewal uses a new exact funding incarnation without rewriting earlier source evidence");
        check(f.Capture() == paid && paid.Funding == expected,"later renewal cannot replace first retained source funding");
        f.Observe(6799,1899); var late = f.Capture(6799,1899);
        check(late.DoubleKnowledge == TimedGrantKnowledge.Granted && late.Funding?.Sequence == renewal.Sequence,
            "actual pre-expiry input captures renewed funding");
        f.Ledger.Advance(1902); f.Observe(6802,1902);
        check(renewal.EndReason == TimedGrantEnd.Expired && f.Capture(6802,1902).DoubleKnowledge == TimedGrantKnowledge.NotGranted,
            "expiry produces known absence only for newly observed source inputs");
        check(late.Funding?.ExpiresWorld == 1902 && late.DoubleKnowledge == TimedGrantKnowledge.Granted
            && paid.Funding == expected,"value funding copies remain unchanged after mutable grant renewal and expiry");
        check(f.Capture().DoubleKnowledge == TimedGrantKnowledge.Unavailable,
            "expired observation cannot be reconstructed from current ownership while previously copied evidence remains immutable");
    }
    private static void UnknownOwnership(Action<bool,string> check)
    {
        var f = new Fixture(); f.Grant(); f.Observe(5001,101);
        foreach (var owner in new[] { default(WeaponOwnerContext),new(8,f.Owner.AuthorityEpoch,3,5,9),
            new(7,f.Owner.AuthorityEpoch+1,3,5,9),new(7,f.Owner.AuthorityEpoch,2,5,9),
            new(7,f.Owner.AuthorityEpoch,3,6,9),new(7,f.Owner.AuthorityEpoch,3,5,10) })
        {
            check(!f.Ledger.TryReadObservation(owner,5001,TimedPowerUp.DoubleDamage,out var answer,out uint world)
                && answer.Knowledge == TimedGrantKnowledge.Unavailable && world == 0,
                "typed observation join requires every full-width owner/life dimension");
            var captured = CustomSourceModifierEvidence.Capture(owner,5001,101,true,f.Ledger);
            check(captured.DoubleKnowledge == TimedGrantKnowledge.Unavailable && captured.Funding == null
                && !captured.Matches(owner,5001,101),"invalid or foreign lifecycle never manufactures known-false modifier evidence");
        }
        var emptyRoom = new NetTimedGrants(f.Owner.AuthorityEpoch,f.Owner.MatchId,18);
        foreach (var ledger in new NetTimedGrants?[] { null,emptyRoom,new Fixture(0).Ledger })
        {
            var evidence = CustomSourceModifierEvidence.Capture(f.Owner,5001,101,true,ledger);
            check(evidence.DoubleKnowledge == TimedGrantKnowledge.Unavailable && evidence.Funding == null
                && !evidence.Matches(f.Owner,5001,101),"absent ledger, empty different room or room zero is unknown");
        }
        foreach (var pair in new (uint Source,uint World)[] { (5002,101),(5001,100),(5001,102) })
            check(f.Capture(pair.Source,pair.World).DoubleKnowledge == TimedGrantKnowledge.Unavailable,
                "missing exact source or different first-observed world cannot be funded by a retained adjacent observation");
        var primeUnknown = f.Capture(primeAbsent:false);
        check(primeUnknown.DoubleKnowledge == TimedGrantKnowledge.Granted && !primeUnknown.PrimeAbsent
            && !primeUnknown.Matches(f.Owner,5001,101),"valid Double ownership cannot bypass missing canonical Prime absence");
        var missingContact = new Fixture(); missingContact.Grant(source:null); missingContact.Observe(5001,101);
        check(missingContact.Capture().DoubleKnowledge == TimedGrantKnowledge.Unavailable,
            "grant without actual source contact boundary remains unknown rather than known absent");
        f.Observe(9999,102);
        check(f.Capture(9999,102).DoubleKnowledge == TimedGrantKnowledge.Unavailable,
            "source clock acceleration cannot turn actual current grant into trusted source-time ownership");
        var paid = f.Capture();
        check(!paid.Matches(f.Owner,5002,101) && !paid.Matches(f.Owner,5001,102)
            && !default(CustomSourceModifierEvidence).Matches(f.Owner,5001,101),
            "modifier evidence matches exact source/world and rejects absent defaults");
    }
    private static void FundingIdentity(Action<bool,string> check)
    {
        var variants = new Func<TimedGrantRecord,TimedGrantRecord>[]
        {
            g=>g with { Sequence = g.Sequence+1 },g=>g with { Type = TimedPowerUp.Cloak },
            g=>g with { Life = g.Life with { AuthorityEpoch = g.Life.AuthorityEpoch+1 } },
            g=>g with { Life = g.Life with { MatchId = 8 } },g=>g with { Life = g.Life with { ShooterSlot = 2 } },
            g=>g with { Life = g.Life with { Generation = 6 } },g=>g with { Life = g.Life with { LifeId = 10 } },
            g=>g with { Life = g.Life with { ShotId = 1 } },
            g=>g with { Spawn = g.Spawn with { Epoch = g.Spawn.Epoch+1 } },
            g=>g with { Spawn = g.Spawn with { Match = 8 } },g=>g with { Spawn = g.Spawn with { Room = 18 } },
            g=>g with { Spawn = g.Spawn with { Generation = 0 } },
            g=>g with { SourceContactFrame = null },g=>g with { ExpiresAtFrame = g.ExpiresAtFrame+1 },
            g=>g with { ContactFrame = 101,ExpiresAtFrame = 1901 },
            g=>g with { ContactFrame = unchecked(101u-NetTimedGrants.Duration),ExpiresAtFrame = 101 },
            g=>g with { SourceContactFrame = 5001 }
        };
        foreach (var corrupt in variants)
        {
            var f = new Fixture(); var grant = f.Grant(); f.Observe(5001,101);
            // Declared corruption seam: trusted storage is deliberately made
            // inconsistent, without publishing a false custom admission proof.
            var history = (Dictionary<ulong,TimedGrantRecord>)typeof(NetTimedGrants)
                .GetField("_history",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(f.Ledger)!;
            history[grant.Sequence] = corrupt(grant);
            var evidence = f.Capture();
            check(evidence.DoubleKnowledge == TimedGrantKnowledge.Unavailable && evidence.Funding == null,
                "inconsistent funding identity, incarnation or temporal boundary fails unknown");
        }
        {
            var f = new Fixture(); var grant = f.Grant(); f.Observe(5001,101);
            var history = (Dictionary<ulong,TimedGrantRecord>)typeof(NetTimedGrants)
                .GetField("_history",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(f.Ledger)!;
            history.Remove(grant.Sequence);
            check(f.Capture().DoubleKnowledge == TimedGrantKnowledge.Unavailable,"missing funding record never authorizes retained granted answer");
        }
    }
    private static void ObservationImmutability(Action<bool,string> check)
    {
        var f = new Fixture(); f.Observe(5000,100); var absent = f.Capture(5000,100); f.Grant();
        var clock = new CustomSourceClock(f.Owner,5000,100);
        var history = new CustomEngineSourceHistory(f.Owner,clock,5000,100);
        var input = new CustomSourceInput(f.Owner,5000,90,128,Vector3.Zero,Vector3.UnitZ,uint.MaxValue);
        var facts = new CustomPrecombatFacts(Vector3.Zero,Vector3.UnitZ,true,absent);
        check(history.Observe(input,facts,100) == CustomObservationResult.Captured,"source fixture captures known absence alongside arbitrary raw owner control bits");
        f.Observe(5001,101); var granted = f.Capture();
        check(history.Observe(input,facts with { Modifiers = granted },101) == CustomObservationResult.Duplicate
            && history.TryGet(f.Owner,5000,101,out var retained) && retained.Modifiers == absent
            && retained.Modifiers.DoubleKnowledge == TimedGrantKnowledge.NotGranted,
            "raw controls and later granted state cannot revise first source modifier evidence");
        var next = input with { Frame = 5001,InputBits = 0 };
        check(history.Observe(next,facts with { Modifiers = granted },101) == CustomObservationResult.Captured
            && history.TryGet(f.Owner,5001,101,out retained) && retained.Modifiers == granted,
            "source with no control bits still retains genuinely funded next-input evidence");
    }
    private static void WrapAndReadPurity(Action<bool,string> check)
    {
        var f = new Fixture(); f.Grant(); f.Observe(5001,101); var captured = f.Capture();
        f.Ledger.Advance(1125);
        check(f.Capture() == captured,"pure capture reads retained observation at inclusive1024 ledger retention");
        check(f.Capture(5001,5000).DoubleKnowledge == TimedGrantKnowledge.Unavailable
            && f.Capture() == captured && f.Ledger.ObservationCount == 1,
            "read with mismatched later world neither advances eviction nor changes grant clock");
        f.Ledger.Advance(1126);
        check(f.Capture().DoubleKnowledge == TimedGrantKnowledge.Unavailable && f.Ledger.ObservationCount == 0
            && captured.Funding.HasValue,"explicit authority Advance expires retained source but cannot mutate copied evidence");
        var wrap = new Fixture(); wrap.Observe(uint.MaxValue,uint.MaxValue-1);
        var grant = wrap.Grant(uint.MaxValue-1,uint.MaxValue); wrap.Observe(0,uint.MaxValue);
        var evidence = wrap.Capture(0,uint.MaxValue);
        check(evidence.DoubleKnowledge == TimedGrantKnowledge.Granted
            && evidence.Funding == new CustomGrantFunding(grant.Sequence,grant.Spawn,uint.MaxValue-1,uint.MaxValue,1798),
            "source activation and world expiry preserve uint wrap without zero-source sentinel");
    }
    private static bool Probe(Fixture f,in CustomSourceModifierEvidence expected)
        => f.Ledger.TryReadObservation(f.Owner,5001,TimedPowerUp.DoubleDamage,out var answer,out uint world)
            && answer.Knowledge == TimedGrantKnowledge.Granted && world == 101 && f.Capture() == expected
            && !f.Ledger.TryReadObservation(f.Owner,5002,TimedPowerUp.DoubleDamage,out _,out _)
            && f.Capture(5002,101).DoubleKnowledge == TimedGrantKnowledge.Unavailable;
    private static void Allocation(Action<bool,string> check)
    {
        var f = new Fixture(); f.Grant(); f.Observe(5001,101); var expected = f.Capture(); bool valid = true;
        for (int i=0;i<10000;i++) valid &= Probe(f,expected);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i=0;i<100000;i++) valid &= Probe(f,expected);
        long bytes = GC.GetAllocatedBytesForCurrentThread()-before;
        check(valid && bytes == 0,"100000 warmed typed ownership reads and funded/missing captures allocate zero bytes");
        Console.WriteLine($"Custom modifier typed read/capture allocation: {bytes} bytes / 100000 warmed probes.");
    }
}
