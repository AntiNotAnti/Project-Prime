using System;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

// Pure bounded-store checks. Certificates below are explicitly supplied fixtures;
// they do not prove network admission, historical pose, trade, collision or HP mutation.
internal static class CustomPaymentChecks
{
    private static int _checks;
    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException("Custom payment: " + label);
        _checks++;
    }
    private static void Reject<T>(Action action, string label) where T : Exception
    {
        try { action(); } catch (T) { _checks++; return; }
        throw new InvalidOperationException("Custom payment expected rejection: " + label);
    }
    private sealed class Fixture : IDisposable
    {
        internal readonly WeaponOwnerContext Owner = new(7,0x123456789abcdef0UL,3,5,9);
        internal readonly WeaponDefinition A, B;
        internal readonly CustomWeaponManifest Manifest;
        internal readonly WeaponRegistry Registry;
        internal readonly CustomLaunchPayments Store;
        internal Fixture(int capacity = 4, uint frame = 100, int lifetime = 6)
        {
            var mechanics = RiftlineM7.Create().Mechanics;
            if (lifetime != mechanics.LifetimeTicks)
            {
                string canonical = System.Text.Encoding.UTF8.GetString(mechanics.ToCanonicalBytes());
                canonical = canonical.Replace("\"lifetimeTicks\":6","\"lifetimeTicks\":"
                    + lifetime.ToString(System.Globalization.CultureInfo.InvariantCulture),StringComparison.Ordinal);
                mechanics = WeaponMechanics.Parse(System.Text.Encoding.UTF8.GetBytes(canonical));
                Check(mechanics.LifetimeTicks == lifetime,"fixture derives validated canonical lifetime variant");
            }
            A = WeaponDefinition.Create(Guid.Parse("00000001-0000-0000-0000-000000000001"),"Payment A",mechanics);
            B = WeaponDefinition.Create(Guid.Parse("00000002-0000-0000-0000-000000000001"),"Payment B",mechanics);
            Manifest = new(new[] { A,B }); Registry = new(true,new[] { B,A });
            Store = new(Owner,Manifest,Registry,frame,capacity);
        }
        internal CustomAcceptedLaunch Launch(uint sequence = 1, uint frame = 100,
            WeaponOwnerContext? owner = null, WeaponDefinition? definition = null,
            WeaponDigest? manifest = null, ushort weapon = 10)
        {
            var who = owner ?? Owner; var exact = definition ?? A;
            var state = new WeaponRuntimeState(exact,who,frame);
            Check(state.SetEquipped(frame,true) == WeaponActionResult.Accepted,"fixture equips real resource state");
            var before = state.Snapshot();
            Check(state.TryFire(frame,sequence,true) == WeaponActionResult.Accepted,"fixture debits real one-round transition");
            var bootstrap = new WorldBootstrapIdentity(new(who.MatchId,who.AuthorityEpoch,1),1,who.Generation,1,1,1);
            var action = new CustomWeaponAction(bootstrap,manifest ?? Manifest.Digest,who.Slot,who.Life,
                new(weapon),sequence,frame,frame,0,CustomWeaponActionKind.Fire,0,new(0,.5f,0),new(0,0,-1));
            var certificate = new CustomSourceCertificate(action,frame,frame,0,Vector3.Zero,new(0,0,-1),true,false,false);
            Check(action.IsValid && certificate.IsValidFor(action),"fixture action/certificate is internally consistent, not authority proof");
            return new(exact,certificate,before,state.Snapshot());
        }
        internal (CustomLaunchPayments.LaunchLease Lease, CustomShotSource Source, CustomAcceptedLaunch Launch) Begin(
            uint sequence = 1, uint frame = 100, CustomLaunchPayments? store = null, WeaponOwnerContext? owner = null)
        {
            var launch = Launch(sequence,frame,owner);
            Check((store ?? Store).TryBeginLaunch(launch,frame,out var lease,out var source) && source != null,
                "consistent fixture launch obtains exactly its store source");
            return (lease,source!,launch);
        }
        public void Dispose() { Store.Dispose(); Registry.Dispose(); }
    }
    private static CustomContact Contact(CustomShotSource source, byte victim = 4,
        CustomImpactChannel channel = CustomImpactChannel.Body)
        => new(new(source.Action!.Value,new(victim,6,10),1,channel),new(1,2,3),false);
    private static CustomContactOutcome Settle(Fixture f, CustomShotSource source, CustomDamageResult health,
        uint frame = 100, CustomLaunchPayments? store = null, CustomContact? contact = null)
    {
        var payments = store ?? f.Store; var hit = contact ?? Contact(source);
        Check(payments.TryBeginContact(source,hit,frame,out var lease,out var prior) == CustomContactDecision.Reserved
            && prior == default,"first canonical contact reserves without fabricated prior outcome");
        Check(payments.CompleteContact(lease,health,out var outcome),"canonical observed health settles contact");
        lease.Dispose();
        Check(payments.TryGetOutcome(hit.Key,out var retained) && retained == outcome,"settled outcome survives lease disposal exactly");
        return outcome;
    }
    internal static int Run()
    {
        _checks = 0;
        IdentityJoins(); ContactTransitions(); HealthResults(); ContactDispositions(); RetentionAndReuse(); InvalidAndForeignLeases(); Lifetimes(); AllocationChecks();
        Console.WriteLine($"Custom payment: {_checks} pure state-machine checks passed; no live authority coverage.");
        return _checks;
    }
    private static void IdentityJoins()
    {
        using var f = new Fixture();
        var foreignOwners = new[]
        {
            new WeaponOwnerContext(8,f.Owner.AuthorityEpoch,3,5,9), new(7,f.Owner.AuthorityEpoch+1,3,5,9),
            new(7,f.Owner.AuthorityEpoch,2,5,9), new(7,f.Owner.AuthorityEpoch,3,6,9), new(7,f.Owner.AuthorityEpoch,3,5,10)
        };
        foreach (var owner in foreignOwners)
            Check(!f.Store.TryBeginLaunch(f.Launch(owner:owner),100,out var lease,out var source)
                && source == null && !f.Store.CommitEmitted(lease),"launch join rejects each complete owner identity mismatch");
        var otherManifest = new CustomWeaponManifest(new[] { f.A });
        Check(!f.Store.TryBeginLaunch(f.Launch(manifest:otherManifest.Digest),100,out _,out _),"manifest digest is mandatory launch join");
        Check(!f.Store.TryBeginLaunch(f.Launch(weapon:11),100,out _,out _),"runtime weapon cannot select another exact definition");
        Check(!f.Store.TryBeginLaunch(f.Launch(definition:f.B),100,out _,out _),"definition cannot claim another runtime weapon");
        var equalDefinition = WeaponDefinition.Create(f.A.Identity.ContentId,f.A.Name,f.A.Mechanics);
        Check(!ReferenceEquals(equalDefinition,f.A)
            && !f.Store.TryBeginLaunch(f.Launch(definition:equalDefinition),100,out _,out _),"equal definition copy is not exact frozen registry object");
        Check(!f.Store.TryBeginLaunch(default,100,out var empty,out var absent) && absent == null
            && !f.Store.CommitEmitted(empty),"default launch refuses with default outputs");
        foreach (var alter in new Func<CustomAcceptedLaunch,CustomAcceptedLaunch>[]
        {
            l => l with { Source = l.Source with { CanUseWeapon = false } },
            l => l with { Source = l.Source with { ObservedWorldFrame = 101 } },
            l => l with { Source = l.Source with { ObservedWorldFrame = 35 } },
            l => l with { Source = l.Source with { TargetWorldFrame = 101 } },
            l => l with { Source = l.Source with { TargetWorldFrame = 35 } },
            l => l with { Source = l.Source with { TargetWorldSubFrame = 1 } },
            l => l with { After = l.After with { Magazine = l.Before.Magazine } },
            l => l with { After = l.After with { LastAcceptedShotId = 2 } }
        }) Check(!f.Store.TryBeginLaunch(alter(f.Launch()),100,out _,out _),"invalid timing/resource/certificate refuses before admission");
        Check(f.Store.HighestSequence == 0,"foreign or inconsistent launch does not burn a valid sequence");
        var accepted = f.Begin(); Check(f.Store.CommitEmitted(accepted.Lease),"native-independent source begins flying");
        var original = Contact(accepted.Source);
        var identity = original.Key.Action;
        var keyVariants = new[]
        {
            original.Key with { Action = identity with { Owner = foreignOwners[0] } },
            original.Key with { Action = identity with { Owner = foreignOwners[1] } },
            original.Key with { Action = identity with { Owner = foreignOwners[2] } },
            original.Key with { Action = identity with { Owner = foreignOwners[3] } },
            original.Key with { Action = identity with { Owner = foreignOwners[4] } },
            original.Key with { Action = identity with { Manifest = otherManifest.Digest } },
            original.Key with { Action = identity with { Weapon = new(11) } },
            original.Key with { Action = identity with { Sequence = 2 } },
            original.Key with { Component = 0 }, original.Key with { Component = 2 },
            original.Key with { Victim = new(8,6,10) }, original.Key with { Victim = new(4,0,10) },
            original.Key with { Victim = new(4,6,0) }, original.Key with { Channel = (CustomImpactChannel)0 },
            original.Key with { Channel = (CustomImpactChannel)3 }
        };
        foreach (var key in keyVariants)
            Check(f.Store.TryBeginContact(accepted.Source,original with { Key = key },100,out _,out _) == CustomContactDecision.Refused,
                "contact rejects foreign action join and invalid victim/component/channel");
        Check(f.Store.TryBeginContact(CustomShotSource.FromLaunch(accepted.Launch),original,100,out _,out _) == CustomContactDecision.Refused,
            "equal reconstructed metadata cannot borrow exact store-owned source capability");
        Check(f.Store.TryBeginContact(accepted.Source with { },original,100,out _,out _) == CustomContactDecision.Refused,
            "immutable record clone is a distinct source capability");
        foreach (var point in new[] { new Vector3(float.NaN,0,0),new(float.PositiveInfinity,0,0),new(0,1_000_001,0) })
            Check(f.Store.TryBeginContact(accepted.Source,original with { Point = point },100,out _,out _) == CustomContactDecision.Refused,
                "nonfinite or out-of-world contact cannot consume valid flight");
        var outcome = Settle(f,accepted.Source,new(200,135,0,0,1));
        foreach (var key in new[] { original.Key with { Victim = new(5,6,10) },original.Key with { Victim = new(4,7,10) },
            original.Key with { Victim = new(4,6,11) },original.Key with { Channel = CustomImpactChannel.Halfturret } })
        {
            Check(!f.Store.TryGetOutcome(key,out _),"every victim identity/channel dimension is required for outcome lookup");
            Check(f.Store.TryBeginContact(accepted.Source,original with { Key = key },100,out _,out _) == CustomContactDecision.Consumed,
                "settled nonpiercing component cannot revive for another victim/channel");
        }
        Check(f.Store.TryBeginContact(accepted.Source,original with { Point = new(9,8,7),Headshot = true },100,out _,out var duplicate)
            == CustomContactDecision.Duplicate && duplicate == outcome,"retry returns immutable original contact and health rather than altered point/head flag");
        foreach (var key in keyVariants) Check(!f.Store.TryGetOutcome(key,out _),"foreign/invalid key never retrieves retained outcome");
        accepted.Lease.Dispose(); Check(!f.Store.IsQuarantined,"disposing committed flying launch after terminal is harmless");
        using var zeroTarget = new Fixture(frame:50);
        var invalidZero = zeroTarget.Launch(frame:50);
        Check(!zeroTarget.Store.TryBeginLaunch(invalidZero with { Source = invalidZero.Source with { TargetWorldFrame = 0 } },50,out _,out _),
            "zero target world is rejected even when inside the age window");
        using var oldest = new Fixture(); var oldestLaunch = oldest.Launch();
        Check(oldest.Store.TryBeginLaunch(oldestLaunch with { Source = oldestLaunch.Source with
            { ObservedWorldFrame = 36,TargetWorldFrame = 36 } },100,out var oldestLease,out _),
            "exact inclusive maximum observation and target ages remain admissible metadata");
        oldest.Store.CompleteNoSpawn(oldestLease);
    }
    private static void ContactTransitions()
    {
        using (var f = new Fixture())
        {
            var launch = f.Begin(); var hit = Contact(launch.Source);
            Check(f.Store.TryBeginContact(launch.Source,hit,100,out var contact,out _) == CustomContactDecision.Reserved,
                "catch-up collision can reserve before emitter returns");
            Check(!f.Store.CompleteNoSpawn(launch.Lease),"contact makes no-spawn refund permanently unavailable");
            Check(f.Store.TryBeginContact(launch.Source,hit,100,out _,out _) == CustomContactDecision.Busy
                && f.Store.TryBeginContact(launch.Source,Contact(launch.Source,5),100,out _,out _) == CustomContactDecision.Busy,
                "reentrant same/different contact cannot enter reserved canonical call");
            Check(f.Store.CompleteContact(contact,new(100,100,30,30,0),out var zero) && !zero.Lethal,
                "protected zero-damage contact still settles terminal outcome");
            Check(!f.Store.CompleteNoSpawn(launch.Lease) && f.Store.CommitEmitted(launch.Lease)
                && !f.Store.CommitEmitted(launch.Lease),"emitter commit after catch-up preserves terminal and is single-use");
            Check(f.Store.TryBeginContact(launch.Source,hit,100,out _,out var duplicate) == CustomContactDecision.Duplicate && duplicate == zero,
                "protected retry returns retained zero result");
            Check(f.Store.TryBeginContact(launch.Source,Contact(launch.Source,5,CustomImpactChannel.Halfturret),100,out _,out _)
                == CustomContactDecision.Consumed,"protected contact cannot retry through another target/channel");
            contact.Dispose(); launch.Lease.Dispose(); Check(!f.Store.IsQuarantined,"completed leases dispose harmlessly");
        }
        using (var f = new Fixture())
        {
            var launch = f.Begin();
            Check(f.Store.CompleteNoSpawn(launch.Lease) && !f.Store.CompleteNoSpawn(launch.Lease)
                && !f.Store.CommitEmitted(launch.Lease),"definite no-spawn is terminal and single-use");
            Check(f.Store.TryBeginContact(launch.Source,Contact(launch.Source),100,out _,out _) == CustomContactDecision.Consumed,
                "no-spawn source cannot later acquire a contact");
            launch.Lease.Dispose(); Check(!f.Store.IsQuarantined,"definite no-spawn disposal is safe");
            Check(!f.Store.TryBeginLaunch(launch.Launch,100,out _,out _),"no-spawn retains denied sequence fence");
        }
        using (var f = new Fixture())
        {
            var uncertain = f.Begin(); var independent = f.Begin(2);
            Check(f.Store.CommitEmitted(independent.Lease),"independent admitted flight begins before another uncertainty");
            var outcome = Settle(f,uncertain.Source,new(200,135,0,0,1)); uncertain.Lease.Dispose();
            Check(f.Store.IsQuarantined && f.Store.TryGetOutcome(outcome.Contact.Key,out var retained) && retained == outcome,
                "emitter throw/dispose after paid catch-up quarantines without erasing outcome");
            Check(!f.Store.TryBeginLaunch(f.Launch(3),100,out _,out _),"quarantine prevents new launches");
            Settle(f,independent.Source,new(100,0,0,0,2));
            Check(f.Store.IsQuarantined,"quarantine preserves settlement of independently admitted flight");
        }
        foreach (bool protectedContact in new[] { false,true })
        {
            using var f = new Fixture(); var pending = f.Begin();
            var paid = Settle(f,pending.Source,protectedContact ? new(100,100,0,0,0) : new(100,35,0,0,1));
            var independent = f.Begin(2,101); f.Store.CommitEmitted(independent.Lease);
            Check(f.Store.Advance(108) && !f.Store.IsQuarantined,"terminal pending emitter remains within exact launch deadline");
            Check(f.Store.Advance(109) && f.Store.IsQuarantined
                && f.Store.TryGetOutcome(paid.Contact.Key,out var retained) && retained == paid,
                "paid/protected catch-up with lost emitter return quarantines at deadline without erasing terminal");
            Check(!f.Store.CommitEmitted(pending.Lease) && !f.Store.CompleteNoSpawn(pending.Lease)
                && !f.Store.TryBeginLaunch(f.Launch(3,109),109,out _,out _),
                "timed-out terminal emitter cannot commit, refund or admit another launch");
            Settle(f,independent.Source,new(100,90,0,0,2),109);
            pending.Lease.Dispose();
            Check(f.Store.TryGetOutcome(paid.Contact.Key,out retained) && retained == paid,
                "late pending-emitter disposal preserves immutable paid/protected terminal");
        }
        foreach (bool protectedContact in new[] { false,true })
        {
            using var f = new Fixture(1,lifetime:600); var pending = f.Begin();
            var paid = Settle(f,pending.Source,protectedContact ? new(100,100,0,0,0) : new(100,35,0,0,1));
            Check(f.Store.Advance(356) && !f.Store.IsQuarantined
                && f.Store.TryGetOutcome(paid.Contact.Key,out var retained) && retained == paid,
                "long-flight paid/protected pending emitter retains outcome through exact receipt boundary");
            Check(f.Store.Advance(357) && f.Store.IsQuarantined && !f.Store.TryGetOutcome(paid.Contact.Key,out _),
                "receipt expiry quarantines still-pending long-flight emitter before clearing terminal slot");
            Check(!f.Store.CommitEmitted(pending.Lease) && !f.Store.CompleteNoSpawn(pending.Lease)
                && !f.Store.CanReserveLaunch(357) && !f.Store.TryBeginLaunch(f.Launch(2,357),357,out _,out _),
                "stale long-flight emitter cannot commit/refund and free index cannot admit another launch");
            Check(f.Store.HighestSequence == 1 && !f.Store.TryBeginLaunch(f.Launch(1,357),357,out _,out _)
                && f.Store.TryBeginContact(pending.Source,paid.Contact,357,out _,out _) == CustomContactDecision.Refused,
                "long-flight receipt expiry never revives original sequence or source capability");
            pending.Lease.Dispose();
            Check(f.Store.IsQuarantined,"late long-flight emitter disposal cannot clear quarantine");
        }
    }
    private static void HealthResults()
    {
        foreach (var health in new[] { new CustomDamageResult(200,135,0,0,1),new(1,1,100,68,2),
            new(100,68,100,67,3),new(65,0,0,0,4),new(1,1,0,0,0),new(0,0,32,0,5),new(65535,0,65535,0,6) })
        {
            using var f = new Fixture(); var launch = f.Begin(); f.Store.CommitEmitted(launch.Lease);
            var outcome = Settle(f,launch.Source,health,contact:Contact(launch.Source,channel:
                health.TurretBefore > 0 ? CustomImpactChannel.Halfturret : CustomImpactChannel.Body));
            Check(outcome.Health == health && outcome.AuthorityFrame == 100
                && outcome.Health.BodyRemoved == health.BodyBefore-health.BodyAfter
                && outcome.Health.TurretRemoved == health.TurretBefore-health.TurretAfter
                && outcome.Lethal == (health.BodyBefore > 0 && health.BodyAfter == 0),
                "body/turret/split/protected/lethal/maximal deltas derive exactly from declared observations");
        }
        foreach (var invalid in new[] { new CustomDamageResult(-1,0,0,0,0),new(65536,0,0,0,1),new(100,-1,0,0,1),
            new(100,101,0,0,0),new(100,100,-1,0,0),new(100,100,65536,0,1),new(100,100,10,-1,1),
            new(100,100,10,11,0),new(100,99,0,0,0),new(100,100,1,0,0),new(100,100,0,0,1) })
        {
            using var f = new Fixture(); var launch = f.Begin(); var hit = Contact(launch.Source);
            Check(!invalid.IsValid,"canonical completion fixture is invalid");
            Check(f.Store.TryBeginContact(launch.Source,hit,100,out var lease,out _) == CustomContactDecision.Reserved,
                "invalid canonical completion fixture reserves normally");
            Check(!f.Store.CompleteContact(lease,invalid,out var result) && result == default && f.Store.IsQuarantined,
                "invalid health/event completion quarantines instead of publishing invented result");
            Check(!f.Store.TryGetOutcome(hit.Key,out _) && !f.Store.CompleteContact(lease,new(100,99,0,0,1),out _)
                && !f.Store.CompleteNoSpawn(launch.Lease) && !f.Store.CommitEmitted(launch.Lease),
                "invalid completion cannot be repaired, refunded or recommitted");
            lease.Dispose(); launch.Lease.Dispose();
        }
    }
    private static void ContactDispositions()
    {
        using (var f = new Fixture())
        {
            var launch = f.Begin(); var hit = Contact(launch.Source);
            Check(f.Store.TryBeginContact(launch.Source,hit,100,out var lease,out _) == CustomContactDecision.Reserved,
                "trade-denied physical contact reserves component before classification");
            Check(f.Store.CompleteContact(lease,new(100,100,20,20,0),out var outcome,CustomContactDisposition.TradeDenied)
                && outcome.Disposition == CustomContactDisposition.TradeDenied && !outcome.Lethal,
                "trade-denied contact retains typed terminal zero outcome");
            Check(f.Store.TryBeginContact(launch.Source,hit with { Key = hit.Key with { Victim = new(5,6,10) } },100,out _,out _)
                == CustomContactDecision.Consumed,"trade denial cannot revive component against another victim");
            lease.Dispose(); f.Store.CommitEmitted(launch.Lease);
        }
        foreach (var item in new[] {
            (CustomContactDisposition.TradeDenied,new CustomDamageResult(100,99,0,0,1)),
            ((CustomContactDisposition)255,new CustomDamageResult(100,100,0,0,0)) })
        {
            using var f = new Fixture(); var launch = f.Begin();
            f.Store.TryBeginContact(launch.Source,Contact(launch.Source),100,out var lease,out _);
            Check(!f.Store.CompleteContact(lease,item.Item2,out _,item.Item1) && f.Store.IsQuarantined,
                "denied-with-damage or unknown outcome disposition quarantines rather than inventing a payment");
            lease.Dispose(); launch.Lease.Dispose();
        }
    }
    private static void RetentionAndReuse()
    {
        using (var f = new Fixture(1))
        {
            var first = f.Begin(); f.Store.CommitEmitted(first.Lease);
            var hit = Contact(first.Source);
            Check(f.Store.TryBeginContact(first.Source,hit,100,out var oldContact,out _) == CustomContactDecision.Reserved,
                "terminal fixture reserves only slot");
            Check(f.Store.CompleteContact(oldContact,new(100,99,0,0,1),out var original),"terminal fixture occupies only slot");
            Check(!f.Store.TryBeginLaunch(f.Launch(2),100,out var refused,out var absent) && absent == null
                && !f.Store.CommitEmitted(refused) && f.Store.HighestSequence == 2,"saturation refuses without eviction and burns denied sequence");
            Check(f.Store.Advance(356) && f.Store.TryGetOutcome(hit.Key,out var retained) && retained == original,
                "terminal retained through exact 256-tick inclusive boundary");
            Check(!f.Store.Advance(355) && f.Store.TryGetOutcome(hit.Key,out _),"stale authority cannot expire retained outcome");
            Check(f.Store.Advance(357) && !f.Store.TryGetOutcome(hit.Key,out _),"terminal expires only after retention boundary");
            Check(!f.Store.TryBeginLaunch(f.Launch(1,357),357,out _,out _)
                && !f.Store.TryBeginLaunch(f.Launch(2,357),357,out _,out _),"expiry never revives settled or capacity-denied sequences");
            var next = f.Begin(3,357);
            oldContact.Dispose(); first.Lease.Dispose();
            Check(!f.Store.IsQuarantined && !f.Store.CompleteContact(oldContact,new(100,0,0,0,2),out _)
                && !f.Store.CommitEmitted(first.Lease) && !f.Store.CompleteNoSpawn(first.Lease),"stale lease tokens cannot operate on reused index");
            Check(f.Store.CommitEmitted(next.Lease),"replacement lease survives stale disposal");
            Settle(f,next.Source,new(100,90,0,0,2),357);
        }
        foreach (bool emitted in new[] { false,true })
        {
            using var f = new Fixture(1); var launch = f.Begin();
            if (emitted) Check(f.Store.CommitEmitted(launch.Lease),"flight timeout fixture commits emission");
            uint duration = (uint)f.A.Mechanics.LifetimeTicks+2;
            Check(f.Store.Advance(100+duration) && !f.Store.IsQuarantined,"flight/emitter remains present at exact duration boundary");
            Check(f.Store.Advance(101+duration) && f.Store.IsQuarantined == !emitted,"expired uncertain emitter quarantines; expired admitted flight does not");
            Check(f.Store.TryBeginContact(launch.Source,Contact(launch.Source),101+duration,out _,out _) == CustomContactDecision.Consumed,
                "expired launch cannot reserve contact");
        }
        using (var f = new Fixture(1))
        {
            var flight = f.Begin(); f.Store.CommitEmitted(flight.Lease); f.Store.Advance(109);
            Check(f.Store.Advance(365) && !f.Store.TryBeginLaunch(f.Launch(2,365),365,out _,out _),
                "faulted flight retains occupied slot through exact fault-retention boundary");
            Check(f.Store.Advance(366),"faulted flight advances beyond retention");
            Check(f.Store.TryBeginLaunch(f.Launch(3,366),366,out var replacement,out _),
                "ordinary expired flight releases slot after retention without quarantining store");
            f.Store.CompleteNoSpawn(replacement);
        }
        using (var f = new Fixture(1))
        {
            var launch = f.Begin(); var hit = Contact(launch.Source);
            f.Store.TryBeginContact(launch.Source,hit,100,out var contact,out _);
            Check(f.Store.Advance(109) && f.Store.IsQuarantined
                && !f.Store.CompleteContact(contact,new(100,0,0,0,1),out _),"timed-out canonical call cannot later publish uncertain outcome");
        }
        using (var f = new Fixture(1))
        {
            var launch = f.Begin(); f.Store.CompleteNoSpawn(launch.Lease);
            Check(f.Store.Advance(356) && !f.Store.TryBeginLaunch(f.Launch(2,356),356,out _,out _),"no-spawn tombstone cannot be evicted at retention boundary");
            Check(f.Store.Advance(357),"no-spawn retirement advances past retention");
            Check(f.Store.TryBeginLaunch(f.Launch(3,357),357,out var next,out _),"expired no-spawn slot becomes reusable");
            f.Store.CompleteNoSpawn(next);
        }
        using (var f = new Fixture(1,uint.MaxValue-2))
        {
            var launch = f.Begin(frame:uint.MaxValue-2); f.Store.CommitEmitted(launch.Lease);
            var outcome = Settle(f,launch.Source,new(100,99,0,0,1),uint.MaxValue-2);
            Check(f.Store.Advance(253) && f.Store.TryGetOutcome(outcome.Contact.Key,out _),"retention uses serial elapsed ticks across uint wrap");
            Check(!f.Store.Advance(uint.MaxValue) && f.Store.TryGetOutcome(outcome.Contact.Key,out _),"pre-wrap stale time cannot roll frontier backward");
            Check(f.Store.Advance(254) && !f.Store.TryGetOutcome(outcome.Contact.Key,out _),"wrapped retention expires at 257 elapsed ticks");
        }
        using (var f = new Fixture(1))
        {
            var final = f.Begin(uint.MaxValue); f.Store.CompleteNoSpawn(final.Lease); f.Store.Advance(357);
            Check(f.Store.HighestSequence == uint.MaxValue && !f.Store.TryBeginLaunch(f.Launch(1,357),357,out _,out _),
                "exhausted sequence domain never wraps into replayable low sequence");
        }
    }
    private static void InvalidAndForeignLeases()
    {
        using var f = new Fixture(); using var other = new CustomLaunchPayments(f.Owner,f.Manifest,f.Registry,100,4);
        CustomLaunchPayments.LaunchLease missingLaunch = default; CustomLaunchPayments.ContactLease missingContact = default;
        missingLaunch.Dispose(); missingContact.Dispose();
        Check(!f.Store.CommitEmitted(missingLaunch) && !f.Store.CompleteNoSpawn(missingLaunch)
            && !f.Store.CompleteContact(missingContact,new(1,0,0,0,1),out _),"default leases refuse harmlessly");
        Check(f.Store.TryBeginContact(null!,default,100,out _,out _) == CustomContactDecision.Refused
            && !f.Store.TryGetOutcome(default,out _),"null source/default key refuse without throwing");
        var launch = f.Begin(); var hit = Contact(launch.Source);
        Check(!other.CommitEmitted(launch.Lease) && !other.CompleteNoSpawn(launch.Lease)
            && other.TryBeginContact(launch.Source,hit,100,out _,out _) == CustomContactDecision.Refused,"foreign store cannot acquire launch or source");
        Check(f.Store.TryBeginContact(launch.Source,hit,100,out var contact,out _) == CustomContactDecision.Reserved
            && !other.CompleteContact(contact,new(100,0,0,0,1),out _),"foreign store cannot complete contact lease");
        contact.Dispose(); contact.Dispose(); launch.Lease.Dispose();
        Check(f.Store.IsQuarantined && !other.IsQuarantined,"abandoned contact quarantines only its own store; repeat disposal harmless");
        using var uncertain = new CustomLaunchPayments(f.Owner,f.Manifest,f.Registry,100,1);
        var pending = f.Begin(store:uncertain); pending.Lease.Dispose();
        Check(uncertain.IsQuarantined && !uncertain.CommitEmitted(pending.Lease),"uncommitted emitter disposal fails closed");
        Reject<ArgumentNullException>(() => new CustomLaunchPayments(f.Owner,null!,f.Registry,100),"null manifest");
        Reject<ArgumentNullException>(() => new CustomLaunchPayments(f.Owner,f.Manifest,null!,100),"null registry");
        foreach (int capacity in new[] { 0,-1,CustomLaunchPayments.MaximumCapacity+1 })
            Reject<ArgumentException>(() => new CustomLaunchPayments(f.Owner,f.Manifest,f.Registry,100,capacity),"bounded constructor capacity");
        Reject<ArgumentException>(() => new CustomLaunchPayments(default,f.Manifest,f.Registry,100),"default owner");
        var wrongManifest = new CustomWeaponManifest(new[] { f.A });
        Reject<ArgumentException>(() => new CustomLaunchPayments(f.Owner,wrongManifest,f.Registry,100),"manifest/registry count mismatch");
        var alien = WeaponDefinition.Create(Guid.Parse("00000003-0000-0000-0000-000000000001"),"Other revision",f.A.Mechanics);
        var wrongRows = new CustomWeaponManifest(new[] { f.A,alien });
        Reject<ArgumentException>(() => new CustomLaunchPayments(f.Owner,wrongRows,f.Registry,100),"equal count does not replace exact manifest row resolution");
        using var maximum = new CustomLaunchPayments(f.Owner,f.Manifest,f.Registry,100,CustomLaunchPayments.MaximumCapacity);
        Check(maximum.Capacity == CustomLaunchPayments.MaximumCapacity,"largest permitted capacity remains explicit and bounded");
        using var disposedRegistry = new WeaponRegistry(true,new[] { f.A,f.B }); disposedRegistry.Dispose();
        Reject<ArgumentException>(() => new CustomLaunchPayments(f.Owner,f.Manifest,disposedRegistry,100),"disposed registry");
    }
    private static void Lifetimes()
    {
        using var f = new Fixture(); var old = f.Begin(); f.Store.CommitEmitted(old.Lease);
        var newOwner = new WeaponOwnerContext(f.Owner.MatchId,f.Owner.AuthorityEpoch,f.Owner.Slot,f.Owner.Generation,10);
        using var nextLife = new CustomLaunchPayments(newOwner,f.Manifest,f.Registry,100,2);
        var replacement = f.Begin(store:nextLife,owner:newOwner); nextLife.CommitEmitted(replacement.Lease);
        var oldOutcome = Settle(f,old.Source,new(100,35,0,0,1));
        var newOutcome = Settle(f,replacement.Source,new(100,35,0,0,2),store:nextLife);
        Check(old.Source.Sequence == replacement.Source.Sequence && oldOutcome.Contact.Key != newOutcome.Contact.Key
            && !nextLife.TryGetOutcome(oldOutcome.Contact.Key,out _) && !f.Store.TryGetOutcome(newOutcome.Contact.Key,out _),
            "respawn stores retain admitted old-life flight and independently settle same-number new-life shot");
        f.Store.RetireOccupant(); f.Store.RetireOccupant();
        Check(f.Store.IsRetired && !f.Store.Advance(101) && !f.Store.TryGetOutcome(oldOutcome.Contact.Key,out _)
            && !f.Store.TryBeginLaunch(f.Launch(2,101),101,out _,out _)
            && f.Store.TryBeginContact(old.Source,Contact(old.Source),101,out _,out _) == CustomContactDecision.Refused,
            "occupant retirement clears receipts and invalidates every old life operation");
        old.Lease.Dispose();
        Check(nextLife.TryGetOutcome(newOutcome.Contact.Key,out var retained) && retained == newOutcome,
            "retiring old store cannot erase independent replacement-life outcome");
        nextLife.Dispose(); replacement.Lease.Dispose();
        Check(nextLife.IsRetired && !nextLife.TryGetOutcome(newOutcome.Contact.Key,out _),"disposal retires replacement store idempotently");
    }
    private static void AllocationChecks()
    {
        using var f = new Fixture(CustomLaunchPayments.DefaultCapacity);
        Check(f.Store.CanReserveLaunch(100) && f.Store.HighestSequence == 0,
            "empty-store preflight does not create a launch or consume sequence");
        // Populate all earlier slots outside measurement, so exact and missing
        // lookups and the duplicate-contact probe traverse the bounded array.
        for (uint sequence=1; sequence<CustomLaunchPayments.DefaultCapacity; sequence++)
        {
            var prior = f.Begin(sequence);
            Check(f.Store.CompleteNoSpawn(prior.Lease),"allocation fixture retains occupied prior slot");
        }
        var launch = f.Begin(CustomLaunchPayments.DefaultCapacity);
        Check(f.Store.CommitEmitted(launch.Lease),"allocation fixture commits last-slot emission");
        var contact = Contact(launch.Source);
        var expected = Settle(f,launch.Source,new(200,135,0,0,1));
        var alteredRetry = contact with { Point = new(9,8,7),Headshot = true };
        var missing = contact.Key with { Victim = contact.Key.Victim with { Life = 11 } };
        Check(!f.Store.CanReserveLaunch(100)
            && f.Store.HighestSequence == CustomLaunchPayments.DefaultCapacity,
            "full-store preflight refuses without burning sequence or evicting outcome");
        bool correct = true;
        for (int i=0; i<10000; i++)
        {
            correct &= f.Store.TryBeginContact(launch.Source,alteredRetry,100,out _,out var duplicate)
                == CustomContactDecision.Duplicate && duplicate == expected;
            correct &= f.Store.TryGetOutcome(contact.Key,out var exact) && exact == expected;
            correct &= !f.Store.TryGetOutcome(missing,out var absent) && absent == default;
            correct &= !f.Store.CanReserveLaunch(100);
        }
        long duplicateBytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i=0; i<100000; i++)
        {
            correct &= f.Store.TryBeginContact(launch.Source,alteredRetry,100,out _,out var duplicate)
                == CustomContactDecision.Duplicate && duplicate == expected;
        }
        duplicateBytes = GC.GetAllocatedBytesForCurrentThread()-duplicateBytes;
        Check(correct && duplicateBytes == 0,"100000 warmed last-slot duplicate contacts allocate zero bytes and preserve original classification");
        long exactBytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i=0; i<100000; i++)
        {
            correct &= f.Store.TryGetOutcome(contact.Key,out var exact) && exact == expected;
        }
        exactBytes = GC.GetAllocatedBytesForCurrentThread()-exactBytes;
        Check(correct && exactBytes == 0,"100000 warmed last-slot exact outcome lookups allocate zero bytes");
        long missingBytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i=0; i<100000; i++)
        {
            correct &= !f.Store.TryGetOutcome(missing,out var absent) && absent == default;
        }
        missingBytes = GC.GetAllocatedBytesForCurrentThread()-missingBytes;
        Check(correct && missingBytes == 0,"100000 warmed mismatched outcome lookups allocate zero bytes and return default");
        long preflightBytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i=0; i<100000; i++) correct &= !f.Store.CanReserveLaunch(100);
        preflightBytes = GC.GetAllocatedBytesForCurrentThread()-preflightBytes;
        Check(correct && preflightBytes == 0,"100000 warmed full-store preflights allocate zero bytes");
        Check(f.Store.TryGetOutcome(contact.Key,out var retained) && retained == expected
            && !f.Store.IsQuarantined && f.Store.HighestSequence == CustomLaunchPayments.DefaultCapacity,
            "allocation probes preserve exact retained outcome and sequence fence");
        // Manifest, registry and warm-up construction are outside the counted
        // interval. Report runtime-dependent store creation cost informationally.
        using (var warm = new CustomLaunchPayments(f.Owner,f.Manifest,f.Registry,100))
            Check(warm.Capacity == CustomLaunchPayments.DefaultCapacity,"construction warm-up uses default bounded capacity");
        long constructionBytes = GC.GetAllocatedBytesForCurrentThread();
        var measured = new CustomLaunchPayments(f.Owner,f.Manifest,f.Registry,100);
        constructionBytes = GC.GetAllocatedBytesForCurrentThread()-constructionBytes;
        Console.WriteLine($"Default-capacity custom payment store construction: {constructionBytes} bytes; manifest/registry prebuilt.");
        Check(measured.Capacity == CustomLaunchPayments.DefaultCapacity,"measured store retains default capacity");
        GC.KeepAlive(measured); measured.Dispose();
    }
}
