using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MphRead.Mods.UI.Components;
using MphRead.Mods.UI.Navigation;
using MphRead.Mods.UI.State;
using MphRead.Mods.UI.Theme;

namespace MphRead.Mods.UI.Screens;

public abstract class ScreenViewBase : UserControl, IUiFocusSource
{
    private readonly Dictionary<string, Control> _focusTargets = [];
    private readonly List<string> _focusOrder = [];
    private readonly TextBlock _heading;
    private UiFocusNavigationPolicy? _focusPolicy;
    private CancellationTokenSource _lifetime = new();

    protected ScreenViewBase(string title, string subtitle, string initialFocusKey)
    {
        InitialFocusKey = initialFocusKey;
        _heading = new TextBlock
        {
            Text = title.ToUpperInvariant(),
            Foreground = UiColors.TextBrush,
            FontFamily = UiTypography.Family,
            FontSize = UiTypography.TextDisplay,
            FontWeight = FontWeight.SemiBold
        };
        AutomationProperties.SetHeadingLevel(_heading, 1);
        AutomationProperties.SetName(_heading, $"{title} screen");
        Body = new ContentControl();
        Root = new StackPanel
        {
            Spacing = UiSpacing.Space4,
            Children =
            {
                _heading,
                new TextBlock
                {
                    Text = subtitle,
                    Foreground = UiColors.TextMutedBrush,
                    FontFamily = UiTypography.Family,
                    FontSize = UiTypography.TextBody,
                    TextWrapping = TextWrapping.Wrap
                },
                Body
            }
        };
        Content = new ScrollViewer
        {
            Content = new Border
            {
                MaxWidth = UiMetrics.ContentMaxWidth,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(UiSpacing.Space4),
                Child = Root
            }
        };
        AutomationProperties.SetName(this, title);
        SizeChanged += (_, e) =>
        {
            LayoutMode = UiBreakpoints.FromWidth(e.NewSize.Width);
            _heading.FontSize = LayoutMode == UiLayoutMode.Compact
                ? UiTypography.TextHeading : UiTypography.TextDisplay;
            OnLayoutModeChanged(LayoutMode);
        };
        AttachedToVisualTree += (_, _) =>
        {
            if (_lifetime.IsCancellationRequested)
            {
                _lifetime.Dispose();
                _lifetime = new CancellationTokenSource();
            }
        };
        DetachedFromVisualTree += (_, _) => _lifetime.Cancel();
    }

    protected StackPanel Root { get; }
    protected ContentControl Body { get; }
    protected UiLayoutMode LayoutMode { get; private set; } = UiLayoutMode.Wide;
    protected CancellationToken ScreenCancellation => _lifetime.Token;
    public IReadOnlyDictionary<string, Control> FocusTargets => _focusTargets;
    public string InitialFocusKey { get; }
    public event Action<string, Control>? FocusTargetAdded;

    public void ConfigureFocus(UiFocusNavigationPolicy policy)
    {
        _focusPolicy = policy;
        policy.ConnectVertical(_focusOrder);
    }

    protected T Register<T>(string key, T control) where T : Control
    {
        bool newKey = !_focusTargets.ContainsKey(key);
        _focusTargets[key] = control;
        if (newKey && _focusOrder.Count > 0 && _focusPolicy is not null)
        {
            string previous = _focusOrder[^1];
            _focusPolicy.Connect(previous, UiFocusDirection.Down, key);
            _focusPolicy.Connect(key, UiFocusDirection.Up, previous);
        }
        if (newKey) _focusOrder.Add(key);
        FocusTargetAdded?.Invoke(key, control);
        return control;
    }

    protected virtual void OnLayoutModeChanged(UiLayoutMode mode) { }

    protected static TextBlock Text(string value, bool muted = false, double? size = null)
        => new()
        {
            Text = value,
            Foreground = muted ? UiColors.TextMutedBrush : UiColors.TextBrush,
            FontFamily = UiTypography.Family,
            FontSize = size ?? UiTypography.TextBody,
            TextWrapping = TextWrapping.Wrap
        };
}

public sealed class AsyncStatePresenter : StackPanel
{
    private readonly LoadingIndicator _loading = new();
    private readonly EmptyState _empty = new();
    private readonly ErrorBanner _error = new();
    private readonly SecondaryButton _retry;

    public AsyncStatePresenter()
    {
        Spacing = UiSpacing.Space3;
        _retry = new SecondaryButton { Content = "Retry", AccessibleName = "Retry loading" };
        _retry.Click += (_, _) => Retry?.Invoke(this, EventArgs.Empty);
        Children.Add(_loading);
        Children.Add(_empty);
        Children.Add(_error);
        Children.Add(_retry);
        Show(new AsyncScreenState());
    }

    public event EventHandler? Retry;

    public void Show(AsyncScreenState state)
    {
        IsVisible = state.State != UiLoadState.Ready;
        _loading.IsVisible = state.State == UiLoadState.Loading;
        _empty.IsVisible = state.State is UiLoadState.Empty or UiLoadState.Offline;
        _error.IsVisible = state.State == UiLoadState.Failed;
        _retry.IsVisible = state.CanRetry;
        if (_loading.IsVisible) _loading.Label = state.Message;
        if (_empty.IsVisible)
        {
            string title = state.State == UiLoadState.Offline ? "Offline" : "Nothing here";
            _empty.SetContent(title, state.Message);
        }
        if (_error.IsVisible) _error.Message = state.Message;
    }
}
