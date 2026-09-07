using System;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using MphRead.Mods.Network;
using MphRead.Mods.UI.Components;
using MphRead.Mods.UI.Navigation;
using MphRead.Mods.UI.State;
using MphRead.Mods.UI.Theme;

namespace MphRead.Mods.UI.Screens.Lobby;

public sealed class LobbyScreenView : ScreenViewBase
{
    private readonly LobbyScreenModel _model;
    private readonly ILobbyScreenController _controller;
    private readonly Grid _sections;
    private readonly StackPanel _players = new() { Spacing = UiSpacing.Space2 };
    private readonly StackPanel _match = new() { Spacing = UiSpacing.Space3 };
    private readonly StackPanel _chat = new() { Spacing = UiSpacing.Space2 };
    private readonly StackPanel _controls = new() { Spacing = UiSpacing.Space2 };
    private readonly StackPanel _matchColumn;
    private readonly AsyncStatePresenter _state = new();
    private readonly ErrorBanner _feedback = new() { IsVisible = false };
    private readonly StackPanel _compactTabs = new()
    {
        Orientation = Orientation.Horizontal, Spacing = UiSpacing.Space2, IsVisible = false
    };
    private readonly DispatcherTimer _refreshTimer;
    private readonly CheckBox _botFill;
    private readonly NumericUpDown _botMinimum;
    private readonly NumericUpDown _botSkill;
    private UiLobbyAction? _pending;

    public LobbyScreenView(ILobbyScreenController controller)
        : base("Lobby", "Server-owned roster, rules, ready state, and chat.", "lobby:player:0")
    {
        _controller = controller;
        _model = new LobbyScreenModel(controller);
        _matchColumn = new StackPanel
        {
            Spacing = UiSpacing.Space4,
            Children = { _match, _controls }
        };
        AddCompactTab("Players", _players);
        AddCompactTab("Match", _matchColumn);
        AddCompactTab("Chat", _chat);
        _sections = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            ColumnSpacing = UiSpacing.Space4,
            Children = { _players, _matchColumn, _chat }
        };
        Grid.SetColumn(_matchColumn, 1);
        Grid.SetColumn(_chat, 2);
        _sections.RowDefinitions = new RowDefinitions("Auto");

        (_botFill, _botMinimum, _botSkill) = BuildControls();
        Body.Content = new StackPanel
        {
            Spacing = UiSpacing.Space4,
            Children = { _compactTabs, _state, _sections, _feedback }
        };
        _state.Show(_model.Status);
        Rebuild();
        _controller.Changed += ControllerChanged;
        _refreshTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background,
            (_, _) => { uint before = _model.Snapshot?.Revision ?? 0; _model.Refresh();
                if ((_model.Snapshot?.Revision ?? 0) != before) Rebuild(); });
        AttachedToVisualTree += (_, _) => _refreshTimer.Start();
        DetachedFromVisualTree += (_, _) => _refreshTimer.Stop();
    }

    protected override void OnLayoutModeChanged(UiLayoutMode mode)
    {
        int columns = LobbyScreenModel.SectionColumns(mode);
        bool compact = columns == 1;
        _compactTabs.IsVisible = compact;
        _sections.ColumnDefinitions = columns switch
        {
            1 => new ColumnDefinitions("*"),
            2 => new ColumnDefinitions("*,*"),
            _ => new ColumnDefinitions("*,*,*")
        };
        _sections.RowDefinitions = columns == 2
            ? new RowDefinitions("Auto,Auto") : new RowDefinitions("Auto");
        if (compact)
        {
            ShowCompact(_players);
            Grid.SetColumn(_matchColumn, 0); Grid.SetRow(_matchColumn, 0);
            Grid.SetColumn(_chat, 0); Grid.SetRow(_chat, 0); Grid.SetColumnSpan(_chat, 1);
            _chat.Margin = default;
        }
        else if (columns == 2)
        {
            _players.IsVisible = _matchColumn.IsVisible = _chat.IsVisible = true;
            Grid.SetColumn(_matchColumn, 1); Grid.SetRow(_matchColumn, 0);
            Grid.SetColumn(_chat, 0); Grid.SetRow(_chat, 1); Grid.SetColumnSpan(_chat, 2);
            _chat.Margin = new Thickness(0, UiSpacing.Space4, 0, 0);
        }
        else
        {
            _players.IsVisible = _matchColumn.IsVisible = _chat.IsVisible = true;
            Grid.SetColumn(_matchColumn, 1); Grid.SetRow(_matchColumn, 0);
            Grid.SetColumn(_chat, 2); Grid.SetRow(_chat, 0); Grid.SetColumnSpan(_chat, 1);
            _chat.Margin = default;
        }
    }

    private void AddCompactTab(string label, Control section)
    {
        var button = new SecondaryButton { Content = label, AccessibleName = $"Show lobby {label}" };
        button.Click += (_, _) => ShowCompact(section);
        _compactTabs.Children.Add(button);
    }

    private void ShowCompact(Control selected)
    {
        _players.IsVisible = ReferenceEquals(selected, _players);
        _matchColumn.IsVisible = ReferenceEquals(selected, _matchColumn);
        _chat.IsVisible = ReferenceEquals(selected, _chat);
    }

    private (CheckBox Fill, NumericUpDown Minimum, NumericUpDown Skill) BuildControls()
    {
        string[] hunters = Enum.GetValues<Hunter>()
            .Where(value => (int)value < 7 || value == Hunter.Random)
            .Select(value => value.ToString()).ToArray();
        var hunter = Register("lobby:hunter", new ComboBox
        {
            ItemsSource = hunters, SelectedIndex = 0, MinWidth = 150
        });
        AutomationProperties.SetName(hunter, "Select Hunter");
        var hunterDetail = Text("Choose one of the seven playable Hunters, or Random. "
            + "The server confirms roster changes.", muted: true);
        var hunterButton = Register("lobby:hunter:apply", new SecondaryButton
        {
            Content = "Change Hunter", AccessibleName = "Request selected Hunter"
        });
        hunterButton.Click += async (_, _) => await RequestAsync(new UiLobbyCommand(
            UiLobbyAction.SelectHunter, hunter.SelectedIndex));

        var team = Register("lobby:team", new ComboBox
        {
            ItemsSource = new[] { "Orange", "Green" }, SelectedIndex = 0, MinWidth = 120
        });
        AutomationProperties.SetName(team, "Requested team");
        var teamButton = Register("lobby:team:apply", new SecondaryButton
        {
            Content = "Request Team", AccessibleName = "Request selected team"
        });
        teamButton.Click += async (_, _) => await RequestAsync(new UiLobbyCommand(
            UiLobbyAction.RequestTeam, team.SelectedIndex));

        var map = Register("lobby:map", new TextBox { Watermark = "Map key", MinWidth = 160 });
        AutomationProperties.SetName(map, "Map key");
        var mapButton = Register("lobby:map:apply", new SecondaryButton
        {
            Content = "Change Map", AccessibleName = "Request map change"
        });
        mapButton.Click += async (_, _) => await RequestAsync(new UiLobbyCommand(
            UiLobbyAction.SetMap, Text: map.Text ?? string.Empty));

        var mode = Register("lobby:mode", new ComboBox
        {
            ItemsSource = Enum.GetNames<MatchMode>(), SelectedIndex = 0, MinWidth = 150
        });
        AutomationProperties.SetName(mode, "Match mode");
        var modeButton = Register("lobby:mode:apply", new SecondaryButton
        {
            Content = "Change Mode", AccessibleName = "Request selected match mode"
        });
        modeButton.Click += async (_, _) => await RequestAsync(new UiLobbyCommand(
            UiLobbyAction.SetMode, mode.SelectedIndex));

        var rule = Register("lobby:rule", new ComboBox
        {
            ItemsSource = new[] { "FriendlyFire", "PlayerRadar", "AffinityWeapons" }, SelectedIndex = 0
        });
        AutomationProperties.SetName(rule, "Lobby rule");
        var ruleButton = Register("lobby:rule:apply", new SecondaryButton
        {
            Content = "Toggle Rule", AccessibleName = "Request rule change"
        });
        ruleButton.Click += async (_, _) => await RequestAsync(new UiLobbyCommand(
            UiLobbyAction.SetRule, 1, Rule: rule.SelectedItem?.ToString() ?? ""));

        var botFill = Register("lobby:bots:enabled", new CheckBox
        {
            Content = "Fill empty player slots with bots"
        });
        AutomationProperties.SetName(botFill, "Bot fill enabled");
        var botFillButton = Register("lobby:bots:enabled:apply", new SecondaryButton
        {
            Content = "Apply Bot Fill", AccessibleName = "Request bot fill enabled state"
        });
        botFillButton.Click += async (_, _) => await RequestAsync(new UiLobbyCommand(
            UiLobbyAction.SetBotFillEnabled, botFill.IsChecked == true ? 1 : 0));

        var botMinimum = Register("lobby:bots:minimum", new NumericUpDown
        {
            Minimum = 0, Maximum = LobbySnapshotPacket.MaximumPlayers, Value = 0, MinWidth = 100
        });
        AutomationProperties.SetName(botMinimum, "Bot fill minimum participants");
        var botMinimumButton = Register("lobby:bots:minimum:apply", new SecondaryButton
        {
            Content = "Apply Bot Minimum", AccessibleName = "Request bot fill minimum participants"
        });
        botMinimumButton.Click += async (_, _) => await RequestAsync(new UiLobbyCommand(
            UiLobbyAction.SetBotMinimumParticipants, (int)(botMinimum.Value ?? 0)));

        var botSkill = Register("lobby:bots:skill", new NumericUpDown
        {
            Minimum = 0, Maximum = 2, Value = 0, MinWidth = 100
        });
        AutomationProperties.SetName(botSkill, "Bot skill");
        var botSkillButton = Register("lobby:bots:skill:apply", new SecondaryButton
        {
            Content = "Apply Bot Skill", AccessibleName = "Request bot skill"
        });
        botSkillButton.Click += async (_, _) => await RequestAsync(new UiLobbyCommand(
            UiLobbyAction.SetBotSkill, (int)(botSkill.Value ?? 0)));

        var ready = Register("lobby:ready", new PrimaryButton
        {
            Content = "Ready", AccessibleName = "Toggle ready state"
        });
        ready.Click += async (_, _) =>
        {
            bool current = _model.Snapshot?.Members.FirstOrDefault(member => member.Local)?.Ready == true;
            await RequestAsync(new UiLobbyCommand(UiLobbyAction.SetReady, current ? 0 : 1));
        };
        var start = Register("lobby:start", new SecondaryButton
        {
            Content = "Start Match", AccessibleName = "Request match start"
        });
        start.Click += async (_, _) => await RequestAsync(new UiLobbyCommand(UiLobbyAction.StartMatch));
        var leave = Register("lobby:leave", new SecondaryButton
        {
            Content = "Leave Server", AccessibleName = "Leave server"
        });
        leave.Click += async (_, _) => await RequestAsync(new UiLobbyCommand(UiLobbyAction.LeaveServer));

        _controls.Children.Add(hunterDetail);
        _controls.Children.Add(new WrapPanel
        {
            Children = { hunter, hunterButton, team, teamButton, map, mapButton, mode, modeButton,
                rule, ruleButton, botFill, botFillButton, botMinimum, botMinimumButton,
                botSkill, botSkillButton, ready, start, leave }
        });
        return (botFill, botMinimum, botSkill);
    }

    private void Rebuild()
    {
        _state.Show(_model.Status);
        UiLobbySnapshot? snapshot = _model.Snapshot;
        if (snapshot is null) return;
        _players.Children.Clear();
        _players.Children.Add(Text("PLAYERS", size: UiTypography.TextHeading));
        int index = 0;
        foreach (UiLobbyMember member in snapshot.Members.Where(member => !member.Observer))
        {
            var row = new PlayerRow
            {
                Title = $"{member.Name}{(member.Local ? " (You)" : "")}",
                Detail = $"{member.Hunter} · {snapshot.TeamLabel(member)} · {member.StatusLabel} · {member.PingMs} ms"
            };
            _players.Children.Add(Register($"lobby:player:{index++}", row));
        }
        _players.Children.Add(Text("SPECTATORS", size: UiTypography.TextHeading));
        int observerIndex = 0;
        foreach (UiLobbyMember member in snapshot.Members.Where(member => member.Observer))
        {
            var row = new PlayerRow
            {
                Title = $"{member.Name}{(member.Local ? " (You)" : "")}",
                Detail = $"{member.Hunter} · {member.StatusLabel} · {member.PingMs} ms"
            };
            _players.Children.Add(Register($"lobby:observer:{observerIndex++}", row));
        }
        _match.Children.Clear();
        _match.Children.Add(Text("MATCH", size: UiTypography.TextHeading));
        _match.Children.Add(Text($"{snapshot.Map}\n{snapshot.Mode}\n{snapshot.RulesSummary}\n"
            + $"Phase: {snapshot.Phase} · Policy: {snapshot.Policy}\n"
            + $"Bot fill: {(snapshot.BotFillEnabled ? "Enabled" : "Disabled")} · "
            + $"Minimum: {snapshot.BotMinimumParticipants} · Skill: {snapshot.BotSkill}"));
        _botFill.IsChecked = snapshot.BotFillEnabled;
        _botMinimum.Value = snapshot.BotMinimumParticipants;
        _botSkill.Value = snapshot.BotSkill;
        _chat.Children.Clear();
        _chat.Children.Add(Text("CHAT", size: UiTypography.TextHeading));
        int chatIndex = 0;
        foreach (UiLobbyChatLine line in snapshot.Chat.TakeLast(32))
        {
            var mute = Register($"lobby:chat:mute:{chatIndex++}", new SecondaryButton
            {
                Content = "Mute", AccessibleName = $"Mute {line.Name}"
            });
            ulong senderIdentity = line.SenderIdentity;
            mute.IsEnabled = senderIdentity != 0;
            mute.Click += (_, _) => _model.SetSenderMuted(senderIdentity, muted: true);
            _chat.Children.Add(new WrapPanel
            {
                Children = { Text($"{line.Name}{(line.Team ? " [TEAM]" : "")}: {line.Text}"), mute }
            });
        }
        foreach (UiMutedLobbySender sender in snapshot.EffectiveMutedSenders)
        {
            var unmute = Register($"lobby:chat:unmute:{sender.SenderIdentity}", new SecondaryButton
            {
                Content = $"Unmute {sender.Name}", AccessibleName = $"Unmute {sender.Name}"
            });
            ulong senderIdentity = sender.SenderIdentity;
            unmute.Click += (_, _) => _model.SetSenderMuted(senderIdentity, muted: false);
            _chat.Children.Add(unmute);
        }
        BuildChatEntry();
        ApplyAvailability(snapshot);
    }

    private void BuildChatEntry()
    {
        var input = Register("lobby:chat", new TextBox { Watermark = "Message", MaxLength = 160 });
        AutomationProperties.SetName(input, "Lobby chat message");
        var send = Register("lobby:chat:send", new SecondaryButton
        {
            Content = "Send", AccessibleName = "Send lobby chat"
        });
        send.Click += async (_, _) =>
        {
            string text = input.Text ?? string.Empty;
            UiActionResult result = await RequestAsync(new UiLobbyCommand(UiLobbyAction.SendLobbyChat, Text: text));
            if (result.Succeeded) input.Text = string.Empty;
        };
        var sendTeam = Register("lobby:chat:team", new SecondaryButton
        {
            Content = "Send Team", AccessibleName = "Send team chat"
        });
        sendTeam.Click += async (_, _) =>
        {
            string text = input.Text ?? string.Empty;
            UiActionResult result = await RequestAsync(new UiLobbyCommand(
                UiLobbyAction.SendTeamChat, Text: text));
            if (result.Succeeded) input.Text = string.Empty;
        };
        _chat.Children.Add(new WrapPanel { Children = { input, send, sendTeam } });
    }

    private void ApplyAvailability(UiLobbySnapshot snapshot)
    {
        foreach ((string key, Control control) in FocusTargets)
        {
            UiLobbyAction? action = key switch
            {
                "lobby:hunter:apply" => UiLobbyAction.SelectHunter,
                "lobby:hunter" => UiLobbyAction.SelectHunter,
                "lobby:team:apply" => UiLobbyAction.RequestTeam,
                "lobby:team" => UiLobbyAction.RequestTeam,
                "lobby:map:apply" => UiLobbyAction.SetMap,
                "lobby:map" => UiLobbyAction.SetMap,
                "lobby:mode:apply" => UiLobbyAction.SetMode,
                "lobby:mode" => UiLobbyAction.SetMode,
                "lobby:rule:apply" => UiLobbyAction.SetRule,
                "lobby:rule" => UiLobbyAction.SetRule,
                "lobby:bots:enabled:apply" => UiLobbyAction.SetBotFillEnabled,
                "lobby:bots:enabled" => UiLobbyAction.SetBotFillEnabled,
                "lobby:bots:minimum:apply" => UiLobbyAction.SetBotMinimumParticipants,
                "lobby:bots:minimum" => UiLobbyAction.SetBotMinimumParticipants,
                "lobby:bots:skill:apply" => UiLobbyAction.SetBotSkill,
                "lobby:bots:skill" => UiLobbyAction.SetBotSkill,
                "lobby:ready" => UiLobbyAction.SetReady,
                "lobby:start" => UiLobbyAction.StartMatch,
                "lobby:chat:send" => UiLobbyAction.SendLobbyChat,
                "lobby:chat:team" => UiLobbyAction.SendTeamChat,
                "lobby:leave" => UiLobbyAction.LeaveServer,
                _ => null
            };
            if (action is null) continue;
            bool enabled = snapshot.IsEnabled(action.Value) && _pending is null;
            if (action == UiLobbyAction.RequestTeam && !snapshot.UsesTeams) enabled = false;
            if (action == UiLobbyAction.SendTeamChat && !snapshot.UsesTeams) enabled = false;
            control.IsEnabled = enabled;
            if (!control.IsEnabled && snapshot.DisabledReason(action.Value) is { } reason)
                AutomationProperties.SetHelpText(control, reason);
            else if (!control.IsEnabled && action is UiLobbyAction.RequestTeam
                or UiLobbyAction.SendTeamChat)
                AutomationProperties.SetHelpText(control, "This match mode does not use teams.");
        }
        string[] reasons = snapshot.DisabledReasons.Values.Distinct().ToArray();
        _feedback.IsVisible = reasons.Length > 0;
        if (reasons.Length > 0) _feedback.Message = string.Join(" ", reasons);
    }

    private async System.Threading.Tasks.Task<UiActionResult> RequestAsync(UiLobbyCommand command)
    {
        _pending = command.Action;
        if (_model.Snapshot is { } snapshot) ApplyAvailability(snapshot);
        UiActionResult result = await _model.RequestAsync(command, ScreenCancellation);
        _pending = null;
        if (!result.Succeeded)
        {
            _feedback.Message = result.Message;
            _feedback.IsVisible = true;
        }
        Rebuild();
        return result;
    }

    private void ControllerChanged()
        => Dispatcher.UIThread.Post(() => { _model.Refresh(); Rebuild(); });
}
