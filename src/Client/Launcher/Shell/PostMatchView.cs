using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using FruityPrime.Server.Shared;
using MphRead.Hud;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>Frozen results and the Node-owned ballot. The host owns navigation and networking.</summary>
public sealed class PostMatchView : UserControl, IDisposable
{
    private readonly StackPanel _ballot = new() { Spacing = 12 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly List<Avalonia.Controls.Button> _cards = new();
    private readonly List<Bitmap> _bitmaps = new();
    private readonly PreviewImageService _images = new();
    private readonly CancellationTokenSource _lifetime = new();
    private NodeRoundSnapshot? _round;
    private ImmutableArray<LobbyVoteEntry> _displayOptions = [];
    private uint? _renderedRevision;
    private bool _disposed;
    private int _renderGeneration;
    private string _optionSignature = "";
    public PostMatchSelection Selection { get; } = new();
    public event Action<byte>? VoteRequested;
    public event Action? LeaveRequested;

    public PostMatchView(MatchResultsSnapshot? results)
    {
        Focusable = true;
        var zones = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Margin = new Thickness(16) };
        var scoreZone = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Margin = new Thickness(0, 0, 12, 0) };
        scoreZone.Children.Add(new TextBlock { Text = "MATCH RESULTS", FontSize = 26, FontWeight = FontWeight.Bold });
        var body = new StackPanel { Spacing = 12 };
        if (results != null)
        {
            body.Children.Add(new TextBlock { Text = $"{results.MapKey} · {results.Mode}" });
            var presentation = new PostMatchPresentation(results.Result);
            for (int page = 0; page < presentation.Pages.Length; page++)
            {
                body.Children.Add(new TextBlock { Text = presentation.Headers[page], FontWeight = FontWeight.Bold });
                foreach (string row in presentation.Pages[page])
                    body.Children.Add(new TextBlock { Text = row, TextWrapping = TextWrapping.Wrap });
            }
        }
        else body.Children.Add(new TextBlock { Text = "Authoritative match results unavailable." });
        var scores = new ScrollViewer { Name = "ResultsScoreScroll", Content = body,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(scores, 1); scoreZone.Children.Add(scores); zones.Children.Add(scoreZone);
        var voteZone = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto") };
        Grid.SetColumn(voteZone, 1); zones.Children.Add(voteZone);
        voteZone.Children.Add(new TextBlock { Text = "NEXT ROUND", FontSize = 24, FontWeight = FontWeight.Bold });
        Grid.SetRow(_status, 1); voteZone.Children.Add(_status);
        var options = new ScrollViewer { Name = "ResultsBallotScroll", Content = _ballot,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 8) };
        Grid.SetRow(options, 2); voteZone.Children.Add(options);
        var hints = new TextBlock { Text = "Arrows / WASD: select · Enter / 1–8: vote · Escape: leave", TextWrapping = TextWrapping.Wrap };
        Grid.SetRow(hints, 3); voteZone.Children.Add(hints);
        var leave = new Avalonia.Controls.Button { Content = "Leave Lobby", MinHeight = 48 };
        leave.Click += (_, _) => LeaveRequested?.Invoke();
        Grid.SetRow(leave, 4); voteZone.Children.Add(leave);
        Content = zones;
        Update(null);
    }

    public void Update(NodeRoundSnapshot? round, string? message = null)
    {
        if (_disposed) return;
        _round = round;
        Selection.Update(round, DateTimeOffset.UtcNow);
        _status.Text = message ?? Status(round, DateTimeOffset.UtcNow);
        _displayOptions = round == null ? [] : !round.Options.IsDefaultOrEmpty ? round.Options
            : round.ResolvedOption is { } resolved ? [resolved] : [];
        string signature = string.Join("|", System.Linq.Enumerable.Select(_displayOptions, entry => $"{entry.Id}:{entry.Choice}:{entry.MapKey}:{entry.Mode}"));
        if (round != null && (_renderedRevision != round.BallotRevision || signature != _optionSignature))
        {
            _renderedRevision = round.BallotRevision;
            _optionSignature = signature;
            _renderGeneration++;
            _ballot.Children.Clear(); _cards.Clear();
            foreach (Bitmap bitmap in _bitmaps) bitmap.Dispose();
            _bitmaps.Clear();
            foreach (LobbyVoteEntry entry in _displayOptions)
            {
                int index = _cards.Count;
                var text = new StackPanel { Spacing = 6 };
                text.Children.Add(new TextBlock { Text = Label(entry), FontWeight = FontWeight.Bold });
                text.Children.Add(new TextBlock { Text = entry.Choice == LobbyVoteChoice.ReturnToLobby ? "Return to the lobby together" : $"{entry.MapKey} · {entry.Mode}", TextWrapping = TextWrapping.Wrap });
                var preview = new ContentControl { Content = new TextBlock { Text = "Preview unavailable" }, Height = 100 };
                text.Children.Add(preview);
                var card = new Avalonia.Controls.Button { Content = text, MinHeight = 150, HorizontalAlignment = HorizontalAlignment.Stretch };
                card.PointerEntered += (_, _) => { Selection.Select(index); RefreshCards(); };
                card.Click += (_, _) => { Selection.Select(index); Choose(); };
                _cards.Add(card); _ballot.Children.Add(card);
                if (entry.Choice != LobbyVoteChoice.ReturnToLobby && !string.IsNullOrEmpty(entry.MapKey))
                    _ = LoadPreviewAsync(preview, entry.MapKey, _renderGeneration);
            }
        }
        RefreshCards();
    }
    public void MoveSelection(int delta) => Move(delta);
    public void SubmitSelection() => Choose();
    public void RequestLeave() => LeaveRequested?.Invoke();
    public void RejectPending() { Selection.RejectPending(); RefreshCards(); }
    public void Move(int delta)
    {
        Selection.Move(delta); RefreshCards();
        if (Selection.SelectedIndex < _cards.Count) _cards[Selection.SelectedIndex].BringIntoView();
    }
    public void Choose()
    {
        Selection.Update(_round, DateTimeOffset.UtcNow);
        if (Selection.Choose() is { } id) VoteRequested?.Invoke(id);
        RefreshCards();
    }
    private void RefreshCards()
    {
        for (int i = 0; i < _cards.Count; i++)
        {
            Avalonia.Controls.Button card = _cards[i];
            card.IsEnabled = !Selection.Locked;
            card.BorderThickness = new Thickness(i == Selection.SelectedIndex ? 3 : 1);
            card.BorderBrush = i == Selection.SelectedIndex ? Brushes.DeepSkyBlue : Brushes.Gray;
            if (_round != null && i < _displayOptions.Length && card.Content is StackPanel panel && panel.Children[0] is TextBlock heading)
            {
                LobbyVoteEntry entry = _displayOptions[i];
                heading.Text = CardHeading(entry, i, Selection.OwnVote, _round.ResolvedOption);
            }
        }
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Left or Key.Up or Key.A or Key.W) Move(-1);
        else if (e.Key is Key.Right or Key.Down or Key.D or Key.S) Move(1);
        else if (e.Key == Key.Enter) Choose();
        else if (e.Key == Key.Escape) LeaveRequested?.Invoke();
        else if (e.Key >= Key.D1 && e.Key <= Key.D8 && (int)e.Key - (int)Key.D1 < _cards.Count)
        { Selection.Select((int)e.Key - (int)Key.D1); Choose(); }
        else return;
        e.Handled = true;
    }
    private async Task LoadPreviewAsync(ContentControl target, string mapKey, int generation)
    {
        try
        {
            PrimePreviewImage? preview = await new MapPreviewService(_images).LoadAsync(mapKey, _lifetime.Token);
            if (preview == null || _disposed || _renderGeneration != generation) return;
            using var stream = new MemoryStream(preview.Data, writable: false);
            var bitmap = new Bitmap(stream);
            _bitmaps.Add(bitmap);
            target.Content = new Image { Source = bitmap, Stretch = Stretch.Uniform };
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    public static string Status(NodeRoundSnapshot? round, DateTimeOffset now)
    {
        if (round == null) return "Waiting for the lobby…";
        if (round.TournamentEnded) return "Tournament ended.";
        if (round.Paused) return "Tournament paused.";
        if (round.ResolvedOption is { } resolved)
            return resolved.Choice == LobbyVoteChoice.ReturnToLobby ? "Returning to lobby…"
                : $"Selected: {Label(resolved)} · Preparing next round…";
        string countdown = round.VoteDeadline is { } deadline
            ? $"Voting closes in {Math.Max(0, (int)Math.Ceiling((deadline - now).TotalSeconds))}s"
            : "Waiting for the next round…";
        return round.OwnVote != 0 ? $"Your vote is locked. {countdown}" : countdown;
    }
    public static string CardHeading(LobbyVoteEntry entry, int index, byte ownVote, LobbyVoteEntry? resolved)
    {
        string winner = resolved?.Id == entry.Id
            ? entry.Choice == LobbyVoteChoice.ReturnToLobby ? " · RETURNING TO LOBBY" : " · NEXT MATCH"
            : "";
        return $"{index + 1}. {Label(entry)} · {entry.Votes} votes{(entry.Id == ownVote ? " · YOUR VOTE" : "")}{winner}";
    }

    private static string Label(LobbyVoteEntry entry) => entry.Choice switch
    {
        LobbyVoteChoice.Rematch => "Rematch", LobbyVoteChoice.NextMap => "Next map",
        LobbyVoteChoice.ReturnToLobby => "Return to lobby", _ => entry.MapKey
    };
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _lifetime.Cancel(); _lifetime.Dispose(); _images.Dispose();
        foreach (Bitmap bitmap in _bitmaps) bitmap.Dispose();
        _bitmaps.Clear();
    }
}
