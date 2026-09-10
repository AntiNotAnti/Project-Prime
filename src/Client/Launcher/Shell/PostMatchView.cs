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
using FruityPrime.Server.Shared;
using AvaloniaButton = Avalonia.Controls.Button;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>
/// Native results view shown over the persistent SDL host. The result and
/// ballot projections are immutable; the host remains responsible for Node
/// commands and match continuation.
/// </summary>
public sealed class PostMatchView : UserControl, IDisposable
{
    private readonly StackPanel _scoreboard = new() { Spacing = 6 };
    private readonly StackPanel _ballot = new() { Spacing = 8 };
    private readonly ScrollViewer _scoreScroll;
    private readonly ScrollViewer _ballotScroll;
    private readonly TextBlock _status = new();
    private readonly TextBlock _leading = new();
    private readonly TextBlock _hints;
    private readonly AvaloniaButton _leaveButton;
    private readonly List<AvaloniaButton> _cards = new();
    private readonly List<Bitmap> _bitmaps = new();
    private readonly List<Task> _previewLoads = new();
    private readonly PreviewImageService _images = new();
    private readonly MapPreviewService _mapPreviews;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly PostMatchResultsModel _results;
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

    public PostMatchSelection Selection { get; } = new();
    public PostMatchResultsModel Results => _results;
    public PostMatchBallotModel Ballot => _ballotModel;
    public event Action<byte>? VoteRequested;
    public event Action? LeaveRequested;

    public PostMatchView(MatchResultsSnapshot? results, int localSlot = -1)
    {
        _results = PostMatchResultsBuilder.Build(results, localSlot);
        _mapPreviews = new MapPreviewService(_images);

        Focusable = true;
        Background = GuiTheme.InkBrush;
        FontFamily = GuiTheme.Display;

        var zones = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("1.15*,0.85*"),
            ColumnSpacing = 16,
            Margin = new Thickness(20)
        };

        var scoreZone = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Margin = new Thickness(0, 0, 8, 0),
            ClipToBounds = true
        };
        scoreZone.Children.Add(BuildResultsHeader());
        _scoreScroll = new ScrollViewer
        {
            Name = "ResultsScoreScroll",
            Content = _scoreboard,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = 440,
            Padding = new Thickness(0, 10, 6, 0)
        };
        Grid.SetRow(_scoreScroll, 1);
        scoreZone.Children.Add(_scoreScroll);
        zones.Children.Add(scoreZone);

        var voteZone = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto"),
            ClipToBounds = true
        };
        Grid.SetColumn(voteZone, 1);
        zones.Children.Add(voteZone);

        var voteHeader = new StackPanel { Spacing = 3 };
        voteHeader.Children.Add(Text("NEXT ROUND", 20, FontWeight.SemiBold, GuiTheme.AccentBrush));
        voteHeader.Children.Add(Text("Choose what happens after the results", 12, FontWeight.Normal, GuiTheme.TextDimBrush));
        voteZone.Children.Add(voteHeader);

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
        Grid.SetRow(status, 1);
        voteZone.Children.Add(status);

        _ballotScroll = new ScrollViewer
        {
            Name = "ResultsBallotScroll",
            Content = _ballot,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = 440,
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(0, 0, 6, 0)
        };
        Grid.SetRow(_ballotScroll, 2);
        voteZone.Children.Add(_ballotScroll);

        _hints = Text("Arrows / WASD: select · Enter / 1–8: vote · Escape: ask to leave lobby",
            10, FontWeight.Normal, GuiTheme.TextDimBrush);
        _hints.TextWrapping = TextWrapping.Wrap;
        _hints.Margin = new Thickness(0, 8, 0, 8);
        Grid.SetRow(_hints, 3);
        voteZone.Children.Add(_hints);

        _leaveButton = BuildActionButton("Leave Lobby", RequestLeave);
        _leaveButton.Name = "ResultsLeaveLobby";
        _leaveButton.MinHeight = 48;
        Grid.SetRow(_leaveButton, 4);
        voteZone.Children.Add(_leaveButton);
        UpdateLeaveConfirmation();

        Content = zones;
        BuildScoreboard();
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
            header.Children.Add(Text("Authoritative match results are unavailable.", 12,
                FontWeight.Normal, GuiTheme.TextDimBrush));
        }
        return header;
    }

    private void BuildScoreboard()
    {
        _scoreboard.Children.Clear();
        if (!_results.HasAuthoritativeResult)
        {
            _scoreboard.Children.Add(EmptyPanel("No authoritative result was received. Scores and outcome are not available."));
            return;
        }
        if (!_results.HasScoreboard)
        {
            _scoreboard.Children.Add(EmptyPanel("No authoritative player rows were included in this result."));
            return;
        }

        _scoreboard.Children.Add(BuildScoreHeader());
        foreach (PostMatchScoreRow row in _results.Scoreboard)
            _scoreboard.Children.Add(BuildScoreRow(row));
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
        string hunter = row.Hunter.ToString();
        if (row.TeamIndex >= 0) hunter += $"  ·  Team {row.TeamIndex + 1}";
        details.Children.Add(Text(hunter, 10, FontWeight.Normal, GuiTheme.TextDimBrush));
        details.Children.Add(Text($"HS {row.HeadshotKills}  ·  Longest streak {row.LongestKillStreak}",
            10, FontWeight.Normal, GuiTheme.TextDimBrush));
        details.Children.Add(Text(row.ObjectiveSummary, 10, FontWeight.Normal, GuiTheme.TextDimBrush));

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), ColumnSpacing = 8 };
        grid.Children.Add(details);
        AddGridText(grid, row.Score.ToString(CultureInfo.InvariantCulture), 1, 13,
            FontWeight.SemiBold, row.IsLocal ? GuiTheme.AccentBrush : GuiTheme.TextBrush);
        AddGridText(grid, $"{row.Kills} / {row.Deaths} / {row.Assists}", 2, 11,
            FontWeight.Normal, GuiTheme.TextBrush);
        AddGridText(grid, row.Damage.ToString(CultureInfo.InvariantCulture), 3, 11,
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
        var text = new StackPanel { Spacing = 5 };
        text.Children.Add(new TextBlock
        {
            Text = CardHeading(option, index, _ballotModel.OwnVote,
                _round?.ResolvedOption?.Id ?? 0),
            FontFamily = GuiTheme.Display,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = GuiTheme.TextBrush,
            TextWrapping = TextWrapping.Wrap
        });
        text.Children.Add(Text(option.Description, 11, FontWeight.Normal, GuiTheme.TextDimBrush));
        var preview = new ContentControl
        {
            Name = $"ResultsPreview{index + 1}",
            Height = 66,
            Background = GuiTheme.InkBrush,
            BorderBrush = GuiTheme.EdgeBrush,
            BorderThickness = new Thickness(1),
            Content = Text("Preview unavailable", 10, FontWeight.Normal, GuiTheme.TextDimBrush)
        };
        text.Children.Add(preview);
        var card = new AvaloniaButton
        {
            Name = $"ResultsOption{index + 1}",
            Content = text,
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
            Selection.Select(index);
            RefreshCards();
        };
        card.Click += (_, _) =>
        {
            Selection.Select(index);
            Choose();
        };
        return card;
    }

    public void Update(NodeRoundSnapshot? round, string? message = null)
    {
        if (_disposed) return;
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
        RefreshCards(projectionChanged);
    }

    public void MoveSelection(int delta) => Move(delta);
    public void SubmitSelection()
    {
        if (_leaveConfirmationPending) ConfirmLeave();
        else Choose();
    }

    public bool LeaveConfirmationPending => _leaveConfirmationPending;

    public void RequestLeave()
    {
        if (_disposed) return;
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
        if (!_leaveConfirmationPending || _disposed) return;
        _leaveConfirmationPending = false;
        UpdateLeaveConfirmation();
        LeaveRequested?.Invoke();
    }

    public void CancelLeaveConfirmation()
    {
        if (!_leaveConfirmationPending || _disposed) return;
        _leaveConfirmationPending = false;
        UpdateLeaveConfirmation();
        _leaveButton.Focus();
    }

    public void RejectPending()
    {
        Selection.RejectPending();
        RefreshCards();
    }

    public void Move(int delta)
    {
        if (_leaveConfirmationPending) return;
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
        Selection.Update(_round, DateTimeOffset.UtcNow);
        if (Selection.Choose() is { } id) VoteRequested?.Invoke(id);
        RefreshCards();
    }

    private void RefreshCards(bool refreshHeadings = false)
    {
        for (int i = 0; i < _cards.Count; i++)
        {
            AvaloniaButton card = _cards[i];
            PostMatchBallotOption option = _displayOptions[i];
            card.IsEnabled = Selection.CanChoose && _ballotModel.CanVote;
            bool selected = i == Selection.SelectedIndex;
            card.BorderThickness = new Thickness(selected ? 2 : 1);
            card.BorderBrush = selected ? GuiTheme.AccentBrush
                : option.IsResolved ? GuiTheme.WarmBrush : GuiTheme.EdgeBrush;
            card.Background = selected ? GuiTheme.PanelLightBrush : GuiTheme.PanelBrush;
            if (refreshHeadings && card.Content is StackPanel panel && panel.Children[0] is TextBlock heading)
            {
                heading.Text = CardHeading(option, i, _ballotModel.OwnVote,
                    _round?.ResolvedOption?.Id ?? 0);
            }
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
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
        if (e.Key is Key.Left or Key.Up or Key.A or Key.W) Move(-1);
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

    private void UpdateLeaveConfirmation()
    {
        if (_leaveConfirmationPending)
        {
            _leaveButton.Content = "Confirm Leave Lobby";
            _leaveButton.BorderBrush = GuiTheme.BadBrush;
            ToolTip.SetTip(_leaveButton,
                "Confirm leaving the lobby; Return to lobby remains a shared vote.");
            _hints.Text = "Confirm leaving lobby? Enter / A: confirm · Escape / B: stay in results";
        }
        else
        {
            _leaveButton.Content = "Leave Lobby";
            _leaveButton.BorderBrush = GuiTheme.AccentBrush;
            ToolTip.SetTip(_leaveButton,
                "Leave the lobby directly after confirmation; Return to lobby is a shared vote.");
            _hints.Text = "Arrows / WASD: select · Enter / 1–8: vote · Escape: ask to leave lobby";
        }
        RefreshCards();
    }

    private async Task LoadPreviewAsync(AvaloniaButton card, string mapKey, int generation)
    {
        try
        {
            PrimePreviewImage? preview = await _mapPreviews.LoadAsync(mapKey, _lifetime.Token);
            if (preview == null || _disposed || _renderGeneration != generation
                || card.Content is not StackPanel panel
                || panel.Children.OfType<ContentControl>().FirstOrDefault() is not { } target)
                return;
            using var stream = new MemoryStream(preview.Data, writable: false);
            var bitmap = new Bitmap(stream);
            if (_disposed || _renderGeneration != generation)
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

    private static TextBlock Text(string text, double size, FontWeight weight, IBrush foreground)
        => new()
        {
            Text = text,
            FontFamily = GuiTheme.Display,
            FontSize = size,
            FontWeight = weight,
            Foreground = foreground
        };

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
        _lifetime.Cancel();
        _lifetime.Dispose();
        _images.Dispose();
        foreach (Bitmap bitmap in _bitmaps) bitmap.Dispose();
        _bitmaps.Clear();
        _previewLoads.Clear();
    }
}
