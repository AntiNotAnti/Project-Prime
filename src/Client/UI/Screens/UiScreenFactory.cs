using System;
using System.Collections.Generic;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MphRead.Mods.UI.Components;
using MphRead.Mods.UI.Navigation;
using MphRead.Mods.UI.State;
using MphRead.Mods.UI.Theme;
using MphRead.Mods.UI.Screens.Home;
using MphRead.Mods.UI.Screens.HunterLicense;
using MphRead.Mods.UI.Screens.Play;
using MphRead.Mods.UI.Screens.PrivateMatch;
using MphRead.Mods.UI.Screens.Replays;
using MphRead.Mods.UI.Screens.ServerBrowser;
using MphRead.Mods.UI.Screens.Settings;
using MphRead.Mods.UI.Screens.Account;
using MphRead.Mods.UI.Screens.Lobby;
using MphRead.Mods.UI.Screens.PostMatch;

namespace MphRead.Mods.UI.Screens;

public interface IUiScreenFactory
{
    Control GetScreen(UiRoute route);
}

public interface IUiFocusSource
{
    IReadOnlyDictionary<string, Control> FocusTargets { get; }
    string InitialFocusKey { get; }
    event Action<string, Control>? FocusTargetAdded;
    void ConfigureFocus(UiFocusNavigationPolicy policy);
}

/// <summary>Foundation route factory. Feature passes can replace individual screens.</summary>
public sealed class UiScreenFactory : IUiScreenFactory
{
    private readonly UiRouter _router;
    private readonly UiScreenServices _services;
    private readonly Dictionary<UiRoute, Control> _screens = [];

    public UiScreenFactory(UiRouter router, UiScreenServices? services = null)
    {
        _router = router;
        _services = services ?? new UiScreenServices();
    }

    public Control GetScreen(UiRoute route)
    {
        if (!_screens.TryGetValue(route, out Control? screen))
        {
            screen = Create(route);
            _screens.Add(route, screen);
        }
        return screen;
    }

    private Control Create(UiRoute route) => route switch
    {
        UiRoute.Home => new HomeScreenView(_router, _services.Home),
        UiRoute.Play => new PlayScreenView(_router, _services.Play),
        UiRoute.ServerBrowser => new ServerBrowserScreenView(_services.Servers),
        UiRoute.PrivateMatch => new PrivateMatchScreenView(_router, _services.PrivateMatches,
            _services.Maps),
        UiRoute.HunterLicense => new HunterLicenseScreenView(_services.HunterLicense),
        UiRoute.Replays => new ReplayLibraryScreenView(_services.Replays, _services.ReplayConfirmation),
        UiRoute.Settings => new SettingsScreenView(_services.Settings, _services.IsAndroid),
        UiRoute.Lobby => new LobbyScreenView(_services.Lobby),
        UiRoute.PostMatch => new PostMatchScreenView(_router, _services.PostMatch),
        UiRoute.Account => new AccountScreenView(_services.Account),
        _ => new PlaceholderScreen(route, _router)
    };
}

public sealed class PlaceholderScreen : UserControl, IUiFocusSource
{
    private readonly Dictionary<string, Control> _focusTargets = [];

    public PlaceholderScreen(UiRoute route, UiRouter router)
    {
        Route = route;
        string title = UiRouteInfo.Label(route);
        string initialKey = $"screen:{route}:primary";
        InitialFocusKey = initialKey;

        var heading = new TextBlock
        {
            Text = title.ToUpperInvariant(),
            Foreground = UiColors.TextBrush,
            FontFamily = UiTypography.Family,
            FontSize = UiTypography.TextDisplay,
            FontWeight = FontWeight.SemiBold
        };
        AutomationProperties.SetHeadingLevel(heading, 1);
        AutomationProperties.SetName(heading, $"{title} screen");

        var description = new TextBlock
        {
            Text = Description(route),
            Foreground = UiColors.TextMutedBrush,
            FontFamily = UiTypography.Family,
            FontSize = UiTypography.TextBody,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 680
        };

        var actions = new WrapPanel
        {
            Orientation = Orientation.Horizontal
        };
        AddActions(route, router, actions, initialKey);

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Spacing = UiSpacing.Space4,
                Margin = new Avalonia.Thickness(UiSpacing.Space5),
                Children = { heading, description, actions }
            }
        };
        AutomationProperties.SetName(this, title);
    }

    public UiRoute Route { get; }
    public IReadOnlyDictionary<string, Control> FocusTargets => _focusTargets;
    public string InitialFocusKey { get; }
    public event Action<string, Control>? FocusTargetAdded { add { } remove { } }

    public void ConfigureFocus(UiFocusNavigationPolicy policy)
        => policy.ConnectHorizontal(System.Linq.Enumerable.ToArray(_focusTargets.Keys));

    private void AddActions(UiRoute route, UiRouter router, WrapPanel actions, string initialKey)
    {
        UiRoute[] destinations = route switch
        {
            UiRoute.Home => [UiRoute.Play, UiRoute.Account],
            UiRoute.Play => [UiRoute.ServerBrowser, UiRoute.PrivateMatch, UiRoute.Lobby],
            UiRoute.Lobby => [UiRoute.PostMatch],
            _ => []
        };

        if (destinations.Length == 0)
        {
            var back = new SecondaryButton { Content = "Back", AccessibleName = "Go back" };
            back.Margin = new Avalonia.Thickness(0, 0, UiSpacing.Space3, UiSpacing.Space3);
            back.Click += (_, _) => router.GoBack();
            actions.Children.Add(back);
            _focusTargets.Add(initialKey, back);
            return;
        }

        for (int i = 0; i < destinations.Length; i++)
        {
            UiRoute destination = destinations[i];
            string key = i == 0 ? initialKey : $"screen:{route}:action:{i}";
            UiActionButton button = i == 0 ? new PrimaryButton() : new SecondaryButton();
            button.Content = UiRouteInfo.Label(destination);
            button.AccessibleName = $"Open {UiRouteInfo.Label(destination)}";
            button.Margin = new Avalonia.Thickness(0, 0, UiSpacing.Space3, UiSpacing.Space3);
            button.Click += (_, _) => router.Navigate(destination, sourceFocusKey: key);
            actions.Children.Add(button);
            _focusTargets.Add(key, button);
        }
    }

    private static string Description(UiRoute route) => route switch
    {
        UiRoute.Home => "Choose where to go. Account and session status stay available from this shell.",
        UiRoute.Play => "Choose Quick Play, Ranked, a public server, a private match, or Practice.",
        UiRoute.Lobby => "Roster, Hunter, team, ready state, rules, and chat will live here.",
        UiRoute.ServerBrowser => "Browse compatible public sessions with labeled quality and phase status.",
        UiRoute.PrivateMatch => "Configure a server-owned private session before entering its lobby.",
        UiRoute.HunterLicense => "Review profile progress, Hunters, and match history.",
        UiRoute.Replays => "Browse, inspect, and launch saved match replays.",
        UiRoute.Settings => "Adjust controls, presentation, audio, network, and accessibility.",
        UiRoute.PostMatch => "Review match results before returning to the persistent lobby.",
        UiRoute.Account => "Manage the current account session.",
        _ => string.Empty
    };
}
