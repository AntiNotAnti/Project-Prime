using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MphRead.Mods.Launcher.RmlUi.Host;
internal sealed partial class RmlUiCocoaViewApi
{
    internal sealed record Diagnostic(string Stage,string ViewClass,string BaseClass,ulong EventType,ulong Modifiers,
        ulong CharactersLength,ulong IgnoringModifiersLength,ulong InsertLength,ulong ReplacementLocation,ulong ReplacementLength,
        bool Owned,bool Consumed,string Command,ulong KeyImplementation,ulong InsertImplementation);
    private static nint _diagnosticView;
    private static readonly List<Diagnostic> _diagnostics=new();
    // Explicit fixture scope only. Text contents, password data and event text
    // are never retained; only bounded numeric lengths/ranges and class names.
    internal static void BeginDiagnostics(nint view)
    {RequireMainThread();_diagnostics.Clear();_diagnosticView=view;}
    internal static Diagnostic[] Diagnostics()
    {RequireMainThread();return _diagnostics.ToArray();}
    internal static void EndDiagnostics()
    {RequireMainThread();_diagnosticView=0;_diagnostics.Clear();}
    private static void Trace(RmlUiCocoaViewApi api,string stage,nint key=0,nint text=0,
        RmlUiCocoaRange? replacement=null,bool consumed=false,nint command=0)
    {
        if(_diagnosticView==0||api._view!=_diagnosticView||_diagnostics.Count>=64)return;
        try
        {
            string Class(nint value)=>value==0?"none":Marshal.PtrToStringUTF8(class_getName(object_getClass(value)))??"unknown";
            string Base(nint cls)=>cls==0?"none":Marshal.PtrToStringUTF8(class_getName(cls))??"unknown";
            ulong Length(nint value)=>value==0?0:DiagnosticUnsigned(value,sel_registerName("length"));
            if(text!=0&&SendBoolObject(text,"isKindOfClass:",objc_getClass("NSAttributedString"))!=0)text=SendObject(text,"string");
            var range=replacement??RmlUiCocoaRange.None;
            _diagnostics.Add(new(stage,Class(api._view),Base(api._class!.Base),
                key==0?0:DiagnosticUnsigned(key,sel_registerName("type")),
                key==0?0:DiagnosticUnsigned(key,sel_registerName("modifierFlags")),
                key==0?0:Length(SendObject(key,"characters")),
                key==0?0:Length(SendObject(key,"charactersIgnoringModifiers")),Length(text),range.Location,range.Length,
                api._callbacks!.Owns(),consumed,command==0?"":Marshal.PtrToStringUTF8(sel_getName(command))??"unknown",
                (ulong)api._class.Methods["keyDown:"],(ulong)api._class.Methods["insertText:replacementRange:"]));
        }
        catch { /* Diagnostics cannot alter the text owner or native callback. */ }
    }
    [DllImport(ObjC)] private static extern nint class_getName(nint cls);
    [DllImport(ObjC)] private static extern nint sel_getName(nint selector);
    [DllImport(ObjC,EntryPoint="objc_msgSend")] private static extern ulong DiagnosticUnsigned(nint value,nint selector);
}
