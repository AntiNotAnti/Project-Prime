#if MPHREAD_RMLUI_ANDROID
using System;
using System.Collections.Generic;
using System.Linq;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Views.Accessibility;
using MphRead.Mods.Launcher.RmlUi.Components;
using A11yAction = Android.Views.Accessibility.Action;

namespace MphRead.Droid;

/// <summary>Real Android virtual descendants for a native SurfaceView. All
/// getters use the last immutable projection; commands return to its owner.</summary>
internal sealed class AndroidRmlUiAccessibility : AccessibilityNodeProvider
{
    private readonly View _view;
    private readonly Func<RmlUiAccessibilityCommand, bool> _enqueue;
    private RmlUiAccessibilitySnapshot _snapshot = RmlUiAccessibilitySnapshot.Empty;
    private readonly Dictionary<string, int> _ids = new(StringComparer.Ordinal);
    private readonly Dictionary<int, RmlUiAccessibilityNode> _nodes = new();
    private int _nextId = 1, _focused = -1, _hovered = -1;
    internal AndroidRmlUiAccessibility(View view, Func<RmlUiAccessibilityCommand, bool> enqueue)
    {
        _view = view; _enqueue = enqueue;
        view.ImportantForAccessibility = ImportantForAccessibility.Yes;
    }
    internal void Publish(RmlUiAccessibilitySnapshot snapshot)
    {
        if (ReferenceEquals(snapshot, _snapshot)) return;
        bool changed = snapshot.Document != _snapshot.Document || snapshot.Revision != _snapshot.Revision;
        if (snapshot.Document != _snapshot.Document) { _ids.Clear(); _nextId = 1; _focused = _hovered = -1; }
        _snapshot = snapshot; _nodes.Clear();
        foreach (var node in snapshot.Nodes) {
            if (!_ids.TryGetValue(node.Key, out int id)) _ids[node.Key] = id = _nextId++;
            _nodes[id] = node;
        }
        if (_focused >= 0 && !_nodes.ContainsKey(_focused)) _focused = -1;
        if (_hovered >= 0 && !_nodes.ContainsKey(_hovered)) _hovered = -1;
        if (changed) Send(HostViewId, EventTypes.WindowContentChanged);
    }
#if MPHREAD_RMLUI_ANDROID_CHECK
    internal int FindVirtualId(string authoredId) => _nodes.FirstOrDefault(n => n.Value.Id == authoredId).Value == null
        ? -1 : _nodes.First(n => n.Value.Id == authoredId).Key;
    internal string CheckSnapshotSummary() => $"document={_snapshot.Document.DocumentId}, revision={_snapshot.Revision}, nodes={_nodes.Count}, ids=[{String.Join(',', _nodes.Values.Select(n => n.Id).Where(id => id.Length != 0).Take(40))}]";
    internal bool CheckMatchesSnapshot(RmlUiAccessibilitySnapshot snapshot) => ReferenceEquals(snapshot, _snapshot);
#endif
    public override AccessibilityNodeInfo? CreateAccessibilityNodeInfo(int virtualViewId)
    {
        if (virtualViewId == HostViewId) {
            var root = AccessibilityNodeInfo.Obtain(_view);
            root.ClassName = "android.view.View"; root.ContentDescription = "Project Prime";
            foreach (var entry in _nodes) root.AddChild(_view, entry.Key);
            return root;
        }
        if (!_nodes.TryGetValue(virtualViewId, out var node)) return null;
        var info = AccessibilityNodeInfo.Obtain(); info.SetSource(_view, virtualViewId); info.SetParent(_view);
        info.PackageName = _view.Context?.PackageName; info.ClassName = node.Role switch {
            "button" or "link" or "tab" => "android.widget.Button", "textbox" => "android.widget.EditText",
            "checkbox" or "switch" => "android.widget.CheckBox", "radio" => "android.widget.RadioButton",
            "slider" => "android.widget.SeekBar", "combobox" => "android.widget.Spinner",
            "img" => "android.widget.ImageView", "region" => "android.widget.ScrollView", _ => "android.widget.TextView" };
        info.ContentDescription = node.Label;
        // Editable values are deliberately absent from the semantic projection.
        if (node.Role != "textbox") info.Text = node.Label;
        info.Enabled = node.Enabled; info.Password = node.Protected; info.Editable = node.Role == "textbox";
        info.Focusable = (node.Actions & RmlUiAccessibilityActions.Focus) != 0;
        info.Focused = node.Focused; info.AccessibilityFocused = _focused == virtualViewId;
        info.VisibleToUser = !node.Offscreen && _view.IsShown;
        info.Clickable = (node.Actions & RmlUiAccessibilityActions.Press) != 0;
        info.Scrollable = (node.Actions & RmlUiAccessibilityActions.Scroll) != 0;
        if (OperatingSystem.IsAndroidVersionAtLeast(28)) info.Heading = node.Role == "heading";
        float sx = _snapshot.FramebufferWidth > 0 ? (float)_view.Width / _snapshot.FramebufferWidth : 1;
        float sy = _snapshot.FramebufferHeight > 0 ? (float)_view.Height / _snapshot.FramebufferHeight : 1;
        var bounds = new Rect((int)Math.Floor(node.X * sx), (int)Math.Floor(node.Y * sy),
            (int)Math.Ceiling((node.X + node.Width) * sx), (int)Math.Ceiling((node.Y + node.Height) * sy));
        info.SetBoundsInParent(bounds); int[] location = new int[2]; _view.GetLocationOnScreen(location);
        bounds.Offset(location[0], location[1]); info.SetBoundsInScreen(bounds);
        info.AddAction(A11yAction.AccessibilityFocus); info.AddAction(A11yAction.ClearAccessibilityFocus);
        if (info.Focusable) info.AddAction(A11yAction.Focus);
        if (info.Clickable) info.AddAction(A11yAction.Click);
        if (info.Scrollable) { info.AddAction(A11yAction.ScrollForward); info.AddAction(A11yAction.ScrollBackward); }
        if ((node.Actions & RmlUiAccessibilityActions.SetText) != 0) info.AddAction(A11yAction.SetText);
        return info;
    }
    public override AccessibilityNodeInfo? FindFocus(NodeFocus focus)
    {
        if (focus == NodeFocus.Accessibility && _focused >= 0) return CreateAccessibilityNodeInfo(_focused);
        var entry = _nodes.FirstOrDefault(n => n.Value.Focused);
        return entry.Value == null ? null : CreateAccessibilityNodeInfo(entry.Key);
    }
    public override bool PerformAction(int virtualViewId, A11yAction action, Bundle? arguments)
    {
        if (!_nodes.TryGetValue(virtualViewId, out var node)) return false;
        if (action == A11yAction.AccessibilityFocus) {
            if (_focused == virtualViewId) return false;
            int previous = _focused; _focused = virtualViewId;
            if (previous >= 0) Send(previous, EventTypes.ViewAccessibilityFocusCleared);
            Send(virtualViewId, EventTypes.ViewAccessibilityFocused);
            if (!node.Focused && (node.Actions & RmlUiAccessibilityActions.Focus) != 0) Queue(node, RmlUiAccessibilityAction.Focus);
            return true;
        }
        if (action == A11yAction.ClearAccessibilityFocus) {
            if (_focused != virtualViewId) return false; _focused = -1;
            Send(virtualViewId, EventTypes.ViewAccessibilityFocusCleared); return true;
        }
        if (!node.Enabled) return false;
        var native = action switch { A11yAction.Focus => RmlUiAccessibilityAction.Focus,
            A11yAction.Click => RmlUiAccessibilityAction.Press, A11yAction.ScrollForward => RmlUiAccessibilityAction.ScrollForward,
            A11yAction.ScrollBackward => RmlUiAccessibilityAction.ScrollBackward, A11yAction.SetText => RmlUiAccessibilityAction.SetText,
            _ => (RmlUiAccessibilityAction)(-1) };
        if (!Enum.IsDefined(native)) return false;
        var needed = native switch { RmlUiAccessibilityAction.Focus => RmlUiAccessibilityActions.Focus,
            RmlUiAccessibilityAction.Press => RmlUiAccessibilityActions.Press,
            RmlUiAccessibilityAction.SetText => RmlUiAccessibilityActions.SetText, _ => RmlUiAccessibilityActions.Scroll };
        if ((node.Actions & needed) == 0) return false;
        string text = action == A11yAction.SetText ? arguments?.GetCharSequence(AccessibilityNodeInfo.ActionArgumentSetTextCharsequence)?.ToString() ?? "" : "";
        return Queue(node, native, text);
    }
    internal bool Hover(MotionEvent e)
    {
        var manager = _view.Context?.GetSystemService(Context.AccessibilityService) as AccessibilityManager;
        if (manager?.IsEnabled != true || !manager.IsTouchExplorationEnabled) return false;
        int hovered = -1;
        if (e.ActionMasked is MotionEventActions.HoverEnter or MotionEventActions.HoverMove) {
            float x = e.GetX() * _snapshot.FramebufferWidth / Math.Max(1, _view.Width);
            float y = e.GetY() * _snapshot.FramebufferHeight / Math.Max(1, _view.Height);
            // Prefer the smallest visible semantic target at the pointer.
            hovered = _nodes.Where(n => !n.Value.Offscreen && x >= n.Value.X && y >= n.Value.Y
                && x < n.Value.X + n.Value.Width && y < n.Value.Y + n.Value.Height)
                .OrderBy(n => n.Value.Width * n.Value.Height).Select(n => n.Key).DefaultIfEmpty(-1).First();
        }
        if (e.ActionMasked is not (MotionEventActions.HoverEnter or MotionEventActions.HoverMove or MotionEventActions.HoverExit)) return false;
        if (hovered != _hovered) { int old = _hovered; _hovered = hovered;
            if (hovered >= 0) Send(hovered, EventTypes.ViewHoverEnter);
            if (old >= 0) Send(old, EventTypes.ViewHoverExit); }
        return hovered >= 0 || e.ActionMasked == MotionEventActions.HoverExit;
    }
    private bool Queue(RmlUiAccessibilityNode node, RmlUiAccessibilityAction action, string text = "")
        => _enqueue(new(_snapshot.Document, _snapshot.Revision, node.Key, action, text));
    private void Send(int id, EventTypes type)
    {
        var manager = _view.Context?.GetSystemService(Context.AccessibilityService) as AccessibilityManager;
        if (!_view.IsShown || manager?.IsEnabled != true) return;
        var e = AccessibilityEvent.Obtain(type); e.SetSource(_view, id); e.PackageName = _view.Context?.PackageName;
        e.ClassName = "android.view.View";
        if (_nodes.TryGetValue(id, out var node)) { e.ContentDescription = node.Label; e.Enabled = node.Enabled; e.Password = node.Protected; }
        try { _view.Parent?.RequestSendAccessibilityEvent(_view, e); }
        catch (Java.Lang.IllegalStateException) { /* The service may stop between its state check and delivery. */ }
    }
}
#endif
