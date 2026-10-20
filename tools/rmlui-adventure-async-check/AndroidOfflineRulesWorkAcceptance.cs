using MphRead;
using MphRead.Droid;
using MphRead.Mods.Launcher.RmlUi.Core;

// Linked production dispatcher + real immutable cache. UI dispatch/admission
// and settings writer are explicit fakes: no Android runtime or user files.
internal static class AndroidOfflineRulesWorkAcceptance
{
    internal static void Run()
    {
        int checks=0,writes=0,reads=0;
        void Check(bool value,string label)
        {if(!value)throw new InvalidOperationException(label);checks++;Console.WriteLine("ANDROID RULE WORK PASS "+label);}
        var before=new RmlUiOfflineRules("7","7:00","3:00","medium",0);
        var next=before with{RiftlineM7Available=true,PointGoal="9"};
        var change=new RmlUiOfflineRulesChange(GameMode.Battle,before,next);
        var cache=new RmlUiOfflineRulesCache(before);var settings=new MenuSettings();before.CopyTo(settings);
        var queue=new Queue<Action>();bool admitted=true;
        RmlUiOfflineRulesSaveResult Perform(RmlUiOfflineRulesChange captured,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();writes++;
            bool success=RmlUiOfflineRules.TryCommit(settings,captured.Expected,captured.Next,_=>{},out string error);
            return new(captured,success,success?"EXACT FAKE UI OWNER VERIFIED":error,RmlUiOfflineRules.Capture(settings));
        }
        RmlUiOfflineRules? Read(){reads++;return RmlUiOfflineRules.Capture(settings);}
        void Canceled(Task<RmlUiOfflineRulesSaveResult> task,string label)
        {
            bool canceled=false;
            try{task.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();}
            catch(OperationCanceledException){canceled=true;}
            Check(canceled,label);
        }
        void Reset(){before.CopyTo(settings);cache.Publish(before);admitted=true;}
        Check(change.Validate(out _),"valid copied Battle availability proposal validates before writer dispatch");
        foreach(var invalid in new[]{change with{Mode=(GameMode)255},change with{Next=next with{Flags=8192}},
            change with{Next=next with{Flags=ushort.MaxValue}},change with{Next=next with{DamageLevel="unknown"}},
            change with{Next=next with{DamageLevel="HIGH"}},change with{Next=next with{TimeLimit="7:65"}},
            change with{Next=next with{Flags=1<<5}},change with{Mode=GameMode.Survival}})
            Check(!invalid.Validate(out string error)&&error.Length>0,"invalid immutable rule proposal refuses: "+invalid.Mode+"/"+invalid.Next.Flags+"/"+invalid.Next.DamageLevel);
        using(var cancellation=new CancellationTokenSource())
        {
            var task=RmlUiAndroidOfflineRulesWork.Run(queue.Enqueue,()=>admitted,Perform,cache.Publish,change,cancellation.Token,readCurrent:Read);
            Check(!task.IsCompleted&&writes==0&&queue.Count==1&&cache.Read()==before,
                "posting exact save is nonblocking and cannot claim cache mutation before UI callback");
            cancellation.Cancel();Canceled(task,"cancel before queued UI callback completes bounded task without a writer");
            queue.Dequeue()();Check(writes==0&&cache.Read()==before,"retired queued callback leaves current cache and settings untouched");
        }
        using(var canceled=new CancellationTokenSource())
        {
            canceled.Cancel();int queued=queue.Count;
            var task=RmlUiAndroidOfflineRulesWork.Run(queue.Enqueue,()=>true,Perform,cache.Publish,change,canceled.Token);
            Canceled(task,"already-canceled call refuses before posting UI work");
            Check(queue.Count==queued&&writes==0,"pre-canceled call posts no callback or effect");
        }
        var valid=RmlUiAndroidOfflineRulesWork.Run(queue.Enqueue,()=>admitted,Perform,cache.Publish,change,CancellationToken.None,readCurrent:Read);
        var callback=queue.Dequeue();callback();callback();var result=valid.GetAwaiter().GetResult();
        Check(result.Success&&result.Change==change&&result.Current==next&&cache.Read()==next&&writes==1,
            "admitted callback writes and publishes exact readback once even when dispatcher repeats callback");
        Reset();
        using(var cancellation=new CancellationTokenSource())
        {
            var task=RmlUiAndroidOfflineRulesWork.Run(queue.Enqueue,()=>{cancellation.Cancel();return true;},Perform,cache.Publish,change,cancellation.Token);
            queue.Dequeue()();Canceled(task,"cancel during admission prevents writer even if admission returns true");
            Check(writes==1&&RmlUiOfflineRules.Capture(settings)==before,"admission cancellation cannot commit copied next rules");
        }
        Reset();
        // Independently retire each copied identity/admission predicate. These
        // model the caller contract, not execution of MainActivity's gate.
        foreach(string identity in new[]{"activation","home","view","lifetime","foreground","pending-match","live-scene","network","replay","pending-save"})
        {
            bool current=true;
            var task=RmlUiAndroidOfflineRulesWork.Run(queue.Enqueue,()=>current,Perform,cache.Publish,change,CancellationToken.None,readCurrent:Read);
            current=false;queue.Dequeue()();var refused=task.GetAwaiter().GetResult();
            Check(!refused.Success&&refused.Change==change&&writes==1&&cache.Read()==null,
                "retired fake caller admission blocks write and publishes unknown cache: "+identity);
            Reset();
        }
        using(var cancellation=new CancellationTokenSource())
        {
            var task=RmlUiAndroidOfflineRulesWork.Run(queue.Enqueue,()=>true,(captured,token)=>
            {var committed=Perform(captured,token);cancellation.Cancel();return committed;},cache.Publish,change,cancellation.Token,readCurrent:Read);
            var apply=queue.Dequeue();apply();apply();Canceled(task,"post-effect cancellation retires result authority instead of claiming verified success");
            Check(writes==2&&cache.Read()==next&&RmlUiOfflineRules.Capture(settings)==next,
                "post-effect cancellation still publishes committed copied readback and cannot repeat or undo write");
        }
        Reset();
        var wrong=RmlUiAndroidOfflineRulesWork.Run(queue.Enqueue,()=>true,(captured,token)=>
        {var committed=Perform(captured,token);return committed with{Change=captured with{Next=next with{PointGoal="10"}}};},
            cache.Publish,change,CancellationToken.None,readCurrent:Read);
        queue.Dequeue()();var wrongResult=wrong.GetAwaiter().GetResult();
        Check(!wrongResult.Success&&wrongResult.Change==change&&wrongResult.Status.Contains("UNVERIFIED")&&cache.Read()==next&&reads==1,
            "wrong change echo cannot verify success but refreshes actual current owner readback inside callback");
        Reset();int previousWrites=writes;
        var threw=RmlUiAndroidOfflineRulesWork.Run(queue.Enqueue,()=>true,(captured,token)=>
        {Perform(captured,token);throw new IOException("PRIVATE PATH OR ACCOUNT DETAIL");},cache.Publish,change,CancellationToken.None,readCurrent:Read);
        queue.Dequeue()();var failed=threw.GetAwaiter().GetResult();
        Check(!failed.Success&&failed.Status.Contains("UNVERIFIED")&&!failed.Status.Contains("PRIVATE")&&cache.Read()==next
            &&writes==previousWrites+1&&failed.Current==next,
            "exception after committed write sanitizes error and preserves actual next cache rather than restoring old rules");
        Reset();int beforeReads=reads;
        var lostOwner=RmlUiAndroidOfflineRulesWork.Run(queue.Enqueue,()=>admitted,(captured,token)=>
        {Perform(captured,token);admitted=false;throw new IOException("PRIVATE");},cache.Publish,change,CancellationToken.None,readCurrent:Read);
        queue.Dequeue()();var lost=lostOwner.GetAwaiter().GetResult();
        Check(!lost.Success&&cache.Read()==null&&RmlUiOfflineRules.Capture(settings)==next&&reads==beforeReads,
            "retired owner after effect cannot be re-read and cache is explicitly unknown without rollback claim");
        Reset();
        var readFailure=RmlUiAndroidOfflineRulesWork.Run(queue.Enqueue,()=>true,(_,_)=>throw new IOException("PRIVATE"),
            cache.Publish,change,CancellationToken.None,readCurrent:()=>throw new IOException("PRIVATE READ"));
        queue.Dequeue()();var unknown=readFailure.GetAwaiter().GetResult();
        Check(!unknown.Success&&unknown.Current==null&&cache.Read()==null&&!unknown.Status.Contains("PRIVATE"),
            "unavailable failure readback leaves explicit unknown cache for declared legacy fallback");
        Reset();beforeReads=reads;
        var postFailure=RmlUiAndroidOfflineRulesWork.Run(_=>throw new IOException("PRIVATE POST"),()=>true,Perform,
            cache.Publish,change,CancellationToken.None,readCurrent:Read).GetAwaiter().GetResult();
        Check(!postFailure.Success&&cache.Read()==null&&!postFailure.Status.Contains("PRIVATE")&&reads==beforeReads,
            "failed UI post sanitizes failure and never reads UI-owned settings off its callback thread");
        Reset();previousWrites=writes;
        var expired=RmlUiAndroidOfflineRulesWork.Run(queue.Enqueue,()=>true,Perform,cache.Publish,change,CancellationToken.None,
            deadlineMilliseconds:20,readCurrent:Read);
        Canceled(expired,"dropped UI post completes by bounded deadline instead of waiting indefinitely");
        queue.Dequeue()();Check(writes==previousWrites&&cache.Read()==before,
            "callback arriving after deadline cannot write, recapture or mutate cached settings");
        Console.WriteLine($"PASS {checks} Android offline rule worker checks (actual linked dispatcher/cache/commit helper; fake UI queue/admission/settings writer; no Android runtime/user files).");
    }
}
