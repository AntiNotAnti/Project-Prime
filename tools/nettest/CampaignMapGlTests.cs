using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.Campaign;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.NetTest;

// Explicit owned-source GL diagnostic. Uses the actual native pause owner,
// NAV models, collision reader, transient render items, shaders and HUD.
// Captures are private derivatives: inspect locally, then delete the PNGs.
internal static class CampaignMapGlTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly int[] ExploredRooms = { 45, 48, 49, 51 };
    private static readonly Stopwatch Deadline = new();
    private static int checks;
    private static object Get(object owner, string name) => owner.GetType().GetField(name, Hidden)!.GetValue(owner)!;
    private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Hidden)!.SetValue(owner, value);
    private static void Check(bool ok, string why) { checks++; if (!ok) throw new InvalidOperationException(why); }
    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string HashFile(string path) { using var stream = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
    private static void Bound() { if (Deadline.Elapsed > TimeSpan.FromSeconds(120)) throw new TimeoutException("Campaign map GL exceeded its 120-second wall budget."); }

    internal static int Run(string[] args)
    {
        if (args.Length != 3 || !Directory.Exists(args[1]) || Directory.Exists(args[2]) || File.Exists(args[2]))
        { Console.Error.WriteLine("Usage: --campaign-map-gl <owned extracted AMHE1> <fresh private output directory>"); return 2; }
        string assets = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
        string root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "prime-campaign-map-gl-" + Guid.NewGuid().ToString("N"));
        string cwd = Directory.GetCurrentDirectory();
        string? oldData = Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA");
        var cases = new List<object>(); var pictures = new List<object>();
        Dictionary<string,string>? sourceBefore = null, sourceAfter = null;
        string status = "FAIL", failure = "", renderer = "", version = "";
        bool oldFocus = GamepadContexts.Focused, oldDetail = false; byte oldSlot = Menu.SaveSlot;
        SaveWhen oldSave = Menu.NeededSave; bool oldSkip = Cheats.SkipPlanetIntros;
        try
        {
            Directory.CreateDirectory(root); Directory.CreateDirectory(output);
            Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA", root); Directory.SetCurrentDirectory(root);
            Check(!Headless.Active && !NetSession.Active && !DemoPlayback.IsActive, "fresh ordinary offline GL process required");
            oldDetail = LauncherPrefs.DetailedCampaignMap;
            Check(!oldDetail, "fresh isolated settings retain default OFF");
            Paths.MphKey = "AMHE1"; Paths.SetPath(Paths.MphKey, assets);
            sourceBefore = SourceHashes(assets);
            Menu.SaveSlot = 0; Menu.NeededSave = SaveWhen.Never; Cheats.SkipPlanetIntros = true;
            GamepadContexts.Focused = true; GamepadManager.ClearAll(); PointerDevice.Reset();
            checks = 0; Deadline.Restart();
            foreach (var size in new[] { new Vector2i(800,600), new Vector2i(960,540) })
            {
                Bound();
                var settings = DesktopGlContext.Settings(background:true);
                settings.ClientSize=size; settings.StartVisible=true; settings.StartFocused=false;
                settings.Title="Project Prime private native campaign map diagnostic";
                using var window = new NativeWindow(settings);
                using var graphics = new DesktopGraphicsSession(window);
                GLFW.SwapInterval(0);
                renderer=GL.GetString(StringName.Renderer); version=GL.GetString(StringName.Version);
                RunAspect(window, size, root, output, cases, pictures);
            }
            sourceAfter=SourceHashes(assets);
            Check(sourceBefore.Count==sourceAfter.Count && sourceBefore.All(p=>sourceAfter.GetValueOrDefault(p.Key)==p.Value), "relevant owned NAV/collision/model inputs unchanged");
            status="PASS"; return 0;
        }
        catch(Exception error) { failure=error.ToString();Console.Error.WriteLine("CAMPAIGN MAP GL FAIL "+failure);return 1; }
        finally
        {
            LauncherPrefs.DetailedCampaignMap=oldDetail; GamepadContexts.Focused=oldFocus;
            Menu.SaveSlot=oldSlot;Menu.NeededSave=oldSave;Cheats.SkipPlanetIntros=oldSkip;
            FrameTiming.Reset();PointerDevice.Reset();Read.ClearCache();
            Directory.SetCurrentDirectory(cwd);Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA",oldData);
            if(Directory.Exists(root))Directory.Delete(root,true);
            if(Directory.Exists(output))File.WriteAllText(Path.Combine(output,"receipt.json"),JsonSerializer.Serialize(new {
                schema=1,status,failure,checks,renderer,version,
                binarySha256=HashFile(typeof(Scene).Assembly.Location),sourceBefore,sourceAfter,cases,pictures,
                temporarySaveContextDeleted=!Directory.Exists(root),
                scope="Owned native Scene/NAV/collision/pause-map GL; scripted keyboard input and virtual host cadences. No physical input, audio, display-Hz, Android, networking or gameplay exploration claim. PNGs are private derivatives; inspect and delete after the run."
            },new JsonSerializerOptions{WriteIndented=true})+Environment.NewLine);
        }
    }

    private static void RunAspect(NativeWindow window,Vector2i requested,string root,string output,List<object> cases,List<object> pictures)
    {
        var keyboard=SyntheticInput.CreateKeyboard();
        var key=typeof(KeyboardState).GetMethod("SetKeyState",Hidden|BindingFlags.Public)!.CreateDelegate<Action<KeyboardState,Keys,bool>>();
        Scene? scene=null;
        try
        {
            LauncherPrefs.DetailedCampaignMap=false;
            scene=new Scene(window.FramebufferSize,keyboard,SyntheticInput.CreateMouse(),_=>{},()=>{});
            scene.AddPlayer(Hunter.Samus);scene.AddRoom("UNIT2_LAND",GameMode.SinglePlayer,playerCount:1);
            MphRead.Sound.Sfx.Load(scene);
            scene.OnLoad();scene.OnResize();
            var player=scene.Players.Main;int lastDoorDraws=0;
            foreach(var control in player.Controls.All){control.Type=ButtonType.Key;control.Key=Keys.Unknown;}
            player.Controls.Pause.Key=Keys.Tab;player.Controls.Shoot.Key=Keys.Enter;
            player.Controls.MoveRight.Key=Keys.D;player.Controls.AimRight.Key=Keys.Right;player.Controls.AimDown.Key=Keys.Down;
            player.Controls.MouseAim=false;player.Controls.KeyboardAim=false;
            key(keyboard,Keys.Enter,true);
            for(int i=0;i<180 && !player.LoadFlags.TestFlag(LoadFlags.Spawned);i++)Step();
            key(keyboard,Keys.Enter,false);scene.CameraSequences.Current?.End();
            for(int i=0;i<90;i++)Step();
            Check(scene.RoomId==45&&scene.GameState.SinglePlayer&&scene.CameraMode==CameraMode.Player,"actual Celestial Gateway single-player scene/camera loaded");
            var save=scene.GameState.StorySave;
            Array.Clear(save.VisitedRooms);Array.Clear(save.VisitedConnectors);
            foreach(int id in ExploredRooms)save.SetVisitedRoom(id);
            save.SetVisitedConnector(0,scene.AreaId);save.SetVisitedConnector(1,scene.AreaId);
            byte[] explored=(byte[])save.VisitedRooms.Clone();int[] connectors=(int[])save.VisitedConnectors.Clone();
            string fixture=Path.Combine(root,"fixture-"+requested.X+".json");
            File.WriteAllBytes(fixture,PostMatchSaveTests.LegacySave(save));
            string fixtureHash=HashFile(fixture);
            TogglePause();Check(scene.GameState.MenuPause,"native Pause binding enters actual campaign pause menu");
            Check(Get(player,"_navMapDetail")==null,"default OFF creates no detail loader");
            string selected=Metadata.GetRoomById(48)!.Name;
            Select(selected);
            int off=Draw("off");Check(off==0,"default OFF submits native NAV meshes without detail polygons");
            TogglePause();LauncherPrefs.DetailedCampaignMap=true;TogglePause();
            var load=(CampaignMapDetailLoad)Get(player,"_navMapDetail");
            WaitLoad(load);
            var ready=load.Completion.Result.Where(p=>p.Value.Ready).ToDictionary(p=>p.Key,p=>p.Value,StringComparer.OrdinalIgnoreCase);
            Check(ready.Count>0,"at least one real explored room has Ready collision detail; fallback is recorded per room");
            Check(ready.ContainsKey(selected),"owned Data Shrine01 detail is Ready for selected-room and hidden-cache controls");
            Select(selected);int on=Draw("on-detail");
            Check(on==Expected(),"actual queued Ngon count equals Ready explored-room polygon count");
            Check(save.VisitedRooms.SequenceEqual(explored)&&save.VisitedConnectors.SequenceEqual(connectors),"OFF/ON native menu draws leave fixture exploration bits untouched");
            var pan=new List<object>();Vector3? reference=null;
            foreach(int hz in new[]{60,120,144})
            {
                Select(selected);Set(player,"_navPanOffset",Vector3.Zero);Set(player,"_navPanTimer",0f);
                Set(player,"_navDrawRotX",0f);Set(player,"_navDrawRotY",28.125f);
                FrameTiming.Reset();int ticks=0,draws=0,held=0;key(keyboard,Keys.D,true);
                while(ticks<60)
                {
                    Bound();int due=FrameTiming.Advance(1d/hz);
                    for(int n=0;n<due&&ticks<60;n++){scene.OnSimulationFrame();ticks++;if(player.Controls.MoveRight.IsDown)held++;}
                    Draw(null);draws++;Check(draws<400,"bounded virtual cadence picture loop");
                }
                key(keyboard,Keys.D,false);Step();
                var offset=(Vector3)Get(player,"_navPanOffset");
                Check(held==60&&offset.Length>1,"held native movement binding pans map on all60 fixed steps");
                if(reference is{} prior)Check((offset-prior).Length<.001f,"native map pan is fixed-step invariant across virtual60/120/144 host cadence");else reference=offset;
                Draw("pan-"+hz);pan.Add(new{virtualHostHz=hz,fixedTicks=ticks,pictures=draws,heldInputTicks=held,offset=V(offset)});
            }
            int hiddenId=ExploredRooms.First(id=>Metadata.GetRoomById(id)!.Name.Equals(selected,StringComparison.OrdinalIgnoreCase));
            Select(selected);Set(player,"_navPanTimer",0f);
            Set(player,"_navDrawRotX",0f);Set(player,"_navDrawRotY",28.125f);
            Draw("before-hidden");int beforeHiddenDoors=lastDoorDraws;
            Check(beforeHiddenDoors>0,"visible selected room submits native door annotations");
            int hiddenPolygons=ready[selected].Polygons.Count;ClearVisited(save,hiddenId);
            Check(load.TryGetRoom(selected,out _),"hidden recheck retains already Ready loader snapshot");
            int hidden=Draw("hidden-cached");int hiddenDoors=lastDoorDraws;
            Check(hiddenDoors==0,"hidden retained selection cannot submit native door annotations");
            Check(hidden==Expected()&&hidden==on-hiddenPolygons,"native visibility gate rechecks cached detail after fixture visited bit clears");
            TogglePause();TogglePause();var reopened=(CampaignMapDetailLoad)Get(player,"_navMapDetail");WaitLoad(reopened);
            Check(!reopened.Completion.Result.ContainsKey(selected),"new pause opening does not request a hidden room");
            Check(((Node)Get(player,"_navMapDrawNode")).Name.Equals(Metadata.GetRoomById(scene.RoomId)!.Name,StringComparison.OrdinalIgnoreCase),
                "native reopening selects the current visited room");
            Draw(null);int reopenedVisitedDoors=lastDoorDraws;
            Check(reopenedVisitedDoors>0,"reopened current visited room retains native door annotations");
            // Re-select the hidden diagnostic node explicitly before testing its annotation gate.
            Select(selected);Draw("hidden-reopened");
            Check(lastDoorDraws==0,"reopened explicitly hidden selection cannot submit native door annotations");
            var fan=new List<object>();
            foreach(int id in new[]{49,51})
            {
                string name=Metadata.GetRoomById(id)!.Name;var node=Nav(name);Vector3 origin=node.Animation.Row3.Xyz;
                var sample=new Vector3(3,4,5);Vector3 actual=CampaignMapDetail.RoomToMap(name,sample,origin,markerLift:1);
                Vector3 expected=new(sample.X+origin.X,sample.Y*(id==49?2.05f:1.305f)+origin.Y+1,sample.Z+origin.Z);
                Check((actual-expected).Length<.0001f,"Fan Room marker transform preserves native height factor, NAV origin and lift");
                fan.Add(new{roomId=id,name,navOrigin=V(origin),sample=V(sample),marker=V(actual),scope="native NAV origin plus marker-transform arithmetic; player remains in room45"});
            }
            cases.Add(new{requestedWidth=requested.X,requestedHeight=requested.Y,framebufferWidth=scene.Size.X,framebufferHeight=scene.Size.Y,
                roomId=scene.RoomId,fixtureHash,exploredRoomIds=ExploredRooms,connectorArea=scene.AreaId,connectorBits=connectors,
                selectedRoomId=48,offDetailDraws=off,onDetailDraws=on,hiddenCachedDraws=hidden,hiddenRoomId=hiddenId,
                beforeHiddenDoorDraws=beforeHiddenDoors,hiddenCachedDoorDraws=hiddenDoors,reopenedVisitedDoorDraws=reopenedVisitedDoors,
                results=load.Completion.Result.Select(p=>new{room=p.Key,p.Value.Ready,p.Value.Reason,polygons=p.Value.Polygons.Count}),pan,fan});
            TogglePause();Check(!scene.GameState.MenuPause,"native Pause binding exits map");
            Check(Get(player,"_navMapDetail")==null,"native Pause exit retires detail owner");
            Check(load.Completion.IsCompletedSuccessfully&&reopened.Completion.IsCompletedSuccessfully,
                "retired completed detail snapshots remain available to the diagnostic");
            Check(Draw(null)==0,"post-exit native frame cannot draw retired detail snapshots");
            TogglePause();
            Check(scene.GameState.MenuPause,"fresh actual pause is active before launcher-style cleanup");
            var cleanupLoad=(CampaignMapDetailLoad)Get(player,"_navMapDetail");WaitLoad(cleanupLoad);
            string currentRoom=Metadata.GetRoomById(scene.RoomId)!.Name;
            Check(cleanupLoad.TryGetRoom(currentRoom,out _),"fresh pause has visible Ready detail before cleanup");
            scene.DoCleanup();
            Check(Get(player,"_navMapDetail")==null,"Scene.DoCleanup retires pause detail without normal Pause exit");
            Check(!cleanupLoad.TryGetRoom(currentRoom,out _),"launcher-style cleanup refuses held completed detail snapshot");

            void Step(){Bound();scene.OnSimulationFrame();Draw(null);}
            void TogglePause(){key(keyboard,Keys.Tab,true);Step();key(keyboard,Keys.Tab,false);Step();}
            void WaitLoad(CampaignMapDetailLoad owner)
            {var wait=Stopwatch.StartNew();while(!owner.Completion.IsCompleted&&wait.Elapsed<TimeSpan.FromSeconds(10)){Step();Thread.Sleep(1);}Check(owner.Completion.IsCompletedSuccessfully,"bounded owned-source detail worker completes");}
            int Expected()=>load.Completion.Result.Where(p=>p.Value.Ready&&ExploredRooms.Any(id=>save.CheckVisitedRoom(id)&&Metadata.GetRoomById(id)!.Name.Equals(p.Key,StringComparison.OrdinalIgnoreCase))).Sum(p=>p.Value.Polygons.Count);
            Node Nav(string name)=>((ModelInstance?[])Get(player,"_navMapModels")).Where(m=>m!=null).SelectMany(m=>m!.Model.Nodes).First(n=>n.ParentIndex==0&&n.Name.Equals(name,StringComparison.OrdinalIgnoreCase));
            void Select(string name)
            {Node node=Nav(name);var model=((ModelInstance?[])Get(player,"_navMapModels")).First(m=>m!=null&&m.Model.Nodes.Contains(node))!;Node center=node;for(int i=node.ChildIndex;i!=-1;i=model.Model.Nodes[i].NextIndex)if(model.Model.Nodes[i].Name.StartsWith("cent",StringComparison.Ordinal)){center=model.Model.Nodes[i];break;}typeof(PlayerEntity).GetMethod("SetNavMapDrawNode",Hidden)!.Invoke(player,new object[]{node,center});Set(player,"_navPanOffset",Vector3.Zero);}
            int Draw(string? label)
            {
                Bound();scene.OnDrawFrame();
                var queued=new[]{"_decalItems","_nonDecalItems","_translucentItems"}.SelectMany(n=>(List<RenderItem>)Get(scene,n)).Distinct().ToArray();
                var items=queued.Where(i=>i.Type==RenderItemType.Ngon).ToArray();
                var doorIds=((ModelInstance)Get(player,"_navDoorModel")).Model.Meshes.Select(m=>m.ListId).Where(id=>id!=0).ToHashSet();
                lastDoorDraws=queued.Count(i=>i.Type==RenderItemType.Mesh&&doorIds.Contains(i.ListId));
                int count=items.Length,vertexCount=items.Sum(i=>i.ItemCount);
                Check(scene.OnRenderFrame(),"actual native Scene render remains active");
                if(label!=null)
                {string path=Path.Combine(output,requested.X+"x"+requested.Y+"-"+label+".png");Check(ScreenCapture.SaveWindow(scene,path),"private native map/HUD framebuffer captured before swap");pictures.Add(new{file=Path.GetFileName(path),sha256=HashFile(path),detailDraws=count,vertices=vertexCount,doorDraws=lastDoorDraws});}
                DesktopGraphicsSession.Present(window);scene.AfterRenderFrame();return count;
            }
        }
        finally
        {
            if(scene!=null){if(scene.GameState.MenuPause){scene.GameState.UnpauseMenu();scene.Players.Main.EndMenuPauseHud();}Menu.NeededSave=SaveWhen.Never;MomentumSceneTests.Release(scene);}
        }
    }
    private static void ClearVisited(StorySave save,int id){int index=id-27;save.VisitedRooms[index/8]&=(byte)~(1<<(index%8));}
    private static float[] V(Vector3 value)=>new[]{value.X,value.Y,value.Z};
    private static Dictionary<string,string> SourceHashes(string root)
    {
        var paths=Metadata.NavMapModelNames.Select(n=>Metadata.HudModels[n].ModelPath).Concat(ExploredRooms.Select(id=>Metadata.GetRoomById(id)!.CollisionPath)).Append(Metadata.GetRoomById(45)!.ModelPath).Distinct().OrderBy(p=>p,StringComparer.Ordinal);
        return paths.ToDictionary(p=>p,p=>HashFile(Paths.Combine(root,p)),StringComparer.Ordinal);
    }
}
