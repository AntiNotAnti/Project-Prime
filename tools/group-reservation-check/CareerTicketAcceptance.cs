using MphRead.Mods.Launcher;
internal static class CareerTicketAcceptance
{
 internal static async Task<int> Run()
 {
  int checks=0;void Check(bool value,string name){if(!value)throw new Exception("ticket owner: "+name);++checks;}
  var accountA=new object();var accountB=new object();HunterLicenseClient.TestLease=accountA;
  int a=HunterLicenseClient.TestGeneration;var held=new TaskCompletionSource();
  var completion=Task.Run(async()=>{await held.Task;return HunterLicenseClient.PublishCareerTicket(1234,"ticket-A",DateTimeOffset.UtcNow.AddHours(1),accountA,a);});
  HunterLicenseClient.TestLease=accountB;HunterLicenseClient.TestInvalidate();
  int b=HunterLicenseClient.TestGeneration;
  Check(HunterLicenseClient.PublishCareerTicket(1234,"ticket-B",DateTimeOffset.UtcNow.AddHours(1),accountB,b),"current B publishes");
  held.SetResult();Check(!await completion&&HunterLicenseClient.TestCached=="ticket-B","held A completion cannot overwrite B cache same ClientId");
  int reads=HunterLicenseClient.TestReads;
  Check(!HunterLicenseClient.TryGetNativeCareerTicket(1234,accountA,out string wrong)&&wrong==""&&reads==HunterLicenseClient.TestReads,"wrong expected account refuses before lookup");
  Check(HunterLicenseClient.TryGetNativeCareerTicket(1234,accountB,out string valid)&&valid=="ticket-B","exact B target receives only B ticket");
  HunterLicenseClient.TestInvalidate();Check(!HunterLicenseClient.PublishCareerTicket(1234,"late-B",DateTimeOffset.UtcNow.AddHours(1),accountB,b),"invalidation fences in-flight even same session ref");
  Check(HunterLicenseClient.TestCached=="","invalidation remains cleared");
  var refreshedSameAccount=new object();HunterLicenseClient.TestLease=refreshedSameAccount;
  Check(!HunterLicenseClient.PublishCareerTicket(1234,"old-reference",DateTimeOffset.UtcNow.AddHours(1),accountB,HunterLicenseClient.TestGeneration),"same-account refresh reference cannot publish old lease");
  return checks;
 }
}
