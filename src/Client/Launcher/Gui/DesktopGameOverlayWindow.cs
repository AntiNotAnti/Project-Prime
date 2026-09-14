using System;
using Avalonia;
using Avalonia.Controls;

namespace MphRead.Mods.Launcher.Gui;

internal enum DesktopOverlayMode
{
    None,
    Pause,
    Settings,
    Results,
    ContinuationLoading
}

internal interface IDesktopGameOverlaySurface : IDisposable
{
    DesktopOverlayMode Mode { get; }
    bool NativeVisible { get; }
    event EventHandler? UserCloseRequested;
    event EventHandler? Activated;
    event EventHandler? Deactivated;
    void SetContent(Control? content, DesktopOverlayMode mode);
    void ShowForHost(GameHostPresentationState state, bool activate);
    void HideForHostPreservingContent(GameHostPresentationState state);
    void SetZOrderOwned(bool owned);
    void ReleaseContent();
}

/// <summary>
/// One Avalonia window reused for every desktop in-game presentation.  A
/// minimized host is represented by native minimization/visibility only; the
/// content remains attached so a live SettingsView does not roll its draft
/// back through its visual-tree detach hook.
/// </summary>
internal sealed class DesktopGameOverlayWindow : Window, IDesktopGameOverlaySurface
{
    private bool _disposed;
    private bool _ownerClosing;
    private bool _nativeVisible;
    private DesktopOverlayMode _mode;

    public DesktopOverlayMode Mode => _mode;
    public bool NativeVisible => _nativeVisible;
    internal GameHostPresentationState LastHostState { get; private set; }
    public event EventHandler? UserCloseRequested;

    public DesktopGameOverlayWindow()
    {
        Title = Mods.Branding.Name + " — overlay";
        Icon = GuiTheme.AppIcon.Value;
        SystemDecorations = SystemDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        // Mapping a background-restored overlay must not steal focus. An
        // intentional activation is performed explicitly by ShowForHost.
        ShowActivated = false;
        // Topmost is activation-owned. A deactivated overlay must not float
        // above an unrelated foreground application.
        Topmost = false;
        Background = GuiTheme.ScrimBrush;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent,
            WindowTransparencyLevel.None];
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        Closing += OverlayClosing;
        Closed += OverlayClosed;
    }

    public void SetContent(Control? content, DesktopOverlayMode mode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (mode == DesktopOverlayMode.None && content != null)
            throw new ArgumentException("A content control requires an overlay mode.",
                nameof(mode));
        _mode = mode;
        Content = content;
    }

    public void ShowForHost(GameHostPresentationState state, bool activate)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        LastHostState = state;
        if (_mode == DesktopOverlayMode.None) return;
        ApplyGeometry(state);
        if (state.IsMinimized)
        {
            // WindowState keeps Content attached. Calling Hide here would
            // detach SettingsView and intentionally restore its draft.
            if (IsVisible) WindowState = WindowState.Minimized;
            SetZOrderOwned(false);
            _nativeVisible = false;
            return;
        }
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        _nativeVisible = true;
        if (activate)
        {
            // The SDL host can lose focus as part of the intentional
            // SDL-to-overlay handoff. The overlay is the requested target, so
            // its activation is not gated by the source host's focus bit. Do
            // not promote it before the native activation callback: Windows
            // may deny this foreground request, and a denied request must not
            // leave a background overlay globally topmost.
            Activate();
        }
    }

    public void HideForHostPreservingContent(GameHostPresentationState state)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        LastHostState = state;
        // See ShowForHost: native minimization is the only hide operation that
        // preserves the exact visual-tree lifetime of an active settings view.
        if (IsVisible) WindowState = WindowState.Minimized;
        SetZOrderOwned(false);
        _nativeVisible = false;
    }

    public void SetZOrderOwned(bool owned)
    {
        if (_disposed) return;
        Topmost = owned && _mode != DesktopOverlayMode.None && _nativeVisible;
    }

    public void ReleaseContent()
    {
        if (_disposed) return;
        Content = null;
        _mode = DesktopOverlayMode.None;
        _nativeVisible = false;
        Topmost = false;
        if (IsVisible)
        {
            // Hide the persistent native window between scene-bound sessions;
            // closing it would make the one-overlay coordinator impossible to
            // reuse for the next pause/results visit.
            Hide();
        }
    }

    private void ApplyGeometry(GameHostPresentationState state)
    {
        if (state.LogicalClientSizeUnits.X <= 0 || state.LogicalClientSizeUnits.Y <= 0)
            return;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new PixelPoint(state.WindowPositionScreenUnits.X,
            state.WindowPositionScreenUnits.Y);
        // SDL logical client units are deliberately used directly for the
        // Avalonia contract. FramebufferSizePixels is retained separately for
        // render/back-end consumers; no display RenderScaling guess belongs in
        // this presentation boundary.
        Width = state.LogicalClientSizeUnits.X;
        Height = state.LogicalClientSizeUnits.Y;
    }

    private void OverlayClosed(object? sender, EventArgs args)
    {
        _nativeVisible = false;
    }

    private void OverlayClosing(object? sender, WindowClosingEventArgs args)
    {
        if (_ownerClosing || _disposed) return;
        // Keep the persistent Window reusable. Treat the native close button
        // as an overlay command and let the coordinator dispose the current
        // scene-bound session explicitly.
        args.Cancel = true;
        UserCloseRequested?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnClosed(EventArgs e)
    {
        _nativeVisible = false;
        base.OnClosed(e);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ownerClosing = true;
        Content = null;
        _mode = DesktopOverlayMode.None;
        _nativeVisible = false;
        Topmost = false;
        if (IsVisible) Close();
        UserCloseRequested = null;
    }
}
