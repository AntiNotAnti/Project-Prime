using System.Diagnostics;
using System.Collections.Concurrent;
using MphRead.Mods.Launcher;

internal static class LifecycleAcceptance
{
    internal static void Run(Action<bool,string> check)
    {
        foreach (var test in new[] {
            (Action:NativeCommunityRevisionAction.Publish,Target:2,Current:2,Latest:4),
            (Action:NativeCommunityRevisionAction.Unlist,Target:4,Current:1,Latest:4),
            (Action:NativeCommunityRevisionAction.Draft,Target:4,Current:1,Latest:4),
            (Action:NativeCommunityRevisionAction.Delete,Target:4,Current:1,Latest:3),
            (Action:NativeCommunityRevisionAction.Restore,Target:4,Current:1,Latest:4),
            (Action:NativeCommunityRevisionAction.Promote,Target:3,Current:3,Latest:4) })
        {
            var service = new Service(test.Action==NativeCommunityRevisionAction.Restore);
            using var controller = new NativeCommunityRevisionsController(service);
            controller.Open(service.Request()); Ready(controller); Select(controller,test.Target);
            check(controller.Propose(test.Action) && controller.Tick().Confirming && service.Accepted==0,"lifecycle action requires explicit confirmation");
            check(controller.Tick().Confirmation.Contains(Service.Hash(test.Target)) && !controller.Select(0),"confirmation binds immutable selected SHA and prevents reselection");
            check(controller.Dismiss() && service.Accepted==0,"dismissed lifecycle confirmation makes no write");
            controller.Propose(test.Action); check(controller.ConfirmChange(),"confirmed lifecycle action starts bounded operation");
            var view=Ready(controller);
            check(view.Connected && view.CurrentHash==Service.Hash(test.Current) && view.LatestHash==Service.Hash(test.Latest),"verified lifecycle readback follows actual server pointer semantics");
            check(service.Accepted==1 && service.Commands.Single().Action==test.Action && service.Commands[0].Number==test.Target
                && service.Commands[0].Hash==Service.Hash(test.Target),"one exact revision action reaches fake authority");
            check(service.Data.Revisions.Length==4 && service.Data.Revisions.Select(row=>row.Hash).Distinct().Count()==4,"lifecycle preserves immutable packages and retained history");
            check(controller.Refresh() && Ready(controller).Connected,"verified changed latest pointer becomes refresh witness");
        }
        var none = new Service();
        none.Data=none.Data with{Revisions=none.Data.Revisions.Select(row=>row.Number==1?row with{Listed=false}:row).ToArray()};
        using(var controller=new NativeCommunityRevisionsController(none))
        {
            controller.Open(none.Request()); Ready(controller); controller.Propose(NativeCommunityRevisionAction.Unlist); controller.ConfirmChange();
            check(Ready(controller).CurrentHash=="" && none.Data.Revisions.Length==4,"unlisting last public package permits an empty public pointer while retaining active revisions");
        }
        foreach(string unavailable in new[]{"last-active","expired-restore","permission","archived","deleted"})
        {
            var service=new Service(unavailable=="expired-restore");
            if(unavailable=="last-active")service.Data=service.Data with{Revisions=service.Data.Revisions.Select(row=>row.Number==4?row:row with{Deleted=true,DeleteAfter=DateTimeOffset.UtcNow.AddDays(1)}).ToArray()};
            if(unavailable=="expired-restore")service.Data=service.Data with{Revisions=service.Data.Revisions.Select(row=>row.Number==4?row with{DeleteAfter=DateTimeOffset.UtcNow.AddSeconds(-1)}:row).ToArray()};
            if(unavailable=="permission")service.Data=service.Data with{CanManage=false};
            if(unavailable=="archived")service.Data=service.Data with{Archived=true};
            if(unavailable=="deleted")service.Data=service.Data with{Deleted=true};
            using var controller=new NativeCommunityRevisionsController(service);
            controller.Open(service.Request()); Ready(controller); Select(controller,4);
            var action=unavailable=="expired-restore"?NativeCommunityRevisionAction.Restore:NativeCommunityRevisionAction.Delete;
            check(!controller.Propose(action) && !controller.ConfirmChange() && service.Commands.Count==0,"last active, expired retention or missing project authority cannot be bypassed");
        }
        foreach(var action in Enum.GetValues<NativeCommunityRevisionAction>())
        {
            var service=new Service(action==NativeCommunityRevisionAction.Restore);
            using var controller=new NativeCommunityRevisionsController(service);
            controller.Open(service.Request()); Ready(controller); Select(controller,action==NativeCommunityRevisionAction.Publish?2:action==NativeCommunityRevisionAction.Promote?3:4);
            controller.Propose(action); service.CurrentLease=new object();
            check(!controller.ConfirmChange() && !controller.Tick().Connected && service.Accepted==0,"account switch rejects every confirmed revision action");
        }
        foreach(string change in new[]{"current","latest","owner","account","permission","revision","visibility","retention"})
        {
            var service=new Service(change=="retention");
            using var controller=new NativeCommunityRevisionsController(service);
            controller.Open(service.Request()); Ready(controller); Select(controller,change=="retention"?4:2);
            var action=change=="retention"?NativeCommunityRevisionAction.Restore:NativeCommunityRevisionAction.Publish;
            controller.Propose(action);
            service.Data=change switch {
                "current"=>service.Data with{CurrentHash=Service.Hash(1)},"latest"=>service.Data with{LatestHash=Service.Hash(3)},
                "owner"=>service.Data with{OwnerId="replacement"},"account"=>service.Data with{Account=Guid.NewGuid()},
                "permission"=>service.Data with{CanManage=false},
                "revision"=>service.Data with{Revisions=service.Data.Revisions.Select(row=>row.Number==2?row with{Notes="REPLACEMENT IMMUTABLE METADATA"}:row).ToArray()},
                "visibility"=>service.Data with{Revisions=service.Data.Revisions.Select(row=>row.Number==2?row with{Draft=false}:row).ToArray()},
                _=>service.Data with{Revisions=service.Data.Revisions.Select(row=>row.Number==4?row with{DeleteAfter=DateTimeOffset.UtcNow.AddSeconds(-1)}:row).ToArray()}};
            controller.ConfirmChange();
            check(!Ready(controller).Connected && service.Accepted==0,"fresh exact project/account/package/visibility/retention preflight rejects changed authority");
        }
        foreach(var action in Enum.GetValues<NativeCommunityRevisionAction>())
        {
            var service=new Service(action==NativeCommunityRevisionAction.Restore){FailWrite=true};
            using var controller=new NativeCommunityRevisionsController(service);
            controller.Open(service.Request()); Ready(controller); Select(controller,action==NativeCommunityRevisionAction.Publish?2:action==NativeCommunityRevisionAction.Promote?3:4);
            var original=service.Data;controller.Propose(action);controller.ConfirmChange();
            check(!Ready(controller).Connected && service.Data==original && service.Accepted==0,"failed writes remove authority without optimistic visibility or lifecycle changes");
        }
        foreach(string tamper in new[]{"unrelated-flags","remove-history","hash","current","latest","deadline","target-state"})
        {
            var service=new Service(){Tamper=tamper};
            using var controller=new NativeCommunityRevisionsController(service);
            controller.Open(service.Request());Ready(controller);controller.Propose(NativeCommunityRevisionAction.Delete);controller.ConfirmChange();
            check(!Ready(controller).Connected && service.Accepted==1,"bad readback never publishes changed hash/history/pointers/retention flags as verified");
        }
        var delayed=new Service(){Hold=true};
        using(var controller=new NativeCommunityRevisionsController(delayed))
        {
            controller.Open(delayed.Request());Ready(controller);controller.Propose(NativeCommunityRevisionAction.Unlist);controller.ConfirmChange();
            var watch=Stopwatch.StartNew();while(delayed.Pending.IsEmpty&&watch.Elapsed<TimeSpan.FromSeconds(4))Thread.Sleep(2);
            check(delayed.Pending.TryDequeue(out var pending),"delayed fake write reached service boundary");
            controller.Cancel();delayed.Hold=false;controller.Open(delayed.Request());Ready(controller);
            pending!.TrySetResult(delayed.Data with{CurrentHash=Service.Hash(1)});Thread.Sleep(20);
            check(controller.Tick().CurrentHash==Service.Hash(4),"completion after route cancellation cannot replace reopened history");
        }
        var recovery=new Service(){Tamper="current"};
        using(var controller=new NativeCommunityRevisionsController(recovery))
        {
            controller.Open(recovery.Request());Ready(controller);controller.Propose(NativeCommunityRevisionAction.Delete);controller.ConfirmChange();
            var failed=Ready(controller);
            check(!failed.Connected&&failed.Status.Contains("OUTCOME UNVERIFIED")&&recovery.Accepted==1&&recovery.Data.LatestHash==Service.Hash(3),
                "server-accepted latest deletion with failed readback reports unknown outcome without retaining action authority");
            check(!controller.Propose(NativeCommunityRevisionAction.Restore),"unchecked failed-write catalog cannot authorize another action");
            recovery.Tamper="";controller.Refresh();
            check(Ready(controller).Connected&&recovery.LastRead.ReconcileAfterWrite&&recovery.Data.Revisions.Any(row=>row.Number==4&&row.Deleted),
                "read-only refresh reconciles same map and account despite changed latest pointer");
            controller.Refresh();Ready(controller);
            check(!recovery.LastRead.ReconcileAfterWrite&&recovery.LastRead.AnchorHash==Service.Hash(3),"only successful reconciliation adopts fresh anchor and clears recovery flag");
            Select(controller,4);controller.Propose(NativeCommunityRevisionAction.Restore);controller.ConfirmChange();
            check(Ready(controller).Connected&&recovery.Commands.Last().LatestHash==Service.Hash(3)&&recovery.Data.LatestHash==Service.Hash(4),
                "next mutation requires a new confirmation with exact reconciled current/latest/history witnesses");
        }
        recovery=new Service(){Tamper="current"};
        using(var controller=new NativeCommunityRevisionsController(recovery))
        {
            controller.Open(recovery.Request());Ready(controller);controller.Propose(NativeCommunityRevisionAction.Delete);controller.ConfirmChange();Ready(controller);
            recovery.Data=recovery.Data with{CanManage=false};controller.Refresh();
            check(!Ready(controller).Connected,"reconciliation still requires fresh project permissions");
            recovery.CurrentLease=new object();
            check(!controller.Refresh()&&controller.Tick().Status.Contains("ACCOUNT CHANGED"),"reconciliation cannot cross account lease changes");
        }
        foreach(bool dispose in new[]{false,true})
        {
            var queued=new Service(){HoldReads=true};
            using var controller=new NativeCommunityRevisionsController(queued);
            controller.Open(queued.Request());
            var watch=Stopwatch.StartNew();while(queued.PendingReads.IsEmpty&&watch.Elapsed<TimeSpan.FromSeconds(4))Thread.Sleep(2);
            check(queued.PendingReads.TryDequeue(out var read),"private delayed catalog read captured");read!.TrySetResult(queued.Data);
            watch.Restart();while(QueueCount(controller)==0&&watch.Elapsed<TimeSpan.FromSeconds(4))Thread.Sleep(2);
            check(QueueCount(controller)==1,"private completion queued before retirement without owner publication");
            if(dispose)controller.Dispose();else controller.Cancel();
            check(QueueCount(controller)==0,"retirement immediately drains queued private catalog and account lease");
        }
    }
    private static NativeCommunityRevisionsView Ready(NativeCommunityRevisionsController controller)
    {var watch=Stopwatch.StartNew();do{var view=controller.Tick();if(!view.Busy)return view;Thread.Sleep(2);}while(watch.Elapsed<TimeSpan.FromSeconds(4));throw new TimeoutException();}
    private static void Select(NativeCommunityRevisionsController controller,int number)
    {var view=controller.Tick();int row=Enumerable.Range(0,view.Rows.Count).Single(i=>view.Rows[i].Number==number);if(!controller.Select(row))throw new InvalidOperationException("synthetic row unavailable");}
    private static int QueueCount(NativeCommunityRevisionsController controller)
    {
        object queue=typeof(NativeCommunityRevisionsController).GetField("_done",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(controller)!;
        return(int)queue.GetType().GetProperty("Count")!.GetValue(queue)!;
    }
    private sealed class Service : INativeCommunityRevisionsAuthority
    {
        private static readonly Guid Map=Guid.Parse("cccccccc-1111-4111-8111-111111111111"),Account=Guid.Parse("dddddddd-1111-4111-8111-111111111111");
        private readonly object _lease=new();
        internal object CurrentLease;
        internal NativeCommunityRevisionCatalog Data;
        internal bool FailWrite,Hold,HoldReads;
        internal NativeCommunityHistoryRequest LastRead;
        internal string Tamper="";
        internal int Accepted;
        internal readonly List<NativeCommunityPromotion> Commands=new();
        internal readonly ConcurrentQueue<TaskCompletionSource<NativeCommunityRevisionCatalog>> Pending=new();
        internal readonly ConcurrentQueue<TaskCompletionSource<NativeCommunityRevisionCatalog>> PendingReads=new();
        internal Service(bool deleted=false)
        {
            CurrentLease=_lease;
            var rows=Enumerable.Range(1,4).Select(i=>new NativeCommunityRevision(i,Hash(i),i==1?"":Hash(i-1),"NOTES "+i,
                DateTimeOffset.UnixEpoch.AddDays(i),i is 1 or 4,i==2,deleted&&i==4,
                new(Map,"SYNTHETIC MAP","CREATOR",Hash(i),i,"BATTLE",1,8)){DeleteAfter=deleted&&i==4?DateTimeOffset.UtcNow.AddDays(1):null}).ToArray();
            Data=new(Map,"SYNTHETIC PROJECT",Hash(deleted?1:4),Hash(deleted?3:4),true,false,false,rows){Account=Account,AccountLease=_lease,OwnerId="synthetic-owner"};
        }
        internal static string Hash(int i)=>i.ToString("X64");
        internal NativeCommunityHistoryRequest Request()=>new(Map,true,Data.LatestHash){AccountLease=_lease};
        public bool IsCurrentAccountLease(object? lease)=>ReferenceEquals(lease,CurrentLease);
        public Task<NativeCommunityRevisionCatalog> ReadAsync(NativeCommunityHistoryRequest request,CancellationToken token)
        {
            LastRead=request;
            if(request.AnchorHash!=Data.LatestHash&&!request.ReconcileAfterWrite)throw new InvalidOperationException("Synthetic stale anchor");
            if(HoldReads){var read=new TaskCompletionSource<NativeCommunityRevisionCatalog>(TaskCreationOptions.RunContinuationsAsynchronously);PendingReads.Enqueue(read);return read.Task;}
            return Task.FromResult(Data);
        }
        public Task<NativeCommunityRevisionCatalog> PromoteAsync(NativeCommunityPromotion request,CancellationToken token)=>ChangeAsync(request,token);
        public Task<NativeCommunityRevisionCatalog> ChangeAsync(NativeCommunityPromotion request,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();Commands.Add(request);
            if(!IsCurrentAccountLease(request.AccountLease)||!NativeCommunityRevisionPolicy.MatchesPreflight(request,Data)||FailWrite)
                throw new InvalidOperationException("SYNTHETIC PRIVATE failure");
            if(Hold){var pending=new TaskCompletionSource<NativeCommunityRevisionCatalog>(TaskCreationOptions.RunContinuationsAsynchronously);Pending.Enqueue(pending);return pending.Task;}
            var changed=Data.Revisions.Select(row=>row.Number!=request.Number?row:request.Action switch{
                NativeCommunityRevisionAction.Promote or NativeCommunityRevisionAction.Publish=>row with{Listed=true,Draft=false},
                NativeCommunityRevisionAction.Unlist=>row with{Listed=false,Draft=false},NativeCommunityRevisionAction.Draft=>row with{Listed=false,Draft=true},
                NativeCommunityRevisionAction.Delete=>row with{Deleted=true,DeleteAfter=DateTimeOffset.UtcNow.AddDays(1)},
                _=>row with{Deleted=false,DeleteAfter=null}}).ToArray();
            string current=Data.CurrentHash,latest=Data.LatestHash;
            if(request.Action is NativeCommunityRevisionAction.Promote or NativeCommunityRevisionAction.Publish)current=request.Hash;
            else if(!changed.Any(row=>row.Hash==current&&!row.Deleted&&row.Listed&&!row.Draft))current=changed.Where(row=>!row.Deleted&&row.Listed&&!row.Draft).OrderByDescending(row=>row.Number).FirstOrDefault()?.Hash??"";
            if(request.Action is NativeCommunityRevisionAction.Delete or NativeCommunityRevisionAction.Restore)latest=changed.Where(row=>!row.Deleted).MaxBy(row=>row.Number)!.Hash;
            Accepted++;Data=Data with{CurrentHash=current,LatestHash=latest,Revisions=changed};
            var result=Tamper switch{
                "unrelated-flags"=>Data with{Revisions=changed.Select(row=>row.Number==1?row with{Draft=true,Listed=false}:row).ToArray()},
                "remove-history"=>Data with{Revisions=changed.Where(row=>row.Number!=4).ToArray()},
                "hash"=>Data with{Revisions=changed.Select(row=>row.Number==4?row with{Hash=Hash(99)}:row).ToArray()},
                "current"=>Data with{CurrentHash=Hash(3)},"latest"=>Data with{LatestHash=Hash(4)},
                "deadline"=>Data with{Revisions=changed.Select(row=>row.Number==4?row with{DeleteAfter=null}:row).ToArray()},
                "target-state"=>Data with{Revisions=changed.Select(row=>row.Number==4?row with{Deleted=false}:row).ToArray()},_=>Data};
            return Task.FromResult(result);
        }
    }
}
