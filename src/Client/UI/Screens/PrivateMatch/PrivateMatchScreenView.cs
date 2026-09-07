using System;
using System.Threading;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using MphRead.Mods.UI.Components;
using MphRead.Mods.UI.Navigation;
using MphRead.Mods.UI.State;
using MphRead.Mods.UI.Theme;

namespace MphRead.Mods.UI.Screens.PrivateMatch;

public sealed class PrivateMatchScreenView : ScreenViewBase
{
    private readonly IPrivateMatchController _controller;
    private readonly UiRouter _router;
    private readonly PrivateMatchDraft _draft = new();
    private readonly ErrorBanner _validation = new() { IsVisible = false };
    private readonly Grid _layout;
    private readonly MapPickerView _maps;

    public PrivateMatchScreenView(UiRouter router, IPrivateMatchController controller,
        IMapCatalogController maps)
        : base("Private Match", "Configure an authoritative private lobby.", "private:name")
    {
        _router = router;
        _controller = controller;
        PrivateMatchCapabilities capabilities = controller.Capabilities;
        _draft.PublicListing = capabilities.AlwaysListed;
        var name = new TextBox { Text = _draft.Name, Watermark = "Match name" };
        Register("private:name", name);
        AutomationProperties.SetName(name, "Private match name");
        name.TextChanged += (_, _) => _draft.Name = name.Text ?? string.Empty;
        var mode = new ComboBox
        {
            ItemsSource = Enum.GetNames<MatchMode>(),
            SelectedIndex = 0
        };
        Register("private:mode", mode);
        AutomationProperties.SetName(mode, "Game mode");
        mode.SelectionChanged += (_, _) => _draft.Mode = mode.SelectedItem?.ToString() ?? "Battle";
        var maxPlayers = Number("private:max-players", "Maximum players", 1, 8, 8,
            value => _draft.MaxPlayers = value);
        var bots = Number("private:bots", "Bot count", 0, 8, 0,
            value => _draft.Bots = value);
        var botSkill = Number("private:bot-skill", "Bot skill", 0, 2, 1,
            value => _draft.BotSkill = value);
        var spectators = Number("private:spectators", "Maximum spectators", 0, 16, 4,
            value => _draft.MaxSpectators = value);
        var time = Number("private:time", "Time limit in seconds", 0, 86400, 420,
            value => _draft.TimeLimitSeconds = value);
        var score = Number("private:score", "Score goal", 0, 1_000_000, 7,
            value => _draft.ScoreGoal = value);
        var objective = Number("private:objective", "Objective time in seconds", 0, 86400, 0,
            value => _draft.ObjectiveTimeSeconds = value);
        var lives = Number("private:lives", "Starting lives", 0, 255, 0,
            value => _draft.StartingLives = value);
        var damage = Number("private:damage", "Damage level", 0, 2, 1,
            value => _draft.DamageLevel = value);
        var radar = Choice("private:radar", "Radar policy", Enum.GetNames<RadarPolicy>(),
            value => _draft.RadarPolicy = value);
        var spawn = Choice("private:spawn", "Spawn policy", Enum.GetNames<SpawnPolicy>(),
            value => _draft.SpawnPolicy = value);
        var overtime = Choice("private:overtime", "Overtime policy", Enum.GetNames<OvertimePolicy>(),
            value => _draft.OvertimePolicy = value);
        var lateJoin = Choice("private:late-join", "Late join policy", Enum.GetNames<LateJoinPolicy>(),
            value => _draft.LateJoinPolicy = value);
        var preset = Choice("private:preset", "Ruleset preset",
            new[] { "Classic", "Competitive", "Custom", "Duel", "Practice" }, value =>
            {
                _draft.Practice = value == "Practice";
                _draft.Preset = _draft.Practice ? nameof(RulesetPreset.Classic) : value;
                if (value == "Duel")
                {
                    mode.SelectedItem = nameof(MatchMode.Battle);
                    maxPlayers.Value = 2;
                }
            });
        var friendlyFire = Toggle("private:friendly-fire", "Friendly Fire",
            value => _draft.FriendlyFire = value);
        var affinity = Toggle("private:affinity", "Affinity weapons",
            value => _draft.AffinityWeapons = value);
        var readyRequired = Toggle("private:ready-required", "Ready required",
            value => _draft.ReadyRequired = value, initial: true);
        var listed = new CheckBox { Content = "List publicly", IsChecked = _draft.PublicListing,
            IsEnabled = !capabilities.AlwaysListed };
        Register("private:public-listing", listed);
        listed.IsCheckedChanged += (_, _) => _draft.PublicListing = listed.IsChecked == true;
        AutomationProperties.SetHelpText(listed, capabilities.AlwaysListed
            ? "Directory-hosted lobbies are always listed." : "Advertise this local lobby in the directory.");
        var password = Register("private:password", new TextBox
        {
            Watermark = capabilities.SupportsPassword ? "Optional password" : "Password unavailable",
            IsEnabled = capabilities.SupportsPassword
        });
        password.TextChanged += (_, _) => _draft.Password = password.Text;
        AutomationProperties.SetName(password, "Lobby password");
        if (!capabilities.SupportsPassword)
            AutomationProperties.SetHelpText(password,
                "Password admission is not supported by the current lobby protocol.");

        var create = Register("private:create", new PrimaryButton
        {
            Content = "Create Lobby", AccessibleName = "Create private lobby"
        });
        create.Click += async (_, _) => await CreateAsync();
        var form = new StackPanel
        {
            Spacing = UiSpacing.Space3,
            Children =
            {
                Text("MATCH", size: UiTypography.TextHeading), name, preset, mode,
                Text("Score / time", muted: true), score, time, objective, lives,
                Text("GAMEPLAY", size: UiTypography.TextHeading), friendlyFire, affinity,
                radar, spawn, damage, overtime, lateJoin,
                Text("PARTICIPANTS", size: UiTypography.TextHeading), maxPlayers, spectators,
                bots, botSkill, readyRequired, listed, password, create, _validation
            }
        };

        _maps = new MapPickerView(maps);
        _maps.SelectionChanged += (_, map) => _draft.MapId = map.Id;
        _maps.FocusTargetAdded += (key, control) => Register($"private:{key}", control);
        var mapArea = new StackPanel
        {
            Spacing = UiSpacing.Space3,
            Children = { Text("MAP", size: UiTypography.TextHeading), _maps }
        };
        _layout = new Grid { ColumnDefinitions = new ColumnDefinitions("*,2*"), ColumnSpacing = UiSpacing.Space5 };
        _layout.Children.Add(form);
        _layout.Children.Add(mapArea);
        Grid.SetColumn(mapArea, 1);
        Body.Content = _layout;
    }

    private NumericUpDown Number(string key, string name, int minimum, int maximum, int value,
        Action<int> changed)
    {
        var control = Register(key, new NumericUpDown
        {
            Minimum = minimum, Maximum = maximum, Value = value
        });
        AutomationProperties.SetName(control, name);
        control.ValueChanged += (_, _) => changed((int)(control.Value ?? value));
        return control;
    }

    private ComboBox Choice(string key, string name, string[] choices, Action<string> changed)
    {
        var control = Register(key, new ComboBox { ItemsSource = choices, SelectedIndex = 0 });
        AutomationProperties.SetName(control, name);
        control.SelectionChanged += (_, _) => changed(control.SelectedItem?.ToString() ?? choices[0]);
        return control;
    }

    private CheckBox Toggle(string key, string name, Action<bool> changed, bool initial = false)
    {
        var control = Register(key, new CheckBox { Content = name, IsChecked = initial });
        AutomationProperties.SetName(control, name);
        control.IsCheckedChanged += (_, _) => changed(control.IsChecked == true);
        return control;
    }

    protected override void OnLayoutModeChanged(UiLayoutMode mode)
    {
        bool compact = mode == UiLayoutMode.Compact;
        _layout.ColumnDefinitions = compact ? new ColumnDefinitions("*") : new ColumnDefinitions("*,2*");
        _layout.RowDefinitions = compact ? new RowDefinitions("Auto,Auto") : new RowDefinitions("Auto");
        Control maps = _layout.Children[1];
        Grid.SetColumn(maps, compact ? 0 : 1);
        Grid.SetRow(maps, compact ? 1 : 0);
        maps.Margin = compact ? new Avalonia.Thickness(0, UiSpacing.Space5, 0, 0) : default;
    }

    private async System.Threading.Tasks.Task CreateAsync()
    {
        var errors = _draft.Validate();
        if (errors.Count > 0)
        {
            _validation.Message = string.Join(" ", errors);
            _validation.IsVisible = true;
            return;
        }
        try
        {
            UiActionResult result = await _controller.CreateAsync(_draft, ScreenCancellation);
            _validation.IsVisible = !result.Succeeded;
            if (!result.Succeeded) _validation.Message = result.Message;
            else _router.Navigate(UiRoute.Lobby, sourceFocusKey: "private:create");
        }
        catch (Exception error)
        {
            _validation.Message = AsyncScreenState.FriendlyFailure(error, "The private lobby");
            _validation.IsVisible = true;
        }
    }
}
