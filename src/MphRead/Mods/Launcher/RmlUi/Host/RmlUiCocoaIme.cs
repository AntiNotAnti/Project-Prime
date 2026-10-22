using System;
using System.Runtime.InteropServices;

namespace MphRead.Mods.Launcher.RmlUi.Host;

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct RmlUiCocoaRange(ulong Location, ulong Length)
{
    // AppKit NSNotFound is NSIntegerMax, even though NSRange.location is
    // unsigned. NSUIntegerMax is a malformed range, not the no-range marker.
    public static RmlUiCocoaRange None => new(0x7fffffffffffffffUL, 0);
}
internal readonly record struct RmlUiCocoaCandidate(RmlUiTextInputBounds Bounds, int Width, int Height, RmlUiCocoaRange ActualRange, bool ExactCaret);
internal sealed record RmlUiCocoaCallbacks(Func<string, int, int, bool> Marked, Func<string, bool> Insert,
    Action Unmark, Action NewKey, Func<RmlUiCocoaRange> Selection, Func<RmlUiCocoaRange> MarkedRange, Func<bool> Owns,
    Func<(RmlUiTextInputBounds Bounds, int Width, int Height)?> Candidate, Action Destroyed)
{
    internal Func<string, RmlUiCocoaRange, RmlUiCocoaRange, bool>? MarkedWithReplacement { get; init; }
    internal Func<string, RmlUiCocoaRange, bool>? InsertWithReplacement { get; init; }
    internal Func<RmlUiCocoaRange, RmlUiCocoaCandidate?>? CandidateForRange { get; init; }
}
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
    private RmlUiTextEditState? _editScope;
    private bool _inEdit;
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
        try { _attached = api.Attach(view, new(Marked, Insert, Unmark, NewKey, Selection, MarkedRange, () => _ownsSequence, Candidate, Destroyed)
            { MarkedWithReplacement = MarkedWithReplacement, InsertWithReplacement = InsertWithReplacement, CandidateForRange = CandidateForRange }); }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        { LastFailure = "Cocoa text input adapter is unavailable"; }
    }
    private bool Current() => _visible() && _host.Active && _scope is { } scope
        && _host.TryGetTextInputState(out var current) && current.Document == scope.Document && current.FocusEpoch == scope.FocusEpoch;
    private static bool Range(RmlUiCocoaRange range, bool unspecified, out int start, out int length)
    {
        start = -1; length = 0;
        if (unspecified && range == RmlUiCocoaRange.None) return true;
        if (range.Location > 65536 || range.Length > 65536 || range.Length > 65536 - range.Location) return false;
        start = (int)range.Location; length = (int)range.Length; return true;
    }
    private bool OwnedRevision(out RmlUiTextEditState state)
    {
        state = default;
        return Current() && _editScope is { } scope && _host.TryGetTextEditState(out state)
            && state.Document == scope.Document && state.FocusEpoch == scope.FocusEpoch && state.Revision == scope.Revision;
    }
    private bool RejectMarkedSequence(string failure)
    {
        LastFailure = failure;
        if (_ownsSequence) { CancelCore(false); return true; }
        if (_visible() && _host.TryGetTextInputState(out var native)) {
            _scope = native; _editScope = null; _ownsSequence = _discardSequence = true;
            return true; // original Cocoa marked cache may still produce a later insert
        }
        return false;
    }
    private bool MarkedWithReplacement(string text, RmlUiCocoaRange selected, RmlUiCocoaRange replacement)
    {
        _host.VerifyOwnerThread();
        if (_inEdit) return _ownsSequence;
        if (!Range(selected, false, out int selectedStart, out int selectedLength)
            || !Range(replacement, true, out int start, out int length)) {
            return RejectMarkedSequence("Cocoa text range is invalid");
        }
        if (_ownsSequence && _editScope is null) {
            if (replacement != RmlUiCocoaRange.None || !Range(selected, false, out int oldStart, out int oldLength))
            { CancelCore(false); LastFailure = "Cocoa replacement range is unsupported by this native bridge"; return true; }
            return Marked(text, oldStart, oldLength);
        }
        if (_ownsSequence && (_discardSequence || !OwnedRevision(out _))) { CancelCore(false); return true; }
        if (!_ownsSequence) {
            if (!_visible() || !_host.TryGetTextEditState(out var state)) {
                if (replacement == RmlUiCocoaRange.None && Range(selected, false, out int oldStart, out int oldLength))
                    return Marked(text, oldStart, oldLength);
                return RejectMarkedSequence("Cocoa replacement range is unsupported by this native bridge");
            }
            if (!_host.TryGetTextInputState(out var legacy) || legacy.Document != state.Document || legacy.FocusEpoch != state.FocusEpoch) return false;
            _editScope = state; _scope = legacy; _ownsSequence = true; _discardSequence = false;
        }
        _inEdit = true;
        try {
            var result = _host.TextEdit(_editScope!.Value, _host.NextTextEditSequence(), RmlUiTextEditKind.Marked,
                text, start, length, selectedStart, selectedLength);
            if (result != RmlUiTextEditResult.Accepted || !_host.TryGetTextEditState(out var updated)
                || updated.Document != _editScope.Value.Document || updated.FocusEpoch != _editScope.Value.FocusEpoch || updated.MarkedStart < 0)
            { CancelCore(false); LastFailure = "Cocoa marked-text edit rejected"; return true; }
            _editScope = updated; _markedStart = (ulong)updated.MarkedStart; _markedLength = updated.MarkedLength;
            _markedCursor = updated.SelectionStart - updated.MarkedStart; _markedSelectionLength = updated.SelectionLength;
            return true;
        } finally { _inEdit = false; }
    }
    private bool InsertWithReplacement(string text, RmlUiCocoaRange replacement)
    {
        _host.VerifyOwnerThread();
        if (_inEdit) return _ownsSequence;
        if (_ownsSequence && _editScope is null) {
            if (replacement != RmlUiCocoaRange.None) { CancelCore(false); return true; }
            return Insert(text);
        }
        if (!_ownsSequence && replacement == RmlUiCocoaRange.None) return false; // normal GLFW character owner
        if (_ownsSequence && (_discardSequence || !OwnedRevision(out _))) { CancelCore(false); return true; }
        if (!_visible() || (!_ownsSequence && !_host.TryGetTextInputState(out _))) return false;
        if (!Range(replacement, true, out int start, out int length)) { LastFailure = "Cocoa replacement range is invalid"; return true; }
        if (!_ownsSequence && !_host.TryGetTextEditState(out _)) { LastFailure = "Cocoa replacement range is unsupported by this native bridge"; return true; }
        RmlUiTextEditState current = _editScope ?? (_host.TryGetTextEditState(out var queried) ? queried : default);
        _inEdit = true;
        try {
            var result = _host.TextEdit(current, _host.NextTextEditSequence(), _ownsSequence ? RmlUiTextEditKind.Commit : RmlUiTextEditKind.Replace,
                text, start, length);
            if (result != RmlUiTextEditResult.Accepted) LastFailure = "Cocoa committed-text edit rejected";
            _scope = null; _editScope = null; _ownsSequence = _discardSequence = false; _markedLength = 0;
            return true; // suppress original GLFW insertion exactly for this callback, no text/time dedup
        } finally { _inEdit = false; }
    }
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
                _markedStart = selected.Location == RmlUiCocoaRange.None.Location ? 0 : selected.Location;
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
        try {
            _host.VerifyOwnerThread();
            // discardMarkedText may synchronously call unmarkText. Keep the
            // canceled sequence fenced until its real new-key boundary; it must
            // not release a delayed insert into GLFW or a replacement focus.
            if (_discardSequence) return;
            if (_editScope is { } edit) {
                if (!OwnedRevision(out _)) { CancelCore(false); return; }
                var result = _host.TextEdit(edit, _host.NextTextEditSequence(), RmlUiTextEditKind.Commit, commitCurrent: true);
                if (result != RmlUiTextEditResult.Accepted) { LastFailure = "Cocoa unmark rejected by current text owner"; return; }
                _scope = null; _editScope = null; _ownsSequence = _discardSequence = false; _markedLength = 0;
                return;
            }
            CancelCore(false); // optional legacy ABI keeps its existing behavior
        }
        catch { LastFailure = "Cocoa composition cancellation rejected"; }
    }
    private void NewKey()
    {
        try
        {
            _host.VerifyOwnerThread();
            // The OS input context was discarded on cancellation. A new physical
            // key starts a new sequence, while delayed asynchronous results remain swallowed.
            if (_discardSequence) { _scope = null; _editScope = null; _ownsSequence = _discardSequence = false; _markedLength = 0; }
        }
        catch { LastFailure = "Cocoa key callback arrived off the owner thread"; }
    }
    private RmlUiCocoaRange Selection()
    {
        try
        {
            _host.VerifyOwnerThread();
            if (_editScope is not null) return OwnedRevision(out var edit) ? new((ulong)edit.SelectionStart, (ulong)edit.SelectionLength) : RmlUiCocoaRange.None;
            if (_ownsSequence && !_discardSequence) return new(_markedStart + (ulong)_markedCursor, (ulong)_markedSelectionLength);
            if (_visible() && _host.TryGetTextInputState(out var state))
                return _host.TryGetTextSelectionUtf16(state, out int start, out int end)
                    ? new((ulong)start, (ulong)(end-start)) : RmlUiCocoaRange.None;
        }
        catch { LastFailure = "Cocoa selection query rejected"; }
        return RmlUiCocoaRange.None;
    }
    private RmlUiCocoaRange MarkedRange()
    {
        _host.VerifyOwnerThread();
        if (_editScope is not null) return OwnedRevision(out var state) && state.MarkedStart >= 0
            ? new((ulong)state.MarkedStart, (ulong)state.MarkedLength) : RmlUiCocoaRange.None;
        return _ownsSequence && !_discardSequence && _markedLength > 0 ? new(_markedStart, (ulong)_markedLength) : RmlUiCocoaRange.None;
    }
    private RmlUiCocoaCandidate? CandidateForRange(RmlUiCocoaRange requested)
    {
        _host.VerifyOwnerThread();
        if (!_visible()) return null;
        if (_host.TryGetTextEditState(out var edit)) {
            if ((edit.Flags & 2) != 0 && edit.SelectionLength == 0 && requested.Location == (ulong)edit.SelectionStart && requested.Length == 0)
                return new(edit.Bounds, _host.FramebufferWidth, _host.FramebufferHeight, new((ulong)edit.SelectionStart, 0), true);
            if (_host.TryGetTextInputState(out var field) && field.Document == edit.Document && field.FocusEpoch == edit.FocusEpoch
                && (field.Capabilities & RmlUiTextInputCapabilities.Bounds) != 0)
                return new(field.Bounds, _host.FramebufferWidth, _host.FramebufferHeight, new(0, (ulong)edit.Length), false);
            return null;
        }
        return Candidate() is { } legacy ? new(legacy.Bounds, legacy.Width, legacy.Height, requested, false) : null;
    }
    private (RmlUiTextInputBounds Bounds, int Width, int Height)? Candidate()
    {
        try
        {
            _host.VerifyOwnerThread();
            if (_visible() && _host.TryGetTextEditState(out var edit) && (edit.Flags & 6) != 0)
                return (edit.Bounds, _host.FramebufferWidth, _host.FramebufferHeight);
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
        if (_ownsSequence && (!Current() || _editScope is not null && !OwnedRevision(out _))) Cancel();
        if (_attached && _visible() && _host.TryGetTextInputState(out _))
            _api.InvalidateCandidatePosition();
    }
    public void Cancel() => CancelCore(true);
    private void CancelCore(bool notify)
    {
        _host.VerifyOwnerThread();
        if (_ownsSequence)
        {
            if (_editScope is { } edit) _host.TextEdit(edit, _host.NextTextEditSequence(), RmlUiTextEditKind.Cancel);
            else Dispatch(RmlUiPlatformInputKind.CompositionCancel);
            _discardSequence = true; _markedLength = 0;
            if (notify && _attached) _api.DiscardMarkedText();
        }
    }
    private void Destroyed()
    {
        try { CancelCore(false); }
        finally { _attached = false; _scope = null; _editScope = null; _ownsSequence = _discardSequence = false; }
    }
    public void Dispose()
    {
        _host.VerifyOwnerThread();
        if (!_attached) return;
        try { Cancel(); } catch { LastFailure = "Cocoa cancellation failed during adapter removal"; }
        if (_api.Detach()) { _attached = false; _scope = null; _editScope = null; _ownsSequence = _discardSequence = false; }
        else LastFailure = "Cocoa view class changed; callbacks remain rooted until view destruction";
    }
}
