using System.Runtime.InteropServices;
using System.Text;
using MphRead.Mods.Launcher.RmlUi.Components;
using MphRead.Mods.Launcher.RmlUi.Host;

if (args.Length is < 1 or > 2) throw new ArgumentException("Usage: rmlui-accessibility-check <native-bridge> [asset-root]");
string source = args.Length == 2 ? Path.GetFullPath(args[1]) : Path.Combine(AppContext.BaseDirectory,"rmlui");
string root = Path.Combine(Path.GetTempPath(), "prime-rmlui-accessibility-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
foreach (string file in Directory.EnumerateFiles(source,"*",SearchOption.AllDirectories)) {
    string target=Path.Combine(root,Path.GetRelativePath(source,file)); Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(file,target);
}
File.WriteAllText(Path.Combine(root,"semantics.rml"), """
<rml><head><link type="text/rcss" href="themes/prime.rcss"/><link type="text/rcss" href="components/controls.rcss"/><style>
body { font-family:Rajdhani; font-size:16px; overflow-y:auto; } button,input,textarea { display:block; width:240px; height:40px; tab-index:auto; }
#scroll { width:400px; height:100px; overflow-y:auto; margin-top:15px; } #spacer { height:400px; }
</style></head><body><h1 id="heading">ACCOUNT SETTINGS</h1><label for="email">Email address</label>
<input id="email" type="text" maxlength="100" value="PRIVATE_EMAIL_VALUE"/>
<label for="password">Password</label><input id="password" type="password" maxlength="20" value="SECRET_PASSWORD_VALUE"/>
<label for="readonly">Read-only name</label><input id="readonly" readonly="readonly" value="PRIVATE_READONLY_VALUE"/>
<button id="submit" data-action="route:settings">SAVE SETTINGS</button>
<button id="disabled" disabled="disabled" data-action="quit">UNAVAILABLE</button>
<div id="hidden" aria-hidden="true"><button id="hidden_button" data-action="quit">HIDDEN SECRET</button></div>
<div id="scroll" role="region" aria-label="Scrollable options"><div id="spacer"/><button id="last" data-action="route:news">LAST OPTION</button></div>
</body></rml>
""");
File.WriteAllText(Path.Combine(root,"modal.rml"),"<rml><head><style>body{font-family:Rajdhani;}button{width:200px;height:40px;tab-index:auto;}</style></head><body><button id='confirm' data-action='quit'>CONFIRM</button></body></rml>");
int checks=0;
void Check(bool value,string name) { if(!value)throw new InvalidOperationException(name); ++checks; Console.WriteLine("PASS "+name); }
nint module=NativeLibrary.Load(Path.GetFullPath(args[0]));
NativeLibrary.SetDllImportResolver(typeof(RmlUiHost).Assembly,(name,_,_)=>name=="ProjectPrime.RmlUi.Native"?module:0);
try {
    foreach(var viewport in new[]{(1280,720,1f),(1280,720,2f),(640,320,1f)}) {
        using var host=new RmlUiHost(); Check(host.Initialize(viewport.Item1,viewport.Item2,viewport.Item3,root,RmlUiRenderBackend.DrawList),"real native initialization");
        var document=host.OpenDocument("semantics.rml",RmlUiDocumentLayer.Page);
        host.ShowDocument(host.HomeDocument,false);host.Update();host.Render(viewport.Item1,viewport.Item2);
        var service=new RmlUiAccessibilityService(); var snapshot=service.Capture(host);
        RmlUiAccessibilityNode Node(string id)=>service.Snapshot.Nodes.Single(n=>n.Id==id);
        Check(service.Available&&snapshot.Document==document&&snapshot.Revision>0,"native semantic snapshot has live generation/document/revision");
        Check(Node("heading").Role=="heading"&&Node("heading").Label=="ACCOUNT SETTINGS","authored heading role/name");
        Check(Node("email").Label=="Email address"&&Node("email").Role=="textbox","associated input label projected");
        Check(Node("password").Protected&&Node("password").Label=="Password","password semantic flag/name");
        Check(!string.Join(" ",snapshot.Nodes.Select(n=>n.Label)).Contains("PRIVATE_")&&!string.Join(" ",snapshot.Nodes.Select(n=>n.Label)).Contains("SECRET_PASSWORD"),"editable values excluded from snapshot");
        Check(!snapshot.Nodes.Any(n=>n.Id=="hidden_button"),"aria-hidden descendants excluded");
        Check(!Node("disabled").Enabled&&Node("disabled").Actions==0,"disabled control advertises no actions");
        Check((Node("readonly").Actions&RmlUiAccessibilityActions.SetText)==0,"readonly control omits SetText action");
        Check(Node("last").Offscreen,"nested scroll clipping marks below-content control offscreen");
        Check(service.Capture(host).Revision==snapshot.Revision,"unchanged DOM has stable semantic revision");
        Check(ReferenceEquals(service.Capture(host),snapshot),"unchanged capture reuses immutable managed snapshot");
        for(int warm=0;warm<16;warm++)service.Capture(host);
        var captureBefore = service.CaptureMetrics;
        long allocated=GC.GetAllocatedBytesForCurrentThread();
        double[] timings=new double[200];
        for(int iteration=0;iteration<timings.Length;iteration++) {
            long started=System.Diagnostics.Stopwatch.GetTimestamp();service.Capture(host);
            timings[iteration]=System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }
        long bytes=GC.GetAllocatedBytesForCurrentThread()-allocated;
        Array.Sort(timings);Console.WriteLine($"MEASURE stable semantics p95Ms={timings[189]:F4} meanMs={timings.Average():F4} managedBytesPerCapture={(bytes-24-timings.Length*8)/(double)timings.Length:F2}");
        Check(bytes<timings.Length*128+timings.Length*8+24,"stable capture avoids per-frame JSON/node/buffer allocation");
        var captureAfter = service.CaptureMetrics;
        Check(captureAfter.NativeReads-captureBefore.NativeReads
                +captureAfter.IdleSkips-captureBefore.IdleSkips==timings.Length
            &&captureAfter.DecodedSnapshots==captureBefore.DecodedSnapshots
            &&captureAfter.ReusedSnapshots-captureBefore.ReusedSnapshots==timings.Length,
            "capture metrics prove native reads or native-confirmed idle reuse with zero unchanged-tree JSON decodes");
        Console.WriteLine($"MEASURE semantic capture nativeReads={captureAfter.NativeReads-captureBefore.NativeReads} idleSkips={captureAfter.IdleSkips-captureBefore.IdleSkips}");
        bool scheduled = host.TryGetUpdateState(out var idleState);
        if(scheduled)Console.WriteLine($"MEASURE native update state dirty={idleState.Dirty} visualRevision={idleState.VisualRevision} nextDelay={idleState.NextUpdateDelaySeconds}");
        if(scheduled) {
            Check(!idleState.Dirty&&idleState.NextUpdateDelaySeconds>0,"actual native state confirms settled document idle");
            Check(captureAfter.IdleSkips-captureBefore.IdleSkips==timings.Length
                &&captureAfter.NativeReads==captureBefore.NativeReads,"actual native-confirmed idle skips semantic traversal and serialization");
            host.SetText(document,"heading","UPDATED ACCOUNT SETTINGS");
            Check(host.TryGetUpdateState(out var dirtyState)&&dirtyState.Dirty,"model mutation marks native state dirty");
            long beforeRead=service.CaptureMetrics.NativeReads;
            service.Capture(host);
            Check(service.CaptureMetrics.NativeReads>beforeRead&&Node("heading").Label=="UPDATED ACCOUNT SETTINGS","dirty heading is captured without managed update or stale graph reuse");
            host.Resize(viewport.Item1+37,viewport.Item2+19,viewport.Item3);
            service.Capture(host);
            Check(service.Snapshot.FramebufferWidth==viewport.Item1+37&&service.Snapshot.FramebufferHeight==viewport.Item2+19,"resize invalidates semantic framebuffer stamp");
            host.Resize(viewport.Item1,viewport.Item2,viewport.Item3);host.Update();service.Capture(host);
            beforeRead=service.CaptureMetrics.NativeReads;
            host.Input.PointerMoved(15,15);service.Capture(host);
            Check(service.CaptureMetrics.NativeReads>beforeRead,"direct native pointer input invalidates idle semantic reuse");
        }
        RmlUiAccessibilityCommand Command(string id,RmlUiAccessibilityAction action,string text="")=>new(service.Snapshot.Document,service.Snapshot.Revision,Node(id).Key,action,text);
        Check(service.Enqueue(Command("email",RmlUiAccessibilityAction.Focus)),"provider queues focus");Check(service.Drain(host)==1,"engine accepts native focus");
        host.Update();service.Capture(host);Check(Node("email").Focused&&host.FocusedElement()=="email","actual Rml input focused");
        if(scheduled) {
            Check(host.TryGetUpdateState(out var caretState)&&double.IsFinite(caretState.NextUpdateDelaySeconds)
                &&caretState.NextUpdateDelaySeconds<=1,"focused input exposes bounded actual caret update deadline");
            Thread.Sleep(Math.Max(1,(int)Math.Ceiling(caretState.NextUpdateDelaySeconds*1000)+20));
            Check(host.TryGetUpdateState(out var dueState)&&dueState.NextUpdateDelaySeconds==0,"expired native caret deadline requires fresh update");
            long beforeRead=service.CaptureMetrics.NativeReads;
            service.Capture(host);
            Check(service.CaptureMetrics.NativeReads>beforeRead,"caret deadline prevents idle semantic reuse");
        }
        Check(service.Enqueue(Command("email",RmlUiAccessibilityAction.SetText,"日本語 café Привет")),"Unicode SetText queued");Check(service.Drain(host)==1,"native text value applied");
        Check(host.ReadField(document,"email")=="日本語 café Привет","actual field retains Unicode without snapshot exposure");
        service.Capture(host);Check(!service.Snapshot.Nodes.Any(n=>n.Label.Contains("日本語 café")),"updated editable value absent from semantic metadata");
        Check(!service.Enqueue(Command("readonly",RmlUiAccessibilityAction.SetText,"change")),"provider rejects readonly writes");
        Check(!service.Enqueue(Command("disabled",RmlUiAccessibilityAction.Press)),"provider rejects disabled press");
        Check(service.Enqueue(Command("submit",RmlUiAccessibilityAction.Press)),"provider queues press");Check(service.Drain(host)==1,"native dispatches actual DOM click");host.Update();
        Check(host.TryTakeIntent(out var intent)&&intent.Kind==RmlUiIntentKind.Navigate&&RmlUiIntentRegistry.ToLegacy(intent)=="route:settings","semantic press uses existing typed business intent");
        service.Capture(host);var stale=Command("submit",RmlUiAccessibilityAction.Press);
        Check(service.Enqueue(stale),"old row action queued before rebinding");host.SetText(document,"action:submit","route:news");
        Check(service.Drain(host)==0&&!host.TryTakeIntent(out _),"private action rebinding rejects stale semantic revision");
        service.Capture(host);Check(service.Enqueue(Command("submit",RmlUiAccessibilityAction.Press))&&service.Drain(host)==1,"fresh recycled control action accepted");host.Update();
        Check(host.TryTakeIntent(out intent)&&RmlUiIntentRegistry.ToLegacy(intent)=="route:news","fresh recycled control keeps authoritative action");
        service.Capture(host);var background=Command("submit",RmlUiAccessibilityAction.Press);Check(service.Enqueue(background),"page command queued before modal");
        var modal=host.OpenDocument("modal.rml",RmlUiDocumentLayer.Modal);host.Update();Check(service.Drain(host)==0,"modal ownership rejects background page action");
        service.Capture(host);Check(service.Snapshot.Document==modal&&service.Snapshot.Nodes.Any(n=>n.Id=="confirm"),"only top modal semantic tree exposed");
        var closed=Command("confirm",RmlUiAccessibilityAction.Press);Check(service.Enqueue(closed),"modal action queued before close");host.CloseDocument(modal);Check(service.Drain(host)==0,"closed modal action rejected");
        service.Capture(host);Check(service.Enqueue(Command("last",RmlUiAccessibilityAction.Focus))&&service.Drain(host)==1,"offscreen control focus scrolls native ancestor");host.Update();service.Capture(host);
        for(int frame=0;frame<20&&Node("last").Offscreen;frame++) { Thread.Sleep(16);host.Update();service.Capture(host); }
        Check(!Node("last").Offscreen&&Node("last").Focused,"focused nested option is visible after native scroll");
        service.Capture(host);var oldGeneration=Command("submit",RmlUiAccessibilityAction.Press);Check(service.Enqueue(oldGeneration),"generation command queued");
        Check(host.Reinitialize(),"real native reinitialize");Check(service.Drain(host)==0,"retired generation rejected");service.Retire();Check(service.Snapshot==RmlUiAccessibilitySnapshot.Empty,"provider retirement removes tree and commands");
    }
    Console.WriteLine($"PASS {checks} actual native accessibility assertions");
    if(Directory.Exists(Path.Combine(root,"pages/home"))) Console.WriteLine($"PASS {NativeThemeCheck.Run(root)} actual native theme/chrome assertions");
} finally { NativeLibrary.Free(module);Directory.Delete(root,true); }
