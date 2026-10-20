using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

// Actual controller/job epoch and native binding projection, with copied rule
// values and fake async owner results. No toolkit, Android runtime or user files.
internal static class OfflineRulesAsyncAcceptance
{
    internal static void Run()
    {
        int checks=0;
        void Check(bool value,string label)
        {if(!value)throw new InvalidOperationException(label);checks++;Console.WriteLine("RML RULES ASYNC PASS "+label);}
        var initial=new RmlUiOfflineRules("7","7:00","3:00","medium",0);
        var cache=new RmlUiOfflineRulesCache(initial);
        var services=new Stub {OfflineRules=initial,OfflineRulesAsync=true,OfflineRulesCache=cache};
        var pending=new TaskCompletionSource<RmlUiOfflineRulesSaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        RmlUiOfflineRulesChange captured=default;CancellationToken token=default;int calls=0;
        services.AsyncOfflineRuleSave=(change,cancellation)=>{captured=change;token=cancellation;calls++;return pending.Task;};
        using var controller=new RmlUiMenuController(services);
        bool Flag(string key)=>controller.Tick().Bindings.TryGetValue(key,out var value)&&value.Boolean;
        string Text(string key)=>controller.Tick().Bindings.TryGetValue(key,out var value)?value.Text:"";
        void PublishThenTick()
        {
            // Wait for the actual worker publication before testing epoch
            // retirement; merely sleeping could pass before stale work ran.
            var queue=typeof(RmlUiMenuController).GetField("_completed",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(controller)!;
            var count=queue.GetType().GetProperty("Count")!;
            Check(SpinWait.SpinUntil(()=>(int)count.GetValue(queue)!>0,TimeSpan.FromSeconds(2)),
                "async worker actually publishes completion before owner-thread observation");
            controller.Tick();
        }
        void Drain(){for(int i=0;i<250&&controller.Busy;i++){controller.Tick();Thread.Sleep(2);}controller.Tick();Check(!controller.Busy,"completed current rule job leaves busy state");}
        void Open()
        {
            if(controller.OfflineRuleInputs!=null)Check(controller.Dispatch(RmlUiIntentKind.OfflineCancelRules),"retire prior rules editor before new proposal");
            Check(controller.Dispatch(RmlUiIntentKind.OfflineOpenRules),"open current copied cached rules");
        }
        void Begin()
        {
            pending=new(TaskCreationOptions.RunContinuationsAsynchronously);
            Check(controller.Dispatch(RmlUiIntentKind.OfflineRuleToggle,14)
                &&controller.Dispatch(RmlUiIntentKind.OfflineApplyRules,offlinePoints:"9",offlineLimit:"8:20",offlineGoal:"3:00"),
                "explicit Apply starts one exact asynchronous availability save");
        }
        Check(controller.Dispatch(RmlUiIntentKind.Navigate,(int)RmlUiRouteArgument.Offline),"open actual offline controller route");
        Open();Begin();
        var first=captured;
        Check(controller.Busy&&Flag("offline_rules_busy")&&Text("offline_rules_error").Contains("PENDING")
            &&calls==1&&services.OfflineRulesSaveAttempts==0&&cache.Read()==initial
            &&first.Expected==initial&&first.Next.RiftlineM7Available&&first.Next.PointGoal=="9",
            "pending async proposal is immutable and makes no synchronous write or optimistic cache change");
        foreach(var intent in new[]{RmlUiIntentKind.OfflineDamage,RmlUiIntentKind.OfflineRuleToggle,RmlUiIntentKind.OfflineChoice,
            RmlUiIntentKind.OfflineLaunchMatch,RmlUiIntentKind.OfflineOpenRules,RmlUiIntentKind.Navigate})
            Check(!controller.Dispatch(intent,intent==RmlUiIntentKind.OfflineRuleToggle?14:0),"pending rule save blocks mutation or route "+intent);
        Check(!controller.Dispatch(RmlUiIntentKind.OfflineApplyRules,offlinePoints:"123",offlineLimit:"1:00",offlineGoal:"1:00")
            &&calls==1&&captured==first&&controller.OfflineRuleInputs==first.Next,
            "second Apply cannot replace captured values, sequence or request while busy");
        var fields=RmlUiMenuPageBindings.Project(RmlUiMenuPage.OfflineRules,controller.Tick().Bindings);
        foreach(string name in new[]{"offline_rules_apply","offline_damage_value","offline_point_goal","offline_time_limit","offline_time_goal","offline_rule_0","offline_rule_13","offline_rule_14"})
            Check(fields["disabled:"+name].Boolean,"pending native field disabled: "+name);
        Check(!fields.TryGetValue("disabled:offline_rules_cancel",out var disabled)||!disabled.Boolean,
            "cancel remains an enabled native affordance during rule save");
        Check(controller.Dispatch(RmlUiIntentKind.OfflineCancelRules)&&token.IsCancellationRequested&&!controller.Busy
            &&!Flag("offline_rules_open")&&Text("offline_error").Contains("NOT UNDONE"),
            "Cancel retires exact job and editor without claiming rollback");
        // The fake UI owner committed before cancellation won. Publish its real
        // copied cache value separately, as the production dispatcher does.
        cache.Publish(first.Next);pending.SetResult(new(first,true,"FORBIDDEN STALE SUCCESS",first.Next));PublishThenTick();
        Check(!Text("offline_error").Contains("FORBIDDEN")&&cache.Read()==first.Next,
            "retired completion cannot publish success or roll back already committed cache");
        Open();Check(controller.OfflineRuleInputs==first.Next,"reopening after canceled committed work reads latest real copied cache");
        Begin();var wrong=captured;
        cache.Publish(wrong.Next);pending.SetResult(new(wrong with{Next=wrong.Next with{PointGoal="10"}},true,"WRONG ECHO",wrong.Next));Drain();
        Check(Flag("offline_rules_open")&&Text("offline_rules_error").Contains("UNVERIFIED")&&!Text("offline_error").Contains("WRONG ECHO"),
            "same-job completion must echo exact immutable expected and next rules");
        Open();Begin();var currentMismatch=captured;
        cache.Publish(currentMismatch.Next);pending.SetResult(new(currentMismatch,true,"FALSE VERIFIED",currentMismatch.Expected));Drain();
        Check(Text("offline_rules_error").Contains("UNVERIFIED")&&Flag("offline_rules_open"),
            "exact success cannot verify mismatched returned current rules even if cache already matches next");
        Open();Begin();var cacheMismatch=captured;
        pending.SetResult(new(cacheMismatch,true,"FALSE READBACK",cacheMismatch.Next));Drain();
        Check(Text("offline_rules_error").Contains("UNVERIFIED")&&cache.Read()==cacheMismatch.Expected,
            "exact success cannot verify next rules without matching current cache readback");
        Open();Begin();pending.SetException(new IOException("PRIVATE PATH OR ACCOUNT DETAIL"));Drain();
        Check(Text("offline_rules_error").Contains("UNVERIFIED")&&!Text("offline_error").Contains("PRIVATE"),
            "async owner exception is sanitized before native error presentation");
        Open();Begin();var refused=captured;
        pending.SetResult(new(refused,false,"OWNER REFUSED CHANGED SETTINGS",refused.Expected));Drain();
        Check(Text("offline_rules_error").Contains("REFUSED")&&Flag("offline_rules_open"),
            "explicit exact owner refusal leaves editor available for deliberate retry");
        Open();Begin();var old=pending;var oldChange=captured;var oldToken=token;
        Check(controller.Dispatch(RmlUiIntentKind.OfflineCancelRules)&&oldToken.IsCancellationRequested,"cancel first job before opening new one");
        Open();Begin();var fresh=pending;var freshChange=captured;
        old.SetResult(new(oldChange,true,"FORBIDDEN OLD JOB",oldChange.Next));PublishThenTick();
        Check(controller.Busy&&Flag("offline_rules_busy")&&controller.OfflineRuleInputs==freshChange.Next
            &&!Text("offline_error").Contains("FORBIDDEN"),"stale completion cannot clear or settle a newer pending rule-save epoch");
        cache.Publish(freshChange.Next);fresh.SetResult(new(freshChange,true,"EXACT OWNER VERIFIED",freshChange.Next));Drain();
        Check(!Flag("offline_rules_open")&&Text("offline_error")=="LOCAL MATCH RULES SAVED"&&cache.Read()==freshChange.Next,
            "only exact current result and next cache readback retire editor as saved");
        Open();Check(controller.OfflineRuleInputs==freshChange.Next,"reopened editor reflects verified cached settings");
        Begin();var lobbyTask=pending;var lobbyChange=captured;var lobbyToken=token;
        services.LobbyView=Stub.EmptyLobby with{Active=true};controller.Tick();
        Check(lobbyToken.IsCancellationRequested&&!controller.Busy&&!Flag("offline_rules_open"),
            "lobby activation retires pending offline rules and closes stale modal");
        lobbyTask.SetResult(new(lobbyChange,true,"FORBIDDEN LOBBY SUCCESS",lobbyChange.Next));PublishThenTick();
        services.LobbyView=Stub.EmptyLobby;controller.Tick();
        Check(!Flag("offline_rules_open")&&!Text("offline_error").Contains("FORBIDDEN"),
            "late completion from retired lobby boundary cannot restore offline editor or success");
        Open();Begin();var disposed=pending;var disposedChange=captured;var disposeToken=token;controller.Dispose();
        Check(disposeToken.IsCancellationRequested,"controller disposal cancels queued offline rule save");
        disposed.SetResult(new(disposedChange,true,"FORBIDDEN DISPOSE SUCCESS",disposedChange.Next));
        Console.WriteLine($"PASS {checks} asynchronous offline rule controller checks (actual controller/bindings/cache, fake async settings owner; no Android runtime or user files).");
#if PROJECTPRIME_RMLUI_ANDROID_EXPERIMENT
        AndroidOfflineRulesWorkAcceptance.Run();
#endif
    }
}
