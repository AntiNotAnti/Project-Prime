using System.Runtime.InteropServices;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Components;
using OpenTK.Windowing.GraphicsLibraryFramework;
using OpenTK.Windowing.Desktop;

if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("Requires an actual macOS AppKit/GLFW content view.");
if (args.Length != 1) throw new ArgumentException("Pass the actual native bridge path.");
int checks=0;void Check(bool value,string name){if(!value)throw new InvalidOperationException(name);checks++;Console.WriteLine("PASS "+name);}
GLFW.InitHint(InitHintBool.CocoaChdirResources,false);GLFW.InitHint(InitHintBool.CocoaMenubar,false);
GLFWProvider.EnsureInitialized();GLFW.DefaultWindowHints();nint pool=Cocoa.Pool();
try {
    using var window=new CocoaWindow();
    nint module=NativeLibrary.Load(Path.GetFullPath(args[0]));
    NativeLibrary.SetDllImportResolver(typeof(RmlUiHost).Assembly,(name,_,_)=>name=="ProjectPrime.RmlUi.Native"?module:0);
    using var host=new RmlUiHost();string assets=Path.Combine(AppContext.BaseDirectory,"rmlui");
    Check(host.Initialize(1280,720,1,assets,RmlUiRenderBackend.DrawList),"actual native initialization");
    File.WriteAllText(Path.Combine(assets,"ax.rml"),"""
<rml><head><style>body{font-family:Rajdhani;}button,input{display:block;width:400px;height:40px;tab-index:auto;}</style></head>
<body><label for="email">Email address</label><input id="email" type="text" value="PRIVATE_EMAIL"/>
<label for="password">Password</label><input id="password" type="password" value="SECRET_VALUE"/>
<button id="submit" data-action="route:settings">SAVE SETTINGS</button><button id="disabled" disabled="disabled" data-action="quit">UNAVAILABLE</button></body></rml>
""");
    var document=host.OpenDocument("ax.rml",RmlUiDocumentLayer.Page);host.Update();
    var service=new RmlUiAccessibilityService();service.Capture(host);
    nint view;unsafe{view=GLFW.GetCocoaView(window.Handle);}
    nint original=Cocoa.Object(view,"accessibilityChildren");
    using(var provider=new RmlUiCocoaAccessibility(view,service)) {
        provider.Publish(service.Snapshot);
        Check(provider.Attached&&provider.ElementCount==service.Snapshot.Nodes.Count,"real NSAccessibilityElement graph attached to GLFW content view");
        nint array=Cocoa.Object(view,"accessibilityChildren");Check(Cocoa.Count(array)==(nuint)provider.ElementCount,"AppKit exposes actual virtual child array");
        nint Element(string id) => Enumerable.Range(0,(int)Cocoa.Count(array)).Select(i=>Cocoa.Index(array,i)).Single(e=>Cocoa.Text(Cocoa.Object(e,"accessibilityIdentifier"))==id);
        nint email=Element("email"),password=Element("password"),submit=Element("submit"),disabled=Element("disabled");
        Check(Cocoa.Text(Cocoa.Object(submit,"accessibilityRole"))=="AXButton"&&Cocoa.Text(Cocoa.Object(submit,"accessibilityLabel"))=="SAVE SETTINGS","actual AppKit button role/name");
        Check(Cocoa.Text(Cocoa.Object(email,"accessibilityLabel"))=="Email address"&&Cocoa.Object(email,"accessibilityParent")==view,"actual text input label/parent");
        Check(Cocoa.Text(Cocoa.Object(password,"accessibilitySubrole"))=="AXSecureTextField"&&Cocoa.Object(password,"accessibilityValue")==0,"secure text role excludes password value");
        Check(Cocoa.Object(email,"accessibilityValue")==0,"ordinary editable value excluded");
        Check(!Cocoa.Bool(disabled,"isAccessibilityEnabled")&&!Cocoa.Bool(disabled,"accessibilityPerformPress"),"disabled AppKit action rejected");
        var frame=Cocoa.Rectangle(email,"accessibilityFrame");Check(double.IsFinite(frame.X)&&double.IsFinite(frame.Y)&&frame.Width>0&&frame.Height>0,"AppKit converts framebuffer bounds into screen points");
        Check(Cocoa.Bool(submit,"accessibilityPerformPress")&&service.Drain(host)==1,"AppKit press queues owner-thread native click");host.Update();
        Check(host.TryTakeIntent(out var intent)&&RmlUiIntentRegistry.ToLegacy(intent)=="route:settings","AppKit press preserves typed engine intent");
        service.Capture(host);provider.Publish(service.Snapshot);
        Cocoa.Focus(email);Check(service.Drain(host)==1,"AppKit focus queues owner action");host.Update();service.Capture(host);provider.Publish(service.Snapshot);
        Check(Cocoa.Bool(email,"isAccessibilityFocused")&&host.FocusedElement()=="email","AppKit keyboard focus reflects actual RmlUi field");
        Cocoa.SetValue(email,"日本語 café");Check(service.Drain(host)==1&&host.ReadField(document,"email")=="日本語 café","AppKit SetValue reaches live field without metadata disclosure");
        service.Capture(host);provider.Publish(service.Snapshot);
        var oldChildren=Cocoa.Retain(array);Check(host.Reinitialize(),"real generation retirement");service.Capture(host);provider.Publish(service.Snapshot);
        Check(!Cocoa.Bool(submit,"accessibilityPerformPress"),"retired NSAccessibilityElement cannot activate later document");Cocoa.Release(oldChildren);
        Check(Task.Run(()=>{try{provider.Publish(service.Snapshot);return false;}catch(InvalidOperationException){return true;}}).GetAwaiter().GetResult(),"AppKit graph publication rejects worker thread");
    }
    Check(Cocoa.Object(view,"accessibilityChildren")==original,"detach restores original content-view accessibility children");
    for(int cycle=0;cycle<100;cycle++){using var provider=new RmlUiCocoaAccessibility(view,service);provider.Publish(service.Snapshot);Check(provider.Attached,"repeated actual AppKit attach");provider.Dispose();Check(!provider.Attached,"repeated actual AppKit detach");}
    Console.WriteLine($"PASS {checks} actual Cocoa accessibility assertions");
} finally { Cocoa.Drain(pool); }

static unsafe class Cocoa {
    const string ObjC="/usr/lib/libobjc.A.dylib";
    public readonly record struct Rect(double X,double Y,double Width,double Height);
    public static nint Pool()=>Object(Object(objc_getClass("NSAutoreleasePool"),"alloc"),"init");
    public static void Drain(nint pool)=>Void(pool,"drain");
    public static nint Object(nint obj,string selector)=>Send(obj,sel_registerName(selector));
    public static nint Retain(nint obj)=>Object(obj,"retain");public static void Release(nint obj)=>Void(obj,"release");
    public static nuint Count(nint array)=>SendCount(array,sel_registerName("count"));
    public static nint Index(nint array,int index)=>SendIndex(array,sel_registerName("objectAtIndex:"),(nuint)index);
    public static string Text(nint value)=>value==0?"":Marshal.PtrToStringUTF8(Object(value,"UTF8String"))??"";
    static nint Method(nint obj,string selector)=>method_getImplementation(class_getInstanceMethod(object_getClass(obj),sel_registerName(selector)));
    public static bool Bool(nint obj,string selector)=>((delegate*unmanaged[Cdecl]<nint,nint,byte>)Method(obj,selector))(obj,sel_registerName(selector))!=0;
    public static Rect Rectangle(nint obj,string selector)=>((delegate*unmanaged[Cdecl]<nint,nint,Rect>)Method(obj,selector))(obj,sel_registerName(selector));
    public static void Focus(nint obj)=>((delegate*unmanaged[Cdecl]<nint,nint,byte,void>)Method(obj,"setAccessibilityFocused:"))(obj,sel_registerName("setAccessibilityFocused:"),1);
    public static void SetValue(nint obj,string text)=>((delegate*unmanaged[Cdecl]<nint,nint,nint,void>)Method(obj,"setAccessibilityValue:"))(obj,sel_registerName("setAccessibilityValue:"),SendString(objc_getClass("NSString"),sel_registerName("stringWithUTF8String:"),text));
    static void Void(nint obj,string selector)=>SendVoid(obj,sel_registerName(selector));
    [DllImport(ObjC)]static extern nint objc_getClass(string name);[DllImport(ObjC)]static extern nint object_getClass(nint obj);[DllImport(ObjC)]static extern nint sel_registerName(string name);
    [DllImport(ObjC)]static extern nint class_getInstanceMethod(nint cls,nint selector);[DllImport(ObjC)]static extern nint method_getImplementation(nint method);
    [DllImport(ObjC,EntryPoint="objc_msgSend")]static extern nint Send(nint obj,nint selector);[DllImport(ObjC,EntryPoint="objc_msgSend")]static extern void SendVoid(nint obj,nint selector);
    [DllImport(ObjC,EntryPoint="objc_msgSend")]static extern nuint SendCount(nint obj,nint selector);[DllImport(ObjC,EntryPoint="objc_msgSend")]static extern nint SendIndex(nint obj,nint selector,nuint index);
    [DllImport(ObjC,EntryPoint="objc_msgSend")]static extern nint SendString(nint obj,nint selector,[MarshalAs(UnmanagedType.LPUTF8Str)]string text);
}
sealed unsafe class CocoaWindow:IDisposable {
    public Window* Handle{get;private set;}
    public CocoaWindow(){GLFW.Init();GLFW.WindowHint(WindowHintClientApi.ClientApi,ClientApi.NoApi);GLFW.WindowHint(WindowHintBool.Visible,false);Handle=GLFW.CreateWindow(640,360,"Prime accessibility contract",null,null);if(Handle==null)throw new InvalidOperationException("GLFW creation failed");}
    public void Dispose(){if(Handle!=null){GLFW.DestroyWindow(Handle);Handle=null;}}
}
