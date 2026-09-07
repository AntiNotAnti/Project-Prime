using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MphRead.Mods.UI.Navigation;
using MphRead.Mods.UI.State;
using MphRead.Mods.UI.Theme;

namespace MphRead.Mods.UI.AppShell;

public sealed class AppShellState : INotifyPropertyChanged, IDisposable
{
    private UiLayoutMode _layoutMode = UiLayoutMode.Wide;
    private double _viewportWidth;

    public AppShellState(UiRouter? router = null, AccessibilityPreferences? accessibility = null)
    {
        Router = router ?? new UiRouter();
        Accessibility = accessibility ?? AccessibilityPreferences.Load();
        App = new AppState();
        Play = new PlayState();
        Session = new SessionViewState();
        Router.Changed += RouterChanged;
        Accessibility.PropertyChanged += AccessibilityChanged;
        Session.PropertyChanged += SessionChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public UiRouter Router { get; }
    public AccessibilityPreferences Accessibility { get; }
    public AppState App { get; }
    public PlayState Play { get; }
    public SessionViewState Session { get; }
    public UiRoute CurrentRoute => Router.CurrentRoute;

    public UiLayoutMode LayoutMode
    {
        get => _layoutMode;
        private set => Set(ref _layoutMode, value);
    }

    public double ViewportWidth
    {
        get => _viewportWidth;
        private set => Set(ref _viewportWidth, value);
    }

    public void SetViewport(double width)
    {
        ViewportWidth = width;
        LayoutMode = UiBreakpoints.FromWidth(width);
    }

    public void Dispose()
    {
        Router.Changed -= RouterChanged;
        Accessibility.PropertyChanged -= AccessibilityChanged;
        Session.PropertyChanged -= SessionChanged;
    }

    private void RouterChanged(object? sender, UiNavigationChangedEventArgs e)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentRoute)));

    private void AccessibilityChanged(object? sender, PropertyChangedEventArgs e)
        => PropertyChanged?.Invoke(this,
            new PropertyChangedEventArgs($"Accessibility.{e.PropertyName}"));

    private void SessionChanged(object? sender, PropertyChangedEventArgs e)
        => PropertyChanged?.Invoke(this,
            new PropertyChangedEventArgs($"Session.{e.PropertyName}"));

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
