using System;
using System.Runtime.InteropServices;

namespace MphRead.Mods.Launcher.RmlUi.Host;

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct RmlUiCocoaRange(ulong Location, ulong Length)
{
    public static RmlUiCocoaRange None => new(ulong.MaxValue, 0);
}
internal sealed record RmlUiCocoaCallbacks(Func<string, int, int, bool> Marked, Func<string, bool> Insert,
    Action Unmark, Action NewKey, Func<RmlUiCocoaRange> Selection, Func<RmlUiCocoaRange> MarkedRange, Func<bool> Owns,
    Func<(RmlUiTextInputBounds Bounds, int Width, int Height)?> Candidate, Action Destroyed);
internal interface IRmlUiCocoaImeApi
{
    bool Attach(nint view, RmlUiCocoaCallbacks callbacks);
    bool Detach();
    void DiscardMarkedText();
    void InvalidateCandidatePosition();
}

/// <summary>Owns the existing Cocoa text-input client for one GLFW view, while a native field owns composition.</summary>
public sealed class RmlUiCocoaIme : IDisposable
{
    private readonly RmlUiHost _host;
    private readonly Func<bool> _visible;
    private readonly IRmlUiCocoaImeApi _api;
    private RmlUiTextInputState? _scope;
    private bool _ownsSequence, _discardSequence, _attached;
    private int _markedLength, _markedCursor, _markedSelectionLength;
    private ulong _markedStart;
    public bool Attached => _attached;
    public string LastFailure { get; private set; } = "";
    public static RmlUiCocoaIme? TryAttach(RmlUiHost host, nint view, Func<bool> visible)
    {
        if (!OperatingSystem.IsMacOS() || view == 0) return null;
        var adapter = new RmlUiCocoaIme(host, view, visible, new RmlUiCocoaViewApi());
        if (adapter.Attached) return adapter;
        adapter.Dispose(); return null;
    }
    internal RmlUiCocoaIme(RmlUiHost host, nint view, Func<bool> visible, IRmlUiCocoaImeApi api)
    {
        (_host, _visible, _api) = (host, visible, api);
        host.VerifyOwnerThread();
        try { _attached = api.Attach(view, new(Marked, Insert, Unmark, NewKey, Selection, MarkedRange, () => _ownsSequence, Candidate, Destroyed)); }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        { LastFailure = "Cocoa text input adapter is unavailable"; }
    }
    private bool Current() => _visible() && _host.Active && _scope is { } scope
        && _host.TryGetTextInputState(out var current) && current.Document == scope.Document && current.FocusEpoch == scope.FocusEpoch;
    private bool Marked(string text, int cursorUtf16, int selectionUtf16)
    {
        try
        {
            _host.VerifyOwnerThread();
            if (_ownsSequence && (_discardSequence || !Current())) { CancelCore(false); return true; }
            if (!_ownsSequence)
            {
                if (!_visible() || !_host.TryGetTextInputState(out var state)
                    || (state.Capabilities & RmlUiTextInputCapabilities.Composition) == 0) return false;
                var selected = Selection();
                _markedStart = selected.Location == ulong.MaxValue ? 0 : selected.Location;
                _scope = state; _ownsSequence = true; _discardSequence = false;
                if (Dispatch(RmlUiPlatformInputKind.CompositionBegin) != RmlUiInputResult.Accepted) { CancelCore(false); return true; }
            }
            int cursor = RmlUiWindowsIme.ScalarOffset(text, Math.Clamp(cursorUtf16, 0, text.Length));
            int end = RmlUiWindowsIme.ScalarOffset(text, Math.Clamp(cursorUtf16 + selectionUtf16, 0, text.Length));
            if (Dispatch(RmlUiPlatformInputKind.CompositionUpdate, text, cursor, Math.Max(0, end - cursor)) != RmlUiInputResult.Accepted)
            { CancelCore(false); return true; }
            _markedLength = text.Length; _markedCursor = Math.Clamp(cursorUtf16, 0, text.Length);
            _markedSelectionLength = Math.Clamp(selectionUtf16, 0, text.Length - _markedCursor);
            return true;
        }
        catch { LastFailure = "Cocoa marked-text event rejected"; return _ownsSequence; }
    }
    private bool Insert(string text)
    {
        try
        {
            _host.VerifyOwnerThread();
            if (!_ownsSequence) return false; // GLFW remains the committed Unicode owner for ordinary typing.
            if (!_discardSequence && Current()) Dispatch(RmlUiPlatformInputKind.CompositionCommit, text);
            else CancelCore(false);
            _scope = null; _ownsSequence = _discardSequence = false; _markedLength = 0;
            return true; // Original GLFW insertText would otherwise emit this result a second time.
        }
        catch { LastFailure = "Cocoa composition commit rejected"; return _ownsSequence; }
    }
    private void Unmark()
    {
        try { _host.VerifyOwnerThread(); CancelCore(false); }
        catch { LastFailure = "Cocoa composition cancellation rejected"; }
    }
    private void NewKey()
    {
        try
        {
            _host.VerifyOwnerThread();
            // The OS input context was discarded on cancellation. A new physical
            // key starts a new sequence, while delayed asynchronous results remain swallowed.
            if (_discardSequence) { _scope = null; _ownsSequence = _discardSequence = false; _markedLength = 0; }
        }
        catch { LastFailure = "Cocoa key callback arrived off the owner thread"; }
    }
    private RmlUiCocoaRange Selection()
    {
        try
        {
            _host.VerifyOwnerThread();
            if (_ownsSequence && !_discardSequence) return new(_markedStart + (ulong)_markedCursor, (ulong)_markedSelectionLength);
            if (_visible() && _host.TryGetTextInputState(out var state))
                return _host.TryGetTextSelectionUtf16(state, out int start, out int end)
                    ? new((ulong)start, (ulong)(end-start)) : RmlUiCocoaRange.None;
        }
        catch { LastFailure = "Cocoa selection query rejected"; }
        return RmlUiCocoaRange.None;
    }
    private RmlUiCocoaRange MarkedRange() => _ownsSequence && !_discardSequence && _markedLength > 0
        ? new(_markedStart, (ulong)_markedLength) : RmlUiCocoaRange.None;
    private (RmlUiTextInputBounds Bounds, int Width, int Height)? Candidate()
    {
        try
        {
            _host.VerifyOwnerThread();
            if (_visible() && _host.TryGetTextInputState(out var state) && (state.Capabilities & RmlUiTextInputCapabilities.Bounds) != 0)
                return (state.Bounds, _host.FramebufferWidth, _host.FramebufferHeight);
        }
        catch { LastFailure = "Cocoa candidate geometry query rejected"; }
        return null;
    }
    private RmlUiInputResult Dispatch(RmlUiPlatformInputKind kind, string text = "", int cursor = -1, int length = 0)
        => _scope is { } scope ? _host.Input.Dispatch(new(scope.Document, kind, RmlUiInputDevice.InputMethod,
            scope.FocusEpoch, Text:text, Cursor:cursor, SelectionLength:length)) : RmlUiInputResult.StaleFocus;
    public void RefreshCandidatePosition()
    {
        _host.VerifyOwnerThread();
        if (_ownsSequence && !Current()) Cancel();
        if (_attached && _visible() && _host.TryGetTextInputState(out _))
            _api.InvalidateCandidatePosition();
    }
    public void Cancel() => CancelCore(true);
    private void CancelCore(bool notify)
    {
        _host.VerifyOwnerThread();
        if (_ownsSequence)
        {
            Dispatch(RmlUiPlatformInputKind.CompositionCancel);
            _discardSequence = true; _markedLength = 0;
            if (notify && _attached) _api.DiscardMarkedText();
        }
    }
    private void Destroyed()
    {
        try { CancelCore(false); }
        finally { _attached = false; _scope = null; _ownsSequence = _discardSequence = false; }
    }
    public void Dispose()
    {
        _host.VerifyOwnerThread();
        if (!_attached) return;
        try { Cancel(); } catch { LastFailure = "Cocoa cancellation failed during adapter removal"; }
        if (_api.Detach()) { _attached = false; _scope = null; _ownsSequence = _discardSequence = false; }
        else LastFailure = "Cocoa view class changed; callbacks remain rooted until view destruction";
    }
}
