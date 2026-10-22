using System.Reflection;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Input;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

// Asset-backed offline public availability. Synthetic device samples use the
// real local input and simulation hooks. Positions, target HP and invulnerability
// are explicit collision-fixture setup; no launch certificate/payment is injected.
internal static class CustomOfflineAvailabilitySceneChecks
{
    private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    private static readonly Action<KeyboardState,Keys,bool> Key=typeof(KeyboardState)
        .GetMethod("SetKeyState",Hidden|BindingFlags.Public)!.CreateDelegate<Action<KeyboardState,Keys,bool>>();
    internal static int Run(string data)
    {
        Scene? scene=null;bool force=MapAudit.ForceEveryone,stylus=PointerInput.StylusMode;
        string cwd=Directory.GetCurrentDirectory();int checks=0;
        var recording=typeof(DemoRecorder).GetField("_pending",BindingFlags.Static|BindingFlags.NonPublic)!;
        bool pending=(bool)recording.GetValue(null)!;
        void Check(bool value,string name)
        {if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
        try
        {
            Directory.SetCurrentDirectory(Path.GetFullPath(data));Paths.UpdatePaths();Paths.ChooseMphPath();
            Headless.Enter();MapAudit.ForceEveryone=true;PointerInput.StylusMode=false;PointerDevice.Reset();
            GamepadManager.ClearAll();GamepadContexts.Focused=true;GamepadContexts.MenuVisible=false;
            GamepadContexts.Capturing=false;GamepadContexts.TextEntryActive=false;
            var keyboard=SyntheticInput.CreateKeyboard();
            scene=new(new Vector2i(256,192),keyboard,SyntheticInput.CreateMouse(),_=>{},()=>{});
            scene.AddPlayer(Hunter.Samus);scene.AddPlayer(Hunter.Weavel);
            scene.Players.PlayerCount=2;scene.Players.MainPlayerIndex=0;
            foreach(var player in scene.Players.Items)player.IsBot=false;
            scene.AddRoom("MP3 PROVING GROUND",GameMode.Battle,playerCount:2,nodeLayerMask:0);scene.OnLoad();
            var input=typeof(Scene).GetField("_inputMode",Hidden)!;input.SetValue(scene,Enum.Parse(input.FieldType,"All"));
            scene.GameState.MatchTime=-1;scene.GameState.PointGoal=10000;
            var human=scene.Players.Items[0];var target=scene.Players.Items[1];
            foreach(var button in human.Controls.All){button.Type=ButtonType.Key;button.Key=Keys.Unknown;}
            human.Controls.Shoot.Key=Keys.Enter;human.Controls.Zoom.Key=Keys.Z;human.Controls.CustomWeapon.Key=Keys.C;
            human.Controls.MouseAim=true;human.Controls.KeyboardAim=false;
            Check(!NetSession.Active&&scene.OfflineWeapons==null&&scene.CustomCombat==null&&scene.CustomWeapons==null,
                "loaded native-only Battle scene has no custom runtime, authority or registry");
            bool refused=false;try{scene.EnableOfflineRifleAvailability(false);}catch(InvalidOperationException){refused=true;}
            Check(refused&&scene.OfflineWeapons==null&&scene.CustomWeapons==null,"false availability opt-in cannot allocate a runtime or registry");
            recording.SetValue(null,true);refused=false;
            try{scene.EnableOfflineRifleAvailability(true);}catch(InvalidOperationException){refused=true;}
            finally{recording.SetValue(null,pending);}
            Check(refused&&scene.OfflineWeapons==null&&scene.CustomWeapons==null,
                "active recording refuses actual scene activation before allocation");
            Console.WriteLine($"AVAILABILITY INITIAL health={human.Health} load={human.LoadFlags} frame={scene.FrameCount}");
            Check(human.Health==0&&!human.LoadFlags.TestFlag(LoadFlags.Spawned),"public availability attaches before the real human spawn");
            var runtime=scene.EnableOfflineRifleAvailability(true);
            Check(ReferenceEquals(scene.OfflineWeapons,runtime)&&runtime.IsEligible&&runtime.State(human)==null
                &&runtime.State(target)==null&&runtime.AcceptedShots==0,
                "availability captures local human without granting a dead actor or another slot");
            void Step(int count=1)
            {for(int i=0;i<count;i++){NetTestScript.Rest(target,wantBiped:true);scene.OnSimulationFrame();}}
            void Release(){Key(keyboard,Keys.Enter,false);Key(keyboard,Keys.Z,false);Key(keyboard,Keys.C,false);}
            Key(keyboard,Keys.Enter,true);
            for(int i=0;i<600&&!human.LoadFlags.TestFlag(LoadFlags.Spawned);i++)Step();
            Release();scene.CameraSequences.Current?.End();Step(240);
            Check(human.ModIsInPlay&&human.Health>0&&target.Health>0&&!human.IsBot,
                "real offline fixed steps spawn the captured local human and native target");
            var ledger=runtime.Pickups??throw new Exception("public runtime lacks authored pickup ledger");
            Check(runtime.State(human)==null&&runtime.InputProjection(human)==null&&runtime.State(target)==null
                &&ledger.Revision==0&&ledger.Layout.Count==1&&runtime.AcceptedShots==0,
                "healthy public spawn is empty and native-selected until actual world contact");
            var center=ledger.Layout.Placements[0].Position;
            Check(ledger.Layout.Placements[0].RespawnTicks==1800&&center==new Vector3(5044/4096f,2662/4096f,-82087/4096f),
                "Proving Ground uses final authored fixed4096 center and1800tick respawn");
            human.Controls.MoveUp.Key=Keys.W;human.Controls.MoveDown.Key=Keys.S;
            human.Controls.CustomReload.Key=Keys.R;human.Controls.PowerBeam.Key=Keys.P;
            human.Controls.MouseAim=false;human.Controls.KeyboardAim=false;
            void ClearKeys(){foreach(var key in new[]{Keys.Enter,Keys.Z,Keys.C,Keys.W,Keys.S,Keys.R,Keys.P})Key(keyboard,key,false);}
            void Tap(params Keys[] keys){ClearKeys();Step();foreach(var key in keys)Key(keyboard,key,true);Step();ClearKeys();}
            int NativeFlights(){int count=0;foreach(var beam in scene.GetBeamProjectileEntities())if(beam.Owner==human&&beam.ModCustomShot==null&&beam.Lifespan>0)count++;return count;}
            string NativeArrays()=>string.Join(',',Enumerable.Range(0,9).Select(i=>human.AvailableWeapons[i]))+"|"
                +string.Join(',',(BeamType[])typeof(PlayerEntity).GetField("_weaponSlots",Hidden)!.GetValue(human)!);
            Tap(Keys.C);Step();
            Check(runtime.State(human)==null&&!scene.CustomInputSuppressesNative(human)&&ledger.Revision==0,
                "C cannot equip unowned public M7 or suppress native firing before pickup");
            Tap(Keys.Enter);
            Check(NativeFlights()>0&&runtime.AcceptedShots==0,"actual native Power Beam still fires before first pickup");
            ClearKeys();Step(30);
            var spawn=new Vector3(2.291504f,.5f,-22.847168f);
            bool naturalSpawn=Vector3.Distance(human.Position,spawn)<.25f;
            Console.WriteLine($"PICKUP SPAWN naturalSpawn4={naturalSpawn} actual={human.Position} authored={spawn}");
            if(!naturalSpawn)human.Reposition(spawn,Vector3.UnitZ,human.NodeRef); // Declared native-body fixture fallback.
            var walk=(center-human.Position).WithY(0).Normalized();
            human.Reposition(human.Position,walk,human.NodeRef);human.ModSetAim(walk);human.Speed=Vector3.Zero;
            var pools=human.ModAmmo;var weapon=human.CurrentWeapon;string nativeArrays=NativeArrays();
            Vector3 walkStart=human.Position;int health=human.Health;Key(keyboard,Keys.W,true);
            int firstWalk=0;for(;firstWalk<120&&ledger.Revision==0;firstWalk++)Step();
            ClearKeys();
            Console.WriteLine($"PICKUP FIRST steps={firstWalk} body={walkStart}->{human.Position} center={center} frame={scene.FrameCount} item={ledger.Revision}");
            Check(firstWalk>0&&firstWalk<120&&ledger.Revision==1&&ledger.TryState(0,out var consumed)
                &&consumed.Key.Incarnation==2&&!consumed.Available&&ledger.PendingOutcomes==0
                &&Vector3.Distance(walkStart,human.Position)>1&&human.Health==health,
                "actual W input/native walking/item phase consumes first authored incarnation exactly once");
            uint firstContact=ledger.AuthorityFrame;
            Check(runtime.InputProjection(human) is {Selected:false},"pickup contact does not select until next fresh input phase");
            Step();var state=runtime.State(human)??throw new Exception("collected pickup did not become owned");var first=state.Snapshot();
            Check(first is {Magazine:4,Reserve:16,Phase:CustomWeaponPhase.Ready,Optic:0}
                &&runtime.InputProjection(human) is {Selected:true}&&first.Tick>=unchecked(firstContact+1)
                &&runtime.State(target)==null&&runtime.AcceptedShots==0,
                "first contact funds exact4/16 and automatically selects once on next eligible biped input");
            Step(30);
            Check(ledger.Revision==1&&ReferenceEquals(runtime.State(human),state)&&state.Snapshot().Magazine==4
                &&human.ModAmmo==pools&&human.CurrentWeapon==weapon&&NativeArrays()==nativeArrays,
                "standing overlap does not duplicate pickup, mutate native arrays/ammo or emit");
            // Walk back along the measured stock path; wait via actual simulation ticks only.
            Key(keyboard,Keys.S,true);
            for(int n=0;n<90&&(human.Position-center).WithY(0).Length<3;n++)Step();
            ClearKeys();Step(12);
            Check((human.Position-center).WithY(0).Length>2&&ledger.Revision==1,"actual reverse walking leaves pickup contact before respawn");
            ledger.TryState(0,out var waiting);uint ready=waiting.ReadyAt;
            while((uint)scene.FrameCount<unchecked(ready-15))Step();
            Check(ledger.Revision==1&&ledger.TryState(0,out waiting)&&!waiting.Available,"natural fixed steps retain first incarnation consumption through respawn wait");
            Tap(Keys.Enter);var fired=state.Snapshot();Tap(Keys.R);var reloading=state.Snapshot();
            Check(reloading.Phase==CustomWeaponPhase.Reloading&&reloading.Magazine==3&&reloading.Reserve==16
                &&runtime.AcceptedShots==1,"fresh actual Fire and Reload start while approaching respawning pickup");
            human.ModSetAim(walk);human.Reposition(human.Position,walk,human.NodeRef);human.Speed=Vector3.Zero;
            Key(keyboard,Keys.W,true);int repeatWalk=0;for(;repeatWalk<120&&ledger.Revision==1;repeatWalk++)Step();ClearKeys();
            Check(repeatWalk>0&&repeatWalk<120&&ledger.Revision==2&&ledger.AuthorityFrame>=ready,
                "actual item phase recollects only after full1800tick simulation respawn");
            Step();var repeat=state.Snapshot();
            Check(repeat.Magazine==3&&repeat.Reserve==32&&repeat.Phase==CustomWeaponPhase.Reloading
                &&repeat.FireReadyTick==fired.FireReadyTick&&repeat.ReloadCompleteTick==reloading.ReloadCompleteTick
                &&runtime.AcceptedShots==1&&human.ModAmmo==pools&&NativeArrays()==nativeArrays,
                "repeat pickup adds only16reserve next tick without resetting magazine/reload/cadence or native storage");
            Step(20);Check(ledger.Revision==2,"standing over consumed second incarnation cannot add more reserve");
            while((uint)scene.FrameCount<=reloading.ReloadCompleteTick)Step();
            Check(state.Snapshot().Magazine==4&&state.Snapshot().Reserve==31,"ordinary reload deadline transfers exactly one round after repeat funding");
            Tap(Keys.P);Step();Check(runtime.InputProjection(human) is {Selected:false},"explicit native Power Beam selection returns from acquired M7");
            Tap(Keys.C);Step();Check(runtime.InputProjection(human) is {Selected:true},"C explicitly reselects already acquired M7");
            target.IsBot=true;Check(CustomWeaponMatchPolicy.TryAdmit(scene,out _)&&runtime.State(target)==null,
                "native offline bot target stays eligible without custom inventory");
            // Declared unobstructed collision fixture, separate from stock walking qualification.
            human.ModPlaceAt(new Vector3(0,10000,0));human.Speed=Vector3.Zero;
            var direction=human.ModGunVector.Normalized();
            target.ModPlaceAt(human.Position+new Vector3(0,.4f,0)+direction*3-new Vector3(0,.2f,0));target.Speed=Vector3.Zero;
            target.Health=200;
            typeof(PlayerEntity).GetField("_spawnInvulnTimer",Hidden)!.SetValue(target,(ushort)0);
            typeof(PlayerEntity).GetField("_damageInvulnTimer",Hidden)!.SetValue(target,(ushort)0);
            Check(!BeamProjectileEntity.TryBeamObstacle(human.Position+new Vector3(0,.4f,0),target.Position+new Vector3(0,.2f,0),scene,out _,out _),
                "canonical bot damage fixture has measured unobstructed native room ray");
            ulong accepted=runtime.AcceptedShots;Tap(Keys.Z,Keys.Enter);ClearKeys();
            BeamProjectileEntity? shot=null;
            foreach(var beam in scene.GetBeamProjectileEntities())if(beam.Owner==human&&beam.ModCustomShot!=null&&beam.Lifespan>0)shot=beam;
            Check(runtime.AcceptedShots==accepted+1&&state.Snapshot().Magazine==3&&shot?.ModCustomShot?.Owner==state.Owner,
                "actual acquired-M7 keyboard scope/Fire pays one round with exact human-life provenance");
            shot!.Process();shot.Process();
            Check(target.IsBot&&target.Health==135&&runtime.LastImpact is {Damage:65,Headshot:false}
                &&runtime.State(target)==null&&human.ModAmmo==pools,
                "real acquired custom collision pays native bot65 through canonical damage without bot inventory");
            target.IsBot=false;ClearKeys();Step(80);
            target.ModPlaceAt(human.Position+new Vector3(0,.4f,0)+direction*9-new Vector3(0,.3f,0));target.Speed=Vector3.Zero;target.Health=200;
            typeof(PlayerEntity).GetField("_damageInvulnTimer",Hidden)!.SetValue(target,(ushort)0);
            Tap(Keys.Enter);BeamProjectileEntity? oldFlight=null;
            foreach(var beam in scene.GetBeamProjectileEntities())if(beam.Owner==human&&beam.ModCustomShot is {} source
                &&source.Sequence==state.Snapshot().LastAcceptedShotId&&beam.Lifespan>0)oldFlight=beam;
            Check(oldFlight!=null,"genuine pre-death custom flight exists before native death");
            var oldOwner=state.Owner;var oldSource=oldFlight!.ModCustomShot!;accepted=runtime.AcceptedShots;
            human.TakeDamage(10000,DamageFlags.Death,null,null);ClearKeys();Step(3);
            Check(human.Health==0&&state.Snapshot().Phase==CustomWeaponPhase.Invalid&&runtime.State(human)==null
                &&target.Health<200&&runtime.LastImpact?.Source==oldSource.Identity,
                "public death invalidates inventory while pre-death emitted flight still resolves canonical damage");
            human.Spawn(spawn,walk,Vector3.UnitY,human.NodeRef,respawn:true);scene.CameraSequences.Current?.End();Step(180);
            Check(runtime.State(human)==null&&runtime.InputProjection(human)==null&&runtime.AcceptedShots==accepted
                &&!scene.CustomInputSuppressesNative(human),"real respawn is empty and cannot retain stale custom suppression");
            Tap(Keys.P);Step();Tap(Keys.Enter);
            Check(NativeFlights()>0&&runtime.State(human)==null&&runtime.AcceptedShots==accepted,
                "actual native Power Beam fires after empty respawn before reacquisition");
            ClearKeys();Step(30);ledger.TryState(0,out var respawnItem);
            while((uint)scene.FrameCount<respawnItem.ReadyAt)Step();
            human.Reposition(spawn,walk,human.NodeRef);human.ModSetAim(walk);human.Speed=Vector3.Zero;
            Key(keyboard,Keys.W,true);int reacquire=0;for(;reacquire<120&&ledger.Revision==2;reacquire++)Step();ClearKeys();
            Check(reacquire>0&&reacquire<120&&ledger.Revision==3,"new life reacquires a later world incarnation through real walking contact");
            Key(keyboard,Keys.P,true);Step();ClearKeys();Step();var respawned=runtime.State(human)!;
            Check(respawned.Owner.Life==oldOwner.Life+1&&respawned.Owner.Generation==oldOwner.Generation
                &&respawned.Snapshot() is {Magazine:4,Reserve:16,Phase:CustomWeaponPhase.Unequipped}
                &&runtime.InputProjection(human) is {Selected:false}&&state.Snapshot().Phase==CustomWeaponPhase.Invalid,
                "reacquisition belongs to fresh empty life and explicit native input wins simultaneous auto-selection");
            Step(30);Check(runtime.InputProjection(human) is {Selected:false},"blocked pickup auto-selection never retries later");
            Tap(Keys.C);Step();Check(runtime.InputProjection(human) is {Selected:true},"explicit C can equip reacquired new-life rifle");
            Check(CustomReplayBoundary.IsBlocked(scene),"public offline availability marks the actual scene unsupported by native replay");
            scene.DoCleanup();
            Check(!runtime.IsEligible&&runtime.State(human)==null&&scene.OfflineWeapons==null&&scene.CustomWeapons==null,
                "scene cleanup retires escaped public runtime and registry");
            Console.WriteLine($"PASS {checks} actual offline world-pickup scene checks; authoredPG walking/contact, natural1800tick respawn, synthetic keyboard and declared collision/respawn geometry; no all27collection/UI/Android/online qualification");
            return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
        finally
        {
            recording.SetValue(null,pending);NetSession.Stop();scene?.DoCleanup();MapAudit.ForceEveryone=force;
            PointerInput.StylusMode=stylus;PointerDevice.Reset();GamepadManager.ClearAll();Directory.SetCurrentDirectory(cwd);
        }
    }
}
