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
using ProjectPrime.Server.Shared;
using MphRead.Mods.Accounts;
using MphRead.Mods;
using MphRead.Mods.Network;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Presentation;
using MphRead.Mods.Launcher.Theme;
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
    bool ExpandAdvancedNetwork = false,
    Action? OpenNetworkSettings = null,
    bool SeatOffersHandledExternally = false,
    Action<LobbyChatPanel?>? TrackChatPanel = null);

/// <summary>Native Avalonia presentation for the Play route.</summary>
internal static class PlayPresentation
{
    private const int MaxDisplayedLobbies = 1024;

    public static Control Build(PlayPresentationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.TrackChatPanel?.Invoke(null);
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
        context.Ui.ResetChatPresentation();
        return context.Ui.Subsection switch
        {
            PlaySubsection.Browser => BuildBrowser(context),
            PlaySubsection.HostMatch => BuildHostMatch(context),
            _ => BuildHome(context)
        };
    }

    private static Control BuildSignedOut(PlayPresentationContext context)
    {
        context.Ui.ResetChatPresentation();
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

        AvaloniaButton quickPlay = Button("Quick Play", () =>
        {
            context.Ui.Subsection = PlaySubsection.Home;
            context.RunCommand("Quick Play", () => context.Controller.QuickPlayAsync(context.CancellationToken));
        }, primary: true);
        quickPlay.IsEnabled = !state.Loading;
        var quickPlayHero = new PrimeCard(Stack(
            Text("QUICK PLAY", "prime-kicker"),
            Text("Find the best available open match.", "prime-heading"),
            quickPlay));
        quickPlayHero.BorderBrush = GuiTheme.BrandBrush;
        quickPlayHero.BorderThickness = new Thickness(3, 1, 1, 1);
        root.Children.Add(quickPlayHero);

        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
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
                Text("Quick Play is checking the current open lobbies. Your match connection stays available while this runs.",
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
                Text("These cards use the latest match directory. Open Browse Matches for filters and all actions.",
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
                "No match directory loaded yet. Choose Browse Matches to look for open games.")));
        }

        PrimeSectionPanel networkPanel = Section(BuildNetwork(context));
        networkPanel.Classes.Add("prime-tech");
        root.Children.Add(new Expander
        {
            Header = Text("Advanced Network", "prime-tech"),
            IsExpanded = context.ExpandAdvancedNetwork,
            Content = networkPanel
        });
        return root;
    }

    private static Control BuildNetwork(PlayPresentationContext context)
    {
        PlayState state = context.State;
        var network = Stack(
            Text("Connection", "prime-heading"),
            Text(state.Node?.Session is not null
                ? "Connected"
                : "Disconnected", "prime-muted"));

        network.Children.Add(Text("Server search region", "prime-label"));
        network.Children.Add(Text(PreferredRegionLabel(context.Controller.PreferredRegion), "prime-body"));
        network.Children.Add(Text("Change this choice in Settings. Match cards only show details supplied by the match directory.",
            "prime-muted"));
        if (context.OpenNetworkSettings is { } openNetworkSettings)
            network.Children.Add(Button("Change in Settings", openNetworkSettings, quiet: true));
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
            "Choose a player seat, spectate an open lobby, or join the player queue when it is full.");
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
                var empty = new PrimeEmptyState(snapshot is null
                    ? "Choose Refresh matches to load the current match directory."
                    : "No matches match these filters. Clear a filter or refresh the directory.");
                if (context.Ui.Filters != new MatchBrowserFilters())
                    empty.Children.Add(Button("Clear filters", () =>
                    {
                        context.Ui.ClearFilters();
                        RebuildCards();
                    }, quiet: true));
                empty.Children.Add(Button("Refresh matches", () => context.RunCommand(
                    "Refresh matches", () => context.Controller.BrowseLobbiesAsync(
                        context.CancellationToken)), primary: true));
                cardsHost.Children.Add(empty);
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
                $"Showing {source.Length} matches.", "prime-muted"),
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
            .Select(value => new ModeChoice(value, PrimeGameText.ModeLabel(value))).ToArray();
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

        MapChoice[] mapChoices = entries
            .Select(entry => entry.MapKey)
            .Concat(context.Ui.Filters.MapKey is { } filterMap
                ? new[] { filterMap } : Array.Empty<string>())
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => PrimeGameText.MapName(key), StringComparer.Ordinal)
            .Select(key => new MapChoice(key, PrimeGameText.MapName(key))).ToArray();
        object[] mapValues = new object[] { "Any" }
            .Concat(mapChoices.Cast<object>()).ToArray();
        object selectedMap = context.Ui.Filters.MapKey is { } selectedMapKey
            ? mapChoices.First(choice => StringComparer.Ordinal.Equals(
                choice.Key, selectedMapKey))
            : "Any";
        ComboBox map = Combo(mapValues, selectedMap);
        map.SelectionChanged += (_, _) =>
        {
            context.Ui.SetFilters(context.Ui.Filters with
            {
                MapKey = map.SelectedItem is MapChoice value ? value.Key : null
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
        content.Children.Add(Text("Match cards show details supplied by the match directory. Connection quality is not estimated.",
            "prime-muted"));
        return Section(content);
    }

    private static Control BuildHostMatch(PlayPresentationContext context)
    {
        HostMatchDraft draft = context.Ui.HostDraft;
        var dashboard = new PrimePlayResponsivePanel(wideLeftWeight: 5,
            wideRightWeight: 7)
        {
            CurrentStep = context.Ui.HostStep,
            Spacing = 14
        };
        dashboard.Classes.Add("prime-host-layout");

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 12
        };
        header.Children.Add(PrimeControlFactory.PageHeading("Host Match",
            "NEW MULTIPLAYER LOBBY",
            "Create and configure a multiplayer lobby."));
        AvaloniaButton backToPlay = Button("Back to Play", () =>
        {
            context.Ui.Subsection = PlaySubsection.Home;
            context.Refresh();
        }, quiet: true);
        header.Children.Add(backToPlay);
        Grid.SetColumn(backToPlay, 1);
        AddDashboardChild(dashboard, header, PrimePlayLane.Full, 0);

        WrapPanel stepRail = StepRail(context.Ui.HostStep);
        PrimePlayResponsivePanel.SetHideInWide(stepRail, true);
        AddDashboardChild(dashboard, stepRail, PrimePlayLane.Full, 1);
        dashboard.CurrentStepChanged += step =>
        {
            context.Ui.HostStep = step;
            UpdateStepRail(stepRail, step);
        };

        var name = Editor(draft.Name, "Lobby name");
        name.MaxLength = 64;
        name.TextChanged += (_, _) => draft.Name = name.Text ?? "";
        TrackEditor(context, name);

        ComboBox players = Combo(Enumerable.Range(1, 8).Cast<object>().ToArray(),
            draft.PlayerLimit);
        ComboBox observers = Combo(Enumerable.Range(0, 17).Cast<object>().ToArray(),
            draft.ObserverLimit);
        ComboBox bots = Combo(Array.Empty<object>(), draft.BotCount);
        void UpdateBots()
        {
            int maximumBots = Math.Max(0, draft.PlayerLimit - 1);
            draft.BotCount = Math.Clamp(draft.BotCount, 0, maximumBots);
            bots.ItemsSource = Enumerable.Range(0, maximumBots + 1)
                .Cast<object>().ToArray();
            bots.SelectedItem = draft.BotCount;
        }
        players.SelectionChanged += (_, _) =>
        {
            if (players.SelectedItem is not int value) return;
            draft.PlayerLimit = value;
            UpdateBots();
        };
        observers.SelectionChanged += (_, _) =>
        {
            if (observers.SelectedItem is int value) draft.ObserverLimit = value;
        };
        bots.SelectionChanged += (_, _) =>
        {
            if (bots.SelectedItem is int value) draft.BotCount = value;
        };
        UpdateBots();
        TrackEditor(context, players);
        TrackEditor(context, observers);
        TrackEditor(context, bots);

        SeatPolicyChoice[] policies = Enum.GetValues<LobbySeatPolicy>()
            .Select(value => new SeatPolicyChoice(value,
                PrimeGameText.SeatPolicyLabel(value))).ToArray();
        ComboBox seatPolicy = Combo(policies.Cast<object>().ToArray(),
            policies.First(policy => policy.Value == draft.SeatPolicy));
        seatPolicy.SelectionChanged += (_, _) =>
        {
            if (seatPolicy.SelectedItem is SeatPolicyChoice value)
                draft.SeatPolicy = value.Value;
        };
        TrackEditor(context, seatPolicy);

        var identityFields = new PrimeFieldGrid(maximumColumns: 2,
            minimumColumnWidth: 170);
        identityFields.Children.Add(Field("Match name", name));
        identityFields.Children.Add(Field("Seat policy", seatPolicy));
        var seatFields = new PrimeFieldGrid(maximumColumns: 3,
            minimumColumnWidth: 110);
        seatFields.Children.Add(Field("Player seats", players));
        seatFields.Children.Add(Field("Observer seats", observers));
        seatFields.Children.Add(Field("Bots", bots));
        Control identity = Stack(
            Text("LOBBY & SEATS", "prime-kicker"),
            identityFields,
            seatFields);
        AddDashboardChild(dashboard, identity, PrimePlayLane.Left, 2,
            PrimePlayStage.One);

        string[] maps = context.Controller.AvailableMaps.ToArray();
        if (draft.MapKey.Length == 0 && maps.Length > 0) draft.MapKey = maps[0];
        MapChoice[] mapChoices = maps
            .Concat(draft.MapKey.Length > 0 ? new[] { draft.MapKey } : Array.Empty<string>())
            .Distinct(StringComparer.Ordinal)
            .Select(value => new MapChoice(value, PrimeGameText.MapName(value))).ToArray();
        ComboBox map = Combo(mapChoices.Cast<object>().ToArray(),
            mapChoices.FirstOrDefault(choice => StringComparer.Ordinal.Equals(
                choice.Key, draft.MapKey)));
        TrackEditor(context, map);

        ModeChoice[] modes = Enum.GetValues<MatchMode>()
            .Select(value => new ModeChoice(value, PrimeGameText.ModeLabel(value))).ToArray();
        ComboBox mode = Combo(modes.Cast<object>().ToArray(),
            modes.First(choice => choice.Value == draft.Mode));
        TrackEditor(context, mode);

        var mapSummary = Text(PrimeGameText.MapName(draft.MapKey), "prime-heading");
        var modeSummary = Text(PrimeGameText.ModeLabel(draft.Mode), "prime-muted");
        var previewHost = new Border
        {
            Child = BuildMapPreview(context, draft.MapKey, 250),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Control mission = Stack(
            Text("MISSION", "prime-kicker"), mapSummary, modeSummary, previewHost);
        AddDashboardChild(dashboard, mission, PrimePlayLane.Left, 3,
            PrimePlayStage.Two);

        var rulesHost = new Border();
        void RebuildRules() => rulesHost.Child = BuildRuleControls(context, draft);
        map.SelectionChanged += (_, _) =>
        {
            if (map.SelectedItem is not MapChoice choice) return;
            draft.MapKey = choice.Key;
            mapSummary.Text = PrimeGameText.MapName(choice.Key);
            Control nextPreview = BuildMapPreview(context, choice.Key, 250);
            nextPreview.Height = HostPreviewHeight(dashboard.Layout,
                dashboard.ViewportSize.Height);
            previewHost.Child = nextPreview;
        };
        mode.SelectionChanged += (_, _) =>
        {
            if (mode.SelectedItem is not ModeChoice value) return;
            draft.Mode = value.Value;
            modeSummary.Text = PrimeGameText.ModeLabel(value.Value);
            RebuildRules();
        };
        RebuildRules();

        var missionFields = new PrimeFieldGrid(maximumColumns: 2,
            minimumColumnWidth: 190);
        missionFields.Children.Add(Field("Map", map));
        missionFields.Children.Add(Field("Mode", mode));
        var configuration = Stack(
            Text("MATCH CONFIGURATION", "prime-heading"), missionFields);
        if (maps.Length == 0)
            configuration.Children.Add(StatusCard(context.Controller.MapCatalogMessage,
                GuiTheme.WarmBrush));
        configuration.Children.Add(rulesHost);
        var createRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        AvaloniaButton create = Button("Create Match", () =>
        {
            if (string.IsNullOrWhiteSpace(draft.Name))
            {
                context.Notify(PrimeNotificationKind.Error,
                    "Enter a match name before creating the lobby.");
                return;
            }
            if (!draft.TryBuildRules(out _, out string error))
            {
                context.Notify(PrimeNotificationKind.Error, error);
                return;
            }
            context.RunCommand("Host match", () => context.HostMatch(draft));
        }, primary: true);
        create.MinWidth = 180;
        createRow.Children.Add(create);
        Grid.SetColumn(create, 1);
        configuration.Children.Add(createRow);
        AddDashboardChild(dashboard, Section(configuration), PrimePlayLane.Right, 4,
            PrimePlayStage.Two);

        AvaloniaButton next = Button("Next: mission and rules", () =>
        {
            if (string.IsNullOrWhiteSpace(draft.Name))
            {
                context.Notify(PrimeNotificationKind.Error,
                    "Enter a match name before continuing.");
                return;
            }
            dashboard.CurrentStep = 2;
        }, primary: true);
        PrimePlayResponsivePanel.SetHideInWide(next, true);
        AddDashboardChild(dashboard, next, PrimePlayLane.Full, 5,
            PrimePlayStage.One);

        AvaloniaButton previous = Button("Back: lobby and seats", () =>
        {
            dashboard.CurrentStep = 1;
        }, quiet: true);
        PrimePlayResponsivePanel.SetHideInWide(previous, true);
        AddDashboardChild(dashboard, previous, PrimePlayLane.Full, 5,
            PrimePlayStage.Two);

        void ApplyHostSizing()
        {
            if (previewHost.Child is not { } preview) return;
            preview.Height = HostPreviewHeight(dashboard.Layout,
                dashboard.ViewportSize.Height);
        }
        dashboard.LayoutChanged += _ => ApplyHostSizing();
        dashboard.ViewportChanged += _ => ApplyHostSizing();

        return dashboard;
    }

    private static Control BuildEditMatch(PlayPresentationContext context, LobbySnapshot lobby,
        HostMatchDraft draft)
    {
        var root = Page("Edit Match", "OWNER MATCH SETTINGS",
            "Update the mission and rules for this match. Changes take effect when the lobby confirms them.");
        root.Children.Add(Button("Back to lobby", () =>
        {
            context.Ui.ClearEdit();
            context.Refresh();
        }, quiet: true));
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
            }, cancelLabel: "Cancel"));
        return root;
    }

    private static Control BuildMatchConfiguration(PlayPresentationContext context, HostMatchDraft draft,
        string submitLabel, Action cancel, Action submit, string cancelLabel = "Back")
    {
        string[] maps = context.Controller.AvailableMaps.ToArray();
        if (draft.MapKey.Length == 0 && maps.Length > 0) draft.MapKey = maps[0];
        MapChoice[] mapChoices = maps
            .Concat(draft.MapKey.Length > 0 ? new[] { draft.MapKey } : Array.Empty<string>())
            .Distinct(StringComparer.Ordinal)
            .Select(value => new MapChoice(value, PrimeGameText.MapName(value))).ToArray();
        var map = Combo(mapChoices.Cast<object>().ToArray(),
            mapChoices.FirstOrDefault(choice => StringComparer.Ordinal.Equals(
                choice.Key, draft.MapKey)));
        map.SelectionChanged += (_, _) => draft.MapKey =
            map.SelectedItem is MapChoice choice ? choice.Key : "";
        TrackEditor(context, map);

        ModeChoice[] modes = Enum.GetValues<MatchMode>()
            .Select(value => new ModeChoice(value, PrimeGameText.ModeLabel(value))).ToArray();
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
        actions.Children.Add(Button(cancelLabel, cancel, quiet: true));
        fields.Children.Add(actions);
        return Section(fields);
    }

    private static Control BuildRuleControls(PlayPresentationContext context, HostMatchDraft draft)
    {
        LobbyRuleApplicability applicability = LobbyRuleApplicability.For(draft.Mode);
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(Text("MATCH RULES", "prime-heading"));
        content.Children.Add(Text("Only controls applicable to the selected mode are shown. Default uses the match mode default.",
            "prime-muted"));
        var fields = new PrimeFieldGrid(maximumColumns: 3,
            minimumColumnWidth: 150);

        TextBox time = Editor(draft.TimeLimitText, "Default or m:ss");
        time.TextChanged += (_, _) => draft.TimeLimitText = time.Text ?? "";
        TrackEditor(context, time);
        fields.Children.Add(Field("Time limit", time));

        if (applicability.ScoreGoal)
        {
            TextBox score = Editor(draft.ScoreGoalText, "Default or points");
            score.TextChanged += (_, _) => draft.ScoreGoalText = score.Text ?? "";
            TrackEditor(context, score);
            fields.Children.Add(Field(applicability.GoalLabel, score));
        }
        else if (applicability.StartingLives)
        {
            TextBox lives = Editor(draft.StartingLivesText, "Default or lives");
            lives.TextChanged += (_, _) => draft.StartingLivesText = lives.Text ?? "";
            TrackEditor(context, lives);
            fields.Children.Add(Field(applicability.GoalLabel, lives));
        }
        else if (applicability.ObjectiveTimeGoal)
        {
            TextBox objective = Editor(draft.ObjectiveTimeGoalText, "Default or m:ss");
            objective.TextChanged += (_, _) => draft.ObjectiveTimeGoalText = objective.Text ?? "";
            TrackEditor(context, objective);
            fields.Children.Add(Field(applicability.GoalLabel, objective));
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
            fields.Children.Add(Field("Damage level", damage));
        }

        AddBoolControl(fields, context, "Friendly fire", draft.FriendlyFire,
            value => draft.FriendlyFire = value, applicability.FriendlyFire);
        AddBoolControl(fields, context, "Affinity weapons", draft.AffinityWeapons,
            value => draft.AffinityWeapons = value, applicability.AffinityWeapons);
        AddBoolControl(fields, context, "Player radar", draft.PlayerRadar,
            value => draft.PlayerRadar = value, applicability.PlayerRadar);
        AddBoolControl(fields, context, "Octolith reset", draft.OctolithReset,
            value => draft.OctolithReset = value, applicability.OctolithReset);
        content.Children.Add(fields);
        return content;
    }

    private static void AddBoolControl(Panel content, PlayPresentationContext context,
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
        content.Children.Add(Field(label, combo));
    }

    private static Control BuildLobby(PlayPresentationContext context, LobbySnapshot lobby)
    {
        context.Ui.ObserveLobby(lobby.LobbyId);
        context.Ui.ObserveChat(lobby.LobbyId, lobby.Chat);
        PlayState state = context.State;
        Guid? sessionId = state.Node?.Session?.SessionId;
        LobbyMember? current = sessionId is { } id
            ? lobby.Members.FirstOrDefault(member => member.SessionId == id)
            : null;
        bool owner = sessionId is { } ownerSession && lobby.OwnerSessionId == ownerSession;
        LobbyWaitlistSnapshot waitlist = lobby.Waitlist ?? LobbyWaitlistSnapshot.Empty;
        context.Ui.ObserveOffer(waitlist.SelfOffer?.OfferId);

        var dashboard = new PrimePlayResponsivePanel(wideLeftWeight: 7,
            wideRightWeight: 4, compactColumns: true)
        {
            Spacing = 8
        };
        dashboard.Classes.Add("prime-lobby-layout");
        var header = new PrimeFieldGrid(maximumColumns: 2,
            minimumColumnWidth: 360);
        header.Children.Add(PrimeControlFactory.PageHeading(lobby.Name, "ACTIVE LOBBY",
            "Mission first: confirm the arena and rules, then ready when your squad is set."));
        header.Children.Add(BuildLobbyHeader(lobby, current, waitlist));
        AddDashboardChild(dashboard, header, PrimePlayLane.Full, 0);

        if (state.Node?.Session is null || NodeSessions.Current is { Connected: false })
        {
            Control connection = Section(Stack(
                Text("Connection interrupted. Reconnect to keep your place in this lobby.",
                    "prime-body"),
                Button("Reconnect", () => context.RunCommand("Reconnect",
                    () => context.Controller.ResumeAsync(context.CancellationToken)),
                    primary: true)));
            AddDashboardChild(dashboard, connection, PrimePlayLane.Full, 1);
        }

        Control mission = BuildMissionPanel(context, lobby, owner,
            out Control missionPreview);
        AddDashboardChild(dashboard, mission, PrimePlayLane.Left, 2);

        bool teamMode = lobby.Mode.IsTeamMode();
        LobbyMember[] players = lobby.Members.Where(member => !member.Observer).ToArray();
        LobbyMember[] observers = lobby.Members.Where(member => member.Observer).ToArray();
        var rosterGroups = new PrimeFieldGrid(maximumColumns: 2,
            minimumColumnWidth: 280);
        rosterGroups.Children.Add(BuildRoster(teamMode ? "TEAM 1" : "PLAYERS",
            teamMode ? players.Where(member => member.Team == 0) : players,
            sessionId, GuiTheme.WarningBrush, showTeam: teamMode));
        if (teamMode)
            rosterGroups.Children.Add(BuildRoster("TEAM 2",
                players.Where(member => member.Team == 1), sessionId,
                GuiTheme.TechBrush, showTeam: true));
        rosterGroups.Children.Add(BuildRoster("OBSERVERS", observers, sessionId,
            GuiTheme.TechBrush, showTeam: false));
        var rosterScroll = new ScrollViewer
        {
            Content = rosterGroups,
            MaxHeight = 340,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        rosterScroll.Classes.Add("prime-roster-scroll");
        AddDashboardChild(dashboard, rosterScroll, PrimePlayLane.Left, 3);

        Control memberControls = BuildMemberControls(context, lobby, current);
        Control lobbyActions = BuildLobbyActions(context, lobby, current, owner, state);
        StackPanel commandContent = Stack(memberControls, lobbyActions);
        var commandRail = (PrimeSectionPanel)Section(commandContent);
        AddDashboardChild(dashboard, commandRail, PrimePlayLane.Right, 4);
        Control? queue = BuildQueuePanel(context, lobby, current, waitlist);
        if (queue is not null)
            AddDashboardChild(dashboard, queue, PrimePlayLane.Right, 5);
        var chat = new LobbyChatPanel(lobby.Chat, context.Ui.ChatDraft,
            context.Ui.SetChatDraft,
            text => SendChat(context, text),
            context.Ui.SetChatEditing,
            context.Ui.ChatScrollOffset,
            context.Ui.ChatScrollPositionKnown,
            context.Ui.SetChatScrollOffset,
            context.Ui.ChatUnreadCount,
            context.Ui.MarkChatRead,
            sessionId);
        context.TrackChatPanel?.Invoke(chat);
        TrackEditor(context, chat.DraftEditor);
        AddDashboardChild(dashboard, chat, PrimePlayLane.Right, 6);
        void ApplyLobbySizing()
        {
            double viewportHeight = dashboard.ViewportSize.Height;
            missionPreview.Height = LobbyPreviewHeight(dashboard.Layout,
                viewportHeight);
            rosterScroll.MaxHeight = LobbyRosterHeight(dashboard.Layout,
                viewportHeight);
            chat.HistoryMaxHeight = LobbyChatHeight(dashboard.Layout,
                viewportHeight);
            bool dense = dashboard.Layout == PrimeContentLayout.Wide;
            commandRail.Padding = new Thickness(dense ? 4 : 16);
            commandRail.Margin = dense ? default : new Thickness(0, 0, 0, 12);
            commandContent.Spacing = dense ? 4 : 8;
            if (memberControls is StackPanel memberStack)
                memberStack.Spacing = dense ? 6 : 8;
            if (lobbyActions is StackPanel actionStack)
                actionStack.Spacing = dense ? 6 : 8;
            chat.CompactChrome = dense;
        }
        dashboard.LayoutChanged += _ => ApplyLobbySizing();
        dashboard.ViewportChanged += _ => ApplyLobbySizing();
        return dashboard;
    }

    private static Control BuildLobbyHeader(LobbySnapshot lobby, LobbyMember? current,
        LobbyWaitlistSnapshot waitlist)
    {
        int players = lobby.Members.Count(member => !member.Observer);
        int observers = lobby.Members.Count(member => member.Observer);
        int openPlayers = Math.Max(0, lobby.PlayerLimit - players - lobby.BotCount);
        var content = new StackPanel { Spacing = 5 };
        content.Children.Add(Text(PrimeGameText.LobbyPhaseLabel(lobby.Phase)
            .ToUpperInvariant(), "prime-kicker"));
        content.Children.Add(Text(
            $"{players + lobby.BotCount}/{lobby.PlayerLimit} player seats · {observers}/{lobby.ObserverLimit} observers · {openPlayers} open player seats",
            "prime-body"));
        if (current is not null)
        {
            if (current.Observer)
                content.Children.Add(Text("You are observing this lobby.", "prime-muted"));
            else if (current.Ready)
                content.Children.Add(Text("Ready.", "prime-body"));
            else
            {
                content.Children.Add(Text("Not ready.", "prime-body"));
                content.Children.Add(Text("Choose your Hunter, then Ready.", "prime-muted"));
            }
        }
        else if (waitlist.IsSelfQueued)
            content.Children.Add(Text("You are waiting for a player seat.",
                "prime-muted"));
        return Section(content);
    }

    private static Control BuildMissionPanel(PlayPresentationContext context,
        LobbySnapshot lobby, bool owner, out Control preview)
    {
        string? mapPath = !string.IsNullOrWhiteSpace(lobby.MapKey)
            && ThumbnailGenerator.Exists(lobby.MapKey)
            ? ThumbnailGenerator.PathFor(lobby.MapKey) : null;
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 12
        };
        header.Children.Add(Text("MISSION", "prime-kicker"));
        if (owner && lobby.Phase == LobbyPhase.Open)
        {
            AvaloniaButton edit = Button("Edit Match", () =>
            {
                context.Ui.BeginEdit(lobby);
                context.Ui.EditMatchOpen = true;
                context.Refresh();
            }, quiet: true);
            header.Children.Add(edit);
            Grid.SetColumn(edit, 1);
        }
        preview = context.BuildPreview(mapPath, 260,
            "No local map preview is available for this arena.");
        var mission = Stack(
            header,
            Text(PrimeGameText.MapName(lobby.MapKey), "prime-heading"));
        var stats = new PrimeFieldGrid(maximumColumns: 5,
            minimumColumnWidth: 110)
        {
            ColumnSpacing = 8,
            RowSpacing = 8
        };
        LobbyRulesOptions rules = LobbyRuleApplicability.RulesFromLobby(lobby);
        LobbyRuleApplicability applicability = LobbyRuleApplicability.For(lobby.Mode);
        PrimeStatTile modeTile = PrimeControlFactory.StatTile("MODE",
            PrimeGameText.ModeLabel(lobby.Mode));
        if (modeTile.Child is StackPanel modeContent
            && modeContent.Children.OfType<TextBlock>().Skip(1).FirstOrDefault() is { } modeValue)
        {
            // Mission stats share the narrow half of the desktop card. Keep
            // multi-word modes readable instead of clipping their last word.
            modeValue.FontSize = 18;
            modeValue.TextWrapping = TextWrapping.Wrap;
        }
        stats.Children.Add(modeTile);
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
        stats.Children.Add(PrimeControlFactory.StatTile("PLAYER SEATS",
            lobby.PlayerLimit.ToString(CultureInfo.InvariantCulture)));
        var details = Stack(stats, Text(
            $"{PrimeGameText.SeatPolicyLabel(lobby.SeatPolicy)} · "
            + $"{lobby.ObserverLimit} observer seats · "
            + $"{lobby.Waitlist?.Count ?? 0} waiting",
            "prime-muted"));
        var missionBody = new PrimeFieldGrid(maximumColumns: 2,
            minimumColumnWidth: 260)
        {
            ColumnSpacing = 12,
            RowSpacing = 10
        };
        missionBody.Children.Add(preview);
        missionBody.Children.Add(details);
        mission.Children.Add(missionBody);
        return Section(mission);
    }

    private static Control BuildRoster(string title, IEnumerable<LobbyMember> members,
        Guid? sessionId, IBrush accent, bool showTeam)
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
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
            var identity = Stack(Text(member.DisplayName, "prime-body"),
                Text(LobbyMemberDetail(member, showTeam, isYou), "prime-muted"));
            line.Children.Add(identity);
            line.Children.Add(new PrimeStatusChip(state,
                LobbyMemberStatusBrush(member)));
            Grid.SetColumn(line.Children[^1], 1);
            // Current-player identity uses a narrow brand edge and explicit
            // text; it is not a selected list item and should not tint the row.
            Border row = PrimeControlFactory.SelectedRow(line, selected: false);
            row.BorderBrush = isYou ? GuiTheme.BrandBrush : GuiTheme.GunmetalBrush;
            row.BorderThickness = new Thickness(3, 1, 1, 1);
            content.Children.Add(row);
        }
        if (list.Length == 0) content.Children.Add(new PrimeEmptyState("No members in this group."));
        return Section(content);
    }

    internal static string LobbyMemberDetail(LobbyMember member, bool showTeam,
        bool isCurrent)
    {
        ArgumentNullException.ThrowIfNull(member);
        var details = new List<string> { PrimeGameText.HunterLabel(member.Hunter) };
        if (showTeam && !member.Observer)
            details.Add($"Team {member.Team + 1}");
        if (isCurrent) details.Add("YOU");
        return String.Join(" · ", details);
    }

    internal static IBrush LobbyMemberStatusBrush(LobbyMember member)
    {
        ArgumentNullException.ThrowIfNull(member);
        return member.Observer ? GuiTheme.TechBrush
            : member.Ready ? GuiTheme.SuccessBrush : GuiTheme.WarningBrush;
    }

    private static Control BuildMemberControls(PlayPresentationContext context, LobbySnapshot lobby,
        LobbyMember? current)
    {
        var content = Stack(Text("YOUR LOADOUT", "prime-heading"));
        if (current is null)
        {
            content.Children.Add(Text("You are not occupying a player or observer seat in this lobby.",
                "prime-muted"));
            return content;
        }

        Hunter[] hunterValues = Enum.GetValues<Hunter>()
            .Where(value => value <= Hunter.Weavel).ToArray();
        var hunter = new ComboBox
        {
            ItemsSource = hunterValues,
            SelectedItem = current.Hunter,
            Tag = ControllerComboSelector.Hunter,
            MinWidth = 220,
            MinHeight = 44,
            IsEnabled = !current.Observer && lobby.Phase == LobbyPhase.Open,
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<Hunter>(
                (value, _) => Text(PrimeGameText.HunterLabel(value), "prime-body"))
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
            teamActions.Children.Add(Button("Team 1", () => context.RunCommand("Select Team 1",
                () => context.Controller.RequestTeamAsync(0, context.CancellationToken)),
                primary: current.Team == 0));
            teamActions.Children.Add(Button("Team 2", () => context.RunCommand("Select Team 2",
                () => context.Controller.RequestTeamAsync(1, context.CancellationToken)),
                primary: current.Team == 1));
            content.Children.Add(Text("TEAM", "prime-label"));
            content.Children.Add(teamActions);
        }
        return content;
    }

    private static Control? BuildQueuePanel(PlayPresentationContext context, LobbySnapshot lobby,
        LobbyMember? current, LobbyWaitlistSnapshot waitlist)
    {
        var content = Stack(Text("PLAYER SEAT QUEUE", "prime-heading"));
        if (waitlist.SelfOffer is { } offer)
        {
            Guid offerId = offer.OfferId;
            if (!context.SeatOffersHandledExternally)
            {
                content.Children.Add(new SeatOfferCard(offer,
                    () => RunOfferAction(context, "Accept seat", offerId, () =>
                        context.Controller.AcceptWaitlistAsync(
                            lobby.LobbyId, lobby.Revision, offerId, context.CancellationToken)),
                    () => RunOfferAction(context, "Decline seat", offerId, () =>
                        context.Controller.DeclineWaitlistAsync(
                            lobby.LobbyId, lobby.Revision, offerId, context.CancellationToken))));
            }
            else
            {
                content.Children.Add(Text(
                    "A player seat is ready for you. Respond in the seat offer prompt.",
                    "prime-body"));
                content.Children.Add(Text(
                    "Your place in the player queue is held while you choose.",
                    "prime-muted"));
            }
            return Section(content);
        }

        if (waitlist.IsSelfQueued)
        {
            LobbyQueueEntrySummary? self = waitlist.SelfQueueSequence is { } sequence
                ? waitlist.Entries.FirstOrDefault(entry => entry.QueueSequence == sequence)
                : null;
            string position = self is { } entry ? $"Position {entry.Position}" : "Position updating";
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
            content.Children.Add(Text("You are observing. The player roster is full; join the player queue if you want a player seat.",
                "prime-muted"));
            content.Children.Add(Button("Join player waitlist", () => context.RunCommand("Join waitlist",
                () => context.Controller.JoinWaitlistAsync(lobby.LobbyId, lobby.Revision,
                    LobbyQueueRequestedRole.Player, null, context.CancellationToken)), primary: true));
        }
        else return null;
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
        var content = Stack();
        LobbyStartEligibility eligibility = LobbyStartEligibility.Evaluate(lobby);
        MapRequirement? required = lobby.RequiredMap;
        bool missingRequiredMap = required != null
            && !context.Controller.IsRequiredMapInstalled(required);
        if (missingRequiredMap)
        {
            content.Children.Add(Text(
                $"This server requires {required!.StableId} {required.Version} · "
                    + $"{required.PackageSize / 1024.0:0.#} KB.", "prime-body"));
            content.Children.Add(Button("Download & Prepare", () => context.RunCommand(
                "Download required map",
                () => context.Controller.AcquireRequiredMapAsync(context.CancellationToken)),
                primary: true));
        }
        var primaryActions = new WrapPanel { Orientation = Orientation.Horizontal };
        if (current is { Observer: false } && lobby.Phase == LobbyPhase.Open)
        {
            bool ready = current.Ready;
            AvaloniaButton readyAction = Button(ready ? "Not Ready" : "Ready", () => context.RunCommand(
                ready ? "Clear ready" : "Set ready",
                () => context.Controller.SetReadyAsync(!ready, context.CancellationToken)),
                primary: !ready);
            readyAction.MinWidth = 120;
            readyAction.IsEnabled = !missingRequiredMap && !state.Loading;
            primaryActions.Children.Add(readyAction);
        }
        if (owner && lobby.Phase == LobbyPhase.Open)
        {
            AvaloniaButton start = Button("Start Match", () => context.RunCommand("Start match",
                () => context.Controller.StartMatchAsync(context.CancellationToken)), primary: true);
            start.IsEnabled = eligibility.CanStart;
            start.MinWidth = 180;
            primaryActions.Children.Add(start);
            if (!eligibility.CanStart)
                content.Children.Add(Text(PlayerFacingEligibilityMessage(eligibility),
                    "prime-body"));
        }
        if (primaryActions.Children.Count > 0)
            content.Children.Add(primaryActions);
        if (state.Handoff is not null)
        {
            content.Children.Add(Text("Match connection is ready. Rejoin if the game link was interrupted.",
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
        return content;
    }

    internal static bool RequiresLeaveConfirmation(LobbyPhase phase, bool hasHandoff)
        => phase != LobbyPhase.Open || hasHandoff;

    internal static string PlayerFacingEligibilityMessage(LobbyStartEligibility eligibility)
    {
        if (eligibility.CanStart) return "All start conditions are satisfied.";

        string message = eligibility.Message;
        if (message.Contains("Choose a map", StringComparison.OrdinalIgnoreCase))
            return "Choose a map before starting.";
        if (message.Contains("Add a player", StringComparison.OrdinalIgnoreCase))
            return "Add a player before starting.";
        if (message.Contains("both teams", StringComparison.OrdinalIgnoreCase))
            return "Team mode needs players on both teams.";
        if (message.Contains("All players must be Ready", StringComparison.OrdinalIgnoreCase))
            return "Waiting for every player to ready.";
        if (message.Contains("no longer open", StringComparison.OrdinalIgnoreCase))
            return "This match is already preparing.";
        return "Waiting for the match requirements.";
    }

    private static void SendChat(PlayPresentationContext context, string text)
    {
        string bounded = Utf8TextLimit.Truncate(text, 256);
        if (string.IsNullOrWhiteSpace(bounded)) return;
        context.RunCommand("Send message", () => ExecuteChatSendAsync(
            context.Ui, bounded,
            () => context.Controller.SendLobbyChatAsync(
                bounded, context.CancellationToken),
            context.PostUi, context.Refresh));
    }

    /// <summary>
    /// A successful send acknowledges only the draft that was submitted. If
    /// typing continued while the request was in flight, its newer generation
    /// remains visible after the authoritative view is rebuilt.
    /// </summary>
    internal static async Task ExecuteChatSendAsync(PlayPresentationState ui,
        string submittedText, Func<Task> action, Action<Action> postUi, Action refresh)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(postUi);
        ArgumentNullException.ThrowIfNull(refresh);
        ChatDraftSubmission submission = ui.CaptureChatDraftSubmission(submittedText);
        await action().ConfigureAwait(false);
        postUi(() =>
        {
            ui.TryAcknowledgeChatSubmission(submission);
            refresh();
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

    private static WrapPanel StepRail(int step)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new PrimeStatusChip(""));
        row.Children.Add(new PrimeStatusChip(""));
        UpdateStepRail(row, step);
        return row;
    }

    private static void UpdateStepRail(WrapPanel rail, int step)
    {
        PrimeStatusChip first = (PrimeStatusChip)rail.Children[0];
        PrimeStatusChip second = (PrimeStatusChip)rail.Children[1];
        first.Text = step == 1 ? "1 · Lobby seats" : "✓ · Lobby seats";
        second.Text = "2 · Mission and rules";
        ((TextBlock)first.Child!).Foreground = step == 1
            ? GuiTheme.BrandBrush : GuiTheme.SuccessBrush;
        ((TextBlock)second.Child!).Foreground = step == 2
            ? GuiTheme.BrandBrush : GuiTheme.EdgeBrush;
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

    private static Control Field(string label, Control editor, double? width = null)
    {
        var field = Stack(Text(label, "prime-label"), editor);
        if (width is > 0) field.Width = width.Value;
        field.HorizontalAlignment = HorizontalAlignment.Stretch;
        editor.HorizontalAlignment = HorizontalAlignment.Stretch;
        return field;
    }

    private static AvaloniaButton Button(string label, Action action, bool primary = false,
        bool quiet = false)
    {
        AvaloniaButton button = PrimeControlFactory.Button(label, action, primary, quiet);
        button.MinHeight = 44;
        button.Margin = new Thickness(0, 0, 8, 8);
        PrimeAccessibility.SetName(button, label);
        return button;
    }

    private static Control StatusCard(string message, IBrush accent)
    {
        Border card = Section(Stack(Text(message, "prime-body")));
        card.BorderBrush = accent;
        card.BorderThickness = new Thickness(3, 1, 1, 1);
        return card;
    }

    private static Control BuildMapPreview(PlayPresentationContext context,
        string mapKey, double height)
    {
        string? mapPath = !string.IsNullOrWhiteSpace(mapKey)
            && ThumbnailGenerator.Exists(mapKey)
            ? ThumbnailGenerator.PathFor(mapKey) : null;
        return context.BuildPreview(mapPath, height,
            "No local map preview is available for this arena.");
    }

    private static double HostPreviewHeight(PrimeContentLayout layout,
        double viewportHeight)
    {
        double height = EffectiveViewportHeight(viewportHeight);
        return layout switch
        {
            PrimeContentLayout.Wide => Math.Clamp(160 + (height - 568) * 0.38,
                120, 320),
            PrimeContentLayout.Compact => Math.Clamp(220 + (height - 568) * 0.18,
                180, 270),
            _ => 250
        };
    }

    private static double LobbyPreviewHeight(PrimeContentLayout layout,
        double viewportHeight)
    {
        double height = EffectiveViewportHeight(viewportHeight);
        return layout switch
        {
            PrimeContentLayout.Wide => Math.Clamp(200 + (height - 568) * 0.34,
                150, 340),
            PrimeContentLayout.Compact => Math.Clamp(180 + (height - 568) * 0.22,
                150, 260),
            _ => 260
        };
    }

    private static double LobbyRosterHeight(PrimeContentLayout layout,
        double viewportHeight)
    {
        double height = EffectiveViewportHeight(viewportHeight);
        return layout switch
        {
            PrimeContentLayout.Wide => Math.Clamp(103 + (height - 568) * 0.36,
                72, 270),
            PrimeContentLayout.Compact => Math.Clamp(180 + (height - 568) * 0.24,
                120, 260),
            _ => 340
        };
    }

    private static double LobbyChatHeight(PrimeContentLayout layout,
        double viewportHeight)
    {
        double height = EffectiveViewportHeight(viewportHeight);
        return layout switch
        {
            // The right lane also owns its command rail and the fixed chat
            // editor chrome. Budgeting 72 DIP for history at a 720p shell
            // clipped the editor in member and active-chat states.
            PrimeContentLayout.Wide => Math.Clamp(36 + (height - 568) * 0.24,
                28, 230),
            PrimeContentLayout.Compact => Math.Clamp(100 + (height - 568) * 0.24,
                64, 190),
            _ => 210
        };
    }

    private static double EffectiveViewportHeight(double viewportHeight)
        => double.IsFinite(viewportHeight) && viewportHeight > 0
            ? viewportHeight : 568;

    private static void AddDashboardChild(PrimePlayResponsivePanel panel,
        Control child, PrimePlayLane lane, int mobileOrder,
        PrimePlayStage stage = PrimePlayStage.Always)
    {
        PrimePlayResponsivePanel.SetLane(child, lane);
        PrimePlayResponsivePanel.SetMobileOrder(child, mobileOrder);
        PrimePlayResponsivePanel.SetStage(child, stage);
        panel.Children.Add(child);
    }

    private static void TrackEditor(PlayPresentationContext context, Control editor)
        => editor.LostFocus += (_, _) => context.EditorLostFocus();

    internal static string PreferredRegionLabel(string? value)
        => LauncherPrefs.PreferredRegionLabel(value);

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

    private sealed record MapChoice(string Key, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record SeatPolicyChoice(LobbySeatPolicy Value, string Label)
    {
        public override string ToString() => Label;
    }
}
