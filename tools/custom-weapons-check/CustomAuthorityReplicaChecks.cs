using System.Reflection;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

internal static partial class CustomAuthoritySceneChecks
{
    private static object ReplicaField(object value,string name)
        => value.GetType().GetField(name,Hidden)!.GetValue(value)!;
    private static CustomLaunchPayments ReplicaPayments(Fixture f,WeaponOwnerContext owner)
    {
        foreach (var value in (Array)ReplicaField(f.Root,"_lives"))
            if (value!=null && (WeaponOwnerContext)ReplicaField(value,"Owner")==owner)
                return (CustomLaunchPayments)ReplicaField(value,"Payments");
        throw new InvalidOperationException("Actual retained custom life missing.");
    }
    private static int ReplicaAllocatedLives(Fixture f)
    { int count=0;foreach (var life in (Array)ReplicaField(f.Root,"_lives")) if (life!=null) count++;return count; }
    private static void ReplicaCaptureEmpty()
    {
        using var f=new Fixture(automaticSourceBoundary:true);
        var slots=new CustomReplicaSlot[8];var flights=new CustomReplicaFlight[64];
        // Explicit control-plane viewer row with no native spawn/life. Capture
        // must represent it without ShotKey.For/EnsureLife or custom BindLife.
        NetSession.SlotOccupied[7]=true;NetSession.SlotSpectating[7]=true;NetPlayerLifecycle.SetOccupant(7,1);
        ushort absentLife=NetPlayerLifecycle.Get(6);long spawns=NetPlayerLifecycle.Spawns;
        Check(NetPlayerLifecycle.Get(7)==0 && ReplicaAllocatedLives(f)==0,"viewer fixture starts occupied/spectating with genuine native Life0 and no custom life");
        Check(f.Root.TryCaptureReplica(slots,flights,out var capture) && capture.SlotCount==8 && capture.FlightCount==0
            && capture.MatchId==f.Owner.MatchId && capture.AuthorityEpoch==f.Owner.AuthorityEpoch
            && capture.Manifest==f.Root.Manifest.Digest && capture.World==NetSession.NetFrame,
            "empty read-only baseline reports exact stream/manifest/world and fixed eight slot rows");
        Check(!slots[6].Occupied && slots[6].Slot==6 && slots[6].Generation==0 && slots[6].Life==0
            && !slots[6].HasWeapon && slots[6].State==default && slots[6].Selected==default,
            "absent slot produces explicit absence with no invented life, weapon or resource state");
        Check(slots[7] is { Slot:7,Occupied:true,Generation:1,Life:0,Spectating:true,HasWeapon:false }
            && slots[7].State==default && ReplicaAllocatedLives(f)==0 && f.Pool.Length==0
            && NetPlayerLifecycle.Get(7)==0 && NetPlayerLifecycle.Get(6)==absentLife && NetPlayerLifecycle.Spawns==spawns,
            "Life0 spectator and unbound real actors cannot receive loadout/pool/life grants through capture");
        NetSession.SlotOccupied[7]=false;NetSession.SlotSpectating[7]=false;
        Check(f.StepFailures==0,"empty authority capture has no simulation exception");
    }
    private static void ReplicaCaptureAtomic()
    {
        using var f=new Fixture();var beam=f.Fire(process:false);
        var slotSentinel=new CustomReplicaSlot(7,true,99,77,true,false,default,123,456,default);
        var flightSentinel=new CustomReplicaFlight(default,default,123,456,Vector3.One,Vector3.One,Vector3.One,Vector3.One,7,9);
        var slots=Enumerable.Repeat(slotSentinel,9).ToArray();var flights=Enumerable.Repeat(flightSentinel,65).ToArray();
        foreach (var sizes in new[] { (0,0),(7,64),(8,63),(0,64),(8,0) })
            Check(!f.Root.TryCaptureReplica(slots.AsSpan(0,sizes.Item1),flights.AsSpan(0,sizes.Item2),out var failed)
                && failed==default && slots.All(x=>x==slotSentinel) && flights.All(x=>x==flightSentinel),
                "every insufficient full-bound span fails with no partial state/flight writes or output counts");
        Check(f.Root.BeginSourceInputPhase(),"capture refusal fixture enters real source phase");
        Check(!f.Root.TryCaptureReplica(slots,flights,out var during) && during==default
            && slots.All(x=>x==slotSentinel) && flights.All(x=>x==flightSentinel),"source-phase capture is refused atomically");
        f.Root.CompleteSourceInputPhase(capture:false);
        Vector3 direction=beam.Direction;beam.Direction=new(float.NaN,0,0);
        Check(!f.Root.TryCaptureReplica(slots,flights,out var invalid) && invalid==default
            && slots.All(x=>x==slotSentinel) && flights.All(x=>x==flightSentinel),
            "late invalid physical-flight validation cannot leak already-staged slot output");
        beam.Direction=direction;
        Check(((CustomReplicaSlot[])ReplicaField(f.Root,"_replicaSlots")).All(x=>x==default)
            && ((CustomReplicaFlight[])ReplicaField(f.Root,"_replicaFlights")).All(x=>x==default)
            && !(bool)ReplicaField(f.Root,"_capturingReplica"),"failed staging clears all scratch rows and capture reentry guard");
        Check(f.Root.TryCaptureReplica(slots,flights,out var capture) && capture.FlightCount==1
            && slots[8]==slotSentinel && flights[1]==flightSentinel && flights[64]==flightSentinel,
            "successful full-bound capture writes only eight slots and active flight prefix, preserving caller tails");
        f.Root.Dispose();Array.Fill(slots,slotSentinel);Array.Fill(flights,flightSentinel);
        Check(!f.Root.TryCaptureReplica(slots,flights,out var disposed) && disposed==default
            && slots.All(x=>x==slotSentinel) && flights.All(x=>x==flightSentinel),"disposed authority refuses capture without touching destination memory");
    }
    private static BeamProjectileEntity ReplicaHipFlight(Fixture f)
    {
        Vector3 aim=f.Aim();
        Check(f.Process(f.Action(CustomWeaponActionKind.Optic,optic:0)).Result==WeaponActionResult.Accepted,"actual custom journal selects hip optic for spread observation");
        var action=f.Action(CustomWeaponActionKind.Fire,optic:0,aim:aim);var receipt=f.Process(action);
        Check(receipt.Emission==CustomEmissionDisposition.Spawned,"hip-spread fixture emits through actual custom/native projectile factory");
        var beam=f.Pool.Single(x=>x.ModCustomShot?.Action==CustomActionIdentity.For(action));
        Console.WriteLine($"REPLICA PHYSICAL FIXTURE: aim={action.Direction} actual={beam.Direction} age={beam.Age} remaining={beam.Lifespan} flags={beam.Flags} position={beam.Position} victim={f.Victim.Position} frameTime={f.Scene.FrameTime}");
        Check(beam.Age==0 && beam.Lifespan>0 && !beam.Flags.TestFlag(BeamFlags.Collided)
            && (beam.Direction-action.Direction).LengthSquared>1e-8f,
            "actual freshly emitted unscoped projectile retains post-spread direction and initial physical age without a guessed simulation step");
        return beam;
    }
    private static void ReplicaCaptureFlight()
    {
        using var f=new Fixture();var beam=ReplicaHipFlight(f);var source=beam.ModCustomShot!;
        var slots=new CustomReplicaSlot[8];var flights=new CustomReplicaFlight[64];
        var payments=ReplicaPayments(f,source.Owner);uint paymentFrame=(uint)ReplicaField(payments,"_frame");
        bool quarantined=payments.IsQuarantined,retired=payments.IsRetired;uint highest=payments.HighestSequence;
        var clock=((Array)ReplicaField(f.Root,"_grantClocks")).GetValue(f.Owner.Slot)!;
        uint clockWorld=(uint)ReplicaField(clock,"_worldFrontier");
        Check(f.Root.TryState(f.Shooter,out var before) && f.Root.TryCaptureReplica(slots,flights,out var capture)
            && capture.FlightCount==1,"real physically active private flight is captured beside its public resource state");
        var flight=flights[0];var slot=slots[f.Shooter.SlotIndex];
        Check(flight.Action==source.Action && flight.Certificate==source.Certificate && flight.SpawnPosition==beam.SpawnPosition
            && flight.Position==beam.Position && flight.Velocity==beam.Velocity && flight.Direction==beam.Direction
            && flight.Direction!=flight.Certificate.Aim && flight.AgeSeconds==beam.Age && flight.RemainingSeconds==beam.Lifespan
            && flight.AgeSeconds==0 && flight.RemainingSeconds is >0 and <1
            && flight.RemainingSeconds==f.Root.Definition.Mechanics.LifetimeTicks/60f,
            "flight value copy reports exact actual post-spread physical vectors and seconds age/remaining, never reconstructing certified aim or ticks");
        Check(flight.LaunchWorld==1000 && flight.PaymentDeadline==1000+f.Root.Definition.Mechanics.LifetimeTicks+2
            && slot.HasWeapon && slot.State==before && slot.Selected.Value==10 && slot.SettledSequence==source.Sequence
            && slot.CommittedSourceFrame==1000,"snapshot preserves canonical launch/deadline and settled public state metadata");
        Frame(flight.PaymentDeadline+1);
        Check(f.Root.TryCaptureReplica(slots,flights,out var expired) && expired.FlightCount==0 && beam.Lifespan>0
            && (uint)ReplicaField(payments,"_frame")==paymentFrame && (uint)ReplicaField(clock,"_worldFrontier")==clockWorld
            && payments.IsQuarantined==quarantined && payments.IsRetired==retired && payments.HighestSequence==highest,
            "expired payment observation excludes stale physical object without advancing/quarantining payment or source clock");
        Frame(1000);
        Check(f.Root.TryCaptureReplica(slots,flights,out capture) && capture.FlightCount==1 && flights[0]==flight,
            "returning read-only query world finds unchanged original payment flight, proving expiry query did not mutate store");
        float lifespan=beam.Lifespan;beam.Lifespan=0;
        Check(f.Root.TryCaptureReplica(slots,flights,out capture) && capture.FlightCount==0,"physically expired beam is excluded even when payment remains committed flying");beam.Lifespan=lifespan;
        ulong contacts=f.Root.TerminalContacts,paymentsCount=f.Root.PositivePayments;ushort damage=NetDamage.Sequence(f.Victim.SlotIndex);
        var position=beam.Position;float age=beam.Age;int hp=f.Victim.Health;
        bool valid=true;for (int i=0;i<10000;i++) valid &= f.Root.TryCaptureReplica(slots,flights,out _);
        long allocated=GC.GetAllocatedBytesForCurrentThread();
        for (int i=0;i<100000;i++) valid &= f.Root.TryCaptureReplica(slots,flights,out _);
        allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;
        Check(valid && allocated==0,"100000 warmed full state/active-flight captures allocate zero bytes");
        Check(f.Root.TryState(f.Shooter,out var after) && after==before && f.Root.TerminalContacts==contacts
            && f.Root.PositivePayments==paymentsCount && NetDamage.Sequence(f.Victim.SlotIndex)==damage && f.Victim.Health==hp
            && beam.Position==position && beam.Age==age && beam.Lifespan==lifespan
            && (uint)ReplicaField(payments,"_frame")==paymentFrame && (uint)ReplicaField(clock,"_worldFrontier")==clockWorld
            && payments.IsQuarantined==quarantined && payments.IsRetired==retired && payments.HighestSequence==highest,
            "capture cannot advance resources/sourceclock/payments/native damage/HP or mutate physical flight fields");
        Console.WriteLine($"CUSTOM REPLICA CAPTURE ALLOCATION:100000 warmed actual-flight captures={allocated}B");
        Check(f.StepFailures==0,"read-only authority capture of physical flight has no simulation exception");
    }
    private static void ReplicaCaptureTerminal()
    {
        using (var f=new Fixture())
        {
            var beam=f.Fire();Check(f.Root.TerminalContacts==1 && f.Root.LastOutcome!=null && beam.Lifespan>0,
                "actual paid terminal contact retains positive-lifespan native impact presentation");
            var slots=new CustomReplicaSlot[8];var flights=new CustomReplicaFlight[64];
            Check(f.Root.TryCaptureReplica(slots,flights,out var capture) && capture.FlightCount==0,
                "paid canonical terminal contact is not an active flight despite retained impact presentation");
        }
        using (var f=new Fixture())
        {
            var beam=f.Fire(process:false);
            // Actual native collision transition; the test supplies a world hit
            // value, not a player contact or a custom payment/source mutation.
            beam.OnCollision(new CollisionResult { Position=beam.Position,Plane=new(0,1,0,0) },null,enhancedImpact:false);
            Check(beam.Flags.TestFlag(BeamFlags.Collided) && beam.Lifespan>0 && f.Root.TerminalContacts==0,
                "actual native world-impact transition leaves positive visual lifespan and no custom player terminal payment");
            var slots=new CustomReplicaSlot[8];var flights=new CustomReplicaFlight[64];
            Check(f.Root.TryCaptureReplica(slots,flights,out var capture) && capture.FlightCount==0,
                "collided world-impact visual must not be captured as an active flying projectile");
        }
    }
    private static void ReplicaCaptureRespawn()
    {
        using var f=new Fixture();var beam=f.Fire(process:false);var old=beam.ModCustomShot!.Owner;
        f.KillShooterWithNativeProjectile();Frame(1001);
        f.Shooter.Spawn(f.Shooter.Position,Vector3.UnitZ,Vector3.UnitY,f.Shooter.NodeRef,respawn:true);Fixture.Clean(f.Shooter,200);f.Root.Advance();
        Check(f.Root.BindLife(f.Shooter,1001,true),"genuine next native life binds its separate unequipped inventory");
        var slots=new CustomReplicaSlot[8];var flights=new CustomReplicaFlight[64];
        Check(f.Root.TryCaptureReplica(slots,flights,out var capture) && capture.FlightCount==1
            && slots[old.Slot].HasWeapon && slots[old.Slot].State.Owner.Life!=old.Life
            && slots[old.Slot].State.Magazine==4 && flights[0].Action.Owner==old
            && flights[0].Action.Owner.Life!=slots[old.Slot].Life && flights[0].Certificate==beam.ModCustomShot.Certificate,
            "new-life public state coexists with actual retained old-life flight without rebasing identity or debiting fresh inventory");
        f.Shooter.ModSetSpectating(true);f.Root.Advance();
        Check(f.Root.TryCaptureReplica(slots,flights,out capture) && !slots[old.Slot].HasWeapon
            && slots[old.Slot].Spectating && capture.FlightCount==0,
            "authoritative spectator retirement exposes no current weapon or obsolete private flights");
        Check(f.StepFailures==0,"replica capture across real native respawn/spectator lifecycle has no simulation exception");
    }
    private static void ReplicaCaptureNativeScope()
    {
        using var f=new Fixture();
        var slots=new CustomReplicaSlot[8];var flights=new CustomReplicaFlight[64];
        var sentinel=new CustomReplicaSlot(7,true,99,77,true,false,default,123,456,default);Array.Fill(slots,sentinel);
        var native=new IntentPacket { MatchId=f.Owner.MatchId,AuthorityEpoch=f.Owner.AuthorityEpoch,SlotGeneration=f.Owner.Generation,
            LifeId=f.Owner.Life,Frame=1000,AckFrame=995,Position=f.Shooter.Position,Aim=f.Aim(),Buttons=IntentButtons.InPlayState,
            HasState=true,HasFireEvents=true,WeaponSelect=(byte)BeamType.PowerBeam };
        Check(NetSession.AcceptSlotIntent(f.Shooter.SlotIndex,native),"scope fixture admits genuine current native input with retained historical ACK");
        var equip=new EquipInfo(f.Scene.WeaponRules[(int)BeamType.PowerBeam],f.Shooter.EquipInfo.Beams) { InfiniteAmmo=true };
        try
        {
            NetUnlagged.BeginShot(f.Shooter,f.Shooter.Position+Vector3.UnitY*.4f,f.Aim(),equip);
            Check((bool)typeof(NetUnlagged).GetField("_inProgress",StaticHidden)!.GetValue(null)!
                && (bool)typeof(NetUnlagged).GetField("_reconciled",StaticHidden)!.GetValue(null)!,
                "actual native BeginShot enters historical reconciliation with real native equip/pool");
            NetUnlagged.ResetSlot(7);
            Check(!NetUnlagged.CollisionCacheActive
                && (bool)typeof(NetUnlagged).GetField("_inProgress",StaticHidden)!.GetValue(null)!,
                "actual ResetSlot clears collision cache while native shot scope remains open");
            Check(!f.Root.TryCaptureReplica(slots,flights,out var capture) && capture==default
                && slots.All(x=>x==sentinel) && flights.All(x=>x==default),
                "open native reconciliation scope rejects capture despite cleared collision cache without partial output");
        }
        finally { NetUnlagged.AbortShot(); }
        Check(f.Root.TryCaptureReplica(slots,flights,out _),"native scope abort restores safe read-only capture");
        Check(f.StepFailures==0,"capture gating around actual native reconciliation has no simulation exception");
    }

}
