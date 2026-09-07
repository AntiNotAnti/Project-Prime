using System;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using MphRead.Mods.Network;
using MphRead.Mods.UI.Components;
using MphRead.Mods.UI.Navigation;
using MphRead.Mods.UI.State;
using MphRead.Mods.UI.Theme;

namespace MphRead.Mods.UI.Screens.ServerBrowser;

public sealed class ServerBrowserScreenView : ScreenViewBase
{
    private readonly ServerBrowserScreenModel _model;
    private readonly IServerBrowserController _controller;
    private readonly Grid _layout;
    private readonly StackPanel _listColumn;
    private readonly StackPanel _list = new() { Spacing = UiSpacing.Space2 };
    private readonly StackPanel _details = new() { Spacing = UiSpacing.Space3 };
    private readonly AsyncStatePresenter _state = new();
    private readonly TextBox _search;
    private readonly CheckBox _hideFull;
    private readonly CheckBox _hideIncompatible;
    private readonly ComboBox _mode;
    private readonly ComboBox _group;
    private readonly NumericUpDown _maxPing;
    private readonly ComboBox _sort;
    private bool _updatingFilters;
    private int _dynamicKey;

    public ServerBrowserScreenView(IServerBrowserController controller)
        : base("Server Browser", "Find a compatible server, inspect its rules, then join or spectate.",
            "browser:refresh")
    {
        _controller = controller;
        _model = new ServerBrowserScreenModel(controller);
        _search = new TextBox { Watermark = "Search server, map, or endpoint", MinWidth = 220 };
        Register("browser:search", _search);
        AutomationProperties.SetName(_search, "Search servers");
        _search.TextChanged += (_, _) => ApplyFilters();
        _hideFull = new CheckBox { Content = "Hide full" };
        Register("browser:hide-full", _hideFull);
        _hideFull.IsCheckedChanged += (_, _) => ApplyFilters();
        _hideIncompatible = new CheckBox { Content = "Hide incompatible", IsChecked = true };
        Register("browser:hide-incompatible", _hideIncompatible);
        _hideIncompatible.IsCheckedChanged += (_, _) => ApplyFilters();
        _mode = new ComboBox { MinWidth = 130, ItemsSource = new[] { "All modes" }, SelectedIndex = 0 };
        Register("browser:mode", _mode);
        AutomationProperties.SetName(_mode, "Game mode");
        _mode.SelectionChanged += (_, _) => ApplyFilters();
        _group = new ComboBox
        {
            MinWidth = 120,
            ItemsSource = Enum.GetValues<UiServerGroup>(),
            SelectedItem = UiServerGroup.All
        };
        Register("browser:group", _group);
        AutomationProperties.SetName(_group, "Server group");
        _group.SelectionChanged += (_, _) => ApplyFilters();
        _maxPing = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 999,
            Increment = 10,
            Value = 0,
            Width = 110
        };
        Register("browser:max-ping", _maxPing);
        AutomationProperties.SetName(_maxPing, "Maximum ping, zero for any");
        _maxPing.ValueChanged += (_, _) => ApplyFilters();
        _sort = new ComboBox
        {
            MinWidth = 120,
            ItemsSource = Enum.GetValues<UiServerSort>(),
            SelectedItem = UiServerSort.Ping
        };
        Register("browser:sort", _sort);
        AutomationProperties.SetName(_sort, "Sort servers");
        _sort.SelectionChanged += (_, _) => ApplyFilters();

        var refresh = Register("browser:refresh", new SecondaryButton
        {
            Content = "Refresh", AccessibleName = "Refresh server list"
        });
        refresh.Click += async (_, _) => await RefreshAsync();
        var filters = new WrapPanel
        {
            Children = { _search, _mode, _group, _maxPing, _sort, _hideFull, _hideIncompatible, refresh }
        };
        foreach (Control child in filters.Children)
            child.Margin = new Thickness(0, 0, UiSpacing.Space3, UiSpacing.Space2);

        _layout = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = UiSpacing.Space4 };
        _listColumn = new StackPanel { Spacing = UiSpacing.Space3, Children = { _state, _list } };
        _layout.Children.Add(_listColumn);
        _layout.Children.Add(_details);
        Grid.SetColumn(_details, 1);
        Body.Content = new StackPanel { Spacing = UiSpacing.Space3, Children = { filters, _layout } };

        _state.Retry += async (_, _) => await RefreshAsync();
        AttachedToVisualTree += async (_, _) =>
        {
            if (_model.Status.State is UiLoadState.Idle or UiLoadState.Loading) await RefreshAsync();
        };
        DetachedFromVisualTree += (_, _) => _model.Dispose();
    }

    protected override void OnLayoutModeChanged(UiLayoutMode mode)
    {
        bool compact = UiScreenLayout.BrowserColumns(mode) == 1;
        _layout.ColumnDefinitions = compact ? new ColumnDefinitions("*") : new ColumnDefinitions("*,*");
        _layout.RowDefinitions = compact ? new RowDefinitions("Auto,Auto") : new RowDefinitions("Auto");
        Grid.SetColumn(_details, compact ? 0 : 1);
        Grid.SetColumn(_listColumn, 0);
        Grid.SetRow(_details, 0);
        Grid.SetRow(_listColumn, compact ? 1 : 0);
        _details.Margin = compact ? new Thickness(0, 0, 0, UiSpacing.Space4) : default;
    }

    private async System.Threading.Tasks.Task RefreshAsync()
    {
        await _model.RefreshAsync(ScreenCancellation);
        RefreshModeChoices();
        _state.Show(_model.Status);
        RebuildList();
    }

    private void ApplyFilters()
    {
        if (_updatingFilters) return;
        string? mode = _mode.SelectedItem as string;
        if (mode == "All modes") mode = null;
        _model.ApplyFilter(_model.Filter with
        {
            Search = _search.Text ?? string.Empty,
            Mode = mode,
            HideFull = _hideFull.IsChecked == true,
            HideIncompatible = _hideIncompatible.IsChecked == true,
            MaxPing = decimal.ToInt32(_maxPing.Value ?? 0),
            Sort = _sort.SelectedItem is UiServerSort sort ? sort : UiServerSort.Ping,
            Group = _group.SelectedItem is UiServerGroup group ? group : UiServerGroup.All
        });
        _state.Show(_model.Status);
        RebuildList();
    }

    private void RebuildList()
    {
        _list.Children.Clear();
        _dynamicKey = 0;
        foreach (UiServerEntry server in _model.Visible.Take(200))
        {
            var card = Register($"browser:server:{_dynamicKey++}", new ServerCard
            {
                Title = server.Name,
                Detail = $"{server.Map} · {server.Mode} · {server.Players}/{server.MaxPlayers} · "
                    + $"{server.Ping} ms · {server.Phase} · {server.JoinLabel}"
            });
            card.IsEnabled = server.Compatible;
            card.Click += (_, _) => { _model.Select(server); ShowDetails(server); };
            _list.Children.Add(card);
        }
        if (_model.Selected is { } selected) ShowDetails(selected);
        else _details.Children.Clear();
        if (_model.Visible.Count == 0 && _model.Status.State == UiLoadState.Ready)
        {
            _model.Status.Empty("No servers match these filters.");
            _state.Show(_model.Status);
        }
    }

    private void ShowDetails(UiServerEntry server)
    {
        _details.Children.Clear();
        _details.Children.Add(Text(server.Name, size: UiTypography.TextHeading));
        _details.Children.Add(Text(
            $"{server.Map} · {server.Mode}\n{server.Players} players, {server.Bots} bots, "
            + $"{server.Spectators} spectators\n{server.Phase} · "
            + (server.TimeRemaining is { } time ? $"{time:mm\\:ss} remaining" : "No time limit")
            + $"\n{server.Ruleset} · {(server.Verified ? "Verified" : "Unverified")}"
            + (server.Ranked ? " · Ranked" : "")
            + $"\nFriendly fire: {(server.FriendlyFire ? "On" : "Off")} · Radar: "
            + $"{(server.Radar ? "On" : "Off")} · Spawn: {server.SpawnPolicy} · Late join: "
            + (server.LateJoin ? "On" : "Off")
            + (server.HasSessionState
                ? $"\nLobby: {server.LobbyPlayers} players, {server.LobbyObservers} observers, "
                    + $"{server.ReadyPlayers} ready · Ranked lock: {(server.RankedLocked ? "On" : "Off")}"
                    + $" · Tournament lock: {(server.TournamentLocked ? "On" : "Off")}"
                : "")));

        bool dispositionSpectates = server.HasSessionState && server.JoinDisposition is
            ServerJoinDisposition.Spectate or ServerJoinDisposition.WaitForNextMatch;
        AddDetailAction(server.JoinLabel, dispositionSpectates, server,
            server.Compatible && (dispositionSpectates || server.CanJoin), "browser:join");
        if (!dispositionSpectates)
        {
            AddDetailAction("Spectate", true, server, server.Compatible
                && (!server.HasSessionState || server.JoinDisposition is not ServerJoinDisposition.Full
                    and not ServerJoinDisposition.Closed), "browser:spectate");
        }
        var favorite = Register("browser:favorite", new SecondaryButton
        {
            Content = server.Favorite ? "Remove Favorite" : "Favorite",
            AccessibleName = server.Favorite ? $"Remove {server.Name} from favorites" : $"Favorite {server.Name}"
        });
        favorite.Click += (_, _) =>
        {
            _model.ToggleFavorite();
            RebuildList();
        };
        _details.Children.Add(favorite);
        var copy = Register("browser:copy", new SecondaryButton
        {
            Content = "Copy Endpoint",
            AccessibleName = $"Copy endpoint for {server.Name}",
            IsEnabled = !string.IsNullOrWhiteSpace(server.Endpoint)
        });
        copy.Click += (_, _) => _controller.CopyEndpoint(server);
        _details.Children.Add(copy);
    }

    private void AddDetailAction(string label, bool spectate, UiServerEntry server, bool enabled,
        string focusKey)
    {
        UiActionButton created = focusKey == "browser:join" ? new PrimaryButton() : new SecondaryButton();
        UiActionButton button = Register(focusKey, created);
        button.Content = label;
        button.AccessibleName = $"{label} {server.Name}";
        button.IsEnabled = enabled;
        button.Click += async (_, _) =>
        {
            try
            {
                UiActionResult result = await _controller.JoinAsync(server, spectate, ScreenCancellation);
                if (!result.Succeeded)
                {
                    _model.Status.Failed(result.Message);
                    _state.Show(_model.Status);
                }
            }
            catch (Exception error)
            {
                _model.Status.Failed(AsyncScreenState.FriendlyFailure(error, spectate ? "Spectating" : "Joining"));
                _state.Show(_model.Status);
            }
        };
        _details.Children.Add(button);
    }

    private void RefreshModeChoices()
    {
        string? selected = _mode.SelectedItem as string;
        string[] modes = ["All modes", .. _model.AvailableModes];
        _updatingFilters = true;
        _mode.ItemsSource = modes;
        _mode.SelectedItem = selected is not null && modes.Contains(selected,
            StringComparer.OrdinalIgnoreCase) ? selected : "All modes";
        _updatingFilters = false;
    }
}
