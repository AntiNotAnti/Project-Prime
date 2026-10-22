using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using System.Reflection;

// Pure readiness-to-input frontier contracts. NoteAcceptedInput fixtures stand
// for a trusted NewCurrentCarrier hook, and TryOpen fixtures stand for actual
// engine consumption. No socket, placement, clock or resource birth is proven.
internal static class SourceBoundaryChecks
{
    private sealed class Fixture
    {
        internal readonly WorldBootstrapIdentity Bootstrap = new(new(7,0x123456789abcdef0UL,3),4,5,3900,6,7);
        internal readonly WeaponOwnerContext Owner = new(7,0x123456789abcdef0UL,3,5,9);
        internal const ulong Connection = 0xfedcba9876543210UL;
        internal readonly CustomReadinessPeer Peer;
        internal readonly CustomPeerSourceBoundary Boundary;
        internal Fixture()
        { Peer = new(Bootstrap,3,Connection,0); Boundary = new(Peer); }
        internal CustomSourceInput Input(uint frame = 1000,WeaponOwnerContext? owner = null)
            => new(owner ?? Owner,frame,3890,128,new(1,2,3),Vector3.UnitZ,(uint)IntentButtons.InPlayState);
        internal void Ready(uint world = 4000)
        {
            if (Peer.Receive(Peer.Wire.ReadyBytes,Connection,world,0) != CustomReadinessResult.Accepted)
                throw new InvalidOperationException("Readiness fixture failed to publish genuine peer capability state.");
        }
        internal CustomSourceReadyContext Open(uint frame = 1001)
        {
            if (!Boundary.NoteAcceptedInput(Input()) ) throw new InvalidOperationException("Pre-open fixture input refused.");
            Ready();
            if (!Boundary.ConfirmReadiness() || !Boundary.NoteAcceptedInput(Input(frame))
                || !Boundary.TryOpen(Input(frame),out var context)) throw new InvalidOperationException("Boundary fixture failed to open.");
            return context;
        }
    }
    internal static void Run(Action<bool,string> check)
    {
        FirstBoundary(check); ExactRawInput(check); IdentityAndOrdering(check); LifeAndFrameWrap(check);
        ReadinessRetirement(check); SerialExhaustion(check); MetadataWorlds(check); ClockPreflight(check); Allocation(check);
    }
    private static void FirstBoundary(Action<bool,string> check)
    {
        var f = new Fixture(); var pre = f.Input();
        check(f.Boundary.NoteAcceptedInput(pre),"trusted exact current carrier may be tracked before capability readiness");
        check(!f.Boundary.ConfirmReadiness() && !f.Boundary.TryOpen(pre,out var unavailable) && unavailable == default
            && !f.Boundary.TryGet(f.Owner,out unavailable) && unavailable == default,
            "input alone cannot manufacture peer readiness or a source-open context");
        f.Ready();
        check(f.Boundary.ConfirmReadiness(),"first real peer readiness pins the current ingress frontier");
        check(!f.Boundary.TryOpen(pre,out unavailable) && unavailable == default,
            "pre-ready accepted input cannot become fresh by later engine reconsumption");
        var post = f.Input(1001);
        check(f.Boundary.NoteAcceptedInput(post) && !f.Boundary.TryGet(f.Owner,out unavailable) && unavailable == default,
            "post-open network acceptance alone never chooses resource birth or a source boundary");
        f.Boundary.ConfirmReadiness();
        check(f.Boundary.TryOpen(post,out var opened),"duplicate readiness confirmation does not move the pinned ingress fence");
        check(CustomWeaponReady.TryRead(f.Peer.Wire.ReadyBytes,out var ready) && opened.Ready == ready
            && opened.PeerIncarnation == Fixture.Connection && opened.OpenedWorld == 4000 && opened.FirstSourceFrame == 1001,
            "first consumed fresh input pins exact bootstrap/manifest/recipient/connection/open-world and source-frame context");
        check(f.Boundary.TryGet(f.Owner,out var retained) && retained == opened
            && f.Boundary.TryOpen(post,out retained) && retained == opened,"read-only lookup and exact consumed-input retry retain immutable context");
        check(f.Peer.Receive(f.Peer.Wire.ReadyBytes,Fixture.Connection,9999,1) == CustomReadinessResult.Duplicate,
            "real peer duplicate preserves its original ready publication");
        var next = f.Input(1002); check(f.Boundary.NoteAcceptedInput(next),"later exact current input advances only the accepted-input frontier");
        f.Boundary.ConfirmReadiness();
        check(f.Boundary.TryOpen(next,out retained) && retained == opened && f.Boundary.TryGet(f.Owner,out retained) && retained == opened,
            "later input and duplicate ready cannot replace first consumed source or first readiness world");
        check(!f.Boundary.TryOpen(post,out unavailable) && unavailable == default,"formerly valid input cannot be reselected after a newer carrier replaces it");
        var delayedConfirm = new Fixture(); delayedConfirm.Ready();
        check(delayedConfirm.Boundary.NoteAcceptedInput(delayedConfirm.Input()),"trusted input can arrive between capability publication and explicit fence pin");
        delayedConfirm.Boundary.ConfirmReadiness();
        check(!delayedConfirm.Boundary.TryOpen(delayedConfirm.Input(),out unavailable) && unavailable == default,
            "first ConfirmReadiness fences every already accepted input even when peer evidence was published earlier");
    }
    private static IEnumerable<CustomSourceInput> ChangedRaw(CustomSourceInput input)
    {
        yield return input with { Frame = input.Frame+1 };
        yield return input with { AckFrame = input.AckFrame+1 };
        yield return input with { AckSubFrame = (byte)(input.AckSubFrame+1) };
        yield return input with { ReportedBody = new(1,2,4) };
        yield return input with { ReportedAim = Vector3.UnitX };
        yield return input with { ReportedAim = Vector3.UnitZ*2 };
        yield return input with { InputBits = input.InputBits^(uint)IntentButtons.Shoot };
    }
    private static void ExactRawInput(Action<bool,string> check)
    {
        var f = new Fixture(); f.Boundary.NoteAcceptedInput(f.Input()); f.Ready(); f.Boundary.ConfirmReadiness();
        var post = f.Input(1001); f.Boundary.NoteAcceptedInput(post);
        foreach (var forged in ChangedRaw(post))
            check(!f.Boundary.TryOpen(forged,out var context) && context == default,
                "engine consumption must match every exact latest accepted raw input field");
        check(f.Boundary.TryOpen(post,out var opened),"failed forged consumption does not discard the genuine latest carrier");
        foreach (var changed in ChangedRaw(post).Where(input=>input.Frame == post.Frame))
        {
            check(!f.Boundary.NoteAcceptedInput(changed),"altered duplicate source frame cannot replace accepted raw input or increment freshness serial");
            check(f.Boundary.TryOpen(post,out var retained) && retained == opened,
                "rejected duplicate does not promote its changed body/aim/ACK or disturb the first context");
        }
        var latest = f.Input(1002); f.Boundary.NoteAcceptedInput(latest);
        check(!f.Boundary.NoteAcceptedInput(post) && !f.Boundary.TryOpen(post,out _)
            && f.Boundary.TryOpen(latest,out var same) && same == opened,"reordered source cannot replace newest carrier after context opens");
    }
    private static IEnumerable<WeaponOwnerContext> ForeignOwners(WeaponOwnerContext owner)
    {
        yield return default;
        yield return new((ushort)(owner.MatchId+1),owner.AuthorityEpoch,owner.Slot,owner.Generation,owner.Life);
        yield return new(owner.MatchId,owner.AuthorityEpoch^0x8000000000000000UL,owner.Slot,owner.Generation,owner.Life);
        yield return new(owner.MatchId,owner.AuthorityEpoch,(byte)(owner.Slot+1),owner.Generation,owner.Life);
        yield return new(owner.MatchId,owner.AuthorityEpoch,owner.Slot,(ushort)(owner.Generation+1),owner.Life);
    }
    private static void IdentityAndOrdering(Action<bool,string> check)
    {
        var f = new Fixture(); var opened = f.Open(); var latest = f.Input(1001);
        foreach (var owner in ForeignOwners(f.Owner))
        {
            var foreign = latest with { Owner = owner,Frame = 1002 };
            check(!f.Boundary.NoteAcceptedInput(foreign) && !f.Boundary.TryOpen(foreign,out var missing) && missing == default
                && !f.Boundary.TryGet(owner,out missing) && missing == default,
                "accepted boundary joins full valid epoch/match/slot/generation rather than a slot hint");
        }
        foreach (var bad in new[] { latest with { ReportedBody = new(float.NaN,0,0),Frame = 1002 },
            latest with { ReportedBody = new(1000001,0,0),Frame = 1002 },latest with { ReportedAim = Vector3.Zero,Frame = 1002 },
            latest with { ReportedAim = new(float.PositiveInfinity,0,0),Frame = 1002 } })
            check(!f.Boundary.NoteAcceptedInput(bad) && !f.Boundary.TryOpen(bad,out _),"invalid raw geometry or absent aim cannot advance the input frontier");
        check(f.Boundary.TryOpen(latest,out var retained) && retained == opened,
            "foreign and invalid offered inputs cannot corrupt the current latest accepted input");
        var nextOwner = new WeaponOwnerContext(f.Owner.MatchId,f.Owner.AuthorityEpoch,f.Owner.Slot,f.Owner.Generation,10);
        var next = f.Input(1002,nextOwner);
        check(f.Boundary.NoteAcceptedInput(next) && f.Boundary.TryOpen(next,out retained) && retained == opened
            && f.Boundary.TryGet(nextOwner,out retained) && retained == opened,
            "newer native life shares occupant capability without changing initial readiness or source boundary");
        check(!f.Boundary.NoteAcceptedInput(latest with { Frame = 2000 }) && !f.Boundary.TryOpen(latest,out _),
            "older-life input cannot replace a newer-life frontier even with a larger frame number");
        var reset = new Fixture(); var original = reset.Open();
        var resetLife = new WeaponOwnerContext(reset.Owner.MatchId,reset.Owner.AuthorityEpoch,reset.Owner.Slot,reset.Owner.Generation,10);
        var resetInput = reset.Input(1,resetLife);
        check(reset.Boundary.NoteAcceptedInput(resetInput) && !reset.Boundary.TryOpen(resetInput,out var absent) && absent == default,
            "newer-life carrier cannot reset source time before the pinned first source boundary");
        var resumed = reset.Input(1002,resetLife);
        check(reset.Boundary.NoteAcceptedInput(resumed) && reset.Boundary.TryOpen(resumed,out var sameContext) && sameContext == original,
            "later verified source in the new life can select exact input without replacing the original context");
    }
    private static void LifeAndFrameWrap(Action<bool,string> check)
    {
        var f = new Fixture(); f.Boundary.NoteAcceptedInput(f.Input(uint.MaxValue-1)); f.Ready(); f.Boundary.ConfirmReadiness();
        var last = f.Input(uint.MaxValue); var zero = f.Input(0);
        CustomSourceReadyContext context = default;
        check(f.Boundary.NoteAcceptedInput(last) && f.Boundary.NoteAcceptedInput(zero)
            && !f.Boundary.TryOpen(last,out _) && f.Boundary.TryOpen(zero,out context) && context.FirstSourceFrame == 0,
            "serial uint source wrap selects exact newest consumed carrier and preserves zero source as data");
        check(!f.Boundary.NoteAcceptedInput(last) && f.Boundary.TryGet(f.Owner,out var retained) && retained == context,
            "pre-wrap late carrier cannot reopen or replace zero-frame boundary");
        var lives = new Fixture();
        var oldOwner = new WeaponOwnerContext(7,lives.Owner.AuthorityEpoch,3,5,ushort.MaxValue);
        var newOwner = new WeaponOwnerContext(7,lives.Owner.AuthorityEpoch,3,5,1);
        lives.Boundary.NoteAcceptedInput(lives.Input(1000,oldOwner)); lives.Ready(); lives.Boundary.ConfirmReadiness();
        check(lives.Boundary.NoteAcceptedInput(lives.Input(1001,newOwner))
            && lives.Boundary.TryOpen(lives.Input(1001,newOwner),out var wrapped) && wrapped.FirstSourceFrame == 1001
            && lives.Boundary.TryGet(newOwner,out retained) && retained == wrapped,"serial ushort life wrap remains same-occupant capability with exact new-life carrier");
        check(!lives.Boundary.NoteAcceptedInput(lives.Input(1002,oldOwner)),"pre-wrap life cannot move the frontier back after canonical life wrap");
    }
    private static void ReadinessRetirement(Action<bool,string> check)
    {
        foreach (bool opened in new[] { false,true })
        {
            var f = new Fixture();
            if (opened) f.Open();
            else { f.Boundary.NoteAcceptedInput(f.Input()); f.Ready(); f.Boundary.ConfirmReadiness(); }
            f.Peer.Invalidate();
            check(!f.Boundary.ConfirmReadiness() && !f.Boundary.TryGet(f.Owner,out var missing) && missing == default
                && !f.Boundary.TryOpen(f.Input(1001),out missing) && missing == default,
                "peer invalidation immediately revokes pending or open source boundary");
            f.Boundary.NoteAcceptedInput(f.Input(1002));
            check(!f.Boundary.TryOpen(f.Input(1002),out _) && !f.Boundary.TryGet(f.Owner,out _),
                "later trusted carrier cannot restore a retired peer capability");
        }
        var conflict = new Fixture(); conflict.Open();
        var changed = conflict.Peer.Wire.ReadyBytes.ToArray(); changed[65] = 4;
        check(conflict.Peer.Receive(changed,Fixture.Connection,4001,1) == CustomReadinessResult.Invalidated
            && !conflict.Boundary.TryGet(conflict.Owner,out _) && !conflict.Boundary.ConfirmReadiness(),
            "same-bootstrap contradictory readiness invalidates the actual peer and its source boundary");
        var replacement = new CustomReadinessPeer(conflict.Bootstrap,3,Fixture.Connection+1,0);
        replacement.Receive(replacement.Wire.ReadyBytes,Fixture.Connection+1,4002,0);
        var freshBoundary = new CustomPeerSourceBoundary(replacement);
        check(freshBoundary.ConfirmReadiness() && !freshBoundary.TryGet(conflict.Owner,out _)
            && !freshBoundary.TryOpen(conflict.Input(1001),out _),"different connection starts with no inherited accepted-input source frontier");
    }
    private static void MetadataWorlds(Action<bool,string> check)
    {
        foreach (uint world in new uint[] { 0,uint.MaxValue,1 })
        {
            var f = new Fixture(); f.Boundary.NoteAcceptedInput(f.Input()); f.Ready(world); f.Boundary.ConfirmReadiness();
            var input = f.Input(1001); f.Boundary.NoteAcceptedInput(input);
            check(f.Boundary.TryOpen(input,out var context) && context.OpenedWorld == world && context.FirstSourceFrame == 1001,
                "capability world metadata remains full-width data and is not converted into source or resource time");
        }
    }
    private static void SerialExhaustion(Action<bool,string> check)
    {
        foreach (bool opened in new[] { false,true })
        {
            var f = new Fixture();
            if (opened) f.Open();
            else { f.Boundary.NoteAcceptedInput(f.Input()); f.Ready(); f.Boundary.ConfirmReadiness(); }
            // Deliberate bounded-counter exhaustion seam; no 2^64 packet loop.
            typeof(CustomPeerSourceBoundary).GetField("_serial",BindingFlags.Instance|BindingFlags.NonPublic)!
                .SetValue(f.Boundary,ulong.MaxValue);
            check(!f.Boundary.NoteAcceptedInput(f.Input(1002)) && !f.Boundary.ConfirmReadiness()
                && !f.Boundary.TryOpen(f.Input(1002),out var unavailable) && unavailable == default
                && !f.Boundary.TryGet(f.Owner,out unavailable) && unavailable == default,
                "ingress serial exhaustion permanently closes pending/open boundary without wrap or context resurrection");
            check(!f.Boundary.NoteAcceptedInput(f.Input(1003)) && !f.Boundary.TryOpen(f.Input(1003),out _),
                "fresh later input cannot revive an exhausted accepted-input serial");
        }
        var field = typeof(CustomReadinessPeer).GetField("_evidence",BindingFlags.Instance|BindingFlags.NonPublic)!;
        for (int path=0;path<3;path++)
        {
            var reference = new Fixture(); reference.Open(); var first = reference.Peer.Evidence!;
            field.SetValue(reference.Peer,first with { });
            bool refused = path switch
            {
                0=>!reference.Boundary.ConfirmReadiness(),
                1=>!reference.Boundary.TryGet(reference.Owner,out _),
                _=>!reference.Boundary.TryOpen(reference.Input(1001),out _)
            };
            check(refused,"confirmation or either read path rejects equal-value-but-replaced peer publication: "+path);
            field.SetValue(reference.Peer,first);
            check(!reference.Boundary.TryGet(reference.Owner,out _) && !reference.Boundary.TryOpen(reference.Input(1001),out _)
                && !reference.Boundary.ConfirmReadiness(),
                "restoring an old peer publication after an observed replacement cannot reopen the boundary: "+path);
        }
    }
    private static void ClockPreflight(Action<bool,string> check)
    {
        var f = new Fixture(); var owner = f.Owner;
        var next = new WeaponOwnerContext(owner.MatchId,owner.AuthorityEpoch,owner.Slot,owner.Generation,10);
        var later = new WeaponOwnerContext(owner.MatchId,owner.AuthorityEpoch,owner.Slot,owner.Generation,11);
        var clock = new CustomSourceClock(owner,1000,5000);
        for (int i=0;i<10;i++)
            check(clock.CanBindLife(owner,1000,5000) && clock.CanBindLife(next,1010,5010),
                "life bind previews may inspect current and future source/world without consuming the available grant");
        check(clock.CanBindLife(next,1033,5001) && !clock.CanBindLife(next,1034,5001),
            "preflight uses the original occupant anchor with inclusive32 source lead");
        check(clock.TryBindLife(owner,1000,5000),"future preflight does not advance world frontier or consume the real initial life bind");
        check(!clock.CanBindLife(owner,1000,5000) && !clock.TryBindLife(owner,1000,5000),
            "current native life cannot preview or repeat its initial loadout grant");
        check(clock.CanBindLife(next,1010,5010) && clock.Observe(owner,1005,5005)
            && !clock.CanBindLife(next,1004,5004) && clock.CanBindLife(next,1005,5005)
            && clock.TryBindLife(next,1005,5005),
            "preflight neither moves frontier nor consumes next life while actual observation rejects backdated world");
        check(!clock.CanBindLife(owner,1006,5006) && !clock.CanBindLife(next,1006,5006)
            && clock.CanBindLife(later,1038,5006) && !clock.CanBindLife(later,1039,5006),
            "new native life retains the original occupant clock and cannot reset lead or once-per-life fences");
        foreach (var foreign in ForeignOwners(later))
            check(!clock.CanBindLife(foreign,1006,5006),"pure preflight rejects every foreign or invalid occupant identity");
        check(clock.CanBindLife(later,1100,5100) && clock.Observe(next,1006,5006)
            && clock.TryBindLife(later,1006,5006),"distant valid preflight leaves earlier genuine observation and life grant available");
        var wrap = new CustomSourceClock(owner,uint.MaxValue-2,uint.MaxValue-2);
        check(wrap.CanBindLife(owner,0,0) && wrap.TryBindLife(owner,uint.MaxValue-2,uint.MaxValue-2)
            && wrap.CanBindLife(next,0,0) && wrap.TryBindLife(next,0,0)
            && !wrap.CanBindLife(later,uint.MaxValue,uint.MaxValue),
            "preflight preserves serial uint source/world wrap and does not move frontier until real binding");
        var allocationClock = new CustomSourceClock(owner,1000,5000); allocationClock.TryBindLife(owner,1000,5000);
        bool valid = true;
        for (int i=0;i<10000;i++) valid &= allocationClock.CanBindLife(next,1032,5000) && !allocationClock.CanBindLife(owner,1000,5000);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i=0;i<100000;i++) valid &= allocationClock.CanBindLife(next,1032,5000) && !allocationClock.CanBindLife(owner,1000,5000);
        long bytes = GC.GetAllocatedBytesForCurrentThread()-before;
        check(valid && bytes == 0 && allocationClock.TryBindLife(next,1000,5000),
            "100000 warmed valid/denied life preflights allocate zero bytes and preserve the real next-life grant");
        Console.WriteLine($"Custom life preflight allocation: {bytes} bytes / 100000 warmed probes.");
    }
    private static bool Probe(Fixture f,CustomSourceReadyContext expected,uint frame)
    {
        var input = f.Input(frame);
        bool accepted = f.Boundary.NoteAcceptedInput(input);
        f.Boundary.ConfirmReadiness();
        return accepted && f.Boundary.TryOpen(input,out var context) && context == expected
            && f.Boundary.TryGet(f.Owner,out context) && context == expected
            && !f.Boundary.NoteAcceptedInput(input)
            && !f.Boundary.TryOpen(input with { AckSubFrame = 129 },out var unavailable) && unavailable == default;
    }
    private static void Allocation(Action<bool,string> check)
    {
        var f = new Fixture(); var expected = f.Open(); bool valid = true;
        for (uint i=1002;i<11002;i++) valid &= Probe(f,expected,i);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (uint i=11002;i<111002;i++) valid &= Probe(f,expected,i);
        long bytes = GC.GetAllocatedBytesForCurrentThread()-before;
        check(valid && bytes == 0,"100000 warmed accepted-input/duplicate/exact-consumption/lookup/mismatch boundary probes allocate zero bytes");
        Console.WriteLine($"Custom source boundary allocation: {bytes} bytes / 100000 warmed probes.");
    }
}
