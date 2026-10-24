using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace MphRead.Mods.Render { internal static class FrameTiming { internal const int SimulationHz=60; } }
namespace MphRead.Mods.Network
{
internal static class NetConfig { internal const byte ProtocolVersion=86; }
internal static class UnifiedModeRules { internal const uint Capability=0x31464d55; }
internal static class NetConnection { private static long next=100;internal static ulong NewId()=>(ulong)Interlocked.Increment(ref next); }
internal enum SessionPhase:byte { Lobby,Starting,InMatch,PostMatch }
internal enum ServerSessionPolicy:byte { Continuous,Lobby }
internal struct NetTokenBucket { internal bool Take(double now,double rate,double burst)=>true; } // Boundary facade; bucket algorithm is outside these cases.
internal static class CareerReportOutbox { internal static bool Enabled=true;internal static bool AuthorizeSocialReport(HttpRequestMessage request)=>true; }
internal readonly record struct ConnectionSnapshot(ulong ConnectionId);
internal readonly struct ReceivedPacket
{
 internal readonly IPEndPoint Sender;internal readonly byte[] Data;internal readonly int Length;internal readonly ulong ConnectionId;
 internal ReceivedPacket(IPEndPoint sender,byte[] data,int length,ulong connectionId=0)=>(Sender,Data,Length,ConnectionId)=(sender,data,length,connectionId);
 internal PacketType Type=>(PacketType)Data[0];internal ReadOnlySpan<byte> Payload=>Data.AsSpan(1,Length-1);
}
// Closed connection/publication boundary. No Socket, UdpClient or network worker.
internal sealed class NetTransport
{
 internal readonly List<(IPEndPoint Endpoint,PacketType Type,byte[] Payload)> Sent=new();
 private readonly Dictionary<IPEndPoint,(ulong Id,bool Queue)> connections=new();
 internal void Bind(IPEndPoint endpoint,ulong id,bool queue)=>connections[endpoint]=(id,queue);
 internal ConnectionSnapshot? ConnectionStats(IPEndPoint endpoint)=>connections.TryGetValue(endpoint,out var c)?new(c.Id):null;
 internal ulong QueueConnectionId(IPEndPoint endpoint)=>connections.TryGetValue(endpoint,out var c)&&c.Queue?c.Id:0;
 internal void RetireConnection(IPEndPoint endpoint)=>connections.Remove(endpoint);
 internal void Send(IPEndPoint endpoint,PacketType type,ReadOnlySpan<byte> payload)
 {
  if(type==PacketType.QueueWelcome&&!connections.ContainsKey(endpoint))Bind(endpoint,NetConnection.NewId(),true);
  if(type==PacketType.Welcome&&connections.TryGetValue(endpoint,out var c))Bind(endpoint,c.Id,false);
  Sent.Add((endpoint,type,payload.ToArray()));
 }
}
internal sealed record OwnerSnapshot(bool Exists,bool Activated,bool Cancelled,string Reservation,ushort Holds,
 bool ClaimRequested,ulong ClaimNonce,bool Declined,bool Admitted,bool QueuePresent,bool Acknowledged,int PeerCount,int Admissions,int Running);
public sealed partial class DedicatedServer:IDisposable
{
 private sealed class Peer { internal IPEndPoint EndPoint=null!;internal uint ClientId;internal int SlotIndex;internal string CareerTicket="";internal bool Spectating=false; }
 private readonly List<Peer> _peers=new(),_bots=new();private readonly ushort[] _slotGenerations=new ushort[8];
 private readonly byte[] _scratch=new byte[1472];private NetTransport? _transport=new();private bool _controlPlaneOnlyForTests=false;
 private double _now;private ushort _matchId=1;private ulong _authorityEpoch=100;private SessionPhase _phase=SessionPhase.Lobby;
 private int _maxPlayers=8;private bool AllowJoinInProgress=false;private ServerSessionPolicy SessionPolicy=ServerSessionPolicy.Lobby;
 internal MatchDefinition CurrentDefinition {get;set;}=new(){Mode=MphRead.GameMode.Battle};
 private int admissions;private readonly List<TaskCompletionSource<PartyReservationReceipt>> fillers=new();
 internal readonly IPEndPoint LeaderEndpoint=new(IPAddress.Loopback,27100),MemberEndpoint=new(IPAddress.Loopback,27101);
 internal PartyReservationRequestPacket Identity {get;private set;}
 internal NetTransport Wire=>_transport!;
 internal DedicatedServer(ClosedBackend backend,int members=1)
 {
  _socialMembershipSequence=0;PartyReservationsEnabled=true;InitializeWaitlist();_partyCapability=true;_nextPartyCapability=double.MaxValue;
  _partyBackend=new(new HttpClient(backend),new Uri("https://closed.invalid/reservation"),_=>true);
  var leader=new Peer{EndPoint=LeaderEndpoint,ClientId=1,SlotIndex=0,CareerTicket=ClosedBackend.Ticket};
  _peers.Add(leader);_slotGenerations[0]=1;_transport!.Bind(LeaderEndpoint,10,false);
  Identity=new(1,PartyReservationPurpose.LeaderCreate,11,1,0,Guid.NewGuid().ToString("D"),Guid.NewGuid().ToString("D"),7,
   Guid.NewGuid().ToString("D"),1,_matchId,_authorityEpoch,ClosedBackend.Ticket);
  backend.Leader=Guid.NewGuid();backend.Members=Enumerable.Range(0,members).Select(_=>Guid.NewGuid()).ToArray();
 }
 // Closed publication boundary: retain the actual refusal codec and mode policy.
 private void SendRefusal(IPEndPoint endpoint,byte reason)
 {var refusal=new RefusedPacket{Reason=reason,Players=(byte)_peers.Count,MaxPlayers=(byte)_maxPlayers};refusal.Write(_scratch);_transport?.Send(endpoint,PacketType.Refused,_scratch.AsSpan(0,RefusedPacket.Size));}
 private Peer? Find(IPEndPoint endpoint)=>_peers.FirstOrDefault(p=>p.EndPoint.Equals(endpoint));
 // Explicit facade for the ordinary admission boundary. The real queue policy,
 // claim scheduler and owner callback execute unmodified; gameplay construction
 // is simulated here and counted to detect duplicate peer/session creation.
 private void HandleHello(ReceivedPacket packet,double now,int reservedSlot)
 {
  ++admissions;if(!_queueAdmitting||Find(packet.Sender)!=null||_peers.Any(p=>p.SlotIndex==reservedSlot))return;
  uint client=BinaryPrimitives.ReadUInt32LittleEndian(packet.Payload[2..]);
  _peers.Add(new(){EndPoint=packet.Sender,ClientId=client,SlotIndex=reservedSlot,CareerTicket=ClosedBackend.Ticket});
  ++_slotGenerations[reservedSlot];
  var payload=new byte[17];payload[0]=(byte)reservedSlot;BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(1),client);
  BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(5),_matchId);BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(7),_authorityEpoch);
  BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(15),_slotGenerations[reservedSlot]);_transport!.Send(packet.Sender,PacketType.Welcome,payload);
 }
 private static ReceivedPacket Receive(IPEndPoint endpoint,PacketType type,byte[] payload,ulong connection)
 { var data=new byte[payload.Length+1];data[0]=(byte)type;payload.CopyTo(data,1);return new(endpoint,data,data.Length,connection); }
 internal void StartLeader(){var b=new byte[Identity.EncodedSize];Identity.Write(b);BeginPartyRequest(Receive(LeaderEndpoint,PacketType.PartyReservationRequest,b,10),null,_now);}
 internal void Attach(int index=0)
 {
  var endpoint=index==0?MemberEndpoint:new IPEndPoint(IPAddress.Loopback,27101+index);uint client=(uint)(2+index);ulong nonce=(ulong)(50+index);
  var hello=new QueueHelloPacket(NetConfig.ProtocolVersion,client,nonce);var hb=new byte[hello.EncodedSize];hello.Write(hb);HandleQueueHello(Receive(endpoint,PacketType.QueueHello,hb,0),_now);
  if(!_queuePeers.TryGetValue(endpoint,out var peer))throw new Exception("fixture queue bootstrap refused");
  var request=Identity with{Purpose=PartyReservationPurpose.MemberAttach,OperationNonce=(ulong)(90+index),ClientId=client,QueueNonce=nonce};
  var bytes=new byte[request.EncodedSize];request.Write(bytes);HandleQueuePeer(Receive(endpoint,PacketType.PartyReservationRequest,bytes,_transport!.QueueConnectionId(endpoint)),_now);
 }
 internal void Action(PartyReservationWireAction action,bool leader=false)
 {
  var group=_partyGroups[Identity.RequestId];var receiver=leader?group.Leader:group.Members[0].Receiver!;
  var p=new PartyReservationActionPacket(1,action,receiver.Identity.OperationNonce,receiver.Revision,Identity.RequestId,
   group.Activated?group.Reservation:PartyReservationWire.ZeroId,receiver.Lease,_matchId,_authorityEpoch);
  var bytes=new byte[p.EncodedSize];p.Write(bytes);HandlePartyAction(Receive(receiver.Endpoint,PacketType.PartyReservationAction,bytes,receiver.Owner.ConnectionId),_now);
 }
 internal void Tick(double increment=.01){_now+=increment;MaintainWaitlist(_now);MaintainPartyReservations(_now);}
 internal bool ClaimCompleted=>_partyGroups.GetValueOrDefault(Identity.RequestId)?.Members[0].Claim?.Task.IsCompleted==true;
 internal bool ProbePresent=>_queuePeers.ContainsKey(new IPEndPoint(IPAddress.Loopback,29000));
 internal void ReplaceMemberConnection()=>_transport!.Bind(MemberEndpoint,NetConnection.NewId(),true);
 internal void AddBot(int slot)=>_bots.Add(new(){EndPoint=new(IPAddress.Loopback,30000+slot),SlotIndex=slot});
 internal void OccupyReleasedSlot(){_peers.Add(new(){EndPoint=new(IPAddress.Loopback,29999),ClientId=555,SlotIndex=1,CareerTicket=ClosedBackend.Ticket});++_slotGenerations[1];}
 internal void Rollover(){++_matchId;++_authorityEpoch;Tick();}
 internal int FillWorkers()
 {
  int added=0;while(_partyWorks.Count(w=>!w.Task.IsCompleted)<8)
  {var t=new TaskCompletionSource<PartyReservationReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);fillers.Add(t);
   if(BeginPartyWork(_=>t.Task,3.5)==null)throw new Exception("fixture saturation failed");++added;}return added;
 }
 internal void ReleaseWorkers(){foreach(var f in fillers)f.TrySetResult(PartyReservationReceipt.Refused);fillers.Clear();}
 internal bool CancellationRetryScheduled=>_partyGroups.TryGetValue(Identity.RequestId,out var group)
  &&group.Cancelled&&!group.CancellationAcknowledged&&group.Cancellation==null&&group.CancelAfter>_now;
 internal bool AcknowledgementRetryScheduled=>_partyGroups.TryGetValue(Identity.RequestId,out var group)
  &&group.Members.Any(member=>member.Admitted&&!member.Acknowledged&&member.Acknowledge==null&&member.AcknowledgeAfter>_now);
 internal OwnerSnapshot Snapshot()
 {
  var g=_partyGroups.GetValueOrDefault(Identity.RequestId);var m=g?.Members.FirstOrDefault();
  return new(g!=null,g?.Activated??false,g?.Cancelled??false,g?.Reservation??"",_waitlist!.ReservedSlots,
   m?.ClaimRequested??false,m?.ClaimNonce??0,m?.Declined??false,m?.Admitted??false,_queuePeers.ContainsKey(MemberEndpoint),
   m?.Acknowledged??false,_peers.Count,admissions,_partyWorks.Count(w=>!w.Task.IsCompleted));
 }
 internal PartyReservationStatePacket[] Publications()=>_transport!.Sent.Where(p=>p.Type==PacketType.PartyReservationState)
  .Select(p=>PartyReservationStatePacket.TryRead(p.Payload,out var s)?s:throw new Exception("invalid production state publication")).ToArray();
 internal void SeedVerifiedQueuePeers(int count)
 {
  for(int i=0;i<count;i++){var ep=new IPEndPoint(IPAddress.Loopback,28000+i);_queuePeers.Add(ep,new(){Endpoint=ep,ClientId=(uint)(100+i),Nonce=(ulong)(100+i),PartyRequest="verified-fixture-proof",CreatedAt=_now,LastSeen=_now});}
 }
 internal bool BootstrapProbe()
 {var ep=new IPEndPoint(IPAddress.Loopback,29000);var p=new QueueHelloPacket(NetConfig.ProtocolVersion,999,999);var b=new byte[p.EncodedSize];p.Write(b);HandleQueueHello(Receive(ep,PacketType.QueueHello,b,0),_now);return _queuePeers.ContainsKey(ep);}
 public void Dispose(){ReleaseWorkers();ShutdownPartyReservations();_socialMembership?.Dispose();}
}
internal sealed class ClosedBackend:HttpMessageHandler
{
 internal const string Ticket="pp1.closed-fixture-ticket-does-not-authenticate";
 internal Guid Leader;internal Guid[] Members=Array.Empty<Guid>();
 internal string? CommittedReservation;internal DateTimeOffset CommittedUntil,ClaimUntil;
 internal bool BackendCancelled;internal int RejectedReservationIds;
 internal bool HoldActivation=true,HoldClaim=true,HoldAcknowledge=false;internal int FailCancel,FailAck;
 internal readonly ConcurrentQueue<JsonElement> Requests=new();
 private readonly ConcurrentQueue<TaskCompletionSource<HttpResponseMessage>> activations=new(),claims=new(),acks=new();
 internal int Count(string action)=>Requests.Count(x=>x.GetProperty("action").GetString()==action);
 internal JsonElement Last(string action)=>Requests.Last(x=>x.GetProperty("action").GetString()==action);
 internal static HttpResponseMessage Reply(object value)=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(value))};
 protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
 {
  if(request.RequestUri?.Host!="closed.invalid")throw new Exception("fixture forbids external HTTP");
  using var document=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));var body=document.RootElement.Clone();
  string action=body.GetProperty("action").GetString()!;
  if(action=="activate"){CommittedReservation=body.GetProperty("reservation_id").GetString();CommittedUntil=DateTimeOffset.UtcNow.AddSeconds(12);}
  if(action=="claim_admission")ClaimUntil=DateTimeOffset.UtcNow.AddSeconds(1.8);
  Requests.Enqueue(body);
  if(action=="validate")return Reply(new{ok=true,status="pending",player_id=body.GetProperty("client_id").GetUInt32()==1?Leader:Members[body.GetProperty("client_id").GetInt32()-2],leader_id=Leader,members=Members,roster=Members.Prepend(Leader).ToArray(),expires_at=DateTimeOffset.UtcNow.AddSeconds(50)});
  if(action=="capability")return Reply(new{ok=true,native_v1=true,version=1});
  if(action=="cancel")
  {
   if(body.GetProperty("cancel_scope").GetString()=="group"&&CommittedReservation!=null&&body.GetProperty("reservation_id").GetString()!=CommittedReservation)
   {Interlocked.Increment(ref RejectedReservationIds);return Reply(new{ok=false,status="reservation_mismatch"});}
   bool ok=Interlocked.Decrement(ref FailCancel)<0;if(ok&&body.GetProperty("cancel_scope").GetString()=="group")BackendCancelled=true;
   return Reply(new{ok});
  }

  if(action=="admitted"&&!HoldAcknowledge)return Reply(new{ok=Interlocked.Decrement(ref FailAck)<0});
  if(action=="activate"&&!HoldActivation)return ActivationReply();
  if(action=="claim_admission"&&!HoldClaim)return ClaimReply();
  var completion=new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
  (action=="activate"?activations:action=="claim_admission"?claims:acks).Enqueue(completion);
  // Deliberately hold remote commitment independently of local cancellation.
  // Source relay checks token again before publishing a late response.
  return await completion.Task;
 }
 internal HttpResponseMessage ActivationReply()=>Reply(new{ok=true,status="reserved",expires_at=CommittedUntil});
 internal HttpResponseMessage ClaimReply()=>Reply(new{ok=true,status="admitting",claim_expires_at=ClaimUntil});
 internal void ReleaseActivation(){while(activations.TryDequeue(out var t))t.TrySetResult(ActivationReply());}
 internal void ReleaseClaim(){while(claims.TryDequeue(out var t))t.TrySetResult(ClaimReply());}
 internal void ReleaseAck(){while(acks.TryDequeue(out var t))t.TrySetResult(Reply(new{ok=true}));}
 protected override void Dispose(bool disposing){ReleaseActivation();ReleaseClaim();ReleaseAck();base.Dispose(disposing);}
}
}
