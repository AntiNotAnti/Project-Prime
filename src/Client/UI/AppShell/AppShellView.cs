using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using MphRead.Mods.UI.Components;
using MphRead.Mods.UI.Navigation;
using MphRead.Mods.UI.Screens;
using MphRead.Mods.UI.State;
using MphRead.Mods.UI.Theme;

namespace MphRead.Mods.UI.AppShell;

public enum UiControllerInput
{
    Up,
    Down,
    Left,
    Right,
    Accept,
    Back,
    PreviousTab,
    NextTab
}

/// <summary>Persistent, responsive navigation surface shared by desktop and Android heads.</summary>
public sealed class AppShellView : UserControl
{
    private static readonly UiRoute[] TopRoutes =
        [UiRoute.Home, UiRoute.Play, UiRoute.HunterLicense, UiRoute.Replays, UiRoute.Settings];

    private readonly AppShellState _state;
    private readonly IUiScreenFactory _screenFactory;
    private readonly UiFocusCoordinator _focus = new();
    private readonly UiFocusNavigationPolicy _focusPolicy = new();
    private readonly Grid _body;
    private readonly StackPanel _rail;
    private readonly Grid _bottomBar;
    private readonly ContentControl _content;
    private readonly StatusBadge _status;
    private readonly UiModalHost _modalHost;
    private readonly Dictionary<UiRoute, SecondaryButton> _railButtons = [];
    private readonly Dictionary<UiRoute, SecondaryButton> _bottomButtons = [];

    public AppShellView(AppShellState? state = null, IUiScreenFactory? screenFactory = null)
    {
        _state = state ?? new AppShellState();
        _screenFactory = screenFactory ?? new UiScreenFactory(_state.Router);
        Background = UiColors.InkBrush;
        Focusable = true;

        _rail = new StackPanel
        {
            Width = UiMetrics.NavigationRailWidth,
            Spacing = UiSpacing.Space2,
            Margin = new Thickness(UiSpacing.Space4)
        };
        _bottomBar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*"),
            ColumnSpacing = UiSpacing.Space1,
            Margin = new Thickness(UiSpacing.Space1)
        };
        BuildNavigation();

        _content = new ContentControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };
        _body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Children = { _rail, _content }
        };
        Grid.SetColumn(_content, 1);

        _status = new StatusBadge();
        var footer = new Border
        {
            Background = UiColors.PanelBrush,
            Padding = new Thickness(UiSpacing.Space4, UiSpacing.Space2),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Children =
                {
                    new TextBlock
                    {
                        Text = "PRIME HUNTERS",
                        Foreground = UiColors.TextMutedBrush,
                        FontFamily = UiTypography.Family,
                        FontSize = UiTypography.TextSmall,
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    _status
                }
            }
        };
        Grid.SetColumn(_status, 1);

        var shell = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto,Auto"),
            Children = { _body, _bottomBar, footer }
        };
        Grid.SetRow(_bottomBar, 1);
        Grid.SetRow(footer, 2);

        _modalHost = new UiModalHost();
        _modalHost.Bind(_state.Router);
        var root = new Grid { Children = { shell, _modalHost } };
        Content = root;

        _state.Router.Changed += NavigationChanged;
        _state.PropertyChanged += StateChanged;
        SizeChanged += (_, e) => _state.SetViewport(e.NewSize.Width);
        KeyDown += HandleKeyDown;
        ShowRoute(_state.Router.Current, null);
        ApplyLayout();
        UpdateStatus();
    }

    public AppShellState State => _state;

    /// <summary>Platform controller entry point. Android maps native pad events here while the
    /// shell is visible; match input continues through the gameplay input bridge.</summary>
    public bool HandleControllerInput(UiControllerInput input)
    {
        if (_state.Router.ModalCount > 0)
        {
            return input switch
            {
                UiControllerInput.Up or UiControllerInput.Left or UiControllerInput.PreviousTab
                    => _modalHost.TryAdjustFocusedSelection(-1) || _modalHost.TryMoveFocus(-1),
                UiControllerInput.Down or UiControllerInput.Right or UiControllerInput.NextTab
                    => _modalHost.TryAdjustFocusedSelection(1) || _modalHost.TryMoveFocus(1),
                UiControllerInput.Accept => _modalHost.TryActivateFocused(),
                UiControllerInput.Back => _state.Router.GoBack(),
                _ => false
            };
        }
        return input switch
        {
            UiControllerInput.Up => _focus.TryAdjustSelection(-1)
                || _focus.TryMove(_focusPolicy, UiFocusDirection.Up),
            UiControllerInput.Down => _focus.TryAdjustSelection(1)
                || _focus.TryMove(_focusPolicy, UiFocusDirection.Down),
            UiControllerInput.Left => _focus.TryAdjustSelection(-1)
                || _focus.TryMove(_focusPolicy, UiFocusDirection.Left),
            UiControllerInput.Right => _focus.TryAdjustSelection(1)
                || _focus.TryMove(_focusPolicy, UiFocusDirection.Right),
            UiControllerInput.PreviousTab => CycleTopRoute(-1),
            UiControllerInput.NextTab => CycleTopRoute(1),
            UiControllerInput.Accept => _focus.TryActivate(),
            UiControllerInput.Back => _state.Router.GoBack(),
            _ => false
        };
    }

    private bool CycleTopRoute(int direction)
    {
        int current = Array.IndexOf(TopRoutes, _state.Router.CurrentRoute);
        if (current < 0) return false;
        int next = (current + direction + TopRoutes.Length) % TopRoutes.Length;
        _state.Router.Navigate(TopRoutes[next]);
        return true;
    }

    private void BuildNavigation()
    {
        var focusKeys = new List<string>(TopRoutes.Length);
        var bottomFocusKeys = new List<string>(TopRoutes.Length);
        foreach (UiRoute route in TopRoutes)
        {
            string label = UiRouteInfo.Label(route);
            string railKey = $"nav:rail:{route}";
            var railButton = new SecondaryButton
            {
                Content = label,
                AccessibleName = $"{label} navigation"
            };
            railButton.HorizontalContentAlignment = HorizontalAlignment.Left;
            railButton.Click += (_, _) => _state.Router.Navigate(route, sourceFocusKey: railKey);
            _railButtons.Add(route, railButton);
            _rail.Children.Add(railButton);
            _focus.Register(railKey, railButton);
            focusKeys.Add(railKey);

            string bottomKey = $"nav:bottom:{route}";
            var bottomButton = new SecondaryButton
            {
                Content = UiRouteInfo.CompactLabel(route),
                AccessibleName = $"{label} navigation",
                Padding = new Thickness(UiSpacing.Space1),
                FontSize = UiTypography.TextSmall,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            bottomButton.Click += (_, _) => _state.Router.Navigate(route, sourceFocusKey: bottomKey);
            _bottomButtons.Add(route, bottomButton);
            _bottomBar.Children.Add(bottomButton);
            Grid.SetColumn(bottomButton, bottomFocusKeys.Count);
            _focus.Register(bottomKey, bottomButton);
            bottomFocusKeys.Add(bottomKey);
        }
        _focusPolicy.ConnectVertical(focusKeys, wrap: true);
        _focusPolicy.ConnectHorizontal(bottomFocusKeys, wrap: true);
    }

    private void NavigationChanged(object? sender, UiNavigationChangedEventArgs e)
    {
        if (e.Kind is UiNavigationChangeKind.Navigate or UiNavigationChangeKind.Replace
            or UiNavigationChangeKind.Back)
        {
            ShowRoute(e.State, e.FocusKey);
        }
        else if (e.Kind == UiNavigationChangeKind.ModalClosed)
        {
            _focus.TryFocus(e.FocusKey);
        }
    }

    private void ShowRoute(UiNavigationState navigation, string? requestedFocus)
    {
        Control screen = _screenFactory.GetScreen(navigation.Route);
        _content.Content = screen;
        string? initial = null;
        if (screen is IUiFocusSource focusSource)
        {
            initial = focusSource.InitialFocusKey;
            focusSource.ConfigureFocus(_focusPolicy);
            focusSource.FocusTargetAdded -= RegisterScreenFocus;
            focusSource.FocusTargetAdded += RegisterScreenFocus;
            foreach ((string key, Control control) in focusSource.FocusTargets)
            {
                _focus.Register(key, control);
            }
        }

        foreach ((UiRoute route, SecondaryButton button) in _railButtons)
        {
            button.BorderBrush = route == navigation.Route ? UiColors.AccentBrush : UiColors.EdgeBrush;
        }
        foreach ((UiRoute route, SecondaryButton button) in _bottomButtons)
        {
            button.BorderBrush = route == navigation.Route ? UiColors.AccentBrush : UiColors.EdgeBrush;
        }
        _focus.TryFocus(requestedFocus ?? initial);
    }

    private void RegisterScreenFocus(string key, Control control)
    {
        _focus.Register(key, control);
    }

    private void StateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppShellState.LayoutMode)) ApplyLayout();
        if (e.PropertyName?.StartsWith("Accessibility.", StringComparison.Ordinal) == true)
        {
            ApplyAccessibility();
        }
        if (e.PropertyName?.StartsWith("Session.", StringComparison.Ordinal) == true)
        {
            UpdateStatus();
        }
    }

    private void ApplyLayout()
    {
        bool compact = _state.LayoutMode == UiLayoutMode.Compact;
        _rail.IsVisible = !compact;
        _bottomBar.IsVisible = compact;
        _body.ColumnDefinitions = compact
            ? new ColumnDefinitions("*")
            : new ColumnDefinitions("Auto,*");
        Grid.SetColumn(_content, compact ? 0 : 1);
        ApplyAccessibility();
    }

    private void ApplyAccessibility()
    {
        double safe = _state.Accessibility.SafeArea;
        _content.Margin = new Thickness(safe);
        double scale = _state.Accessibility.UiScale
            * (_state.Accessibility.LargeText ? 1.1 : 1);
        RenderTransform = new Avalonia.Media.ScaleTransform(scale, scale);
        RenderTransformOrigin = RelativePoint.TopLeft;
    }

    private void UpdateStatus()
    {
        UiStatusTone tone = _state.Session.Phase switch
        {
            SessionPhase.Lobby or SessionPhase.Match or SessionPhase.PostMatch
                => UiStatusTone.Good,
            SessionPhase.Connecting or SessionPhase.Reconnecting => UiStatusTone.Warning,
            SessionPhase.Failed => UiStatusTone.Error,
            _ => UiStatusTone.Neutral
        };
        _status.SetStatus(_state.Session.StatusLabel, tone);
    }

    private void HandleKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = _state.Router.GoBack();
            return;
        }
        if (UiFocusNavigationPolicy.TryDirection(e.Key, out UiFocusDirection direction))
        {
            e.Handled = _focus.TryMove(_focusPolicy, direction);
        }
    }
}
