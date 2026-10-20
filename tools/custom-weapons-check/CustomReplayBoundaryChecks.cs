using System.Reflection;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Input;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;

// Admission/lifetime tests only. Pending writer flags below model real recorder
// admission without creating disk writers, custom packets or combat certificates.
internal static class CustomReplayBoundaryChecks
{
    private const BindingFlags Hidden = BindingFlags.Static | BindingFlags.NonPublic;
    private sealed class UnusedOracle : ICustomSourceAdmission
    {
        public CustomSourceDecision Inspect(in CustomWeaponAction action,out CustomSourceCertificate source)
        { source=default; throw new InvalidOperationException("No combat source is inspected by replay admission."); }
    }
    internal static void Run(Action<bool,string> check)
    {
        var originalState = GameState.Current;
        FieldInfo demo = typeof(DemoRecorder).GetField("_pending",Hidden)!;
        FieldInfo server = typeof(ServerReplayRecorder).GetField("_pending",Hidden)!;
        FieldInfo clip = typeof(DemoClip).GetField("_pendingPath",Hidden)!;
        object? oldDemo=demo.GetValue(null),oldServer=server.GetValue(null),oldClip=clip.GetValue(null);
        var scenes=new List<Scene>();
        Scene NewScene()
        {
            var scene=new Scene(new(32,32),SyntheticInput.CreateKeyboard(),SyntheticInput.CreateMouse(),_=>{},()=>{},initializeRuntime:false);
            scene.GameState.Mode=GameMode.Battle;
            scene.GameState.BalancedMode=scene.GameState.EnhancedHunters=false;
            scene.GameState.InstaGib=scene.GameState.OneInTheChamber=scene.GameState.NoImperialist=false;
            scenes.Add(scene);return scene;
        }
        try
        {
            CustomReplayBoundary.ResetMatch();
            check(!ReplayCapture.LiveRecordingActive,"replay boundary fixture starts without actual writer/pending clip");
            var native=NewScene();
            check(ReplayCapture.CanStartLiveRecording(out _)&&!CustomReplayBoundary.IsBlocked(native),"fresh native scene remains recordable");
            foreach(var (field,value) in new (FieldInfo,object)[]{(demo,true),(server,true),(clip,"synthetic-admission-only")})
            {
                field.SetValue(null,value);
                try
                {
                    check(!CustomReplayBoundary.TryActivateClient(out string? error)&&error==CustomReplayBoundary.Error,
                        "existing recorder/clip prevents client custom activation before ACK");
                    check(!CustomReplayBoundary.IsBlocked(),"refused client activation does not select custom scene");
                    foreach(Action enable in new Action[]{()=>native.EnableDeveloperRifle(true),
                        ()=>native.EnableDeveloperCustomCombat(true,new UnusedOracle()),
                        ()=>native.EnableDeveloperEngineCustomCombat(true,null!)})
                    {
                        bool denied=false;
                        try { enable(); } catch(InvalidOperationException ex) { denied=ex.Message==CustomReplayBoundary.Error; }
                        check(denied&&native.OfflineWeapons==null&&native.CustomCombat==null&&native.CustomWeapons==null,
                            "custom activation refuses pending recording before constructing runtime/registry");
                    }
                }
                finally { field.SetValue(null,field==clip?null:false); }
            }
            check(ReplayCapture.CanStartLiveRecording(out _),"failed custom activation preserves native recording admission");
            bool explicitOptIn=false;
            demo.SetValue(null,true);
            try { native.EnableDeveloperRifle(false); }
            catch(InvalidOperationException ex) { explicitOptIn=ex.Message!=CustomReplayBoundary.Error; }
            finally { demo.SetValue(null,false); }
            check(explicitOptIn&&!CustomReplayBoundary.IsBlocked(),"disabled developer opt-in does not select or invoke custom recording policy");

            typeof(KillCam).GetField("_finalRequested",Hidden)!.SetValue(null,true);
            check(CustomReplayBoundary.TryActivateClient(out var acceptedError)&&acceptedError==null,
                "idle client may select custom replica before first consumer commit");
            check(!KillCam.FinalPresentationPending,"custom replica selection clears pending killcam");
            check(CustomReplayBoundary.TryActivateClient(out _)&&CustomReplayBoundary.IsBlocked(),"duplicate custom selection is idempotent");
            check(!ReplayCapture.CanStartLiveRecording(out var reason)&&reason==CustomReplayBoundary.Error&&!ReplayCapture.NeedsWorld,
                "selected custom client denies archive and private replay-world construction");
            string? demoPath=DemoRecorder.CurrentPath,serverPath=ServerReplayRecorder.CurrentPath,clipPath=DemoClip.LastSavedPath;
            check(!DemoRecorder.Start()&&!DemoRecorder.IsRecording,"actual recording entry refuses selected custom client");
            check(!ServerReplayRecorder.Start(new ReplayMetadata())&&!ServerReplayRecorder.IsRecording
                &&ServerReplayRecorder.LastError==CustomReplayBoundary.Error,"actual server recording entry refuses before policy or metadata");
            check(DemoClip.Save()==null&&!DemoClip.IsSaving&&DemoClip.LastError==CustomReplayBoundary.Error,
                "actual clip entry refuses selected custom client");
            check(DemoRecorder.CurrentPath==demoPath&&ServerReplayRecorder.CurrentPath==serverPath&&DemoClip.LastSavedPath==clipPath,
                "refused custom recording entries leave output paths unchanged");
            var context=(KillcamContext)typeof(KillCam).GetMethod("Context",Hidden)!.Invoke(null,new object?[]{native})!;
            check(!context.PersonalEnabled&&!context.FinalEnabled,"both killcam policies are disabled for custom client scene");
            KillCam.BeginFinal(123);
            check(!KillCam.FinalPresentationPending&&KillCam.Presentation(native)==null,"custom scene cannot queue or present final killcam");
            typeof(NetSession).GetMethod("ResetCustomReplica",Hidden)!.Invoke(null,null);
            check(CustomReplayBoundary.IsBlocked()&&!ReplayCapture.CanStartLiveRecording(out _),
                "readiness/consumer loss cannot reopen native recording in same custom room");
            NetSession.ResetMatchState();
            check(CustomReplayBoundary.IsBlocked(native)&&!ReplayCapture.CanStartLiveRecording(out _),
                "match reset cannot make retained custom scene recordable during loading transition");
            var nextMatch=NewScene();
            check(!CustomReplayBoundary.IsBlocked(nextMatch)&&ReplayCapture.CanStartLiveRecording(out _),
                "new native match scene does not inherit previous scene marker");
            check(CustomReplayBoundary.TryActivateClient(out _)&&nextMatch.CustomReplayUnsupported,
                "actual client activation marks foreground scene durably");
            NetSession.Stop();
            check(CustomReplayBoundary.IsBlocked(nextMatch),"actual disconnect preserves old custom scene fence");
            var afterDisconnect=NewScene();
            check(!CustomReplayBoundary.IsBlocked(afterDisconnect)&&ReplayCapture.CanStartLiveRecording(out _),
                "actual disconnect followed by fresh offline scene restores native recording");
            var foreground=GameState.Current;var foregroundOwner=foreground.Owner;
            try
            {
                foreground.Owner=null;
                check(CustomReplayBoundary.TryActivateClient(out _)&&CustomReplayBoundary.IsBlocked(),
                    "ownerless authenticated client admission retains bounded stream fallback");
                NetSession.ResetMatchState();
                check(!CustomReplayBoundary.IsBlocked(),"fresh match clears ownerless-only fallback");
                check(CustomReplayBoundary.TryActivateClient(out _),"ownerless selection can precede actual scene binding");
                foreground.Owner=foregroundOwner;
                check(CustomReplayBoundary.IsBlocked(afterDisconnect)&&afterDisconnect.CustomReplayUnsupported,
                    "first actual scene adopts ownerless selection durably");
                CustomReplayBoundary.ResetMatch();
                check(CustomReplayBoundary.IsBlocked(afterDisconnect),"fallback reset cannot erase adopted scene marker");
            }
            finally { foreground.Owner=foregroundOwner; }

            var offline=NewScene();var runtime=offline.EnableDeveloperRifle(true);
            check(CustomReplayBoundary.IsBlocked(offline)&&!ReplayCapture.CanStartLiveRecording(out _),
                "actual selected offline custom runtime blocks recording without live readiness");
            runtime.Dispose();
            check(!runtime.IsEligible&&CustomReplayBoundary.IsBlocked(offline)&&!ReplayCapture.CanStartLiveRecording(out _),
                "retired custom runtime remains unrecordable while selected in scene");
            CustomReplayBoundary.ResetMatch();
            check(CustomReplayBoundary.IsBlocked(offline),"resetting client marker cannot bypass authority/offline selected runtime");
            var fresh=NewScene();
            check(!CustomReplayBoundary.IsBlocked(fresh)&&ReplayCapture.CanStartLiveRecording(out _),
                "fresh native scene does not inherit retired custom runtime fence");
        }
        finally
        {
            demo.SetValue(null,oldDemo);server.SetValue(null,oldServer);clip.SetValue(null,oldClip);
            CustomReplayBoundary.ResetMatch();KillCam.Reset();
            foreach(var scene in scenes) scene.DoCleanup();
            GameState.Current=originalState;
        }
    }
}
