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
using Avalonia.Threading;
using Avalonia.VisualTree;
using ProjectPrime.Server.Shared;
using MphRead.Mods.Accounts;
using MphRead.Mods;
using MphRead.Mods.Network;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Presentation;
using MphRead.Mods.Launcher.Resources;
using MphRead.Mods.Launcher.Theme;
using MphRead.Mods.MapGen;
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
    private const string HeadingPresenceHostName = "PlayHeadingPresenceHost";
    private const string DirectoryPresenceHostName = "PlayDirectoryPresenceHost";

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
            {
                context.Ui.LobbyView = null;
                return BuildEditMatch(context, lobby, edit);
            }
            if (context.Ui.EditMatchOpen && context.Ui.EditDraft is not null)
                context.Ui.ClearEdit();
            return BuildLobby(context, lobby);
        }

        // A lobby disappearing is an authoritative boundary, so do not carry
        // a confirmation into a later lobby that happens to reuse the route.
        context.Ui.LobbyView = null;
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
            "Choose an identity before entering the lobby directory.");
        root.Children.Add(Section(Stack(
            Text("Sign in or continue as a guest", "prime-heading"),
            Text("Use an account or explicit guest access before browsing lobbies or joining one.",
                "prime-muted"),
            Button("Sign in", context.OpenGateway, primary: true))));
        root.Children.Add(Section(Stack(
            Text("Your identity stays explicit", "prime-heading"),
            Text("Guest access is separate from account identity and can be selected again before entering multiplayer.",
                "prime-body"))));
        return root;
    }

    private static Control BuildHome(PlayPresentationContext context)
    {
        PlayState state = context.State;
        var headingPresenceHost = new Border
        {
            Name = HeadingPresenceHostName,
            Child = BuildOnlineBadge(context.Controller.Presence)
        };
        var root = Page(PrimeUiCopy.Play_Title, "ONLINE MULTIPLAYER",
            PrimeUiCopy.Play_Subtitle, headingPresenceHost);

        void BrowseLobbies()
        {
            context.Ui.Subsection = PlaySubsection.Browser;
            context.RunCommand("Browse lobbies",
                () => context.Controller.BrowseLobbiesAsync(context.CancellationToken));
            context.Refresh();
        }

        void PrepareHostLobby()
        {
            context.Ui.Subsection = PlaySubsection.HostMatch;
            context.Ui.HostStep = 1;
            context.RunCommand("Prepare host lobby",
                () => context.Controller.PrepareHostMatchAsync(context.CancellationToken));
            context.Refresh();
        }

        void RefreshLobbies()
        {
            context.RunCommand("Refresh lobbies",
                () => Task.WhenAll(
                    context.Controller.BrowseLobbiesAsync(context.CancellationToken),
                    context.Controller.RefreshPresenceAsync(context.CancellationToken)));
            context.Refresh();
        }

        AvaloniaButton host = Button(PrimeUiCopy.Play_HostLobby_Title,
            PrepareHostLobby, primary: true);
        host.IsEnabled = !state.Loading;
        var hostCard = PrimeControlFactory.HeroCard(Stack(
            Text("HOST A LOBBY", "prime-kicker"),
            Text("CREATE A LOBBY", "prime-hero"),
            Text("Choose the mission, rules, and player slots.", "prime-body"),
            host));
        hostCard.MinHeight = 180;
        hostCard.Classes.Add("prime-play-host-hero");
        PrimeAccessibility.SetName(hostCard, "Host a lobby");
        PrimeAccessibility.SetDescription(hostCard,
            "Create and configure a new multiplayer lobby.");

        AvaloniaButton quickPlay = Button("Quick Play", () =>
        {
            context.Ui.Subsection = PlaySubsection.Home;
            context.RunCommand("Quick Play", () => context.Controller.QuickPlayAsync(context.CancellationToken));
        });
        quickPlay.IsEnabled = !state.Loading;
        var quickPlayCard = PrimeControlFactory.ActionCard(Stack(
            Text("QUICK PLAY", "prime-kicker"),
            Text("Find a match.", "prime-card-heading"),
            Text("Join the best available open lobby.", "prime-body"),
            quickPlay), selected: false);
        quickPlayCard.Classes.Add("prime-play-quick-action");
        PrimeAccessibility.SetName(quickPlayCard, "Quick Play: find a match");
        PrimeAccessibility.SetDescription(quickPlayCard,
            "Join the best available open lobby.");

        var dashboard = new PrimePlayResponsivePanel(wideLeftWeight: 7,
            wideRightWeight: 5)
        {
            Spacing = 10
        };
        dashboard.Classes.Add("prime-play-landing-dashboard");
        AddDashboardChild(dashboard, hostCard, PrimePlayLane.Left, 0);
        AddDashboardChild(dashboard, quickPlayCard, PrimePlayLane.Right, 1);
        root.Children.Add(dashboard);

        // The directory panel is the single source of loading, empty, loaded,
        // and failure copy on the landing page. This prevents a stale generic
        // status and an empty-state card appearing together after a rebuild.
        var directoryAndPresence = new PrimePlayResponsivePanel(wideLeftWeight: 7,
            wideRightWeight: 5, compactColumns: true)
        {
            Spacing = 10
        };
        directoryAndPresence.Classes.Add("prime-play-directory-presence");
        AddDashboardChild(directoryAndPresence,
            BuildLobbyDirectoryState(context, state.MatchDirectoryState,
                BrowseLobbies, RefreshLobbies), PrimePlayLane.Left, 0);
        var directoryPresenceHost = new Border
        {
            Name = DirectoryPresenceHostName,
            Child = BuildPresencePanel(context)
        };
        AddDashboardChild(directoryAndPresence,
            directoryPresenceHost,
            PrimePlayLane.Right, 1);
        root.Children.Add(directoryAndPresence);

        if (ShouldShowStandaloneNetwork(context))
            root.Children.Add(BuildNetworkSummary(context));
        return root;
    }

    /// <summary>
    /// Refreshes only the bounded public-presence regions on the retained Home
    /// tree. Route actions, focus, scroll position, and route-entry motion are
    /// deliberately left untouched by periodic presence updates.
    /// </summary>
    internal static bool TryUpdateHomePresence(Control root,
        PlayPresentationContext context)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(context);
        Border? heading = FindHost(root, HeadingPresenceHostName);
        Border? directory = FindHost(root, DirectoryPresenceHostName);
        if (heading is null || directory is null) return false;

        AvaloniaButton? focusedAction = TopLevel.GetTopLevel(root)?.FocusManager?
            .GetFocusedElement() as AvaloniaButton;
        string? focusedLabel = focusedAction?.Content as string;
        bool restoreDirectoryFocus = focusedAction != null
            && directory.Child is Control previousDirectory
            && (ReferenceEquals(focusedAction, previousDirectory)
                || focusedAction.GetVisualAncestors().Contains(previousDirectory));

        heading.Child = BuildOnlineBadge(context.Controller.Presence);
        directory.Child = BuildPresencePanel(context);
        if (restoreDirectoryFocus && focusedLabel != null)
        {
            Dispatcher.UIThread.Post(() => directory.GetVisualDescendants()
                    .OfType<AvaloniaButton>()
                    .FirstOrDefault(button => StringComparer.Ordinal.Equals(
                        button.Content as string, focusedLabel))
                    ?.Focus(),
                DispatcherPriority.Input);
        }
        return true;
    }

    private static Border? FindHost(Control root, string name)
        => root.GetVisualDescendants().OfType<Border>()
            .FirstOrDefault(control => StringComparer.Ordinal.Equals(
                control.Name, name));

    private static OnlinePlayersPanel BuildPresencePanel(
        PlayPresentationContext context)
    {
        void ShowAllPlayers()
        {
            context.Ui.ShowAllPresence();
            context.Refresh();
        }

        void ShowPlayerPreview()
        {
            context.Ui.ShowPresencePreview();
            context.Refresh();
        }

        void SelectPresencePage(int page)
        {
            int pageCount = Math.Max(1,
                (context.Controller.Presence.Players.Length
                    + PresenceContract.PageSize - 1) / PresenceContract.PageSize);
            context.Ui.SetPresencePage(page, pageCount);
            context.Refresh();
        }

        return new OnlinePlayersPanel(context.Controller.Presence,
            context.Ui.PresenceExpanded, context.Ui.PresencePage,
            ShowAllPlayers, ShowPlayerPreview, SelectPresencePage,
            () => context.RunCommand("Refresh online players",
                () => context.Controller.RefreshPresenceAsync(
                    context.CancellationToken)));
    }

    private static Control? BuildOnlineBadge(PresencePresentationState presence)
    {
        if (presence.State is not PresenceLoadState.Ready
            && !(presence.State == PresenceLoadState.Failed
                && presence.TotalOnline > 0))
            return null;
        var onlineBadge = new PrimeStatusChip(presence.BadgeText);
        onlineBadge.Classes.Add("prime-online-badge");
        onlineBadge.SetStatus(presence.BadgeText,
            presence.State == PresenceLoadState.Failed
                ? PrimeStatusKind.Warning
                : PrimeStatusKind.Success);
        return onlineBadge;
    }

    private static bool ShouldShowStandaloneNetwork(PlayPresentationContext context)
    {
        string preferred = PreferredRegionLabel(context.Controller.PreferredRegion);
        bool custom = !preferred.Equals(LauncherPrefs.AutomaticPreferredRegionId,
            StringComparison.OrdinalIgnoreCase);
        // An explicit diagnostics request is also allowed to keep the compact
        // summary visible. Automatic-region details stay out of the primary
        // action layout and remain available from Settings.
        return context.ExpandAdvancedNetwork || custom || context.State.Phase == PlayPhase.Error;
    }

    private static Control BuildLobbyDirectoryState(PlayPresentationContext context,
        MatchDirectoryPresentationState state, Action browseLobbies,
        Action refreshLobbies)
    {
        StackPanel content;
        string className;
        switch (state)
        {
            case MatchDirectoryPresentationState.Loading:
                className = "prime-directory-loading";
                content = Stack(
                    Text("OPEN LOBBIES", "prime-kicker"),
                    Text("Loading the public lobby directory…", "prime-heading"),
                    Text("The latest open lobbies are being retrieved.", "prime-muted"),
                    Button("Cancel", () => context.Controller.CancelEntry(), quiet: true));
                break;
            case MatchDirectoryPresentationState.Empty:
                className = "prime-directory-empty";
                var emptyActions = new WrapPanel { Orientation = Orientation.Horizontal };
                emptyActions.Children.Add(Button("Refresh", refreshLobbies, quiet: true));
                content = Stack(
                    Text("NO OPEN LOBBIES", "prime-kicker"),
                    Text(PrimeUiCopy.Play_NoLobbies_Title, "prime-heading"),
                    Text(PrimeUiCopy.Play_NoLobbies_Description, "prime-muted"),
                    emptyActions);
                break;
            case MatchDirectoryPresentationState.Loaded:
                className = "prime-directory-loaded";
                content = Stack(
                    Text("OPEN LOBBIES", "prime-kicker"),
                    Text("Choose a lobby to join or browse the full directory.", "prime-heading"));
                foreach (LobbyListEntry entry in context.State.Lobbies!.Lobbies.Take(3))
                    content.Children.Add(CreateCompactMatchCard(context, entry));
                content.Children.Add(Button("Browse all lobbies", browseLobbies, quiet: true));
                break;
            case MatchDirectoryPresentationState.Failed:
                className = "prime-directory-failed";
                content = Stack(
                    Text("LOBBY DIRECTORY UNAVAILABLE", "prime-kicker"),
                    Text("We couldn’t load public lobbies right now.", "prime-heading"),
                    Text("Refresh to try again.", "prime-muted"),
                    Button("Refresh", refreshLobbies, primary: true));
                break;
            default:
                className = "prime-directory-not-loaded";
                content = Stack(
                    Text("OPEN LOBBIES", "prime-kicker"),
                    Text("Find an open lobby.", "prime-heading"),
                    Text("Review the map, mode, and players before joining.",
                        "prime-muted"),
                    Button("Browse lobbies", browseLobbies));
                break;
        }

        PrimeSectionPanel panel = Section(content);
        panel.Classes.Add("prime-directory-state");
        panel.Classes.Add(className);
        PrimeAccessibility.SetName(panel, state switch
        {
            MatchDirectoryPresentationState.Empty => "No open lobbies",
            MatchDirectoryPresentationState.Loading => "Loading open lobbies",
            MatchDirectoryPresentationState.Failed => "Lobby directory unavailable",
            MatchDirectoryPresentationState.Loaded => "Open lobbies",
            _ => "Open lobbies not loaded"
        });
        return panel;
    }

    private static Control BuildNetworkSummary(PlayPresentationContext context)
    {
        string preferred = PreferredRegionLabel(context.Controller.PreferredRegion);
        bool automatic = preferred.Equals(LauncherPrefs.AutomaticPreferredRegionId,
            StringComparison.OrdinalIgnoreCase);
        string summary = automatic ? "Automatic region" : $"{preferred} region";
        var content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 12
        };
        string detail = automatic
            ? "Region selection is automatic."
            : "Using your preferred region.";
        detail += context.OpenNetworkSettings is not null
            ? " Change it in Settings if needed."
            : " Region selection is managed by the launcher.";
        var copy = Stack(Text("NETWORK", "prime-kicker"),
            Text(summary, "prime-tech"),
            Text(detail, "prime-muted"));
        content.Children.Add(copy);
        if (context.OpenNetworkSettings is { } openNetworkSettings)
        {
            AvaloniaButton settings = Button("Network settings", openNetworkSettings,
                quiet: true);
            content.Children.Add(settings);
            Grid.SetColumn(settings, 1);
        }
        PrimeSectionPanel panel = Section(content);
        panel.Classes.Add("prime-network-summary");
        PrimeAccessibility.SetName(panel, $"Network: {summary}");
        return panel;
    }

    private static Control BuildBrowser(PlayPresentationContext context)
    {
        PlayState state = context.State;
        var root = Page("Browse lobbies", "LOBBY DIRECTORY",
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
                    ? "Choose Refresh to load the current lobby directory."
                    : "No lobbies match these filters. Clear a filter or refresh the directory.");
                if (context.Ui.Filters != new MatchBrowserFilters())
                    empty.Children.Add(Button("Clear filters", () =>
                    {
                        context.Ui.ClearFilters();
                        RebuildCards();
                    }, quiet: true));
                empty.Children.Add(Button("Refresh", () => context.RunCommand(
                    "Refresh lobbies", () => context.Controller.BrowseLobbiesAsync(
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
        footer.Children.Add(Button("Refresh", () => context.RunCommand("Refresh lobbies",
            () => context.Controller.BrowseLobbiesAsync(context.CancellationToken)), primary: true));
        if (state.Loading)
            footer.Children.Add(Button("Cancel", () => context.Controller.CancelEntry(), quiet: true));
        root.Children.Add(Section(Stack(
            Text(state.Loading ? "Loading the lobby directory…" :
                snapshot is null ? "The directory has not been loaded." :
                $"Showing {source.Length} lobbies.", "prime-muted"),
            footer)));
        return root;
    }

    private static Control BuildBrowserFilters(PlayPresentationContext context,
        LobbyListEntry[] entries, Action rebuild)
    {
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(Text("LOBBY FILTERS", "prime-label"));
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
        CheckBox hideFull = Check("Hide full lobbies", context.Ui.Filters.HideFull);
        hideFull.IsCheckedChanged += (_, _) =>
        {
            context.Ui.SetFilters(context.Ui.Filters with { HideFull = hideFull.IsChecked == true });
            rebuild();
        };
        checks.Children.Add(open);
        checks.Children.Add(spectate);
        checks.Children.Add(hideFull);
        content.Children.Add(checks);
        content.Children.Add(Text("Lobby cards show details supplied by the directory. Connection quality is not estimated.",
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
            Spacing = 8
        };
        dashboard.Classes.Add("prime-host-layout");

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 12
        };
        header.Children.Add(PrimeControlFactory.PageHeading("Host lobby",
            "NEW LOBBY",
            "Create a new lobby."));
        AvaloniaButton backToPlay = Button("Back to Play", () =>
        {
            context.Ui.Subsection = PlaySubsection.Home;
            context.Refresh();
        }, quiet: true);
        header.Children.Add(backToPlay);
        Grid.SetColumn(backToPlay, 1);
        AddDashboardChild(dashboard, header, PrimePlayLane.Full, 0);

        WrapPanel stepRail = StepRail(context.Ui.HostStep);
        stepRail.Classes.Add("prime-host-step-rail");
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
        BotDifficultyChoice[] difficultyChoices = Enum.GetValues<BotDifficulty>()
            .Select(value => new BotDifficultyChoice(value,
                PrimeGameText.BotDifficultyLabel(value))).ToArray();
        ComboBox botDifficulty = Combo(difficultyChoices.Cast<object>().ToArray(),
            difficultyChoices.First(choice => choice.Value == draft.BotDifficulty));
        void UpdateBots()
        {
            int maximumBots = Math.Max(0, draft.PlayerLimit - 1);
            draft.BotCount = Math.Clamp(draft.BotCount, 0, maximumBots);
            bots.ItemsSource = Enumerable.Range(0, maximumBots + 1)
                .Cast<object>().ToArray();
            bots.SelectedItem = draft.BotCount;
            botDifficulty.IsEnabled = draft.BotCount > 0;
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
            if (bots.SelectedItem is int value)
            {
                draft.BotCount = value;
                botDifficulty.IsEnabled = value > 0;
            }
        };
        botDifficulty.SelectionChanged += (_, _) =>
        {
            if (botDifficulty.SelectedItem is BotDifficultyChoice value)
                draft.BotDifficulty = value.Value;
        };
        UpdateBots();
        TrackEditor(context, players);
        TrackEditor(context, observers);
        TrackEditor(context, bots);
        TrackEditor(context, botDifficulty);

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

        var lobbyFields = new PrimeFieldGrid(maximumColumns: 2,
            minimumColumnWidth: 170);
        lobbyFields.Children.Add(Field("Lobby name", name));
        lobbyFields.Children.Add(Field("Seat policy", seatPolicy));
        var capacityFields = new PrimeFieldGrid(maximumColumns: 4,
            minimumColumnWidth: 110);
        capacityFields.Children.Add(Field("Player seats", players));
        capacityFields.Children.Add(Field("Observer seats", observers));
        capacityFields.Children.Add(Field("Bots", bots));
        capacityFields.Children.Add(Field("Bot difficulty", botDifficulty));
        Control identity = Stack(
            Text("LOBBY & SEATS", "prime-kicker"),
            Text("Lobby details", "prime-card-heading"),
            lobbyFields,
            new PrimeDivider(),
            Text("CAPACITY", "prime-kicker"),
            capacityFields);
        identity.Classes.Add("prime-host-lobby-seats");
        AddDashboardChild(dashboard, PrimeControlFactory.CompactPanel(identity),
            PrimePlayLane.Left, 2,
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

        var mapSummary = Text(PrimeGameText.MapName(draft.MapKey), "prime-card-heading");
        var modeSummary = Text(PrimeGameText.ModeLabel(draft.Mode), "prime-muted");
        var previewHost = new Border
        {
            Child = BuildMapPreview(context, draft.MapKey, 250),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        previewHost.Child.Classes.Add("prime-host-preview");
        Control mission = Stack(
            Text("MISSION", "prime-kicker"), mapSummary, modeSummary, previewHost);
        mission.Classes.Add("prime-host-mission-preview");
        AddDashboardChild(dashboard, PrimeControlFactory.CompactPanel(mission),
            PrimePlayLane.Left, 3,
            PrimePlayStage.Two);

        var rulesHost = new Border();
        void RebuildRules() => rulesHost.Child = BuildRuleControls(context, draft);
        map.SelectionChanged += (_, _) =>
        {
            if (map.SelectedItem is not MapChoice choice) return;
            draft.MapKey = choice.Key;
            mapSummary.Text = PrimeGameText.MapName(choice.Key);
            Control nextPreview = BuildMapPreview(context, choice.Key, 250);
            nextPreview.Classes.Add("prime-host-preview");
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
            Text("MATCH CONFIGURATION", "prime-section-heading"),
            Text("Choose a map and mode, then adjust the rules.",
                "prime-muted"), missionFields);
        configuration.Classes.Add("prime-host-match-configuration");
        if (maps.Length == 0)
            configuration.Children.Add(StatusCard(context.Controller.MapCatalogMessage,
                GuiTheme.WarmBrush));
        configuration.Children.Add(rulesHost);
        var createRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        AvaloniaButton create = Button("Create lobby", () =>
        {
            if (string.IsNullOrWhiteSpace(draft.Name))
            {
                context.Notify(PrimeNotificationKind.Error,
                    "Enter a lobby name before creating it.");
                return;
            }
            if (!draft.TryBuildRules(out _, out string error))
            {
                context.Notify(PrimeNotificationKind.Error, error);
                return;
            }
            context.RunCommand("Create lobby", () => context.HostMatch(draft));
        }, primary: true);
        create.MinWidth = 180;
        create.IsEnabled = !context.State.Loading;
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
                    "Enter a lobby name before continuing.");
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
        BotDifficultyChoice[] difficultyChoices = Enum.GetValues<BotDifficulty>()
            .Select(value => new BotDifficultyChoice(value,
                PrimeGameText.BotDifficultyLabel(value))).ToArray();
        var botDifficulty = Combo(difficultyChoices.Cast<object>().ToArray(),
            difficultyChoices.First(choice => choice.Value == draft.BotDifficulty));
        botDifficulty.IsEnabled = draft.BotCount > 0;
        bots.SelectionChanged += (_, _) =>
        {
            if (bots.SelectedItem is int value)
            {
                draft.BotCount = value;
                botDifficulty.IsEnabled = value > 0;
            }
        };
        botDifficulty.SelectionChanged += (_, _) =>
        {
            if (botDifficulty.SelectedItem is BotDifficultyChoice value)
                draft.BotDifficulty = value.Value;
        };
        TrackEditor(context, bots);
        TrackEditor(context, botDifficulty);

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
        fields.Children.Add(Field("Bot difficulty", botDifficulty, 220));
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
        MphRead.MatchRules defaults = LobbyRuleDefaults.For(draft.Mode);
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(Text("MATCH RULES", "prime-section-heading"));
        content.Children.Add(Text("Only rules for the selected mode are shown. Clear a value to restore its default.",
            "prime-muted"));
        var fields = new PrimeFieldGrid(maximumColumns: 4,
            minimumColumnWidth: 150);

        if (draft.Mode.IsTeamMode())
        {
            int maximumTeams = draft.Mode is MatchMode.TeamBattle or MatchMode.TeamSurvival
                ? MatchRules.MaximumTeamCount : 2;
            int effectiveTeams = Math.Clamp(draft.TeamCount ?? defaults.TeamCount, 2,
                maximumTeams);
            object[] choices = Enumerable.Range(2, maximumTeams - 1)
                .Cast<object>().ToArray();
            ComboBox teamCount = Combo(choices, effectiveTeams);
            teamCount.SelectionChanged += (_, _) =>
            {
                if (teamCount.SelectedItem is int value) draft.TeamCount = value;
            };
            TrackEditor(context, teamCount);
            fields.Children.Add(Field("Teams", teamCount));
        }

        TextBox time = RuleEditor(draft.TimeLimitText,
            LobbyRuleDefaults.Time(draft.Mode, null),
            value => draft.TimeLimitText = value);
        TrackEditor(context, time);
        fields.Children.Add(Field("Time limit", time));

        if (applicability.ScoreGoal)
        {
            TextBox score = RuleEditor(draft.ScoreGoalText,
                LobbyRuleDefaults.Score(draft.Mode, null),
                value => draft.ScoreGoalText = value);
            TrackEditor(context, score);
            fields.Children.Add(Field(applicability.GoalLabel, score));
        }
        else if (applicability.StartingLives)
        {
            TextBox lives = RuleEditor(draft.StartingLivesText,
                LobbyRuleDefaults.Lives(draft.Mode, null),
                value => draft.StartingLivesText = value);
            TrackEditor(context, lives);
            fields.Children.Add(Field(applicability.GoalLabel, lives));
        }
        else if (applicability.ObjectiveTimeGoal)
        {
            TextBox objective = RuleEditor(draft.ObjectiveTimeGoalText,
                LobbyRuleDefaults.ObjectiveTime(draft.Mode, null),
                value => draft.ObjectiveTimeGoalText = value);
            TrackEditor(context, objective);
            fields.Children.Add(Field(applicability.GoalLabel, objective));
        }

        if (applicability.DamageLevel)
        {
            int defaultDamage = defaults.DamageLevel;
            var choices = new List<DamageChoice>
            {
                new(LobbyRuleDefaults.Damage(draft.Mode), null)
            };
            foreach (int value in new[] { 0, 1, 2 })
            {
                if (value == defaultDamage) continue;
                choices.Add(new(DamageLabel(value), value));
            }
            DamageChoice selectedDamage = choices.FirstOrDefault(choice =>
                choice.Value == draft.DamageLevel) ?? choices[0];
            ComboBox damage = Combo(choices.Cast<object>().ToArray(), selectedDamage);
            damage.SelectionChanged += (_, _) =>
            {
                if (damage.SelectedItem is DamageChoice choice) draft.DamageLevel = choice.Value;
            };
            TrackEditor(context, damage);
            fields.Children.Add(Field("Damage level", damage));
        }

        var spawnChoices = new List<SpawnPolicyChoice>
        {
            new(SpawnPolicyLabel(defaults.SpawnPolicy), null)
        };
        foreach (SpawnPolicy policy in new[] { SpawnPolicy.Classic, SpawnPolicy.Enhanced,
            SpawnPolicy.Duel })
        {
            if (policy == defaults.SpawnPolicy
                || policy == SpawnPolicy.Duel
                    && (draft.Mode != MatchMode.Battle || draft.PlayerLimit != 2))
            {
                continue;
            }
            spawnChoices.Add(new(SpawnPolicyLabel(policy), policy));
        }
        SpawnPolicyChoice selectedSpawn = spawnChoices.FirstOrDefault(choice =>
            choice.Value == draft.SpawnPolicy) ?? spawnChoices[0];
        ComboBox spawnPolicy = Combo(spawnChoices.Cast<object>().ToArray(), selectedSpawn);
        spawnPolicy.SelectionChanged += (_, _) =>
        {
            if (spawnPolicy.SelectedItem is SpawnPolicyChoice choice)
                draft.SpawnPolicy = choice.Value;
        };
        TrackEditor(context, spawnPolicy);
        fields.Children.Add(Field("Spawn policy", spawnPolicy));

        ResourceRadarPolicyChoice[] resourceRadarChoices =
        [
            new(ResourceRadarPolicy.Disabled, "Disabled"),
            new(ResourceRadarPolicy.SpawnLocations, "Spawn locations"),
            new(ResourceRadarPolicy.AvailableResources, "Available resources"),
            new(ResourceRadarPolicy.AvailableWithRespawn, "Available + respawn")
        ];
        ResourceRadarPolicyChoice selectedResourceRadar = resourceRadarChoices
            .FirstOrDefault(choice => choice.Value == draft.ResourceRadarPolicy)
            ?? resourceRadarChoices[0];
        ComboBox resourceRadar = Combo(resourceRadarChoices.Cast<object>().ToArray(),
            selectedResourceRadar);
        resourceRadar.SelectionChanged += (_, _) =>
        {
            if (resourceRadar.SelectedItem is ResourceRadarPolicyChoice choice)
                draft.ResourceRadarPolicy = choice.Value;
        };
        TrackEditor(context, resourceRadar);
        fields.Children.Add(Field("Resource radar", resourceRadar));

        AddBoolControl(fields, context, "Friendly fire", draft.FriendlyFire,
            defaults.FriendlyFire,
            value => draft.FriendlyFire = value, applicability.FriendlyFire);
        AddBoolControl(fields, context, "Affinity weapons", draft.AffinityWeapons,
            defaults.AffinityWeapons,
            value => draft.AffinityWeapons = value, applicability.AffinityWeapons);
        AddBoolControl(fields, context, "Enhanced hunters", draft.EnhancedHunters,
            defaults.EnhancedHunters,
            value => draft.EnhancedHunters = value, applicability.EnhancedHunters);
        AddBoolControl(fields, context, "Balanced Mode", draft.BalancedMode,
            defaults.BalancedMode,
            value => draft.BalancedMode = value, applicability.BalancedMode);
        AddBoolControl(fields, context, "Power-ups", draft.PowerupsEnabled,
            defaults.PowerupsEnabled,
            value => draft.PowerupsEnabled = value, applicable: true);
        AddBoolControl(fields, context, "Player radar", draft.PlayerRadar,
            defaults.PlayerRadar,
            value => draft.PlayerRadar = value, applicability.PlayerRadar);
        AddBoolControl(fields, context, "Octolith reset", draft.OctolithReset,
            defaults.OctolithReset,
            value => draft.OctolithReset = value, applicability.OctolithReset);
        AddBoolControl(fields, context, "Protection ends on attack",
            draft.CancelSpawnProtectionOnOffensiveAction,
            defaults.CancelSpawnProtectionOnOffensiveAction,
            value => draft.CancelSpawnProtectionOnOffensiveAction = value,
            applicable: true);
        content.Children.Add(fields);
        return content;
    }

    private static void AddBoolControl(Panel content, PlayPresentationContext context,
        string label, bool? value, bool defaultValue, Action<bool?> set, bool applicable)
    {
        if (!applicable) return;
        BoolChoice[] choices =
        [
            new(defaultValue ? "On" : "Off", null),
            new(defaultValue ? "Off" : "On", !defaultValue)
        ];
        BoolChoice selected = choices.FirstOrDefault(choice => choice.Value == value)
            ?? choices[0];
        ComboBox combo = Combo(choices.Cast<object>().ToArray(), selected);
        if (label == "Enhanced hunters")
        {
            ToolTip.SetTip(combo,
                "Enhances each Hunter's native affinity with an additional combat effect.");
        }
        else if (label == "Balanced Mode")
        {
            ToolTip.SetTip(combo,
                "Applies Project Prime's competitive hunter and weapon balance adjustments. Disable for original MPH-style mechanics.");
        }
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
        LobbyWaitlistSnapshot waitlist = lobby.Waitlist ?? LobbyWaitlistSnapshot.Empty;
        context.Ui.ObserveOffer(waitlist.SelfOffer?.OfferId);
        if (context.Ui.LobbyView is { } existing
            && existing.LobbyId == lobby.LobbyId)
        {
            existing.Update(context, lobby);
            context.TrackChatPanel?.Invoke(existing.ChatPanel);
            return existing.View;
        }

        var view = new LobbyPresentationView(context, lobby);
        context.Ui.LobbyView = view;
        context.TrackChatPanel?.Invoke(view.ChatPanel);
        return view.View;
    }

    private static Control BuildLobbyHeader(LobbySnapshot lobby, LobbyMember? current,
        LobbyWaitlistSnapshot waitlist)
    {
        int players = lobby.Members.Count(member => !member.Observer);
        int observers = lobby.Members.Count(member => member.Observer);
        int occupiedPlayers = players + lobby.BotCount;
        string phase = PrimeGameText.LobbyPhaseLabel(lobby.Phase).ToUpperInvariant();
        var content = Stack(
            Text($"● {phase} · {occupiedPlayers}/{lobby.PlayerLimit} PLAYERS", "prime-body"));
        if (lobby.ObserverLimit > 0)
            content.Children.Add(Text($"{observers}/{lobby.ObserverLimit} OBSERVERS",
                "prime-muted"));

        // Keep the bar about lobby capacity only. Readiness belongs to the
        // player's loadout card and queue state belongs to the queue panel.
        if (lobby.Phase == LobbyPhase.Open)
            content.Children.Add(Text(PrimeGameText.SeatPolicyLabel(lobby.SeatPolicy),
                "prime-muted"));
        else if (current?.Observer == true)
            content.Children.Add(Text("You are observing this lobby.", "prime-muted"));
        else if (waitlist.IsSelfQueued)
            content.Children.Add(Text("You are waiting for a player seat.",
                "prime-muted"));
        PrimeSectionPanel panel = Section(content);
        panel.Classes.Add("prime-lobby-status");
        PrimeAccessibility.SetName(panel,
            $"Lobby status: {phase}, {occupiedPlayers} of {lobby.PlayerLimit} players, "
                + $"{observers} of {lobby.ObserverLimit} observers.");
        return panel;
    }

    private static Control BuildMissionPanel(PlayPresentationContext context,
        LobbySnapshot lobby, bool owner, out Control preview)
    {
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
        preview = BuildMapPreview(context, lobby.MapKey, 260);
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
            LobbyRuleDefaults.Time(lobby.Mode, rules.TimeLimitSeconds)));
        if (applicability.ScoreGoal)
            stats.Children.Add(PrimeControlFactory.StatTile("SCORE",
                LobbyRuleDefaults.Score(lobby.Mode, rules.ScoreGoal)));
        else if (applicability.StartingLives)
            stats.Children.Add(PrimeControlFactory.StatTile("LIVES",
                LobbyRuleDefaults.Lives(lobby.Mode, rules.StartingLives)));
        else if (applicability.ObjectiveTimeGoal)
            stats.Children.Add(PrimeControlFactory.StatTile("OBJECTIVE",
                LobbyRuleDefaults.ObjectiveTime(lobby.Mode,
                    rules.ObjectiveTimeGoalSeconds)));
        stats.Children.Add(PrimeControlFactory.StatTile("BOTS",
            lobby.BotCount.ToString(CultureInfo.InvariantCulture)));
        stats.Children.Add(PrimeControlFactory.StatTile("BOT DIFFICULTY",
            lobby.BotCount > 0
                ? PrimeGameText.BotDifficultyLabel(lobby.BotDifficulty) : "OFF"));
        stats.Children.Add(PrimeControlFactory.StatTile("PLAYER SEATS",
            lobby.PlayerLimit.ToString(CultureInfo.InvariantCulture)));
        SpawnPolicy effectiveSpawnPolicy = rules.SpawnPolicy
            ?? LobbyRuleDefaults.For(lobby.Mode).SpawnPolicy;
        stats.Children.Add(PrimeControlFactory.StatTile("SPAWNS",
            SpawnPolicyLabel(effectiveSpawnPolicy)));
        stats.Children.Add(PrimeControlFactory.StatTile("RESOURCE RADAR",
            LobbyRuleDefaults.ResourceRadar(rules.ResourceRadarPolicy)));
        stats.Children.Add(PrimeControlFactory.StatTile("POWER-UPS",
            (rules.PowerupsEnabled ?? true) ? "ON" : "OFF"));
        stats.Children.Add(PrimeControlFactory.StatTile("ENHANCED HUNTERS",
            (rules.EnhancedHunters ?? false) ? "ON" : "OFF"));
        stats.Children.Add(PrimeControlFactory.StatTile("BALANCED",
            (rules.BalancedMode ?? false) ? "ON" : "OFF"));
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
        Guid? sessionId, Guid ownerSessionId, IBrush accent, bool showTeam)
    {
        LobbyMember[] list = members.ToArray();
        var content = Stack();
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        TextBlock heading = Text(title, "prime-heading");
        heading.Foreground = accent;
        header.Children.Add(heading);
        string countLabel = title.Equals("OBSERVERS", StringComparison.Ordinal)
            ? $"{list.Length} observing"
            : $"{list.Length} player{(list.Length == 1 ? "" : "s")}";
        header.Children.Add(Text(countLabel, "prime-label"));
        Grid.SetColumn(header.Children[^1], 1);
        content.Children.Add(header);
        foreach (LobbyMember member in list)
        {
            bool isYou = sessionId == member.SessionId;
            bool isOwner = ownerSessionId == member.SessionId;
            string state = member.Observer ? "Observer" : member.Ready ? "Ready" : "Not ready";
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
            var identity = Stack(Text(member.DisplayName, "prime-body"),
                Text(LobbyMemberDetail(member, showTeam, isYou, isOwner), "prime-muted"));
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
        if (list.Length == 0)
        {
            string emptyCopy = title.Equals("OBSERVERS", StringComparison.Ordinal)
                ? "No observers."
                : "No players yet.";
            content.Children.Add(new PrimeEmptyState(emptyCopy));
        }
        return Section(content);
    }

    internal static string LobbyMemberDetail(LobbyMember member, bool showTeam,
        bool isCurrent, bool isOwner = false)
    {
        ArgumentNullException.ThrowIfNull(member);
        var details = new List<string> { PrimeGameText.HunterLabel(member.Hunter) };
        if (showTeam && !member.Observer)
            details.Add($"Team {member.Team + 1}");
        if (isCurrent) details.Add("YOU");
        if (isOwner) details.Add("HOST");
        return String.Join(" · ", details);
    }

    internal static IBrush LobbyMemberStatusBrush(LobbyMember member)
    {
        ArgumentNullException.ThrowIfNull(member);
        return member.Observer ? GuiTheme.TechBrush
            : member.Ready ? GuiTheme.SuccessBrush : GuiTheme.WarningBrush;
    }

    private static Control BuildMemberControls(PlayPresentationContext context, LobbySnapshot lobby,
        LobbyMember? current, PlayState state)
    {
        var content = Stack(Text("YOUR HUNTER", "prime-heading"));
        if (current is null)
        {
            content.Children.Add(Text("Join a player or observer seat to choose a Hunter.",
                "prime-muted"));
            return content;
        }

        if (current.Observer)
        {
            content.Children.Add(Text("You are observing this lobby.", "prime-muted"));
            return content;
        }

        var hunterPreview = PrimeControlFactory.PreviewStage(
            HunterPreviewFallback.ForHunterCompact(current.Hunter));
        hunterPreview.Height = 120;
        hunterPreview.Classes.Add("prime-lobby-hunter-preview");
        PrimeAccessibility.SetName(hunterPreview,
            $"Hunter preview: {PrimeGameText.HunterLabel(current.Hunter)}");
        content.Children.Add(hunterPreview);

        Hunter[] hunterValues = PlayableHunterCatalog.All.ToArray();
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

        var readiness = new WrapPanel { Orientation = Orientation.Horizontal };
        readiness.Children.Add(Text(current.Ready ? "✓ Ready" : "Not ready",
            current.Ready ? "prime-body" : "prime-muted"));
        if (lobby.Phase == LobbyPhase.Open)
        {
            bool missingRequiredMap = lobby.RequiredMap is { } required
                && !context.Controller.IsRequiredMapInstalled(required);
            bool ready = current.Ready;
            AvaloniaButton readyAction = Button(ready ? "Cancel ready" : "Ready", () =>
                context.RunCommand(ready ? "Cancel ready" : "Ready",
                    () => context.Controller.SetReadyAsync(!ready,
                        context.CancellationToken)), primary: !ready);
            readyAction.MinWidth = 120;
            readyAction.IsEnabled = !missingRequiredMap && !state.Loading;
            readiness.Children.Add(readyAction);
            if (missingRequiredMap)
                content.Children.Add(Text("Prepare the required map before readying.",
                    "prime-muted"));
        }
        var memberActions = new PrimeFieldGrid(maximumColumns: 2,
            minimumColumnWidth: 180)
        {
            ColumnSpacing = 8,
            RowSpacing = 6
        };
        memberActions.Children.Add(readiness);

        if (lobby.Mode.IsTeamMode() && lobby.Phase == LobbyPhase.Open)
        {
            var teamActions = new WrapPanel { Orientation = Orientation.Horizontal };
            teamActions.Children.Add(Text("TEAM", "prime-label"));
            int teamCount = lobby.Rules?.TeamCount ?? 2;
            for (byte team = 0; team < teamCount; team++)
            {
                byte selectedTeam = team;
                string label = $"Team {team + 1}";
                teamActions.Children.Add(Button(label, () => context.RunCommand(
                    $"Select {label}", () => context.Controller.RequestTeamAsync(
                        selectedTeam, context.CancellationToken)),
                    primary: current.Team == selectedTeam));
            }
            memberActions.Children.Add(teamActions);
        }
        content.Children.Add(memberActions);
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
        bool owner, PlayState state)
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
        if (owner && lobby.Phase == LobbyPhase.Open)
        {
            AvaloniaButton start = Button("Start Match", () => context.RunCommand("Start match",
                () => context.Controller.StartMatchAsync(context.CancellationToken)), primary: true);
            bool canStart = eligibility.CanStart && !missingRequiredMap
                && !state.Loading;
            start.IsEnabled = canStart;
            start.MinWidth = 140;
            primaryActions.Children.Add(start);
            if (!canStart)
            {
                string explanation = missingRequiredMap
                    ? "Prepare the required map before starting."
                    : state.Loading ? "Updating the lobby…"
                    : PlayerFacingEligibilityMessage(eligibility);
                content.Children.Add(Text(explanation, "prime-body"));
            }
        }
        bool requiresConfirmation = RequiresLeaveConfirmation(lobby.Phase,
            state.Handoff is not null);
        bool leaveGroupedWithPrimary = false;
        if (!requiresConfirmation && primaryActions.Children.Count > 0)
        {
            primaryActions.Children.Add(Button("Leave lobby", () => context.RunCommand(
                "Leave lobby", () => context.Controller.LeaveLobbyAsync(
                    context.CancellationToken)), quiet: true));
            leaveGroupedWithPrimary = true;
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
        if (!requiresConfirmation)
        {
            context.Ui.CancelLeaveConfirmation(lobby.LobbyId);
            if (!leaveGroupedWithPrimary)
            {
                content.Children.Add(Button("Leave lobby", () => context.RunCommand(
                    "Leave lobby", () => context.Controller.LeaveLobbyAsync(
                        context.CancellationToken)), quiet: true));
            }
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
        if (message.Contains("configured team", StringComparison.OrdinalIgnoreCase)
            || message.Contains("both teams", StringComparison.OrdinalIgnoreCase))
            return "Team mode needs players on every configured team.";
        if (message.Contains("All players must be Ready", StringComparison.OrdinalIgnoreCase))
            return "Waiting for all players.";
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

    private static PrimeCompactLobbyRow CreateCompactMatchCard(
        PlayPresentationContext context, LobbyListEntry entry)
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
        return new PrimeCompactLobbyRow(entry, join, waitlist, spectate);
    }

    private static StackPanel Page(string title, string kicker, string subtitle,
        Control? trailingContent = null)
    {
        var root = new StackPanel { Spacing = 14, HorizontalAlignment = HorizontalAlignment.Stretch };
        root.Children.Add(PrimeControlFactory.PageHeading(title, kicker, subtitle,
            trailingContent));
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
        first.Text = step == 1 ? "1 · Lobby & seats" : "✓ · Lobby & seats";
        second.Text = "2 · Mission & rules";
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

    /// <summary>
    /// Shows the effective mode default in an editor while retaining the
    /// nullable draft semantics: an untouched value still means "use the
    /// server default" on the wire. The first edit replaces the displayed
    /// default, so a player never has to decode a placeholder watermark.
    /// </summary>
    private static TextBox RuleEditor(string value, string effectiveValue,
        Action<string> set)
    {
        bool showingDefault = String.IsNullOrEmpty(value);
        TextBox editor = Editor(showingDefault ? effectiveValue : value, "");
        editor.GotFocus += (_, _) =>
        {
            if (!showingDefault) return;
            editor.SelectAll();
            showingDefault = false;
        };
        editor.TextChanged += (_, _) =>
        {
            string current = editor.Text ?? "";
            if (showingDefault && StringComparer.Ordinal.Equals(current,
                effectiveValue)) return;
            showingDefault = false;
            set(current);
        };
        return editor;
    }

    private static string DamageLabel(int value) => value switch
    {
        0 => "Low",
        2 => "High",
        _ => "Normal"
    };

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
        PrimeAccessibility.SetName(editor, label);
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
        Control preview = context.BuildPreview(mapPath, height,
            "No local map preview is available for this arena.");
        if (mapPath is null && preview is PrimePreviewStage stage)
        {
            // Keep the shell-owned preview stage (and its existing lifetime
            // contract) while replacing only its missing-image content with
            // the shared deterministic map fallback.
            stage.Child = new PrimeMapFallback(PrimeGameText.MapName(mapKey),
                MapInstallSource.BundledPackage);
        }
        preview.Height = height;
        return preview;
    }

    /// <summary>
    /// Stable active-lobby composition.  The shell can rebuild its route from
    /// an authoritative PlayState, but a revision for the same lobby updates
    /// these bounded regions in place so map previews, layout controls, and
    /// chat editing are not discarded on every roster or message change.
    /// </summary>
    private sealed class LobbyPresentationView : ILobbyPresentationView
    {
        private PlayPresentationContext _context;
        private LobbySnapshot _lobby;
        private readonly PrimePlayResponsivePanel _view;
        private readonly PrimeFieldGrid _header;
        private readonly Border _headingHost;
        private readonly Border _headerStatusHost;
        private readonly Border _connectionHost;
        private readonly LobbyMissionPanel _mission;
        private readonly PrimeFieldGrid _rosterGroups;
        private readonly ScrollViewer _rosterScroll;
        private readonly PrimeSectionPanel _commandRail;
        private readonly StackPanel _commandContent;
        private readonly Border _memberHost;
        private readonly Border _actionsHost;
        private readonly Border _queueHost;
        private readonly LobbyChatPanel _chat;
        private readonly Dictionary<string, LobbyRosterGroup> _rosterViews =
            new(StringComparer.Ordinal);
        private bool? _connectionVisible;

        public LobbyPresentationView(PlayPresentationContext context,
            LobbySnapshot lobby)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(lobby);
            _context = context;
            _lobby = lobby;
            _view = new PrimePlayResponsivePanel(wideLeftWeight: 7,
                wideRightWeight: 4, compactColumns: true)
            {
                Spacing = 5
            };
            _view.Classes.Add("prime-lobby-layout");

            (Guid? sessionId, LobbyMember? current, bool owner) = ResolveUser(
                context, lobby);
            LobbyWaitlistSnapshot waitlist = lobby.Waitlist
                ?? LobbyWaitlistSnapshot.Empty;

            _header = new PrimeFieldGrid(maximumColumns: 2,
                minimumColumnWidth: 360);
            _headingHost = new Border { Child = PrimeControlFactory.PageHeading(
                lobby.Name, "ACTIVE LOBBY",
                "Review the mission, roster, and readiness.") };
            _header.Children.Add(_headingHost);
            _headerStatusHost = new Border
            {
                Child = BuildLobbyHeader(lobby, current, waitlist)
            };
            _header.Children.Add(_headerStatusHost);
            AddDashboardChild(_view, _header, PrimePlayLane.Full, 0);

            _connectionHost = new Border();
            AddDashboardChild(_view, _connectionHost, PrimePlayLane.Full, 1);

            _mission = new LobbyMissionPanel(context, lobby, owner);
            AddDashboardChild(_view, _mission.View, PrimePlayLane.Left, 3);

            _rosterGroups = new PrimeFieldGrid(maximumColumns: 2,
                minimumColumnWidth: 280);
            _rosterScroll = new ScrollViewer
            {
                Content = _rosterGroups,
                MaxHeight = 340,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            _rosterScroll.Classes.Add("prime-roster-scroll");
            AddDashboardChild(_view, _rosterScroll, PrimePlayLane.Left, 5);

            _memberHost = new Border();
            _actionsHost = new Border();
            _commandContent = Stack(_actionsHost);
            _commandRail = new PrimeSectionPanel(_commandContent);
            AddDashboardChild(_view, _memberHost, PrimePlayLane.Right, 4);
            AddDashboardChild(_view, _commandRail, PrimePlayLane.Right, 2);

            _queueHost = new Border();
            AddDashboardChild(_view, _queueHost, PrimePlayLane.Right, 6);

            _chat = new LobbyChatPanel(lobby.Chat, context.Ui.ChatDraft,
                context.Ui.SetChatDraft,
                text => SendChat(_context, text),
                context.Ui.SetChatEditing,
                context.Ui.ChatScrollOffset,
                context.Ui.ChatScrollPositionKnown,
                context.Ui.SetChatScrollOffset,
                context.Ui.ChatUnreadCount,
                context.Ui.MarkChatRead,
                sessionId);
            TrackEditor(context, _chat.DraftEditor);
            AddDashboardChild(_view, _chat, PrimePlayLane.Right, 7);

            PopulateRoster(_rosterGroups, lobby, sessionId);
            UpdateConnection(context);
            _memberHost.Child = BuildMemberControls(context, lobby, current,
                context.State);
            _actionsHost.Child = BuildLobbyActions(context, lobby, owner,
                context.State);
            UpdateQueue(context, lobby, current, waitlist);
            _view.LayoutChanged += _ => ApplyLobbySizing();
            _view.ViewportChanged += _ => ApplyLobbySizing();
            ApplyLobbySizing();
        }

        public Guid LobbyId => _lobby.LobbyId;

        public Control View => _view;

        public LobbyChatPanel ChatPanel => _chat;

        public void Update(PlayPresentationContext context, LobbySnapshot lobby)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(lobby);
            if (lobby.LobbyId != LobbyId)
                throw new InvalidOperationException(
                    "A lobby presentation cannot be updated with another lobby identity.");

            _context = context;
            _lobby = lobby;
            (Guid? sessionId, LobbyMember? current, bool owner) = ResolveUser(
                context, lobby);
            LobbyWaitlistSnapshot waitlist = lobby.Waitlist
                ?? LobbyWaitlistSnapshot.Empty;

            UpdateHeading(lobby.Name);
            _headerStatusHost.Child = BuildLobbyHeader(lobby, current, waitlist);
            UpdateConnection(context);
            _mission.Update(context, lobby, owner);
            PopulateRoster(_rosterGroups, lobby, sessionId);
            _memberHost.Child = BuildMemberControls(context, lobby, current,
                context.State);
            _actionsHost.Child = BuildLobbyActions(context, lobby, owner,
                context.State);
            UpdateQueue(context, lobby, current, waitlist);
            _chat.Update(lobby.Chat, context.Ui.ChatDraft,
                context.Ui.ChatScrollOffset,
                context.Ui.ChatScrollPositionKnown,
                context.Ui.ChatUnreadCount,
                sessionId);
            ApplyLobbySizing();
        }

        private void UpdateHeading(string name)
        {
            if (_headingHost.Child is PrimePageHeading heading)
            {
                if (StringComparer.Ordinal.Equals(heading.Title, name)) return;
                heading.Title = name;
                return;
            }
            _headingHost.Child = PrimeControlFactory.PageHeading(name,
                "ACTIVE LOBBY",
                "Review the mission, roster, and readiness.");
        }

        private void UpdateConnection(PlayPresentationContext context)
        {
            bool visible = context.State.Node?.Session is null
                || NodeSessions.Current is { Connected: false };
            if (_connectionVisible == visible) return;
            _connectionVisible = visible;
            _connectionHost.IsVisible = visible;
            _connectionHost.Child = visible
                ? Section(Stack(
                    Text("Connection interrupted. Reconnect to keep your place in this lobby.",
                        "prime-body"),
                    Button("Reconnect", () => _context.RunCommand("Reconnect",
                        () => _context.Controller.ResumeAsync(
                            _context.CancellationToken)), primary: true)))
                : null;
        }

        private void UpdateQueue(PlayPresentationContext context,
            LobbySnapshot lobby, LobbyMember? current,
            LobbyWaitlistSnapshot waitlist)
        {
            Control? queue = BuildQueuePanel(context, lobby, current, waitlist);
            _queueHost.Child = queue;
            _queueHost.IsVisible = queue is not null;
        }

        private void ApplyLobbySizing()
        {
            double viewportHeight = _view.ViewportSize.Height;
            _mission.Preview.Height = LobbyPreviewHeight(_view.Layout,
                viewportHeight);
            _rosterScroll.MaxHeight = LobbyRosterHeight(_view.Layout,
                viewportHeight);
            _chat.HistoryMaxHeight = LobbyChatHeight(_view.Layout,
                viewportHeight);
            bool dense = _view.Layout == PrimeContentLayout.Wide;
            _commandRail.Padding = new Thickness(dense ? 4 : 16);
            _commandRail.Margin = dense ? default : new Thickness(0, 0, 0, 12);
            _commandContent.Spacing = dense ? 4 : 8;
            if (_memberHost.Child is StackPanel memberStack)
                memberStack.Spacing = dense ? 6 : 8;
            if (_actionsHost.Child is StackPanel actionStack)
                actionStack.Spacing = dense ? 6 : 8;
            _chat.CompactChrome = dense;
        }

        private static (Guid? SessionId, LobbyMember? Current, bool Owner)
            ResolveUser(PlayPresentationContext context, LobbySnapshot lobby)
        {
            Guid? sessionId = context.State.Node?.Session?.SessionId;
            LobbyMember? current = sessionId is { } id
                ? lobby.Members.FirstOrDefault(member => member.SessionId == id)
                : null;
            bool owner = sessionId is { } ownerSession
                && lobby.OwnerSessionId == ownerSession;
            return (sessionId, current, owner);
        }

        private void PopulateRoster(PrimeFieldGrid rosterGroups,
            LobbySnapshot lobby, Guid? sessionId)
        {
            bool teamMode = lobby.Mode.IsTeamMode();
            LobbyMember[] players = lobby.Members
                .Where(member => !member.Observer).ToArray();
            LobbyMember[] observers = lobby.Members
                .Where(member => member.Observer).ToArray();
            var groups = new List<(string Key, IEnumerable<LobbyMember> Members,
                IBrush Accent, bool ShowTeam)>();
            if (!teamMode)
            {
                groups.Add(("PLAYERS", players, GuiTheme.WarningBrush, false));
            }
            else
            {
                int teamCount = lobby.Rules?.TeamCount ?? 2;
                for (byte team = 0; team < teamCount; team++)
                {
                    groups.Add(($"TEAM {team + 1}",
                        players.Where(member => member.Team == team).ToArray(),
                        team % 2 == 0 ? GuiTheme.WarningBrush : GuiTheme.TechBrush,
                        true));
                }
            }
            groups.Add(("OBSERVERS", observers, GuiTheme.TechBrush, false));

            rosterGroups.Children.Clear();
            var active = new HashSet<string>(StringComparer.Ordinal);
            foreach ((string key, IEnumerable<LobbyMember> members, IBrush accent,
                bool showTeam) in groups)
            {
                active.Add(key);
                if (!_rosterViews.TryGetValue(key, out LobbyRosterGroup? group))
                {
                    group = new LobbyRosterGroup(key, accent, showTeam);
                    _rosterViews.Add(key, group);
                }
                group.Update(members, sessionId, lobby.OwnerSessionId);
                rosterGroups.Children.Add(group);
            }
            foreach (string stale in _rosterViews.Keys.Where(key => !active.Contains(key))
                .ToArray())
                _rosterViews.Remove(stale);
        }
    }

    /// <summary>One keyed roster group whose member rows survive lobby revisions.</summary>
    private sealed class LobbyRosterGroup : PrimeSectionPanel
    {
        private readonly string _title;
        private readonly IBrush _accent;
        private readonly bool _showTeam;
        private readonly StackPanel _content;
        private readonly TextBlock _count;
        private readonly Dictionary<Guid, PrimeSelectedRow> _rows = new();
        private readonly Dictionary<Guid, LobbyMember> _members = new();
        private readonly Dictionary<Guid, TextBlock> _names = new();
        private readonly Dictionary<Guid, TextBlock> _details = new();
        private readonly Dictionary<Guid, PrimeStatusChip> _statuses = new();

        public LobbyRosterGroup(string title, IBrush accent, bool showTeam)
            : base(new StackPanel { Spacing = 6 })
        {
            _title = title;
            _accent = accent;
            _showTeam = showTeam;
            _content = (StackPanel)Child!;
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            TextBlock heading = Text(title, "prime-section-heading");
            heading.Foreground = accent;
            header.Children.Add(heading);
            _count = Text("", "prime-label");
            header.Children.Add(_count);
            Grid.SetColumn(_count, 1);
            _content.Children.Add(header);
            Classes.Add("prime-lobby-roster-group");
            PrimeAccessibility.SetName(this, title);
        }

        public void Update(IEnumerable<LobbyMember> members, Guid? sessionId,
            Guid ownerSessionId)
        {
            LobbyMember[] list = members.ToArray();
            _count.Text = _title.Equals("OBSERVERS", StringComparison.Ordinal)
                ? $"{list.Length} observing"
                : $"{list.Length} player{(list.Length == 1 ? "" : "s")}";
            var current = list.Select(member => member.SessionId).ToHashSet();
            foreach (Guid stale in _rows.Keys.Where(id => !current.Contains(id)).ToArray())
            {
                _content.Children.Remove(_rows[stale]);
                _rows.Remove(stale);
                _members.Remove(stale);
                _names.Remove(stale);
                _details.Remove(stale);
                _statuses.Remove(stale);
            }

            int insertIndex = 1;
            foreach (LobbyMember member in list)
            {
                bool isCurrent = sessionId == member.SessionId;
                bool isOwner = ownerSessionId == member.SessionId;
                if (!_rows.TryGetValue(member.SessionId, out PrimeSelectedRow? row))
                {
                    row = CreateRow(member, isCurrent, isOwner);
                    _rows.Add(member.SessionId, row);
                    _content.Children.Add(row);
                }
                UpdateRow(member, isCurrent, isOwner);
                int existingIndex = _content.Children.IndexOf(row);
                if (existingIndex >= 0 && existingIndex != insertIndex)
                {
                    _content.Children.Remove(row);
                    _content.Children.Insert(Math.Min(insertIndex, _content.Children.Count), row);
                }
                insertIndex++;
            }

            Control? empty = _content.Children.OfType<PrimeEmptyState>().FirstOrDefault();
            if (list.Length == 0)
            {
                if (empty is null)
                    _content.Children.Add(new PrimeEmptyState(
                        _title.Equals("OBSERVERS", StringComparison.Ordinal)
                            ? "No observers." : "No players yet."));
            }
            else if (empty is not null)
                _content.Children.Remove(empty);
        }

        private PrimeSelectedRow CreateRow(LobbyMember member, bool isCurrent,
            bool isOwner)
        {
            var line = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                ColumnSpacing = 8,
                MinHeight = 44
            };
            TextBlock name = Text(member.DisplayName, "prime-body");
            TextBlock detail = Text("", "prime-muted");
            var identity = Stack(name, detail);
            line.Children.Add(identity);
            var status = new PrimeStatusChip("", LobbyMemberStatusBrush(member));
            line.Children.Add(status);
            Grid.SetColumn(status, 1);
            var row = PrimeControlFactory.SelectedRow(line, selected: false);
            _names[member.SessionId] = name;
            _details[member.SessionId] = detail;
            _statuses[member.SessionId] = status;
            PrimeAccessibility.SetName(row, $"Lobby member: {member.DisplayName}");
            return row;
        }

        private void UpdateRow(LobbyMember member, bool isCurrent, bool isOwner)
        {
            _members[member.SessionId] = member;
            _names[member.SessionId].Text = member.DisplayName;
            _details[member.SessionId].Text = LobbyMemberDetail(member, _showTeam,
                isCurrent, isOwner);
            string state = member.Observer ? "Observer"
                : member.Ready ? "Ready" : "Not ready";
            _statuses[member.SessionId].Text = state;
            if (_statuses[member.SessionId].Child is TextBlock text)
                text.Foreground = LobbyMemberStatusBrush(member);
            PrimeSelectedRow row = _rows[member.SessionId];
            row.BorderBrush = isCurrent ? GuiTheme.BrandBrush : GuiTheme.GunmetalBrush;
            row.BorderThickness = new Thickness(3, 1, 1, 1);
            PrimeAccessibility.SetDescription(row,
                $"{LobbyMemberDetail(member, _showTeam, isCurrent, isOwner)}. {state}.");
        }
    }

    /// <summary>Mission region whose preview is replaced only when the map key changes.</summary>
    private sealed class LobbyMissionPanel
    {
        private readonly PrimeSectionPanel _view;
        private readonly StackPanel _content;
        private readonly Grid _header;
        private readonly Border _previewHost;
        private readonly Border _statsHost;
        private readonly PrimeStatRail _stats;
        private readonly Dictionary<string, PrimeStatTile> _statTiles = new(
            StringComparer.Ordinal);
        private readonly TextBlock _mapName;
        private readonly TextBlock _details;
        private string _mapKey;
        private AvaloniaButton? _editButton;
        private PlayPresentationContext _context;
        private LobbySnapshot _lobby;

        public LobbyMissionPanel(PlayPresentationContext context,
            LobbySnapshot lobby, bool owner)
        {
            _view = new PrimeSectionPanel(new StackPanel { Spacing = 8 });
            _content = (StackPanel)_view.Child!;
            _context = context;
            _lobby = lobby;
            _mapKey = lobby.MapKey;

            _header = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                ColumnSpacing = 12
            };
            _header.Children.Add(Text("MISSION", "prime-kicker"));
            _content.Children.Add(_header);

            _mapName = Text(PrimeGameText.MapName(lobby.MapKey), "prime-heading");
            _content.Children.Add(_mapName);

            _previewHost = new Border
            {
                Child = BuildMapPreview(context, lobby.MapKey, 260)
            };
            _stats = BuildStats();
            _statsHost = new Border { Child = _stats };
            _details = Text("", "prime-muted");
            var body = new PrimeFieldGrid(maximumColumns: 2,
                minimumColumnWidth: 260)
            {
                ColumnSpacing = 12,
                RowSpacing = 10
            };
            body.Children.Add(_previewHost);
            body.Children.Add(Stack(_statsHost, _details));
            _content.Children.Add(body);
            Update(context, lobby, owner);
        }

        public Control Preview => _previewHost.Child!;

        public Control View => _view;

        public void Update(PlayPresentationContext context,
            LobbySnapshot lobby, bool owner)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(lobby);
            _context = context;
            _lobby = lobby;
            if (!StringComparer.Ordinal.Equals(_mapKey, lobby.MapKey))
            {
                _mapKey = lobby.MapKey;
                _previewHost.Child = BuildMapPreview(context, lobby.MapKey, 260);
            }
            _mapName.Text = PrimeGameText.MapName(lobby.MapKey);
            UpdateStats(lobby);
            UpdateEditButton(owner && lobby.Phase == LobbyPhase.Open);
        }

        private void UpdateStats(LobbySnapshot lobby)
        {
            LobbyRulesOptions rules = LobbyRuleApplicability.RulesFromLobby(lobby);
            LobbyRuleApplicability applicability = LobbyRuleApplicability.For(lobby.Mode);
            SetStat("MODE", PrimeGameText.ModeLabel(lobby.Mode));
            SetStat("TIME", LobbyRuleDefaults.Time(lobby.Mode, rules.TimeLimitSeconds));
            SetStat("SCORE", applicability.ScoreGoal
                ? LobbyRuleDefaults.Score(lobby.Mode, rules.ScoreGoal) : "",
                applicability.ScoreGoal);
            SetStat("LIVES", applicability.StartingLives
                ? LobbyRuleDefaults.Lives(lobby.Mode, rules.StartingLives) : "",
                applicability.StartingLives);
            SetStat("OBJECTIVE", applicability.ObjectiveTimeGoal
                ? LobbyRuleDefaults.ObjectiveTime(lobby.Mode,
                    rules.ObjectiveTimeGoalSeconds) : "",
                applicability.ObjectiveTimeGoal);
            SetStat("BOTS", lobby.BotCount.ToString(CultureInfo.InvariantCulture));
            SetStat("BOT DIFFICULTY", lobby.BotCount > 0
                ? PrimeGameText.BotDifficultyLabel(lobby.BotDifficulty) : "OFF");
            SetStat("SEATS", lobby.PlayerLimit.ToString(CultureInfo.InvariantCulture));
            SetStat("RESOURCE RADAR", LobbyRuleDefaults.ResourceRadar(
                rules.ResourceRadarPolicy));
            SetStat("BALANCED", (rules.BalancedMode ?? false) ? "ON" : "OFF");
            _details.Text = $"{PrimeGameText.SeatPolicyLabel(lobby.SeatPolicy)} · "
                + $"{lobby.ObserverLimit} observer seats · "
                + $"{lobby.Waitlist?.Count ?? 0} waiting";
        }

        private PrimeStatRail BuildStats()
        {
            var tiles = new List<PrimeStatTile>
            {
                CreateStat("MODE"),
                CreateStat("TIME"),
                CreateStat("SCORE"),
                CreateStat("LIVES"),
                CreateStat("OBJECTIVE"),
                CreateStat("BOTS"),
                CreateStat("BOT DIFFICULTY"),
                CreateStat("SEATS"),
                CreateStat("RESOURCE RADAR"),
                CreateStat("BALANCED")
            };
            PrimeStatRail rail = PrimeControlFactory.StatRail(tiles.ToArray());
            rail.Classes.Add("prime-lobby-stat-rail");
            return rail;
        }

        private PrimeStatTile CreateStat(string label)
        {
            PrimeStatTile tile = PrimeControlFactory.StatTile(label, "");
            _statTiles.Add(label, tile);
            return tile;
        }

        private void SetStat(string label, string value, bool visible = true)
        {
            PrimeStatTile tile = _statTiles[label];
            tile.IsVisible = visible;
            if (tile.Child is StackPanel content
                && content.Children.OfType<TextBlock>().Skip(1).FirstOrDefault() is { } text)
                text.Text = value;
        }

        private void UpdateEditButton(bool visible)
        {
            if (visible && _editButton is null)
            {
                _editButton = Button("Edit Match", () =>
                {
                    _context.Ui.BeginEdit(_lobby);
                    _context.Ui.EditMatchOpen = true;
                    _context.Refresh();
                }, quiet: true);
                _header.Children.Add(_editButton);
                Grid.SetColumn(_editButton, 1);
            }
            else if (!visible && _editButton is not null)
            {
                _header.Children.Remove(_editButton);
                _editButton = null;
            }
        }
    }

    private static double HostPreviewHeight(PrimeContentLayout layout,
        double viewportHeight)
    {
        double height = EffectiveViewportHeight(viewportHeight);
        return layout switch
        {
            PrimeContentLayout.Wide => Math.Clamp(96 + (height - 568) * 0.25,
                96, 260),
            PrimeContentLayout.Medium => Math.Clamp(200 + (height - 568) * 0.26,
                180, 290),
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
            PrimeContentLayout.Medium => Math.Clamp(190 + (height - 568) * 0.28,
                150, 300),
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
            PrimeContentLayout.Wide => Math.Clamp(94 + (height - 568) * 0.36,
                72, 270),
            PrimeContentLayout.Medium => Math.Clamp(150 + (height - 568) * 0.28,
                100, 250),
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
            PrimeContentLayout.Wide => Math.Clamp(72 + (height - 568) * 0.24,
                64, 230),
            PrimeContentLayout.Medium => Math.Clamp(100 + (height - 568) * 0.24,
                64, 210),
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

    private static string SpawnPolicyLabel(SpawnPolicy policy) => policy switch
    {
        SpawnPolicy.Enhanced => "Enhanced",
        SpawnPolicy.Duel => "Duel",
        _ => "Classic"
    };

    private sealed record BoolChoice(string Label, bool? Value)
    {
        public override string ToString() => Label;
    }

    private sealed record DamageChoice(string Label, int? Value)
    {
        public override string ToString() => Label;
    }

    private sealed record BotDifficultyChoice(BotDifficulty Value, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record SpawnPolicyChoice(string Label, SpawnPolicy? Value)
    {
        public override string ToString() => Label;
    }

    private sealed record ResourceRadarPolicyChoice(ResourceRadarPolicy Value,
        string Label)
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
