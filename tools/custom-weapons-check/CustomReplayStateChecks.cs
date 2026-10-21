using System.Security.Cryptography;
using System.Text.Json;
using MphRead;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.CustomWeapons.Packages;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

// Detached supplied facts only: no Scene, grants, damage or playback admission.
internal static class CustomReplayStateChecks
{
    private sealed class Fixture:IDisposable
    {
        internal readonly WeaponPackage Package=WeaponPackage.Parse(WeaponPackageReference.BuildRiftlineM7());
        internal readonly WeaponRegistry Registry;
        internal readonly ReplayCustomWeaponContext Context;
        internal readonly CustomReplicaSlot[] Slots=new CustomReplicaSlot[8];
        internal readonly WorldBootstrapIdentity World;
        internal Fixture()
        {
            var definition=Package.Definition;Registry=new(true,new[]{definition});var manifest=new CustomWeaponManifest(new[]{definition});
            var layout=new CustomPickupLayout(MapContentIdentity.BuiltIn("MP3 PROVING GROUND"),manifest,Registry,StockRiftlinePickups.ForRoom("MP3 PROVING GROUND",definition));World=new(new(7,0x123456789abcdef0,3),2,5,990,1,1);
            Context=new(new(World,42,layout.Digest,new(new[]{Package})),definition,layout);
            for(byte i=0;i<8;i++)Slots[i]=new(i,false,0,0,false,false,default,0,0,default);
            var owner=new WeaponOwnerContext(7,World.Start.AuthorityEpoch,1,5,10);var runtime=new WeaponRuntimeState(definition,owner,1000);runtime.SetEquipped(1000,true);runtime.TrySetOptic(1000,1,1);Slots[1]=new(1,true,5,10,false,true,new(10),0,1000,runtime.Snapshot());
        }
        internal CustomReplicaLaunch Launch(uint sequence=1)
        {
            var action=new CustomWeaponAction(World,Context.Bootstrap.Packages.MechanicsDigest,1,10,new(10),sequence,1000,999,0,CustomWeaponActionKind.Fire,1,new(0,.5f,0),Vector3.UnitZ);
            return new(new(action,1000,999,0,Vector3.Zero,Vector3.UnitZ,true,false,false),1001,new(0,.4f,0),Vector3.UnitZ);
        }
        internal CustomReplicaFlight Flight(uint sequence=1)=>new(Launch(sequence).Identity,Launch(sequence).Source,1001,1001+(uint)Context.Definition.Mechanics.LifetimeTicks+2,Vector3.Zero,new(2,3,4),Vector3.UnitZ*(Context.Definition.Mechanics.ProjectileSpeed4096/4096f),Vector3.UnitZ,1/60f,(Context.Definition.Mechanics.LifetimeTicks-1)/60f);
        internal CustomContactOutcome Contact(uint sequence=1)=>new(new(new(Launch(sequence).Identity,new(2,6,11),1,CustomImpactChannel.Body),Vector3.UnitZ,false),1001,new(200,135,0,0,9));
        internal ReplayCustomWeaponState State(bool baselineFlight=false,bool pickups=true)
        {
            var state=new ReplayCustomWeaponState(Context);state.Accept(ReplayCustomWeaponFact.Bootstrap(Context),10);var flights=baselineFlight?new[]{Flight()}:Array.Empty<CustomReplicaFlight>();state.Accept(ReplayCustomWeaponFact.Baseline(Context,2,new(7,World.Start.AuthorityEpoch,Context.Bootstrap.Packages.MechanicsDigest,1001,8,flights.Length),Slots,flights),11);
            if(pickups)for(int i=0;i<Context.Layout.Count;i++)state.Accept(Pickup(state.Sequence+1,i),12);return state;
        }
        internal ReplayCustomWeaponFact Pickup(ulong sequence,int index,ulong incarnation=1,ulong revision=0,uint frame=1001,uint ready=990,bool available=true)
            =>ReplayCustomWeaponFact.Pickup(Context,sequence,frame,index,new(new(Context.PickupWorld,Context.Layout.Placements[index].SpawnId,incarnation),ready,available,revision));
        public void Dispose()=>Registry.Dispose();
    }
    internal static int Run(string? output=null)
    {
        int checks=0;string root=Path.GetFullPath(output??Path.Combine(Path.GetTempPath(),"cw12f-state-"+Guid.NewGuid().ToString("N")));
        void Check(bool ok,string label){if(!ok)throw new InvalidOperationException(label);checks++;}
        void Reject(Action action,string label){try{action();}catch(Exception error)when(error is InvalidDataException or ArgumentException){Check(true,label);return;}throw new InvalidOperationException("Expected rejection: "+label);}
        CustomReplicaFlight[] Flights(ReplayCustomWeaponState state){var values=new CustomReplicaFlight[256];return values[..state.CopyFlights(values)];}
        void Equal(ReplayCustomWeaponState a,ReplayCustomWeaponState b,string label)
        {Check(a.World==b.World&&a.Sequence==b.Sequence&&a.RecordingFrame==b.RecordingFrame&&a.KnownPickups==b.KnownPickups&&a.CompleteForRestore==b.CompleteForRestore&&Flights(a).SequenceEqual(Flights(b)),label+" clocks/flights");for(int i=0;i<8;i++){a.TrySlot(i,out var x);b.TrySlot(i,out var y);Check(x==y,label+" slot "+i);}for(int i=0;i<a.Context.Layout.Count;i++){a.TryPickup(i,out var x);b.TryPickup(i,out var y);Check(x==y,label+" pickup "+i);}}
        try
        {
            Directory.CreateDirectory(root);using var f=new Fixture();Check(ReplayCustomWeaponState.FlightCapacity==256&&ReplayCustomWeaponState.ActionCapacity==1024&&ReplayCustomWeaponState.SeenCapacity==1024,"fixed passive state budgets");
            var cold=new ReplayCustomWeaponState(f.Context);Check(!cold.HasBaseline&&!cold.CompleteForRestore&&!cold.TrySlot(1,out _),"fresh passive context carries no state");Reject(()=>cold.CaptureRestorePoint(),"unseeded restore refused");
            var incomplete=f.State(pickups:false);Check(incomplete.HasBaseline&&!incomplete.CompleteForRestore,"baseline alone is not a complete pickup restore");Reject(()=>incomplete.CaptureRestorePoint(),"missing pickup table refuses restore");
            var linear=f.State(baselineFlight:true);Check(linear.CompleteForRestore&&linear.KnownPickups==f.Context.Layout.Count,"all authored pickups complete restore scope");var initial=Flights(linear).Single();linear.Advance(1004,20);var moved=Flights(linear).Single();Check(moved.Position==initial.Position+initial.Velocity*3&&moved.AgeSeconds==initial.AgeSeconds+3/60f,"baseline anchor uses velocity per fixed tick, not per second");
            var restorePoint=linear.CaptureRestorePoint();using var fresh=new Fixture();var restored=restorePoint.Restore(fresh.Context);Check(!ReferenceEquals(restored,linear)&&ReferenceEquals(restored.Context,fresh.Context),"restore creates independent owner with fresh equal context");Equal(linear,restored,"initial restore equality");
            linear.Advance(1005,21);restored.Advance(1005,21);Equal(linear,restored,"advance suffix exact anchor equality");Check(Flights(restored).Single().Position==initial.Position+initial.Velocity*4,"restore preserves original anchor without accumulated rounding");
            var immutable=restorePoint.Restore(fresh.Context);Check(immutable.World==1004&&Flights(immutable).Single().Position==moved.Position,"restore point snapshot remains immutable after owner advances");
            var terminal=ReplayCustomWeaponFact.Contact(f.Context,linear.Sequence+1,f.World,f.Contact());linear.Accept(terminal,22);restored.Accept(terminal,22);Equal(linear,restored,"contact suffix equality");Check(Flights(linear).Length==0&&linear.TryContact(f.Launch().Identity,out var contact,out uint frame)&&contact==f.Contact()&&frame==22,"terminal contact removes flight and retains first recording frame");
            var baselineRetry=f.State(baselineFlight:true);var originalAnchor=Flights(baselineRetry).Single();
            var baselineLaunch=f.Launch() with{Origin=f.Flight().SpawnPosition};baselineRetry.Accept(ReplayCustomWeaponFact.Launch(f.Context,baselineRetry.Sequence+1,baselineLaunch),20);
            Check(Flights(baselineRetry).Single()==originalAnchor,"consistent delayed baseline launch preserves original position/age anchor");baselineRetry.Advance(1004,21);Check(Flights(baselineRetry).Single().Position==originalAnchor.Position+originalAnchor.Velocity*3,"consistent launch cannot reset baseline motion anchor");
            var contradictory=f.State(baselineFlight:true);Reject(()=>contradictory.Accept(ReplayCustomWeaponFact.Launch(f.Context,contradictory.Sequence+1,baselineLaunch with{Direction=Vector3.UnitX}),20),"same identity/certificate/world contradictory launch direction invalidates baseline flight");Check(contradictory.Invalidated,"contradictory baseline launch closes passive state");
            var wrongContactSource=f.State();wrongContactSource.Accept(ReplayCustomWeaponFact.Launch(f.Context,wrongContactSource.Sequence+1,f.Launch()),20);var alteredSource=f.World with{Revision=f.World.Revision+1};
            Reject(()=>wrongContactSource.Accept(ReplayCustomWeaponFact.Contact(f.Context,wrongContactSource.Sequence+1,alteredSource,f.Contact()),21),"known launch contact cannot replace exact source bootstrap revision");Check(wrongContactSource.Invalidated,"launch/contact source contradiction invalidates");
            var contactFirst=f.State();contactFirst.Accept(ReplayCustomWeaponFact.Contact(f.Context,contactFirst.Sequence+1,f.World,f.Contact()),20);contactFirst.Accept(ReplayCustomWeaponFact.Launch(f.Context,contactFirst.Sequence+1,f.Launch()),21);Check(Flights(contactFirst).Length==0,"contact-before-launch never resurrects flight");
            var duplicate=f.State();var launchFact=ReplayCustomWeaponFact.Launch(f.Context,duplicate.Sequence+1,f.Launch());Check(duplicate.Accept(launchFact,20)&&!duplicate.Accept(launchFact,20),"exact same-sequence retry inert");duplicate.Accept(ReplayCustomWeaponFact.Launch(f.Context,duplicate.Sequence+1,f.Launch()),21);Check(Flights(duplicate).Length==1,"same launch at later file sequence remains one flight");
            var conflict=f.State();conflict.Accept(ReplayCustomWeaponFact.Launch(f.Context,conflict.Sequence+1,f.Launch()),20);Reject(()=>conflict.Accept(ReplayCustomWeaponFact.Launch(f.Context,conflict.Sequence,f.Launch(2)),21),"same-sequence conflicting payload closes state");Check(conflict.Invalidated&&!conflict.TrySlot(1,out _),"conflict makes typed reads unavailable");Reject(()=>conflict.CopyFlights(new CustomReplicaFlight[256]),"invalid state refuses flight reads");Reject(()=>conflict.CaptureRestorePoint(),"invalid state cannot be checkpointed");
            var gap=f.State();Reject(()=>gap.Accept(ReplayCustomWeaponFact.Launch(f.Context,gap.Sequence+2,f.Launch()),20),"sequence gap fails closed");Check(gap.Invalidated,"gap permanently invalidates");
            var generation=f.State();generation.Accept(ReplayCustomWeaponFact.Launch(f.Context,generation.Sequence+1,f.Launch()),20);var rows=(CustomReplicaSlot[])f.Slots.Clone();rows[1]=new(1,true,6,0,true,false,default,0,0,default);generation.Accept(ReplayCustomWeaponFact.State(f.Context,generation.Sequence+1,1002,rows),21);Check(Flights(generation).Length==0,"new slot generation retires old projectile");generation.Accept(ReplayCustomWeaponFact.Launch(f.Context,generation.Sequence+1,f.Launch(2)),22);Check(Flights(generation).Length==0,"old-generation delayed launch cannot resurrect occupant projectile");
            var life=f.State();life.Accept(ReplayCustomWeaponFact.Launch(f.Context,life.Sequence+1,f.Launch()),20);rows=(CustomReplicaSlot[])f.Slots.Clone();rows[1]=rows[1] with{Life=11,State=rows[1].State with{Owner=new WeaponOwnerContext(rows[1].State.Owner.MatchId,rows[1].State.Owner.AuthorityEpoch,rows[1].State.Owner.Slot,rows[1].State.Owner.Generation,11)}};life.Accept(ReplayCustomWeaponFact.State(f.Context,life.Sequence+1,1002,rows),21);Check(Flights(life).Length==1,"same-generation prior-life projectile remains valid detached posthumous flight");
            var pickups=f.State();pickups.Accept(f.Pickup(pickups.Sequence+1,0,2,1,1002,1100,false),20);Check(pickups.TryPickup(0,out var consumed)&&!consumed.Available&&consumed.Key.Incarnation==2,"new pickup incarnation/revision accepted");pickups.Accept(f.Pickup(pickups.Sequence+1,0,2,1,1100,1100,true),21);Check(pickups.TryPickup(0,out var ready)&&ready.Available,"same incarnation becomes available at ready frame");Reject(()=>pickups.Accept(f.Pickup(pickups.Sequence+1,0,1,0,1101),22),"pickup incarnation/revision rollback fails closed");
            var stateRegression=f.State();stateRegression.Accept(ReplayCustomWeaponFact.State(f.Context,stateRegression.Sequence+1,1005,f.Slots),20);Reject(()=>stateRegression.Accept(ReplayCustomWeaponFact.State(f.Context,stateRegression.Sequence+1,1004,f.Slots),21),"canonical table world rollback rejected");
            var clock=f.State();Reject(()=>clock.Accept(ReplayCustomWeaponFact.State(f.Context,clock.Sequence+1,1002,f.Slots),1),"backward recording-frame admission closes state");Check(clock.Invalidated,"recording regression invalidates");Reject(()=>linear.CopyFlights(new CustomReplicaFlight[255]),"flight copy requires full bounded buffer");
            var expires=f.State(baselineFlight:true);expires.Advance(1001+(uint)f.Context.Definition.Mechanics.LifetimeTicks,30);Check(Flights(expires).Length==0,"baseline flight expires within bounded mechanics lifetime");
            var fullFlights=f.State();for(uint i=1;i<=256;i++)fullFlights.Accept(ReplayCustomWeaponFact.Launch(f.Context,fullFlights.Sequence+1,f.Launch(i)),20);Check(Flights(fullFlights).Length==256,"exact256 passive flight budget accepted");Reject(()=>fullFlights.Accept(ReplayCustomWeaponFact.Launch(f.Context,fullFlights.Sequence+1,f.Launch(257)),20),"257th concurrent flight fails closed");
            var actions=f.State();for(uint i=1;i<=1024;i++)actions.Accept(ReplayCustomWeaponFact.Contact(f.Context,actions.Sequence+1,f.World,f.Contact(i)),20);Check(!actions.Invalidated,"exact1024 retained terminal action budget accepted");Reject(()=>actions.Accept(ReplayCustomWeaponFact.Contact(f.Context,actions.Sequence+1,f.World,f.Contact(1025)),20),"unexpired action history cannot exceed1024");
            var history=f.State();var old=ReplayCustomWeaponFact.State(f.Context,history.Sequence+1,1001,f.Slots);history.Accept(old,20);for(int i=0;i<1024;i++)history.Accept(ReplayCustomWeaponFact.State(f.Context,history.Sequence+1,1001,f.Slots),20);Reject(()=>history.Accept(old,20),"retry older than bounded seen window fails closed");
            var otherBootstrap=new ReplayCustomWeaponBootstrap(f.World,43,f.Context.Layout.Digest,f.Context.Bootstrap.Packages);var otherContext=new ReplayCustomWeaponContext(otherBootstrap,f.Context.Definition,f.Context.Layout);Reject(()=>restorePoint.Restore(otherContext),"restore into different exact context refused");
            File.WriteAllText(Path.Combine(root,"results.json"),JsonSerializer.Serialize(new{checks,scope="Detached constructed state/restore/clock/history tests. No custom record/reopen/seek scene, damage authority, GL or playback enable claim."}));Console.WriteLine($"PASS {checks} passive custom replay state/restore checks");return 0;
        }
        catch(Exception error){Console.Error.WriteLine("CUSTOM REPLAY STATE FAIL "+error);return 1;}
    }
}
