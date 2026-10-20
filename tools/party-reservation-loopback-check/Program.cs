using System.Buffers.Binary;
using System.Collections;
using System.Diagnostics;
using System.Net;
using System.Numerics;
using System.Reflection;
using System.Text;
using MphRead;
using MphRead.Mods.Network;

// No production source or authority boundary is replaced in this executable.
// Only HTTPS is a closed HttpMessageHandler. The actual server control methods
// run on this original thread, including ordinary HandleHello and actual Peer.
internal static class Program
{
    private static int _checks;
    internal static void Check(bool value,string name)
    { if(!value)throw new InvalidOperationException(name);++_checks; }
    internal static readonly BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    internal static object? Call(object owner,string method,params object?[] args)
        => owner.GetType().GetMethod(method,Hidden)!.Invoke(owner,args);
    internal static T Get<T>(object owner,string name)
        => (T)owner.GetType().GetField(name,Hidden)!.GetValue(owner)!;
    internal static void Set(object owner,string name,object value)
        => owner.GetType().GetField(name,Hidden)!.SetValue(owner,value);
    internal static T Peer<T>(object peer,string name)
        => (T)peer.GetType().GetField(name)!.GetValue(peer)!;
    internal static int Main()
    {
        try
        {
            Check(Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA") is {Length:>0},"private user data is explicit");
            Check(Environment.GetEnvironmentVariable("PROJECT_PRIME_CAREER_SERVER_KEY")==ClosedBackend.Reporter,
                "only explicit noncredential fixture reporter is enabled");
            Check(Environment.GetEnvironmentVariable("PROJECT_PRIME_SUPABASE_URL")=="https://closed.invalid/",
                "only explicit closed backend origin is authorized");
            Check(Environment.GetEnvironmentVariable("PROJECT_PRIME_SUPABASE_KEY")=="sb_publishable_fixture_only",
                "only explicit noncredential fixture API key is enabled");
            foreach(int cohort in new[]{2,4,8})for(int round=0;round<6;++round)Success(cohort,round);
            HeldActivationCancel(); CancelBeforeClaim(); ClaimCancellation(); RetiredClaim(); InvalidWire(); BootstrapBudget(); Fifo();
            Console.WriteLine($"PASS {_checks} actual-DLL loopback control integration assertions;2/4/8 local participants x6 requests; real UDP/transport/Peer/Welcome/TakeAdmission, closed HTTP only; no live backend/assets/physical devices");
            return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
        finally{NetSession.Stop();}
    }
    private static void Success(int count,int round)
    {
        using var rig=new Rig(count);rig.StartGroup();
        rig.Until(()=>rig.Backend.Count("activate")==1,"activation reaches closed boundary");
        Check(rig.Holds==((1<<count)-2)&&rig.Server.PeerCount==1,"actual capacity holds all missing slots without peers");
        Check(!rig.Leader.States.Any(s=>s.State==PartyReservationWireState.Reserved),"no Reserved before held activation reply");
        rig.Backend.ReleaseActivation();rig.Until(()=>rig.Leader.Latest?.State==PartyReservationWireState.Reserved,"actual leader receives activated Reserved");
        var queues=rig.AttachAll();rig.Until(()=>queues.All(q=>q.SeatAvailable),"all actual member queues receive live seats");
        var slots=queues.Select(q=>q.PartyState!.Value.SelfSlot).ToArray();
        Check(slots.Distinct().Count()==count-1&&slots.All(s=>s>0&&s<count),"all-or-none assignments are unique actual free slots");
        Check(queues.All(q=>!q.Admitted)&&rig.Server.PeerCount==1,"Reserved never silently admits a member");
        foreach(var queue in queues)Check(queue.AcceptPartySeat(),"explicit real queue Accept");
        rig.Until(()=>queues.All(q=>q.Admitted),"actual claims create actual Welcome for whole cohort");
        rig.Until(()=>rig.Backend.Count("admitted")==count-1&&rig.GroupCount==0,"actual peer acknowledgements settle group metadata");
        Check(rig.Server.PeerCount==count&&rig.Holds==0,"actual Peers consume exact holds once");
        foreach(var queue in queues)
        {
            NetTransport transport=queue.Transport;int port=transport.LocalPort;
            ulong incarnation=transport.ConnectionStats(rig.Endpoint)!.Value.ConnectionId;
            var admission=queue.TakeAdmission();var payload=admission.Welcome.Payload;
            Check(ReferenceEquals(admission.Transport,transport)&&admission.Transport.LocalPort==port
                &&admission.Welcome.ConnectionId==incarnation,"TakeAdmission retains exact real socket and connection incarnation");
            Check(payload[0]==slots[Array.IndexOf(queues,queue)]&&BinaryPrimitives.ReadUInt32LittleEndian(payload[1..])==queue.ClientId
                &&BinaryPrimitives.ReadUInt16LittleEndian(payload[5..])==rig.Identity.MatchId
                &&BinaryPrimitives.ReadUInt64LittleEndian(payload[7..])==rig.Identity.AuthorityEpoch
                &&BinaryPrimitives.ReadUInt16LittleEndian(payload[15..])!=0,"actual Welcome agrees on member/slot/match/epoch/generation endian layout");
            object peer=rig.Peers.Single(p=>Peer<uint>(p,"ClientId")==queue.ClientId);
            Check(Peer<int>(peer,"SlotIndex")==payload[0]
                &&rig.Generations[payload[0]]==BinaryPrimitives.ReadUInt16LittleEndian(payload[15..]),"ordinary production Peer owns that actual occupant generation");
            queue.Dispose();Check(admission.Transport.LocalPort==port,"disposed drained queue cannot dispose adopted transport");
            rig.Adoptions.Add(admission.Transport);
        }
        Check(rig.Backend.Errors.IsEmpty,"closed HTTP accepted exact unchanged authority/claim witnesses");
        Console.WriteLine($"PASS actual UDP cohort{count} request{round+1}: live Peers{rig.Server.PeerCount}, exact transport transfers{queues.Length}");
    }
    private static void HeldActivationCancel()
    {
        using var rig=new Rig(4);rig.StartGroup();rig.Until(()=>rig.Backend.Count("activate")==1,"held activation committed");
        string id=rig.Backend.Reservation!;rig.Cancel();rig.Until(()=>rig.Backend.Cancelled&&rig.Holds==0,"cancel before activation reply releases real holds");
        rig.Backend.ReleaseActivation();rig.PumpFor(80);
        Check(rig.Backend.Reservation==id&&rig.GroupCount==0&&rig.Server.PeerCount==1
            &&!rig.Leader.States.Any(s=>s.State==PartyReservationWireState.Reserved),"late activation cannot revive cancelled actual queue/slot owner");
    }
    private static void ClaimCancellation()
    {
        using var rig=new Rig(2);rig.StartGroup();rig.Activate();var queue=rig.AttachAll()[0];rig.Until(()=>queue.SeatAvailable,"claim race member Reserved");
        rig.Backend.HoldClaims=true;Check(queue.AcceptPartySeat(),"claim race explicit Accept");
        rig.Until(()=>rig.Backend.Count("claim_admission")==1,"claim commits before group cancel");
        rig.Cancel();rig.Until(()=>rig.Backend.Cancelled,"cancel records after short claim");
        Check(!queue.Admitted&&rig.Server.PeerCount==1,"committed HTTP claim alone is not actual admission");
        rig.Backend.ReleaseClaims();rig.Until(()=>queue.Admitted&&rig.Server.PeerCount==2,"already committed short claim settles after cancel");
        rig.Until(()=>rig.Backend.Count("admitted")==1,"post-cancel actual Peer acknowledged once");
    }
    private static void CancelBeforeClaim()
    {
        using var rig=new Rig(2);rig.StartGroup();rig.Activate();var queue=rig.AttachAll()[0];rig.Until(()=>queue.SeatAvailable,"cancel-before-claim member Reserved");
        var state=queue.PartyState!.Value;rig.Cancel();rig.Until(()=>rig.Backend.Cancelled&&rig.Holds==0,"group cancellation linearizes before a claim");
        var stale=new PartyReservationActionPacket(1,PartyReservationWireAction.Accept,state.OperationNonce,state.Revision,
            state.RequestId,state.ReservationId,state.MemberLeaseNonce,state.MatchId,state.AuthorityEpoch);
        var bytes=new byte[stale.EncodedSize];stale.Write(bytes);queue.Transport.Send(rig.Endpoint,PacketType.PartyReservationAction,bytes);
        rig.PumpFor(60);Check(rig.Backend.Count("claim_admission")==0&&rig.Server.PeerCount==1,"late exact Accept after group cancel cannot start a claim or create a Peer");
    }
    private static void RetiredClaim()
    {
        using var rig=new Rig(2);rig.StartGroup();rig.Activate();var queue=rig.AttachAll()[0];rig.Until(()=>queue.SeatAvailable,"replacement member Reserved");
        rig.Backend.HoldClaims=true;queue.AcceptPartySeat();rig.Until(()=>rig.Backend.Count("claim_admission")==1,"retired claim held after commitment");
        rig.Wire.RetireConnection(new IPEndPoint(IPAddress.Loopback,queue.Transport.LocalPort));
        rig.Until(()=>rig.Holds==0,"retired queue releases its exact slot hold");
        using var replacement=new WirePeer(rig.Endpoint,9901);rig.Until(()=>replacement.Slot>=0,"new real peer reuses released capacity",replacement.Drain);
        ushort generation=rig.Generations[replacement.Slot];rig.Backend.ReleaseClaims();rig.PumpFor(80);
        Check(rig.Server.PeerCount==2&&rig.Holds==0&&rig.Backend.Count("admitted")==0
            &&rig.Peers.Any(p=>Peer<uint>(p,"ClientId")==9901&&Peer<int>(p,"SlotIndex")==replacement.Slot)
            &&rig.Generations[replacement.Slot]==generation,"late real claim cannot admit retired queue or remove its replacement occupant");
    }
    private static void InvalidWire()
    {
        using var rig=new Rig(2);
        var bytes=new byte[rig.Identity.EncodedSize];rig.Identity.Write(bytes);
        // Existing parser/owner must reject malformed endian and changed epoch,
        // without authorizing even one backend validation worker.
        var wrong=rig.Identity with{AuthorityEpoch=rig.Identity.AuthorityEpoch^0x100};rig.Leader.Request(wrong);
        rig.Leader.Request(rig.Identity with{MatchId=(ushort)(rig.Identity.MatchId<<8)});
        rig.Leader.Send(PacketType.PartyReservationRequest,bytes.AsSpan(0,bytes.Length-1).ToArray());
        rig.PumpFor(80);Check(rig.Backend.Count("validate")==0&&rig.GroupCount==0,"actual wire stale epoch/endian/truncation refuses before backend work");
        rig.StartGroup();rig.Activate();var queue=rig.AttachAll()[0];rig.Until(()=>queue.SeatAvailable,"stale action seat available");
        var state=queue.PartyState!.Value;
        var stale=new PartyReservationActionPacket(1,PartyReservationWireAction.Accept,state.OperationNonce,state.Revision,
            state.RequestId,state.ReservationId,state.MemberLeaseNonce^1,state.MatchId,state.AuthorityEpoch);
        var action=new byte[stale.EncodedSize];stale.Write(action);queue.Transport.Send(rig.Endpoint,PacketType.PartyReservationAction,action);
        rig.PumpFor(60);Check(rig.Backend.Count("claim_admission")==0&&rig.Server.PeerCount==1,"real wire wrong captured lease cannot claim a slot");
    }
    private static void BootstrapBudget()
    {
        using var rig=new Rig(2);var peers=new List<WirePeer>();
        try
        {
            for(uint i=0;i<24;++i){var peer=new WirePeer(rig.Endpoint,8000+i,false);peers.Add(peer);peer.QueueHello(9000+i);}
            rig.PumpFor(50);Check(rig.QueueCount<=16&&rig.QueueCount>0,"actual token bucket/unproven budget bounds UDP bootstrap burst");
            Check(rig.Server.PeerCount==1&&rig.Backend.Count("validate")==0&&rig.Holds==0,"unproven real sockets allocate no player slots/backend workers");
            rig.Advance(5.1);Check(rig.QueueCount==0,"actual unproven sockets expire at absolute5sec lifetime");
        }
        finally{foreach(var peer in peers)peer.Dispose();}
    }
    private static void Fifo()
    {
        using var rig=new Rig(4);var blockers=new List<WirePeer>();
        try
        {
            for(uint i=0;i<5;++i){var p=new WirePeer(rig.Endpoint,5000+i);blockers.Add(p);rig.Until(()=>p.Slot>=0,"actual blocker admitted",p.Drain);}
            rig.StartGroup();rig.Until(()=>rig.GroupCount==1,"group joins shared real FIFO");
            using var younger=new LobbyQueueClient(rig.Endpoint,6000);rig.ExtraPoll=younger.Poll;
            rig.Until(()=>younger.Position==2,"younger solo follows actual group FIFO entry");
            Check(rig.Holds==0&&rig.Backend.Count("activate")==0&&!younger.SeatAvailable,"insufficient actual capacity holds no partial group and cannot bypass head");
            blockers[0].Dispose();rig.Until(()=>rig.Backend.Count("activate")==1,"one real departure makes whole group fit");
            Check(BitOperations.PopCount(rig.Holds)==3&&!younger.SeatAvailable,"older group takes all newly sufficient seats before younger solo");
        }
        finally{rig.ExtraPoll=null;foreach(var peer in blockers)peer.Dispose();}
    }

    internal sealed class Rig:IDisposable
    {
        internal readonly DedicatedServer Server;internal readonly NetTransport Wire=new(0);
        internal readonly ClosedBackend Backend;internal readonly WirePeer Leader;internal readonly IPEndPoint Endpoint;
        internal readonly PartyReservationRequestPacket Identity;internal readonly List<LobbyQueueClient> Queues=new();
        internal readonly List<NetTransport> Adoptions=new();internal Action? ExtraPoll;
        private readonly Stopwatch clock=Stopwatch.StartNew();private double offset;
        internal ushort Holds=>Get<LobbyWaitlist>(Server,"_waitlist").ReservedSlots;
        internal int GroupCount=>Get<IDictionary>(Server,"_partyGroups").Count;
        internal int QueueCount=>Get<IDictionary>(Server,"_queuePeers").Count;
        internal object[] Peers=>Get<IList>(Server,"_peers").Cast<object>().ToArray();
        internal ushort[] Generations=>Get<ushort[]>(Server,"_slotGenerations");
        internal Rig(int count)
        {
            Server=new DedicatedServer(0,8,MapRotation.SingleMatch("MP1 SANCTORUS",GameMode.Battle,0,0))
                {SessionPolicy=ServerSessionPolicy.Lobby,PartyReservationsEnabled=true};
            Set(Server,"_controlPlaneOnlyForTests",true);Set(Server,"_transport",Wire);
            Set(Server,"_phase",SessionPhase.Lobby);Set(Server,"_lobbyMatch",new MatchDefinition{RoomKey="MP1 SANCTORUS",Mode=GameMode.Battle});
            Call(Server,"InitializeWaitlist");Endpoint=new(IPAddress.Loopback,Wire.LocalPort);
            Backend=new(count);Identity=new(1,PartyReservationPurpose.LeaderCreate,NetConnection.NewId(),1001,0,
                Guid.NewGuid().ToString("D"),Guid.NewGuid().ToString("D"),7,Guid.NewGuid().ToString("D"),1,
                Get<ushort>(Server,"_matchId"),Get<ulong>(Server,"_authorityEpoch"),ClosedBackend.Ticket(1001));
            Backend.Identity=Identity;
            Set(Server,"_partyBackend",new SocialPartyReservationRelay(new HttpClient(Backend),new Uri("https://closed.invalid/reservation"),CareerReportOutbox.AuthorizeSocialReport));
            Leader=new(Endpoint,1001);Until(()=>Leader.Slot>=0,"actual persistent leader admitted");
            Leader.Send(PacketType.CareerIdentity,Encoding.ASCII.GetBytes(Identity.CareerTicket));
            Until(()=>Peers.Any(p=>Peer<uint>(p,"ClientId")==1001&&Peer<string>(p,"CareerTicket")==Identity.CareerTicket),"actual admitted leader ticket captured");
            Until(()=>Get<bool>(Server,"_partyCapability"),"actual worker verifies optional closed HTTP capability");
        }
        internal void Tick()
        {
            double now=clock.Elapsed.TotalSeconds+offset;Set(Server,"_now",now);
            Call(Server,"RefreshWaitlist",now);
            foreach(var packet in Wire.Drain(NetPumpBudget.BeforeSimulation))Call(Server,"Handle",packet,now);
            foreach(var packet in Wire.Drain(NetPumpBudget.AfterSimulation))Call(Server,"Handle",packet,now);
            Call(Server,"MaintainWaitlist",now);
            // Only this actual maintenance method sees the normal flag. It runs
            // real capability/auth checks against the injected closed HTTP owner.
            // Other owner methods retain the existing asset-free test seam.
            Set(Server,"_controlPlaneOnlyForTests",false);
            try{Call(Server,"MaintainPartyReservations",now);}finally{Set(Server,"_controlPlaneOnlyForTests",true);}
            Leader?.Drain();foreach(var queue in Queues)queue.Poll();ExtraPoll?.Invoke();
        }
        internal void Until(Func<bool> condition,string label,Action? extra=null,int ms=2500)
        {
            var timeout=Stopwatch.StartNew();do{Tick();extra?.Invoke();if(condition()){Check(true,label);return;}Thread.Sleep(1);}while(timeout.ElapsedMilliseconds<ms);
            throw new InvalidOperationException($"timeout: {label}; peers{Server.PeerCount} groups{GroupCount} holds{Holds} queues{QueueCount}; HTTP {string.Join(',',Backend.Actions)}; errors{string.Join(',',Backend.Errors)}");
        }
        internal void PumpFor(int ms){var stop=Stopwatch.StartNew();do{Tick();Thread.Sleep(1);}while(stop.ElapsedMilliseconds<ms);}
        internal void Advance(double seconds){offset+=seconds;Tick();}
        internal void StartGroup(){Leader.Request(Identity);}
        internal void Activate(){Until(()=>Backend.Count("activate")==1,"group activation pending");Backend.ReleaseActivation();Until(()=>Leader.Latest?.State==PartyReservationWireState.Reserved,"leader live group Reserved");}
        internal LobbyQueueClient[] AttachAll()
        {
            for(uint index=0;index<Backend.Members.Length;++index)
            {
                uint client=2001+index;var request=Identity with{Purpose=PartyReservationPurpose.MemberAttach,ClientId=client,
                    OperationNonce=NetConnection.NewId(),QueueNonce=1,CareerTicket=ClosedBackend.Ticket(client)};
                Queues.Add(new(Endpoint,client,request));
            }
            return Queues.ToArray();
        }
        internal void Cancel()
        {
            Until(()=>Leader.Latest.HasValue,"current leader cancellation witness");var state=Leader.Latest!.Value;
            var cancel=new PartyReservationActionPacket(1,PartyReservationWireAction.CancelGroup,state.OperationNonce,
                state.Revision,state.RequestId,state.ReservationId,state.MemberLeaseNonce,state.MatchId,state.AuthorityEpoch);
            var bytes=new byte[cancel.EncodedSize];cancel.Write(bytes);Leader.Send(PacketType.PartyReservationAction,bytes);
        }
        public void Dispose()
        {
            Backend.ReleaseActivation();Backend.ReleaseClaims();
            foreach(var queue in Queues)queue.Dispose();foreach(var transport in Adoptions)transport.Dispose();Leader.Dispose();
            Call(Server,"ShutdownPartyReservations");Wire.Dispose();Backend.Dispose();
        }
    }
    internal sealed class WirePeer:IDisposable
    {
        internal readonly NetTransport Transport=new(0);internal readonly IPEndPoint Endpoint;internal readonly uint Id;
        internal int Slot=-1;internal readonly List<PartyReservationStatePacket> States=new();
        internal PartyReservationStatePacket? Latest=>States.Count==0?null:States[^1];
        private bool disposed;
        internal WirePeer(IPEndPoint endpoint,uint id,bool hello=true)
        {Endpoint=endpoint;Id=id;Transport.AnswerPingsImmediately();if(hello){var b=new byte[6];b[0]=NetConfig.ProtocolVersion;b[1]=255;BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(2),id);Send(PacketType.Hello,b);}}
        internal void Send(PacketType type,byte[] bytes)=>Transport.Send(Endpoint,type,bytes);
        internal void Request(PartyReservationRequestPacket request){var bytes=new byte[request.EncodedSize];request.Write(bytes);Send(PacketType.PartyReservationRequest,bytes);}
        internal void QueueHello(ulong nonce){var p=new QueueHelloPacket(NetConfig.ProtocolVersion,Id,nonce);var bytes=new byte[p.EncodedSize];p.Write(bytes);Send(PacketType.QueueHello,bytes);}
        internal void Drain(){if(disposed)return;foreach(var packet in Transport.Drain()){if(packet.Type==PacketType.Welcome)Slot=packet.Payload[0];if(packet.Type==PacketType.PartyReservationState&&PartyReservationStatePacket.TryRead(packet.Payload,out var s))States.Add(s);}}
        public void Dispose(){if(disposed)return;disposed=true;Send(PacketType.Bye,Array.Empty<byte>());Transport.Dispose();}
    }
}
