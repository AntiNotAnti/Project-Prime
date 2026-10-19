using System.Net;
using System.Text.Json;
using MphRead.Mods.Network;

int checks=0;
void Check(bool yes,string name){if(!yes)throw new Exception(name);checks++;Console.WriteLine($"PASS {checks}: {name}");}
var id=Guid.NewGuid();
var valid=new SocialLobbyMembershipReport(ulong.MaxValue,uint.MaxValue,"pp1."+new string('s',30),long.MaxValue,true,false);
Handler Make(string content, HttpStatusCode status=HttpStatusCode.OK)=>new(async (r,c)=>{await Task.Yield();return new HttpResponseMessage(status){Content=new StringContent(content)};});
SocialLobbyMembershipRelay Relay(Handler h,string uri="https://example.invalid/report",bool authorize=true)=>new(new HttpClient(h),new Uri(uri),_=>authorize);
var h=Make($"{{\"ok\":true,\"player_id\":\"{id}\"}}");
h.Inspect=async r=>{using var json=JsonDocument.Parse(await r.Content!.ReadAsStringAsync());var p=json.RootElement;Check(r.Method==HttpMethod.Post,"POST");Check(p.EnumerateObject().Count()==6,"exact six payload fields");Check(p.GetProperty("authority_epoch").GetString()==ulong.MaxValue.ToString(),"epoch exact string");Check(p.GetProperty("client_id").GetUInt32()==uint.MaxValue,"client exact number");Check(p.GetProperty("career_ticket").GetString()==valid.CareerTicket,"ticket exact");Check(p.GetProperty("reporter_sequence").GetString()==long.MaxValue.ToString(),"sequence exact string");Check(p.GetProperty("action").GetString()=="heartbeat"&&p.GetProperty("lobby_eligible").GetBoolean(),"heartbeat eligible");};
var receipt=await Relay(h).SendAsync(valid,default);Check(receipt.Verified&&receipt.PlayerId==id,"verified UUID receipt");
h.Inspect=async r=>{using var p=JsonDocument.Parse(await r.Content!.ReadAsStringAsync());Check(p.RootElement.GetProperty("action").GetString()=="leave"&&!p.RootElement.GetProperty("lobby_eligible").GetBoolean(),"leave always ineligible");};
await Relay(h).SendAsync(valid with{Leave=true},default);
h.Inspect=async r=>{using var p=JsonDocument.Parse(await r.Content!.ReadAsStringAsync());Check(!p.RootElement.GetProperty("lobby_eligible").GetBoolean(),"ineligible heartbeat");};
await Relay(h).SendAsync(valid with{LobbyEligible=false},default);
foreach(var body in new[]{"null","[]","{}","{", "", "{\"ok\":false,\"player_id\":\""+id+"\"}","{\"ok\":true,\"player_id\":null}","{\"ok\":true,\"player_id\":3}","{\"ok\":true,\"player_id\":\"invalid\"}","{\"ok\":true,\"player_id\":\"00000000-0000-0000-0000-000000000000\"}",new string(' ',8193)}) {var bad=Make(body);Check(!(await Relay(bad).SendAsync(valid,default)).Verified,$"refuse reply {checks}");}
var denied=Make("{}");Check(!(await Relay(denied,statusUri(),false).SendAsync(valid,default)).Verified&&denied.Calls==0,"unauthorized before send");
string statusUri()=>"https://example.invalid/report";
var insecure=Make("{}");Check(!(await Relay(insecure,"http://example.invalid").SendAsync(valid,default)).Verified&&insecure.Calls==0,"HTTP refused before send");
foreach(var report in new[]{valid with{AuthorityEpoch=0},valid with{ClientId=0},valid with{ReporterSequence=0},valid with{ReporterSequence=-1},valid with{CareerTicket=null!},valid with{CareerTicket="pp1.short"},valid with{CareerTicket="no1."+new string('x',30)},valid with{CareerTicket="pp1."+new string('x',765)},valid with{CareerTicket="pp1."+new string('x',20)+"\n"}}){var bad=Make("{}");Check(!(await Relay(bad).SendAsync(report,default)).Verified&&bad.Calls==0,$"invalid report before send {checks}");}
var err=Make("{}",HttpStatusCode.Unauthorized);Check(!(await Relay(err).SendAsync(valid,default)).Verified,"401 response refused");
using var pre=new CancellationTokenSource();pre.Cancel();var preH=Make("{}");try{await Relay(preH).SendAsync(valid,pre.Token);throw new Exception("no cancellation");}catch(OperationCanceledException){Check(preH.Calls==0,"pre cancellation before send");}
using var flight=new CancellationTokenSource();var begun=new TaskCompletionSource();var flying=new Handler(async(r,c)=>{begun.SetResult();await Task.Delay(Timeout.Infinite,c);return new();});var pending=Relay(flying).SendAsync(valid,flight.Token);await begun.Task;flight.Cancel();try{await pending;throw new Exception("no cancellation");}catch(OperationCanceledException){Check(flying.Calls==1,"in flight cancellation propagates");}
Check(!valid.ToString().Contains(valid.CareerTicket)&&!valid.ToString().Contains(new string('s',30)),"ToString hides credential");
PumpAcceptance.Run(Check);
Console.WriteLine($"COMPLETED {checks} source-only checks; stub HTTP/session facade, no backend or socket.");
sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send):HttpMessageHandler{public int Calls;public Func<HttpRequestMessage,Task>? Inspect;protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken c){Calls++;if(Inspect!=null)await Inspect(r);return await send(r,c);}}
