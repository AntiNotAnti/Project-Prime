using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Movement;
using OpenTK.Mathematics;
using System.Reflection;

// Pure history/clock storage contracts. Facts are explicitly supplied engine
// fixtures; this suite does not establish placement/control or wire admission.
internal static class EngineSourceHistoryChecks
{
    private sealed class Fixture
    {
        internal readonly WeaponOwnerContext Owner = new(7,0x123456789abcdef0UL,3,5,9);
        internal readonly CustomSourceClock Clock;
        internal readonly CustomEngineSourceHistory History;
        internal Fixture(uint birth = 1000,uint world = 5000)
        {
            Clock = new(Owner,birth,world); History = new(Owner,Clock,birth,world);
        }
        internal CustomSourceInput Input(uint frame = 1000)
            => new(Owner,frame,4990,128,new(1,2,3),Vector3.UnitZ,0x10204080);
    }
    private static CustomPrecombatFacts Facts(bool canUse = true)
        => new(new(10,20,30),Vector3.UnitZ,canUse);
    internal static void Run(Action<bool,string> check)
    {
        ImmutableObservation(check); RawConflicts(check); DeniedObservation(check); RetentionAndMissing(check);
        InvalidInputs(check); ClockAndWrap(check); RetainedStance(check); Allocation(check);
    }
    private static void ImmutableObservation(Action<bool,string> check)
    {
        foreach (bool canUse in new[] { true,false })
        {
            var f = new Fixture(); var input = f.Input(); var facts = Facts(canUse);
            check(f.History.Observe(input,facts,5000) == CustomObservationResult.Captured,"first exact engine source observation captures");
            check(f.History.TryGet(f.Owner,1000,5000,out var first)
                && first == new CustomEngineObservation(input,5000,facts.Body,facts.Aim,canUse),
                "captured observation binds full raw input plus actual body/aim/permission and first world");
            facts = new(new(80,90,100),Vector3.UnitX,!canUse);
            check(f.History.Observe(input,facts,5001) == CustomObservationResult.Duplicate
                && f.History.TryGet(f.Owner,1000,5001,out var retained) && retained == first,
                "same raw input cannot resample later engine body, aim, eligibility or world timestamp");
            check(f.History.Observe(input,new(new(float.NaN,0,0),Vector3.Zero,!canUse),5002) == CustomObservationResult.Duplicate
                && f.History.TryGet(f.Owner,1000,5002,out retained) && retained == first,
                "duplicate facts cannot overwrite first proof even when later facts are unusable");
            check(f.History.TryGet(f.Owner,1000,5064,out retained) && retained == first,
                "first observation remains available through exact64 world ticks");
            check(f.History.Observe(input,facts,5065) == CustomObservationResult.Duplicate
                && !f.History.TryGet(f.Owner,1000,5065,out var expired) && expired == default,
                "duplicate cannot refresh expired observation age or supply replacement current pose");
        }
    }
    private static void RetainedStance(Action<bool,string> check)
    {
        var f = new Fixture(); var input = f.Input();
        var facts = Facts() with { Crouched = true };
        var result = f.History.Observe(input,facts,5000);
        bool available = f.History.TryGet(f.Owner,1000,5000,out var source);
        check(result == CustomObservationResult.Captured && available && source.Crouched
            && source.Input == input && source.MuzzleOrigin == facts.Body + Vector3.UnitY * TraversalStance.CustomMuzzleOffset(true)
            && source.TraceOrigin == facts.Body + Vector3.UnitY * TraversalStance.CustomTraceOffset(true),
            "exact accepted source stores crouch and derives muzzle/trace from its retained body and stance");
        check(f.History.Observe(input,Facts() with { Body = new(80,90,100), Crouched = false },5001) == CustomObservationResult.Duplicate
            && f.History.TryGet(f.Owner,1000,5001,out var retained) && retained == source
            && retained.MuzzleOrigin != facts.Body + Vector3.UnitY * TraversalStance.CustomMuzzleOffset(false),
            "later standing facts cannot revise an earlier source stance or move its emitted origin");
        var next = f.Input(1001);
        check(f.History.Observe(next,Facts(),5001) == CustomObservationResult.Captured
            && f.History.TryGet(f.Owner,1001,5001,out var standing) && !standing.Crouched
            && standing.MuzzleOrigin == standing.Body + Vector3.UnitY * .4f
            && standing.TraceOrigin == standing.Body + Vector3.UnitY * .2f,
            "neighbor standing source retains the historical custom muzzle and trace offsets");
    }
    private static void RawConflicts(Action<bool,string> check)
    {
        var variants = new Func<CustomSourceInput,CustomSourceInput>[]
        {
            i=>i with { AckFrame = i.AckFrame+1 }, i=>i with { AckSubFrame = (byte)(i.AckSubFrame+1) },
            i=>i with { ReportedBody = new(1,2,4) },i=>i with { ReportedAim = Vector3.UnitX },
            i=>i with { ReportedAim = Vector3.UnitZ*2 },i=>i with { InputBits = i.InputBits^1u }
        };
        foreach (var mutate in variants)
        {
            var f = new Fixture(); var input = f.Input();
            check(f.History.Observe(input,Facts(),5000) == CustomObservationResult.Captured,"conflict fixture has first exact observation");
            var changed = mutate(input);
            check(changed.IsValid && f.History.Observe(changed,Facts(false),5001) == CustomObservationResult.Conflict,
                "each changed valid raw input field tombstones exact retained source");
            check(!f.History.TryGet(f.Owner,input.Frame,5001,out var missing) && missing == default,
                "raw conflict makes earlier positive permission unavailable");
            check(f.History.Observe(input,Facts(),5002) == CustomObservationResult.Conflict
                && f.History.Observe(changed,Facts(),5003) == CustomObservationResult.Conflict
                && !f.History.TryGet(f.Owner,input.Frame,5003,out _),
                "original or changed raw retry cannot revive conflicted source cell");
            check(f.History.Observe(f.Input(1001),Facts(),5003) == CustomObservationResult.Captured
                && !f.History.TryGet(f.Owner,input.Frame,5003,out _),"neighbor capture does not erase conflict tombstone");
        }
    }
    private static void RetentionAndMissing(Action<bool,string> check)
    {
        var f = new Fixture(); var input = f.Input();
        check(!f.History.TryGet(f.Owner,1000,5000,out var absent) && absent == default,"unobserved birth frame never synthesizes pose");
        check(f.History.Observe(input,Facts(),5000) == CustomObservationResult.Captured,"retention fixture captures birth");
        check(!f.History.TryGet(f.Owner,1001,5000,out _) && !f.History.TryGet(f.Owner,999,5000,out _),
            "missing source frames have no current/nearest observation fallback");
        check(!f.History.TryGet(f.Owner,1000,4999,out var future) && future == default,"future world observation cannot be looked up prematurely");
        check(f.History.TryGet(f.Owner,1000,5064,out _)
            && !f.History.TryGet(f.Owner,1000,5065,out _),"observation age64 inclusive and65 expired");
        for (uint offset=1; offset<=CustomEngineSourceHistory.Capacity; offset++)
            check(f.History.Observe(f.Input(1000+offset),Facts(),5000+offset) == CustomObservationResult.Captured,
                "bounded history captures actual supplied source frames without filling omissions");
        check(!f.History.TryGet(f.Owner,1000,5128,out _) && f.History.TryGet(f.Owner,1128,5128,out var wrappedCell)
            && wrappedCell.Input.Frame == 1128,"128-cell ring replacement never aliases old source to newer observation");
        check(f.History.Observe(input,Facts(),5128) == CustomObservationResult.Unavailable,
            "exact128-source old observation cannot overwrite replacement ring cell");
        var sparse = new Fixture();
        check(sparse.History.Observe(sparse.Input(1128),Facts(),5128) == CustomObservationResult.Captured
            && !sparse.History.TryGet(sparse.Owner,1001,5128,out _),"source jump cannot synthesize missing intermediate input");
        check(sparse.History.Observe(sparse.Input(1001),Facts(false),5128) == CustomObservationResult.Captured
            && sparse.History.TryGet(sparse.Owner,1001,5128,out var late) && late.ObservedWorldFrame == 5128,
            "within127-cell window first supplied observation retains actual late observation time");
        sparse.History.Clear();
        check(!sparse.History.TryGet(sparse.Owner,1001,5128,out _) && !sparse.History.TryGet(sparse.Owner,1128,5128,out _),
            "explicit history clear removes retained observations");
        check(sparse.History.Observe(sparse.Input(1000),Facts(),5128) == CustomObservationResult.Unavailable
            && sparse.History.Observe(sparse.Input(1129),Facts(),5127) == CustomObservationResult.Invalid,
            "clear does not reset source/window or world high-water fences");
    }
    private static (CustomEngineObservation Observation,bool HasPose,bool Conflict) Cell(Fixture fixture,uint source)
    {
        // Read-only inspection distinguishes the deliberately inaccessible
        // negative tombstone from a valid accepted pose. Never in allocation loops.
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        const BindingFlags properties = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        var entries = (Array)typeof(CustomEngineSourceHistory).GetField("_entries",fields)!.GetValue(fixture.History)!;
        object entry = entries.GetValue(source%CustomEngineSourceHistory.Capacity)
            ?? throw new InvalidOperationException("Expected retained engine source cell.");
        var type = entry.GetType();
        return ((CustomEngineObservation)type.GetProperty("Observation",properties)!.GetValue(entry)!,
            (bool)type.GetProperty("HasPose",properties)!.GetValue(entry)!,
            (bool)type.GetProperty("Conflict",properties)!.GetValue(entry)!);
    }
    private static void DeniedObservation(Action<bool,string> check)
    {
        var f = new Fixture(); var input = f.Input();
        check(f.History.ObserveDenied(input,5000) == CustomObservationResult.Captured,
            "valid rejected engine input creates first negative permission cell");
        var first = Cell(f,1000);
        check(first.Observation.Input == input && first.Observation.ObservedWorldFrame == 5000 && !first.HasPose
            && !first.Conflict && !first.Observation.CanUseWeapon && first.Observation.Body == Vector3.Zero
            && first.Observation.Aim == Vector3.Zero,"negative input retains raw identity/time but never invents accepted body/aim pose");
        check(!f.History.TryGet(f.Owner,1000,5000,out var unavailable) && unavailable == default,
            "negative permission tombstone is never exposed as accepted source pose");
        check(f.History.ObserveDenied(input,5001) == CustomObservationResult.Duplicate
            && f.History.Observe(input,Facts(),5002) == CustomObservationResult.Duplicate
            && Cell(f,1000) == first && !f.History.TryGet(f.Owner,1000,5002,out _),
            "later rejected or accepted duplicate cannot promote denied first input or refresh its first world");
        check(f.History.Observe(input,Facts(),5064) == CustomObservationResult.Duplicate
            && f.History.ObserveDenied(input,5065) == CustomObservationResult.Duplicate
            && f.History.Observe(input,Facts(),5065) == CustomObservationResult.Duplicate
            && Cell(f,1000) == first && !f.History.TryGet(f.Owner,1000,5065,out _),
            "denied first input cannot gain a fresh pose at or after64-tick observation expiry");
        foreach (bool canUse in new[] { true,false })
        {
            var accepted = new Fixture();
            check(accepted.History.Observe(accepted.Input(),Facts(canUse),5000) == CustomObservationResult.Captured,
                "accepted-pose first observation fixture captures exact initial permission");
            var captured = Cell(accepted,1000);
            check(accepted.History.ObserveDenied(accepted.Input(),5001) == CustomObservationResult.Duplicate
                && Cell(accepted,1000) == captured && accepted.History.TryGet(accepted.Owner,1000,5001,out var pose)
                && pose.CanUseWeapon == canUse && pose == captured.Observation,
                "later denial cannot revise first accepted pose or its original positive/negative permission");
        }
        foreach (bool deniedFirst in new[] { true,false })
        {
            var conflict = new Fixture(); var raw = conflict.Input();
            check((deniedFirst ? conflict.History.ObserveDenied(raw,5000) : conflict.History.Observe(raw,Facts(),5000))
                == CustomObservationResult.Captured,"mixed denied/accepted conflict fixture captures first input");
            var captured = Cell(conflict,1000); var changed = raw with { InputBits = raw.InputBits^1u };
            check((deniedFirst ? conflict.History.Observe(changed,Facts(),5001) : conflict.History.ObserveDenied(changed,5001))
                == CustomObservationResult.Conflict,"changed raw input conflicts across denied/accepted observation forms");
            check(Cell(conflict,1000) == (captured.Observation,captured.HasPose,true)
                && !conflict.History.TryGet(conflict.Owner,1000,5001,out _),
                "cross-form raw conflict preserves first facts and permanently hides retained cell");
            check(conflict.History.ObserveDenied(raw,5002) == CustomObservationResult.Conflict
                && conflict.History.Observe(raw,Facts(),5003) == CustomObservationResult.Conflict,
                "neither original denied nor original accepted retry revives raw conflict");
        }
        var ring = new Fixture();
        for (uint offset=0; offset<=CustomEngineSourceHistory.Capacity; offset++)
            check(ring.History.ObserveDenied(ring.Input(1000+offset),5000+offset) == CustomObservationResult.Captured,
                "negative first observations consume bounded source cells without accepted poses");
        var replacement = Cell(ring,1128);
        check(replacement.Observation.Input.Frame == 1128 && replacement.Observation.ObservedWorldFrame == 5128
            && !replacement.HasPose && !ring.History.TryGet(ring.Owner,1128,5128,out _),
            "negative ring replacement preserves exact newer raw identity and cannot masquerade as pose");
        check(ring.History.ObserveDenied(ring.Input(),5128) == CustomObservationResult.Unavailable
            && ring.History.Observe(ring.Input(),Facts(),5128) == CustomObservationResult.Unavailable
            && ring.History.Observe(ring.Input(1128),Facts(),5128) == CustomObservationResult.Duplicate
            && Cell(ring,1128) == replacement,"expired/evicted negative source cannot overwrite or promote replacement cell");
        var invalid = new Fixture();
        check(invalid.History.ObserveDenied(invalid.Input() with { ReportedAim = Vector3.Zero },5000) == CustomObservationResult.Invalid
            && invalid.History.ObserveDenied(invalid.Input(999),5000) == CustomObservationResult.Invalid
            && invalid.History.ObserveDenied(invalid.Input(1033),5000) == CustomObservationResult.Invalid,
            "negative path still requires valid raw aim, bound birth and fixed source clock");
        var foreignOwner = new WeaponOwnerContext(invalid.Owner.MatchId,invalid.Owner.AuthorityEpoch,invalid.Owner.Slot,invalid.Owner.Generation,10);
        check(invalid.History.ObserveDenied(invalid.Input() with { Owner = foreignOwner },5000) == CustomObservationResult.Invalid
            && invalid.History.ObserveDenied(invalid.Input(),5000) == CustomObservationResult.Captured
            && invalid.History.ObserveDenied(invalid.Input(),4999) == CustomObservationResult.Invalid,
            "negative path keeps exact owner/life and monotonic world guards");
        var hot = new Fixture(); var hotInput = hot.Input();
        check(hot.History.ObserveDenied(hotInput,5000) == CustomObservationResult.Captured,"duplicate-denial allocation fixture captures first tombstone");
        bool duplicate = true;
        for (int i=0; i<10000; i++) duplicate &= hot.History.ObserveDenied(hotInput,5001) == CustomObservationResult.Duplicate;
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i=0; i<100000; i++) duplicate &= hot.History.ObserveDenied(hotInput,5001) == CustomObservationResult.Duplicate;
        bytes = GC.GetAllocatedBytesForCurrentThread()-bytes;
        check(duplicate && bytes == 0 && !hot.History.TryGet(hot.Owner,1000,5001,out _)
            && Cell(hot,1000).Observation.ObservedWorldFrame == 5000,
            "100000 warmed duplicate denied observations allocate zero bytes without minting pose or refreshing time");
    }
    private static void InvalidInputs(Action<bool,string> check)
    {
        var f = new Fixture(); var input = f.Input();
        var owners = new[] { new WeaponOwnerContext(8,f.Owner.AuthorityEpoch,3,5,9),new(7,f.Owner.AuthorityEpoch+1,3,5,9),
            new(7,f.Owner.AuthorityEpoch,2,5,9),new(7,f.Owner.AuthorityEpoch,3,6,9),new(7,f.Owner.AuthorityEpoch,3,5,10),default };
        foreach (var owner in owners)
            check(f.History.Observe(input with { Owner = owner },Facts(),5000) == CustomObservationResult.Invalid
                && !f.History.TryGet(owner,1000,5000,out _),"history rejects every foreign owner/life join dimension");
        foreach (var bad in new[] { input with { ReportedBody = new(float.NaN,0,0) },input with { ReportedBody = new(1000001,0,0) },
            input with { ReportedAim = Vector3.Zero },input with { ReportedAim = new(float.PositiveInfinity,0,0) },
            input with { ReportedAim = new(0,float.NaN,0) },input with { ReportedAim = new(1000001,0,0) },
            input with { ReportedAim = new(.00001f,0,0) } })
            check(!bad.IsValid && f.History.Observe(bad,Facts(),5000) == CustomObservationResult.Invalid,
                "invalid raw geometry/aim cannot create engine source observation");
        foreach (var facts in new[] { new CustomPrecombatFacts(new(float.NaN,0,0),Vector3.UnitZ,true),
            new(new(1000001,0,0),Vector3.UnitZ,true),new(Vector3.Zero,Vector3.Zero,true),
            new(Vector3.Zero,Vector3.UnitZ*2,true),new(Vector3.Zero,new(float.PositiveInfinity,0,0),true) })
            check(f.History.Observe(input,facts,5000) == CustomObservationResult.Invalid,
                "invalid engine facts cannot capture a new raw input");
        check(f.History.Observe(input,Facts(),5000) == CustomObservationResult.Captured
            && f.History.TryGet(f.Owner,1000,5000,out _),"invalid attempts never reserve or poison missing valid source cell");
        foreach (var owner in owners)
            check(!f.History.TryGet(owner,1000,5000,out _),"populated history never crosses complete owner/life boundary");
        check(f.History.Observe(f.Input(999),Facts(),5001) == CustomObservationResult.Invalid,"source cannot predate bound life birth");
        check(f.History.Observe(input,Facts(),4999) == CustomObservationResult.Invalid
            && f.History.TryGet(f.Owner,1000,5000,out _),"backdated duplicate world neither replaces nor conflicts first proof");
        var wrongClock = new CustomSourceClock(owners[0],1000,5000);
        var foreign = new CustomEngineSourceHistory(f.Owner,wrongClock,1000,5000);
        check(foreign.Observe(input,Facts(),5000) == CustomObservationResult.Invalid,
            "clock from a different occupant cannot authorize history capture");
        bool invalidOwner = false, nullClock = false;
        try { _ = new CustomEngineSourceHistory(default,f.Clock,1000,5000); } catch (ArgumentException) { invalidOwner = true; }
        try { _ = new CustomEngineSourceHistory(f.Owner,null!,1000,5000); } catch (ArgumentNullException) { nullClock = true; }
        check(invalidOwner && nullClock,"history constructor rejects absent owner and clock");
    }
    private static void ClockAndWrap(Action<bool,string> check)
    {
        var f = new Fixture();
        check(f.History.Observe(f.Input(1033),Facts(),5000) == CustomObservationResult.Invalid,
            "untrusted source acceleration beyond fixed anchor plus32 is rejected");
        check(f.History.Observe(f.Input(1032),Facts(),5000) == CustomObservationResult.Captured,
            "source acceleration exact32 allowance is inclusive");
        check(f.History.Observe(f.Input(1033),Facts(),5001) == CustomObservationResult.Captured,
            "source clock allowance advances only with actual world clock");
        var nextOwner = new WeaponOwnerContext(f.Owner.MatchId,f.Owner.AuthorityEpoch,f.Owner.Slot,f.Owner.Generation,10);
        var nextLife = new CustomEngineSourceHistory(nextOwner,f.Clock,1034,5001);
        check(nextLife.Observe(f.Input(1034) with { Owner = nextOwner },Facts(),5001) == CustomObservationResult.Invalid,
            "new-life history sharing occupant clock cannot reset source acceleration anchor");
        check(nextLife.Observe(f.Input(1034) with { Owner = nextOwner },Facts(),5002) == CustomObservationResult.Captured,
            "life-specific history may use shared occupant clock after actual time advances");
        var wrap = new Fixture(uint.MaxValue-2,uint.MaxValue-2);
        check(wrap.History.Observe(wrap.Input(uint.MaxValue-2),Facts(),uint.MaxValue-2) == CustomObservationResult.Captured,
            "source/world anchors can begin near uint wrap");
        check(wrap.History.Observe(wrap.Input(0),Facts(false),0) == CustomObservationResult.Captured
            && wrap.History.TryGet(wrap.Owner,0,0,out var zero) && !zero.CanUseWeapon,
            "source and world zero are valid elapsed-clock values across uint wrap");
        check(wrap.History.TryGet(wrap.Owner,uint.MaxValue-2,61,out _)
            && !wrap.History.TryGet(wrap.Owner,uint.MaxValue-2,62,out _),"64/65 world-age boundary survives uint wrap");
        check(wrap.History.Observe(wrap.Input(uint.MaxValue),Facts(),uint.MaxValue) == CustomObservationResult.Invalid,
            "pre-wrap world cannot roll observed frontier backward after zero");
        check(wrap.History.Observe(wrap.Input(uint.MaxValue-3),Facts(),1) == CustomObservationResult.Invalid,
            "pre-birth source remains invalid around uint wrap");
    }
    private static void Allocation(Action<bool,string> check)
    {
        var f = new Fixture(); var input = f.Input(); var facts = Facts();
        check(f.History.Observe(input,facts,5000) == CustomObservationResult.Captured,"allocation fixture captures exact source");
        check(f.History.TryGet(f.Owner,1000,5000,out var expected),"allocation fixture reads exact immutable source");
        var laterFacts = new CustomPrecombatFacts(new(40,50,60),Vector3.UnitX,false);
        bool valid = true;
        for (int i=0; i<10000; i++)
        {
            valid &= f.History.Observe(input,laterFacts,5001) == CustomObservationResult.Duplicate;
            valid &= f.History.TryGet(f.Owner,1000,5001,out var observation) && observation == expected;
        }
        long lookupBytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i=0; i<100000; i++)
        {
            valid &= f.History.TryGet(f.Owner,1000,5001,out var observation) && observation == expected;
            valid &= !f.History.TryGet(f.Owner,1001,5001,out var missing) && missing == default;
        }
        lookupBytes = GC.GetAllocatedBytesForCurrentThread()-lookupBytes;
        check(valid && lookupBytes == 0,"100000 warmed exact/missing source lookup pairs allocate zero bytes");
        long duplicateBytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i=0; i<100000; i++) valid &= f.History.Observe(input,laterFacts,5001) == CustomObservationResult.Duplicate;
        duplicateBytes = GC.GetAllocatedBytesForCurrentThread()-duplicateBytes;
        check(valid && duplicateBytes == 0 && f.History.TryGet(f.Owner,1000,5001,out var retained) && retained == expected,
            "100000 warmed duplicate engine observations allocate zero bytes and never resample facts");
    }
}
