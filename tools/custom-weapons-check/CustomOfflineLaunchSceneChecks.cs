using System.Reflection;
using MphRead;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Diagnostics;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
using MphRead.Mods.Network;
using OpenTK.Windowing.GraphicsLibraryFramework;

// Real hidden RenderWindow + MatchStart.Begin + LoadScene. Headless suppresses
// game rendering/audio only; this still creates an actual desktop GL window.
// No menu click, Android head or public online entry point is qualified here.
internal static class CustomOfflineLaunchSceneChecks
{
    private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    private static readonly Action<KeyboardState,Keys,bool> Key=typeof(KeyboardState)
        .GetMethod("SetKeyState",Hidden|BindingFlags.Public)!.CreateDelegate<Action<KeyboardState,Keys,bool>>();
    internal static int Run(string data)
    {
        string cwd=Directory.GetCurrentDirectory(),filesRoot=GameFiles.Root;
        byte save=Menu.SaveSlot;bool force=MapAudit.ForceEveryone;
        RenderWindow? window=null;int checks=0;
        void Check(bool value,string label)
        {if(!value)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
        var loaded=typeof(RenderWindow).GetField("_sceneLoaded",Hidden)!;
        var pending=typeof(DemoRecorder).GetField("_pending",BindingFlags.Static|BindingFlags.NonPublic)!;
        bool recording=(bool)pending.GetValue(null)!;
        try
        {
            data=Path.GetFullPath(data);Directory.SetCurrentDirectory(data);GameFiles.Root=data;
            Check(GameFiles.Ready&&!NetSession.Active&&!DemoPlayback.IsActive&&Menu.SaveSlot==0,
                "explicit private configured files and no network/replay/save owner admit launch fixture");
            using var files=GameFiles.TryUseConfiguredFiles(data)??throw new Exception("configured-files lease unavailable");
            Headless.Enter();MapAudit.ForceEveryone=true;GamepadManager.ClearAll();
            GamepadContexts.Focused=true;GamepadContexts.MenuVisible=false;GamepadContexts.Capturing=false;GamepadContexts.TextEntryActive=false;
            var settings=new MenuSettings {RiftlineM7Available=true};
            var flagged=OfflineLaunch.Preview(settings,"MP3 PROVING GROUND",GameMode.Battle,Hunter.Samus,0,0,0);
            foreach(var kind in new[]{LaunchKind.Adventure,LaunchKind.Demo,LaunchKind.AimTrainer,LaunchKind.None})
            {
                // Null window proves the actual entry refuses before dispatch
                // or Scene construction; no reflection bypasses BeginCore.
                Check(!MatchStart.Begin(null!,settings,flagged with{Kind=kind})&&MatchStart.LastError is {Length:>0}
                    &&Menu.SaveSlot==0,"actual Begin rejects flagged unsupported kind before touching its window: "+kind);
            }
            pending.SetValue(null,true);
            try
            {
                Check(!MatchStart.Begin(null!,settings,flagged)&&MatchStart.LastError==CustomReplayBoundary.Error,
                    "actual Begin refuses active recording before constructing or loading a scene");
            }
            finally{pending.SetValue(null,recording);}
            window=RenderWindow.Create();
            Check(window.HasScene&&window.Scene.OfflineWeapons==null,"actual hidden RenderWindow starts with native-only empty scene");
            window.EndScene();
            using var observations=SceneLifetimeDiagnostics.Begin();
            Check(MatchStart.Begin(window,settings,flagged),"actual MatchStart.Begin accepts frozen offline availability: "+MatchStart.LastError);
            var scene=window.Scene;
            Check(window.HasScene&&(bool)loaded.GetValue(window)!&&scene.Room!=null
                &&observations.Snapshots.Any(s=>s.Generation==scene.LifetimeGeneration&&s.Stage==SceneLifetimeStage.Loaded),
                "real window LoadScene completed the native scene initialization before Begin returns");
            var runtime=scene.OfflineWeapons??throw new Exception("flagged Begin did not attach runtime");
            var human=scene.Players.Items[0];
            Check(runtime.IsEligible&&human.Health==0&&!human.IsBot&&runtime.State(human)==null,
                "flagged launch attaches captured-human runtime after loading but before native spawn or inventory grant");
            var input=typeof(Scene).GetField("_inputMode",Hidden)!;input.SetValue(scene,Enum.Parse(input.FieldType,"All"));
            foreach(var button in human.Controls.All){button.Type=ButtonType.Key;button.Key=Keys.Unknown;}
            human.Controls.Shoot.Key=Keys.Enter;scene.GameState.MatchTime=-1;scene.GameState.PointGoal=10000;
            Key(window.KeyboardState,Keys.Enter,true);
            for(int i=0;i<600&&!human.LoadFlags.TestFlag(LoadFlags.Spawned);i++)scene.OnSimulationFrame();
            Key(window.KeyboardState,Keys.Enter,false);scene.CameraSequences.Current?.End();
            for(int i=0;i<180;i++)scene.OnSimulationFrame();
            Check(human.Health>0&&runtime.State(human)==null&&runtime.InputProjection(human)==null
                &&runtime.Pickups is {Revision:0,PendingOutcomes:0}&&runtime.Pickups.Layout.Count==1&&runtime.AcceptedShots==0,
                "launcher-attached public pickup world leaves real spawned life empty with native selected");
            window.EndScene();
            Check(!window.HasScene&&!runtime.IsEligible&&scene.OfflineWeapons==null&&scene.CustomWeapons==null,
                "ending actual window scene releases launcher-attached runtime and registry");
            var native=flagged with{MatchRules=flagged.MatchRules with{RiftlineM7Available=false}};
            // Deliberately leave mutable menu true: the accepted frozen plan
            // controls this launch, rather than a later settings read.
            Check(settings.RiftlineM7Available&&MatchStart.Begin(window,settings,native),
                "actual native launch accepts frozen false flag despite mutable menu availability being true");
            var vanilla=window.Scene;
            Check((bool)loaded.GetValue(window)!&&vanilla.OfflineWeapons==null&&vanilla.CustomWeapons==null&&vanilla.CustomCombat==null,
                "unflagged actual Begin and LoadScene leave native gameplay entirely unattached");
            window.EndScene();

            // Explicit stale/invalid-owner fixture: an existing, not-yet-loaded
            // scene has a bot at slot0. Begin adds its ordinary players and
            // loads it; public activation must refuse the captured nonhuman.
            var invalid=window.BeginScene();window.AddPlayer(Hunter.Weavel);invalid.Players.Items[0].IsBot=true;
            long generation=invalid.LifetimeGeneration;
            Check(!MatchStart.Begin(window,settings,flagged)&&MatchStart.LastError?.Contains("human",StringComparison.OrdinalIgnoreCase)==true,
                "actual post-load activation refuses preseeded nonhuman owner and returns a useful error");
            var stages=observations.Snapshots.Where(s=>s.Generation==generation).Select(s=>s.Stage).ToArray();
            int loadedIndex=Array.IndexOf(stages,SceneLifetimeStage.Loaded),cleanupIndex=Array.IndexOf(stages,SceneLifetimeStage.CleanupComplete);
            Console.WriteLine("LAUNCH FAILURE DIAG stages="+string.Join(',',stages)+" error="+MatchStart.LastError);
            Check(loadedIndex>=0&&cleanupIndex>loadedIndex&&stages.Contains(SceneLifetimeStage.VisualResourcesReleased)
                &&!window.HasScene&&!(bool)loaded.GetValue(window)!&&invalid.OfflineWeapons==null&&invalid.CustomWeapons==null,
                "activation failure occurs after real LoadScene and EndScene fully cleans up the loaded failed scene");
            Check(MatchStart.Begin(window,settings,native)&&window.Scene.LifetimeGeneration!=generation
                &&window.Scene.OfflineWeapons==null&&MatchStart.LastError==null,
                "same actual window recovers after failed activation with a fresh native scene and cleared error");
            window.EndScene();
            Console.WriteLine($"PASS {checks} actual RenderWindow offline launch checks; hidden native window/headless simulation, no menu-click/Android/online qualification");
            return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
        finally
        {
            pending.SetValue(null,recording);NetSession.Stop();window?.EndScene();window?.Dispose();
            MapAudit.ForceEveryone=force;Menu.SaveSlot=save;GameFiles.Root=filesRoot;GamepadManager.ClearAll();Directory.SetCurrentDirectory(cwd);
        }
    }
}
