#if MPHREAD_AVALONIA
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher.Gui
{
    /// <summary>
    /// Tactical arena selection over the engine scoreboard. Network matches
    /// retain the existing ballot; local bot matches keep their configuration
    /// and repeat the current arena unless the player selects another one.
    /// </summary>
    internal sealed class EndPanelView : UserControl
    {
        private readonly bool _hasBallot;
        private readonly DeckGrid _ballot = new() { FixedColumns = 2, Ratio = 16 / 9.0 };
        private readonly ScrollViewer _ballotScroll;
        private readonly Note _count = new("");
        private readonly TextBlock _next = PrimeChrome.Text("CURRENT ARENA // REMATCH", PrimeTypography.DataSmall,
            PrimeTheme.HighlightBrush, data: true);
        private readonly TextBlock _phase = PrimeChrome.Eyebrow(
            "MATCH COMPLETE // NEXT DEPLOYMENT");
        private readonly TextBlock _panelTitle = PrimeChrome.Title("NEXT DEPLOYMENT");
        private readonly StackPanel _lobbyPane = new() { Spacing = 10 };
        private readonly TextBox _search = new() { PlaceholderText = "Search arenas" };

        /// <summary>What the ballot face says before the server has sent one.</summary>
        private readonly Note _empty = new("The rotation decides where next.")
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = false
        };

        /// <summary>What the ballot was built from, so it is only rebuilt when it moves.</summary>
        private string _order = "";

        public EndPanelView()
        {
            Background = Brushes.Transparent;
            Focusable = true;
            IsHitTestVisible = true;

            // Persistent online sessions return to their lobby after the report,
            // so the lobby is where the next map is chosen. Continuous/offline
            // flows keep the existing results ballot.
            _hasBallot = !NetSession.PersistentLobby;
            _ballotScroll = new ScrollViewer
            {
                Content = _ballot,
                ClipToBounds = true,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            _lobbyPane.Children.Add(new PrimeBadge("PERSISTENT LOBBY"));
            _lobbyPane.Children.Add(PrimeChrome.Title("RETURNING TO LOBBY"));
            _lobbyPane.Children.Add(PrimeChrome.Text(
                "The scoreboard remains authoritative while the session moves every connected player back to the lobby.",
                PrimeTypography.BodySmall, PrimeTheme.TextSecondaryBrush));
            _lobbyPane.Children.Add(PrimeChrome.Eyebrow(
                "SESSION REMAINS CONNECTED", PrimeTheme.GreenBrush));
            _lobbyPane.IsVisible = false;

            var body = new Panel();
            body.Children.Add(_ballotScroll);
            body.Children.Add(_empty);
            body.Children.Add(_lobbyPane);

            var start = new PrimeButton("START NEXT MATCH", () => OfflineRematch.Continue(), primary: true)
            { IsVisible = !NetSession.Active };
            start.SetValue(ControllerNav.NavIdProperty, "results.next");
            var foot = PrimeChrome.Stack(_next, _count, start,
                PrimeChrome.Text("ESC / PAUSE  //  LEAVE MATCH", PrimeTypography.DataSmall, PrimeTheme.TextSecondaryBrush, data: true));
            _search.TextChanged += (_, _) => FilterMaps();
            _search.IsVisible = _hasBallot;
            var heading = PrimeChrome.Stack(
                new PrimeBadge("POST-MATCH REPORT"),
                _phase,
                _panelTitle,
                _search);

            var stack = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*,Auto"),
                RowSpacing = 8
            };
            stack.Children.Add(heading);
            Grid.SetRow(body, 1);
            stack.Children.Add(body);
            Grid.SetRow(foot, 2);
            stack.Children.Add(foot);

            var root = new Panel();
            var host = new Border
            {
                Child = new PrimePanel(stack),
                // Preserve the original scoreboard's deaths-column clearance.
                Width = Deck.Phone ? 285 : 340,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = _hasBallot
                    ? VerticalAlignment.Stretch
                    : VerticalAlignment.Center,
                Margin = new Thickness(0, 14, 14, 14)
            };
            root.Children.Add(host);
            GuiTheme.PixelPerfect(root);
            Content = root;
            ShowFace();
            Refresh();
        }

        private void FilterMaps()
        {
            string query = _search.Text?.Trim() ?? "";
            foreach (Control child in _ballot.Children)
                if (child is DeckTile tile)
                    tile.IsVisible = tile.RoomKey.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || tile.Blurb.Contains(query, StringComparison.OrdinalIgnoreCase);
        }

        private void ShowFace()
        {
            bool ballot = _hasBallot;
            bool returning = NetSession.PersistentLobby;
            _ballotScroll.IsVisible = ballot;
            _lobbyPane.IsVisible = returning;
            _search.IsVisible = ballot;
            _phase.Text = returning ? "MATCH COMPLETE // SESSION CONTINUES" : "MATCH COMPLETE // NEXT DEPLOYMENT";
            _panelTitle.Text = returning ? "RETURNING TO LOBBY" : "NEXT DEPLOYMENT";
            _empty.Text = ballot ? "The rotation decides where next." : "Hunter changes are available from the lobby.";
            _empty.IsVisible = !returning && (!ballot || MapPick.Order.Count == 0);
        }

        /// <summary>
        /// Read the match's own state back into the panel, once a frame.
        ///
        /// The ballot remains owned by MapPick/the server. This reads its current
        /// presentation state without changing match or respawn choices.
        /// </summary>
        public void Refresh()
        {
            // A copy, because the list is the network thread's: it is rebuilt
            // whenever a vote arrives, and enumerating it from here while that
            // happens took the process down with "collection was modified".
            string[] order = _hasBallot
                ? System.Linq.Enumerable.ToArray(MapPick.Order)
                : Array.Empty<string>();
            string key = String.Join('|', order);
            if (key != _order)
            {
                _order = key;
                _ballot.Children.Clear();
                foreach (string room in order)
                {
                    bool returnToLobby = MapPick.IsReturnToLobby(room);
                    var tile = new DeckTile(room, returnToLobby ? "LOBBY" : "")
                    {
                        Blurb = returnToLobby ? MapPick.NameOf(room) : LauncherRoomDisplayName.Get(room),
                        Tactical = true,
                        // No slab: the whole card is the button, and on a
                        // ballot of 27 it was a third of every one of them.
                        Verb = "",
                        ChosenVerb = "",
                        Ratio = 16 / 9.0
                    };
                    tile.Click += (_, _) =>
                    {
                        MapPick.Choose(MapPick.IndexOf(tile.RoomKey));
                        Refresh();
                    };
                    _ballot.Children.Add(tile);
                }
                FilterMaps();
            }
            bool showingBallot = _hasBallot;
            _empty.IsVisible = !showingBallot || order.Length == 0;
            int best = 0;
            foreach (string room in order)
            {
                best = Math.Max(best, MapPick.VotesFor(room));
            }
            foreach (Control child in _ballot.Children)
            {
                if (child is not DeckTile tile)
                {
                    continue;
                }
                int votes = MapPick.VotesFor(tile.RoomKey);
                int tally = NetSession.Active ? votes : -1;
                bool leader = best > 0 && votes == best;
                // Only when one of them moved. This runs ten times a second
                // off TickEndPanel, and an invalidation here re-rasterises
                // the whole window and re-uploads it: unconditionally, that
                // was half the frame rate for as long as the panel was up.
                if (tile.Tally != tally || tile.Leader != leader)
                {
                    tile.Tally = tally;
                    tile.Leader = leader;
                    tile.InvalidateVisual();
                }
                tile.Chosen = tile.RoomKey == MapPick.Picked;
            }

            string next = MapPick.Picked;
            if (NetSession.PersistentLobby)
            {
                _next.Text = "NEXT // LOBBY";
                _count.Text = GameState.MatchState == MatchState.Ending
                    ? $"RETURNING IN {Math.Max(0, Math.Ceiling(GameState.MatchTime)):0} SEC"
                    : "RESULTS COMPLETE // LOBBY RESUMING";
            }
            else
            {
                _next.Text = next.Length > 0
                    ? "SELECTED // " + DisplayRoom(next).ToUpperInvariant()
                    : NetSession.Active
                        ? "NEXT // " + Mods.EndScreen.NextRoomName.ToUpperInvariant()
                        : "CURRENT ARENA // REMATCH";
                _count.Text = GameState.MatchState == MatchState.Ending
                    ? $"DEPLOYING IN {Math.Max(0, Math.Ceiling(GameState.MatchTime)):0} SEC"
                    : "RESULTS // CHOOSE YOUR NEXT ARENA";
            }
        }

        private static string DisplayRoom(string room) => MapPick.IsReturnToLobby(room)
            ? MapPick.NameOf(room)
            : LauncherRoomDisplayName.Get(room);
    }
}
#endif
