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
            void Press(params Keys[] keys){Release();Step();foreach(var key in keys)Key(keyboard,key,true);Step();}
            Key(keyboard,Keys.Enter,true);
            for(int i=0;i<600&&!human.LoadFlags.TestFlag(LoadFlags.Spawned);i++)Step();
            Release();scene.CameraSequences.Current?.End();Step(240);
            Check(human.ModIsInPlay&&human.Health>0&&target.Health>0&&!human.IsBot,
                "real offline fixed steps spawn the captured local human and native target");
            var state=runtime.State(human)!;var first=state.Snapshot();
            Check(first is {Magazine:4,Reserve:16,Phase:CustomWeaponPhase.Unequipped,Optic:0}
                &&runtime.InputProjection(human) is {Selected:false}&&runtime.State(target)==null&&runtime.AcceptedShots==0,
                "first healthy fixed step grants exact4/16 availability while leaving native weapon selected");
            var pools=human.ModAmmo;var weapon=human.CurrentWeapon;
            Step(30);
            Check(ReferenceEquals(runtime.State(human),state)&&runtime.State(human)!.Snapshot().Magazine==4
                &&human.ModAmmo==pools&&human.CurrentWeapon==weapon&&runtime.AcceptedShots==0,
                "repeated availability steps neither refill inventory nor emit or change native equipment");
            Press(Keys.C);Release();Step();
            Check(runtime.InputProjection(human) is {Selected:true}&&state.Snapshot().Phase==CustomWeaponPhase.Ready
                &&human.ModAmmo==pools&&human.CurrentWeapon==weapon,
                "existing physical custom selection input equips public availability without touching native ammo or beam");
            Check(!runtime.Grant(target)&&runtime.State(target)==null,
                "public runtime refuses explicit grant to a different living human in the same scene");
            target.IsBot=true;
            Check(CustomWeaponMatchPolicy.TryAdmit(scene,out _)&&!runtime.Grant(target)&&runtime.State(target)==null,
                "native offline bot target remains eligible while custom bot grant is refused");
            // Lift the explicit collision probe above room geometry. Native bot
            // state is retained, but its AI movement is not the subject of this probe.
            human.ModPlaceAt(new Vector3(0,10000,0));human.Speed=Vector3.Zero;
            var direction=human.ModGunVector.Normalized();
            target.ModPlaceAt(human.Position+new Vector3(0,.4f,0)+direction*3-new Vector3(0,.2f,0));target.Speed=Vector3.Zero;
            target.Health=200;
            typeof(PlayerEntity).GetField("_spawnInvulnTimer",Hidden)!.SetValue(target,(ushort)0);
            typeof(PlayerEntity).GetField("_damageInvulnTimer",Hidden)!.SetValue(target,(ushort)0);
            Check(!BeamProjectileEntity.TryBeamObstacle(human.Position+new Vector3(0,.4f,0),target.Position+new Vector3(0,.2f,0),scene,out _,out _),
                "native bot collision fixture has an actually unobstructed measured room ray");
            Press(Keys.Z,Keys.Enter);Release();
            BeamProjectileEntity? shot=null;
            foreach(var beam in scene.GetBeamProjectileEntities())if(beam.Owner==human&&beam.ModCustomShot!=null&&beam.Lifespan>0)shot=beam;
            Console.WriteLine($"AVAILABILITY SHOT selected={runtime.InputProjection(human)} state={state.Snapshot()} HP={target.Health} beam={shot!=null} shots={runtime.AcceptedShots}");
            Check(runtime.AcceptedShots==1&&state.Snapshot().Magazine==3&&shot?.ModCustomShot?.Owner==state.Owner,
                "actual keyboard scope and Fire consume one public M7 round with exact human-life provenance");
            shot!.Process();shot.Process();
            Check(target.IsBot&&target.Health==135&&runtime.LastImpact is {Damage:65,Headshot:false}
                &&runtime.State(target)==null&&human.ModAmmo==pools,
                "real custom collision pays native bot target65 through canonical damage without granting bot inventory");
            target.IsBot=false;
            var oldOwner=state.Owner;ulong accepted=runtime.AcceptedShots;
            human.TakeDamage(10000,DamageFlags.Death,null,null);runtime.Step();
            Check(human.Health==0&&state.Snapshot().Phase==CustomWeaponPhase.Invalid&&runtime.State(human)==null,
                "canonical native death invalidates the captured custom life");
            human.Spawn(human.Position,Vector3.UnitZ,Vector3.UnitY,human.NodeRef,respawn:true);Step();
            var respawned=runtime.State(human)!;
            Check(respawned.Owner.Life==oldOwner.Life+1&&respawned.Owner.Generation==oldOwner.Generation
                &&respawned.Snapshot() is {Magazine:4,Reserve:16,Phase:CustomWeaponPhase.Unequipped,Optic:0}
                &&runtime.InputProjection(human) is {Selected:false}&&state.Snapshot().Phase==CustomWeaponPhase.Invalid
                &&runtime.AcceptedShots==accepted,"real native respawn creates fresh4/16 life with native selected and cannot revive old state");
            Check(CustomReplayBoundary.IsBlocked(scene),"public offline availability marks the actual scene unsupported by native replay");
            scene.DoCleanup();
            Check(!runtime.IsEligible&&runtime.State(human)==null&&scene.OfflineWeapons==null&&scene.CustomWeapons==null,
                "scene cleanup retires escaped public runtime and registry");
            Console.WriteLine($"PASS {checks} public offline availability scene checks; real assets/simulation and synthetic keyboard, no UI toolkit, Android or online qualification");
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
