using System.Collections;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using MphRead;
using MphRead.Mods.CustomWeapons.Packages;
using MphRead.Mods.Network;

// Real worker/store ownership checks. Start callbacks are observed delegates;
// this suite deliberately does not claim child allocation or UDP coverage.
internal static class RemoteWeaponPackagePreparationChecks
{
    internal static int Run(string? output=null)
    {
        int checks=0;string root=Path.GetFullPath(output??Path.Combine(Path.GetTempPath(),"cw12e-host-preparation-"+Guid.NewGuid().ToString("N")));
        void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);checks++;}
        void Until(Func<bool> done,Action pump,string label){var time=Stopwatch.StartNew();while(!done()){if(time.Elapsed.TotalSeconds>8)throw new TimeoutException(label);pump();Thread.Sleep(5);}Check(true,label);}
        Task Worker(HostedMapRequests worker)
        {var list=(IList)typeof(HostedMapRequests).GetField("_pending",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(worker)!;object pending=list[0]!;return (Task)pending.GetType().GetField("Download")!.GetValue(pending)!;}
        try
        {
            Directory.CreateDirectory(root);byte[] archive=WeaponPackageReference.BuildRiftlineM7();string hash=WeaponPackage.Parse(archive).PackageHash;
            HostRequestPacket Request()=>new(){Protocol=NetConfig.ProtocolVersion,MaxPlayers=2,Mode=(byte)GameMode.Battle,RoomKey="MP3 PROVING GROUND",ServerName="Package worker",WeaponArchiveHash=hash};
            int endpoint=32000;IPEndPoint Sender()=>new(IPAddress.Loopback,++endpoint);
            WeaponPackageStore Store(string name){var store=new WeaponPackageStore(Path.Combine(root,name));store.Install(archive);return store;}
            void Failure(string name,string? approval,GameMode mode=GameMode.Battle)
            {
                var replies=new List<HostReplyPacket>();int starts=0;string cache=Path.Combine(root,name+"-cache");using var work=new HostedMapRequests(cache,"http://127.0.0.1:1");var request=Request();request.Mode=(byte)mode;
                work.Enqueue(request,Sender(),1,(_,reply)=>replies.Add(reply),approval);
                Until(()=>replies.Count>0,()=>work.Pump(2,(_,_,_,_)=>{starts++;return new(){Started=true,Port=123};},(_,reply)=>replies.Add(reply)),name+" produces bounded refusal");
                Check(starts==0&&!replies[0].Started&&replies[0].Port==0&&!replies[0].Reason.Contains(root,StringComparison.OrdinalIgnoreCase),name+" never allocates or exposes private path");
                Check(!Directory.Exists(cache),name+" never enters map cache");
            }
            Failure("unapproved",null);string missing=Path.Combine(root,"missing-store");Failure("missing",missing);Check(!Directory.Exists(missing),"missing approved store remains noncreating");
            var corrupt=Store("corrupt-store");File.WriteAllBytes(Path.Combine(root,"corrupt-store","archives",hash+".ppweapon"),new byte[]{1,2,3});Failure("corrupt",Path.Combine(root,"corrupt-store"));
            var modes=Store("mode-store");Failure("survival",Path.Combine(root,"mode-store"),mode:GameMode.Survival);
            var store=Store("valid-store");string storePath=Path.Combine(root,"valid-store"),cachePath=Path.Combine(root,"package-only-cache");
            using(var work=new HostedMapRequests(cachePath,"http://127.0.0.1:1"))
            {
                var replies=new List<HostReplyPacket>();int starts=0;var sender=Sender();var request=Request();
                work.Enqueue(request,sender,1,(_,reply)=>replies.Add(reply),storePath);work.Enqueue(request,sender,1,(_,reply)=>replies.Add(reply),storePath);
                var changed=request;changed.WeaponArchiveHash=new string('b',64);work.Enqueue(changed,sender,1,(_,reply)=>replies.Add(reply),storePath);
                Check(replies.Count==1&&!replies[0].Started,"changed hash cannot replace pending exact request");
                Until(()=>starts==1,()=>work.Pump(2,(_,_,_,prepared)=>{starts++;Check(prepared.Maps==null&&prepared.Weapon!=null&&prepared.Weapon.Matches(storePath,hash),"worker returns exact weapon proof without map preparation");using var lease=prepared.Weapon!.Retain();Check(lease.Package.PackageHash==hash,"worker verifies real reference archive bytes");return new(){Started=true,Port=12345};},(_,reply)=>replies.Add(reply)),"duplicate pending wire starts exactly once");
                work.Enqueue(request,sender,3,(_,reply)=>replies.Add(reply),storePath);Check(starts==1&&replies.Count==3&&replies[^1].Started&&replies[^1].Port==12345,"completed identical retry returns cached reply without restart");
                Check(!Directory.Exists(cachePath)&&store.Uninstall(hash)&&store.Prune()==1,"completed cached reply retains no package lease or map cache");
            }
            foreach(bool throws in new[]{false,true})
            {
                string name=throws?"throwing-start":"refused-start";var target=Store(name);using var work=new HostedMapRequests(Path.Combine(root,name+"-cache"));var replies=new List<HostReplyPacket>();
                work.Enqueue(Request(),Sender(),1,(_,reply)=>replies.Add(reply),Path.Combine(root,name));
                Until(()=>replies.Count==1,()=>work.Pump(2,(_,_,_,_)=>throws?throw new IOException("private "+root):new(){Started=false,Reason="No capacity"},(_,reply)=>replies.Add(reply)),name+" settles worker");
                Check(!replies[0].Started&&!replies[0].Reason.Contains(root,StringComparison.OrdinalIgnoreCase)&&target.Uninstall(hash)&&target.Prune()==1,name+" releases proof and hides worker exceptions");
            }
            var retained=Store("retain");string retainedPath=Path.Combine(root,"retain");var proof=PreparedLocalWeaponPackage.Prepare(retainedPath,hash,default);
            Check(proof.Selection.StoreDirectory==retainedPath&&proof.Matches(retainedPath,hash)&&!proof.Matches(retainedPath,new string('c',64)),"prepared proof binds canonical store and exact hash");
            Check(retained.Uninstall(hash),"uninstall removes marker while prepared proof remains");
            using(var gate=new FileStream(Path.Combine(retainedPath,".store.lock"),FileMode.Open,FileAccess.ReadWrite,FileShare.None))
            {var timer=Stopwatch.StartNew();using var child=proof.Retain();Check(timer.ElapsedMilliseconds<500&&child.Package.PackageHash==hash,"Retain performs no gate acquisition or installed-marker IO");}
            using(var child=proof.Retain()){proof.Dispose();Check(retained.Prune()==0,"independent retained child survives original proof disposal");}
            Check(retained.Prune()==1,"last retained owner releases real archive lease");
            {var pool=new HostPool();pool.SetWeaponStore(retainedPath);using var prepared=new PreparedHostRequest(null,proof);Check(!pool.StartPrepared(Request(),Sender(),1,prepared).Started&&pool.Count==0,"disposed prepared proof refuses before pool allocation");Check(!pool.Start(Request(),Sender(),2).Started&&pool.Count==0,"public unprepared package start refuses before port allocation");}
            foreach(bool completed in new[]{false,true})
            {
                string name=completed?"completed-unconsumed":"cancelled-gated";var target=Store(name);string path=Path.Combine(root,name);var work=new HostedMapRequests(Path.Combine(root,name+"-cache"));
                FileStream? gate=completed?null:new(Path.Combine(path,".store.lock"),FileMode.Open,FileAccess.ReadWrite,FileShare.None);
                var timer=Stopwatch.StartNew();work.Enqueue(Request(),Sender(),1,(_,_)=>throw new InvalidOperationException("unexpected immediate response"),path);Task task=Worker(work);
                if(!completed)work.Pump(2,(_,_,_,_)=>throw new InvalidOperationException("gate-held callback"),(_,_)=>{});
                if(!completed)Check(timer.ElapsedMilliseconds<500&&!task.IsCompleted,"gate-held Enqueue and Pump remain nonblocking");
                if(completed)Until(()=>task.IsCompleted,()=>{},"worker finishes before unconsumed result disposal");
                work.Dispose();gate?.Dispose();Until(()=>task.IsCompleted,()=>{},name+" worker terminates after disposal");
                Check(target.Uninstall(hash),name+" installed marker removable");int removed=0;Until(()=>removed==1,()=>removed+=target.Prune(),name+" releases unfinished or unconsumed lease");
            }
            Console.WriteLine($"PASS {checks} remote weapon package preparation/ownership checks. Private output: {root}. No actual UDP or child allocator claim.");return 0;
        }
        catch(Exception error){Console.Error.WriteLine("REMOTE PACKAGE PREPARATION FAIL "+error);return 1;}
    }
}


