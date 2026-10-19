using System.Text.Json;
using MphRead.Mods.Launcher;

int count=0;
void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("TRAVEL ADAPTER PASS "+label);++count;}
async Task<bool> Reject(Func<Task> invoke){try{await invoke();return false;}catch(InvalidOperationException){return true;}}
string self="PP-AAAA-BBBB-CCCC-DDDD-0001",leader="PP-AAAA-BBBB-CCCC-DDDD-0002";
string party="00000001-1111-4111-8111-111111111111",travel="00000002-1111-4111-8111-111111111111",lobby="00000003-1111-4111-8111-111111111111";
var wire=new AuthenticatedNativePartyAuthority.PartyWireSnapshot {SelfPrimeId=self,PartyId=party,LeaderPrimeId=leader,ServerRevision=7,
 Members=new(){new(){PrimeId=self,DisplayName="Fixture follower"},new(){PrimeId=leader,DisplayName="Fixture leader"}},Incoming=new(),Outgoing=new(),
 Travel=new(){TravelId=travel,Revision=3,LeaderPrimeId=leader,LobbyId=lobby,RoomKey="SYNTHETIC ROOM",ServerName="Fixture",AuthorityEpoch="18446744073709551615",ExpiresAt=DateTimeOffset.UtcNow.AddSeconds(60),SelfStatus="pending"}};
PartyLeaseAdapterAcceptance.Run(wire);
var first=HunterLicenseClient.NewFixtureLease();var sameIdentityReplacement=HunterLicenseClient.NewFixtureLease();
HunterLicenseClient.SetFixtureLease(sameIdentityReplacement);
Check(await Reject(async()=>{await HunterLicenseClient.InvokeNativeFunctionWithLeaseAsync<object>("party",new{},default,first);})
 &&HunterLicenseClient.AuthCalls==0&&HunterLicenseClient.FunctionCalls==0,"replacement lease rejects captured mutation before authentication/delegate");
HunterLicenseClient.SetFixtureLease(first);HunterLicenseClient.Replacement=sameIdentityReplacement;
Check(await Reject(async()=>{await HunterLicenseClient.InvokeNativeFunctionWithLeaseAsync<object>("party",new{},default,first);})
 &&HunterLicenseClient.AuthCalls==1&&HunterLicenseClient.FunctionCalls==0,"Authenticate refresh to equivalent account retires captured lease before delegate");
HunterLicenseClient.SetFixtureLease(first);
var normal=await HunterLicenseClient.InvokeNativeFunctionWithLeaseAsync<object>("party",new{},default);
Check(ReferenceEquals(normal.Lease,first)&&HunterLicenseClient.FunctionCalls==1,"existing callers retain default lease behavior");
HunterLicenseClient.SetFixtureLease(first);int sends=0;var heldTicket=new TaskCompletionSource();
var afterTicket=Task.Run(async()=>{await heldTicket.Task;return await HunterLicenseClient.ExecuteNativeLeaseActionAsync(first,()=>{++sends;return true;},default);});
HunterLicenseClient.SetFixtureLease(sameIdentityReplacement);heldTicket.SetResult();
Check(!await afterTicket&&sends==0,"held post-ticket continuation rejects retired account before local UDP/adoption action");
HunterLicenseClient.SetFixtureLease(first);
Check(await HunterLicenseClient.ExecuteNativeLeaseActionAsync(first,()=>{++sends;return true;},default)&&sends==1,"current local publication runs once under existing account gate");
using(var cancelled=new CancellationTokenSource()){cancelled.Cancel();try{await HunterLicenseClient.ExecuteNativeLeaseActionAsync(first,()=>{++sends;return true;},cancelled.Token);throw new Exception("expected cancel");}catch(OperationCanceledException){Check(sends==1,"cancelled local publication cannot send or adopt");}}
HunterLicenseClient.SetFixtureLease(first);
var authority=new AuthenticatedNativePartyAuthority();
var local=new NativePartyLocalLobby(42,ulong.MaxValue,"SYNTHETIC ROOM","");
MphRead.Mods.Network.NetSession.Active=MphRead.Mods.Network.NetSession.IsClient=MphRead.Mods.Network.NetSession.PersistentLobby=true;
MphRead.Mods.Network.NetSession.ServerSession=new(){Phase=MphRead.Mods.Network.SessionPhase.Lobby,MatchId=42,AuthorityEpoch=ulong.MaxValue,Match=new(){RoomKey="SYNTHETIC ROOM"}};
MphRead.Mods.Network.NetSession.LocalSlot=0;MphRead.Mods.Network.NetSession.SemanticLegacyPlayback=true;
Check(!AuthenticatedNativePartyAuthority.MatchesLocalLobby(local),"replay client role cannot begin backend leader intent or Publish");
MphRead.Mods.Network.NetSession.SemanticLegacyPlayback=false;MphRead.Mods.Network.NetSession.LocalSlot=-1;
Check(!AuthenticatedNativePartyAuthority.MatchesLocalLobby(local),"unadmitted client role cannot begin backend leader intent or Publish");
MphRead.Mods.Network.NetSession.LocalSlot=0;
Check(AuthenticatedNativePartyAuthority.MatchesLocalLobby(local),"accepted live client identity may verify current persistent lobby");
MphRead.Mods.Network.NetSession.Active=MphRead.Mods.Network.NetSession.IsClient=MphRead.Mods.Network.NetSession.PersistentLobby=false;
MphRead.Mods.Network.NetSession.ServerSession=null;
var command=new NativePartyTravelCommand(NativePartyTravelAction.Follow,self,party,7,travel,3){AccountWitness=first};
Check(await Reject(async()=>{await authority.TravelAsync(command with{AccountWitness=null},default);})&&HunterLicenseClient.AuthCalls==0,"travel requires captured opaque lease before authenticated work");
var locator=new AuthenticatedNativePartyAuthority.PartyWireLocator {LobbyId=lobby,Host="203.0.113.4",Port=27015,Protocol=57,AuthorityEpoch=ulong.MaxValue.ToString(),RoomKey="SYNTHETIC ROOM",ExpiresAt=DateTimeOffset.UtcNow.AddSeconds(60)};
ushort expectedQueryMatch=42;
HunterLicenseClient.Reply=body=>{
 var request=(AuthenticatedNativePartyAuthority.PartyWireRequest)body;
 Check(request.Action=="follow_travel"&&request.ExpectedSelfPrimeId==self&&request.ExpectedRevision==7&&request.PartyId==party
  &&request.TravelId==travel&&request.TravelRevision==3,"actual adapter emits exact account/party/travel witnesses");
 return new AuthenticatedNativePartyAuthority.PartyWireEnvelope {Ok=true,Snapshot=wire,Locator=locator};
};
var result=await authority.TravelAsync(command,default);
Check(result.JoinTarget is {AuthorityEpoch:ulong.MaxValue}&&ReferenceEquals(result.JoinTarget.AccountWitness,first)
 &&ReferenceEquals(result.Snapshot.AccountWitness,first),"actual adapter preserves unsigned epoch and exact opaque response lease");
string json=JsonSerializer.Serialize(command)+JsonSerializer.Serialize(result.JoinTarget);
Check(!json.Contains("AccountWitness")&&!json.Contains("AccessToken")&&!json.Contains("RefreshToken"),"travel command and locator never serialize account witness or credentials");
foreach(var bad in new Action[]{()=>locator.Protocol=56,()=>locator.Host="untrusted.invalid",()=>locator.LobbyId=party,
 ()=>locator.AuthorityEpoch="18446744073709551616",()=>locator.RoomKey="OTHER",()=>locator.ExpiresAt=DateTimeOffset.UtcNow.AddSeconds(-1)}) {
 locator.Protocol=57;locator.Host="203.0.113.4";locator.LobbyId=lobby;locator.AuthorityEpoch=ulong.MaxValue.ToString();locator.RoomKey="SYNTHETIC ROOM";locator.ExpiresAt=DateTimeOffset.UtcNow.AddSeconds(60);
 bad();Check(await Reject(async()=>{await authority.TravelAsync(command,default);}),"malformed or changed locator is rejected "+count);
}
var reservationWire=new AuthenticatedNativePartyAuthority.PartyWireReservation {NativeV1=true,
 RequestId="00000009-1111-4111-8111-111111111111",Generation=1,PartyRevision=12,TravelId=travel,
 TravelRevision=3,MatchId=42,AuthorityEpoch=ulong.MaxValue.ToString(),ExpiresAt=DateTimeOffset.UtcNow.AddSeconds(30),Status="reserved",RequestedCount=1};
HunterLicenseClient.SetFixtureLease(first);
HunterLicenseClient.Reply=body=>{
 var request=(AuthenticatedNativePartyAuthority.PartyWireRequest)body;
 Check(request.Action=="reservation_snapshot"&&request.ExpectedSelfPrimeId==self&&request.ExpectedRevision==7
  &&request.PartyId==party&&request.TravelId==travel&&request.TravelRevision==3&&request.MatchId==expectedQueryMatch
  &&request.AuthorityEpoch==ulong.MaxValue.ToString(),"actual reservation discovery emits requesting account/travel/currentmatch witness");
 return new AuthenticatedNativePartyAuthority.PartyWireEnvelope{Ok=true,Reservation=reservationWire};
};
var reservation=await authority.ReadReservationAsync(result.JoinTarget!,42,default);
Check(reservation is {PartyRevision:12,MatchId:42,AuthorityEpoch:ulong.MaxValue}&&result.JoinTarget!.PartyRevision==7,
 "actual adapter keeps frozen leader revision distinct from requesting member revision");
expectedQueryMatch=43;
Check(await Reject(async()=>{await authority.ReadReservationAsync(result.JoinTarget!,43,default);}),"actual adapter rejects mismatched returned reservation match");
foreach(var bad in new Action[]{()=>reservationWire.NativeV1=false,()=>reservationWire.Generation=2,
 ()=>reservationWire.RequestId="bad",()=>reservationWire.PartyRevision=-1,()=>reservationWire.TravelRevision=0,
 ()=>reservationWire.MatchId=0,()=>reservationWire.AuthorityEpoch="18446744073709551616",
 ()=>reservationWire.RequestedCount=8,()=>reservationWire.Status="joined",()=>reservationWire.ExpiresAt=DateTimeOffset.UtcNow.AddMinutes(4)})
{
 reservationWire.NativeV1=true;reservationWire.Generation=1;reservationWire.RequestId="00000009-1111-4111-8111-111111111111";
 reservationWire.PartyRevision=12;reservationWire.TravelRevision=3;reservationWire.MatchId=42;reservationWire.AuthorityEpoch=ulong.MaxValue.ToString();
 reservationWire.RequestedCount=1;reservationWire.Status="reserved";reservationWire.ExpiresAt=DateTimeOffset.UtcNow.AddSeconds(30);
 bad();Check(await Reject(()=>Task.FromResult(AuthenticatedNativePartyAuthority.ParseReservation(reservationWire))),"malformed native reservation response rejected "+count);
}
Console.WriteLine($"PASS {count} new source-linked adapter checks plus lease adapter10; closed auth/directory facades, no HTTP/main build.");
