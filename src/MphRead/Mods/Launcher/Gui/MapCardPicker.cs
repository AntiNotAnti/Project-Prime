#if MPHREAD_AVALONIA
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace MphRead.Mods.Launcher.Gui
{
    /// <summary>
    /// Builds the same map card used by the Offline screen and every popup
    /// that asks the player to choose one map.
    /// </summary>
    internal static class MapCardFactory
    {
        public static DeckTile Create(string room)
        {
            string code = room.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                is { Length: > 0 } parts ? parts[0] : room;
            (RoomMetadata? meta, _) = Metadata.GetRoomByName(room);
            return new DeckTile(room, code)
            {
                Blurb = meta?.InGameName ?? room, Tactical = true, Verb = "SELECT", ChosenVerb = "SELECTED"
            };
        }
    }

    /// <summary>
    /// One-map picker using the exact same DeckTile/DeckGrid presentation as
    /// Offline. The lobby uses this rather than a second list-style picker.
    /// </summary>
    internal sealed partial class MapCardPicker : UserControl
    {
        public event EventHandler<string>? Done;
        public event EventHandler? Cancelled;

        private readonly DeckGrid _grid = new() { FixedColumns = 2, Ratio = 16 / 9.0 };
        private readonly Image _preview = new() { Stretch = Stretch.UniformToFill, Height = 150 };
        private readonly TextBlock _name = PrimeChrome.Title("SELECT AN ARENA");
        private readonly TextBox _search = new() { PlaceholderText = "Search arena name or code" };
        private readonly Note _note = new("");
        private readonly PrimeButton _use;
        private string? _selected;
        private readonly Dictionary<string, string?> _incompatibilities = new();
        private string? Incompatibility(string room) => _incompatibilities.GetValueOrDefault(room);

        public MapCardPicker(IReadOnlyList<string> rooms, string? selected, Func<string, string?>? incompatibility = null,
            bool includeCommunity = false,
            Func<System.Threading.CancellationToken, System.Threading.Tasks.Task<MphRead.Mods.MapGen.CommunityMap[]>>? browseCommunity = null,
            Func<MphRead.Mods.MapGen.CommunityMap, System.Threading.CancellationToken, System.Threading.Tasks.Task<string>>? installCommunity = null)
        {
            _includeCommunity = includeCommunity;
            _incompatibility = incompatibility;
            _browseCommunity = browseCommunity ?? BrowseCommunityAsync;
            _installCommunity = installCommunity ?? InstallCommunityAsync;
            foreach (string room in rooms) _incompatibilities[room] = incompatibility?.Invoke(room);
            string[] compatibleRooms = rooms.Where(room => Incompatibility(room) == null).ToArray();
            Background = Brushes.Transparent;
            Focusable = true;
            _selected = selected != null && compatibleRooms.Contains(selected, StringComparer.OrdinalIgnoreCase) ? selected : null;

            var back = new PrimeButton("CANCEL", () => Cancelled?.Invoke(this, EventArgs.Empty));
            _use = new PrimeButton("USE MAP", () =>
            {
                _ = ConfirmSelectionAsync();
            }, primary: true);
            _use.SetValue(ControllerNav.NavIdProperty, "map-picker.use");
            _search.SetValue(ControllerNav.NavIdProperty, "map-picker.search");
            _search.TextChanged += (_, _) => FilterCards();

            var scroll = new ScrollViewer
            {
                Content = _grid,
                ClipToBounds = true,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
            };

            var gallery = new Grid { RowDefinitions = new("Auto,*"), RowSpacing = PrimeMetrics.PanelGap };
            var searchPanel = PrimeChrome.Stack(_search);
            if (_includeCommunity)
            {
                _communityStatus.Text = "Loading Community maps…";
                var refresh = new PrimeButton("REFRESH COMMUNITY", () => _ = LoadCommunityAsync());
                refresh.SetValue(ControllerNav.NavIdProperty, "map-picker.refresh-community");
                searchPanel.Children.Add(_communityStatus);
                searchPanel.Children.Add(refresh);
            }
            gallery.Children.Add(searchPanel); Grid.SetRow(scroll, 1); gallery.Children.Add(scroll);
            var inspector = new PrimePanel(PrimeChrome.Stack(new PrimeBadge("DEPLOYMENT PREVIEW"),
                _preview, _name, _note, PrimeChrome.Text("Select an arena, then confirm to update the match.",
                    PrimeTypography.BodySmall, PrimeTheme.TextSecondaryBrush)));
            var body = PrimeChrome.Columns("2*,*", gallery, inspector);
            var frame = new Grid { RowDefinitions = new("Auto,*,Auto"), RowSpacing = PrimeMetrics.PanelGap,
                Margin = new Thickness(PrimeMetrics.PanelPadding) };
            frame.Children.Add(PrimeChrome.Stack(new PrimeBadge("ARENA DIRECTORY"), PrimeChrome.Title("CHOOSE DEPLOYMENT ZONE")));
            Grid.SetRow(body, 1); frame.Children.Add(body);
            var actions = PrimeChrome.Columns("*,*", back, _use);
            Grid.SetRow(actions, 2); frame.Children.Add(actions);
            Content = frame;

            if (compatibleRooms.Length == 0)
            {
                _note.Text = rooms.Count == 0
                    ? "No multiplayer rooms were found. Set the game files up from Settings."
                    : "No maps support the selected game type and matchup.";
                _note.Foreground = GuiTheme.WarmBrush;
                _use.IsEnabled = false;
                return;
            }

            foreach (string room in compatibleRooms)
            {
                DeckTile tile = MapCardFactory.Create(room);
                tile.Chosen = String.Equals(room, _selected, StringComparison.OrdinalIgnoreCase);
                tile.Click += (_, _) => Select(tile);
                _grid.Children.Add(tile);
            }
            RefreshSelection();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (_includeCommunity && !_communityStarted) { _communityStarted = true; _ = LoadCommunityAsync(); }
            foreach (Control child in _grid.Children)
            {
                if (child is DeckTile tile && tile.Chosen)
                {
                    tile.Focus();
                    return;
                }
            }
            if (_grid.Children.Count > 0)
                _grid.Children[0].Focus();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Cancelled?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }

        private void Select(DeckTile selected)
        {
            if (_installing) return;
            _selectedCommunity = _communityCards.GetValueOrDefault(selected);
            _selected = selected.RoomKey;
            foreach (Control child in _grid.Children)
                if (child is DeckTile tile)
                    tile.Chosen = ReferenceEquals(tile, selected);
            RefreshSelection();
        }

        private void RefreshSelection()
        {
            string? reason = _selected == null ? null : Incompatibility(_selected);
            if (_selectedCommunity is { } community)
            {
                _use.IsEnabled = !_installing;
                _name.Text = (community.DisplayName ?? community.Name).ToUpperInvariant();
                _note.Text = $"Community · {community.Author ?? "Unknown author"} · v{community.Version ?? "1"}\n"
                    + "Use Map downloads and prepares this version before selecting it.";
                _note.Foreground = GuiTheme.TextDimBrush;
                _preview.Source = MapShot.For(community.Name);
                return;
            }
            _use.IsEnabled = !_installing && !String.IsNullOrWhiteSpace(_selected) && reason == null;
            _note.Text = String.IsNullOrWhiteSpace(_selected)
                ? "Choose a map."
                : $"Selected: {Metadata.GetRoomByName(_selected).Item1?.InGameName ?? _selected}";
            if (reason != null) _note.Text = reason;
            _note.Foreground = reason == null ? GuiTheme.TextDimBrush : GuiTheme.WarmBrush;
            _name.Text = String.IsNullOrWhiteSpace(_selected) ? "SELECT AN ARENA" : MapPick.NameOf(_selected).ToUpperInvariant();
            _preview.Source = MapShot.For(_selected);
        }
    }
}
#endif
