using System;
using System.Reflection;
using System.Runtime.InteropServices;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Launcher.RmlUi.Host;

/// <summary>
/// Test-only AppKit key-event queuing for the already-owned production
/// NSWindow. Normal GLFW event polling establishes NSApp.currentEvent and
/// traverses NSApplication/NSWindow routing, GLFW keyDown/interpretKeyEvents,
/// character callback and RenderWindow.OnTextInput. It does not emulate a
/// physical keyboard, switch focus, call host text input, or post global events.
/// </summary>
internal static class ProductionCocoaKeyInput
{
    private const string Lib="/usr/lib/libobjc.A.dylib";
    private const BindingFlags Instance=BindingFlags.Instance|BindingFlags.NonPublic;
    private const BindingFlags Static=BindingFlags.Static|BindingFlags.NonPublic;
    [StructLayout(LayoutKind.Sequential)] private readonly record struct Point(double X,double Y);
    internal sealed record Facts(bool Attached,bool KeyWindow,bool ViewIsFirstResponder,string ViewClass,string? FirstResponderClass,
        ulong CurrentEventType=0,ulong CurrentEventModifiers=0);
    private static nint View()
    {
        var ime=typeof(RmlUiOpenGlSmoke).GetField("_cocoaIme",Static)!.GetValue(null);
        if(ime==null)throw new InvalidOperationException("Production Cocoa IME is not attached.");
        var api=ime.GetType().GetField("_api",Instance)!.GetValue(ime)!;
        nint view=(nint)api.GetType().GetField("_view",Instance)!.GetValue(api)!;
        if(view==0)throw new InvalidOperationException("Production Cocoa view has retired.");
        return view;
    }
    internal static void BeginDiagnostics()=>RmlUiCocoaViewApi.BeginDiagnostics(View());
    internal static object Diagnostics()=>RmlUiCocoaViewApi.Diagnostics();
    internal static void EndDiagnostics()=>RmlUiCocoaViewApi.EndDiagnostics();
    internal static Facts Inspect()
    {
        if(!OperatingSystem.IsMacOS())return new(false,false,false,"not-macOS",null);
        nint view=View(),window=Object(view,Selector("window")),responder=Object(window,Selector("firstResponder"));
        string Name(nint value)=>Marshal.PtrToStringUTF8(class_getName(object_getClass(value)))??"unknown";
        nint app=Object(objc_getClass("NSApplication"),Selector("sharedApplication"));
        nint current=Object(app,Selector("currentEvent"));
        return new(true,Bool(window,Selector("isKeyWindow"))!=0,responder==view,Name(view),responder==0?null:Name(responder),
            current==0?0:Unsigned(current,Selector("type")),current==0?0:Unsigned(current,Selector("modifierFlags")));
    }
    internal static Facts Send(string characters,ushort physicalKeyCode=0)
    {
        if(!OperatingSystem.IsMacOS())throw new PlatformNotSupportedException("Production Cocoa key fixture requires macOS.");
        if(pthread_main_np()==0)throw new InvalidOperationException("Send production Cocoa keys on AppKit main thread.");
        if(string.IsNullOrEmpty(characters)||characters.Length>32)throw new ArgumentException("Use a bounded authored key fixture.");
        nint view=View(),window=Object(view,Selector("window"));
        Facts facts=Inspect();
        if(!facts.KeyWindow)throw new InvalidOperationException("Production Cocoa NSWindow must already own key focus.");
        nint text=Utf8(Object(objc_getClass("NSString"),Selector("alloc")),Selector("initWithUTF8String:"),characters);
        try
        {
            long windowNumber=Number(window,Selector("windowNumber"));
            nint app=Object(objc_getClass("NSApplication"),Selector("sharedApplication"));
            nint info=Object(objc_getClass("NSProcessInfo"),Selector("processInfo"));
            double timestamp=Double(info,Selector("systemUptime"));
            nint eventClass=objc_getClass("NSEvent");
            nint selector=Selector("keyEventWithType:location:modifierFlags:timestamp:windowNumber:context:characters:charactersIgnoringModifiers:isARepeat:keyCode:");
            foreach(ulong kind in new ulong[]{10,11}) // NSEventTypeKeyDown / KeyUp.
            {
                nint key=CreateKey(eventClass,selector,kind,new(0,0),0,timestamp,windowNumber,0,text,text,0,physicalKeyCode);
                if(key==0)throw new InvalidOperationException("AppKit rejected the authored production-window key event.");
                // Do not call NSWindow.sendEvent synchronously from RenderFrame:
                // GLFW insertText reads NSApp.currentEvent for modifiers. Only
                // the normal AppKit dequeue path owns that current-event scope.
                // This queue belongs to this process; windowNumber targets only
                // the existing fixture window, never another app or global input.
                PostEvent(app,Selector("postEvent:atStart:"),key,0);
            }
        }
        finally{Void(text,Selector("release"));}
        return facts;
    }
    private static nint Selector(string text)=>sel_registerName(text);
    [DllImport(Lib)] private static extern nint objc_getClass(string name);
    [DllImport(Lib)] private static extern nint object_getClass(nint value);
    [DllImport(Lib)] private static extern nint class_getName(nint value);
    [DllImport(Lib)] private static extern nint sel_registerName(string name);
    [DllImport(Lib,EntryPoint="objc_msgSend")] private static extern nint Object(nint value,nint selector);
    [DllImport(Lib,EntryPoint="objc_msgSend")] private static extern long Number(nint value,nint selector);
    [DllImport(Lib,EntryPoint="objc_msgSend")] private static extern byte Bool(nint value,nint selector);
    [DllImport(Lib,EntryPoint="objc_msgSend")] private static extern void Void(nint value,nint selector);
    [DllImport(Lib,EntryPoint="objc_msgSend")] private static extern void PostEvent(nint value,nint selector,nint argument,byte atStart);
    [DllImport(Lib,EntryPoint="objc_msgSend")] private static extern ulong Unsigned(nint value,nint selector);
    [DllImport(Lib,EntryPoint="objc_msgSend")] private static extern double Double(nint value,nint selector);
    [DllImport(Lib,EntryPoint="objc_msgSend")] private static extern nint Utf8(nint value,nint selector,[MarshalAs(UnmanagedType.LPUTF8Str)]string text);
    [DllImport(Lib,EntryPoint="objc_msgSend")] private static extern nint CreateKey(nint value,nint selector,ulong kind,Point location,
        ulong modifiers,double timestamp,long windowNumber,nint context,nint characters,nint ignoringModifiers,byte repeat,ushort keyCode);
    [DllImport("/usr/lib/libSystem.B.dylib")] private static extern int pthread_main_np();
}
