using System;

namespace MphRead.Mods.Launcher.RmlUi.Host;

internal enum RmlUiLinuxImeStatus { Connecting, Available, Unavailable }
internal enum RmlUiLinuxImeReply { Declined, Accepted, Failed }
internal enum RmlUiLinuxImeSignalKind { Preedit, Commit, Hide, Disconnected }
internal readonly record struct RmlUiLinuxImeSignal(long Session, RmlUiLinuxImeSignalKind Kind,
    string Text = "", int Cursor = 0, bool Visible = true)
{
    public override string ToString() => $"Linux input method {Kind}, session {Session}";
}
internal readonly record struct RmlUiLinuxImeRectangle(int X, int Y, int Width, int Height, bool Relative = false);
internal interface IRmlUiLinuxImeApi : IDisposable
{
    RmlUiLinuxImeStatus Status { get; }
    void Focus(long session, bool protectedField, RmlUiLinuxImeRectangle rectangle);
    void Move(long session, RmlUiLinuxImeRectangle rectangle);
    RmlUiLinuxImeReply ProcessKey(long session, uint symbol, uint scanCode, uint modifiers);
    bool TryTake(out RmlUiLinuxImeSignal signal);
}

/// <summary>
/// Optional IBus client. GLFW owns native events; only the render owner applies
/// immutable bus messages to a captured document and text-focus lifetime.
/// </summary>
public sealed class RmlUiLinuxIme : IDisposable
{
    private readonly RmlUiHost _host;
    private readonly Func<bool> _visible;
    private readonly Func<RmlUiTextInputBounds, RmlUiLinuxImeRectangle> _rectangle;
    private readonly IRmlUiLinuxImeApi _api;
    private RmlUiTextInputState? _scope;
    private long _session;
    private bool _composing, _suppressCharacters, _disposed;
    public bool Available => !_disposed && _api.Status == RmlUiLinuxImeStatus.Available;
    public string LastFailure { get; private set; } = "";

    internal RmlUiLinuxIme(RmlUiHost host, Func<bool> visible,
        Func<RmlUiTextInputBounds, RmlUiLinuxImeRectangle> rectangle, IRmlUiLinuxImeApi api)
    {
        (_host, _visible, _rectangle, _api) = (host, visible, rectangle, api);
        host.VerifyOwnerThread();
    }

    internal static RmlUiLinuxIme? TryAttach(RmlUiHost host, Func<bool> visible,
        Func<RmlUiTextInputBounds, RmlUiLinuxImeRectangle> rectangle)
        => OperatingSystem.IsLinux() ? new(host, visible, rectangle, new RmlUiIbusApi()) : null;

    private bool Current() => !_disposed && _visible() && _scope is { } scope
        && _host.TryGetTextInputState(out var current)
        && current.Document == scope.Document && current.FocusEpoch == scope.FocusEpoch;

    public void Pump()
    {
        _host.VerifyOwnerThread();
        if (_disposed) return;
        if (_api.Status == RmlUiLinuxImeStatus.Unavailable)
        {
            Cancel();
            while (_api.TryTake(out _)) { }
            return;
        }
        if (!Current())
        {
            Cancel();
            if (_visible() && _api.Status != RmlUiLinuxImeStatus.Unavailable
                && _host.TryGetTextInputState(out var state)
                && (state.Capabilities & RmlUiTextInputCapabilities.Composition) != 0)
            {
                _scope = state;
                _api.Focus(++_session, (state.Capabilities & RmlUiTextInputCapabilities.Protected) != 0,
                    _rectangle(state.Bounds));
            }
        }
        else if (_scope is { } scope && _host.TryGetTextInputState(out var current))
            _api.Move(_session, _rectangle(current.Bounds));

        while (_api.TryTake(out var signal))
        {
            if (signal.Session != _session || !Current()) continue;
            switch (signal.Kind)
            {
                case RmlUiLinuxImeSignalKind.Preedit:
                    if (!signal.Visible || signal.Text.Length == 0) { CancelComposition(); break; }
                    if (!_composing)
                        _composing = Dispatch(RmlUiPlatformInputKind.CompositionBegin) == RmlUiInputResult.Accepted;
                    if (_composing && Dispatch(RmlUiPlatformInputKind.CompositionUpdate, signal.Text, signal.Cursor)
                        != RmlUiInputResult.Accepted) Cancel();
                    break;
                case RmlUiLinuxImeSignalKind.Commit:
                    if (_composing) Dispatch(RmlUiPlatformInputKind.CompositionCommit, signal.Text);
                    else Dispatch(RmlUiPlatformInputKind.TextCommitted, signal.Text);
                    _composing = false;
                    break;
                case RmlUiLinuxImeSignalKind.Hide: CancelComposition(); break;
                case RmlUiLinuxImeSignalKind.Disconnected:
                    LastFailure = "IBus disconnected; committed Unicode input remains available";
                    Cancel(); break;
            }
        }
    }

    /// <summary>Returns true only when IBus accepted this GLFW key event.</summary>
    public bool ProcessKey(uint symbol, uint scanCode, uint modifiers, bool released = false)
    {
        _host.VerifyOwnerThread();
        if (!released) _suppressCharacters = false;
        Pump();
        if (!Available || !Current() || symbol == 0) return false;
        var reply = _api.ProcessKey(_session, symbol, scanCode, modifiers | (released ? 1u << 30 : 0));
        if (reply == RmlUiLinuxImeReply.Failed)
        {
            LastFailure = "IBus key request failed; committed Unicode input remains available";
            Cancel(); return false;
        }
        bool accepted = reply == RmlUiLinuxImeReply.Accepted;
        if (!released) _suppressCharacters = accepted;
        Pump();
        return accepted;
    }

    // GLFW emits its character callback after the key callback. All characters
    // from an accepted key are owned by IBus, including multi-scalar commits.
    public bool SuppressCharacterCallback()
    {
        _host.VerifyOwnerThread();
        return !_disposed && _suppressCharacters;
    }

    private RmlUiInputResult Dispatch(RmlUiPlatformInputKind kind, string text = "", int cursor = -1)
        => _scope is { } scope ? _host.Input.Dispatch(new(scope.Document, kind,
            RmlUiInputDevice.InputMethod, scope.FocusEpoch, Text: text, Cursor: cursor)) : RmlUiInputResult.StaleFocus;

    private void CancelComposition()
    {
        if (_composing) Dispatch(RmlUiPlatformInputKind.CompositionCancel);
        _composing = false;
    }
    public void Cancel()
    {
        _host.VerifyOwnerThread();
        if (_disposed) return;
        CancelComposition();
        if (_scope.HasValue) { _scope = null; ++_session; _api.Focus(0, false, default); }
        _suppressCharacters = false;
    }
    public void Dispose()
    {
        _host.VerifyOwnerThread();
        if (_disposed) return;
        Cancel(); _disposed = true; _api.Dispose();
    }
}
