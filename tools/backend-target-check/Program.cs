using MphRead.Mods;
using System.Net.Http;
using System.Text;
int checks=0;
void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
BackendServiceConfiguration Capture(params (string,string)[] values){var map=values.ToDictionary(v=>v.Item1,v=>v.Item2);return BackendServiceConfiguration.Capture(name=>map.GetValueOrDefault(name));}
void Refuse(Action action,string message){bool rejected=false;try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException){rejected=true;}Check(rejected,message);}
const string U="PROJECT_PRIME_SUPABASE_URL",K="PROJECT_PRIME_SUPABASE_KEY",R="PROJECT_PRIME_CAREER_REPORT_URL";
var original=Capture();Check(original.BaseUri==new Uri(BackendServiceConfiguration.DefaultUrl),"unset configuration preserves existing production origin");
Check(original.CareerReportUri==original.Function("career-report"),"default career and client functions share one origin");
var stage=Capture((U,"https://staging.example.test:8443"),(K,"sb_publishable_fixture"));
foreach(string function in new[]{"social","party","native-presence","social-lobby-membership","social-party-reservation-server","community-map-ticket","career-ticket"})
{
 var endpoint=stage.Function(function);using var request=new HttpRequestMessage(HttpMethod.Post,endpoint);stage.AddApiKey(request);
 Check(endpoint==new Uri("https://staging.example.test:8443/functions/v1/"+function)&&request.Headers.GetValues("apikey").Single()=="sb_publishable_fixture","client/server route and public key stay paired: "+function);
}
Check(stage.CareerReportUri==new Uri("https://staging.example.test:8443/functions/v1/career-report"),"reporter derives configured backend");
foreach(string url in new[]{"http://staging.example.test","https://user:password@staging.example.test","https://staging.example.test/path","https://staging.example.test?token=private","https://staging.example.test/#fragment"," https://staging.example.test","invalid",""})
 Refuse(()=>Capture((U,url),(K,"sb_publishable_fixture")),"invalid backend origin refuses before requests");
Refuse(()=>Capture((U,"https://staging.example.test")),"custom origin cannot accidentally reuse production public key");
foreach(string key in new[]{"","sb_secret_private","sb_publishable_","sb_publishable_foo\nbar","private opaque token",new string('x',4097)})
 Refuse(()=>Capture((K,key)),"invalid or privileged API key refuses without logging its value");
string Jwt(string role)=>"eyJhbGciOiJIUzI1NiJ9."+Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"role\":\""+role+"\"}")).TrimEnd('=').Replace('+','-').Replace('/','_')+".fixture";
Check(Capture((K,Jwt("anon"))).BaseUri==original.BaseUri,"legacy anon key remains supported as public metadata");
string RefJwt(string project)=>"eyJhbGciOiJIUzI1NiJ9."+Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"role\":\"anon\",\"ref\":\""+project+"\"}")).TrimEnd('=').Replace('+','-').Replace('/','_')+".fixture";
Check(Capture((U,"https://staging-ref.supabase.co"),(K,RefJwt("staging-ref"))).BaseUri.Host=="staging-ref.supabase.co","legacy anon project metadata matches selected project");
Refuse(()=>Capture((U,"https://staging-ref.supabase.co"),(K,RefJwt("other-ref"))),"legacy anon metadata mismatch fails before key or reporter attachment");
Refuse(()=>Capture((K,Jwt("service_role"))),"legacy service-role key cannot enter public owner");
Refuse(()=>Capture((R,"https://other-project.supabase.co/functions/v1/career-report")),"explicit reporter URL cannot accidentally route a default-project key to another Supabase project");
var proxy=Capture((U,"https://staging.example.test"),(K,"sb_publishable_fixture"),(R,"https://reporting.example.test/custom/report"));
using(var reportRequest=new HttpRequestMessage(HttpMethod.Post,proxy.CareerReportUri))
{proxy.AddReportApiKey(reportRequest);Check(reportRequest.RequestUri==new Uri("https://reporting.example.test/custom/report")&&reportRequest.Headers.GetValues("apikey").Single()=="sb_publishable_fixture","explicit custom HTTPS career proxy preserves existing separate reporter destination");Refuse(()=>proxy.AddApiKey(reportRequest),"explicit reporter proxy does not become client or Social authority");}
using(var wrong=new HttpRequestMessage(HttpMethod.Post,"https://reporting.example.test/unreviewed"))Refuse(()=>proxy.AddReportApiKey(wrong),"exact custom reporter destination cannot authorize other proxy paths");
foreach(string report in new[]{"http://reporting.example.test/report","https://user:pass@reporting.example.test/report","https://reporting.example.test/report?token=private"})Refuse(()=>Capture((R,report)),"custom reporter override requires HTTPS and excludes embedded credentials");
Check(Capture((U,"https://staging.example.test"),(K,"sb_publishable_fixture"),(R,"https://staging.example.test/reporter")).CareerReportUri.AbsolutePath=="/reporter","same-origin explicit career path remains supported");
using(var foreign=new HttpRequestMessage(HttpMethod.Post,"https://other.example.test/functions/v1/party"))
{Refuse(()=>stage.AddApiKey(foreign),"foreign endpoint cannot receive captured public key");Check(!foreign.Headers.Contains("apikey"),"refused request has no attached key");}
Check(!stage.Owns(new Uri("https://staging.example.test/functions/v1/party")),"port is part of configured authority");
foreach(string name in new[]{"../social","social?x=1","/social","",new string('a',65)})Refuse(()=>stage.Function(name),"unbounded or path-bearing function name refuses");
int reads=0;var snapshot=BackendServiceConfiguration.Capture(name=>{reads++;return name==U?"https://snapshot.example.test":name==K?"sb_publishable_fixture":null;});
Check(reads==3,"public configuration captured once without credential/session environment access");Check(snapshot.ToString()=="Project Prime backend https://snapshot.example.test","diagnostic string contains origin only");
Console.WriteLine("BACKEND TARGET PASS "+checks+" checks; source-linked pure configuration, no HTTP/auth/files.");
