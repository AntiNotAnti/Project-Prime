using System.Runtime.InteropServices;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Components;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.GraphicsLibraryFramework;

if (!OperatingSystem.IsMacOS()) { Console.WriteLine("Cocoa integration check requires macOS.");return; }
if (args.Length!=1) throw new InvalidOperationException("Pass the actual native bridge library path.");
int checks=0;
void Check(bool value,string message) { checks++;if(!value) throw new InvalidOperationException(message); }
Console.WriteLine("Creating hidden GLFW window");
GLFW.InitHint(InitHintBool.CocoaChdirResources,false);
GLFW.InitHint(InitHintBool.CocoaMenubar,false);
GLFWProvider.EnsureInitialized();GLFW.DefaultWindowHints();
nint pool=Cocoa.Pool();
using var window=new CocoaTestWindow();
nint module=NativeLibrary.Load(Path.GetFullPath(args[0]));
NativeLibrary.SetDllImportResolver(typeof(RmlUiHost).Assembly,(name,_,_)=>name=="ProjectPrime.RmlUi.Native"?module:0);
Console.WriteLine("Creating native RmlUi host");
using var host=new RmlUiHost();
Check(host.Initialize(1280,720,1,Path.Combine(AppContext.BaseDirectory,"rmlui"),RmlUiRenderBackend.DrawList),"Native host initialization failed");
string fixture=Path.Combine(AppContext.BaseDirectory,"rmlui","cocoa.rml");
File.WriteAllText(fixture,"""<rml><head><style>body { font-family: Rajdhani; font-weight: 600; } input { display:block; width:400px; height:40px; }</style></head><body><input id="name" type="text" /><input id="other" type="text" /><button id="submit">APPLY</button></body></rml>""");
nint view;unsafe { view=GLFW.GetCocoaView(window.Handle); }
nint original=Cocoa.Class(view);
Console.WriteLine("Installing Cocoa text client");
var candidateApi = new CountingCocoaImeApi();
using(var ime=new RmlUiCocoaIme(host,view,()=>true,candidateApi))
{
    Check(ime!=null && ime.Attached && Cocoa.Class(view)!=original,"Actual GLFW view did not acquire its isolated text client class");
    var document=host.OpenDocument("cocoa.rml",RmlUiDocumentLayer.Modal);
    host.Update(); host.FocusDocument(document,"submit");
    Check(!host.TextInputActive,"Button unexpectedly owns a native text context");
    for(int frame=0;frame<100;frame++)ime!.RefreshCandidatePosition();
    Check(candidateApi.Invalidations==0,"Idle button focus unnecessarily invalidated Cocoa candidate coordinates");
    host.SetField(document,"name","λ😀");host.Update();host.FocusDocument(document,"name");
    ime!.RefreshCandidatePosition();
    Check(candidateApi.Invalidations==1,"Actual text focus did not immediately refresh Cocoa candidate coordinates");
    host.Resize(1440,900,1);host.Update();ime.RefreshCandidatePosition();
    Check(candidateApi.Invalidations==2,"Text candidate geometry refresh was delayed after viewport change");
    host.Resize(1280,720,1);host.Update();
    var accessibility=new RmlUiAccessibilityService();
    using var ax=new RmlUiCocoaAccessibility(view,accessibility);
    ax.Publish(accessibility.Capture(host));
    Check(ax.Attached&&ax.ElementCount>0,"Cocoa accessibility did not coexist with the per-view text client");
    host.Input.Key(10,true,RmlUiInputModifiers.Control);host.Input.Key(10,false,RmlUiInputModifiers.Control);
    Check(host.TryGetTextInputState(out var state) && host.TryGetTextSelectionUtf16(state,out int start,out int end) && start==3 && end==3,
        "Native UTF16 selection exposed wrong supplementary scalar offsets");
    var selected=Cocoa.Range(view,"selectedRange");
    Check(selected==new RmlUiCocoaRange(3,0),"Cocoa selectedRange did not return native UTF16 indices");
    Console.WriteLine("Invoking actual marked-text callback");
    Cocoa.Mark(view,"日本😀",3,0);
    Check(host.ReadField(document,"name")=="λ😀日本😀" && host.TryGetTextInputState(out var preedit) && preedit.Composing,
        "Actual Cocoa marked text did not replace the native preedit range");
    Check(Cocoa.Bool(view,"hasMarkedText") && Cocoa.Range(view,"markedRange")==new RmlUiCocoaRange(3,4),"Actual Cocoa marked range lost supplementary UTF16 length");
    ax.Publish(accessibility.Capture(host));
    Check(!accessibility.Snapshot.Nodes.Any(n=>n.Label.Contains("λ😀")||n.Label.Contains("日本😀")),"Combined Cocoa accessibility exposed marked/field text");
    Console.WriteLine("Querying Cocoa candidate rectangle");
    var rectangle=Cocoa.Rect(view,"firstRectForCharacterRange:actualRange:");
    Check(double.IsFinite(rectangle.X)&&double.IsFinite(rectangle.Y)&&rectangle.Width>0&&rectangle.Height>0,"Cocoa candidate geometry was not converted to screen points");
    Cocoa.Mark(view,"日",1,0);Check(host.ReadField(document,"name")=="λ😀日","Cocoa repeated marked text appended instead of replacing");
    Cocoa.Insert(view,"日本");Check(host.ReadField(document,"name")=="λ😀日本"&&!Cocoa.Bool(view,"hasMarkedText"),"Cocoa commit was duplicated or left marked text active");
    Console.WriteLine("Cancelling Cocoa composition");
    Cocoa.Mark(view,"取消",2,0);ime!.Cancel();
    Check(host.ReadField(document,"name")=="λ😀日本"&&!Cocoa.Bool(view,"hasMarkedText"),"Cocoa cancellation lost the original draft");
    host.FocusDocument(document,"other");Cocoa.Insert(view,"late");
    Check(host.ReadField(document,"other")=="","Canceled Cocoa commit entered a different focused field");
    host.FocusDocument(document,"name");Cocoa.Mark(view,"retired",7,0);host.CloseDocument(document);Cocoa.Insert(view,"late");
    Check(!host.IsAlive(document)&&host.ReadField(host.HomeDocument,"room_key_input")=="","Retired Cocoa composition reached a later document");
    accessibility.Retire();ax.Publish(accessibility.Snapshot);
    Check(ax.ElementCount==0,"Hidden menu accessibility kept retired input nodes");
    ax.Dispose();
    ime.Dispose();Check(!ime.Attached&&Cocoa.Class(view)==original,"Cocoa detach did not restore exactly the original view class");
}
for(int cycle=0;cycle<100;cycle++)
{
    using var ime=RmlUiCocoaIme.TryAttach(host,view,()=>true)!;
    Check(ime.Attached,"Repeated Cocoa attach failed");ime.Dispose();Check(Cocoa.Class(view)==original,"Repeated Cocoa detach retained a native hook");
}
Check(Task.Run(()=> { try { RmlUiCocoaIme.TryAttach(host,view,()=>true);return false; }catch(InvalidOperationException){return true;} }).GetAwaiter().GetResult(),"Cocoa hook changed class from a worker");
var last=RmlUiCocoaIme.TryAttach(host,view,()=>true)!;window.Dispose();GLFW.PollEvents();Cocoa.Drain(pool);
Check(!last.Attached,"Actual Cocoa view destruction retained its managed callback root");last.Dispose();
Console.WriteLine($"PASS: {checks} actual Cocoa/GLFW/RmlUi composition and lifecycle checks.");

static unsafe class Cocoa
{
    const string ObjC="/usr/lib/libobjc.A.dylib";
    public static nint Class(nint value)=>object_getClass(value);
    public static nint Pool()=>objc_msgSend(objc_msgSend(objc_getClass("NSAutoreleasePool"),sel_registerName("alloc")),sel_registerName("init"));
    public static void Drain(nint pool)=>objc_msgSend(pool,sel_registerName("drain"));
    static nint Method(nint value,string selector)=>method_getImplementation(class_getInstanceMethod(Class(value),sel_registerName(selector)));
    static nint String(string text)=>objc_msgSend_string(objc_getClass("NSString"),sel_registerName("stringWithUTF8String:"),text);
    public static void Mark(nint view,string text,int location,int length)=>((delegate* unmanaged[Cdecl]<nint,nint,nint,RmlUiCocoaRange,RmlUiCocoaRange,void>)Method(view,"setMarkedText:selectedRange:replacementRange:"))(view,sel_registerName("setMarkedText:selectedRange:replacementRange:"),String(text),new((ulong)location,(ulong)length),RmlUiCocoaRange.None);
    public static void Insert(nint view,string text)=>((delegate* unmanaged[Cdecl]<nint,nint,nint,RmlUiCocoaRange,void>)Method(view,"insertText:replacementRange:"))(view,sel_registerName("insertText:replacementRange:"),String(text),RmlUiCocoaRange.None);
    public static RmlUiCocoaRange Range(nint view,string selector)=>((delegate* unmanaged[Cdecl]<nint,nint,RmlUiCocoaRange>)Method(view,selector))(view,sel_registerName(selector));
    public static bool Bool(nint view,string selector)=>((delegate* unmanaged[Cdecl]<nint,nint,byte>)Method(view,selector))(view,sel_registerName(selector))!=0;
    public static RmlUiCocoaRect Rect(nint view,string selector)=>((delegate* unmanaged[Cdecl]<nint,nint,RmlUiCocoaRange,nint,RmlUiCocoaRect>)Method(view,selector))(view,sel_registerName(selector),new(0,0),0);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void MarkCall(nint self,nint selector,nint text,RmlUiCocoaRange selected,RmlUiCocoaRange replacement);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void InsertCall(nint self,nint selector,nint text,RmlUiCocoaRange replacement);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate RmlUiCocoaRange RangeCall(nint self,nint selector);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate byte BoolCall(nint self,nint selector);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate RmlUiCocoaRect RectCall(nint self,nint selector,RmlUiCocoaRange range,nint actual);
    [DllImport(ObjC)] static extern nint objc_msgSend(nint value,nint selector);
    [DllImport(ObjC)] static extern nint object_getClass(nint value);
    [DllImport(ObjC)] static extern nint objc_getClass(string name);
    [DllImport(ObjC)] static extern nint sel_registerName(string name);
    [DllImport(ObjC)] static extern nint class_getInstanceMethod(nint cls,nint selector);
    [DllImport(ObjC)] static extern nint method_getImplementation(nint method);
    [DllImport(ObjC,EntryPoint="objc_msgSend")] static extern nint objc_msgSend_string(nint value,nint selector,[MarshalAs(UnmanagedType.LPUTF8Str)]string text);
}

sealed unsafe class CocoaTestWindow : IDisposable
{
    public Window* Handle { get; private set; }
    public CocoaTestWindow()
    {
        GLFW.Init();
        GLFW.WindowHint(WindowHintClientApi.ClientApi,ClientApi.NoApi);
        GLFW.WindowHint(WindowHintBool.Visible,false);
        Handle=GLFW.CreateWindow(640,360,"RmlUi Cocoa contract",null,null);
        if(Handle==null) throw new InvalidOperationException("Cocoa test window could not be created.");
    }
    public void Dispose() { if(Handle!=null) { GLFW.DestroyWindow(Handle);Handle=null; } }
}

sealed class CountingCocoaImeApi : IRmlUiCocoaImeApi
{
    private readonly RmlUiCocoaViewApi _actual = new();
    public int Invalidations;
    public bool Attach(nint view,RmlUiCocoaCallbacks callbacks)=>_actual.Attach(view,callbacks);
    public bool Detach()=>_actual.Detach();
    public void DiscardMarkedText()=>_actual.DiscardMarkedText();
    public void InvalidateCandidatePosition(){Invalidations++;_actual.InvalidateCandidatePosition();}
}
