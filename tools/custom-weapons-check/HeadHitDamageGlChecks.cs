using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Input;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using MphRead.Mods.Cosmetics;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

// Genuine custom emitter/collision/TakeDamage integration in a visual scene.
// HP, placement and initial developer ownership are declared test setup.
internal static class HeadHitDamageGlChecks
{
    private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    private static readonly Action<KeyboardState,Keys,bool> Key=typeof(KeyboardState)
        .GetMethod("SetKeyState",Hidden|BindingFlags.Public)!.CreateDelegate<Action<KeyboardState,Keys,bool>>();
    internal static int Run(string data,string output)
    {
        string cwd=Directory.GetCurrentDirectory();bool force=MapAudit.ForceEveryone;
        bool cosmetics=RenderOptions.ShowCustomCosmetics;var quality=RenderOptions.CosmeticQuality;
        NativeWindow? window=null;DesktopGraphicsSession? graphics=null;Scene? scene=null;
        int checks=0;var results=new List<object>();var watch=Stopwatch.StartNew();
        void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Console.WriteLine("HEAD DAMAGE PASS "+label);}
        try
        {
            output=Path.GetFullPath(output);Directory.CreateDirectory(output);
            Directory.SetCurrentDirectory(Path.GetFullPath(data));Paths.UpdatePaths();Paths.ChooseMphPath();
            Check(!Headless.Active&&!NetSession.Active,"fresh real visual offline scene");
            MapAudit.ForceEveryone=true;RenderOptions.ShowCustomCosmetics=true;RenderOptions.CosmeticQuality=CosmeticEffectQuality.Medium;
            var settings=DesktopGlContext.Settings(background:true);settings.ClientSize=new(960,720);settings.StartVisible=true;
            window=new NativeWindow(settings);graphics=new DesktopGraphicsSession(window);GLFW.SwapInterval(0);
            foreach(var hunter in new[]{Hunter.Samus,Hunter.Kanden,Hunter.Trace,Hunter.Sylux,Hunter.Noxus,Hunter.Spire,Hunter.Weavel})
            {
                FrameTiming.Reset();var keyboard=SyntheticInput.CreateKeyboard();
                scene=new(window.FramebufferSize,keyboard,SyntheticInput.CreateMouse(),_=>{},()=>{});
                scene.AddPlayer(hunter);scene.AddPlayer(Hunter.Samus);scene.Players.PlayerCount=2;
                foreach(var player in scene.Players.Items)player.IsBot=false;
                scene.AddRoom("MP3 PROVING GROUND",GameMode.Battle,playerCount:2,nodeLayerMask:0);scene.OnLoad();scene.OnResize();
                scene.GameState.MatchTime=-1;scene.GameState.PointGoal=10000;
                var shooter=scene.Players.Items[1];var victim=scene.Players.Items[0];
                void Step(int count=1){for(int i=0;i<count;i++){if(watch.Elapsed.TotalSeconds>600)throw new TimeoutException();NetTestScript.Rest(shooter,true);scene.OnSimulationFrame();}}
                Key(keyboard,Keys.Enter,true);Step(240);Key(keyboard,Keys.Enter,false);scene.CameraSequences.Current?.End();Step(10);
                foreach(var player in scene.Players.Items)foreach(var button in player.Controls.All){button.Type=ButtonType.Key;button.Key=Keys.Unknown;}
                Check(shooter.Health>0&&victim.Health>0,hunter+" actual two-player spawn");
                var runtime=scene.EnableDeveloperRifle(true);Check(runtime.Grant(shooter),hunter+" declared shooter-only grant");Step();
                Check(!runtime.TryPeek(victim,out _,out _),hunter+" victim has no custom inventory requirement");
                uint sequence=0;float head=Fixed.ToFloat(victim.Values.MaxPickupHeight)-.15f;
                Vector3 from=default,to=default;bool found=false;
                foreach(int height in new[]{0,2,5,10})
                {
                    foreach(var direction in new[]{Vector3.UnitX,-Vector3.UnitX,Vector3.UnitZ,-Vector3.UnitZ})
                    {var a=shooter.Position+Vector3.UnitY*height;var b=a+direction*20;
                     if(!BeamProjectileEntity.TryBeamObstacle(a+new Vector3(0,.4f,0),b+new Vector3(0,head,0),scene,out _,out _))
                     {from=a;to=b;found=true;break;}}
                    if(found)break;
                }
                Check(found,hunter+" genuine unobstructed 20-unit head ray");
                void Prepare(ushort hp,bool protect=false)
                {
                    while((uint)scene.FrameCount<runtime.State(shooter)!.Snapshot().FireReadyTick)Step();
                    shooter.ModPlaceAt(from);victim.ModPlaceAt(to);shooter.Speed=victim.Speed=Vector3.Zero;victim.Health=hp;
                    typeof(PlayerEntity).GetField("_spawnInvulnTimer",Hidden)!.SetValue(victim,(ushort)(protect?120:0));
                    typeof(PlayerEntity).GetField("_damageInvulnTimer",Hidden)!.SetValue(victim,(ushort)0);
                }
                void Fire(float height)
                {
                    Check(runtime.Optic(shooter,++sequence,1)==WeaponActionResult.Accepted,hunter+" scoped precision source");
                    Check(runtime.Fire(shooter,++sequence,victim.Position+Vector3.UnitY*height-(shooter.Position+new Vector3(0,.4f,0)))
                        ==WeaponActionResult.Accepted,hunter+" genuine accepted emitter");Step(2);
                }
                Prepare(200);Fire(head);
                Check(victim.Health==50&&runtime.LastImpact is {Headshot:true,Damage:150,Lethal:false},hunter+" real head collision 200→50");
                Step(4);var peak=scene.ModHeadHitPose(victim);
                Check(peak.Amount>.8f&&peak.Amount<=1,hunter+" accepted surviving headshot starts bounded overlay");
                Check(scene.GameState.BeamKills[shooter.SlotIndex,(int)BeamType.Imperialist]==0,hunter+" native Imperialist ledger unchanged");
                Prepare(200);Fire(.2f);
                Check(victim.Health==135&&runtime.LastImpact is {Headshot:false,Damage:65}&&scene.ModHeadHitPose(victim).Amount==0,
                    hunter+" real body hit pays65 without head overlay");
                Prepare(200,true);Fire(head);
                Check(victim.Health==200&&scene.ModHeadHitPose(victim).Amount==0,hunter+" actual protected head contact pays0/no overlay");
                Prepare(100);Fire(head);
                Check(victim.Health==0&&runtime.LastImpact is {Headshot:true,Lethal:true}&&scene.ModHeadHitPose(victim).Amount==0,
                    hunter+" accepted lethal headshot uses canonical death and cancels overlay");
                int before=scene.GameState.SpawnOrdinals[victim.SlotIndex];
                victim.Spawn(victim.Position,Vector3.UnitZ,Vector3.UnitY,victim.NodeRef,respawn:true);Step();
                Check(scene.GameState.SpawnOrdinals[victim.SlotIndex]>before&&scene.ModHeadHitPose(victim).Amount==0,
                    hunter+" real respawn does not revive prior life overlay");
                results.Add(new{hunter=hunter.ToString(),headDamage=150,bodyDamage=65,protectedDamage=0,peak=peak.Amount,
                    victimOwnedCustom=false,scope="genuine M7 emitter/collision/health; setup HP/placement/developer shooter grant explicit"});
                scene.DoCleanup();scene=null;
            }
            File.WriteAllText(Path.Combine(output,"damage-results.json"),JsonSerializer.Serialize(new{checks,seconds=watch.Elapsed.TotalSeconds,results},new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine($"PASS {checks} genuine seven-Hunter accepted damage/overlay checks");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
        finally{scene?.DoCleanup();graphics?.Dispose();window?.Dispose();MapAudit.ForceEveryone=force;RenderOptions.ShowCustomCosmetics=cosmetics;RenderOptions.CosmeticQuality=quality;Directory.SetCurrentDirectory(cwd);}
    }
}
