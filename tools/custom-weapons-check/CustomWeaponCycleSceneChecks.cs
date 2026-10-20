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

// Real public offline acquisition and fixed-step keyboard cycle routing. The
// initial native-spawn approach and later native inventory/ammo top-up are declared
// fixture seams. No custom Grant, control submission, clock or custom ammo injection.
internal static class CustomWeaponCycleSceneChecks
{
    private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    private static readonly Action<KeyboardState,Keys,bool> Key=typeof(KeyboardState)
        .GetMethod("SetKeyState",Hidden|BindingFlags.Public)!.CreateDelegate<Action<KeyboardState,Keys,bool>>();
    internal static int Run(string data)
    {
        Scene? scene=null;string cwd=Directory.GetCurrentDirectory();bool force=MapAudit.ForceEveryone;
        bool stylus=PointerInput.StylusMode;int checks=0;
        void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("CYCLE PASS "+name);}
        try
        {
            Directory.SetCurrentDirectory(Path.GetFullPath(data));Paths.UpdatePaths();Paths.ChooseMphPath();
            Headless.Enter();MapAudit.ForceEveryone=true;PointerInput.StylusMode=false;PointerDevice.Reset();
            GamepadManager.ClearAll();GamepadContexts.Focused=true;GamepadContexts.MenuVisible=false;
            GamepadContexts.Capturing=false;GamepadContexts.TextEntryActive=false;
            var keyboard=SyntheticInput.CreateKeyboard();scene=new(new Vector2i(256,192),keyboard,SyntheticInput.CreateMouse(),_=>{},()=>{});
            scene.AddPlayer(Hunter.Samus);scene.AddRoom("MP3 PROVING GROUND",GameMode.Battle,playerCount:1,nodeLayerMask:0);
            scene.OnLoad();var mode=typeof(Scene).GetField("_inputMode",Hidden)!;mode.SetValue(scene,Enum.Parse(mode.FieldType,"All"));
            scene.GameState.MatchTime=-1;scene.GameState.PointGoal=10000;
            var player=scene.Players.Main;player.IsBot=false;var controls=player.Controls;
            foreach(var button in controls.All){button.Type=ButtonType.Key;button.Key=Keys.Unknown;}
            controls.Shoot.Key=Keys.Enter;controls.MoveUp.Key=Keys.W;controls.CustomWeapon.Key=Keys.C;
            controls.NextWeapon.Key=Keys.N;controls.PrevWeapon.Key=Keys.B;controls.PowerBeam.Key=Keys.P;controls.Missile.Key=Keys.M;
            controls.MouseAim=controls.KeyboardAim=false;
            void Step(int count=1){for(int i=0;i<count;i++)scene.OnSimulationFrame();}
            void Clear(){foreach(var k in new[]{Keys.Enter,Keys.W,Keys.C,Keys.N,Keys.B,Keys.P,Keys.M})Key(keyboard,k,false);}
            void Press(params Keys[] keys){Clear();Step();foreach(var k in keys)Key(keyboard,k,true);Step();}
            Key(keyboard,Keys.Enter,true);for(int i=0;i<600&&!player.LoadFlags.TestFlag(LoadFlags.Spawned);i++)Step();
            Clear();scene.CameraSequences.Current?.End();Step(180);scene.CameraSequences.Current?.End();
            Check(player.Health>0&&player.ModIsInPlay&&player.CurrentWeapon==BeamType.PowerBeam,"native human starts alive on Power Beam");
            controls.ScrollAllWeapons=false;Press(Keys.N);Press(Keys.B);
            Check(player.CurrentWeapon==BeamType.PowerBeam&&scene.OfflineWeapons==null,"native-only affinity cycle retains Power Beam no-op");
            controls.ScrollAllWeapons=true;Press(Keys.N);
            Check(player.CurrentWeapon==BeamType.Missile,"native-only all-weapons next uses native missile");
            Press(Keys.B);Check(player.CurrentWeapon==BeamType.PowerBeam,"native-only previous returns Power Beam");
            var runtime=scene.EnableOfflineRifleAvailability(true);Clear();Step();
            Press(Keys.B);Check(!runtime.TryPeek(player,out _,out _)&&!scene.CustomInputSuppressesNative(player),"empty public cycle cannot grant or suppress native");
            Press(Keys.P);Clear();Step(30);
            PlayerSpawnEntity? spawn=null;foreach(var item in scene.GetPlayerSpawnEntities())if(item.Id==4)spawn=item;
            Check(spawn!=null,"native approach spawn4 exists");var start=spawn!.Position;CollisionResult floor=default;
            Check(CollisionDetection.CheckBetweenPoints(start+Vector3.UnitY*2,start-Vector3.UnitY*6,TestFlags.Players,scene,ref floor),"actual floor collision supports approach");
            start.Y=floor.Position.Y;var center=runtime.Pickups!.Layout.Placements[0].Position;var walk=(center-start).WithY(0).Normalized();
            player.Reposition(start.AddY(.5f),walk,player.NodeRef);player.ModSetAim(walk);player.Speed=Vector3.Zero;Step(15);
            Key(keyboard,Keys.W,true);for(int i=0;i<120&&runtime.Pickups.Revision==0;i++)Step();Clear();Step(2);
            Check(runtime.TryPeek(player,out var initial,out bool selected)&&selected&&initial.Magazine==4&&initial.Reserve==16,
                "actual walking pickup funds and selects M7 before cycle tests");
            var owner=initial.Owner;var nativeAmmo=player.ModAmmo;
            bool Selected()=>runtime.InputProjection(player)?.Selected==true;
            void Unchanged(string label)=>Check(runtime.TryPeek(player,out var s,out _)&&s.Owner==owner&&s.Magazine==4&&s.Reserve==16
                &&runtime.AcceptedShots==0&&player.ModAmmo==nativeAmmo,label);
            controls.ScrollAllWeapons=true;Press(Keys.N,Keys.Enter);
            Check(!Selected()&&player.CurrentWeapon==BeamType.PowerBeam&&runtime.AcceptedShots==0,"M7 next wraps to Power Beam and simultaneous Shoot cannot emit custom");
            Unchanged("selection plus fire cannot debit either inventory");
            int NativeFlights(){int n=0;foreach(var b in scene.GetBeamProjectileEntities())if(b.Owner==player&&b.Lifespan>0&&b.ModCustomShot==null)n++;return n;}
            Check(NativeFlights()==0,"simultaneous return to native cannot also emit its fallback");
            Clear();Step();Check(!scene.CustomInputSuppressesNative(player),"settled native cycle releases suppression");
            Press(Keys.B);Check(Selected()&&player.CurrentWeapon==BeamType.PowerBeam,"reverse Power Beam cycle enters acquired M7 without native ID mutation");
            Step(20);Check(Selected()&&player.CurrentWeapon==BeamType.PowerBeam&&runtime.AcceptedShots==0,"held previous control cannot create repeated selection edges");
            Press(Keys.B);Check(!Selected()&&player.CurrentWeapon==BeamType.Missile,"reverse M7 cycle skips unavailable native weapons to missile");
            Press(Keys.N);Check(Selected()&&player.CurrentWeapon==BeamType.Missile,"forward missile cycle skips unavailable natives into M7");
            Press(Keys.N);Check(!Selected()&&player.CurrentWeapon==BeamType.PowerBeam,"forward custom cycle wraps to native Power Beam");
            controls.ScrollAllWeapons=false;Press(Keys.N);Check(Selected(),"affinity-only cycle can enter acquired M7 from Power Beam");
            Press(Keys.N);Check(!Selected()&&player.CurrentWeapon==BeamType.PowerBeam,"affinity-only cycle returns to held native fallback");
            Press(Keys.B);Check(Selected(),"affinity-only reverse can enter M7");
            Press(Keys.B);Check(!Selected()&&player.CurrentWeapon==BeamType.PowerBeam,"affinity-only reverse exits M7 with no affinity weapons");
            // Declared extra native inventory fixture: actual native eligibility
            // still filters candidates, and TryEquipWeapon owns each transition.
            player.AvailableWeapons[BeamType.VoltDriver]=true;player.AvailableWeapons[BeamType.Imperialist]=true;
            Press(Keys.N);Check(Selected()&&player.CurrentWeapon==BeamType.PowerBeam,
                "real native zero-ammo filter skips newly available empty affinity weapons");
            Unchanged("native dry candidates cannot change either inventory");
            player.ModArmWeapon(BeamType.VoltDriver);player.ModArmWeapon(BeamType.Imperialist);
            nativeAmmo=player.ModAmmo;Press(Keys.P);Clear();Step(30);
            Press(Keys.N);Check(!Selected()&&player.CurrentWeapon==BeamType.VoltDriver,"acquired custom cycle retains native Volt Driver order");
            Press(Keys.N);Check(!Selected()&&player.CurrentWeapon==BeamType.Imperialist,"acquired custom cycle retains native Imperialist order");
            Press(Keys.N);Check(Selected()&&player.CurrentWeapon==BeamType.Imperialist,"M7 follows eligible native affinity candidates");
            Press(Keys.N);Check(!Selected()&&player.CurrentWeapon==BeamType.VoltDriver,"affinity-only M7 wraps to first eligible affinity");
            Press(Keys.C);Clear();Step();
            Press(Keys.P,Keys.N,Keys.Enter);Check(!Selected()&&player.CurrentWeapon==BeamType.PowerBeam,"direct native binding wins a simultaneous cycle and Shoot");
            Unchanged("all cycling preserves custom magazine/reserve, shot count, owner and native ammo");
            Press(Keys.B,Keys.N);Check(!Selected()&&player.CurrentWeapon==BeamType.VoltDriver,"simultaneous next/previous follows native next priority");
            Clear();Step(20);Press(Keys.C);Clear();Step();
            // Ownership deletion is an explicit fixture seam; actual death and
            // empty respawn are separately covered by the pickup lifecycle tool.
            player.Health=0;Step();Press(Keys.N);
            Check(!runtime.TryPeek(player,out _,out _)&&!scene.CustomInputSuppressesNative(player),"dead owner cannot cycle stale M7 or retain offline suppression");
            Console.WriteLine($"PASS {checks} acquired custom weapon cycle scene checks; real public pickup and fixed-step synthetic keyboard; native inventory/death fixture seams, no physical pad/touch/online qualification.");
            return 0;
        }
        catch(Exception error){Console.Error.WriteLine("CYCLE FAIL "+error);return 1;}
        finally{scene?.DoCleanup();MapAudit.ForceEveryone=force;PointerInput.StylusMode=stylus;PointerDevice.Reset();GamepadManager.ClearAll();Directory.SetCurrentDirectory(cwd);}
    }
}
