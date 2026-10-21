using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.CustomWeapons.Packages;
using MphRead.Mods.Input;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

// Real GL/offline package activation. Authored native spawn positioning is setup;
// ownership comes only from walking ordinary item contact. No inventory/state injection.
internal static class WeaponPackageGameplayChecks
{
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static readonly Action<KeyboardState,Keys,bool> Key=typeof(KeyboardState).GetMethod("SetKeyState",Hidden|BindingFlags.Public)!.CreateDelegate<Action<KeyboardState,Keys,bool>>();
    internal static int Run(string data,string output)
    {
        string cwd=Directory.GetCurrentDirectory();bool force=MapAudit.ForceEveryone,script=NetTestScript.Enabled;
        bool focus=GamepadContexts.Focused,menu=GamepadContexts.MenuVisible,capturing=GamepadContexts.Capturing,text=GamepadContexts.TextEntryActive;
        bool stylus=PointerInput.StylusMode,scopeHold=InputSettings.CustomScopeHold;
        Scene? scene=null;NativeWindow? window=null;DesktopGraphicsSession? graphics=null;int checks=0;
        var watch=Stopwatch.StartNew();var captures=new List<object>();
        void Check(bool ok,string label){if(!ok)throw new InvalidOperationException(label);checks++;Console.WriteLine("PACKAGE GL PASS "+label);}
        try
        {
            output=Path.GetFullPath(output);Directory.CreateDirectory(output);
            data=Path.GetFullPath(data);Directory.SetCurrentDirectory(data);Paths.UpdatePaths();Paths.ChooseMphPath();
            Check(!Headless.Active&&!NetSession.Active&&!DemoPlayback.IsActive,"fresh ordinary visual offline process");
            MapAudit.ForceEveryone=true;NetTestScript.Enabled=false;GamepadManager.ClearAll();GamepadContexts.Focused=true;
            GamepadContexts.MenuVisible=GamepadContexts.Capturing=GamepadContexts.TextEntryActive=false;PointerInput.StylusMode=false;PointerDevice.Reset();InputSettings.CustomScopeHold=false;
            var original=WeaponPackage.Parse(WeaponPackageReference.BuildRiftlineM7());
            var resources=original.ResourcePaths.Where(x=>x!="weapon.json").ToDictionary(x=>x,x=>original.CopyResource(x),StringComparer.Ordinal);
            var mechanics=JsonNode.Parse(resources[original.Project.MechanicsPath])!;
            mechanics["damage"]!["body"]=37;mechanics["damage"]!["head"]=83;mechanics["magazineCapacity"]=6;mechanics["initialReserve"]=23;mechanics["maximumReserve"]=40;mechanics["shotCooldownTicks"]=24;mechanics["reloadTicks"]=42;
            resources[original.Project.MechanicsPath]=Encoding.UTF8.GetBytes(mechanics.ToJsonString());
            var model=JsonNode.Parse(resources[original.Project.ModelPath])!;model["palette"]![0]=new JsonArray(0.95f,0.08f,0.55f);resources[original.Project.ModelPath]=Encoding.UTF8.GetBytes(model.ToJsonString());
            foreach(string path in original.Project.AudioPaths){var wave=resources[path];for(int i=44;i<wave.Length;i+=2){short sample=BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(i));BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(i),(short)(sample/2));}}
            var project=JsonNode.Parse(original.Project.ToCanonicalBytes())!;project["name"]="Package acceptance magenta six-round";project["contentId"]="11c011c0-1122-3344-5566-778899aabbcc";
            byte[] archive=WeaponPackageBuilder.Build(WeaponProject.Parse(Encoding.UTF8.GetBytes(project.ToJsonString())),resources);
            var zeroRules=JsonNode.Parse(resources[original.Project.MechanicsPath])!;zeroRules["initialReserve"]=0;zeroRules["maximumReserve"]=0;
            var zeroDefinition=WeaponDefinition.Create(original.Project.ContentId,"Zero reserve package",WeaponMechanics.Parse(Encoding.UTF8.GetBytes(zeroRules.ToJsonString())));
            using(var zeroRegistry=new WeaponRegistry(true,new[]{zeroDefinition}))
            {
                var zeroManifest=new CustomWeaponManifest(new[]{zeroDefinition});var placements=StockRiftlinePickups.ForRoom("MP3 PROVING GROUND");placements[0]=placements[0] with{ReserveRefill=0};
                var zeroLayout=new CustomPickupLayout(MphRead.Mods.MapGen.MapContentIdentity.BuiltIn("MP3 PROVING GROUND"),zeroManifest,zeroRegistry,placements);
                Check(zeroLayout.Count==1&&zeroLayout.Definition(0).Mechanics.MaximumReserve==0,"zero maximum reserve admits zero refill layout");
            }
            using(var positiveRegistry=new WeaponRegistry(true,new[]{original.Definition}))
            {
                bool zeroRefused=false;var placements=StockRiftlinePickups.ForRoom("MP3 PROVING GROUND");placements[0]=placements[0] with{ReserveRefill=0};
                try{_ =new CustomPickupLayout(MphRead.Mods.MapGen.MapContentIdentity.BuiltIn("MP3 PROVING GROUND"),new CustomWeaponManifest(new[]{original.Definition}),positiveRegistry,placements);}catch(ArgumentException){zeroRefused=true;}
                Check(zeroRefused,"positive maximum reserve still refuses zero refill layout");
            }
            var store=new WeaponPackageStore(Path.Combine(output,"store"));var package=store.Install(archive);
            File.WriteAllBytes(Path.Combine(output,"variant.ppweapon"),archive);
            Check(package.Project.ContentId!=original.Project.ContentId&&package.Definition.Identity!=original.Definition.Identity&&package.ContentHash!=original.ContentHash,"installed variant has distinct mechanics and presentation identity");
            var settings=DesktopGlContext.Settings(background:true);settings.ClientSize=new(960,720);settings.StartVisible=true;settings.Title="Project Prime local package gameplay acceptance";
            window=new NativeWindow(settings);graphics=new DesktopGraphicsSession(window);GLFW.SwapInterval(0);
            string renderer=GL.GetString(StringName.Renderer),version=GL.GetString(StringName.Version);
            var keyboard=SyntheticInput.CreateKeyboard();scene=new(window.FramebufferSize,keyboard,SyntheticInput.CreateMouse(),_=>{},()=>{});
            scene.AddPlayer(Hunter.Samus);scene.AddPlayer(Hunter.Kanden);scene.Players.PlayerCount=2;scene.AddRoom("MP3 PROVING GROUND",GameMode.Battle,playerCount:2,nodeLayerMask:0);scene.OnLoad();scene.OnResize();
            var inputMode=typeof(Scene).GetField("_inputMode",Hidden)!;inputMode.SetValue(scene,Enum.Parse(inputMode.FieldType,"All"));scene.GameState.MatchTime=-1;scene.GameState.PointGoal=10000;
            var player=scene.Players.Main;var victim=scene.Players.Items[1];player.IsBot=victim.IsBot=false;foreach(var button in player.Controls.All){button.Type=ButtonType.Key;button.Key=Keys.Unknown;}
            player.Controls.Shoot.Key=Keys.Enter;player.Controls.MoveUp.Key=Keys.W;player.Controls.Zoom.Key=Keys.Z;player.Controls.CustomReload.Key=Keys.R;player.Controls.CustomWeapon.Key=Keys.C;
            player.Controls.MouseAim=player.Controls.KeyboardAim=false;
            bool refused=false;try{scene.EnableOfflinePackageAvailability(store,package.PackageHash,false);}catch(InvalidOperationException){refused=true;}
            Check(refused&&scene.LocalWeaponPackage==null&&scene.CustomWeapons==null&&scene.CustomRifleModel==null&&scene.CustomRifleWave(0).Length==0,"false opt-in leaves package registry and presentation unbound");
            var runtime=scene.EnableOfflinePackageAvailability(store,package.PackageHash,true);var ledger=runtime.Pickups??throw new InvalidOperationException("Missing package pickup ledger.");
            Check(scene.LocalWeaponPackage?.PackageHash==package.PackageHash&&runtime.Mechanics.DefinitionHash==package.Definition.Mechanics.DefinitionHash,"scene binds exact installed package before first tick");
            Check(runtime.Mechanics.MagazineCapacity==6&&runtime.Mechanics.InitialReserve==23&&runtime.Mechanics.BodyDamage==37&&runtime.Mechanics.HeadDamage==83&&runtime.Mechanics.ShotCooldownTicks==24&&runtime.Mechanics.ReloadTicks==42,"runtime uses variant rules instead of bundled rifle constants");
            Check(scene.CustomRifleModel!=null&&scene.CustomRifleModel.Color(0)==new Vector4(.95f,.08f,.55f,1),"CPU prewarm owns package magenta palette before simulation");
            for(int cue=0;cue<4;cue++)Check(scene.CustomRifleWave(cue).SequenceEqual(WeaponPackage.DecodeWave(package.CopyResource(package.Project.AudioPaths[cue]))),"CPU prewarm owns exact package wave "+cue);
            void Step(int count=1){for(int i=0;i<count;i++){if(watch.Elapsed.TotalSeconds>180)throw new TimeoutException("Package GL exceeded180seconds.");NetTestScript.Rest(victim,true);scene.OnSimulationFrame();}}
            void Press(Keys key,int after=2){Key(keyboard,key,true);Step();Key(keyboard,key,false);Step(after);}
            Key(keyboard,Keys.Enter,true);for(int i=0;i<600&&!player.LoadFlags.TestFlag(LoadFlags.Spawned);i++)Step();Key(keyboard,Keys.Enter,false);scene.CameraSequences.Current?.End();Step(180);scene.CameraSequences.Current?.End();
            Check(player.Health>0&&player.ModIsInPlay&&scene.CameraMode==CameraMode.Player,"native player actually spawns first person");
            Check(!runtime.TryPeek(player,out _,out _)&&ledger.Revision==0,"explicit package activation starts empty without grant");
            PlayerSpawnEntity? spawn=null;foreach(var candidate in scene.GetPlayerSpawnEntities())if(candidate.Id==4)spawn=candidate;if(spawn==null)throw new InvalidOperationException("Authored spawn4 missing.");var start=spawn.Position;CollisionResult floor=default;
            Check(CollisionDetection.CheckBetweenPoints(start+Vector3.UnitY*2,start-Vector3.UnitY*6,TestFlags.Players,scene,ref floor),"authored spawn4 has native floor collision");start.Y=floor.Position.Y;
            var center=ledger.Layout.Placements[0].Position;var walk=(center-start).WithY(0).Normalized();player.Reposition(start.AddY(.5f),walk,scene.Room!.GetNodeRefByPosition(start));player.ModSetAim(walk);player.Speed=Vector3.Zero;Step(15);
            void Draw(string name)
            {
                ulong frame=scene.FrameCount,shots=runtime.AcceptedShots,revision=ledger.Revision;bool owned=runtime.TryPeek(player,out var before,out bool selected);
                scene.OnDrawFrame();Check(scene.OnRenderFrame(),"actual GL renderer draws "+name);Check(GL.GetError()==OpenTK.Graphics.OpenGL.ErrorCode.NoError,"GL no error "+name);
                string windowPath=Path.Combine(output,name+"-window.png"),worldPath=Path.Combine(output,name+"-world.png");
                Check(ScreenCapture.SaveWindow(scene,windowPath)&&ScreenCapture.Save(scene,worldPath),"captures native window and world "+name);
                captures.Add(new{name,windowPath,worldPath,windowSha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(windowPath))),worldSha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(worldPath)))});
                DesktopGraphicsSession.Present(window);scene.AfterRenderFrame();bool ownedAfter=runtime.TryPeek(player,out var after,out bool selectedAfter);
                Check(scene.FrameCount==frame&&runtime.AcceptedShots==shots&&ledger.Revision==revision&&owned==ownedAfter&&before==after&&selected==selectedAfter,"draw does not mutate simulation "+name);
            }
            // Presentation-only free camera sees the package station before contact.
            scene.SetFreeCamera(true);var camera=center+new Vector3(2,1.5f,-3);var facing=(center-camera).Normalized();var right=Vector3.Cross(facing,Vector3.UnitY).Normalized();
            void Camera(string field,Vector3 value)=>typeof(Scene).GetField(field,Hidden)!.SetValue(scene,value);
            Camera("_cameraPosition",camera);Camera("_cameraFacing",facing);Camera("_cameraRight",right);Camera("_cameraUp",Vector3.Cross(right,facing));Draw("package-station");scene.SetFreeCamera(false);
            player.ModSetAim(walk);Key(keyboard,Keys.W,true);int walkingSteps=0;for(;walkingSteps<120&&ledger.Revision==0;walkingSteps++)Step();Key(keyboard,Keys.W,false);Step(2);
            Check(walkingSteps>0&&walkingSteps<120&&ledger.Revision==1,"ordinary W movement and item phase acquire installed variant");
            Check(runtime.TryPeek(player,out var funded,out bool equipped)&&equipped&&funded.Magazine==6&&funded.Reserve==23&&runtime.AcceptedShots==0,"ordinary world funding uses package capacity without firing");Draw("package-equipped");
            Press(Keys.Z);Check(runtime.TryPeek(player,out var scoped,out _)&&scoped.Optic!=0,"ordinary scope binding accepts package optic");Draw("package-scoped");
            Press(Keys.Enter);Check(runtime.AcceptedShots==1&&runtime.TryPeek(player,out var fired,out _)&&fired.Magazine==5,"ordinary shoot binding accepts package shot and spends one round");
            Check(scene.CustomRiflePose(player).Kick>0||scene.CustomRiflePose(player).Flash>0,"new creator identity drives actual accepted shot cosmetic clock");
            Step(26);Press(Keys.R,45);Check(runtime.TryPeek(player,out var reloaded,out _)&&reloaded.Magazine==6&&reloaded.Reserve==22,"ordinary reload binding completes package42tick reload and conserves reserve");Step(30);Draw("package-reloaded");
            Check(victim.Health>0,"native target actually spawned without custom ownership");
            Vector3 targetFrom=default,targetTo=default;bool clear=false;
            foreach(int height in new[]{0,2,5,10})
            {
                foreach(var direction in new[]{Vector3.UnitX,-Vector3.UnitX,Vector3.UnitZ,-Vector3.UnitZ})
                {
                    var from=player.Position+Vector3.UnitY*height;var to=from+direction*20;
                    if(!BeamProjectileEntity.TryBeamObstacle(from+new Vector3(0,.4f,0),to+new Vector3(0,.2f,0),scene,out _,out _)){targetFrom=from;targetTo=to;clear=true;break;}
                }
                if(clear)break;
            }
            Check(clear,"declared native target setup has unobstructed body ray");
            player.ModPlaceAt(targetFrom);victim.ModPlaceAt(targetTo);player.Speed=victim.Speed=Vector3.Zero;victim.Health=200;
            typeof(PlayerEntity).GetField("_spawnInvulnTimer",Hidden)!.SetValue(victim,(ushort)0);typeof(PlayerEntity).GetField("_damageInvulnTimer",Hidden)!.SetValue(victim,(ushort)0);
            Press(Keys.Z);Check(runtime.TryPeek(player,out var damageScoped,out _)&&damageScoped.Optic!=0,"native damage probe uses exact zero-spread scoped package ray");
            player.ModSetAim((victim.Position+Vector3.UnitY*.2f-(player.Position+Vector3.UnitY*.4f)).Normalized());
            Press(Keys.Enter,2);
            Check(runtime.AcceptedShots==2&&victim.Health==163&&runtime.LastImpact is {Damage:37,Headshot:false},"actual package emitter collision pays variant37body damage to native target");
            Check(!runtime.TryPeek(victim,out _,out _),"native victim has no custom ownership");Draw("package-body-impact");
            Check(store.Uninstall(package.PackageHash)&&store.Prune()==0,"active scene retains archive lease after uninstall");
            bool uninstalledRefused=false;try{using var unexpected=store.AcquireInstalled(package.PackageHash);}catch(FileNotFoundException){uninstalledRefused=true;}
            Check(uninstalledRefused,"installed-only acquisition refuses retained uninstalled archive");
            scene.DoCleanup();scene=null;Check(store.Prune()==1,"scene cleanup releases exact package lease for prune");
            File.WriteAllText(Path.Combine(output,"package-gl-results.json"),JsonSerializer.Serialize(new{checks,renderer,version,walkingSteps,package.PackageHash,package.ContentHash,mechanicsHash=package.Definition.Mechanics.DefinitionHash,seconds=watch.Elapsed.TotalSeconds,captures,scope="Actual native GL, explicit package activation, ordinary world acquisition and keyboard fire/scope/reload; authored spawn positioning and presentation camera declared; actual native target body damage with declared placement/HP setup; no physical audio device, Android or multiplayer qualification."},new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine($"PASS {checks} local package gameplay GL checks");return 0;
        }
        catch(Exception error){Console.Error.WriteLine("PACKAGE GL FAIL "+error);return 1;}
        finally{scene?.DoCleanup();MphRead.Sound.AudioLifetime.Shutdown();graphics?.Dispose();window?.Dispose();MapAudit.ForceEveryone=force;NetTestScript.Enabled=script;GamepadContexts.Focused=focus;GamepadContexts.MenuVisible=menu;GamepadContexts.Capturing=capturing;GamepadContexts.TextEntryActive=text;PointerInput.StylusMode=stylus;InputSettings.CustomScopeHold=scopeHold;PointerDevice.Reset();GamepadManager.ClearAll();Directory.SetCurrentDirectory(cwd);}
    }
}
