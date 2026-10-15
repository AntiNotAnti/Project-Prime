using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using ProjectPrime.Studio;
using ProjectPrime.Studio.IPC;
using ProjectPrime.Studio.Jobs;
using ProjectPrime.Studio.Protocol;
using MphRead.Mods.Launcher.Gui;

if (args is ["--child", var childRoot])
{
    await using var child = await StudioInstanceGuard.TryAcquireAsync(Path.Combine(childRoot,"install"),childRoot,
        new(Guid.NewGuid(),StudioOpenKind.Home), (request,_) => {
            File.AppendAllText(Path.Combine(childRoot,"received"), request.RequestId+Environment.NewLine);
            return Task.FromResult(StudioRequestResult.Success);
        });
    File.WriteAllText(Path.Combine(childRoot,"ready"), child.IsPrimary ? "primary" : child.ForwardResult.Error);
    while (!File.Exists(Path.Combine(childRoot,"stop"))) await Task.Delay(30);
    return;
}
int checks = 0;
void Check(bool valid,string name) { if(!valid)throw new Exception("FAILED: "+name);Console.WriteLine("PASS "+name);checks++; }
async Task RejectFrame(byte[] data,string name)
{
    try { await StudioIpcFraming.ReadAsync(new MemoryStream(data)); throw new Exception("Accepted invalid frame: "+name); }
    catch (StudioProtocolException) { Check(true,name); }
}
string root=Path.Combine(Path.GetTempPath(),"studio-process-check-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    // Exercise the same route seam used by the Game header and Q/E/gamepad tab
    // navigation. External launch requests must not instantiate/cache Forge.
    var routes = new PrimeRouter();
    int mapRequests = 0, embeddedActivations = 0;
    routes.RegisterRouteAction(PrimeRoute.Forge, () => mapRequests++);
    routes.Changed += route => { if(route==PrimeRoute.Forge)embeddedActivations++; };
    Check(!routes.Navigate(PrimeRoute.Forge) && mapRequests==1 && routes.Current==PrimeRoute.News
        && routes.Previous==null && embeddedActivations==0,"normal Forge entry requests Studio without creating embedded workspace or history");
    Check(routes.NavigateWorkspace(PrimeRoute.Forge) && mapRequests==1 && embeddedActivations==1,
        "explicit embedded/legacy entry retains the Forge workspace without an external launch");
    routes.Back();
    Check(!routes.Navigate(PrimeRoute.Forge) && mapRequests==2 && embeddedActivations==1 && routes.Current==PrimeRoute.News,
        "normal Forge reentry still opens Studio after an embedded workspace was previously cached");
    routes.Navigate(PrimeRoute.Theatre);routes.NextRoute();
    Check(mapRequests==3 && routes.Current==PrimeRoute.Theatre && embeddedActivations==1,
        "keyboard/gamepad next-tab Forge entry uses the same external action");
    routes.CanNavigate = route => route!=PrimeRoute.Forge;
    routes.Navigate(PrimeRoute.Forge);
    Check(mapRequests==3 && routes.Current==PrimeRoute.Theatre,"navigation guards prevent external creator launches");
    Check(!routes.NavigateWorkspace(PrimeRoute.Forge) && embeddedActivations==1,
        "explicit embedded entry still respects unsaved/overlay navigation guards");
    routes.CanNavigate = null;
    routes.NavigateWorkspace(PrimeRoute.Forge);routes.Navigate(PrimeRoute.Offline);routes.Back();
    Check(mapRequests==3 && routes.Current==PrimeRoute.Forge && embeddedActivations==3,
        "back navigation restores an explicitly opened embedded editor without launching another process");
    routes.Navigate(PrimeRoute.Settings);
    Action? continueNavigation = null;
    routes.CanNavigate = route =>
    {
        bool workspace = routes.IsWorkspaceNavigation;
        continueNavigation = () => { routes.CanNavigate=null;if(workspace)routes.NavigateWorkspace(route);else routes.Navigate(route); };
        return false;
    };
    routes.NavigateWorkspace(PrimeRoute.Forge);continueNavigation!();
    Check(mapRequests==3 && routes.Current==PrimeRoute.Forge && !routes.IsWorkspaceNavigation,
        "delayed unsaved-settings continuation retains explicit embedded intent and resets navigation scope");
    var currentRoute = new PrimeRouter();
    int currentRequests=0, currentChanges=0, guardCalls=0;
    currentRoute.RegisterRouteAction(PrimeRoute.Forge, () => currentRequests++);
    currentRoute.Changed += _ => currentChanges++;
    currentRoute.NavigateWorkspace(PrimeRoute.Forge);
    Check(!currentRoute.Navigate(PrimeRoute.Forge) && !currentRoute.Navigate(PrimeRoute.Forge)
        && currentRequests==2 && currentChanges==1 && currentRoute.Current==PrimeRoute.Forge && currentRoute.Previous==PrimeRoute.News,
        "repeated normal current-Forge requests open Studio without replacing embedded workspace or history");
    currentRoute.CanNavigate = _ => { guardCalls++;return false; };
    Check(!currentRoute.Navigate(PrimeRoute.Forge) && guardCalls==1 && currentRequests==2 && currentChanges==1,
        "current-Forge external action still obeys overlay and navigation guards");
    currentRoute.CanNavigate=null;currentRoute.Navigate(PrimeRoute.Settings);guardCalls=0;
    currentRoute.CanNavigate = _ => { guardCalls++;return false; };
    Check(currentRoute.Navigate(PrimeRoute.Settings) && guardCalls==0 && currentRequests==2 && currentChanges==2,
        "ordinary current Settings remains a no-op without prompting for unsaved edits");
    Check(StudioLaunchRequest.TryParse(["--home","--safe-mode"],out var home,out _) && home.Kind==StudioOpenKind.Home && home.SafeMode,"explicit home and safe mode");
    Check(!StudioLaunchRequest.TryParse(["--home","--map","map.json"],out _,out _),"reject competing workspace selectors");
    Check(!StudioLaunchRequest.TryParse(["--replay","wrong.json"],out _,out _),"reject document extension mismatch");
    Check(!StudioLaunchRequest.TryParse(["--map"],out _,out _),"reject missing document");
    Check(StudioLaunchRequest.TryParse(["--map","map.json","--recover"],out var map,out _) && Path.IsPathFullyQualified(map.Path!),"normalize launch path");
    byte[] header=new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header,StudioProtocol.MaximumFrameBytes+1);
    await RejectFrame(header,"bounded allocation before reading payload");
    byte[] unknown=Encoding.UTF8.GetBytes("{\"version\":1,\"type\":\"Ping\",\"requestId\":\""+Guid.NewGuid()+"\",\"unknown\":true}");
    byte[] raw=new byte[unknown.Length+4];BinaryPrimitives.WriteInt32LittleEndian(raw,unknown.Length);unknown.CopyTo(raw,4);
    await RejectFrame(raw,"reject unknown JSON members");
    string secret=StudioIpcAuthentication.NewSecret(), nonce=StudioIpcAuthentication.NewNonce();
    string proof=StudioIpcAuthentication.CreateProof(secret,nonce,"client",1);
    Check(StudioIpcAuthentication.VerifyProof(secret,nonce,"client",1,proof) && !StudioIpcAuthentication.VerifyProof(secret,nonce,"server",1,proof),"mutual proof direction separation");
    int handled=0;
    var endpoint=new StudioEndpointDescriptor(1,"ProjectPrime.Studio."+Guid.NewGuid().ToString("N")[..12],secret,Environment.ProcessId);
    await using (var server=new LocalIpcServer(endpoint,(request,_) => {
        Interlocked.Increment(ref handled);
        return Task.FromResult(new StudioIpcEnvelope(1,"Result",request.RequestId,Result:StudioRequestResult.Success));
    }))
    {
        server.Start();
        var open=new StudioOpenRequest(Guid.NewGuid(),StudioOpenKind.Home);
        var request=new StudioIpcEnvelope(1,"Open",open.RequestId,OpenRequest:open);
        var first=await LocalIpcClient.RequestAsync(endpoint,request);
        var again=await LocalIpcClient.RequestAsync(endpoint,request);
        Check(first.Result?.Accepted==true && again.Result?.Accepted==true && handled==1,"duplicate request executes once");
        var conflict=await LocalIpcClient.RequestAsync(endpoint,request with {OpenRequest=open with {SafeMode=true}});
        Check(conflict.Type=="Error" && handled==1,"duplicate ID with changed payload rejected");
        var stale=await LocalIpcClient.RequestAsync(endpoint,request with {RequestId=Guid.NewGuid(),OpenRequest=new(Guid.NewGuid(),StudioOpenKind.Home),CreatedUtc=1});
        Check(stale.Type=="Error","mismatched request IDs rejected");
        var staleOpen=new StudioOpenRequest(Guid.NewGuid(),StudioOpenKind.Home);
        stale=await LocalIpcClient.RequestAsync(endpoint,new(1,"Open",staleOpen.RequestId,OpenRequest:staleOpen){CreatedUtc=1});
        Check(stale.Type=="Error" && handled==1,"stale capability request rejected");
        var unauth=await LocalIpcClient.RequestAsync(endpoint with {Secret=StudioIpcAuthentication.NewSecret()},request);
        Check(unauth.Type=="Error" && handled==1,"wrong local capability cannot open document");
        var mixed=await LocalIpcClient.RequestAsync(endpoint,request with {GameRequest=new(Guid.NewGuid(),StudioGameCommand.Diagnostics)});
        Check(mixed.Type=="Error","mixed envelope rejected");
    }
    LocalIpcEndpointStore.EnsurePrivateDirectory(root);
    LocalIpcEndpointStore.Write(root,endpoint);
    string descriptor=Path.Combine(root,"endpoint.json");
    Check(LocalIpcEndpointStore.ReadPath(descriptor)==endpoint,"private descriptor roundtrip");
    if(!OperatingSystem.IsWindows())
    {
        File.SetUnixFileMode(descriptor,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.OtherRead);
        try { LocalIpcEndpointStore.ReadPath(descriptor);throw new Exception("Public descriptor accepted"); }
        catch(UnauthorizedAccessException){ Check(true,"reject public descriptor permissions"); }
    }
    Directory.CreateDirectory(Path.Combine(root,"install"));
    string host=Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? Path.Combine(Environment.GetEnvironmentVariable("DOTNET_ROOT")!,OperatingSystem.IsWindows()?"dotnet.exe":"dotnet");
    Process StartChild()
    {
        var start=new ProcessStartInfo(host){UseShellExecute=false};start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);start.ArgumentList.Add("--child");start.ArgumentList.Add(root);
        return Process.Start(start)!;
    }
    async Task WaitFor(string path)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while(!File.Exists(path))await Task.Delay(30,timeout.Token);
    }
    using (var child=StartChild())
    {
        await WaitFor(Path.Combine(root,"ready"));
        Check(File.ReadAllText(Path.Combine(root,"ready"))=="primary","standalone primary owns process lock");
        string ipc=LocalIpcEndpointStore.GetDirectory(Path.Combine(root,"install"),root);
        using var gameLease=LocalIpcEndpointStore.OpenLock(ipc,StudioEndpointRole.Game);
        Check(true,"Game and Studio process locks coexist");
        var forwarded=new StudioOpenRequest(Guid.NewGuid(),StudioOpenKind.Home);
        await using(var second=await StudioInstanceGuard.TryAcquireAsync(Path.Combine(root,"install"),root,forwarded,(_,_)=>throw new Exception("Secondary became owner")))
            Check(!second.IsPrimary && second.ForwardResult.Accepted,"second real process forwards to primary");
        Check(File.ReadAllLines(Path.Combine(root,"received")).Contains(forwarded.RequestId.ToString()),"primary receives forwarded document");
        child.Kill(true);await child.WaitForExitAsync();
    }
    await using(var replacement=await StudioInstanceGuard.TryAcquireAsync(Path.Combine(root,"install"),root,new(Guid.NewGuid(),StudioOpenKind.Home),(_,_)=>Task.FromResult(StudioRequestResult.Success)))
        Check(replacement.IsPrimary,"crashed process lock and stale descriptor recovered");
    await using(var jobs=new StudioJobManager())
    {
        using var cancel=new CancellationTokenSource();
        Task<bool> pending=jobs.RunAsync("cancel probe",async(progress,token)=>{progress.Report(new(2));await Task.Delay(Timeout.Infinite,token);return true;},cancel.Token);
        cancel.Cancel();try{await pending;}catch(OperationCanceledException){}
        Check(jobs.Jobs.Single().State==StudioJobState.Cancelled,"central job cancellation observable");
    }
    Console.WriteLine($"{checks} Studio process/protocol checks passed.");
}
finally { try {Directory.Delete(root,true);}catch(IOException){} }
