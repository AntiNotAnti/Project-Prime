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

// Real asset scene, public offline availability and fixed input/sink hooks.
// MenuVisible models an asynchronous surface-resume boundary, not a UI click.
internal static class CustomMenuSelectionSceneChecks
{
    private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    private static readonly Action<KeyboardState,Keys,bool> Key=typeof(KeyboardState)
        .GetMethod("SetKeyState",Hidden|BindingFlags.Public)!.CreateDelegate<Action<KeyboardState,Keys,bool>>();
    internal static int Run(string data)
    {
        Scene? scene=null;int checks=0;string cwd=Directory.GetCurrentDirectory();
        bool force=MapAudit.ForceEveryone,focus=GamepadContexts.Focused,menu=GamepadContexts.MenuVisible;
        bool capture=GamepadContexts.Capturing,text=GamepadContexts.TextEntryActive;var context=GamepadContexts.Current;
        void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Console.WriteLine("MENU SELECTION PASS "+label);}
        try
        {
            NetSession.Stop();Directory.SetCurrentDirectory(Path.GetFullPath(data));Paths.UpdatePaths();Paths.ChooseMphPath();
            Headless.Enter();MapAudit.ForceEveryone=true;GamepadManager.ClearAll();GamepadContexts.Focused=true;
            GamepadContexts.MenuVisible=false;GamepadContexts.Capturing=false;GamepadContexts.TextEntryActive=false;
            var keyboard=SyntheticInput.CreateKeyboard();
            scene=new(new Vector2i(256,192),keyboard,SyntheticInput.CreateMouse(),_=>{},()=>{});
            scene.AddPlayer(Hunter.Samus);scene.AddPlayer(Hunter.Weavel);scene.Players.PlayerCount=2;scene.Players.MainPlayerIndex=0;
            foreach(var actor in scene.Players.Items)actor.IsBot=false;
            scene.AddRoom("MP3 PROVING GROUND",GameMode.Battle,playerCount:2,nodeLayerMask:0);scene.OnLoad();
            scene.GameState.MatchTime=-1;scene.GameState.PointGoal=10000;
            var mode=typeof(Scene).GetField("_inputMode",Hidden)!;mode.SetValue(scene,Enum.Parse(mode.FieldType,"All"));
            var player=scene.Players.Items[0];var other=scene.Players.Items[1];
            foreach(var button in player.Controls.All){button.Type=ButtonType.Key;button.Key=Keys.Unknown;}
            player.Controls.Shoot.Key=Keys.Enter;player.Controls.Zoom.Key=Keys.Z;player.Controls.PowerBeam.Key=Keys.D1;
            void Step(int count=1){for(int i=0;i<count;i++){NetTestScript.Rest(other,true);scene.OnSimulationFrame();}}
            Check(scene.CustomWeaponMenuSelection==null,"native-only scene publishes no custom selection snapshot");
            var runtime=scene.EnableOfflineRifleAvailability(true);
            Key(keyboard,Keys.Enter,true);for(int i=0;i<600&&!player.LoadFlags.TestFlag(LoadFlags.Spawned);i++)Step();
            Key(keyboard,Keys.Enter,false);scene.CameraSequences.Current?.End();Step(240);
            var initial=scene.CustomWeaponMenuSelection??throw new Exception("selection snapshot missing after native spawn");
            var state=runtime.State(player)!;var pools=player.ModAmmo;var native=player.CurrentWeapon;
            Check(initial.CanEquip&&!initial.Selected&&!initial.CanReturnNative&&initial.Context.SceneGeneration==scene.LifetimeGeneration
                &&initial.Context.Owner==state.Owner&&initial.Context.Manifest==runtime.Manifest&&initial.Context.Weapon==state.Definition.Identity
                &&state.Snapshot() is {Magazine:4,Reserve:16,Phase:CustomWeaponPhase.Unequipped},
                "fixed-step public snapshot names exact scene/full-life/manifest/weapon and native-selected4/16 grant");
            var before=state.Snapshot();int health=player.Health,otherHealth=other.Health;ulong frame=scene.FrameCount,shots=runtime.AcceptedShots;
            var controls=player.Controls.All.Select(b=>(b.IsPressed,b.IsDown)).ToArray();bool stable=true;
            for(int i=0;i<10000;i++)stable&=ReferenceEquals(initial,scene.CustomWeaponMenuSelection);
            long allocated=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<100000;i++)stable&=ReferenceEquals(initial,scene.CustomWeaponMenuSelection);
            allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;
            Check(stable&&allocated==0&&state.Snapshot()==before&&scene.FrameCount==frame&&runtime.AcceptedShots==shots
                &&player.Health==health&&other.Health==otherHealth&&player.ModAmmo==pools
                &&player.Controls.All.Select(b=>(b.IsPressed,b.IsDown)).SequenceEqual(controls),
                "100000 immutable UI reads allocate0B and cannot advance resources/clocks/HP/ammo or physical controls");
            for(int i=0;i<100;i++)if(!scene.RequestCustomWeaponMenuSelection(initial,true))throw new Exception("coalesced UI request refused");
            Check(state.Snapshot()==before&&runtime.InputProjection(player)?.SettledSequence==0,
                "100 desired clicks only queue and cannot issue a control or change gameplay before next sample");
            Step();
            Check(runtime.InputProjection(player) is {Selected:true,SettledSequence:1}&&state.Snapshot().Magazine==4
                &&runtime.AcceptedShots==0&&player.CurrentWeapon==native&&player.ModAmmo==pools,
                "next actual input sample emits one Equip through canonical sink without refill/fire/native payment");
            Step();var custom=scene.CustomWeaponMenuSelection!;
            Check(custom.Selected&&custom.CanReturnNative&&!custom.CanEquip&&!scene.RequestCustomWeaponMenuSelection(custom,true),
                "settled custom snapshot refuses redundant Equip and offers native return");
            Check(scene.RequestCustomWeaponMenuSelection(custom,false),"native return queues desired state through same mailbox");
            Step();var pending=scene.CustomWeaponMenuSelection!;
            Check(pending.NativePending&&scene.CustomInputSuppressesNative(player)&&!pending.CanReturnNative
                &&!scene.RequestCustomWeaponMenuSelection(pending,false)&&runtime.InputProjection(player)?.SettledSequence==2,
                "one native return preserves pending native fence until following canonical synchronization");
            Step();var nativeView=scene.CustomWeaponMenuSelection!;
            Check(!nativeView.Selected&&!nativeView.NativePending&&!scene.CustomInputSuppressesNative(player)
                &&runtime.InputProjection(player)?.SettledSequence==2,"settled Unequip clears native fence without duplicate controls");
            Check(scene.RequestCustomWeaponMenuSelection(nativeView,true)&&scene.RequestCustomWeaponMenuSelection(nativeView,false),
                "opposing UI choices coalesce to final desired native state before sample");
            Step();Check(runtime.InputProjection(player) is {Selected:false,SettledSequence:2},"final already-native choice is a no-op, not another request");
            var priority=scene.CustomWeaponMenuSelection!;scene.RequestCustomWeaponMenuSelection(priority,true);
            Key(keyboard,Keys.D1,true);Step();
            Check(player.Controls.PowerBeam.IsPressed&&runtime.InputProjection(player) is {Selected:false,SettledSequence:2}
                &&runtime.AcceptedShots==0,"same-fallback physical native selection wins simultaneous desired custom request");
            Key(keyboard,Keys.D1,false);Step();
            var held=scene.CustomWeaponMenuSelection!;Check(scene.RequestCustomWeaponMenuSelection(held,true),"menu-resume choice queues while actor ready");
            GamepadContexts.MenuVisible=true;Step(3);
            Check(runtime.InputProjection(player) is {Selected:false,SettledSequence:2}&&scene.CustomWeaponMenuSelection!.State.ActorReady,
                "eligible actor request waits without controls only across bounded asynchronous menu surface transition");
            GamepadContexts.MenuVisible=false;Step();
            Check(runtime.InputProjection(player) is {Selected:true,SettledSequence:3},"first resumed gameplay sample consumes held desired selection once");
            Step();scene.RequestCustomWeaponMenuSelection(scene.CustomWeaponMenuSelection!,false);Step(2);
            uint settled=runtime.InputProjection(player)!.Value.SettledSequence;
            foreach(string restriction in new[]{"morph","frozen","text entry"})
            {
                Check(scene.RequestCustomWeaponMenuSelection(scene.CustomWeaponMenuSelection!,true),"queue before restriction: "+restriction);
                var flags=player.Flags1;
                if(restriction=="morph")typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Flags1))!.SetValue(player,flags|PlayerFlags1.Morphing);
                if(restriction=="frozen")typeof(PlayerEntity).GetField("_frozenTimer",Hidden)!.SetValue(player,(ushort)60);
                if(restriction=="text entry")GamepadContexts.TextEntryActive=true;
                Step();
                Check(runtime.InputProjection(player) is {Selected:false}&&runtime.InputProjection(player)!.Value.SettledSequence==settled,
                    "restricted sample drops desired selection before sink: "+restriction);
                typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Flags1))!.SetValue(player,flags);
                typeof(PlayerEntity).GetField("_frozenTimer",Hidden)!.SetValue(player,(ushort)0);
                GamepadContexts.TextEntryActive=false;Step(2);
                Check(runtime.InputProjection(player) is {Selected:false}&&runtime.InputProjection(player)!.Value.SettledSequence==settled,
                    "restriction recovery cannot rearm old UI choice: "+restriction);
            }
            Check(runtime.AcceptedShots==0&&player.ModAmmo==pools,
                "all desired-selection journeys preserve native ammo and emit no custom Fire before native lifecycle reset");
            var dying=scene.CustomWeaponMenuSelection!;Check(scene.RequestCustomWeaponMenuSelection(dying,true),"queue desired selection before canonical death");
            player.TakeDamage(10000,DamageFlags.Death,null,null);Step();
            Check(scene.CustomWeaponMenuSelection==null&&!scene.RequestCustomWeaponMenuSelection(dying,true),"death clears publication and rejects escaped old-life snapshot");
            player.Spawn(player.Position,Vector3.UnitZ,Vector3.UnitY,player.NodeRef,respawn:true);Step(2);
            var reborn=scene.CustomWeaponMenuSelection!;
            Check(reborn.Context.Owner.Life==dying.Context.Owner.Life+1&&!reborn.Selected
                &&!scene.RequestCustomWeaponMenuSelection(dying,true)&&runtime.State(player)!.Snapshot() is {Magazine:4,Reserve:16},
                "native respawn creates fresh native-selected context and cannot consume old-life queue");
            Check(runtime.AcceptedShots==0&&runtime.State(player)!.Snapshot().LastAcceptedShotId==0,
                "native respawn cannot turn retired menu choice into a custom Fire");
            scene.RequestCustomWeaponMenuSelection(reborn,true);scene.DoCleanup();
            Check(scene.CustomWeaponMenuSelection==null&&!scene.RequestCustomWeaponMenuSelection(reborn,true)&&!runtime.IsEligible,
                "cleanup atomically retires cached snapshot and pending desired selection");
            Console.WriteLine($"PASS {checks} menu selection scene checks; actual assets/fixed-step/sink, synthetic menu-context transition and keyboard, no UI toolkit/device/Android/online claim");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
        finally
        {
            NetSession.Stop();scene?.DoCleanup();MapAudit.ForceEveryone=force;GamepadManager.ClearAll();GamepadContexts.Focused=focus;
            GamepadContexts.MenuVisible=menu;GamepadContexts.Capturing=capture;GamepadContexts.TextEntryActive=text;GamepadContexts.Current=context;Directory.SetCurrentDirectory(cwd);
        }
    }
}
