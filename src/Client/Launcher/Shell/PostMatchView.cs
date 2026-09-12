using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using ProjectPrime.Server.Shared;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher.Presentation;
using MphRead.Mods.Launcher.Theme;
using AvaloniaButton = Avalonia.Controls.Button;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>
/// Native results view shown over the persistent SDL host. The result and
/// ballot projections are immutable; the host remains responsible for Node
/// commands and match continuation.
/// </summary>
public sealed class PostMatchView : UserControl, IDisposable
{
    internal const double CompactResultsBreakpoint = 900;
    private const double DefaultResultsHeight = 1080;
    private const double CompactScoreMaxHeight = 260;
    private const double CompactBallotMaxHeight = 240;
    private const double WideScoreMaxHeight = 440;
    private const double WideBallotMaxHeight = 440;
    private readonly StackPanel _scoreboard = new() { Spacing = 6 };
    private readonly StackPanel _ballot = new() { Spacing = 8 };
    private readonly Grid _zones;
    private readonly Grid _scoreZone;
    private readonly Grid _voteZone;
    private readonly ScrollViewer _scoreScroll;
    private readonly ScrollViewer _ballotScroll;
    private readonly TextBlock _status = new();
    private readonly TextBlock _leading = new();
    private readonly ComboBox _hunterSelector;
    private readonly TextBlock _hunterHelp;
    private readonly TextBlock _hints;
    private readonly AvaloniaButton _leaveButton;
    private readonly AvaloniaButton _cancelLeaveButton;
    private readonly List<AvaloniaButton> _cards = new();
    private readonly List<Bitmap> _bitmaps = new();
    private readonly List<Task> _previewLoads = new();
    private readonly PreviewImageService _images = new();
    private readonly MapPreviewService _mapPreviews;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly PostMatchResultsModel _results;
    private readonly IBrush _selectedBallotBorderBrush;
    private readonly IBrush _selectedBallotSurfaceBrush;
    private readonly EventHandler<SizeChangedEventArgs> _sizeChangedHandler;
    private NodeRoundSnapshot? _round;
    private NodeRoundSnapshot? _projectedRound;
    private long _projectionCountdown = long.MinValue;
    private bool _projectionInitialized;
    private ImmutableArray<PostMatchBallotOption> _displayOptions = [];
    private uint? _renderedRevision;
    private bool _disposed;
    private bool _leaveConfirmationPending;
    private int _renderGeneration;
    private string _optionSignature = "";
    private PostMatchBallotModel _ballotModel = PostMatchBallotModel.Loading;
    private bool _compactLayout;
    private bool _layoutInitialized;
    private bool _scoreboardUsesMobileCards;
    private PrimeInputDevice _lastInputDevice = PrimeInputDevice.KeyboardMouse;
    private ControllerFamily _lastControllerFamily = ControllerFamily.Generic;
    private bool _continuationLoading;
    private MatchTransitionView? _continuationView;
    private Hunter? _authoritativeHunter;
    private Hunter? _pendingHunter;
    private bool _canSelectHunter;
    private bool _syncingHunter;

    public PostMatchSelection Selection { get; } = new();
    public PostMatchResultsModel Results => _results;
    public PostMatchBallotModel Ballot => _ballotModel;
    internal bool IsCompactLayout => _compactLayout;
    internal bool ScoreboardUsesMobileCards => _scoreboardUsesMobileCards;
    internal PrimeInputDevice LastInputDevice => _lastInputDevice;
    internal string InputHint => _hints.Text ?? "";
    internal bool InputHintVisible => _hints.IsVisible;
    internal bool IsContinuationLoading => _continuationLoading;
    internal ComboBox HunterSelector => _hunterSelector;
    internal bool HunterChangePending => _pendingHunter.HasValue;
    internal PostMatchPresentationMode PresentationMode => _continuationLoading
        ? PostMatchPresentationMode.ContinuationLoading : PostMatchPresentationMode.Results;
    public event Action<byte>? VoteRequested;
    public event Action<Hunter>? HunterRequested;
    public event Action? LeaveRequested;

    public PostMatchView(MatchResultsSnapshot? results, int localSlot = -1)
    {
        _results = PostMatchResultsBuilder.Build(results, localSlot);
        _mapPreviews = new MapPreviewService(_images);

        Focusable = true;
        Background = GuiTheme.InkBrush;
        FontFamily = GuiTheme.Display;

        _zones = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("1.15*,0.85*"),
            ColumnSpacing = 16,
            Margin = new Thickness(20)
        };

        _scoreZone = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Margin = new Thickness(0, 0, 8, 0),
            ClipToBounds = true
        };
        _scoreZone.Children.Add(BuildResultsHeader());
        _scoreScroll = new ScrollViewer
        {
            Name = "ResultsScoreScroll",
            Content = _scoreboard,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = WideScoreMaxHeight,
            Padding = new Thickness(0, 10, 6, 0)
        };
        Grid.SetRow(_scoreScroll, 1);
        _scoreZone.Children.Add(_scoreScroll);
        _zones.Children.Add(_scoreZone);

        _voteZone = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto,Auto"),
            ClipToBounds = true
        };
        Grid.SetColumn(_voteZone, 1);
        _zones.Children.Add(_voteZone);

        var voteHeader = new StackPanel { Spacing = 3 };
        voteHeader.Children.Add(Text("NEXT ROUND", 20, FontWeight.SemiBold, GuiTheme.AccentBrush));
        voteHeader.Children.Add(Text("Choose what happens after the results", 12, FontWeight.Normal, GuiTheme.TextDimBrush));
        _voteZone.Children.Add(voteHeader);

        var status = new StackPanel { Spacing = 3, Margin = new Thickness(0, 10, 0, 0) };
        _status.TextWrapping = TextWrapping.Wrap;
        _status.FontSize = 13;
        _status.FontWeight = FontWeight.SemiBold;
        _status.Foreground = GuiTheme.TextBrush;
        status.Children.Add(_status);
        _leading.TextWrapping = TextWrapping.Wrap;
        _leading.FontSize = 11;
        _leading.Foreground = GuiTheme.TextDimBrush;
        status.Children.Add(_leading);
        var hunterPanel = new StackPanel
        {
            Spacing = 5,
            Margin = new Thickness(0, 10, 0, 2)
        };
        hunterPanel.Children.Add(Text("NEXT ROUND HUNTER", 10,
            FontWeight.SemiBold, GuiTheme.AccentBrush));
        Hunter[] hunterValues = Enum.GetValues<Hunter>()
            .Where(value => value <= Hunter.Weavel).ToArray();
        _hunterSelector = new ComboBox
        {
            Name = "ResultsNextHunter",
            ItemsSource = hunterValues,
            MinWidth = 180,
            MinHeight = 44,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<Hunter>(
                (value, _) => Text(PrimeGameText.HunterLabel(value), 12,
                    FontWeight.Normal, GuiTheme.TextBrush))
        };
        _hunterSelector.SelectionChanged += (_, _) => RequestHunterSelection();
        hunterPanel.Children.Add(_hunterSelector);
        _hunterHelp = Text("Choose before the ballot resolves.", 10,
            FontWeight.Normal, GuiTheme.TextDimBrush);
        _hunterHelp.TextWrapping = TextWrapping.Wrap;
        hunterPanel.Children.Add(_hunterHelp);
        status.Children.Add(hunterPanel);
        Grid.SetRow(status, 1);
        _voteZone.Children.Add(status);

        _ballotScroll = new ScrollViewer
        {
            Name = "ResultsBallotScroll",
            Content = _ballot,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = WideBallotMaxHeight,
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(0, 0, 6, 0)
        };
        Grid.SetRow(_ballotScroll, 2);
        _voteZone.Children.Add(_ballotScroll);

        _hints = Text("", 10, FontWeight.Normal, GuiTheme.TextDimBrush);
        _hints.TextWrapping = TextWrapping.Wrap;
        _hints.Margin = new Thickness(0, 8, 0, 8);
        Grid.SetRow(_hints, 3);
        _voteZone.Children.Add(_hints);

        _leaveButton = BuildActionButton("Leave Lobby", RequestLeave);
        _leaveButton.Name = "ResultsLeaveLobby";
        _leaveButton.MinHeight = 48;
        Grid.SetRow(_leaveButton, 4);
        _voteZone.Children.Add(_leaveButton);

        _cancelLeaveButton = BuildActionButton("Cancel", CancelLeaveConfirmation);
        _cancelLeaveButton.Name = "ResultsCancelLeave";
        _cancelLeaveButton.MinHeight = 48;
        _cancelLeaveButton.BorderBrush = GuiTheme.EdgeBrush;
        ToolTip.SetTip(_cancelLeaveButton, "Stay in the results screen.");
        Grid.SetRow(_cancelLeaveButton, 5);
        _voteZone.Children.Add(_cancelLeaveButton);

        _sizeChangedHandler = (_, _) => ApplyResponsiveLayout();
        SizeChanged += _sizeChangedHandler;
        UpdateLeaveConfirmation();

        Content = _zones;
        _selectedBallotBorderBrush = ResolveThemeBrush(
            PrimeVisualTokens.BrushKey(PrimeSemanticColor.Brand),
            ResolveThemeBrush(PrimeVisualTokens.BrushKey(PrimeSemanticColor.Accent),
                GuiTheme.AccentBrush));
        _selectedBallotSurfaceBrush = ResolveThemeBrush(PrimeVisualTokens.BrandSurfaceBrush,
            ResolveThemeBrush(PrimeVisualTokens.SelectedSurfaceBrush, GuiTheme.BrandSurfaceBrush));
        ApplyResponsiveLayout();
        Update(null);
    }

    private Control BuildResultsHeader()
    {
        var header = new StackPanel { Spacing = 4 };
        IBrush outcomeBrush = _results.Outcome switch
        {
            PostMatchOutcome.Victory => GuiTheme.GoodBrush,
            PostMatchOutcome.Defeat => GuiTheme.BadBrush,
            PostMatchOutcome.Draw => GuiTheme.WarmBrush,
            _ => GuiTheme.TextBrush
        };
        header.Children.Add(Text(_results.OutcomeLabel, 26, FontWeight.SemiBold, outcomeBrush));
        header.Children.Add(Text("MATCH RESULTS", 11, FontWeight.SemiBold, GuiTheme.AccentBrush));
        if (_results.HasAuthoritativeResult)
        {
            string map = string.IsNullOrWhiteSpace(_results.MapKey) ? "Map unavailable" : _results.MapKey;
            string metadata = $"{map}  ·  {_results.ModeLabel}";
            if (_results.EndReasonLabel is { } reason) metadata += $"  ·  {reason}";
            header.Children.Add(Text(metadata, 12, FontWeight.Normal, GuiTheme.TextDimBrush));
            if (_results.Outcome == PostMatchOutcome.Unknown && !_results.HasLocalPlayer)
                header.Children.Add(Text("Your player identity is not available for this result.", 11,
                    FontWeight.Normal, GuiTheme.TextDimBrush));
        }
        else
        {
            header.Children.Add(Text("Match results are unavailable.", 12,
                FontWeight.Normal, GuiTheme.TextDimBrush));
        }
        return header;
    }

    private void BuildScoreboard(bool compact = false)
    {
        _scoreboardUsesMobileCards = compact;
        _scoreboard.Children.Clear();
        if (!_results.HasAuthoritativeResult)
        {
            _scoreboard.Children.Add(EmptyPanel("Match results are unavailable."));
            return;
        }
        if (!_results.HasScoreboard)
        {
            _scoreboard.Children.Add(EmptyPanel("No player results were received."));
            return;
        }

        _scoreboard.Children.Add(compact ? BuildMobileScoreHeader() : BuildScoreHeader());
        foreach (PostMatchScoreRow row in _results.Scoreboard)
            _scoreboard.Children.Add(compact ? BuildMobileScoreRow(row) : BuildScoreRow(row));
    }

    private void ApplyResponsiveLayout()
    {
        if (_continuationLoading || _disposed) return;
        double width = Bounds.Width > 0 ? Bounds.Width : Width;
        bool compact = double.IsFinite(width) && width > 0
            && width < CompactResultsBreakpoint;
        if (!_layoutInitialized || compact != _compactLayout)
        {
            _layoutInitialized = true;
            _compactLayout = compact;
            _zones.ColumnDefinitions = compact
                ? new ColumnDefinitions("*")
                : new ColumnDefinitions("1.15*,0.85*");
            _zones.RowDefinitions = compact
                ? new RowDefinitions("Auto,Auto")
                : new RowDefinitions("Auto");
            Grid.SetColumn(_scoreZone, 0);
            Grid.SetColumn(_voteZone, compact ? 0 : 1);
            Grid.SetRow(_scoreZone, 0);
            Grid.SetRow(_voteZone, compact ? 1 : 0);
            _scoreZone.Margin = compact
                ? new Thickness()
                : new Thickness(0, 0, 8, 0);
            BuildScoreboard(compact);
        }
        ApplyScrollBounds();
    }

    private void ApplyScrollBounds()
    {
        double height = Bounds.Height > 0 ? Bounds.Height : Height;
        if (!double.IsFinite(height) || height <= 0) height = DefaultResultsHeight;

        if (_compactLayout)
        {
            // Budget the fixed header, status, hint, action, and outer-margin
            // bands before splitting the remaining short-screen space. A
            // 160-DIP minimum for both scroll regions made the Leave action
            // unreachable immediately below the compact breakpoint.
            double flexibleHeight = Math.Max(0, height - 390);
            _scoreScroll.MaxHeight = Math.Min(CompactScoreMaxHeight,
                Math.Max(72, flexibleHeight * .45));
            _ballotScroll.MaxHeight = Math.Min(CompactBallotMaxHeight,
                Math.Max(72, flexibleHeight * .35));
        }
        else
        {
            // Reserve the fixed header/status/action bands before allowing a
            // scroll region to consume the measured desktop viewport.
            _scoreScroll.MaxHeight = Math.Min(WideScoreMaxHeight,
                Math.Max(180, height - 120));
            _ballotScroll.MaxHeight = Math.Min(WideBallotMaxHeight,
                Math.Max(72, height - 340));
        }
    }

    private static Control BuildMobileScoreHeader()
    {
        var header = new Border
        {
            Child = Text("SCOREBOARD", 10,
                FontWeight.SemiBold, GuiTheme.TextDimBrush),
            BorderBrush = GuiTheme.EdgeBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        if (header.Child is TextBlock text) text.TextWrapping = TextWrapping.Wrap;
        return header;
    }

    private static Control BuildMobileScoreRow(PostMatchScoreRow row)
    {
        string name = $"{row.Rank}. {row.Name}";
        if (row.IsLocal) name += "  ·  YOU";
        if (row.IsBot) name += "  ·  BOT";
        string hunter = row.Hunter?.ToString() ?? "Hunter unavailable";
        if (row.TeamIndex >= 0) hunter += $"  ·  Team {row.TeamIndex + 1}";

        var details = new StackPanel { Spacing = 3, HorizontalAlignment = HorizontalAlignment.Stretch };
        details.Children.Add(WrappedText(name, 13, FontWeight.SemiBold,
            row.IsLocal ? GuiTheme.AccentBrush : GuiTheme.TextBrush));
        details.Children.Add(WrappedText(hunter, 10, FontWeight.Normal, GuiTheme.TextDimBrush));
        details.Children.Add(WrappedText($"Score {row.Score.ToString(CultureInfo.InvariantCulture)}  ·  "
            + (row.HasDetailedStats ? $"K / D / A {row.Kills} / {row.Deaths} / {row.Assists}" : $"K / D {row.Kills} / {row.Deaths}"),
            11, FontWeight.SemiBold, row.IsLocal ? GuiTheme.AccentBrush : GuiTheme.TextBrush));
        if (row.HasDetailedStats)
            details.Children.Add(WrappedText($"Damage {row.Damage.ToString(CultureInfo.InvariantCulture)}  ·  "
                + $"HS {row.HeadshotKills}  ·  Longest streak {row.LongestKillStreak}",
                10, FontWeight.Normal, GuiTheme.TextDimBrush));
        details.Children.Add(WrappedText(row.ObjectiveSummary, 10, FontWeight.Normal,
            GuiTheme.TextDimBrush));

        return new Border
        {
            Child = details,
            Background = row.IsLocal ? GuiTheme.PanelLightBrush : GuiTheme.PanelBrush,
            BorderBrush = row.IsLocal ? GuiTheme.AccentBrush : GuiTheme.EdgeBrush,
            BorderThickness = new Thickness(row.IsLocal ? 2 : 1),
            Padding = new Thickness(8, 7),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }

    private static Control BuildScoreHeader()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), ColumnSpacing = 8 };
        grid.Children.Add(Text("PLAYER", 10, FontWeight.SemiBold, GuiTheme.TextDimBrush));
        AddGridText(grid, "SCORE", 1, 10, FontWeight.SemiBold, GuiTheme.TextDimBrush);
        AddGridText(grid, "K / D / A", 2, 10, FontWeight.SemiBold, GuiTheme.TextDimBrush);
        AddGridText(grid, "DAMAGE", 3, 10, FontWeight.SemiBold, GuiTheme.TextDimBrush);
        return new Border
        {
            Child = grid,
            BorderBrush = GuiTheme.EdgeBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 4)
        };
    }

    private static Control BuildScoreRow(PostMatchScoreRow row)
    {
        var details = new StackPanel { Spacing = 3 };
        string name = $"{row.Rank}. {row.Name}";
        if (row.IsLocal) name += "  ·  YOU";
        if (row.IsBot) name += "  ·  BOT";
        details.Children.Add(Text(name, 13, FontWeight.SemiBold,
            row.IsLocal ? GuiTheme.AccentBrush : GuiTheme.TextBrush));
        string hunter = row.Hunter?.ToString() ?? "Hunter unavailable";
        if (row.TeamIndex >= 0) hunter += $"  ·  Team {row.TeamIndex + 1}";
        details.Children.Add(Text(hunter, 10, FontWeight.Normal, GuiTheme.TextDimBrush));
        if (row.HasDetailedStats)
            details.Children.Add(Text($"HS {row.HeadshotKills}  ·  Longest streak {row.LongestKillStreak}",
                10, FontWeight.Normal, GuiTheme.TextDimBrush));
        details.Children.Add(Text(row.ObjectiveSummary, 10, FontWeight.Normal, GuiTheme.TextDimBrush));

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), ColumnSpacing = 8 };
        grid.Children.Add(details);
        AddGridText(grid, row.Score.ToString(CultureInfo.InvariantCulture), 1, 13,
            FontWeight.SemiBold, row.IsLocal ? GuiTheme.AccentBrush : GuiTheme.TextBrush);
        AddGridText(grid, row.HasDetailedStats ? $"{row.Kills} / {row.Deaths} / {row.Assists}" : $"{row.Kills} / {row.Deaths} / —", 2, 11,
            FontWeight.Normal, GuiTheme.TextBrush);
        AddGridText(grid, row.HasDetailedStats ? row.Damage.ToString(CultureInfo.InvariantCulture) : "—", 3, 11,
            FontWeight.Normal, GuiTheme.TextBrush);
        return new Border
        {
            Child = grid,
            Background = row.IsLocal ? GuiTheme.PanelLightBrush : GuiTheme.PanelBrush,
            BorderBrush = row.IsLocal ? GuiTheme.AccentBrush : GuiTheme.EdgeBrush,
            BorderThickness = new Thickness(row.IsLocal ? 2 : 1),
            Padding = new Thickness(8, 7)
        };
    }

    private void RebuildBallot()
    {
        _renderGeneration++;
        _ballot.Children.Clear();
        _cards.Clear();
        foreach (Bitmap bitmap in _bitmaps) bitmap.Dispose();
        _bitmaps.Clear();

        if (_displayOptions.IsDefaultOrEmpty)
        {
            _ballot.Children.Add(EmptyPanel(_ballotModel.State == PostMatchBallotState.Loading
                ? "Loading vote options…" : "No vote options are available."));
            return;
        }

        for (int i = 0; i < _displayOptions.Length; i++)
        {
            PostMatchBallotOption option = _displayOptions[i];
            var card = BuildBallotCard(option, i);
            _cards.Add(card);
            _ballot.Children.Add(card);
            if (!string.IsNullOrWhiteSpace(option.MapKey)
                && option.Choice != LobbyVoteChoice.ReturnToLobby)
            {
                // Keep preview work bounded if Node rotates ballots faster
                // than local image decoding can finish.
                if (_previewLoads.Count < 16)
                {
                    Task load = LoadPreviewAsync(card, option.MapKey, _renderGeneration);
                    _previewLoads.Add(load);
                }
            }
        }
        while (_previewLoads.Count > 16)
        {
            int completed = _previewLoads.FindIndex(task => task.IsCompleted);
            if (completed < 0) break;
            _previewLoads.RemoveAt(completed);
        }
    }

    private AvaloniaButton BuildBallotCard(PostMatchBallotOption option, int index)
    {
        var text = new StackPanel
        {
            Spacing = 5,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        text.Children.Add(new TextBlock
        {
            Text = CardHeadingForDisplay(option, index),
            FontFamily = GuiTheme.Display,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = GuiTheme.TextBrush,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Stretch
        });
        text.Children.Add(new TextBlock
        {
            Text = option.Description,
            FontFamily = GuiTheme.Display,
            FontSize = 11,
            FontWeight = FontWeight.Normal,
            Foreground = GuiTheme.TextDimBrush,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Stretch
        });
        var preview = new ContentControl
        {
            Name = $"ResultsPreview{index + 1}",
            Height = 66,
            Background = GuiTheme.InkBrush,
            BorderBrush = GuiTheme.EdgeBrush,
            BorderThickness = new Thickness(1),
            Content = Text("Preview unavailable", 10, FontWeight.Normal, GuiTheme.TextDimBrush),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        text.Children.Add(preview);
        var card = new AvaloniaButton
        {
            Name = $"ResultsOption{index + 1}",
            Content = text,
            MinWidth = 48,
            MinHeight = 126,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Padding = new Thickness(10),
            Background = GuiTheme.PanelBrush,
            Foreground = GuiTheme.TextBrush,
            BorderThickness = new Thickness(1),
            BorderBrush = GuiTheme.EdgeBrush,
            Focusable = true
        };
        card.PointerEntered += (_, _) =>
        {
            if (_disposed || _continuationLoading) return;
            Selection.Select(index);
            RefreshCards();
        };
        card.Click += (_, _) =>
        {
            if (_disposed || _continuationLoading) return;
            Selection.Select(index);
            Choose();
        };
        return card;
    }

    /// <summary>
    /// Switches this existing view to the opaque continuation stage without
    /// rebuilding its authoritative result or ballot projections.
    /// </summary>
    internal bool EnterContinuationLoading(MatchTransitionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (_disposed || _continuationLoading) return false;

        _continuationLoading = true;
        _renderGeneration++;
        _lifetime.Cancel();
        _leaveConfirmationPending = false;
        _ballot.Children.Clear();
        _cards.Clear();
        foreach (Bitmap bitmap in _bitmaps) bitmap.Dispose();
        _bitmaps.Clear();
        _previewLoads.Clear();

        Background = GuiTheme.InkBrush;
        Opacity = 1;
        _continuationView = new MatchTransitionView(state);
        Content = _continuationView;
        return true;
    }

    internal void UpdateContinuationLoading(MatchTransitionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (_disposed || !_continuationLoading) return;
        _continuationView?.Update(state);
    }

    public void Update(NodeRoundSnapshot? round, string? message = null,
        LobbySnapshot? lobby = null, Guid? localSessionId = null)
    {
        if (_disposed || _continuationLoading) return;
        _round = round;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Selection.Update(round, now);
        long countdown = round?.VoteDeadline is { } deadline
            ? (long)Math.Ceiling((deadline - now).TotalSeconds) : long.MinValue;
        bool projectionChanged = !_projectionInitialized
            || !ReferenceEquals(round, _projectedRound) || countdown != _projectionCountdown;
        if (projectionChanged)
        {
            _projectionInitialized = true;
            _projectedRound = round;
            _projectionCountdown = countdown;
            _ballotModel = PostMatchBallotModel.From(round, now);
        }
        _status.Text = message ?? _ballotModel.StatusText;
        _status.Foreground = _ballotModel.State switch
        {
            PostMatchBallotState.Resolved => GuiTheme.GoodBrush,
            PostMatchBallotState.Ended or PostMatchBallotState.Paused => GuiTheme.WarmBrush,
            PostMatchBallotState.Closed => GuiTheme.TextDimBrush,
            _ => GuiTheme.TextBrush
        };
        _leading.Text = _ballotModel.LeadingText;
        if (projectionChanged)
        {
            _displayOptions = _ballotModel.Options;
            string signature = string.Join("|", _displayOptions.Select(option =>
                $"{option.Id}:{option.Choice}:{option.MapKey}:{option.Mode}:{option.IsResolved}"));
            if (round == null || _renderedRevision != round.BallotRevision || signature != _optionSignature)
            {
                _renderedRevision = round?.BallotRevision;
                _optionSignature = signature;
                RebuildBallot();
            }
        }
        UpdateHunter(lobby ?? round?.Lobby, localSessionId, now);
        RefreshCards(projectionChanged);
    }

    public void MoveSelection(int delta) => Move(delta);
    public void SubmitSelection()
    {
        if (_disposed || _continuationLoading) return;
        if (_leaveConfirmationPending) ConfirmLeave();
        else Choose();
    }

    public bool LeaveConfirmationPending => _leaveConfirmationPending;

    internal void SetInputDevice(PrimeInputDevice device)
    {
        if (_disposed || _continuationLoading) return;
        ControllerFamily family = device == PrimeInputDevice.Gamepad
            ? GamepadInput.State.Family : ControllerFamily.Generic;
        if (_lastInputDevice == device
            && (device != PrimeInputDevice.Gamepad || _lastControllerFamily == family)) return;
        _lastInputDevice = device;
        _lastControllerFamily = family;
        UpdateInputHints();
    }

    public void RequestLeave()
    {
        if (_disposed || _continuationLoading) return;
        if (_leaveConfirmationPending)
        {
            ConfirmLeave();
            return;
        }
        _leaveConfirmationPending = true;
        UpdateLeaveConfirmation();
        _leaveButton.Focus();
    }

    public void ConfirmLeave()
    {
        if (!_leaveConfirmationPending || _disposed || _continuationLoading) return;
        _leaveConfirmationPending = false;
        UpdateLeaveConfirmation();
        LeaveRequested?.Invoke();
    }

    public void CancelLeaveConfirmation()
    {
        if (!_leaveConfirmationPending || _disposed || _continuationLoading) return;
        _leaveConfirmationPending = false;
        UpdateLeaveConfirmation();
        _leaveButton.Focus();
    }

    public void RejectPending()
    {
        if (_disposed || _continuationLoading) return;
        Selection.RejectPending();
        _pendingHunter = null;
        SyncHunterSelection();
        RefreshHunter();
        RefreshCards();
    }

    internal void CycleHunter(int delta)
    {
        if (_disposed || _continuationLoading || _leaveConfirmationPending
            || !_canSelectHunter
            || _pendingHunter.HasValue || _authoritativeHunter is not { } current)
            return;
        int count = (int)Hunter.Weavel + 1;
        int next = ((int)current + Math.Sign(delta) + count) % count;
        RequestHunter((Hunter)next);
    }

    public void Move(int delta)
    {
        if (_disposed || _continuationLoading || _leaveConfirmationPending) return;
        Selection.Move(delta);
        RefreshCards();
        if (Selection.SelectedIndex < _cards.Count)
        {
            _cards[Selection.SelectedIndex].BringIntoView();
            _cards[Selection.SelectedIndex].Focus(NavigationMethod.Directional);
        }
    }

    public void Choose()
    {
        if (_disposed || _continuationLoading) return;
        Selection.Update(_round, DateTimeOffset.UtcNow);
        if (Selection.Choose() is { } id) VoteRequested?.Invoke(id);
        RefreshCards();
    }

    private void RefreshCards(bool refreshHeadings = false)
    {
        if (_disposed || _continuationLoading) return;
        for (int i = 0; i < _cards.Count; i++)
        {
            AvaloniaButton card = _cards[i];
            PostMatchBallotOption option = _displayOptions[i];
            card.IsEnabled = !_leaveConfirmationPending && !_pendingHunter.HasValue
                && Selection.CanChoose && _ballotModel.CanVote;
            bool selected = i == Selection.SelectedIndex;
            card.BorderThickness = new Thickness(selected ? 2 : 1);
            card.BorderBrush = selected ? _selectedBallotBorderBrush
                : option.IsResolved ? GuiTheme.WarmBrush : GuiTheme.EdgeBrush;
            card.Background = selected ? _selectedBallotSurfaceBrush : GuiTheme.PanelBrush;
            if (refreshHeadings
                && card.Content is StackPanel panel && panel.Children[0] is TextBlock heading)
            {
                heading.Text = CardHeadingForDisplay(option, i);
            }
        }
    }

    private void UpdateHunter(LobbySnapshot? lobby, Guid? localSessionId,
        DateTimeOffset now)
    {
        LobbyMember? member = localSessionId is { } sessionId
            ? lobby?.Members.FirstOrDefault(candidate => candidate.SessionId == sessionId)
            : null;
        _authoritativeHunter = member?.Hunter;
        if (_pendingHunter == _authoritativeHunter) _pendingHunter = null;
        _canSelectHunter = lobby?.Phase == LobbyPhase.PostMatch
            && member is { Observer: false }
            && RoundAllowsHunterChange(_round, now);
        if (!_pendingHunter.HasValue) SyncHunterSelection();
        RefreshHunter();

        static bool RoundAllowsHunterChange(NodeRoundSnapshot? round,
            DateTimeOffset current)
            => round is { TournamentEnded: false, ResolvedOption: null }
                && round.VoteDeadline is { } deadline && current < deadline;
    }

    private void RequestHunterSelection()
    {
        if (_syncingHunter || _hunterSelector.SelectedItem is not Hunter hunter)
            return;
        RequestHunter(hunter);
    }

    private void RequestHunter(Hunter hunter)
    {
        if (_leaveConfirmationPending || !_canSelectHunter || _pendingHunter.HasValue
            || _authoritativeHunter is not { } current || hunter == current)
        {
            SyncHunterSelection();
            return;
        }
        _pendingHunter = hunter;
        SyncHunterSelection();
        RefreshHunter();
        RefreshCards();
        HunterRequested?.Invoke(hunter);
    }

    private void SyncHunterSelection()
    {
        Hunter? selected = _pendingHunter ?? _authoritativeHunter;
        _syncingHunter = true;
        _hunterSelector.SelectedItem = selected;
        _syncingHunter = false;
    }

    private void RefreshHunter()
    {
        _hunterSelector.IsEnabled = _canSelectHunter && !_pendingHunter.HasValue
            && !_leaveConfirmationPending;
        _hunterHelp.Text = _pendingHunter is { } pending
            ? $"Switching to {PrimeGameText.HunterLabel(pending)}…"
            : _authoritativeHunter is null
                ? "Hunter selection is unavailable."
                : _canSelectHunter
                    ? "Choose before the ballot resolves. Keyboard: Q / E."
                    : "Hunter selection is locked for this transition.";
    }

    private string CardHeadingForDisplay(PostMatchBallotOption option, int index)
    {
        string heading = CardHeading(option, index, _ballotModel.OwnVote,
            _round?.ResolvedOption?.Id ?? 0);
        return option.IsResolved ? $"{heading} · SELECTED" : heading;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_continuationLoading)
        {
            base.OnKeyDown(e);
            return;
        }
        SetInputDevice(PrimeInputDevice.KeyboardMouse);
        base.OnKeyDown(e);
        if (_leaveConfirmationPending)
        {
            if (e.Key == Key.Enter) ConfirmLeave();
            else if (e.Key == Key.Escape) CancelLeaveConfirmation();
            else return;
            e.Handled = true;
            return;
        }
        int digit = e.Key switch
        {
            Key.D1 or Key.NumPad1 => 0,
            Key.D2 or Key.NumPad2 => 1,
            Key.D3 or Key.NumPad3 => 2,
            Key.D4 or Key.NumPad4 => 3,
            Key.D5 or Key.NumPad5 => 4,
            Key.D6 or Key.NumPad6 => 5,
            Key.D7 or Key.NumPad7 => 6,
            Key.D8 or Key.NumPad8 => 7,
            _ => -1
        };
        if (e.Key == Key.Q) CycleHunter(-1);
        else if (e.Key == Key.E) CycleHunter(1);
        else if (e.Key is Key.Left or Key.Up or Key.A or Key.W) Move(-1);
        else if (e.Key is Key.Right or Key.Down or Key.D or Key.S) Move(1);
        else if (e.Key == Key.Enter) Choose();
        else if (e.Key == Key.Escape) RequestLeave();
        else if (digit >= 0 && digit < _cards.Count)
        {
            Selection.Select(digit);
            Choose();
        }
        else return;
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (_continuationLoading)
        {
            base.OnPointerPressed(e);
            return;
        }
        SetInputDevice(PrimeInputDevice.Touch);
        base.OnPointerPressed(e);
    }

    private void UpdateLeaveConfirmation()
    {
        if (_leaveConfirmationPending)
        {
            _leaveButton.Content = "Confirm Leave Lobby";
            _leaveButton.BorderBrush = GuiTheme.BadBrush;
            ToolTip.SetTip(_leaveButton,
                "Confirm leaving the lobby; Return to lobby remains a shared vote.");
            _cancelLeaveButton.IsVisible = true;
        }
        else
        {
            _leaveButton.Content = "Leave Lobby";
            _leaveButton.BorderBrush = GuiTheme.AccentBrush;
            ToolTip.SetTip(_leaveButton,
                "Leave the lobby directly after confirmation; Return to lobby is a shared vote.");
            _cancelLeaveButton.IsVisible = false;
        }
        UpdateInputHints();
        RefreshHunter();
        RefreshCards();
    }

    private void UpdateInputHints()
    {
        if (_lastInputDevice == PrimeInputDevice.Touch)
        {
            _hints.Text = "";
            _hints.IsVisible = false;
            return;
        }

        _hints.IsVisible = true;
        if (_leaveConfirmationPending)
        {
            _hints.Text = _lastInputDevice == PrimeInputDevice.Gamepad
                ? $"{PrimeControllerGlyphs.Prompt("Confirm", GamepadButtons.A,
                    GamepadInput.State.Family)}    {PrimeControllerGlyphs.Prompt(
                        "Cancel", GamepadButtons.B, GamepadInput.State.Family)}"
                : "Confirm leaving lobby: Enter Confirm    Escape Cancel";
            return;
        }

        _hints.Text = _lastInputDevice == PrimeInputDevice.Gamepad
            ? $"D-pad Up / Down: select    {PrimeControllerGlyphs.Prompt("Vote",
                GamepadButtons.A, GamepadInput.State.Family)}    {PrimeControllerGlyphs.Prompt(
                    "Leave", GamepadButtons.B, GamepadInput.State.Family)}    LB / RB: hunter"
            : "Arrows / WASD: select · Enter / 1–8: vote · Q / E: hunter · Escape: ask to leave lobby";
    }

    private async Task LoadPreviewAsync(AvaloniaButton card, string mapKey, int generation)
    {
        try
        {
            PrimePreviewImage? preview = await _mapPreviews.LoadAsync(mapKey, _lifetime.Token);
            if (preview == null || _disposed || _continuationLoading
                || _renderGeneration != generation
                || card.Content is not StackPanel panel
                || panel.Children.OfType<ContentControl>().FirstOrDefault() is not { } target)
                return;
            using var stream = new MemoryStream(preview.Data, writable: false);
            var bitmap = new Bitmap(stream);
            if (_disposed || _continuationLoading || _renderGeneration != generation)
            {
                bitmap.Dispose();
                return;
            }
            _bitmaps.Add(bitmap);
            target.Content = new Image
            {
                Source = bitmap,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { }
    }

    public static string Status(NodeRoundSnapshot? round, DateTimeOffset now)
        => PostMatchBallotModel.From(round, now).StatusText;

    public static string CardHeading(LobbyVoteEntry entry, int index, byte ownVote,
        LobbyVoteEntry? resolved)
        => CardHeading(new PostMatchBallotOption(entry.Id, entry.Choice, entry.MapKey,
            entry.Mode, Math.Max(0, entry.Votes), PostMatchBallotModel.Label(entry),
            PostMatchBallotModel.Description(entry), entry.Id == ownVote,
            false, resolved?.Id == entry.Id), index, ownVote, resolved?.Id ?? 0);

    public static string CardHeading(PostMatchBallotOption option, int index,
        byte ownVote, byte resolvedId)
    {
        string winner = option.IsResolved || resolvedId == option.Id
            ? option.Choice == LobbyVoteChoice.ReturnToLobby ? " · RETURNING TO LOBBY" : " · NEXT MATCH"
            : "";
        return $"{index + 1}. {option.Label} · {option.Votes} votes"
            + (option.Id == ownVote ? " · YOUR VOTE" : "") + winner;
    }

    private static AvaloniaButton BuildActionButton(string label, Action? action)
    {
        var button = new AvaloniaButton
        {
            Content = label,
            Background = GuiTheme.PanelLightBrush,
            Foreground = GuiTheme.TextBrush,
            BorderBrush = GuiTheme.AccentBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 7),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Focusable = true
        };
        if (action != null) button.Click += (_, _) => action();
        return button;
    }

    private static Border EmptyPanel(string message)
        => new()
        {
            Child = Text(message, 12, FontWeight.Normal, GuiTheme.TextDimBrush),
            Background = GuiTheme.PanelBrush,
            BorderBrush = GuiTheme.EdgeBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 6)
        };

    private IBrush ResolveThemeBrush(string resourceKey, IBrush fallback)
    {
        if (this.TryFindResource(resourceKey, out object? localValue)
            && localValue is IBrush localBrush)
            return localBrush;
        if (Application.Current?.TryGetResource(resourceKey, ThemeVariant.Default,
                out object? applicationValue) == true
            && applicationValue is IBrush applicationBrush)
            return applicationBrush;
        return fallback;
    }

    private static TextBlock Text(string text, double size, FontWeight weight, IBrush foreground)
        => new()
        {
            Text = text,
            FontFamily = GuiTheme.Display,
            FontSize = size,
            FontWeight = weight,
            Foreground = foreground
        };

    private static TextBlock WrappedText(string text, double size, FontWeight weight,
        IBrush foreground)
    {
        TextBlock value = Text(text, size, weight, foreground);
        value.TextWrapping = TextWrapping.Wrap;
        value.HorizontalAlignment = HorizontalAlignment.Stretch;
        return value;
    }

    private static void AddGridText(Grid grid, string text, int column, double size,
        FontWeight weight, IBrush foreground)
    {
        TextBlock value = Text(text, size, weight, foreground);
        value.HorizontalAlignment = HorizontalAlignment.Right;
        value.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(value, column);
        grid.Children.Add(value);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _continuationLoading = true;
        _continuationView?.Dispose();
        _continuationView = null;
        SizeChanged -= _sizeChangedHandler;
        _lifetime.Cancel();
        _lifetime.Dispose();
        _images.Dispose();
        foreach (Bitmap bitmap in _bitmaps) bitmap.Dispose();
        _bitmaps.Clear();
        _previewLoads.Clear();
    }
}
