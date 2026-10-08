using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Components;

/// <summary>AppKit NSAccessibilityElement descendants attached to the actual
/// GLFW content view. AppKit callbacks read published metadata and enqueue
/// commands; they never enter RmlUi or expose editable field values.</summary>
public sealed class RmlUiCocoaAccessibility : IDisposable
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib", AppKit = "/System/Library/Frameworks/AppKit.framework/AppKit";
    private readonly record struct Rect(double X, double Y, double Width, double Height);
    private sealed record Descriptor(RmlUiAccessibilitySnapshot Snapshot, RmlUiAccessibilityNode Node);
    private sealed class Element
    {
        public readonly RmlUiCocoaAccessibility Owner;
        public readonly nint Handle;
        public Descriptor Value;
        public Element(RmlUiCocoaAccessibility owner, nint handle, Descriptor value) { Owner = owner; Handle = handle; Value = value; }
    }
    private static readonly ConcurrentDictionary<nint, Element> Elements = new();
    private static readonly BoolMethod PressCallback = Press, IncrementCallback = Increment, DecrementCallback = Decrement, FocusedCallback = Focused;
    private static readonly SetBoolMethod FocusCallback = SetFocused;
    private static readonly SetObjectMethod ValueCallback = SetValue;
    private static readonly ObjectMethod ReadValueCallback = ReadValue;
    private static readonly SelectorMethod SelectorCallback = SelectorAllowed;
    private static nint _elementClass, _framework;
    private readonly RmlUiAccessibilityService _service;
    private readonly Dictionary<string, Element> _elements = new(StringComparer.Ordinal);
    private nint _view, _oldChildren, _oldRole, _oldLabel;
    private byte _oldElement;
    private bool _disposed;
    private RmlUiDocumentToken _document;
    private ulong _revision;
    private Rect _bounds;
    public bool Attached => _view != 0;
    public int ElementCount => _elements.Count;
    public RmlUiCocoaAccessibility(nint contentView, RmlUiAccessibilityService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        if (!OperatingSystem.IsMacOS() || contentView == 0) return;
        RequireMainThread();
        _framework = _framework == 0 ? NativeLibrary.Load(AppKit) : _framework;
        EnsureClass();
        _view = contentView;
        _oldChildren = Retain(Object(_view, "accessibilityChildren"));
        _oldRole = Retain(Object(_view, "accessibilityRole"));
        _oldLabel = Retain(Object(_view, "accessibilityLabel"));
        _oldElement = Boolean(_view, "isAccessibilityElement");
        SetBool(_view, "setAccessibilityElement:", 1);
        SetString(_view, "setAccessibilityRole:", "AXGroup");
        SetString(_view, "setAccessibilityLabel:", "Project Prime");
    }
    public void Publish(RmlUiAccessibilitySnapshot snapshot)
    {
        if (!Attached || _disposed) return;
        RequireMainThread();
        Rect bounds = GetRect(_view, "bounds");
        if (snapshot.Document == _document && snapshot.Revision == _revision && bounds == _bounds) return;
        if (snapshot.Document != _document)
            foreach (var key in new List<string>(_elements.Keys)) Remove(key);
        _document = snapshot.Document; _revision = snapshot.Revision; _bounds = bounds;
        bool flipped = Boolean(_view, "isFlipped") != 0;
        double sx = snapshot.FramebufferWidth > 0 ? bounds.Width / snapshot.FramebufferWidth : 1;
        double sy = snapshot.FramebufferHeight > 0 ? bounds.Height / snapshot.FramebufferHeight : 1;
        nint children = Object(objc_getClass("NSMutableArray"), "new");
        var live = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in snapshot.Nodes)
        {
            live.Add(node.Key);
            if (!_elements.TryGetValue(node.Key, out var element)) {
                nint handle = Object(_elementClass, "new");
                if (handle == 0) continue;
                element = new(this, handle, new(snapshot, node)); _elements.Add(node.Key, element); Elements.TryAdd(handle, element);
                SetObject(handle, "setAccessibilityParent:", _view);
            }
            Volatile.Write(ref element.Value, new Descriptor(snapshot, node));
            SetString(element.Handle, "setAccessibilityRole:", Role(node));
            SetString(element.Handle, "setAccessibilityLabel:", node.Label);
            SetString(element.Handle, "setAccessibilityIdentifier:", node.Id.Length == 0 ? node.Key : node.Id);
            if (node.Protected) SetString(element.Handle, "setAccessibilitySubrole:", "AXSecureTextField");
            else SetObject(element.Handle, "setAccessibilitySubrole:", 0);
            SetBool(element.Handle, "setAccessibilityEnabled:", node.Enabled ? (byte)1 : (byte)0);
            SetBool(element.Handle, "setAccessibilityElement:", node.Offscreen ? (byte)0 : (byte)1);
            var local = new Rect(bounds.X + node.X * sx,
                bounds.Y + (flipped ? node.Y * sy : bounds.Height - (node.Y + node.Height) * sy), node.Width * sx, node.Height * sy);
            SetRect(element.Handle, "setAccessibilityFrameInParentSpace:", local);
            SetObject(children, "addObject:", element.Handle);
        }
        SetObject(_view, "setAccessibilityChildren:", children);
        // Navigation order follows DOM order, including offscreen descendants
        // that can be brought into view through their focus action.
        SetObject(_view, "setAccessibilityChildrenInNavigationOrder:", children);
        Release(children);
        foreach (var key in new List<string>(_elements.Keys)) if (!live.Contains(key)) Remove(key);
        NSAccessibilityPostNotification(_view, String("AXLayoutChanged"));
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_view == 0) return;
        RequireMainThread();
        SetObject(_view, "setAccessibilityChildren:", _oldChildren);
        SetObject(_view, "setAccessibilityChildrenInNavigationOrder:", _oldChildren);
        SetObject(_view, "setAccessibilityRole:", _oldRole); SetObject(_view, "setAccessibilityLabel:", _oldLabel);
        SetBool(_view, "setAccessibilityElement:", _oldElement);
        foreach (var key in new List<string>(_elements.Keys)) Remove(key);
        Release(_oldChildren); Release(_oldRole); Release(_oldLabel);
        _view = _oldChildren = _oldRole = _oldLabel = 0;
    }
    private void Remove(string key) { var element = _elements[key]; Elements.TryRemove(element.Handle, out _); _elements.Remove(key); SetObject(element.Handle, "setAccessibilityParent:", 0); Release(element.Handle); }
    private static string Role(RmlUiAccessibilityNode node) => node.Role switch {
        "button" or "tab" => "AXButton", "link" => "AXLink", "textbox" => "AXTextField", "checkbox" or "switch" => "AXCheckBox",
        "radio" => "AXRadioButton", "slider" => "AXSlider", "combobox" => "AXPopUpButton", "img" => "AXImage",
        "dialog" => "AXDialog", "region" => "AXScrollArea", "group" or "list" or "tablist" => "AXGroup", _ => "AXStaticText" };
    private static bool Queue(nint self, RmlUiAccessibilityAction action, string text = "")
    {
        if (!Elements.TryGetValue(self, out var element) || element.Owner._disposed) return false;
        var value = Volatile.Read(ref element.Value);
        return element.Owner._service.Enqueue(new(value.Snapshot.Document, value.Snapshot.Revision, value.Node.Key, action, text));
    }
    private static byte Press(nint self, nint selector) { try { return Queue(self, RmlUiAccessibilityAction.Press) ? (byte)1 : (byte)0; } catch { return 0; } }
    private static byte Increment(nint self, nint selector) { try { return Queue(self, RmlUiAccessibilityAction.ScrollForward) ? (byte)1 : (byte)0; } catch { return 0; } }
    private static byte Decrement(nint self, nint selector) { try { return Queue(self, RmlUiAccessibilityAction.ScrollBackward) ? (byte)1 : (byte)0; } catch { return 0; } }
    private static byte Focused(nint self, nint selector) => Elements.TryGetValue(self, out var element) && Volatile.Read(ref element.Value).Node.Focused ? (byte)1 : (byte)0;
    private static void SetFocused(nint self, nint selector, byte focused) { try { if (focused != 0) Queue(self, RmlUiAccessibilityAction.Focus); } catch { } }
    private static void SetValue(nint self, nint selector, nint value)
    {
        try { if (value != 0) Queue(self, RmlUiAccessibilityAction.SetText, Marshal.PtrToStringUTF8(Object(value, "UTF8String")) ?? ""); } catch { }
    }
    private static nint ReadValue(nint self, nint selector) => 0;
    private static byte SelectorAllowed(nint self, nint selector, nint requested)
    {
        if (!Elements.TryGetValue(self, out var element)) return 0;
        var node = Volatile.Read(ref element.Value).Node;
        string name = Marshal.PtrToStringUTF8(sel_getName(requested)) ?? "";
        var needed = name switch { "accessibilityPerformPress" => RmlUiAccessibilityActions.Press,
            "accessibilityPerformIncrement" or "accessibilityPerformDecrement" => RmlUiAccessibilityActions.Scroll,
            "setAccessibilityFocused:" => RmlUiAccessibilityActions.Focus, "setAccessibilityValue:" => RmlUiAccessibilityActions.SetText,
            _ => RmlUiAccessibilityActions.None };
        return needed == RmlUiAccessibilityActions.None || node.Enabled && (node.Actions & needed) != 0 ? (byte)1 : (byte)0;
    }
    private static void EnsureClass()
    {
        if (_elementClass != 0) return;
        nint cls = objc_allocateClassPair(objc_getClass("NSAccessibilityElement"), "ProjectPrimeRmlAccessibility_" + Guid.NewGuid().ToString("N"), 0);
        if (cls == 0) throw new InvalidOperationException("AppKit semantic element class creation failed.");
        void Add(string name, Delegate callback, string encoding) {
            if (class_addMethod(cls, sel_registerName(name), Marshal.GetFunctionPointerForDelegate(callback), encoding) == 0)
                throw new InvalidOperationException("AppKit semantic selector registration failed.");
        }
        Add("accessibilityPerformPress", PressCallback, "c@:"); Add("accessibilityPerformIncrement", IncrementCallback, "c@:");
        Add("accessibilityPerformDecrement", DecrementCallback, "c@:"); Add("isAccessibilityFocused", FocusedCallback, "c@:");
        Add("setAccessibilityFocused:", FocusCallback, "v@:c"); Add("setAccessibilityValue:", ValueCallback, "v@:@");
        Add("accessibilityValue", ReadValueCallback, "@@:"); Add("isAccessibilitySelectorAllowed:", SelectorCallback, "c@::");
        objc_registerClassPair(cls); _elementClass = cls;
    }
    private static nint Object(nint self, string selector) => Send(self, sel_registerName(selector));
    private static byte Boolean(nint self, string selector) => SendBool(self, sel_registerName(selector));
    private static nint Retain(nint value) => value == 0 ? 0 : Object(value, "retain");
    private static void Release(nint value) { if (value != 0) SendVoid(value, sel_registerName("release")); }
    private static nint String(string value) => SendString(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), value);
    private static void SetObject(nint self, string selector, nint value) => SendObject(self, sel_registerName(selector), value);
    private static void SetString(nint self, string selector, string value) => SetObject(self, selector, String(value));
    private static void SetBool(nint self, string selector, byte value) => SendSetBool(self, sel_registerName(selector), value);
    private static Rect GetRect(nint self, string selector) => Method<RectMethod>(self, selector)(self, sel_registerName(selector));
    private static void SetRect(nint self, string selector, Rect rect) => Method<SetRectMethod>(self, selector)(self, sel_registerName(selector), rect);
    private static T Method<T>(nint self, string selector) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(
        method_getImplementation(class_getInstanceMethod(object_getClass(self), sel_registerName(selector))));
    private static void RequireMainThread() { if (pthread_main_np() == 0) throw new InvalidOperationException("AppKit accessibility publication requires the macOS main thread."); }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate byte BoolMethod(nint self, nint selector);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetBoolMethod(nint self, nint selector, byte value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetObjectMethod(nint self, nint selector, nint value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint ObjectMethod(nint self, nint selector);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate byte SelectorMethod(nint self, nint selector, nint requested);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate Rect RectMethod(nint self, nint selector);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetRectMethod(nint self, nint selector, Rect value);
    [DllImport(ObjC)] private static extern nint objc_getClass(string name);
    [DllImport(ObjC)] private static extern nint object_getClass(nint obj);
    [DllImport(ObjC)] private static extern nint objc_allocateClassPair(nint parent, string name, nuint extraBytes);
    [DllImport(ObjC)] private static extern void objc_registerClassPair(nint cls);
    [DllImport(ObjC)] private static extern byte class_addMethod(nint cls, nint selector, nint imp, string encoding);
    [DllImport(ObjC)] private static extern nint class_getInstanceMethod(nint cls, nint selector);
    [DllImport(ObjC)] private static extern nint method_getImplementation(nint method);
    [DllImport(ObjC)] private static extern nint sel_registerName(string name);
    [DllImport(ObjC)] private static extern nint sel_getName(nint selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint Send(nint obj, nint selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendVoid(nint obj, nint selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern byte SendBool(nint obj, nint selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendSetBool(nint obj, nint selector, byte value);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendObject(nint obj, nint selector, nint value);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint SendString(nint obj, nint selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport("/usr/lib/libSystem.B.dylib")] private static extern int pthread_main_np();
    [DllImport(AppKit)] private static extern void NSAccessibilityPostNotification(nint element, nint notification);
}
