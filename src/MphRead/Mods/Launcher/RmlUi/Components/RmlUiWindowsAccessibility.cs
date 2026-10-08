#if !ANDROID
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Components;

// Interface order, GUIDs and marshaling follow the Windows SDK UIAutomationCore.idl.
// These narrow definitions avoid pulling WPF or a second UI framework into the game.
[ComVisible(true), Guid("d6dd68d1-86fd-4332-8666-9abedea2d24c"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IRmlUiUiaSimple
{
    int ProviderOptions { get; }
    [return: MarshalAs(UnmanagedType.IUnknown)] object? GetPatternProvider(int patternId);
    [return: MarshalAs(UnmanagedType.Struct)] object? GetPropertyValue(int propertyId);
    IRmlUiUiaSimple? HostRawElementProvider { get; }
}
[ComVisible(true), Guid("f7063da8-8359-439c-9297-bbc5299a7d87"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IRmlUiUiaFragment
{
    IRmlUiUiaFragment? Navigate(int direction);
    [return: MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_I4)] int[]? GetRuntimeId();
    RmlUiUiaRect BoundingRectangle { get; }
    [return: MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_UNKNOWN)] IRmlUiUiaRoot[]? GetEmbeddedFragmentRoots();
    void SetFocus();
    IRmlUiUiaRoot FragmentRoot { get; }
}
[ComVisible(true), Guid("620ce2a5-ab8f-40a9-86cb-de3c75599b58"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IRmlUiUiaRoot
{
    IRmlUiUiaFragment? ElementProviderFromPoint(double x, double y);
    IRmlUiUiaFragment? GetFocus();
}
[ComVisible(true), Guid("54fcb24b-e18e-47a2-b4d3-eccbe77599a2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IRmlUiUiaInvoke { void Invoke(); }
[ComVisible(true), Guid("c7935180-6fb3-4201-b174-7df73adbf64a"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IRmlUiUiaValue
{
    void SetValue([MarshalAs(UnmanagedType.LPWStr)] string value);
    string Value { [return: MarshalAs(UnmanagedType.BStr)] get; }
    bool IsReadOnly { [return: MarshalAs(UnmanagedType.Bool)] get; }
}
[ComVisible(true), Guid("2360c714-4bf1-4b26-ba65-9b21316127eb"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IRmlUiUiaScrollItem { void ScrollIntoView(); }
[StructLayout(LayoutKind.Sequential)]
public readonly record struct RmlUiUiaRect(double Left, double Top, double Width, double Height);

internal interface IRmlUiUiaWindowApi
{
    bool Attach(nint window, RmlUiWindowSubclass callback, nuint id);
    bool Detach(nint window, RmlUiWindowSubclass callback, nuint id);
    nint Forward(nint window, uint message, nuint wParam, nint lParam);
    nint Return(nint window, nuint wParam, nint lParam, IRmlUiUiaSimple? provider);
    IRmlUiUiaSimple? Host(nint window);
    RmlUiUiaRect Bounds(nint window);
    void Changed(IRmlUiUiaSimple provider);
    void Focused(IRmlUiUiaSimple provider);
    void Disconnect(IRmlUiUiaSimple provider);
}

/// <summary>Actual HWND UI Automation fragment. OS COM calls read immutable
/// snapshots and queue guarded commands; they never call the RmlUi host.</summary>
public sealed class RmlUiWindowsAccessibility : IDisposable
{
    private const nuint SubclassId = 0x50504158;
    private static readonly HashSet<RmlUiWindowsAccessibility> Hooks = new();
    private readonly RmlUiAccessibilityService _service;
    private readonly IRmlUiUiaWindowApi _api;
    private readonly RmlUiWindowSubclass _callback;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly RootProvider _root;
    private Graph _graph = Graph.Empty;
    private nint _window;
    private int _disposed;
    public bool Attached => Volatile.Read(ref _window) != 0 && Volatile.Read(ref _disposed) == 0;
    public int ElementCount => Volatile.Read(ref _graph).Children.Length;
    internal IRmlUiUiaSimple Provider => _root;

    public RmlUiWindowsAccessibility(nint window, RmlUiAccessibilityService service)
        : this(window, service, new WindowsUiaWindowApi(), OperatingSystem.IsWindows()) { }
    internal RmlUiWindowsAccessibility(nint window, RmlUiAccessibilityService service, IRmlUiUiaWindowApi api, bool enabled = true)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service)); _api = api; _callback = WindowMessage;
        _root = new(this);
        if (!enabled || window == 0) return;
        try {
            if (api.Attach(window, _callback, SubclassId)) {
                _window = window; lock (Hooks) Hooks.Add(this);
            }
        } catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException) { }
    }
    public void Publish(RmlUiAccessibilitySnapshot snapshot)
    {
        VerifyOwner(); if (!Attached) return;
        var previous = Volatile.Read(ref _graph); var bounds = _api.Bounds(_window);
        if (ReferenceEquals(previous.Snapshot, snapshot) && previous.Bounds == bounds) return;
        var next = new Graph(snapshot, bounds);
        next.Children = snapshot.Nodes.Select((node, index) => new NodeProvider(this, next, node, index)).ToArray();
        Volatile.Write(ref _graph, next);
        // Old COM objects remain safe for clients holding them. Their graph
        // identity makes all later actions fail rather than targeting a reused row.
        _api.Changed(_root);
        var focused = next.Children.FirstOrDefault(p => p.Node.Focused);
        if (focused != null && (previous.Snapshot.Document != snapshot.Document
            || previous.Children.FirstOrDefault(p => p.Node.Focused)?.Node.Key != focused.Node.Key)) _api.Focused(focused);
    }
    public void Dispose()
    {
        VerifyOwner(); if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Volatile.Write(ref _graph, Graph.Empty);
        nint window = _window; if (window == 0) return;
        try { _api.Disconnect(_root); } catch { }
        bool removed = false;
        try { removed = _api.Detach(window, _callback, SubclassId); } catch { }
        if (removed) { _window = 0; lock (Hooks) Hooks.Remove(this); }
        // A failed HWND uninstall deliberately keeps callback + owner rooted;
        // WM_NCDESTROY is the final release and only forwards after retirement.
    }
    private nint WindowMessage(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        try {
            if (message == 0x003d && Volatile.Read(ref _disposed) == 0)
                return _api.Return(window, wParam, lParam, _root); // WM_GETOBJECT, pass both parameters unchanged.
            if (message == 0x0002) _api.Return(window, 0, 0, null); // WM_DESTROY event-map cleanup.
            if (message == 0x0082) {
                Volatile.Write(ref _disposed, 1); Volatile.Write(ref _graph, Graph.Empty);
                try { _api.Detach(window, _callback, id); }
                finally { _window = 0; lock (Hooks) Hooks.Remove(this); }
            }
        } catch { /* Exceptions must never unwind through a Windows procedure. */ }
        try { return _api.Forward(window, message, wParam, lParam); } catch { return 0; }
    }
    private void VerifyOwner() {
        if (_ownerThread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("UI Automation HWND lifetime requires the window owner thread");
    }
    private bool Queue(NodeProvider provider, RmlUiAccessibilityAction action, string text = "")
    {
        if (!Attached || !ReferenceEquals(Volatile.Read(ref _graph), provider.Graph)) return false;
        return _service.Enqueue(new(provider.Graph.Snapshot.Document, provider.Graph.Snapshot.Revision, provider.Node.Key, action, text));
    }
    internal sealed class Graph
    {
        public static Graph Empty { get; } = new(RmlUiAccessibilitySnapshot.Empty, default);
        public readonly RmlUiAccessibilitySnapshot Snapshot;
        public readonly RmlUiUiaRect Bounds;
        public NodeProvider[] Children = Array.Empty<NodeProvider>();
        public Graph(RmlUiAccessibilitySnapshot snapshot, RmlUiUiaRect bounds) { Snapshot = snapshot; Bounds = bounds; }
    }
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class RootProvider : IRmlUiUiaSimple, IRmlUiUiaFragment, IRmlUiUiaRoot
    {
        private readonly RmlUiWindowsAccessibility _owner;
        internal RootProvider(RmlUiWindowsAccessibility owner) => _owner = owner;
        // UIA focuses the nearest HWND before calling Fragment.SetFocus.
        // This provider queues only the RmlUi element's internal focus state.
        public int ProviderOptions => 2 | 32; // ServerSide, UseComThreading.
        public object? GetPatternProvider(int patternId) => null;
        public object? GetPropertyValue(int propertyId) => propertyId switch {
            30003 => 50026, 30005 => "Project Prime", 30012 => "ProjectPrime.RmlUi", 30024 => "RmlUi",
            30016 or 30017 => true, 30022 => false, _ => null };
        public IRmlUiUiaSimple? HostRawElementProvider => _owner._window == 0 ? null : _owner._api.Host(_owner._window);
        public IRmlUiUiaFragment? Navigate(int direction) {
            var children = Volatile.Read(ref _owner._graph).Children;
            return children.Length == 0 ? null : direction == 3 ? children[0] : direction == 4 ? children[^1] : null;
        }
        public int[]? GetRuntimeId() => null; // The HWND host supplies the root runtime ID.
        public RmlUiUiaRect BoundingRectangle => Volatile.Read(ref _owner._graph).Bounds;
        public IRmlUiUiaRoot[]? GetEmbeddedFragmentRoots() => null;
        public void SetFocus() {
            var children = Volatile.Read(ref _owner._graph).Children;
            (children.FirstOrDefault(p => p.Node.Focused) ?? children.FirstOrDefault(p => p.Node.Enabled
                && (p.Node.Actions & RmlUiAccessibilityActions.Focus) != 0))?.SetFocus();
        }
        public IRmlUiUiaRoot FragmentRoot => this;
        public IRmlUiUiaFragment? ElementProviderFromPoint(double x, double y) {
            var graph = Volatile.Read(ref _owner._graph);
            return graph.Children.LastOrDefault(p => !p.Node.Offscreen && Contains(p.BoundingRectangle, x, y))
                ?? (IRmlUiUiaFragment)this;
        }
        public IRmlUiUiaFragment? GetFocus() => Volatile.Read(ref _owner._graph).Children.FirstOrDefault(p => p.Node.Focused);
    }
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class NodeProvider : IRmlUiUiaSimple, IRmlUiUiaFragment, IRmlUiUiaInvoke, IRmlUiUiaValue, IRmlUiUiaScrollItem
    {
        private readonly RmlUiWindowsAccessibility _owner;
        private readonly int _index;
        private readonly Graph _graph;
        internal RmlUiAccessibilityNode Node { get; }
        internal Graph Graph => _graph;
        internal NodeProvider(RmlUiWindowsAccessibility owner, Graph graph, RmlUiAccessibilityNode node, int index) {
            _owner = owner; _graph = graph; Node = node; _index = index;
        }
        private void Live() { if (!_owner.Attached || !ReferenceEquals(Volatile.Read(ref _owner._graph), _graph))
            throw new COMException("UI element is no longer available", unchecked((int)0x80040201)); }
        public int ProviderOptions => 2 | 32;
        public object? GetPatternProvider(int patternId) {
            Live(); return patternId switch {
                10000 when (Node.Actions & RmlUiAccessibilityActions.Press) != 0 => (IRmlUiUiaInvoke)this,
                10002 when Node.Role == "textbox" || (Node.Actions & RmlUiAccessibilityActions.SetText) != 0 => (IRmlUiUiaValue)this,
                10017 when (Node.Actions & RmlUiAccessibilityActions.Focus) != 0 => (IRmlUiUiaScrollItem)this, _ => null };
        }
        public object? GetPropertyValue(int propertyId) {
            Live(); return propertyId switch {
                30003 => ControlType(Node.Role), 30004 => Node.Role, 30005 => Node.Label,
                30008 => Node.Focused, 30009 => (Node.Actions & RmlUiAccessibilityActions.Focus) != 0,
                30010 => Node.Enabled, 30011 => Node.Id.Length > 0 ? Node.Id : Node.Key,
                30012 => "ProjectPrime.RmlUi", 30016 or 30017 => true, 30019 => Node.Protected,
                30020 => 0, 30022 => Node.Offscreen, 30024 => "RmlUi",
                30043 => Node.Role == "textbox" || (Node.Actions & RmlUiAccessibilityActions.SetText) != 0,
                30045 => null, 30046 => (Node.Actions & RmlUiAccessibilityActions.SetText) == 0,
                _ => null };
        }
        public IRmlUiUiaSimple? HostRawElementProvider => null;
        public IRmlUiUiaFragment? Navigate(int direction) {
            Live(); return direction switch { 0 => _owner._root, 1 when _index + 1 < _graph.Children.Length => _graph.Children[_index + 1],
                2 when _index > 0 => _graph.Children[_index - 1], _ => null };
        }
        public int[] GetRuntimeId() {
            Live(); var doc = _graph.Snapshot.Document;
            return new[] { 3, (int)doc.Generation, (int)(doc.Generation >> 32), (int)doc.DocumentId, (int)(doc.DocumentId >> 32),
                (int)_graph.Snapshot.Revision, (int)(_graph.Snapshot.Revision >> 32), _index };
        }
        public RmlUiUiaRect BoundingRectangle { get {
            Live(); double sx = _graph.Snapshot.FramebufferWidth > 0 ? _graph.Bounds.Width / _graph.Snapshot.FramebufferWidth : 1;
            double sy = _graph.Snapshot.FramebufferHeight > 0 ? _graph.Bounds.Height / _graph.Snapshot.FramebufferHeight : 1;
            return new(_graph.Bounds.Left + Node.X * sx, _graph.Bounds.Top + Node.Y * sy, Node.Width * sx, Node.Height * sy);
        } }
        public IRmlUiUiaRoot[]? GetEmbeddedFragmentRoots() => null;
        public void SetFocus() => Action(RmlUiAccessibilityAction.Focus);
        public IRmlUiUiaRoot FragmentRoot => _owner._root;
        public void Invoke() => Action(RmlUiAccessibilityAction.Press);
        public void SetValue(string value) => Action(RmlUiAccessibilityAction.SetText, value);
        public string Value { get { Live(); throw new COMException("Editable values are private", unchecked((int)0x80070005)); } }
        public bool IsReadOnly { get { Live(); return (Node.Actions & RmlUiAccessibilityActions.SetText) == 0; } }
        public void ScrollIntoView() => SetFocus(); // Native focus scrolls the actual ancestors.
        private void Action(RmlUiAccessibilityAction action, string text = "") {
            Live(); if (!_owner.Queue(this, action, text)) throw new COMException("UI action is unavailable", unchecked((int)0x80040200));
        }
    }
    private static bool Contains(RmlUiUiaRect rect, double x, double y) => x >= rect.Left && y >= rect.Top && x < rect.Left + rect.Width && y < rect.Top + rect.Height;
    private static int ControlType(string role) => role switch { "button" => 50000, "checkbox" or "switch" => 50002,
        "combobox" => 50003, "textbox" => 50004, "link" => 50005, "img" => 50006, "listitem" => 50007,
        "list" => 50008, "radio" => 50013, "slider" => 50015, "status" or "alert" => 50017,
        "tablist" => 50018, "tab" => 50019, "text" or "heading" => 50020, "dialog" => 50032, _ => 50026 };
}

internal sealed class WindowsUiaWindowApi : IRmlUiUiaWindowApi
{
    public bool Attach(nint window, RmlUiWindowSubclass callback, nuint id) => GetWindowThreadProcessId(window, 0) == GetCurrentThreadId() && SetWindowSubclass(window, callback, id, 0);
    public bool Detach(nint window, RmlUiWindowSubclass callback, nuint id) => RemoveWindowSubclass(window, callback, id);
    public nint Forward(nint window, uint message, nuint wParam, nint lParam) => DefSubclassProc(window, message, wParam, lParam);
    public nint Return(nint window, nuint wParam, nint lParam, IRmlUiUiaSimple? provider) => UiaReturnRawElementProvider(window, wParam, lParam, provider);
    public IRmlUiUiaSimple? Host(nint window) { UiaHostProviderFromHwnd(window, out var result); return result; }
    public RmlUiUiaRect Bounds(nint window) {
        if (!GetClientRect(window, out var rect)) return default;
        var origin = new Point(); if (!ClientToScreen(window, ref origin)) return default;
        return new(origin.X, origin.Y, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }
    public void Changed(IRmlUiUiaSimple provider) => UiaRaiseStructureChangedEvent(provider, 2, null, 0);
    public void Focused(IRmlUiUiaSimple provider) => UiaRaiseAutomationEvent(provider, 20005);
    public void Disconnect(IRmlUiUiaSimple provider) => UiaDisconnectProvider(provider);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowSubclass(nint window, RmlUiWindowSubclass callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveWindowSubclass(nint window, RmlUiWindowSubclass callback, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, nint process);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetClientRect(nint window, out Rect rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ClientToScreen(nint window, ref Point point);
    [DllImport("UIAutomationCore.dll")] private static extern nint UiaReturnRawElementProvider(nint window, nuint wParam, nint lParam, [MarshalAs(UnmanagedType.Interface)] IRmlUiUiaSimple? provider);
    [DllImport("UIAutomationCore.dll")] private static extern int UiaHostProviderFromHwnd(nint window, out IRmlUiUiaSimple? provider);
    [DllImport("UIAutomationCore.dll")] private static extern int UiaRaiseStructureChangedEvent(IRmlUiUiaSimple provider, int type, int[]? runtimeId, int count);
    [DllImport("UIAutomationCore.dll")] private static extern int UiaRaiseAutomationEvent(IRmlUiUiaSimple provider, int eventId);
    [DllImport("UIAutomationCore.dll")] private static extern int UiaDisconnectProvider(IRmlUiUiaSimple provider);
}
#endif
