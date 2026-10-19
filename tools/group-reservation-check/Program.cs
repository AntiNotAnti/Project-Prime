using System.Net;
using System.Text.Json;
using MphRead.Mods.Network;

int checks=0;
void Check(bool condition,string label) { if(!condition)throw new Exception(label); ++checks; }
string Id()=>Guid.NewGuid().ToString("D");
var p=new PartyReservationRequestPacket(1,PartyReservationPurpose.MemberAttach,1,uint.MaxValue,2,
    Id(),Id(),9007199254740991,Id(),1000000,ushort.MaxValue,ulong.MaxValue,"pp1."+new string('x',764));
Check(p.Validate(),"maximum request valid");
Check(PartyReservationRequestPacket.MaximumSize==929 && p.EncodedSize==929,"bounded maximum request929");
Check(24+4+p.EncodedSize<=1472,"MTU with reliable/header overhead");
byte[] data=new byte[p.EncodedSize];p.Write(data);
Check(data[0]==80&&PartyReservationRequestPacket.TryRead(data,out var decoded)&&decoded==p,"actual generated max request roundtrip");
for(int i=0;i<data.Length;i++)Check(!PartyReservationRequestPacket.TryRead(data.AsSpan(0,i),out _),"every truncated request rejected");
Check(!PartyReservationRequestPacket.TryRead(data.Concat(new byte[]{0}).ToArray(),out _),"trailing request rejected");
foreach(var bad in new[]{p with{Version=2},p with{Purpose=(PartyReservationPurpose)99},p with{OperationNonce=0},p with{QueueNonce=0},p with{AuthorityEpoch=0},p with{MatchId=0},p with{PartyRevision=9007199254740992},p with{CareerTicket="pp1."+new string('x',765)},p with{RequestId="bad"},p with{TravelId=PartyReservationWire.ZeroId},p with{CareerTicket="pp1."+new string('x',20)+"\n"}})Check(!bad.Validate(),"invalid identity refused");
Check(!p.ToString().Contains(p.CareerTicket),"ticket hidden in record string");
var state=new PartyReservationStatePacket(1,1,1,p.RequestId,Id(),p.PartyRevision,p.TravelId,p.TravelRevision,p.MatchId,p.AuthorityEpoch,PartyReservationWireState.Reserved,3,7,900,42,1,1,1);
byte[] stateBytes=new byte[state.EncodedSize];state.Write(stateBytes);
Check(state.EncodedSize==177&&PartyReservationStatePacket.TryRead(stateBytes,out var stateDecoded)&&stateDecoded==state,"state177 roundtrip");
Check(!(state with{RemainingTicks=0}).Validate()&&!(state with{MemberLeaseNonce=0}).Validate(),"Reserved needs live deadline/member lease");
var action=new PartyReservationActionPacket(1,PartyReservationWireAction.Accept,1,1,p.RequestId,state.ReservationId,42,p.MatchId,p.AuthorityEpoch);
var ab=new byte[action.EncodedSize];action.Write(ab);Check(ab.Length==109&&PartyReservationActionPacket.TryRead(ab,out var ad)&&ad==action,"action109 roundtrip");
Check(!(action with{MemberLeaseNonce=0}).Validate()&&!(action with{Action=(PartyReservationWireAction)99}).Validate(),"unproved action rejected");

LobbyQueueConnection Owner(int n)=>new("127.0.0.1",1000+n,(ulong)n);
foreach(int cohort in new[]{2,4,8})
{
 var q=new LobbyWaitlist();ulong retired=0;
 for(int round=1;round<=6;round++)
 {
  q.Update(round*20,8,1,true,(uint)round,(ulong)round);
  Check(q.TryJoinGroup(Owner(10),Id(),cohort-1,(uint)round,(ulong)round,round*20+20,round*20,out var g),"virtual cohort enters current generation");
  Check(System.Numerics.BitOperations.PopCount((uint)g.Slots)==cohort-1,"all missing cohort seats held atomically");
  Check(!q.ActivateGroup(g.QueueId,(uint)round,(ulong)(round+1),round*20+10,round*20),"delayed mismatched epoch activation cannot activate current hold");
  if(retired!=0)Check(!q.ActivateGroup(retired,(uint)(round-1),(ulong)(round-1),round*20+10,round*20),"retired queue incarnation cannot reactivate after rotation");
  Check(q.ActivateGroup(g.QueueId,(uint)round,(ulong)round,round*20+10,round*20),"current backend acknowledgement activates cohort");
  for(int slot=0;slot<8;slot++)if((g.Slots&(1<<slot))!=0)Check(q.TryAcceptGroup(g.QueueId,slot,(uint)round,(ulong)round,round*20+1,_=>true),"cohort member consumes only exact reserved seat");
  Check(q.ReservedSlots==0&&!q.TryGetGroup(g.QueueId,out _),"complete cohort retires reservation without holding admitted players");retired=g.QueueId;
 }
}
for(int rotation=1;rotation<=6;rotation++)
{
    var q=new LobbyWaitlist(32,15,10);q.Update(0,8,0b11,true,(uint)rotation,(ulong)rotation);
    Check(q.TryJoin(Owner(1),1,0,out var older),"older single");
    Check(q.TryJoinGroup(Owner(2),Id(),4,(uint)rotation,(ulong)rotation,20,0,out var group),"group joins same FIFO");
    Check(group.Slots==0b1111000&&q.ReservedSlots==0b1111100,"older solo plus all4 group held without overlap");
    Check(q.TryJoin(Owner(3),3,0,out var newer)&&newer.Offer?.Slot==7,"newer solo uses only remaining seat");
    Check(!q.CanDirectJoin(3)&&!q.CanDirectJoin(7),"direct/bot admission cannot consume any hold");
    Check(!q.TryAcceptGroup(group.QueueId,3,(uint)rotation,(ulong)rotation,1,_=>true),"backend activation required");
    Check(q.ActivateGroup(group.QueueId,(uint)rotation,(ulong)rotation,10,1),"backend acknowledgement activates existing hold");
    Check(!q.TryAcceptGroup(group.QueueId,3,(uint)rotation,(ulong)rotation+1,1,_=>true),"stale epoch cannot consume");
    Check(q.TryAcceptGroup(group.QueueId,3,(uint)rotation,(ulong)rotation,1,slot=>slot==3),"ordinary admission consumes exact held slot");
    Check((q.ReservedSlots&8)==0&&q.ReservedSlots==0b11110100,"admitted slot removed from reserved mask");
    q.CancelGroup(group.QueueId,2);Check(q.ReservedSlots==0b10000100,"cancel preserves older/newer single holds");
    Check(!q.TryAcceptGroup(group.QueueId,4,(uint)rotation,(ulong)rotation,2,_=>true),"late group action cannot resurrect");
}
{
 var q=new LobbyWaitlist();q.Update(0,4,0b11,true,1,1);
 Check(q.TryJoinGroup(Owner(1),Id(),3,1,1,20,0,out var g)&&g.Slots==0,"insufficient capacity holds no partial seats");
 Check(q.TryJoin(Owner(2),2,0,out var s)&&s.Offer==null&&!q.CanDirectJoin(2),"strict FIFO head blocks newer solo and direct join");
 q.Update(20,4,0b11,true,1,1);Check(!q.TryGetGroup(g.QueueId,out _)&&q.TryGetState(Owner(2),s.QueueId,out var released)&&released.Offer?.Slot==2,"fixed head deadline releases queue");
}
{
 var q=new LobbyWaitlist();q.Update(0,8,1,true,1,1);q.TryJoinGroup(Owner(1),Id(),7,1,1,20,0,out var g);
 q.Update(1,8,1,true,2,2);Check(!q.TryGetGroup(g.QueueId,out _)&&q.ReservedSlots==0,"rotation retires entire group mask");
}

var handler=new ClosedHttp();var relay=new SocialPartyReservationRelay(new HttpClient(handler),new("https://example.invalid/reservations"),_=>true);
handler.Reply="{\"ok\":true,\"native_v1\":true,\"version\":1}";Check((await relay.CapabilityAsync(default)).Capability,"explicit backend capability");
handler.Reply="{\"ok\":true}";Check(!(await relay.CapabilityAsync(default)).Capability,"legacy ok cannot enable capability");
handler.Reply="{\"ok\":true,\"player_id\":\""+Guid.NewGuid()+"\",\"leader_id\":\""+Guid.NewGuid()+"\",\"members\":[],\"roster\":[]}";
await relay.ReportAsync(p,"claim_admission",state.ReservationId,DateTimeOffset.UtcNow.AddSeconds(2),default,slot:3,connection:ulong.MaxValue,claim:ulong.MaxValue);
using(var j=JsonDocument.Parse(handler.Body)){var r=j.RootElement;Check(r.GetProperty("native_v1").GetBoolean()&&r.GetProperty("operation_generation").GetInt32()==1,"native generation explicit");Check(r.GetProperty("connection_id").GetString()==ulong.MaxValue.ToString()&&r.GetProperty("claim_nonce").GetString()==ulong.MaxValue.ToString(),"full64bit proof encoded exact strings");Check(r.GetProperty("career_ticket").GetString()==p.CareerTicket,"opaque ticket forwarded to existing verifier");Check(!r.TryGetProperty("player_id",out _),"client UUID never supplied as authentication");}
handler.Reply=new string(' ',8193);Check(!(await relay.CapabilityAsync(default)).Ok,"oversized response rejected");
int before=handler.Calls;var insecure=new SocialPartyReservationRelay(new HttpClient(handler),new("http://example.invalid"),_=>true);Check(!(await insecure.CapabilityAsync(default)).Ok&&handler.Calls==before,"no credential to HTTP");
checks+=QueueClientAcceptance.Run();checks+=await CareerTicketAcceptance.Run();
Console.WriteLine($"PASS {checks} source-linked group codec/FIFO/worker/queue/ticket checks; closed HTTP/transport only, no backend or socket");

sealed class ClosedHttp:HttpMessageHandler
{ public string Reply="{}",Body="";public int Calls;protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req,CancellationToken token){++Calls;Body=await req.Content!.ReadAsStringAsync(token);return new(HttpStatusCode.OK){Content=new StringContent(Reply)};} }
