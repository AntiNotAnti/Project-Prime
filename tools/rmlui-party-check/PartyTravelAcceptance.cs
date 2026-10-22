using System.Collections.Concurrent;
using System.Reflection;
using MphRead.Mods.Launcher;

static class PartyTravelAcceptance
{
    static string Id(int value) => $"PP-AAAA-BBBB-CCCC-DDDD-{value:X4}";
    static readonly string Party="00000001-1111-4111-8111-111111111111", Travel="00000002-1111-4111-8111-111111111111", Lobby="00000003-1111-4111-8111-111111111111";
    static NativePartySnapshot State(object lease) => Bind(new(Id(1),Party,Id(2),7,
        new[]{new NativePartyMember(Id(1),"Follower",NativePresence.Unknown),new NativePartyMember(Id(2),"Leader",NativePresence.InLobby)},[],[])
        {Travel=new(Travel,3,Id(2),Lobby,"SYNTHETIC ROOM","Synthetic server",100,DateTimeOffset.UtcNow.AddSeconds(60),false,"pending")},lease);
    static NativePartySnapshot Bind(NativePartySnapshot state,object lease)
    {typeof(NativePartySnapshot).GetProperty("AccountWitness",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(state,lease);return state;}
    static NativePartyJoinTarget Target() => new(Id(1),Party,7,Travel,3,Lobby,"203.0.113.7",27015,57,100,"SYNTHETIC ROOM",DateTimeOffset.UtcNow.AddSeconds(60));
    static void Check(bool okay,string label)
    {if(!okay)throw new Exception("PARTY TRAVEL FAIL "+label);Console.WriteLine("PARTY TRAVEL PASS "+label);}
    static NativePartyView Wait(NativePartyAuthorityController owner,Func<NativePartyView,bool> ready)
    {for(int i=0;i<400;++i){var view=owner.Tick();if(ready(view))return view;Thread.Sleep(5);}throw new TimeoutException("Party travel source owner");}
    public static void Run()
    {
        var service=new Service();using var owner=new NativePartyAuthorityController(service);
        void Load(){service.Snapshot=State(service.Lease);owner.Refresh();Wait(owner,v=>v.Connected&&!v.Busy);}
        Load();
        Check(owner.CanTravel(NativePartyTravelAction.Follow)&&!owner.CanTravel(NativePartyTravelAction.Publish,new(1,100,"SYNTHETIC ROOM","")),"follower may follow but cannot publish leader destination");
        Check(!owner.CanTravel((NativePartyTravelAction)99),"undefined travel action is refused");
        Check(owner.Travel(NativePartyTravelAction.Follow),"captured follow starts one authoritative worker");var sent=service.Take();
        Check(sent.Command.ExpectedSelfPrimeId==Id(1)&&sent.Command.ExpectedRevision==7&&sent.Command.PartyId==Party
            &&sent.Command.TravelId==Travel&&sent.Command.TravelRevision==3,"travel captures exact account/party/travel witnesses");
        var target=Target();sent.Reply.SetResult(new(service.Snapshot,target));Wait(owner,v=>!v.Busy);
        Check(owner.TryTakeJoin(out var taken)&&ReferenceEquals(taken,target),"owner drains exact immutable verified target once");
        Check(!owner.TryTakeJoin(out _),"target cannot be replayed after drain");
        Load();owner.Travel(NativePartyTravelAction.Follow);sent=service.Take();sent.Reply.SetResult(new(service.Snapshot,target with{PartyRevision=8}));Wait(owner,v=>!v.Busy);
        Check(!owner.TryTakeJoin(out _),"target with changed party revision cannot leave projection");
        Load();owner.Travel(NativePartyTravelAction.JoinLeader);sent=service.Take();service.Lease=new();owner.Tick();
        Check(sent.Token.IsCancellationRequested&&!owner.Tick().Connected,"account retirement cancels travel and removes private cached state synchronously");
        sent.Reply.SetResult(new(service.Snapshot,target));Thread.Sleep(20);
        Check(!owner.TryTakeJoin(out _)&&owner.Tick().Snapshot==null,"late cancellation-ignoring travel cannot publish old account target");
        Load();owner.Travel(NativePartyTravelAction.Follow);sent=service.Take();sent.Reply.SetResult(new(Bind(service.Snapshot with{SelfPrimeId=Id(2)},service.Lease),target));
        Check(!Wait(owner,v=>!v.Busy).Connected,"travel readback from changed authenticated self is rejected");
        Load();service.Snapshot=service.Snapshot with{Travel=service.Snapshot.Travel! with{ExpiresAt=DateTimeOffset.UtcNow.AddSeconds(-1)}};
        owner.Refresh();Wait(owner,v=>!v.Busy);
        Check(owner.Tick().Snapshot?.Travel==null&&!owner.CanTravel(NativePartyTravelAction.Follow),"expired travel retires from cached projection without HTTP on UI");
        Check(!NativePartyTravelValidation.ValidTarget(target with{ExpiresAt=DateTimeOffset.UtcNow},DateTimeOffset.UtcNow)
            &&!NativePartyTravelValidation.ValidTarget(target with{Host="server.invalid"},DateTimeOffset.UtcNow),"expired or nonliteral locator cannot enter native join owner");
    }
    sealed class Service:INativePartyTravelAuthority
    {
        internal object Lease=new();internal NativePartySnapshot Snapshot=null!;
        internal sealed record Sent(NativePartyTravelCommand Command,CancellationToken Token,TaskCompletionSource<NativePartyTravelResult> Reply);
        readonly BlockingCollection<Sent> _commands=new();
        public bool IsCurrent(NativePartySnapshot state)=>ReferenceEquals(typeof(NativePartySnapshot).GetProperty("AccountWitness",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(state),Lease);
        public Task<NativePartySnapshot> ReadAsync(CancellationToken token)=>Task.FromResult(Snapshot);
        public Task<bool> ExecuteAsync(NativePartyCommand command,CancellationToken token)=>Task.FromResult(false);
        public Task<NativePartyTravelResult> TravelAsync(NativePartyTravelCommand command,CancellationToken token)
        {var reply=new TaskCompletionSource<NativePartyTravelResult>(TaskCreationOptions.RunContinuationsAsynchronously);_commands.Add(new(command,token,reply));return reply.Task;}
        internal Sent Take()=>_commands.TryTake(out var sent,2000)?sent:throw new TimeoutException("No travel worker");
    }
}
