#if MPHREAD_AVALONIA
using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MphRead.Mods.Campaign;
using MphRead.Mods.Sound;

namespace MphRead.Mods.Launcher.Gui
{
    /// <summary>
    /// The two simple ROM-inspired ship pages share one Project Prime audio
    /// owner and the existing AdventureLaunch. No hosted Scene, GPU resources,
    /// soundtrack copy or alternate game-loop survives inside this screen.
    /// </summary>
    internal sealed class CampaignShipView : UserControl
    {
        public event EventHandler<LaunchPlan>? Launched;
        public event EventHandler? Closed;

        private readonly CampaignDockReceipt _dock;
        private readonly IReadOnlyList<CampaignPlanetStatus> _statuses;
        private readonly Func<Hunter> _hunter;
        private readonly StackPanel _dockPage;
        private readonly StackPanel _starMapPage;
        private readonly TextBlock _detail;
        private readonly TextBlock _intro;
        private readonly PrimeButton _land;
        private readonly Dictionary<CampaignPlanet, PrimeButton> _planets = new();
        private readonly DispatcherTimer _audioTimer;
        private CampaignAudioSession? _audio;
        private CampaignPlanet _selected;
        private bool _starMap;

        public CampaignShipView(CampaignDockReceipt dock, IReadOnlyList<CampaignPlanetStatus> statuses,
            Func<Hunter> hunter)
        {
            _dock = dock;
            _statuses = statuses;
            _hunter = hunter;
            _selected = dock.Planet;

            _detail = PrimeChrome.Text("", 14, PrimeTheme.TextSecondaryBrush);
            _intro = PrimeChrome.Text(
                "Hunter Gunship secure. Expedition progress was written to the selected save slot. "
                + "Exit Ship to return on foot, or Launch Ship to select an unlocked planet.",
                13, PrimeTheme.TextSecondaryBrush);

            var leave = new PrimeButton("EXIT SHIP / RETURN TO SURFACE",
                () => Launch(_dock.Planet, exitShipToSurface: true));
            var starMap = new PrimeButton("LAUNCH SHIP / OPEN STAR MAP",
                ShowStarMap, primary: true);
            ControllerNav.Identify(leave, "campaign.ship.exit");
            ControllerNav.Identify(starMap, "campaign.ship.open-map");
            _dockPage = PrimeChrome.Stack(
                PrimeChrome.Eyebrow("DOCKED GUNSHIP  //  CHECKPOINT COMPLETE"),
                PrimeChrome.Text("SHIP COCKPIT MUSIC: ORIGINAL LOCAL CARTRIDGE SEQUENCE",
                    12, PrimeTheme.TextSecondaryBrush, data: true),
                PrimeChrome.Columns("*,*", leave, starMap));

            var rows = new StackPanel { Spacing = 7 };
            foreach (CampaignPlanetStatus status in statuses)
            {
                CampaignPlanet planet = status.Planet.Id;
                var button = new PrimeButton(status.Planet.Name
                    + (status.Unlocked ? "" : "  //  LOCKED"), () => SelectPlanet(planet));
                ControllerNav.Identify(button, "campaign.ship.planet" + (int)planet);
                _planets.Add(planet, button);
                rows.Children.Add(button);
            }
            _land = new PrimeButton("LAND SHIP", () => Launch(_selected), primary: true);
            var returnDock = new PrimeButton("RETURN TO COCKPIT", () => ShowDock());
            ControllerNav.Identify(_land, "campaign.ship.land");
            ControllerNav.Identify(returnDock, "campaign.ship.return");
            _starMapPage = PrimeChrome.Stack(
                PrimeChrome.Eyebrow("ALIMBIC CLUSTER  //  DISCOVERED DESTINATIONS"),
                PrimeChrome.Text("STAR MAP AUDIO: ORIGINAL LOCAL CARTRIDGE SEQUENCE",
                    12, PrimeTheme.TextSecondaryBrush, data: true),
                rows, PrimeChrome.Columns("*,*", returnDock, _land));

            var back = new PrimeButton("RETURN TO CAMPAIGN FILE SELECT", Close);
            ControllerNav.Identify(back, "campaign.ship.close");
            var root = PrimeChrome.Stack(
                new PrimeBadge("CAMPAIGN // HUNTER GUNSHIP", PrimeTheme.GreenBrush),
                PrimeChrome.Title("SHIP NAVIGATION"),
                _intro, _detail,
                _dockPage, _starMapPage, back);

            Content = new PrimePanel(new ScrollViewer
            {
                Content = root,
                MaxHeight = 680,
                HorizontalScrollBarVisibility =
                    Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility =
                    Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
            });
            _audioTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _audioTimer.Tick += (_, _) => _audio?.Tick();
            ShowDock(updateAudio: false);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _audio?.Dispose();
            _audio = new CampaignAudioSession();
            _audio.Enter(_starMap ? CampaignAudioPage.StarMap : CampaignAudioPage.Dock, _dock.Planet);
            _audioTimer.Start();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            StopAudio();
            base.OnDetachedFromVisualTree(e);
        }

        private void StopAudio()
        {
            _audioTimer.Stop();
            _audio?.Dispose();
            _audio = null;
        }

        private void SelectPlanet(CampaignPlanet planet)
        {
            _selected = planet;
            foreach (var (id, button) in _planets) button.Selected = id == planet;
            CampaignPlanetStatus selected = _statuses[(int)planet];
            string state = selected.Unlocked
                ? (selected.Cleared ? "AREA CLEARED" : "ACCESS GRANTED")
                : "SECTOR LOCKED";
            _detail.Text = $"{selected.Planet.Name}  //  {state}\n"
                + (selected.Unlocked
                    ? $"OCTOLITHS {selected.Octoliths}/{selected.OctolithTotal}  //  "
                    + $"ARTIFACTS {selected.Artifacts}/{selected.ArtifactTotal}\n"
                    + (selected.Visited ? "PREVIOUSLY VISITED" : "FIRST LANDING")
                    : "Explore the Alimbic Cluster to unlock this destination.");
            _land.IsEnabled = selected.Unlocked;
        }

        private void ShowDock(bool updateAudio = true)
        {
            _starMap = false;
            _dockPage.IsVisible = true;
            _starMapPage.IsVisible = false;
            _intro.IsVisible = true;
            CampaignPlanetStatus atDock = _statuses[(int)_dock.Planet];
            _detail.Text = $"{atDock.Planet.Name}  //  SHIP DOCKED\n"
                + $"OCTOLITHS {atDock.Octoliths}/{atDock.OctolithTotal}  //  "
                + $"ARTIFACTS {atDock.Artifacts}/{atDock.ArtifactTotal}";
            if (updateAudio) _audio?.Enter(CampaignAudioPage.Dock, _dock.Planet);
        }

        private void ShowStarMap()
        {
            _starMap = true;
            _dockPage.IsVisible = false;
            _starMapPage.IsVisible = true;
            _intro.IsVisible = false;
            _audio?.Enter(CampaignAudioPage.StarMap);
            SelectPlanet(_selected);
        }

        private void Launch(CampaignPlanet destination, bool exitShipToSurface = false)
        {
            if (exitShipToSurface && destination != _dock.Planet) return;
            if (!CampaignShipTravel.TryChooseLanding(GameState.PeekSave(_dock.Slot),
                MatchStart.RecentShipDock, _dock.Slot, newGame: false, destination,
                out _, out string reason))
            {
                _detail.Text = reason;
                UiFeedbackAudio.Play(UiFeedbackCue.Error);
                return;
            }
            _audio?.LandingCue();
            StopAudio();
            Launched?.Invoke(this, AdventureLaunch.Create(
                _dock.Slot, newGame: false, _hunter(), destination, exitShipToSurface));
        }

        private void Close()
        {
            StopAudio();
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }
}
#endif
