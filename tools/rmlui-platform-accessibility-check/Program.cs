using System.Runtime.InteropServices;
using MphRead.Mods.Launcher.RmlUi.Components;
using MphRead.Mods.Launcher.RmlUi.Host;

if (args.Length < 2 || args[0] != "--native") throw new ArgumentException("Usage: --native bridge [--windows]");
var module = NativeLibrary.Load(Path.GetFullPath(args[1]));
NativeLibrary.SetDllImportResolver(typeof(RmlUiHost).Assembly, (name, _, _) => {
    if(name=="ProjectPrime.RmlUi.Native")return module;
    if(OperatingSystem.IsMacOS() && name is "libglib-2.0.so.0" or "libgio-2.0.so.0" or "libgobject-2.0.so.0") {
        string path="/opt/homebrew/lib/"+name.Replace(".so.0",".dylib");if(File.Exists(path))return NativeLibrary.Load(path);
    }
    return 0;
});
string root = Path.Combine(Path.GetTempPath(), "prime-uia-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
foreach (string file in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "rmlui"), "*", SearchOption.AllDirectories)) {
    string target = Path.Combine(root, Path.GetRelativePath(Path.Combine(AppContext.BaseDirectory, "rmlui"), file));
    Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
}
File.WriteAllText(Path.Combine(root, "provider.rml"), """
<rml><head><style>body{font-family:Rajdhani;font-size:20px}button,input{display:block;width:240px;height:40px;tab-index:auto}</style></head>
<body><h1 id="heading">ACCOUNT</h1><label for="email">Email address</label><input id="email" value="PRIVATE_VALUE"/>
<label for="password">Password</label><input id="password" type="password" value="SECRET_VALUE"/>
<input id="readonly" readonly="readonly" value="PRIVATE_READONLY"/>
<button id="save" data-action="route:settings">SAVE SETTINGS</button><button id="disabled" disabled="disabled" data-action="quit">UNAVAILABLE</button>
</body></rml>
""");
int checks = 0;
void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); ++checks; }
try {
    using var host = new RmlUiHost(); Check(host.Initialize(1280,720,1,root,RmlUiRenderBackend.DrawList),"native host");
    var doc = host.OpenDocument("provider.rml", RmlUiDocumentLayer.Page);host.Update();
    var service = new RmlUiAccessibilityService();service.Capture(host);
    var api = new FakeWindow(); using var provider = new RmlUiWindowsAccessibility(1, service, api);
    provider.Publish(service.Snapshot);
    var rootProvider = (IRmlUiUiaFragment)provider.Provider;
    IRmlUiUiaSimple Find(string id) {
        var next = rootProvider.Navigate(3);
        while(next != null) { if ((string?)((IRmlUiUiaSimple)next).GetPropertyValue(30011) == id) return (IRmlUiUiaSimple)next; next = next.Navigate(1); }
        throw new InvalidOperationException("missing node "+id);
    }
    var email = Find("email");var password = Find("password");var save = Find("save");
    Check((provider.Provider.ProviderOptions&16)==0,"UIA owns native HWND focus before fragment focus");
    Check((email.ProviderOptions&16)==0,"child fragment does not claim HWND focus ownership");
    Check(provider.ElementCount == service.Snapshot.Nodes.Count,"real immutable native controls exposed");
    Check((string?)email.GetPropertyValue(30005)=="Email address","real field label");
    Check((int?)email.GetPropertyValue(30003)==50004,"UIA Edit control mapping");
    Check((bool?)password.GetPropertyValue(30019)==true,"protected UIA flag");
    Check(email.GetPropertyValue(30045)==null && password.GetPropertyValue(30045)==null,"no value metadata");
    foreach (var field in new[]{email,password}) {
        try { _=((IRmlUiUiaValue)field.GetPatternProvider(10002)!).Value;throw new InvalidOperationException("private value exposed"); }
        catch(COMException error) { Check(error.HResult==unchecked((int)0x80070005),"editable Value access denied"); }
    }
    Check(((IRmlUiUiaValue)Find("readonly").GetPatternProvider(10002)!).IsReadOnly,"readonly accurate");
    Check(Find("disabled").GetPatternProvider(10000)==null,"disabled no Invoke pattern");
    var emailBounds=((IRmlUiUiaFragment)email).BoundingRectangle;
    var node=service.Snapshot.Nodes.Single(n=>n.Id=="email");
    Check(emailBounds.Left==api.Screen.Left+node.X*.5 && emailBounds.Top==api.Screen.Top+node.Y*.5,"framebuffer->screen coordinates");
    Check(ReferenceEquals(((IRmlUiUiaRoot)provider.Provider).ElementProviderFromPoint(emailBounds.Left+1,emailBounds.Top+1),email),"screen hit testing");
    var runtime=((IRmlUiUiaFragment)email).GetRuntimeId()!;
    Check(runtime[0]==3 && runtime.Length==8,"runtime IDs contain document generation and revision");
    Task.Run(()=>((IRmlUiUiaValue)email.GetPatternProvider(10002)!).SetValue("日本語 😀")).GetAwaiter().GetResult();
    Check(host.ReadField(doc,"email")=="PRIVATE_VALUE","COM worker does not enter host");
    Check(service.Drain(host)==1 && host.ReadField(doc,"email")=="日本語 😀","owner applies Unicode SetValue");
    ((IRmlUiUiaFragment)email).SetFocus(); Check(service.Drain(host)==1,"focus owner queued");
    host.Update();provider.Publish(service.Capture(host));
    Check((bool?)Find("email").GetPropertyValue(30008)==true,"actual keyboard focus projected");
    try { ((IRmlUiUiaInvoke)save.GetPatternProvider(10000)!).Invoke();throw new InvalidOperationException("stale object action accepted"); }
    catch(COMException error) { Check(error.HResult==unchecked((int)0x80040201),"old revision COM object becomes unavailable"); }
    ((IRmlUiUiaInvoke)Find("save").GetPatternProvider(10000)!).Invoke();Check(service.Drain(host)==1,"real invoke queued");
    host.Update();Check(host.TryTakeIntent(out var intent)&&intent.Kind==RmlUiIntentKind.Navigate,"Invoke existing typed business action");
    var queued=Find("save");((IRmlUiUiaInvoke)queued.GetPatternProvider(10000)!).Invoke();
    var modal=host.OpenDocument("provider.rml",RmlUiDocumentLayer.Modal);host.Update();
    Check(service.Drain(host)==0,"queued background action rejected after modal");provider.Publish(service.Capture(host));
    var old=Find("email");host.CloseDocument(modal);host.Update();provider.Publish(service.Capture(host));
    try { ((IRmlUiUiaFragment)old).SetFocus();throw new InvalidOperationException("retired modal accepted"); }
    catch(COMException) { ++checks; }
    for(int cycle=0;cycle<100;cycle++) {
        var fake=new FakeWindow();using var instance=new RmlUiWindowsAccessibility(2,service,fake);
        instance.Publish(service.Snapshot); instance.Dispose(); Check(fake.Attaches==1&&fake.Detaches==1,"100 HWND lifetime cycles");
    }
    var failedApi=new FakeWindow { FailDetach=true };var failed=new RmlUiWindowsAccessibility(3,service,failedApi);failed.Dispose();
    failedApi.Callback!(3,0x003d,42,-25,0,0);Check(failedApi.Returns==0,"disposed callback forwards without stale provider");
    failedApi.Callback!(3,0x0082,0,0,0,0);Check(!failed.Attached,"failed uninstall releases on HWND destroy");
    api.Callback!(1,0x003d,123,-25,0,0);Check(api.LastWParam==123&&api.LastLParam==-25,"WM_GETOBJECT parameters preserved");
    api.Callback!(1,0x0002,0,0,0,0);Check(api.DestroyNotification,"UIA window destroy map retired");
    bool rejected=Task.Run(()=>{try{provider.Publish(service.Snapshot);return false;}catch(InvalidOperationException){return true;}}).Result;
    Check(rejected,"HWND publish requires window thread");
    service.Retire();provider.Publish(service.Snapshot);Check(rootProvider.Navigate(3)==null,"retirement empties OS tree");
    foreach(var current in service.Capture(host).Nodes) {
        Check(!RmlUiLinuxAccessibility.Interfaces(current).Contains("org.a11y.atspi.Text"),"AT-SPI never fabricates private text interface");
        Check(RmlUiLinuxAccessibility.Interfaces(current).Contains("org.a11y.atspi.EditableText") == ((current.Actions&RmlUiAccessibilityActions.SetText)!=0),"AT-SPI exact native editable capabilities");
    }
    Check(RmlUiLinuxAccessibility.Role(service.Snapshot.Nodes.Single(n=>n.Id=="password"))==40,"AT-SPI PasswordText role");
    var tree=new RmlUiLinuxAccessibility.Tree(service.Snapshot,new(200,100,640,360),true);
    for(int i=0;i<tree.Snapshot.Nodes.Count;i++)Check(tree.Index(tree.Path(i).Split('/')[^1])==i,"AT-SPI real node path roundtrip");
    Check(tree.Index("n_0_0_0_0")==-2,"AT-SPI retired generation/revision path rejected");
    Check((tree with { ScreenCoordinates=false }).Rectangle(0,0).Width==0,"Wayland unavailable global position is explicit");
    Check((tree with { ScreenCoordinates=false }).Rectangle(0,1).Width>0,"Wayland window coordinates remain real");
    Console.WriteLine($"PASS {checks} real-native OS provider contract checks");
    LinuxWireCheck.Run(service);
    if(args.Contains("--atspi")) LinuxFixture.Run(host,service,doc);
    if(args.Contains("--windows")) {
        // The preceding provider contract deliberately exercises stale COM
        // elements, modal retirement and a forced failed HWND detach. Its
        // document has been through multiple revisions and service.Retire().
        // Actual OS acceptance must start from a fresh, unretired native UI
        // lifetime while still demanding an external Invoke acknowledgment.
        host.Dispose();
        using var osHost = new RmlUiHost();
        Check(osHost.Initialize(1280,720,1,root,RmlUiRenderBackend.DrawList),
            "fresh Windows UIA owner host");
        var osDocument = osHost.OpenDocument("provider.rml", RmlUiDocumentLayer.Page);
        osHost.Update();
        var osService = new RmlUiAccessibilityService();
        Check(osService.Capture(osHost).Nodes.Any(n => n.Id == "save"),
            "fresh Windows UIA owner exposes a real Save element");
        WindowsFixture.Run(osHost,osService,root,osDocument);
    }
} finally { Directory.Delete(root,true);NativeLibrary.Free(module); }

sealed class FakeWindow : IRmlUiUiaWindowApi {
    public RmlUiWindowSubclass? Callback;public int Attaches,Detaches,Returns;public bool FailDetach,DestroyNotification;
    public nuint LastWParam;public nint LastLParam;
    public RmlUiUiaRect Screen=new(200,100,640,360);
    public bool Attach(nint w,RmlUiWindowSubclass c,nuint id){Callback=c;++Attaches;return true;}
    public bool Detach(nint w,RmlUiWindowSubclass c,nuint id){++Detaches;return !FailDetach;}
    public nint Forward(nint w,uint m,nuint p,nint l)=>0;
    public nint Return(nint w,nuint p,nint l,IRmlUiUiaSimple? provider){++Returns;LastWParam=p;LastLParam=l;DestroyNotification=provider==null;return 123;}
    public IRmlUiUiaSimple? Host(nint w)=>null;
    public RmlUiUiaRect Bounds(nint w)=>Screen;
    public void Changed(IRmlUiUiaSimple provider){}
    public void Focused(IRmlUiUiaSimple provider){}
    public void Disconnect(IRmlUiUiaSimple provider){}
}
