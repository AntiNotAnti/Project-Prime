using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using MphRead.Mods.Campaign;
using MphRead.Mods.Network;
using MphRead.Formats;
using MphRead.Text;

namespace MphRead.Mods.Launcher.Gui
{
    /// <summary>
    /// Single-screen campaign map and scanned logbook. Built exclusively
    /// from a detached CampaignJournal snapshot, never from a live Scene or
    /// from secret scan data that hasn't been discovered.
    /// </summary>
    internal sealed class CampaignJournalView : UserControl
    {
        public event EventHandler? Closed;

        public static bool Available(ScenePlayerRegistry? players)
        {
            if (players == null || players.PlayerCount == 0 || NetSession.Active
                || DemoPlayback.IsActive) return false;
            var scene = players.Main?.OwningScene;
            return scene != null && scene.GameState.SinglePlayer && !scene.Services.IsReplica;
        }

        /// <summary>
        /// Copy immutable values on the pause-menu UI thread; no renderer,
        /// loaded model, audio task or Scene survives in the resulting view.
        /// </summary>
        public static CampaignJournalView? OpenFor(ScenePlayerRegistry? players)
        {
            if (!Available(players)) return null;
            StorySave save = players!.Main.OwningScene.GameState.StorySave;
            System.Collections.Generic.IReadOnlyList<StringTableEntry>? entries = null;
            try { entries = Strings.ReadStringTable(StringTables.ScanLog); }
            catch (Exception ex)
            {
                // A user may not yet have extracted this localization table.
                // The discovered rooms and bit counts must still be viewable.
                Console.WriteLine("[campaign] scan archive text unavailable: " + ex.Message);
            }
            return new CampaignJournalView(CampaignJournal.Capture(save, entries));
        }

        private readonly CampaignJournal _journal;
        private readonly StackPanel _entries = new() { Spacing = 8 };
        private readonly PrimeButton _map, _scans;
        private readonly ChoiceRow _category;
        private readonly TextBlock _section = PrimeChrome.Eyebrow("DISCOVERED ROOMS");
        private bool _mapOpen = true;

        public CampaignJournalView(CampaignJournal journal)
        {
            _journal = journal;
            Focusable = true;
            _map = new PrimeButton("MISSION MAP", () => ShowMap());
            _scans = new PrimeButton("SCAN LOGBOOK", () => ShowScans());
            ControllerNav.Identify(_map, "campaign.journal.map");
            ControllerNav.Identify(_scans, "campaign.journal.scans");
            _category = new ChoiceRow("Records",
                new[] { "All", "Lore", "Bioforms", "Objects", "Equipment", "Other" });
            _category.Changed += (_, _) => { if (!_mapOpen) RefreshEntries(); };
            var close = new PrimeButton("RETURN TO PAUSE MENU",
                () => Closed?.Invoke(this, EventArgs.Empty), primary: true);
            ControllerNav.Identify(close, "campaign.journal.back", initial: true);

            var layout = PrimeChrome.Stack(
                new PrimeBadge("NARRATIVE CAMPAIGN  //  SCAN ARCHIVE"),
                PrimeChrome.Title("ALIMBIC EXPLORATION"),
                PrimeChrome.Text(
                    "Native 3D pause map and scan visor remain in the game. "
                    + "This journal summarizes only rooms you visited and entries you scanned.",
                    PrimeTypography.BodySmall, PrimeTheme.TextSecondaryBrush),
                PrimeChrome.Columns("*,*", _map, _scans),
                _section,
                _category,
                new ScrollViewer
                {
                    Content = _entries,
                    MaxHeight = 410,
                    HorizontalScrollBarVisibility =
                        Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility =
                        Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
                },
                close);
            Content = new PrimePanel(new ScrollViewer
            {
                Content = layout,
                MaxHeight = 680,
                HorizontalScrollBarVisibility =
                    Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility =
                    Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
            });
            ShowMap();
        }

        private void ShowMap()
        {
            _mapOpen = true;
            _map.Selected = true;
            _scans.Selected = false;
            _category.IsVisible = false;
            RefreshEntries();
        }

        private void ShowScans()
        {
            _mapOpen = false;
            _map.Selected = false;
            _scans.Selected = true;
            _category.IsVisible = true;
            RefreshEntries();
        }

        private void RefreshEntries()
        {
            _entries.Children.Clear();
            if (_mapOpen)
            {
                _section.Text = $"DISCOVERED ROOMS {_journal.Rooms.Count}  //  "
                    + $"OCTOLITHS {_journal.Octoliths}/8";
                foreach (CampaignPlanetStatus planet in _journal.Planets)
                {
                    _entries.Children.Add(PrimeChrome.Eyebrow(planet.Planet.Name));
                    if (!planet.Unlocked)
                    {
                        _entries.Children.Add(PrimeChrome.Text("SECTOR LOCKED",
                            PrimeTypography.BodySmall, PrimeTheme.TextSecondaryBrush));
                        continue;
                    }
                    _entries.Children.Add(PrimeChrome.Text(
                        $"OCTOLITHS {planet.Octoliths}/{planet.OctolithTotal}"
                        + $"  //  ARTIFACTS {planet.Artifacts}/{planet.ArtifactTotal}"
                        + $"  //  {(planet.Cleared ? "CLEARED" : "IN PROGRESS")}",
                        PrimeTypography.DataSmall, PrimeTheme.TextSecondaryBrush, data: true));
                    int count = 0;
                    foreach (CampaignVisitedRoom room in _journal.Rooms)
                    {
                        if (room.Planet != planet.Planet.Id) continue;
                        _entries.Children.Add(PrimeChrome.Text(
                            (room.LandingSite ? "◈  " : "•  ") + room.DisplayName,
                            13, PrimeTheme.TextBrush));
                        count++;
                    }
                    if (count == 0)
                        _entries.Children.Add(PrimeChrome.Text("No explored rooms",
                            PrimeTypography.BodySmall, PrimeTheme.TextSecondaryBrush));
                }
                return;
            }

            _section.Text = $"SCAN LOGBOOK  //  {_journal.Scans.Count} LISTED"
                + $"  //  {_journal.DiscoveredScanBits} DISCOVERED";
            if (!_journal.HasLogbookTable)
            {
                _entries.Children.Add(PrimeChrome.Text(
                    "Scan logbook text is unavailable from the local game files. "
                    + "Previously recorded scan bits are preserved and have not been reset.",
                    PrimeTypography.Body, PrimeTheme.WarningBrush));
                return;
            }
            int shown = 0;
            foreach (CampaignJournalScan scan in _journal.Scans)
            {
                if (!Matches(scan.Category, _category.Index)) continue;
                shown++;
                _entries.Children.Add(PrimeChrome.Eyebrow(
                    $"#{scan.Id:000}  //  {CategoryName(scan.Category)}"));
                _entries.Children.Add(PrimeChrome.Text(scan.Title,
                    14, PrimeTheme.TextBrush));
                if (!String.IsNullOrWhiteSpace(scan.Description))
                    _entries.Children.Add(PrimeChrome.Text(scan.Description,
                        PrimeTypography.BodySmall, PrimeTheme.TextSecondaryBrush));
            }
            if (shown == 0)
                _entries.Children.Add(PrimeChrome.Text("No scanned entries in this category.",
                    PrimeTypography.Body, PrimeTheme.TextSecondaryBrush));
        }

        private static bool Matches(char category, int index) => index switch
        {
            0 => true,
            1 => category == 'L',
            2 => category == 'B',
            3 => category == 'O',
            4 => category == 'E',
            _ => category is not ('L' or 'B' or 'O' or 'E')
        };

        private static string CategoryName(char category) => category switch
        {
            'L' => "LORE",
            'B' => "BIOFORM",
            'O' => "OBJECT",
            'E' => "EQUIPMENT",
            _ => "OTHER"
        };

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Closed?.Invoke(this, EventArgs.Empty);
                return;
            }
            base.OnKeyDown(e);
        }
    }
}
