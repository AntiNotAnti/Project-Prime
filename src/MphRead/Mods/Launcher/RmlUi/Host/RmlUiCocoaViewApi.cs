using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MphRead.Mods.Launcher.RmlUi.Host;

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct RmlUiCocoaRect(double X, double Y, double Width, double Height);

/// <summary>Per-view Objective-C subclass. No global method swizzling and no editable text is exposed through accessibility queries.</summary>
internal sealed class RmlUiCocoaViewApi : IRmlUiCocoaImeApi
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private sealed class ViewClass
    {
        public nint Base, Derived;
        public readonly Dictionary<string,nint> Methods = new();
    }
    private static readonly Dictionary<nint,ViewClass> Classes = new();
    private static readonly Dictionary<nint,RmlUiCocoaViewApi> Views = new();
    private static readonly MarkedMethod MarkedCallback = OnMarked;
    private static readonly InsertMethod InsertCallback = OnInsert;
    private static readonly VoidMethod UnmarkCallback = OnUnmark, DeallocCallback = OnDealloc;
    private static readonly ObjectMethod KeyCallback = OnKey;
    private static readonly RangeMethod SelectionCallback = OnSelection, RangeCallback = OnRange;
    private static readonly BoolMethod HasMarkedCallback = OnHasMarked;
    private static readonly RectRangeMethod RectCallback = OnRect;
    private nint _view;
    private ViewClass? _class;
    private RmlUiCocoaCallbacks? _callbacks;
    public bool Attach(nint view, RmlUiCocoaCallbacks callbacks)
    {
        RequireMainThread();
        if (view == 0 || _view != 0 || Views.ContainsKey(view)) return false;
        nint original = object_getClass(view);
        if (original == 0) return false;
        if (!Classes.TryGetValue(original,out var record))
        {
            record = new() { Base = original };
            record.Derived = objc_allocateClassPair(original,"ProjectPrimeRmlText_"+Guid.NewGuid().ToString("N"),0);
            if(record.Derived==0) return false;
            bool Add(string selector, Delegate callback)
            {
                nint name=sel_registerName(selector),method=class_getInstanceMethod(original,name);
                if(method==0) return false;
                record.Methods[selector]=method_getImplementation(method);
                return class_addMethod(record.Derived,name,Marshal.GetFunctionPointerForDelegate(callback),method_getTypeEncoding(method))!=0;
            }
            if (!Add("setMarkedText:selectedRange:replacementRange:",MarkedCallback)
                || !Add("insertText:replacementRange:",InsertCallback) || !Add("unmarkText",UnmarkCallback)
                || !Add("keyDown:",KeyCallback) || !Add("selectedRange",SelectionCallback)
                || !Add("markedRange",RangeCallback) || !Add("hasMarkedText",HasMarkedCallback)
                || !Add("firstRectForCharacterRange:actualRange:",RectCallback) || !Add("dealloc",DeallocCallback))
            { objc_disposeClassPair(record.Derived); return false; }
            objc_registerClassPair(record.Derived); Classes.Add(original,record);
        }
        _view=view;_class=record;_callbacks=callbacks;
        Views.Add(view,this); // The view does not root reverse-P/Invoke delegates or callbacks.
        nint previous=object_setClass(view,record.Derived);
        if(previous!=original)
        {
            if(previous!=0) object_setClass(view,previous);
            Views.Remove(view);_view=0;_callbacks=null;_class=null;return false;
        }
        return true;
    }
    public bool Detach()
    {
        RequireMainThread();
        if(_view==0) return true;
        if(object_getClass(_view)!=_class!.Derived) return false;
        object_setClass(_view,_class.Base);Views.Remove(_view);_view=0;_callbacks=null;return true;
    }
    public void DiscardMarkedText()
    {
        RequireMainThread(); if(_view==0) return;
        var context=SendObject(_view,"inputContext");
        if(context!=0) SendVoid(context,"discardMarkedText");
        Original<VoidMethod>("unmarkText")(_view,sel_registerName("unmarkText"));
    }
    public void InvalidateCandidatePosition()
    {
        RequireMainThread(); if(_view==0) return;
        var context=SendObject(_view,"inputContext");
        if(context!=0) SendVoid(context,"invalidateCharacterCoordinates");
    }
    private T Original<T>(string selector) where T:Delegate => Marshal.GetDelegateForFunctionPointer<T>(_class!.Methods[selector]);
    private static RmlUiCocoaViewApi? Find(nint self) => Views.GetValueOrDefault(self);
    private static void OnMarked(nint self,nint selector,nint text,RmlUiCocoaRange selection,RmlUiCocoaRange replacement)
    {
        var api=Find(self);if(api==null) return;
        try { api._callbacks!.Marked(Text(text),checked((int)Math.Min(selection.Location,int.MaxValue)),checked((int)Math.Min(selection.Length,int.MaxValue))); }
        catch { /* Never unwind through AppKit or log composition/password content. */ }
        api.Original<MarkedMethod>("setMarkedText:selectedRange:replacementRange:")(self,selector,text,selection,replacement);
    }
    private static void OnInsert(nint self,nint selector,nint text,RmlUiCocoaRange replacement)
    {
        var api=Find(self);if(api==null) return;
        bool consumed=api._callbacks!.Owns();
        try { consumed=api._callbacks.Insert(Text(text)); } catch { }
        if(consumed) api.Original<VoidMethod>("unmarkText")(self,sel_registerName("unmarkText"));
        else api.Original<InsertMethod>("insertText:replacementRange:")(self,selector,text,replacement);
    }
    private static void OnUnmark(nint self,nint selector)
    {
        var api=Find(self);if(api==null) return;
        try { api._callbacks!.Unmark(); } catch { }
        api.Original<VoidMethod>("unmarkText")(self,selector);
    }
    private static void OnKey(nint self,nint selector,nint key)
    {
        var api=Find(self);if(api==null) return;
        try { api._callbacks!.NewKey(); } catch { }
        api.Original<ObjectMethod>("keyDown:")(self,selector,key);
    }
    private static RmlUiCocoaRange OnSelection(nint self,nint selector)
    {
        var api=Find(self);if(api==null) return RmlUiCocoaRange.None;
        try { var selected=api._callbacks!.Selection(); if(selected.Location!=ulong.MaxValue) return selected; } catch { }
        return api.Original<RangeMethod>("selectedRange")(self,selector);
    }
    private static RmlUiCocoaRange OnRange(nint self,nint selector)
    {
        var api=Find(self);if(api==null) return RmlUiCocoaRange.None;
        try { if(api._callbacks!.Owns()) return api._callbacks.MarkedRange(); } catch { }
        return api.Original<RangeMethod>("markedRange")(self,selector);
    }
    private static byte OnHasMarked(nint self,nint selector)
    {
        var api=Find(self);if(api==null) return 0;
        try { if(api._callbacks!.Owns()) return api._callbacks.MarkedRange().Length>0?(byte)1:(byte)0; } catch { }
        return api.Original<BoolMethod>("hasMarkedText")(self,selector);
    }
    private static RmlUiCocoaRect OnRect(nint self,nint selector,RmlUiCocoaRange range,nint actualRange)
    {
        var api=Find(self);if(api==null) return default;
        try
        {
            if(api._callbacks!.Candidate() is { } candidate && candidate.Width>0 && candidate.Height>0)
            {
                RmlUiCocoaRect bounds=Method<RectMethod>(self,"bounds")(self,sel_registerName("bounds"));
                double sx=bounds.Width/candidate.Width,sy=bounds.Height/candidate.Height;
                bool flipped=Method<BoolMethod>(self,"isFlipped")(self,sel_registerName("isFlipped"))!=0;
                var b=candidate.Bounds;
                var local=new RmlUiCocoaRect(bounds.X+b.X*sx,
                    bounds.Y+(flipped?b.Y*sy:bounds.Height-(b.Y+b.Height)*sy),b.Width*sx,b.Height*sy);
                var window=SendObject(self,"window");
                var inWindow=Method<RectViewMethod>(self,"convertRect:toView:")(self,sel_registerName("convertRect:toView:"),local,0);
                if(actualRange!=0) Marshal.StructureToPtr(range,actualRange,false);
                return window==0?inWindow:Method<RectArgumentMethod>(window,"convertRectToScreen:")(window,sel_registerName("convertRectToScreen:"),inWindow);
            }
        }
        catch { /* Field bounds remain the documented fallback when caret geometry is unavailable. */ }
        return api.Original<RectRangeMethod>("firstRectForCharacterRange:actualRange:")(self,selector,range,actualRange);
    }
    private static void OnDealloc(nint self,nint selector)
    {
        var api=Find(self);if(api==null) return;
        var original=api.Original<VoidMethod>("dealloc");
        try { api._callbacks!.Destroyed(); } catch { }
        finally { Views.Remove(self);api._view=0;api._callbacks=null;original(self,selector); }
    }
    private static string Text(nint text)
    {
        if(text==0) return "";
        if(SendBoolObject(text,"isKindOfClass:",objc_getClass("NSAttributedString"))!=0) text=SendObject(text,"string");
        return Marshal.PtrToStringUTF8(SendObject(text,"UTF8String"))??"";
    }
    private static T Method<T>(nint self,string selector) where T:Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(method_getImplementation(class_getInstanceMethod(object_getClass(self),sel_registerName(selector))));
    private static nint SendObject(nint self,string selector) => objc_msgSend(self,sel_registerName(selector));
    private static void SendVoid(nint self,string selector) => objc_msgSend_void(self,sel_registerName(selector));
    private static byte SendBoolObject(nint self,string selector,nint argument) => objc_msgSend_bool_object(self,sel_registerName(selector),argument);
    private static void RequireMainThread() { if(pthread_main_np()==0) throw new InvalidOperationException("Cocoa view hooks require the macOS main thread."); }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void MarkedMethod(nint self,nint selector,nint text,RmlUiCocoaRange selection,RmlUiCocoaRange replacement);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void InsertMethod(nint self,nint selector,nint text,RmlUiCocoaRange replacement);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void VoidMethod(nint self,nint selector);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ObjectMethod(nint self,nint selector,nint argument);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate RmlUiCocoaRange RangeMethod(nint self,nint selector);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate byte BoolMethod(nint self,nint selector);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate RmlUiCocoaRect RectMethod(nint self,nint selector);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate RmlUiCocoaRect RectArgumentMethod(nint self,nint selector,RmlUiCocoaRect rect);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate RmlUiCocoaRect RectViewMethod(nint self,nint selector,RmlUiCocoaRect rect,nint view);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate RmlUiCocoaRect RectRangeMethod(nint self,nint selector,RmlUiCocoaRange range,nint actual);
    [DllImport(ObjC)] private static extern nint objc_getClass(string name);
    [DllImport(ObjC)] private static extern nint object_getClass(nint value);
    [DllImport(ObjC)] private static extern nint object_setClass(nint value,nint cls);
    [DllImport(ObjC)] private static extern nint objc_allocateClassPair(nint superclass,string name,nuint extraBytes);
    [DllImport(ObjC)] private static extern void objc_registerClassPair(nint cls);
    [DllImport(ObjC)] private static extern void objc_disposeClassPair(nint cls);
    [DllImport(ObjC)] private static extern nint sel_registerName(string name);
    [DllImport(ObjC)] private static extern nint class_getInstanceMethod(nint cls,nint selector);
    [DllImport(ObjC)] private static extern nint method_getImplementation(nint method);
    [DllImport(ObjC)] private static extern nint method_getTypeEncoding(nint method);
    [DllImport(ObjC)] private static extern byte class_addMethod(nint cls,nint selector,nint imp,nint types);
    [DllImport(ObjC)] private static extern nint objc_msgSend(nint self,nint selector);
    [DllImport(ObjC,EntryPoint="objc_msgSend")] private static extern void objc_msgSend_void(nint self,nint selector);
    [DllImport(ObjC,EntryPoint="objc_msgSend")] private static extern byte objc_msgSend_bool_object(nint self,nint selector,nint argument);
    [DllImport("/usr/lib/libSystem.B.dylib")] private static extern int pthread_main_np();
}
