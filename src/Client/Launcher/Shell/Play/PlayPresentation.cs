using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FruityPrime.Server.Shared;
using MphRead.Mods.Accounts;
using MphRead.Mods;
using MphRead.Mods.Network;
using AvaloniaButton = Avalonia.Controls.Button;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>
/// Dependencies kept at the shell boundary. The presentation layer can rebuild
/// from a new authoritative PlayState without owning a Node session or
/// inventing a second copy of lobby state.
/// </summary>
internal sealed record PlayPresentationContext(
    PrimeShellState Shell,
    PlayController Controller,
    PlayState State,
    IReadOnlyList<string> LocalMaps,
    PlayPresentationState Ui,
    CancellationToken CancellationToken,
    Action<string, Func<Task>> RunCommand,
    Action<Action> PostUi,
    Action Refresh,
    Action OpenGateway,
    Func<string?, double, string, Control> BuildPreview,
    Func<HostMatchDraft, Task> HostMatch,
    Func<HostMatchDraft, Task> ConfigureMatch,
    Action<ComboBox, DeferredControllerSelection<Hunter>> TrackHunter,
    Action EditorLostFocus,
    Action<PrimeNotificationKind, string> Notify,
    bool ExpandAdvancedNetwork = false);

/// <summary>Native Avalonia presentation for the Play route.</summary>
internal static class PlayPresentation
{
    private const int MaxDisplayedLobbies = 1024;

    public static Control Build(PlayPresentationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.Shell.HasNetworkIdentity)
            return BuildSignedOut(context);

        LobbySnapshot? lobby = context.State.Lobby;
        if (lobby is not null)
        {
            if (context.Ui.EditMatchOpen && context.Ui.EditDraft is { } edit
                && edit.SourceLobbyId == lobby.LobbyId)
                return BuildEditMatch(context, lobby, edit);
            if (context.Ui.EditMatchOpen && context.Ui.EditDraft is not null)
                context.Ui.ClearEdit();
            return BuildLobby(context, lobby);
        }

        // A lobby disappearing is an authoritative boundary, so do not carry
        // a confirmation into a later lobby that happens to reuse the route.
        context.Ui.ObserveLobby(Guid.Empty);
        return context.Ui.Subsection switch
        {
            PlaySubsection.Browser => BuildBrowser(context),
            PlaySubsection.HostMatch => BuildHostMatch(context),
            _ => BuildHome(context)
        };
    }

    private static Control BuildSignedOut(PlayPresentationContext context)
    {
        var root = Page("Play", "ONLINE MULTIPLAYER",
            "Choose an identity before entering the match directory.");
        root.Children.Add(Section(Stack(
            Text("Sign in required", "prime-heading"),
            Text("Choose an account or explicit Guest access in Gateway before browsing matches or joining a lobby.",
                "prime-muted"),
            Button("Open Gateway", context.OpenGateway, primary: true))));
        root.Children.Add(Section(Stack(
            Text("Your identity stays explicit", "prime-heading"),
            Text("Guest access is separate from account identity and can be selected again from Gateway at any time.",
                "prime-body"))));
        return root;
    }

    private static Control BuildHome(PlayPresentationContext context)
    {
        PlayState state = context.State;
        var root = Page("Play", "ONLINE MULTIPLAYER",
            "Find a match, browse open lobbies, or host a game for your squad.");

        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(Button("Quick Play", () =>
        {
            context.Ui.Subsection = PlaySubsection.Home;
            context.RunCommand("Quick Play", () => context.Controller.QuickPlayAsync(context.CancellationToken));
        }, primary: true));
        actions.Children.Add(Button("Browse Matches", () =>
        {
            context.Ui.Subsection = PlaySubsection.Browser;
            context.RunCommand("Browse matches", () => context.Controller.BrowseLobbiesAsync(context.CancellationToken));
            context.Refresh();
        }));
        actions.Children.Add(Button("Host Match", () =>
        {
            context.Ui.Subsection = PlaySubsection.HostMatch;
            context.Ui.HostStep = 1;
            context.Refresh();
        }));
        actions.IsEnabled = !state.Loading;
        root.Children.Add(actions);

        if (state.Loading)
        {
            root.Children.Add(Section(Stack(
                Text("Finding a match…", "prime-heading"),
                Text("Quick Play is checking the current open lobbies. Your Node connection stays available while this runs.",
                    "prime-muted"),
                Button("Cancel", () => context.Controller.CancelEntry(), quiet: true))));
        }
        else if (!string.IsNullOrWhiteSpace(state.Message)
            && !(state.Phase == PlayPhase.Nodes && state.Revision == 0))
        {
            root.Children.Add(StatusCard(state.Message,
                state.Phase == PlayPhase.Error ? GuiTheme.BadBrush : GuiTheme.EdgeBrush));
        }

        LobbyListSnapshot? lobbies = state.Lobbies;
        if (lobbies is { Lobbies.IsDefaultOrEmpty: false })
        {
            ImmutableArray<LobbyListEntry> entries = lobbies.Lobbies;
            var preview = Stack(
                Text("Matches ready to join", "prime-heading"),
                Text("These cards use the latest server directory snapshot. Open Browse Matches for filters and all actions.",
                    "prime-muted"));
            foreach (LobbyListEntry entry in entries.Take(3))
                preview.Children.Add(CreateMatchCard(context, entry));
            preview.Children.Add(Button("Browse all matches", () =>
            {
                context.Ui.Subsection = PlaySubsection.Browser;
                context.Refresh();
            }, quiet: true));
            root.Children.Add(Section(preview));
        }
        else if (state.Phase is PlayPhase.Nodes or PlayPhase.Connected && !state.Loading)
        {
            root.Children.Add(Section(new PrimeEmptyState(
                "No match directory loaded yet. Choose Browse Matches to ask the connected Node for open games.")));
        }

        root.Children.Add(new Expander
        {
            Header = "Advanced Network",
            IsExpanded = context.ExpandAdvancedNetwork,
            Content = BuildNetwork(context)
        });
        return root;
    }

    private static Control BuildNetwork(PlayPresentationContext context)
    {
        PlayState state = context.State;
        var network = Stack(
            Text("Connection", "prime-heading"),
            Text(state.Node?.Session is { } session
                ? $"Connected · session {session.SessionId.ToString()[..8]}"
                : "Disconnected", "prime-muted"));

        var region = Editor(context.Controller.PreferredRegion, "Preferred region (optional)");
        region.TextChanged += (_, _) => context.Controller.PreferredRegion = region.Text?.Trim() ?? "";
        TrackEditor(context, region);
        network.Children.Add(Text("Automatic server selection", "prime-label"));
        network.Children.Add(region);
        network.Children.Add(Text("Region is used only when selecting a Node. Lobby cards do not claim a region unless the authoritative lobby DTO provides one.",
            "prime-muted"));
        network.Children.Add(Button("Refresh servers", () => context.RunCommand("Refresh servers",
            () => context.Controller.ForceRefreshNodesAsync(context.CancellationToken)), quiet: true));
        if (NodeSessions.Current is not null)
            network.Children.Add(Button("Disconnect", () => context.RunCommand("Disconnect",
                context.Controller.DisconnectAsync), quiet: true));

        if (state.Nodes.Count == 0)
        {
            network.Children.Add(Text(state.Loading ? "Loading compatible servers…"
                : "No compatible servers are available yet.", "prime-muted"));
        }
        else
        {
            network.Children.Add(Text("Available servers", "prime-label"));
            foreach (NodeListing node in state.Nodes)
            {
                NodeListing captured = node;
                network.Children.Add(Button(
                    $"{node.Name} · {node.Region} · {node.OnlineUsers}/{node.Capacity} players",
                    () => context.RunCommand("Connect server",
                        () => context.Controller.ConnectNodeAsync(captured, context.CancellationToken))));
            }
        }
        if (state.Node?.Error is { Length: > 0 } error)
            network.Children.Add(StatusCard(error, GuiTheme.BadBrush));
        return network;
    }

    private static Control BuildBrowser(PlayPresentationContext context)
    {
        PlayState state = context.State;
        var root = Page("Browse Matches", "MATCH DIRECTORY",
            "Choose a player seat, spectate an open lobby, or join its authoritative waitlist when it is full.");
        root.Children.Add(Button("Back to Play", () =>
        {
            context.Ui.Subsection = PlaySubsection.Home;
            context.Refresh();
        }, quiet: true));

        LobbyListSnapshot? snapshot = state.Lobbies;
        LobbyListEntry[] source = snapshot?.Lobbies.Take(MaxDisplayedLobbies).ToArray()
            ?? Array.Empty<LobbyListEntry>();
        var cardsHost = new StackPanel { Spacing = 0 };

        void RebuildCards()
        {
            cardsHost.Children.Clear();
            ImmutableArray<LobbyListEntry> entries = MatchBrowserFiltering.Apply(
                source, context.Ui.Filters, context.Ui.Sort);
            if (entries.IsDefaultOrEmpty)
            {
                cardsHost.Children.Add(new PrimeEmptyState(snapshot is null
                    ? "Choose Refresh matches to load the current server directory."
                    : "No matches match these filters. Clear a filter or refresh the directory."));
                return;
            }
            foreach (LobbyListEntry entry in entries)
                cardsHost.Children.Add(CreateMatchCard(context, entry));
        }

        root.Children.Add(BuildBrowserFilters(context, source, RebuildCards));
        root.Children.Add(cardsHost);
        RebuildCards();

        var footer = new WrapPanel { Orientation = Orientation.Horizontal };
        footer.Children.Add(Button("Refresh matches", () => context.RunCommand("Refresh matches",
            () => context.Controller.BrowseLobbiesAsync(context.CancellationToken)), primary: true));
        if (state.Loading)
            footer.Children.Add(Button("Cancel", () => context.Controller.CancelEntry(), quiet: true));
        root.Children.Add(Section(Stack(
            Text(state.Loading ? "Loading the match directory…" :
                snapshot is null ? "The directory has not been loaded." :
                $"Showing {source.Length} authoritative match entries.", "prime-muted"),
            footer)));
        return root;
    }

    private static Control BuildBrowserFilters(PlayPresentationContext context,
        LobbyListEntry[] entries, Action rebuild)
    {
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(Text("FILTERS", "prime-label"));
        var row = new WrapPanel { Orientation = Orientation.Horizontal };

        ModeChoice[] modeChoices = Enum.GetValues<MatchMode>()
            .Select(value => new ModeChoice(value, FormatWords(value.ToString()))).ToArray();
        object[] modes = new object[] { "Any" }
            .Concat(modeChoices.Cast<object>()).ToArray();
        object selectedMode = context.Ui.Filters.Mode is { } selected
            ? modeChoices.First(choice => choice.Value == selected)
            : "Any";
        ComboBox mode = Combo(modes, selectedMode);
        mode.SelectionChanged += (_, _) =>
        {
            context.Ui.SetFilters(context.Ui.Filters with
            {
                Mode = mode.SelectedItem is ModeChoice value ? value.Value : null
            });
            rebuild();
        };
        TrackEditor(context, mode);
        row.Children.Add(Field("Mode", mode, 190));

        string[] mapValues = new[] { "Any" }.Concat(entries
            .Select(entry => entry.MapKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)).ToArray();
        ComboBox map = Combo(mapValues.Cast<object>().ToArray(),
            context.Ui.Filters.MapKey ?? "Any");
        map.SelectionChanged += (_, _) =>
        {
            context.Ui.SetFilters(context.Ui.Filters with
            {
                MapKey = map.SelectedItem as string is { } value && value != "Any" ? value : null
            });
            rebuild();
        };
        TrackEditor(context, map);
        row.Children.Add(Field("Map", map, 230));

        ComboBox sort = Combo(new object[] { "Recommended", "Most players", "Most open slots", "Name" },
            SortLabel(context.Ui.Sort));
        sort.SelectionChanged += (_, _) =>
        {
            context.Ui.SetSort((sort.SelectedItem as string) switch
            {
                "Most players" => MatchBrowserSort.MostPlayers,
                "Most open slots" => MatchBrowserSort.MostOpenSlots,
                "Name" => MatchBrowserSort.Name,
                _ => MatchBrowserSort.Recommended
            });
            rebuild();
        };
        TrackEditor(context, sort);
        row.Children.Add(Field("Sort", sort, 180));
        content.Children.Add(row);

        var checks = new WrapPanel { Orientation = Orientation.Horizontal };
        CheckBox open = Check("Open player seats", context.Ui.Filters.OpenPlayerSlotsOnly);
        open.IsCheckedChanged += (_, _) =>
        {
            context.Ui.SetFilters(context.Ui.Filters with { OpenPlayerSlotsOnly = open.IsChecked == true });
            rebuild();
        };
        CheckBox spectate = Check("Spectatable", context.Ui.Filters.SpectatableOnly);
        spectate.IsCheckedChanged += (_, _) =>
        {
            context.Ui.SetFilters(context.Ui.Filters with { SpectatableOnly = spectate.IsChecked == true });
            rebuild();
        };
        CheckBox hideFull = Check("Hide full matches", context.Ui.Filters.HideFull);
        hideFull.IsCheckedChanged += (_, _) =>
        {
            context.Ui.SetFilters(context.Ui.Filters with { HideFull = hideFull.IsChecked == true });
            rebuild();
        };
        checks.Children.Add(open);
        checks.Children.Add(spectate);
        checks.Children.Add(hideFull);
        content.Children.Add(checks);
        content.Children.Add(Text("The directory provides mode, map, capacity, observer seats, bots, rules, phase, and waitlist counts. Ping, trust, latency, and lobby region are not inferred here.",
            "prime-muted"));
        return Section(content);
    }

    private static Control BuildHostMatch(PlayPresentationContext context)
    {
        HostMatchDraft draft = context.Ui.HostDraft;
        var root = Page("Host Match", "NEW MULTIPLAYER LOBBY",
            "Set the lobby capacity first, then choose the mission and rules the server will validate.");
        root.Children.Add(Button("Back to Play", () =>
        {
            context.Ui.Subsection = PlaySubsection.Home;
            context.Refresh();
        }, quiet: true));
        root.Children.Add(StepRail(context.Ui.HostStep));

        if (context.Ui.HostStep == 1)
            root.Children.Add(BuildHostStepOne(context, draft));
        else
            root.Children.Add(BuildMatchConfiguration(context, draft,
                submitLabel: "Create Match", cancel: () =>
                {
                    context.Ui.HostStep = 1;
                    context.Refresh();
                }, submit: () =>
                {
                    if (!draft.TryBuildRules(out _, out string error))
                    {
                        context.Notify(PrimeNotificationKind.Error, error);
                        return;
                    }
                    context.RunCommand("Host match", () => context.HostMatch(draft));
                }));
        return root;
    }

    private static Control BuildHostStepOne(PlayPresentationContext context, HostMatchDraft draft)
    {
        var name = Editor(draft.Name, "Lobby name");
        name.MaxLength = 64;
        name.TextChanged += (_, _) => draft.Name = name.Text ?? "";
        TrackEditor(context, name);

        ComboBox players = Combo(Enumerable.Range(1, 8).Cast<object>().ToArray(), draft.PlayerLimit);
        players.SelectionChanged += (_, _) =>
        {
            if (players.SelectedItem is int value)
            {
                draft.PlayerLimit = value;
                draft.BotCount = Math.Min(draft.BotCount, value);
            }
        };
        TrackEditor(context, players);

        ComboBox observers = Combo(Enumerable.Range(0, 17).Cast<object>().ToArray(), draft.ObserverLimit);
        observers.SelectionChanged += (_, _) =>
        {
            if (observers.SelectedItem is int value) draft.ObserverLimit = value;
        };
        TrackEditor(context, observers);

        SeatPolicyChoice[] policies = Enum.GetValues<LobbySeatPolicy>()
            .Select(value => new SeatPolicyChoice(value, FormatWords(value.ToString()))).ToArray();
        ComboBox seatPolicy = Combo(policies.Cast<object>().ToArray(),
            policies.First(policy => policy.Value == draft.SeatPolicy));
        seatPolicy.SelectionChanged += (_, _) =>
        {
            if (seatPolicy.SelectedItem is SeatPolicyChoice value) draft.SeatPolicy = value.Value;
        };
        TrackEditor(context, seatPolicy);

        var fields = new StackPanel { Spacing = 10 };
        fields.Children.Add(Field("Match name", name, 420));
        fields.Children.Add(Field("Player seats", players, 180));
        fields.Children.Add(Field("Observer seats", observers, 180));
        fields.Children.Add(Field("Seat policy", seatPolicy, 280));
        fields.Children.Add(Text("Player seats include bots. Observer seats remain separate, and a full player roster can expose a server-owned waitlist.",
            "prime-muted"));
        fields.Children.Add(Button("Next: mission and rules", () =>
        {
            if (string.IsNullOrWhiteSpace(draft.Name))
            {
                context.Notify(PrimeNotificationKind.Error, "Enter a match name before continuing.");
                return;
            }
            context.Ui.HostStep = 2;
            context.Refresh();
        }, primary: true));
        return Section(fields);
    }

    private static Control BuildEditMatch(PlayPresentationContext context, LobbySnapshot lobby,
        HostMatchDraft draft)
    {
        var root = Page("Edit Match", "OWNER MATCH SETTINGS",
            "Changes are sent to the Node as one structured request. The authoritative snapshot decides the resulting mission and readiness state.");
        root.Children.Add(Button("Back to lobby", () =>
        {
            context.Ui.ClearEdit();
            context.Refresh();
        }, quiet: true));
        root.Children.Add(StatusCard("Changing map, mode, bots, or rules resets player readiness on the server.",
            GuiTheme.WarmBrush));
        root.Children.Add(BuildMatchConfiguration(context, draft, "Apply match settings",
            cancel: () =>
            {
                context.Ui.ClearEdit();
                context.Refresh();
            }, submit: () =>
            {
                if (!draft.TryBuildRules(out _, out string error))
                {
                    context.Notify(PrimeNotificationKind.Error, error);
                    return;
                }
                context.RunCommand("Apply match settings", () => context.ConfigureMatch(draft));
            }));
        root.Children.Add(Text($"Current authoritative revision · {lobby.Revision}", "prime-muted"));
        return root;
    }

    private static Control BuildMatchConfiguration(PlayPresentationContext context, HostMatchDraft draft,
        string submitLabel, Action cancel, Action submit)
    {
        string[] maps = context.Controller.AvailableMaps.ToArray();
        if (draft.MapKey.Length == 0 && maps.Length > 0) draft.MapKey = maps[0];
        var map = Combo(maps.Cast<object>().ToArray(), draft.MapKey);
        map.SelectionChanged += (_, _) => draft.MapKey = map.SelectedItem as string ?? "";
        TrackEditor(context, map);

        ModeChoice[] modes = Enum.GetValues<MatchMode>()
            .Select(value => new ModeChoice(value, FormatWords(value.ToString()))).ToArray();
        var mode = Combo(modes.Cast<object>().ToArray(),
            modes.First(choice => choice.Value == draft.Mode));
        TrackEditor(context, mode);

        int maximumBots = Math.Max(0, draft.PlayerLimit - 1);
        draft.BotCount = Math.Clamp(draft.BotCount, 0, maximumBots);
        var bots = Combo(Enumerable.Range(0, maximumBots + 1).Cast<object>().ToArray(), draft.BotCount);
        bots.SelectionChanged += (_, _) =>
        {
            if (bots.SelectedItem is int value) draft.BotCount = value;
        };
        TrackEditor(context, bots);

        var rulesHost = new Border();
        void RebuildRules() => rulesHost.Child = BuildRuleControls(context, draft);
        mode.SelectionChanged += (_, _) =>
        {
            if (mode.SelectedItem is ModeChoice value)
            {
                draft.Mode = value.Value;
                RebuildRules();
            }
        };
        RebuildRules();

        var fields = new StackPanel { Spacing = 10 };
        fields.Children.Add(Field("Map", map, 420));
        if (maps.Length == 0)
            fields.Children.Add(StatusCard(context.Controller.MapCatalogMessage, GuiTheme.WarmBrush));
        fields.Children.Add(Field("Mode", mode, 260));
        fields.Children.Add(Field("Bots", bots, 180));
        fields.Children.Add(rulesHost);
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(Button(submitLabel, submit, primary: true));
        actions.Children.Add(Button("Back", cancel, quiet: true));
        fields.Children.Add(actions);
        return Section(fields);
    }

    private static Control BuildRuleControls(PlayPresentationContext context, HostMatchDraft draft)
    {
        LobbyRuleApplicability applicability = LobbyRuleApplicability.For(draft.Mode);
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(Text("MATCH RULES", "prime-heading"));
        content.Children.Add(Text("Only controls applicable to the selected mode are shown. Default means the server's mode default.",
            "prime-muted"));

        TextBox time = Editor(draft.TimeLimitText, "Default or m:ss");
        time.TextChanged += (_, _) => draft.TimeLimitText = time.Text ?? "";
        TrackEditor(context, time);
        content.Children.Add(Field("Time limit", time, 180));

        if (applicability.ScoreGoal)
        {
            TextBox score = Editor(draft.ScoreGoalText, "Default or points");
            score.TextChanged += (_, _) => draft.ScoreGoalText = score.Text ?? "";
            TrackEditor(context, score);
            content.Children.Add(Field(applicability.GoalLabel, score, 180));
        }
        else if (applicability.StartingLives)
        {
            TextBox lives = Editor(draft.StartingLivesText, "Default or lives");
            lives.TextChanged += (_, _) => draft.StartingLivesText = lives.Text ?? "";
            TrackEditor(context, lives);
            content.Children.Add(Field(applicability.GoalLabel, lives, 180));
        }
        else if (applicability.ObjectiveTimeGoal)
        {
            TextBox objective = Editor(draft.ObjectiveTimeGoalText, "Default or m:ss");
            objective.TextChanged += (_, _) => draft.ObjectiveTimeGoalText = objective.Text ?? "";
            TrackEditor(context, objective);
            content.Children.Add(Field(applicability.GoalLabel, objective, 180));
        }

        if (applicability.DamageLevel)
        {
            DamageChoice[] choices =
            [
                new("Default", null),
                new("Low", 0),
                new("Normal", 1),
                new("High", 2)
            ];
            ComboBox damage = Combo(choices.Cast<object>().ToArray(),
                choices.First(choice => choice.Value == draft.DamageLevel));
            damage.SelectionChanged += (_, _) =>
            {
                if (damage.SelectedItem is DamageChoice choice) draft.DamageLevel = choice.Value;
            };
            TrackEditor(context, damage);
            content.Children.Add(Field("Damage level", damage, 180));
        }

        AddBoolControl(content, context, "Friendly fire", draft.FriendlyFire,
            value => draft.FriendlyFire = value, applicability.FriendlyFire);
        AddBoolControl(content, context, "Affinity weapons", draft.AffinityWeapons,
            value => draft.AffinityWeapons = value, applicability.AffinityWeapons);
        AddBoolControl(content, context, "Player radar", draft.PlayerRadar,
            value => draft.PlayerRadar = value, applicability.PlayerRadar);
        AddBoolControl(content, context, "Octolith reset", draft.OctolithReset,
            value => draft.OctolithReset = value, applicability.OctolithReset);
        return PrimeControlFactory.SectionPanel(content);
    }

    private static void AddBoolControl(StackPanel content, PlayPresentationContext context,
        string label, bool? value, Action<bool?> set, bool applicable)
    {
        if (!applicable) return;
        BoolChoice[] choices =
        [
            new("Default", null),
            new("On", true),
            new("Off", false)
        ];
        ComboBox combo = Combo(choices.Cast<object>().ToArray(),
            choices.First(choice => choice.Value == value));
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is BoolChoice choice) set(choice.Value);
        };
        TrackEditor(context, combo);
        content.Children.Add(Field(label, combo, 180));
    }

    private static Control BuildLobby(PlayPresentationContext context, LobbySnapshot lobby)
    {
        context.Ui.ObserveLobby(lobby.LobbyId);
        PlayState state = context.State;
        Guid? sessionId = state.Node?.Session?.SessionId;
        LobbyMember? current = sessionId is { } id
            ? lobby.Members.FirstOrDefault(member => member.SessionId == id)
            : null;
        bool owner = sessionId is { } ownerSession && lobby.OwnerSessionId == ownerSession;
        LobbyWaitlistSnapshot waitlist = lobby.Waitlist ?? LobbyWaitlistSnapshot.Empty;
        context.Ui.ObserveOffer(waitlist.SelfOffer?.OfferId);

        var root = Page(lobby.Name, "ACTIVE LOBBY",
            "Mission first: confirm the arena and rules, choose your hunter, then ready when your squad is set.");
        root.Children.Add(BuildLobbyHeader(lobby, current, waitlist));

        if (state.Node?.Session is null || NodeSessions.Current is { Connected: false })
        {
            root.Children.Add(StatusCard("Connection interrupted. Restore the Node session to continue this lobby.",
                GuiTheme.WarmBrush));
            root.Children.Add(Button("Restore connection", () => context.RunCommand("Restore connection",
                () => context.Controller.ResumeAsync(context.CancellationToken)), primary: true));
        }

        root.Children.Add(BuildMissionPanel(context, lobby, owner));

        bool teamMode = lobby.Mode.IsTeamMode();
        LobbyMember[] players = lobby.Members.Where(member => !member.Observer).ToArray();
        LobbyMember[] observers = lobby.Members.Where(member => member.Observer).ToArray();
        Control left = BuildRoster(teamMode ? "TEAM 1" : "PLAYERS",
            teamMode ? players.Where(member => member.Team == 0) : players,
            sessionId, GuiTheme.WarmBrush);
        Control right = teamMode
            ? Stack(
                BuildRoster("TEAM 2", players.Where(member => member.Team == 1),
                    sessionId, GuiTheme.AccentBrush),
                BuildRoster("OBSERVERS", observers, sessionId, GuiTheme.EdgeBrush))
            : BuildRoster("OBSERVERS", observers, sessionId, GuiTheme.AccentBrush);
        root.Children.Add(ResponsiveColumns(left, right));

        var lower = new StackPanel { Spacing = 12 };
        lower.Children.Add(BuildMemberControls(context, lobby, current));
        lower.Children.Add(BuildQueuePanel(context, lobby, current, waitlist));
        lower.Children.Add(BuildLobbyActions(context, lobby, current, owner, state));
        var chat = new LobbyChatPanel(lobby.Chat, context.Ui.ChatDraft,
            context.Ui.SetChatDraft,
            text => SendChat(context, text));
        TrackEditor(context, chat.DraftEditor);
        lower.Children.Add(chat);
        root.Children.Add(lower);
        return root;
    }

    private static Control BuildLobbyHeader(LobbySnapshot lobby, LobbyMember? current,
        LobbyWaitlistSnapshot waitlist)
    {
        int players = lobby.Members.Count(member => !member.Observer);
        int observers = lobby.Members.Count(member => member.Observer);
        int openPlayers = Math.Max(0, lobby.PlayerLimit - players - lobby.BotCount);
        var content = new StackPanel { Spacing = 5 };
        content.Children.Add(Text(lobby.Phase switch
        {
            LobbyPhase.Open => "OPEN FOR PLAYERS",
            LobbyPhase.StartingMatch => "PREPARING MATCH",
            LobbyPhase.InMatch => "MATCH IN PROGRESS",
            LobbyPhase.PostMatch => "BETWEEN ROUNDS",
            _ => lobby.Phase.ToString().ToUpperInvariant()
        }, "prime-kicker"));
        content.Children.Add(Text(
            $"{players + lobby.BotCount}/{lobby.PlayerLimit} player seats · {observers}/{lobby.ObserverLimit} observers · {openPlayers} open player seats",
            "prime-body"));
        if (current is not null)
            content.Children.Add(Text(current.Observer ? "You are observing this lobby."
                : current.Ready ? "You are Ready." : "You are choosing a hunter.", "prime-muted"));
        else if (waitlist.IsSelfQueued)
            content.Children.Add(Text("You are waiting for a player seat; you are not yet a lobby member.",
                "prime-muted"));
        return Section(content);
    }

    private static Control BuildMissionPanel(PlayPresentationContext context, LobbySnapshot lobby, bool owner)
    {
        string? mapPath = !string.IsNullOrWhiteSpace(lobby.MapKey)
            && ThumbnailGenerator.Exists(lobby.MapKey)
            ? ThumbnailGenerator.PathFor(lobby.MapKey) : null;
        var mission = Stack(context.BuildPreview(mapPath, 210,
                "No local map preview is available for this arena."),
            Text("MISSION", "prime-kicker"),
            Text(DisplayMap(lobby.MapKey), "prime-heading"));
        var stats = new WrapPanel { Orientation = Orientation.Horizontal };
        LobbyRulesOptions rules = LobbyRuleApplicability.RulesFromLobby(lobby);
        LobbyRuleApplicability applicability = LobbyRuleApplicability.For(lobby.Mode);
        stats.Children.Add(PrimeControlFactory.StatTile("MODE", FormatWords(lobby.Mode.ToString())));
        stats.Children.Add(PrimeControlFactory.StatTile("TIME",
            rules.TimeLimitSeconds is { } seconds ? FormatDuration(seconds) : "Default"));
        if (applicability.ScoreGoal)
            stats.Children.Add(PrimeControlFactory.StatTile("SCORE",
                rules.ScoreGoal?.ToString(CultureInfo.InvariantCulture) ?? "Default"));
        else if (applicability.StartingLives)
            stats.Children.Add(PrimeControlFactory.StatTile("LIVES",
                rules.StartingLives?.ToString(CultureInfo.InvariantCulture) ?? "Default"));
        else if (applicability.ObjectiveTimeGoal)
            stats.Children.Add(PrimeControlFactory.StatTile("OBJECTIVE",
                rules.ObjectiveTimeGoalSeconds is { } objective
                    ? FormatDuration(objective) : "Default"));
        stats.Children.Add(PrimeControlFactory.StatTile("BOTS",
            lobby.BotCount.ToString(CultureInfo.InvariantCulture)));
        stats.Children.Add(PrimeControlFactory.StatTile("SEATS",
            $"{lobby.PlayerLimit} players / {lobby.ObserverLimit} observers"));
        mission.Children.Add(stats);
        mission.Children.Add(Text(
            $"Seat policy · {FormatWords(lobby.SeatPolicy.ToString())} · waitlist {lobby.Waitlist?.Count ?? 0}",
            "prime-muted"));
        if (owner && lobby.Phase == LobbyPhase.Open)
            mission.Children.Add(Button("Edit Match", () =>
            {
                context.Ui.BeginEdit(lobby);
                context.Ui.EditMatchOpen = true;
                context.Refresh();
            }, quiet: true));
        return Section(mission);
    }

    private static Control BuildRoster(string title, IEnumerable<LobbyMember> members,
        Guid? sessionId, IBrush accent)
    {
        LobbyMember[] list = members.ToArray();
        var content = Stack();
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        TextBlock heading = Text(title, "prime-heading");
        heading.Foreground = accent;
        header.Children.Add(heading);
        header.Children.Add(Text($"{list.Length} occupied", "prime-label"));
        Grid.SetColumn(header.Children[^1], 1);
        content.Children.Add(header);
        foreach (LobbyMember member in list)
        {
            bool isYou = sessionId == member.SessionId;
            string state = member.Observer ? "Observer" : member.Ready ? "Ready" : "Not ready";
            string owner = isYou ? " · You" : "";
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
            var identity = Stack(Text(member.DisplayName, "prime-body"),
                Text($"{FormatWords(member.Hunter.ToString())} · Team {member.Team + 1}{owner}", "prime-muted"));
            line.Children.Add(identity);
            line.Children.Add(new PrimeStatusChip(state,
                member.Ready ? GuiTheme.GoodBrush : member.Observer ? GuiTheme.AccentBrush : GuiTheme.WarmBrush));
            Grid.SetColumn(line.Children[^1], 1);
            Border row = PrimeControlFactory.SelectedRow(line, isYou);
            row.BorderBrush = isYou ? accent : GuiTheme.EdgeBrush;
            row.BorderThickness = new Thickness(3, 1, 1, 1);
            content.Children.Add(row);
        }
        if (list.Length == 0) content.Children.Add(new PrimeEmptyState("No members in this group."));
        return Section(content);
    }

    private static Control BuildMemberControls(PlayPresentationContext context, LobbySnapshot lobby,
        LobbyMember? current)
    {
        var content = Stack(Text("YOUR LOADOUT", "prime-heading"));
        if (current is null)
        {
            content.Children.Add(Text("You are not occupying a player or observer seat in this lobby.",
                "prime-muted"));
            return Section(content);
        }

        var hunter = new ComboBox
        {
            ItemsSource = Enum.GetValues<Hunter>().Where(value => value <= Hunter.Weavel).ToArray(),
            SelectedItem = current.Hunter,
            Tag = ControllerComboSelector.Hunter,
            MinWidth = 220,
            MinHeight = 44,
            IsEnabled = !current.Observer && lobby.Phase == LobbyPhase.Open
        };
        var selection = new DeferredControllerSelection<Hunter>(current.Hunter, selected =>
            context.RunCommand("Select hunter", () => context.Controller.SelectLobbyHunterAsync(
                selected, context.CancellationToken)));
        context.TrackHunter(hunter, selection);
        hunter.SelectionChanged += (_, _) =>
        {
            if (hunter.SelectedItem is not Hunter selected) return;
            if (selection.Active) selection.Preview(selected);
            else if (selected != current.Hunter) selection.CommitImmediate(selected);
        };
        TrackEditor(context, hunter);
        content.Children.Add(Field("Hunter", hunter, 260));

        if (lobby.Mode.IsTeamMode() && !current.Observer && lobby.Phase == LobbyPhase.Open)
        {
            var teamActions = new WrapPanel { Orientation = Orientation.Horizontal };
            teamActions.Children.Add(Button("Request Team 1", () => context.RunCommand("Request Team 1",
                () => context.Controller.RequestTeamAsync(0, context.CancellationToken)),
                primary: current.Team == 0));
            teamActions.Children.Add(Button("Request Team 2", () => context.RunCommand("Request Team 2",
                () => context.Controller.RequestTeamAsync(1, context.CancellationToken)),
                primary: current.Team == 1));
            content.Children.Add(Text($"Authoritative team · Team {current.Team + 1}. A request can be rejected or applied after the current revision.",
                "prime-muted"));
            content.Children.Add(teamActions);
        }
        return Section(content);
    }

    private static Control BuildQueuePanel(PlayPresentationContext context, LobbySnapshot lobby,
        LobbyMember? current, LobbyWaitlistSnapshot waitlist)
    {
        var content = Stack(Text("PLAYER SEAT QUEUE", "prime-heading"));
        if (waitlist.SelfOffer is { } offer)
        {
            Guid offerId = offer.OfferId;
            content.Children.Add(new SeatOfferCard(offer,
                () => RunOfferAction(context, "Accept seat", offerId, () =>
                    context.Controller.AcceptWaitlistAsync(
                        lobby.LobbyId, lobby.Revision, offerId, context.CancellationToken)),
                () => RunOfferAction(context, "Decline seat", offerId, () =>
                    context.Controller.DeclineWaitlistAsync(
                        lobby.LobbyId, lobby.Revision, offerId, context.CancellationToken))));
            return Section(content);
        }

        if (waitlist.IsSelfQueued)
        {
            LobbyQueueEntrySummary? self = waitlist.SelfQueueSequence is { } sequence
                ? waitlist.Entries.FirstOrDefault(entry => entry.QueueSequence == sequence)
                : null;
            string position = self is { } entry ? $"Position {entry.Position}" : "Position pending server update";
            string role = current?.Observer == true ? "Spectating · " : "";
            content.Children.Add(Text($"{role}Waiting for a player seat · {position}", "prime-body"));
            content.Children.Add(Text($"Queue state · {FormatQueueState(waitlist.SelfState)} · {waitlist.Count} total waiting",
                "prime-muted"));
            content.Children.Add(Button("Leave waitlist", () => context.RunCommand("Leave waitlist",
                () => context.Controller.LeaveWaitlistAsync(lobby.LobbyId, lobby.Revision,
                    context.CancellationToken)), quiet: true));
            return Section(content);
        }

        int players = lobby.Members.Count(member => !member.Observer);
        int open = Math.Max(0, lobby.PlayerLimit - players - lobby.BotCount);
        if (current is { Observer: true } && lobby.Phase == LobbyPhase.Open && open == 0)
        {
            content.Children.Add(Text("You are observing. The player roster is full; request the server-owned waitlist if you want a player seat.",
                "prime-muted"));
            content.Children.Add(Button("Join player waitlist", () => context.RunCommand("Join waitlist",
                () => context.Controller.JoinWaitlistAsync(lobby.LobbyId, lobby.Revision,
                    LobbyQueueRequestedRole.Player, null, context.CancellationToken)), primary: true));
        }
        else
        {
            content.Children.Add(Text("No queue action is available for your current authoritative seat.", "prime-muted"));
        }
        return Section(content);
    }

    private static void RunOfferAction(PlayPresentationContext context, string operation,
        Guid offerId, Func<Task> action)
    {
        if (!context.Ui.TryBeginOfferAction(offerId)) return;
        context.RunCommand(operation, () => ExecuteOfferActionAsync(context.Ui, offerId,
            action, context.PostUi, context.Refresh));
    }

    /// <summary>
    /// Completes the one-shot offer guard on the presentation thread. The
    /// controller request is intentionally not retried: a failed request is
    /// released for an explicit user retry after the refreshed card appears.
    /// </summary>
    internal static async Task ExecuteOfferActionAsync(PlayPresentationState ui,
        Guid offerId, Func<Task> action, Action<Action> postUi, Action refresh)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(postUi);
        ArgumentNullException.ThrowIfNull(refresh);
        try
        {
            await action().ConfigureAwait(false);
        }
        catch
        {
            postUi(() =>
            {
                ui.ReleaseOfferAction(offerId);
                refresh();
            });
            throw;
        }
        postUi(() =>
        {
            ui.CompleteOfferAction(offerId);
            refresh();
        });
    }

    private static Control BuildLobbyActions(PlayPresentationContext context, LobbySnapshot lobby,
        LobbyMember? current, bool owner, PlayState state)
    {
        var content = Stack(Text("LOBBY ACTIONS", "prime-heading"));
        LobbyStartEligibility eligibility = LobbyStartEligibility.Evaluate(lobby);
        if (current is { Observer: false } && lobby.Phase == LobbyPhase.Open)
        {
            bool ready = current.Ready;
            content.Children.Add(Button(ready ? "Not Ready" : "Ready", () => context.RunCommand(
                ready ? "Clear ready" : "Set ready",
                () => context.Controller.SetReadyAsync(!ready, context.CancellationToken)), primary: !ready));
        }
        if (owner && lobby.Phase == LobbyPhase.Open)
        {
            content.Children.Add(Text(eligibility.CanStart
                ? "All start conditions are satisfied."
                : eligibility.Message, eligibility.CanStart ? "prime-muted" : "prime-body"));
            AvaloniaButton start = Button("Start Match", () => context.RunCommand("Start match",
                () => context.Controller.StartMatchAsync(context.CancellationToken)), primary: true);
            start.IsEnabled = eligibility.CanStart;
            content.Children.Add(start);
        }
        if (state.Handoff is not null)
        {
            content.Children.Add(Text("The Node has authorized a gameplay connection. Rejoin is available if the Worker link was interrupted.",
                "prime-muted"));
            content.Children.Add(Button("Rejoin match", () => context.RunCommand("Rejoin match",
                () => context.Controller.RejoinWorkerAsync(context.CancellationToken)), primary: true));
            content.Children.Add(Button("Retry connection", () => context.RunCommand("Retry connection",
                async () => { await context.Controller.RetryHandoffAsync(context.CancellationToken); }), quiet: true));
        }
        if (lobby.Phase == LobbyPhase.PostMatch && owner)
            content.Children.Add(Button("Return to lobby", () => context.RunCommand("Return to lobby",
                () => context.Controller.ReturnToLobbyAsync(context.CancellationToken)), primary: true));
        bool requiresConfirmation = RequiresLeaveConfirmation(lobby.Phase, state.Handoff is not null);
        if (!requiresConfirmation)
            context.Ui.CancelLeaveConfirmation(lobby.LobbyId);
        if (!requiresConfirmation)
        {
            content.Children.Add(Button("Leave lobby", () => context.RunCommand("Leave lobby",
                () => context.Controller.LeaveLobbyAsync(context.CancellationToken)), quiet: true));
        }
        else if (!context.Ui.IsLeaveConfirmationOpen(lobby.LobbyId))
        {
            content.Children.Add(Text(
                "Leaving now may abandon a recoverable match connection or the current round.",
                "prime-muted"));
            content.Children.Add(Button("Leave lobby…", () =>
            {
                context.Ui.RequestLeaveConfirmation(lobby.LobbyId);
                context.Refresh();
            }, quiet: true));
        }
        else
        {
            content.Children.Add(StatusCard(
                "Leave this lobby? Rejoin and recovery options will no longer be available here.",
                GuiTheme.WarmBrush));
            var confirmation = new WrapPanel { Orientation = Orientation.Horizontal };
            confirmation.Children.Add(Button("Confirm leave lobby", () =>
            {
                if (!context.Ui.TryConfirmLeave(lobby.LobbyId)) return;
                context.RunCommand("Leave lobby", () => context.Controller.LeaveLobbyAsync(
                    context.CancellationToken));
            }, primary: true));
            confirmation.Children.Add(Button("Stay in lobby", () =>
            {
                context.Ui.CancelLeaveConfirmation(lobby.LobbyId);
                context.Refresh();
            }, quiet: true));
            content.Children.Add(confirmation);
        }
        return Section(content);
    }

    internal static bool RequiresLeaveConfirmation(LobbyPhase phase, bool hasHandoff)
        => phase != LobbyPhase.Open || hasHandoff;

    private static void SendChat(PlayPresentationContext context, string text)
    {
        string bounded = Utf8TextLimit.Truncate(text, 256);
        if (string.IsNullOrWhiteSpace(bounded)) return;
        context.RunCommand("Send message", async () =>
        {
            await context.Controller.SendLobbyChatAsync(bounded, context.CancellationToken)
                .ConfigureAwait(false);
            context.PostUi(() =>
            {
                context.Ui.ClearChatDraft();
                context.Refresh();
            });
        });
    }

    private static PrimeMatchCard CreateMatchCard(PlayPresentationContext context, LobbyListEntry entry)
    {
        Action? join = MatchBrowserFiltering.IsPlayerJoinable(entry)
            ? () => context.RunCommand($"Join {entry.Name}", () => context.Controller.JoinLobbyAsync(
                entry.LobbyId, entry.Revision, observer: false, context.CancellationToken))
            : null;
        Action? waitlist = MatchBrowserFiltering.IsWaitlistJoinable(entry)
            ? () => context.RunCommand($"Join waitlist for {entry.Name}",
                () => context.Controller.JoinWaitlistAsync(entry.LobbyId, entry.Revision,
                    LobbyQueueRequestedRole.Player, null, context.CancellationToken))
            : null;
        Action? spectate = MatchBrowserFiltering.IsSpectatable(entry)
            ? () => context.RunCommand($"Spectate {entry.Name}", () => context.Controller.JoinObserverAsync(
                entry.LobbyId, entry.Revision, context.CancellationToken))
            : null;
        return new PrimeMatchCard(entry, join, waitlist, spectate);
    }

    private static StackPanel Page(string title, string kicker, string subtitle)
    {
        var root = new StackPanel { Spacing = 14, HorizontalAlignment = HorizontalAlignment.Stretch };
        root.Children.Add(PrimeControlFactory.PageHeading(title, kicker, subtitle));
        return root;
    }

    private static PrimeSectionPanel Section(Control child)
        => PrimeControlFactory.SectionPanel(child);

    private static Control StepRail(int step)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new PrimeStatusChip(step == 1 ? "1 · Lobby seats" : "✓ · Lobby seats",
            step == 1 ? GuiTheme.AccentBrush : GuiTheme.GoodBrush));
        row.Children.Add(new PrimeStatusChip("2 · Mission and rules",
            step == 2 ? GuiTheme.AccentBrush : GuiTheme.EdgeBrush));
        return row;
    }

    private static StackPanel Stack(params Control[] controls)
    {
        var stack = new StackPanel { Spacing = 8 };
        foreach (Control control in controls) stack.Children.Add(control);
        return stack;
    }

    private static TextBlock Text(string value, string? classes = null)
    {
        var text = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap };
        if (!string.IsNullOrWhiteSpace(classes)) text.Classes.Add(classes);
        return text;
    }

    private static TextBox Editor(string value, string watermark)
    {
        var editor = new TextBox
        {
            Text = value,
            Watermark = watermark,
            MinHeight = 44,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        editor.Classes.Add("prime-input");
        return editor;
    }

    private static ComboBox Combo(object[] values, object? selected)
        => new()
        {
            ItemsSource = values,
            SelectedItem = selected,
            MinHeight = 44,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };

    private static CheckBox Check(string label, bool value)
        => new() { Content = label, IsChecked = value, MinHeight = 44,
            Margin = new Thickness(0, 0, 16, 0) };

    private static Control Field(string label, Control editor, double width)
    {
        var field = Stack(Text(label, "prime-label"), editor);
        field.Width = width;
        field.Margin = new Thickness(0, 0, 12, 8);
        return field;
    }

    private static AvaloniaButton Button(string label, Action action, bool primary = false,
        bool quiet = false)
    {
        AvaloniaButton button = PrimeControlFactory.Button(label, action, primary, quiet);
        button.MinHeight = 44;
        button.Margin = new Thickness(0, 0, 8, 8);
        return button;
    }

    private static Control StatusCard(string message, IBrush accent)
    {
        Border card = Section(Stack(Text(message, "prime-body")));
        card.BorderBrush = accent;
        card.BorderThickness = new Thickness(3, 1, 1, 1);
        return card;
    }

    private static Grid ResponsiveColumns(Control left, Control right)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowDefinitions = new RowDefinitions("Auto"),
            ColumnSpacing = 12
        };
        grid.Children.Add(left);
        grid.Children.Add(right);
        Grid.SetColumn(right, 1);
        grid.SizeChanged += (_, args) =>
        {
            bool narrow = args.NewSize.Width > 0 && args.NewSize.Width < 720;
            grid.ColumnDefinitions = new ColumnDefinitions(narrow ? "*" : "*,*");
            grid.RowDefinitions = new RowDefinitions(narrow ? "Auto,Auto" : "Auto");
            grid.RowSpacing = narrow ? 12 : 0;
            Grid.SetColumn(left, 0);
            Grid.SetColumn(right, narrow ? 0 : 1);
            Grid.SetRow(left, 0);
            Grid.SetRow(right, narrow ? 1 : 0);
        };
        return grid;
    }

    private static void TrackEditor(PlayPresentationContext context, Control editor)
        => editor.LostFocus += (_, _) => context.EditorLostFocus();

    private static string DisplayMap(string map)
        => string.IsNullOrWhiteSpace(map) ? "MAP NOT CONFIGURED"
            : map.Replace('_', ' ').ToUpperInvariant();

    private static string FormatWords(string value)
        => System.Text.RegularExpressions.Regex.Replace(value, "(?<!^)([A-Z])", " $1");

    private static string FormatDuration(int seconds)
        => TimeSpan.FromSeconds(Math.Max(0, seconds))
            .ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");

    private static string FormatQueueState(LobbyQueueEntryState? state)
        => state switch
        {
            LobbyQueueEntryState.Queued => "Queued",
            LobbyQueueEntryState.SeatOffered => "Seat offered",
            LobbyQueueEntryState.Promoted => "Promoted",
            LobbyQueueEntryState.Expired => "Expired",
            LobbyQueueEntryState.Cancelled => "Cancelled",
            _ => "Unknown"
        };

    private static string SortLabel(MatchBrowserSort sort) => sort switch
    {
        MatchBrowserSort.MostPlayers => "Most players",
        MatchBrowserSort.MostOpenSlots => "Most open slots",
        MatchBrowserSort.Name => "Name",
        _ => "Recommended"
    };

    private sealed record BoolChoice(string Label, bool? Value)
    {
        public override string ToString() => Label;
    }

    private sealed record DamageChoice(string Label, int? Value)
    {
        public override string ToString() => Label;
    }

    private sealed record ModeChoice(MatchMode Value, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record SeatPolicyChoice(LobbySeatPolicy Value, string Label)
    {
        public override string ToString() => Label;
    }
}
