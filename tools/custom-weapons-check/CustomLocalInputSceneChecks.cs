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

// Asset-backed, headless LOCAL slot zero acceptance. Synthetic device samples
// enter Scene.OnSimulationFrame; they do not write weapon payments or gun aim.
// This does not qualify a physical device, Android view, GL scope or network peer.
internal static class CustomLocalInputSceneChecks
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Action<KeyboardState,Keys,bool> Key = typeof(KeyboardState)
        .GetMethod("SetKeyState",Hidden | BindingFlags.Public)!.CreateDelegate<Action<KeyboardState,Keys,bool>>();
    private static readonly Action<MouseState,Vector2> Position = typeof(MouseState)
        .GetProperty("Position",Hidden | BindingFlags.Public)!.SetMethod!.CreateDelegate<Action<MouseState,Vector2>>();

    internal static int Run(string data)
    {
        Scene? scene=null; bool force=MapAudit.ForceEveryone, stylus=PointerInput.StylusMode;
        float sensitivity=InputSettings.MouseSensitivity; int checks=0;
        void Check(bool value,string name)
        { if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name); }
        try
        {
            Directory.SetCurrentDirectory(Path.GetFullPath(data));Paths.UpdatePaths();Paths.ChooseMphPath();
            Headless.Enter();MapAudit.ForceEveryone=true;PointerInput.StylusMode=false;PointerDevice.Reset();
            GamepadManager.ClearAll();GamepadContexts.Focused=true;GamepadContexts.MenuVisible=false;
            GamepadContexts.Capturing=false;GamepadContexts.TextEntryActive=false;InputSettings.MouseSensitivity=1;
            var keyboard=SyntheticInput.CreateKeyboard();var mouse=SyntheticInput.CreateMouse();
            scene=new(new Vector2i(256,192),keyboard,mouse,_=>{},()=>{});
            scene.AddPlayer(Hunter.Samus);scene.AddPlayer(Hunter.Weavel);
            scene.Players.PlayerCount=2;scene.Players.MainPlayerIndex=0;
            foreach(var player in scene.Players.Items)player.IsBot=false;
            scene.AddRoom("MP3 PROVING GROUND",GameMode.Battle,playerCount:2,nodeLayerMask:0);scene.OnLoad();
            // Headless Roam retains the real simulation while All admits local
            // hardware samples, as in the existing native movement checks.
            var mode=typeof(Scene).GetField("_inputMode",Hidden)!;
            mode.SetValue(scene,Enum.Parse(mode.FieldType,"All"));
            scene.GameState.MatchTime=-1;scene.GameState.PointGoal=10000;
            var shooter=scene.Players.Items[0];var victim=scene.Players.Items[1];var controls=shooter.Controls;
            foreach(var bind in controls.All){bind.Type=ButtonType.Key;bind.Key=Keys.Unknown;}
            controls.Shoot.Key=Keys.Enter;controls.Zoom.Key=Keys.Z;controls.CustomWeapon.Key=Keys.C;
            controls.CustomReload.Key=Keys.R;controls.CustomOptic.Key=Keys.O;controls.PowerBeam.Key=Keys.D1;
            controls.Missile.Key=Keys.D2;
            controls.WeaponMenu.Key=Keys.Tab;controls.MoveRight.Key=Keys.D;controls.Morph.Key=Keys.F;
            controls.MouseAim=true;controls.KeyboardAim=false;
            void Step(int count=1)
            {for(int i=0;i<count;i++){NetTestScript.Rest(victim,wantBiped:true);scene.OnSimulationFrame();}}
            void Release()
            {foreach(var key in new[]{Keys.Enter,Keys.Z,Keys.C,Keys.R,Keys.O,Keys.D1,Keys.D2,Keys.Tab,Keys.D,Keys.F})Key(keyboard,key,false);}
            void Press(params Keys[] keys)
            {Release();Step();foreach(var key in keys)Key(keyboard,key,true);Step();}
            Key(keyboard,Keys.Enter,true);
            for(int i=0;i<600&&!shooter.LoadFlags.TestFlag(LoadFlags.Spawned);i++)Step();
            Release();scene.CameraSequences.Current?.End();Step(240);
            Check(!NetSession.Active&&NetHooks.LocalSlot==0&&ReferenceEquals(shooter,scene.Players.Main)
                &&shooter.ModIsInPlay&&shooter.Health>0&&!shooter.IsAltForm&&victim.Health>0,
                "real offline LOCAL slot zero and second player spawn through native simulation");
            // This map spawn touches a side wall in the selected strafe
            // direction. Lift the body clear so native movement is measurable.
            shooter.ModPlaceAt(shooter.Position+new Vector3(0,5,0));Step(2);
            var runtime=scene.EnableDeveloperRifle(true);
            int NativeFlights()
            {int count=0;foreach(var b in scene.GetBeamProjectileEntities())if(b.Owner==shooter&&b.ModCustomShot==null&&b.Lifespan>0)count++;return count;}
            BeamProjectileEntity CustomFlight(uint? sequence=null)
            {
                BeamProjectileEntity? found=null;
                foreach(var b in scene.GetBeamProjectileEntities())
                    if(b.Owner==shooter&&b.ModCustomShot is {} source&&(!sequence.HasValue||source.Sequence==sequence))
                    {if(found!=null)throw new Exception("ambiguous custom flight");found=b;}
                return found??throw new Exception("missing custom flight");
            }
            Check(runtime.Grant(shooter)&&runtime.State(shooter)!.Owner.Slot==0,"developer grant belongs to actual local slot zero");
            Check(shooter.ModTryCaptureCustomPrecombatFacts(shooter.ModGunVector,out var facts)&&facts.CanUseWeapon,
                "local precombat eligibility uses production player facts");
            int nativeAmmo=shooter.EquipInfo.Ammo;BeamType fallback=shooter.CurrentWeapon;
            Vector3 beforeBody=shooter.Position,beforeAim=shooter.ModGunVector;
            // Cycle chooses precision optic 2, Zoom enters it, then Fire consumes
            // that optic in the same sample. Raw mouse and movement are consumed
            // by PlayerInput before the queued local emission is completed.
            Position(mouse,mouse.Position+new Vector2(96,0));
            Key(keyboard,Keys.O,true);Key(keyboard,Keys.Z,true);Key(keyboard,Keys.Enter,true);Key(keyboard,Keys.D,true);Step();
            var shot=CustomFlight();
            Check(runtime.AcceptedShots==1&&runtime.State(shooter)!.Snapshot() is {Magazine:3,Optic:2},
                "actual keyboard cycle, Zoom and Shoot accept one ordered custom shot");
            Check(controls.Shoot.IsPressed&&controls.Shoot.IsDown&&controls.Zoom.IsPressed&&controls.Zoom.IsDown,
                "custom routing preserves physical Shoot and Zoom evidence");
            var input=(PlayerEntity.PlayerInput)typeof(PlayerEntity).GetProperty("Input",Hidden)!.GetValue(shooter)!;
            Console.WriteLine($"LOCAL INPUT DIAG body={beforeBody}->{shooter.Position} speed={shooter.Speed} aim={beforeAim}->{shooter.ModGunVector} mouse={input.MouseDeltaX}/{input.MouseDeltaY} right={controls.MoveRight.IsDown} flags={shooter.Flags1}");
            Check(Vector3.Distance(beforeBody,shooter.Position)>.0001f
                &&Vector3.Dot(beforeAim.Normalized(),shooter.ModGunVector.Normalized())<.999f,
                "same input step really changes body position and physical gun aim");
            Check(Vector3.Distance(shot.SpawnPosition,shooter.Position+new Vector3(0,.4f,0))<.0001f
                &&Vector3.Dot(shot.Direction.Normalized(),shooter.ModGunVector.Normalized())>.999999f,
                $"emission uses current postmovement muzzle and gun vector: muzzle={shot.SpawnPosition}, body={shooter.Position}, beam={shot.Direction}, gun={shooter.ModGunVector}");
            Check(shooter.CurrentWeapon==fallback&&shooter.EquipInfo.Ammo==nativeAmmo&&shooter.EquipInfo.ChargeLevel==0
                &&!shooter.EquipInfo.Zoomed&&NativeFlights()==0,"custom selection prevents native emission, payment, charge and native scope");
            Key(keyboard,Keys.D,false);Step(80);
            Check(runtime.AcceptedShots==1&&controls.Shoot.IsDown&&!controls.Shoot.IsPressed
                &&shooter.EquipInfo.ChargeLevel==0,"held real Shoot neither retries custom bolt nor charges native fallback");
            Press(Keys.R);
            Check(runtime.State(shooter)!.Snapshot().Phase==CustomWeaponPhase.Reloading
                &&runtime.State(shooter)!.Snapshot().Optic==0&&shooter.EquipInfo.Ammo==nativeAmmo,
                "public reload starts canonical custom reload and clears optic without native ammo mutation");
            Release();Step(138);
            Check(runtime.State(shooter)!.Snapshot().Magazine==4,"actual simulation completes custom reload");
            ulong accepted=runtime.AcceptedShots;
            Press(Keys.D1,Keys.Enter);
            Check(shooter.CurrentWeapon==fallback&&runtime.InputProjection(shooter) is {Selected:false}
                &&runtime.AcceptedShots==accepted&&controls.Shoot.IsPressed&&NativeFlights()==0,
                "same native fallback selection submits Unequip and suppresses simultaneous custom fire");
            Release();Step();
            Check(!scene.CustomInputSuppressesNative(shooter),"canonical settled Unequip releases local native fence");
            Press(Keys.Enter);
            Check(NativeFlights()>0&&runtime.AcceptedShots==accepted,"a fresh Shoot after settled Unequip restores genuine native emission");
            Release();Step(20);
            Press(Keys.C,Keys.Z,Keys.Enter);
            Check(runtime.InputProjection(shooter) is {Selected:true,Optic:2}&&runtime.AcceptedShots==accepted+1
                &&shooter.EquipInfo.Ammo==nativeAmmo,"same-step public Equip, scope and Fire commit in order");
            Release();Step(80);
            Press(Keys.Tab);
            Check(shooter.Flags1.TestFlag(PlayerFlags1.WeaponMenuOpen)&&shooter.WeaponSelection==fallback,
                "native wheel opens with existing fallback selected");
            Release();Step();
            Check(runtime.InputProjection(shooter) is {Selected:false}&&shooter.CurrentWeapon==fallback
                &&!shooter.Flags1.TestFlag(PlayerFlags1.WeaponMenuOpen),
                "releasing native wheel on same fallback explicitly unequips custom weapon");
            Press(Keys.C);Release();Step();
            var nativePools=shooter.ModAmmo;accepted=runtime.AcceptedShots;
            Check(nativePools.Missiles>0,"native spawn grants actual available missile ammunition");
            Press(Keys.D2,Keys.Enter);
            Console.WriteLine($"LOCAL INPUT MISSILE DIAG weapon={shooter.CurrentWeapon} selected={runtime.InputProjection(shooter)?.Selected} ammo={nativePools}->{shooter.ModAmmo} flights={NativeFlights()}");
            Check(shooter.CurrentWeapon==BeamType.Missile&&runtime.InputProjection(shooter) is {Selected:false}
                &&shooter.ModAmmo==nativePools&&runtime.AcceptedShots==accepted&&NativeFlights()==0,
                "different native selection changes beam and unequips custom before either weapon can debit or emit");
            Press(Keys.D1);Release();Step();

            // A genuine custom flight survives selection changes and resolves
            // through the ordinary room/player collision and TakeDamage path.
            Press(Keys.C,Keys.Z);Release();Step();
            shooter.ModPlaceAt(new Vector3(0,10000,0));shooter.Speed=Vector3.Zero;
            var aim=shooter.ModGunVector.Normalized();
            victim.ModPlaceAt(shooter.Position+new Vector3(0,.4f,0)+aim*9-new Vector3(0,.3f,0));
            victim.Speed=Vector3.Zero;
            Check(!BeamProjectileEntity.TryBeamObstacle(shooter.Position+new Vector3(0,.4f,0),
                victim.Position+new Vector3(0,.3f,0),scene,out _,out _),"old-flight probe has a measured unobstructed native room ray");
            victim.Health=200;
            typeof(PlayerEntity).GetField("_spawnInvulnTimer",Hidden)!.SetValue(victim,(ushort)0);
            typeof(PlayerEntity).GetField("_damageInvulnTimer",Hidden)!.SetValue(victim,(ushort)0);
            Press(Keys.Enter);
            uint oldSequence=runtime.State(shooter)!.Snapshot().LastAcceptedShotId;
            var oldShot=CustomFlight(oldSequence);
            var oldSource=oldShot.ModCustomShot!;
            Press(Keys.D1);Step(3);
            Check(runtime.InputProjection(shooter) is {Selected:false}&&victim.Health<200
                &&runtime.LastImpact?.Source.Sequence==oldSequence&&runtime.LastImpact?.Source==oldSource.Identity,
                "pre-Unequip custom flight retains typed provenance and pays through canonical collision");

            // These are declared typed queue seams, not hardware acceptance:
            // introduce a lifecycle boundary between submission and completion.
            Press(Keys.C);Release();Step();
            while((uint)scene.FrameCount<runtime.State(shooter)!.Snapshot().FireReadyTick)Step();
            int magazine=runtime.State(shooter)!.Snapshot().Magazine;accepted=runtime.AcceptedShots;
            Check(runtime.SubmitInput(shooter,CustomWeaponActionKind.Fire,0,out uint expired),"typed queue accepts current-life source");
            Step();runtime.CompleteInput(shooter);
            Check(runtime.AcceptedShots==accepted&&runtime.State(shooter)!.Snapshot().Magazine==magazine
                &&runtime.InputProjection(shooter)!.Value.SettledSequence>=expired,
                "unprocessed prior-step queue expires before later player aim phase");
            Check(runtime.SubmitInput(shooter,CustomWeaponActionKind.Fire,0,out _),"typed queue accepts pre-morph source");
            var flags=shooter.Flags1;
            typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Flags1))!.SetValue(shooter,flags|PlayerFlags1.Morphing);
            runtime.CompleteInput(shooter);
            typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Flags1))!.SetValue(shooter,flags);
            Check(runtime.AcceptedShots==accepted&&runtime.State(shooter)!.Snapshot().Magazine==magazine,
                "morph boundary cancels queued fire before resource payment or emission");
            Check(runtime.SubmitInput(shooter,CustomWeaponActionKind.Fire,0,out _),"typed queue accepts pre-death source");
            var retired=runtime.State(shooter)!;shooter.Health=0;runtime.CompleteInput(shooter);runtime.Step();
            Check(runtime.AcceptedShots==accepted&&retired.Snapshot().Magazine==magazine
                &&retired.Snapshot().Phase==CustomWeaponPhase.Invalid,"death boundary cancels queued fire and invalidates old life");
            shooter.Spawn(shooter.Position,Vector3.UnitZ,Vector3.UnitY,shooter.NodeRef,respawn:true);runtime.Step();
            runtime.CompleteInput(shooter);
            Check(runtime.State(shooter)!.Owner.Life==2&&runtime.State(shooter)!.Snapshot().Magazine==4
                &&runtime.AcceptedShots==accepted,"respawn cannot revive a queued old-life shot");
            Console.WriteLine($"PASS {checks} local custom input scene checks; synthetic keyboard/mouse, no physical/Android/GL/online qualification");
            return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        finally
        {NetSession.Stop();scene?.DoCleanup();MapAudit.ForceEveryone=force;PointerInput.StylusMode=stylus;PointerDevice.Reset();InputSettings.MouseSensitivity=sensitivity;GamepadManager.ClearAll();}
    }
}
