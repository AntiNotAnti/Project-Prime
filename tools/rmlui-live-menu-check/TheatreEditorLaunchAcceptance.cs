using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

internal static class TheatreEditorLaunchAcceptance
{
    internal static void Run()
    {
        int checks=0;
        void Check(bool value,string message)
        {if(!value)throw new InvalidOperationException(message);checks++;}
        var service=new Stub();
        using(var controller=new RmlUiMenuController(service))
        {
            Open(controller,service,false);
            controller.Dispatch(RmlUiIntentKind.TheatreAction,0,theatreSearch:"NO MATCHING FIXTURE");
            Check(!Action(controller,18)&&service.TheatreValidations==0&&service.LegacyTheatreHandoffs==0,
                "editor requires an exact selected entry and never opens a legacy route");
            controller.Dispatch(RmlUiIntentKind.TheatreAction,23);
            Check(controller.Dispatch(RmlUiIntentKind.TheatreEntry,2)&&!Action(controller,18)&&service.TheatreValidations==0,
                "unplayable metadata cannot enter the native editor preflight");
            controller.Dispatch(RmlUiIntentKind.TheatreEntry,0);Prepare(service);
            var selected=service.TheatreRows[0];
            Check(Action(controller,18,new(ImportPath:"/untrusted/intent-path.ppdemo"))&&controller.Busy
                &&service.LastValidatedReplay==selected&&service.LastValidatedReplay.VirtualWitness==selected.VirtualWitness
                &&service.ReplayLaunches==0,
                "editor captures exact selected descriptor/source facts for preflight without optimistic launch");
            Check(!Action(controller,18)&&!Action(controller,6)&&!controller.Dispatch(RmlUiIntentKind.TheatreEntry,1),
                "editor preflight is single flight and fences selected source changes");
            service.TheatreValidationPending.SetResult("");Wait(controller,()=>service.ReplayLaunches==1);
            Check(service.LastReplayOpenEditor&&service.LastReplay==selected&&service.LegacyTheatreHandoffs==0
                &&!Action(controller,18)&&!Action(controller,6),
                "verified editor launch carries selected entry and editor flag once with no legacy fallback");
        }
        service=new Stub();
        using(var controller=new RmlUiMenuController(service))
        {
            Check(!controller.ReplayPlaybackClosed(),"playback return without a library is a no-op");
            Open(controller,service);
            Check(!controller.ReplayPlaybackClosed(),"playback return before a successful launch is a no-op");
            Check(Action(controller,1)&&controller.Dispatch(RmlUiIntentKind.TheatreAction,0,theatreSearch:"OTHER")
                &&controller.Dispatch(RmlUiIntentKind.TheatreEntry,0),
                "return fixture captures a selected recording in a nondefault filter and search");
            var before=controller.Tick();
            Prepare(service);Check(Action(controller,18),"filtered recording can launch Editor");
            service.TheatreValidationPending.SetResult("");Wait(controller,()=>service.ReplayLaunches==1);
            Check(!Action(controller,6)&&!Action(controller,18)
                &&!controller.Dispatch(RmlUiIntentKind.TheatreEntry,0)
                &&!controller.Tick().Bindings["theatre_watch_enabled"].Boolean,
                "successful handoff stays fenced until explicit playback return");
            Check(controller.ReplayPlaybackClosed(),"explicit playback return releases the launch fence");
            var returned=controller.Tick();
            Check(new[]{"theatre_selected_title","theatre_filter_label","theatre_summary","theatre_page_number"}
                .All(key=>returned.Bindings[key].Text==before.Bindings[key].Text)
                &&returned.Bindings["theatre_filter_label"].Text=="FULL MATCHES"
                &&returned.Bindings["theatre_summary"].Text.StartsWith("1 MATCHING",StringComparison.Ordinal)
                &&returned.Bindings["theatre_watch_enabled"].Boolean&&returned.Bindings["theatre_edit_enabled"].Boolean,
                "return preserves selected recording, nondefault filter and search while enabling Watch and Editor");
            Check(!controller.ReplayPlaybackClosed(),"duplicate playback return is a no-op");
            Check(Action(controller,23)&&Enumerable.Range(0,13).All(_=>Action(controller,1))
                &&controller.Dispatch(RmlUiIntentKind.TheatreEntry,0),
                "returned library can clear search, restore all recordings and select another recording");
            var second=service.TheatreRows[0];
            Prepare(service);Check(Action(controller,6),"another recording starts fresh Watch validation");
            var pending=service.TheatreValidationPending;var token=service.LastReplayValidationToken;
            Check(!controller.ReplayPlaybackClosed()&&controller.Busy&&!token.IsCancellationRequested
                &&ReferenceEquals(pending,service.TheatreValidationPending)&&service.ReplayLaunches==1,
                "extra return during fresh validation cannot cancel or clear its newer work");
            pending.SetResult("");Wait(controller,()=>service.ReplayLaunches==2);
            Check(service.LastReplay==second&&!service.LastReplayOpenEditor,
                "second Watch launches the newly selected recording without inherited Editor state");
            Check(controller.ReplayPlaybackClosed(),"second playback returns to the same library controller");
            Prepare(service);Check(Action(controller,18),"same selected path can enter Editor again after return");
            service.TheatreValidationPending.SetResult("");Wait(controller,()=>service.ReplayLaunches==3);
            Check(service.LastReplay==second&&service.LastReplayOpenEditor,
                "same-path relaunch retains exact selected descriptor and fresh Editor intent");
            controller.Dispose();
            Check(!controller.ReplayPlaybackClosed(),"disposed library ignores a late playback return");
        }
        foreach(bool throws in new[]{false,true})
        {
            service=new Stub();using var controller=new RmlUiMenuController(service);Open(controller,service);Prepare(service);
            Action(controller,18);
            if(throws)service.TheatreValidationPending.SetException(new InvalidDataException("SYNTHETIC CORRUPT REPLAY"));
            else service.TheatreValidationPending.SetResult("SYNTHETIC MAP OR PROTOCOL MISMATCH");
            Wait(controller,()=>!controller.Busy);
            Check(service.ReplayLaunches==0&&service.ReplayLaunchAttempts==0&&service.LegacyTheatreHandoffs==0,
                "failed or malformed preflight never launches a replay or falls back to Studio");
            Prepare(service);Check(Action(controller,6),"ordinary Watch can retry after editor verification refusal");
            service.TheatreValidationPending.SetResult("");Wait(controller,()=>service.ReplayLaunches==1);
            Check(!service.LastReplayOpenEditor,"failed editor preflight cannot leak its presentation flag into ordinary Watch");
        }
        service=new Stub{IgnoreReplayValidationCancellation=true};
        using(var controller=new RmlUiMenuController(service))
        {
            Open(controller,service);Prepare(service);Action(controller,18);
            var old=service.TheatreValidationPending;var token=service.LastReplayValidationToken;
            Check(Action(controller,10)&&token.IsCancellationRequested&&!controller.Busy,
                "cancel retires editor preflight and requests cancellation on its original validation owner");
            Prepare(service);Action(controller,6);var fresh=service.TheatreValidationPending;
            old.SetResult("");Drain(controller);
            Check(controller.Busy&&service.ReplayLaunches==0,"late cancelled editor validation cannot launch or retire newer Watch preflight");
            fresh.SetResult("");Wait(controller,()=>service.ReplayLaunches==1);
            Check(!service.LastReplayOpenEditor&&service.LegacyTheatreHandoffs==0,
                "newer ordinary Watch retains its own false editor flag after stale editor completion");
        }
        service=new Stub{IgnoreReplayValidationCancellation=true};
        using(var controller=new RmlUiMenuController(service))
        {
            Open(controller,service);Prepare(service);Action(controller,18);
            var late=service.TheatreValidationPending;var token=service.LastReplayValidationToken;
            Check(controller.Dispatch(RmlUiIntentKind.Navigate,(int)RmlUiRouteArgument.Home)&&token.IsCancellationRequested,
                "leaving archive retires original editor preflight");
            late.SetResult("");Drain(controller);
            Check(!controller.TheatreOpen&&service.ReplayLaunches==0&&service.LegacyTheatreHandoffs==0,
                "late route-away validation cannot reopen editor, replay or legacy UI");
            Open(controller,service);Prepare(service);Action(controller,6);service.TheatreValidationPending.SetResult("");Wait(controller,()=>service.ReplayLaunches==1);
            Check(!service.LastReplayOpenEditor,"reopened archive does not inherit retired editor flag");
        }
        foreach(bool virtualChanged in new[]{false,true})
        {
            service=new Stub();using var controller=new RmlUiMenuController(service);Open(controller,service);Prepare(service);Action(controller,18);
            var expected=service.LastValidatedReplay;
            service.TheatreRows[0]=virtualChanged?expected with{VirtualWitness=expected.VirtualWitness!.Value with{DescriptorSha256=new string('B',64)}}
                :expected with{Bytes=expected.Bytes+1,LastWriteTicks=expected.LastWriteTicks+1};
            service.TheatreValidationPending.SetResult("");Wait(controller,()=>!controller.Busy);
            Check(service.ReplayLaunchAttempts==1&&service.ReplayLaunches==0&&service.LastReplayOpenEditor
                &&service.LegacyTheatreHandoffs==0,
                "changed recording stamp or virtual descriptor owner refuses exact editor submission without fallback");
        }
        service=new Stub{ReplayEditorSupported=false};
        using(var controller=new RmlUiMenuController(service))
        {
            Open(controller,service);Prepare(service);Action(controller,18);service.TheatreValidationPending.SetResult("");Wait(controller,()=>!controller.Busy);
            Check(service.ReplayLaunchAttempts==1&&service.ReplayLaunches==0&&service.LegacyTheatreHandoffs==0,
                "explicit unsupported native editor launch refuses without opening legacy UI");
        }
        service=new Stub();IRmlUiMenuServices legacy=new LegacyOnlyReplayService(service);
        using(var controller=new RmlUiMenuController(legacy))
        {
            Open(controller,service);Prepare(service);Action(controller,18);service.TheatreValidationPending.SetResult("");Wait(controller,()=>!controller.Busy);
            Check(service.ReplayLaunches==0&&service.LegacyTheatreHandoffs==0,
                "actual default service overload refuses editor when frontend only implements legacy Watch");
            Prepare(service);Action(controller,6);service.TheatreValidationPending.SetResult("");Wait(controller,()=>service.ReplayLaunches==1);
            Check(service.LastReplay==service.LastValidatedReplay,
                "actual default service overload still delegates ordinary Watch to its existing launcher");
        }
        service=new Stub{IgnoreReplayValidationCancellation=true};
        var disposed=new RmlUiMenuController(service);Open(disposed,service);Prepare(service);Action(disposed,18);
        var retired=service.TheatreValidationPending;var retiredToken=service.LastReplayValidationToken;disposed.Dispose();
        retired.SetResult("");Thread.Sleep(30);
        Check(retiredToken.IsCancellationRequested&&service.ReplayLaunches==0&&service.LegacyTheatreHandoffs==0,
            "disposed archive rejects late editor validation without callbacks into any new scene");
        Console.WriteLine($"PASS {checks} native library selected/preflight/editor-flag/cancellation/unsupported launch checks; fake services only");
    }
    private static bool Action(RmlUiMenuController controller,int action,RmlUiReplayInputs inputs=default)
        =>controller.Dispatch(RmlUiIntentKind.TheatreAction,action,theatreInputs:inputs);
    private static void Prepare(Stub service)=>service.TheatreValidationPending=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Open(RmlUiMenuController controller,Stub service,bool select=true)
    {
        controller.Dispatch(RmlUiIntentKind.Navigate,(int)RmlUiRouteArgument.Home);
        service.TheatreRows=new[]{
            new RmlUiReplayEntry("/synthetic/selected.ppclip","SELECTED","fixture","Map",300,111,222,false,true,false,true)
                {VirtualWitness=new(new string('A',64),"/synthetic/source.ppdemo",333,444)},
            new RmlUiReplayEntry("/synthetic/other.ppdemo","OTHER","fixture","Map",200,555,666,false,false,false,true),
            new RmlUiReplayEntry("/synthetic/corrupt.ppdemo","UNPLAYABLE","fixture","Map",100,777,888,false,false,false,false)};
        service.TheatreReadPending=new(TaskCreationOptions.RunContinuationsAsynchronously);
        if(!controller.Dispatch(RmlUiIntentKind.Navigate,(int)RmlUiRouteArgument.Theatre))throw new InvalidOperationException("Archive fixture failed to open.");
        service.TheatreReadPending.SetResult(service.TheatreRows);Wait(controller,()=>!controller.Busy);
        if(select&&!controller.Dispatch(RmlUiIntentKind.TheatreEntry,0))throw new InvalidOperationException("Archive fixture failed to select.");
    }
    private static void Wait(RmlUiMenuController controller,Func<bool> predicate)
    {for(int i=0;i<300;i++){controller.Tick();if(predicate())return;Thread.Sleep(10);}throw new TimeoutException("Native editor launch fixture did not settle.");}
    private static void Drain(RmlUiMenuController controller)
    {for(int i=0;i<20;i++){controller.Tick();Thread.Sleep(5);}}
    // Intentionally omits the new bool overload: calls exercise the ACTUAL default interface implementation.
    private sealed class LegacyOnlyReplayService(Stub inner):IRmlUiMenuServices
    {
        public HubSnapshot Hub()=>inner.Hub();public string BuildVersion()=>inner.BuildVersion();public RmlUiLobbyView Lobby()=>inner.Lobby();
        public Task<RmlUiDirectory> Browse(CancellationToken token)=>inner.Browse(token);
        public Task<RmlUiServerCandidate?> QuickMatch(CancellationToken token)=>inner.QuickMatch(token);
        public Task<RmlUiJoinResult> Join(string endpoint,string name,CancellationToken token)=>inner.Join(endpoint,name,token);
        public Task<RmlUiJoinResult> CreateLobby(LobbyCreationRequest request,CancellationToken token)=>inner.CreateLobby(request,token);
        public bool AcceptJoin(LaunchPlan plan)=>inner.AcceptJoin(plan);public bool OpenCreateLobby()=>inner.OpenCreateLobby();
        public bool NavigateTo(RmlUiRouteArgument route)=>inner.NavigateTo(route);public bool OpenStudio()=>inner.OpenStudio();
        public bool SetReady()=>inner.SetReady();public bool StartMatch()=>inner.StartMatch();public bool LeaveLobby()=>inner.LeaveLobby();
        public bool NextHunter()=>inner.NextHunter();public bool NextSuit()=>inner.NextSuit();public bool OpenLobbyDetails()=>inner.OpenLobbyDetails();
        public RmlUiRuleDraft? ReadRules()=>inner.ReadRules();public IReadOnlyList<string> BuiltInArenaKeys()=>inner.BuiltInArenaKeys();
        public bool SubmitRules(RmlUiRuleDraft draft,out string error)=>inner.SubmitRules(draft,out error);public void Quit()=>inner.Quit();
        public Task<RmlUiReplayEntry[]> ReadTheatreAsync(CancellationToken token)=>inner.ReadTheatreAsync(token);
        public Task<string> ValidateReplayAsync(RmlUiReplayEntry entry,CancellationToken token)=>inner.ValidateReplayAsync(entry,token);
        public bool LaunchReplay(RmlUiReplayEntry entry)=>inner.LaunchReplay(entry);
    }
}
