#if MPHREAD_AVALONIA
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MphRead.Entities;
using MphRead.Mods.Network;
namespace MphRead.Mods.Launcher.Gui
{
    internal sealed class OfflineWorkspace : UserControl, IPrimeWorkspace
    {
        public event EventHandler<LaunchPlan>? Launched;
        private readonly MenuSettings _settings;
        private readonly IReadOnlyList<string> _rooms;
        private readonly PrimeOverlayHost _overlays;
        private readonly ChoiceRow _hunter, _suit, _mode, _format, _bots, _skill;
        private readonly PrimeButton _map, _resume, _newRun, _start;
        private readonly TextBlock _saveDetail;
        private readonly PrimeButton[] _slots = new PrimeButton[AdventureSave.SlotCount];
        private readonly Image _preview = new() { Height = 120, Stretch = Stretch.UniformToFill };
        private readonly PrimeHeroPanel _matchHero;
        private readonly TextBlock _arenaTitle = PrimeChrome.HeroTitle("SELECT ARENA");
        private readonly TextBlock _matchSummary = PrimeChrome.Eyebrow(
            "MATCH SETUP // LOCAL SIMULATION");
        private readonly HunterStand _stand;
        private string _room;
        private byte _slot = 1;
        public OfflineWorkspace(MenuSettings settings, IReadOnlyList<string> rooms, PrimeOverlayHost overlays)
        {
            _settings = settings; _rooms = rooms; _overlays = overlays;
            _room = rooms.Contains(settings.RoomKey) ? settings.RoomKey : rooms.FirstOrDefault() ?? "";
            var names = Multiplayer.HunterRules.Pool(settings.LowTier == "on").Select(h => h.ToString()).Append("Random").ToArray();
            _hunter = new ChoiceRow("Hunter", names, Math.Max(0, Array.IndexOf(names, LauncherPrefs.LastHunter.ToString())));
            _suit = new ChoiceRow("Suit", new[] { "1", "2", "3", "4" }, Math.Clamp(LauncherPrefs.LastColor, 0, 3));
            _mode = new ChoiceRow("Game type", MatchTypeCatalog.GameTypes.Select(m => m.Label).ToArray());
            _format = new ChoiceRow("Matchup", MatchTypeCatalog.BasicMatchups.Select(m => m.Label).ToArray(),
                settings.TeamPlay == "on" ? MatchTypeCatalog.BasicMatchupIndex(MatchFormat.Auto) : 0);
            _mode.Changed += (_, _) => SyncMatchup();
            _format.Changed += (_, _) => SyncMatchup();
            SyncMatchup();
            _bots = new ChoiceRow("Combatants", Enumerable.Range(0, PlayerEntity.SlotCapacity).Select(i => i + " BOTS").ToArray(), Math.Clamp(LauncherPrefs.Bots, 0, 7));
            _skill = new ChoiceRow("Difficulty", new[] { "Easy", "Normal", "Hard", "Insane" }, Math.Clamp(LauncherPrefs.BotLevel, 0, 3));
            _map = new PrimeButton("SELECT ARENA", PickMap);
            ControllerNav.Identify(_map, "offline.map");
            var start = _start = new PrimeButton("▷ INITIATE BOT SIMULATION", () =>
            {
                if (_room.Length == 0) return;
                var plan = OfflineLaunch.Create(settings, _room, SelectedMode(),
                    SelectedHunter(), _suit.Index, _bots.Index, _skill.Index);
                if (!Multiplayer.MapModeCapabilities.Supports(_room, plan.Mode,
                    Multiplayer.MatchWorldProfile.Resolve(_bots.Index + 1), out string reason, _bots.Index + 1)
                    || !Multiplayer.MatchModifierRules.Validate(plan.MatchRules, out reason))
                {
                    _overlays.Show(new PrimePanel(PrimeChrome.Stack(PrimeChrome.Text(reason, 14, PrimeTheme.DangerBrush),
                        new PrimeButton("BACK", _overlays.Close))), PrimeModalSize.Medium);
                    return;
                }
                Launched?.Invoke(this, plan);
            }, true) { IsEnabled = rooms.Count > 0 };
            ControllerNav.Identify(start, "offline.start");

            var matchPanel = new PrimePanel(PrimeChrome.Stack(
                PrimeChrome.Eyebrow("MATCH"),
                PrimeChrome.Title("MATCH FORMAT"),
                PrimeChrome.Text(
                    "Choose the game type, matchup and bot opposition.",
                    PrimeTypography.BodySmall, PrimeTheme.TextSecondaryBrush),
                _mode,
                _format,
                _bots,
                _skill,
                new PrimeButton("ADVANCED MATCH RULES", Rules)),
                raised: true)
            { Padding = new Thickness(12) };

            var arenaCopy = new StackPanel
            {
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Bottom
            };
            arenaCopy.Children.Add(PrimeChrome.Eyebrow("ARENA"));
            arenaCopy.Children.Add(_arenaTitle);
            arenaCopy.Children.Add(PrimeChrome.Text(
                "Choose a compatible arena for this local simulation.",
                PrimeTypography.BodySmall, PrimeTheme.TextSecondaryBrush));
            arenaCopy.Children.Add(_map);
            _matchHero = new PrimeHeroPanel(arenaCopy, minHeight: 220)
            {
                VerticalAlignment = VerticalAlignment.Stretch
            };

            _stand = new HunterStand
            {
                MinHeight = 160,
                Height = 180,
                Name2 = _hunter.Value,
                Suit = _suit.Index,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var deploymentPanel = new PrimePanel(PrimeChrome.Stack(
                PrimeChrome.Eyebrow("DEPLOYMENT"),
                PrimeChrome.Title("HUNTER"),
                PrimeChrome.Text(
                    "Set the local hunter and suit used when the simulation begins.",
                    PrimeTypography.BodySmall, PrimeTheme.TextSecondaryBrush),
                _stand,
                _hunter,
                _suit),
                raised: true)
            { Padding = new Thickness(12) };

            var setup = new Grid
            {
                ColumnDefinitions = new("0.95*,1.15*,0.9*"),
                ColumnSpacing = 12,
                RowSpacing = 12
            };
            setup.Children.Add(matchPanel);
            Grid.SetColumn(_matchHero, 1);
            setup.Children.Add(_matchHero);
            Grid.SetColumn(deploymentPanel, 2);
            setup.Children.Add(deploymentPanel);

            _matchSummary.VerticalAlignment = VerticalAlignment.Center;
            var launchBar = new PrimePanel(
                PrimeChrome.Columns("*,Auto", _matchSummary, start))
            { Padding = new Thickness(10, 8) };

            var primary = new Grid
            {
                RowDefinitions = new("*,Auto"),
                RowSpacing = 10
            };
            primary.Children.Add(setup);
            Grid.SetRow(launchBar, 1);
            primary.Children.Add(launchBar);

            _saveDetail = PrimeChrome.Text("", 13, PrimeTheme.TextSecondaryBrush);
            _resume = new PrimeButton("▷ RESUME", () => Adventure(false), true);
            ControllerNav.Identify(_resume, "offline.adventure.resume");
            var adventureBody = PrimeChrome.Stack(
                new PrimeBadge("MODE 03 // NARRATIVE CAMPAIGN", PrimeTheme.GreenBrush),
                PrimeChrome.Title("ADVENTURE RUNS"),
                PrimeChrome.Text(
                    "Explore the Alimbic Cluster and recover the Octoliths.",
                    14, PrimeTheme.TextSecondaryBrush));
            for (byte i = 1; i <= AdventureSave.SlotCount; i++)
            {
                byte slot = i;
                var button = new PrimeButton("SLOT " + i, () => SelectSlot(slot));
                ControllerNav.Identify(button, $"offline.adventure.slot{i}");
                _slots[i - 1] = button;
                adventureBody.Children.Add(button);
            }
            adventureBody.Children.Add(_saveDetail);
            _newRun = new PrimeButton("+ NEW RUN", () => Adventure(true));
            ControllerNav.Identify(_newRun, "offline.adventure.new");
            var adventure = WithAction(adventureBody,
                PrimeChrome.Columns("*,*", _resume, _newRun));
            var training = TrainingCard();

            var secondary = new StackPanel { Spacing = 10 };
            secondary.Children.Add(PrimeChrome.Eyebrow("OTHER OFFLINE MODES"));
            secondary.Children.Add(PrimeChrome.Title("TRAINING & ADVENTURE"));
            secondary.Children.Add(PrimeChrome.Text(
                "Specialized drills and campaign saves remain available without competing with Match Setup.",
                PrimeTypography.BodySmall, PrimeTheme.TextSecondaryBrush));
            secondary.Children.Add(training);
            secondary.Children.Add(adventure);

            var secondaryScroll = new ScrollViewer
            {
                Content = secondary,
                HorizontalScrollBarVisibility =
                    Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility =
                    Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
            };
            var secondaryPanel = new PrimePanel(secondaryScroll)
            {
                Padding = new Thickness(10)
            };

            var body = new Grid
            {
                ColumnDefinitions = new("1.8*,0.85*"),
                ColumnSpacing = 14,
                RowSpacing = 14
            };
            body.Children.Add(primary);
            Grid.SetColumn(secondaryPanel, 1);
            body.Children.Add(secondaryPanel);

            var root = new Grid
            {
                Margin = PrimeMetrics.PageMargin,
                RowDefinitions = new("Auto,*"),
                RowSpacing = 14
            };
            root.Children.Add(HubChrome.Header(
                "OPERATIONS // OFFLINE",
                "OFFLINE MATCH SETUP",
                "Configure a local match, then deploy. Training and Adventure stay one step away.",
                "STANDALONE"));
            Grid.SetRow(body, 1);
            root.Children.Add(body);

            root.SizeChanged += (_, e) =>
            {
                bool compact = PrimeMetrics.IsNarrow(e.NewSize);
                body.ColumnDefinitions = compact
                    ? new ColumnDefinitions("*")
                    : new ColumnDefinitions("1.8*,0.85*");
                body.RowDefinitions = compact
                    ? new RowDefinitions("Auto,*")
                    : new RowDefinitions("*");
                Grid.SetColumn(primary, 0);
                Grid.SetRow(primary, 0);
                Grid.SetColumn(secondaryPanel, compact ? 0 : 1);
                Grid.SetRow(secondaryPanel, compact ? 1 : 0);

                bool narrowSetup = PrimeMetrics.IsPhoneLayout(e.NewSize);
                setup.ColumnDefinitions = narrowSetup
                    ? new ColumnDefinitions("*")
                    : new ColumnDefinitions("0.95*,1.15*,0.9*");
                setup.RowDefinitions = narrowSetup
                    ? new RowDefinitions("Auto,Auto,Auto")
                    : new RowDefinitions("*");
                Grid.SetColumn(matchPanel, 0);
                Grid.SetRow(matchPanel, 0);
                Grid.SetColumn(_matchHero, narrowSetup ? 0 : 1);
                Grid.SetRow(_matchHero, narrowSetup ? 1 : 0);
                Grid.SetColumn(deploymentPanel, narrowSetup ? 0 : 2);
                Grid.SetRow(deploymentPanel, narrowSetup ? 2 : 0);
            };

            _hunter.Changed += (_, _) =>
            {
                _stand.Name2 = _hunter.Value;
                RefreshMatchChrome();
            };
            _suit.Changed += (_, _) =>
            {
                _stand.Suit = _suit.Index;
                RefreshMatchChrome();
            };
            _mode.Changed += (_, _) => RefreshMatchChrome();
            _format.Changed += (_, _) => RefreshMatchChrome();
            _bots.Changed += (_, _) => RefreshMatchChrome();
            _skill.Changed += (_, _) => RefreshMatchChrome();

            Content = root;
            Refresh();
        }
        private Control TrainingCard()
        {
            var saved = LauncherPrefs.Training.Sanitize();
            var hunterNames = Enumerable.Range(0, Hunters.Playable).Select(i => ((Hunter)i).ToString()).Append("Random").ToArray();
            var trainerHunter = new ChoiceRow("Hunter", hunterNames, Math.Max(0, Array.IndexOf(hunterNames, LauncherPrefs.LastHunter.ToString())));
            var drill = new ChoiceRow("Drill", Enum.GetNames<Mods.Training.AimTrainerDrill>().Select(Mods.Training.TrainingLabels.Display).ToArray(), (int)saved.Drill);
            var weapon = new ChoiceRow("Weapon", Enumerable.Range(0, 8).Select(i => Mods.Training.TrainingLabels.Display(((BeamType)i).ToString())).ToArray(), (int)saved.Weapon);
            var duration = new ChoiceRow("Duration", new[] { "30", "60", "120", "300" }, saved.DurationSeconds == 30 ? 0 : saved.DurationSeconds == 120 ? 2 : saved.DurationSeconds == 300 ? 3 : 1);
            var targets = new ChoiceRow("Targets", Enumerable.Range(1, 7).Select(i => i.ToString()).ToArray(), saved.TargetCount - 1);
            var movement = new ChoiceRow("Movement", Enum.GetNames<Mods.Training.AimTrainerMovement>().Select(Mods.Training.TrainingLabels.Display).ToArray(), (int)saved.Movement);
            void ConfigureDrill(bool changed)
            {
                var selected = (Mods.Training.AimTrainerDrill)drill.Index;
                if (changed)
                {
                    if (selected == Mods.Training.AimTrainerDrill.StrafeTracking) movement.Index = (int)Mods.Training.AimTrainerMovement.HorizontalStrafe;
                    else if (selected == Mods.Training.AimTrainerDrill.JumpTracking) movement.Index = (int)Mods.Training.AimTrainerMovement.JumpStrafe;
                    else if (selected is Mods.Training.AimTrainerDrill.TimedFlick or Mods.Training.AimTrainerDrill.MultiTargetFlick) movement.Index = 0;
                    if (selected == Mods.Training.AimTrainerDrill.MultiTargetFlick) targets.Index = 4;
                    if (selected == Mods.Training.AimTrainerDrill.ImperialistPrecision) weapon.Index = (int)BeamType.Imperialist;
                }
                if (selected == Mods.Training.AimTrainerDrill.TimedFlick) targets.Index = 0;
                if (selected == Mods.Training.AimTrainerDrill.MultiTargetFlick) targets.Index = Math.Clamp(targets.Index, 3, 4);
                targets.IsEnabled = selected != Mods.Training.AimTrainerDrill.TimedFlick;
                weapon.IsEnabled = selected != Mods.Training.AimTrainerDrill.ImperialistPrecision;
            }
            drill.Changed += (_, _) => ConfigureDrill(true);
            targets.Changed += (_, _) => ConfigureDrill(false);
            ConfigureDrill(false);
            ControllerNav.Identify(drill, "offline.training.drill");
            ControllerNav.Identify(targets, "offline.training.targets");
            ControllerNav.Identify(movement, "offline.training.movement");
            var difficulty = new ChoiceRow("Difficulty", Enum.GetNames<Mods.Training.TrainingDifficulty>(), (int)saved.Difficulty);
            var distance = new ChoiceRow("Target distance", Enum.GetNames<Mods.Training.TrainingDistance>(), (int)saved.Distance);
            var scope = new ChoiceRow("Imperialist scope", Enum.GetNames<Mods.Training.TrainingScope>(), (int)saved.Scope);
            var heads = new ToggleRow("Headshots only", saved.HeadshotsOnly);
            void ConfigureHeadshots()
            {
                bool required = (Mods.Training.AimTrainerDrill)drill.Index == Mods.Training.AimTrainerDrill.HeadshotPrecision;
                if (required) heads.On = true;
                heads.IsEnabled = !required;
            }
            drill.Changed += (_, _) => ConfigureHeadshots();
            ConfigureHeadshots();
            var ammo = new ToggleRow("Infinite ammo", saved.InfiniteAmmo);
            var reload = new ToggleRow("Refill ammo on hit", saved.ReloadOnHit);
            var clickReload = new ToggleRow("Imperialist: click skips reload", !saved.NormalImperialistReload);
            var seed = new ToggleRow("Fixed seed", saved.FixedSeed);
            var advancedView = new PrimePanel(PrimeChrome.Stack(PrimeChrome.Title("TRAINING OPTIONS"), difficulty, scope, distance, heads, ammo, reload, clickReload, seed,
                new PrimeButton("DONE", _overlays.Close)));
            var advanced = new PrimeButton("ADVANCED TRAINING OPTIONS", () => _overlays.Show(advancedView, PrimeModalSize.Medium));
            var start = new PrimeButton("▷ INITIATE TRAINING", () => Launched?.Invoke(this,
                AimTrainerLaunch.Create(new Mods.Training.AimTrainerDefinition
                {
                    Drill = (Mods.Training.AimTrainerDrill)drill.Index, Weapon = (BeamType)weapon.Index,
                    DurationSeconds = int.Parse(duration.Value), TargetCount = targets.Index + 1,
                    Movement = (Mods.Training.AimTrainerMovement)movement.Index,
                    Difficulty = (Mods.Training.TrainingDifficulty)difficulty.Index,
                    Distance = (Mods.Training.TrainingDistance)distance.Index,
                    Scope = (Mods.Training.TrainingScope)scope.Index, HeadshotsOnly = heads.On,
                    InfiniteAmmo = ammo.On, ReloadOnHit = reload.On, NormalImperialistReload = !clickReload.On, FixedSeed = seed.On,
                    Seed = seed.On ? saved.Seed : (uint)Random.Shared.Next(1, int.MaxValue)
                }, Enum.Parse<Hunter>(trainerHunter.Value), _suit.Index)), true) { IsEnabled = GameFiles.Ready };
            ControllerNav.Identify(start, "offline.training.start");
            return WithAction(PrimeChrome.Stack(new PrimeBadge("MODE 02 // TARGETING SIMULATION"),
                PrimeChrome.Title("AIM TRAINER"), PrimeChrome.Text("Timed Flick: hit before 1.5 seconds. Multi Target Flick: 4–5 targets across ground and elevated platforms. Imperialist reload can be skipped with each new click; holding fire keeps normal timing."),
                trainerHunter, drill, weapon, duration, targets, movement, advanced), start);
        }
        private static Control WithAction(Control content, Control action)
        {
            var grid = new Grid { RowDefinitions = new("*,Auto"), RowSpacing = 12 };
            grid.Children.Add(new ScrollViewer { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
            Grid.SetRow(action, 1); grid.Children.Add(action); return new PrimePanel(grid);
        }
        private void SelectSlot(byte slot)
        {
            _slot = slot;
            for (byte i = 1; i <= _slots.Length; i++)
            {
                var info = AdventureSave.Read(i);
                _slots[i - 1].Label = $"SLOT {i:00} // " + (info.Used ? info.Area.ToUpperInvariant() : "EMPTY");
                _slots[i - 1].Selected = i == slot;
            }
            var save = AdventureSave.Read(slot);
            _resume.IsEnabled = save.Used;
            _saveDetail.Text = save.Used ? $"{save.Area}\nOCTOLITHS: {save.Octoliths} / 8\nENERGY: {save.Health} / {save.HealthMax}"
                : "Initialize a fresh expedition from Celestial Archives.";
        }
        private Hunter SelectedHunter()
        {
            return Enum.TryParse(_hunter.Value, ignoreCase: true, out Hunter hunter)
                && Enum.IsDefined(hunter) ? hunter : Hunter.Samus;
        }

        private void Adventure(bool fresh)
        {
            void Launch() => Launched?.Invoke(this,
                AdventureLaunch.Create(_slot, fresh, SelectedHunter()));
            if (fresh && AdventureSave.Read(_slot).Used)
            {
                _overlays.Show(new PrimePanel(PrimeChrome.Stack(PrimeChrome.Title("REPLACE SAVE SLOT " + _slot + "?"),
                    PrimeChrome.Text("Starting a new run replaces this slot's existing progress when saved."),
                    PrimeChrome.Columns("*,*", new PrimeButton("CANCEL", _overlays.Close),
                        new PrimeButton("START NEW RUN", () => { _overlays.Close(); Launch(); }, danger: true)))), PrimeModalSize.Small);
            }
            else Launch();
        }
        private GameMode SelectedMode()
        {
            MatchTypeDefinition type = MatchTypeCatalog.GameTypes[Math.Clamp(_mode.Index, 0, MatchTypeCatalog.GameTypes.Length - 1)];
            MatchFormat format = MatchTypeCatalog.BasicMatchups[Math.Clamp(_format.Index, 0, MatchTypeCatalog.BasicMatchups.Length - 1)].Format;
            return type.Resolve(MatchTypeCatalog.NormalizeFormat(type, format));
        }

        private void SyncMatchup()
        {
            MatchTypeDefinition type = MatchTypeCatalog.GameTypes[Math.Clamp(_mode.Index, 0, MatchTypeCatalog.GameTypes.Length - 1)];
            MatchFormat normalized = MatchTypeCatalog.NormalizeFormat(type,
                MatchTypeCatalog.BasicMatchups[Math.Clamp(_format.Index, 0, MatchTypeCatalog.BasicMatchups.Length - 1)].Format);
            int index = MatchTypeCatalog.BasicMatchupIndex(normalized);
            if (_format.Index != index) _format.Index = index;
            _format.IsEnabled = !type.TeamOnly && !type.FfaOnly;
        }

        private void PickMap()
        {
            var picker = new MapCardPicker(_rooms, _room, room =>
                Multiplayer.MapModeCapabilities.Supports(room, SelectedMode(),
                    Multiplayer.MatchWorldProfile.Resolve(_bots.Index + 1), out string reason, _bots.Index + 1) ? null : reason);
            picker.Done += (_, room) => { _room = room; Refresh(); _overlays.Close(); };
            picker.Cancelled += (_, _) => _overlays.Close();
            _overlays.Show(picker);
        }
        private void Rules()
        {
            var score = new FieldRow("Point goal", _settings.PointGoal, boxWidth: 88);
            var time = new FieldRow("Time limit (m:ss)", _settings.TimeLimit, boxWidth: 88);
            var objective = new FieldRow("Time goal (m:ss)", _settings.TimeGoal, boxWidth: 88);
            GameMode selectedMode = SelectedMode();
            score.IsVisible = !Network.MatchGoalRules.UsesTimeTarget(selectedMode) && selectedMode is not (GameMode.GunGame or GameMode.OneInTheChamber);
            time.IsVisible = selectedMode != GameMode.OneInTheChamber;
            objective.IsVisible = Network.MatchGoalRules.UsesTimeTarget(selectedMode);
            var autoReset = new ToggleRow("Octolith auto reset", _settings.AutoReset == "on")
            { IsVisible = Multiplayer.MatchModifierRules.UsesOctolith(selectedMode) };
            var fire = new ToggleRow("Friendly fire", _settings.FriendlyFire == "on");
            fire.IsVisible = GameState.IsTeamMode(selectedMode);
            var affinity = new ToggleRow("Affinity weapons", _settings.AffinityWeapons == "on");
            var enhancedHunters = new ToggleRow("Enhanced Hunters", _settings.EnhancedHunters == "on");
            var freeze = new ToggleRow("Shadow freeze", _settings.ShadowFreeze == "on");
            var spawnProtection = new ToggleRow("Spawn protection (3s)", _settings.SpawnProtection == "on");
            var fiesta = new ToggleRow("Fiesta", _settings.Fiesta == "on");
            var instaGib = new ToggleRow("Insta-Gib", _settings.InstaGib == "on");
            var lowTier = new ToggleRow("Low Tier", _settings.LowTier == "on");
            var noImperialist = new ToggleRow("No Imp", _settings.NoImperialist == "on");
            instaGib.Changed += (_, _) => { if (instaGib.On) noImperialist.On = false; };
            noImperialist.Changed += (_, _) => { if (noImperialist.On) instaGib.On = false; };
            var radar = new ToggleRow("Hunter radar", _settings.HunterRadar == "on");
            var damage = new ChoiceRow("Damage", new[] { "low", "medium", "high" }, _settings.DamageLevel == "low" ? 0 : _settings.DamageLevel == "high" ? 2 : 1);
            var error = PrimeChrome.Text("", 12, PrimeTheme.DangerBrush);
            static Control Section(string title, params Control[] rows)
            {
                var section = new StackPanel { Spacing = 8 };
                section.Children.Add(PrimeChrome.Text(title, 11, PrimeTheme.HighlightBrush, data: true));
                foreach (var row in rows) section.Children.Add(row);
                return new PrimePanel(section, raised: true) { Padding = new Thickness(12) };
            }
            var body = PrimeChrome.Stack(PrimeChrome.Title("MATCH RULES"),
                PrimeChrome.Columns("*,*,*",
                    Section("MATCH", PrimeChrome.Text(selectedMode == GameMode.OneInTheChamber ? "3 lives • Last player standing\nOne lethal shot • Kills earn ammo" : "", 12, PrimeTheme.TextSecondaryBrush), score, time, objective, damage),
                    Section("GAMEPLAY", fire, affinity, enhancedHunters, freeze, spawnProtection, radar),
                    Section("ADVANCED", fiesta, instaGib, lowTier, noImperialist, autoReset)), error,
                PrimeChrome.Columns("*,*", new PrimeButton("CANCEL", _overlays.Close), new PrimeButton("APPLY RULES", () =>
                {
                    bool Duration(string value) => TimeSpan.TryParseExact(value, @"m\:ss", null, out _)
                        || TimeSpan.TryParseExact(value, @"mm\:ss", null, out _);
                    if (!int.TryParse(score.Value, out int points) || points < 0 || points > 999 || !Duration(time.Value) || !Duration(objective.Value))
                    { error.Text = "Use a point goal from 0–999 and durations as m:ss."; return; }
                    // The shell reloads settings.json after every completed or abandoned
                    // match. These controls used to change only this MenuSettings instance,
                    // so the first match saw the edit and the next reload restored 7:00/7.
                    // Treat APPLY RULES like the main settings screen: persist first, then
                    // keep the runtime settings facade in sync with the committed values.
                    var rules = new Network.MatchDefinition { Mode = SelectedMode(),
                        InstaGib = instaGib.On, NoImperialist = noImperialist.On, Fiesta = fiesta.On };
                    if (!Multiplayer.MatchModifierRules.Validate(rules, out string reason)) { error.Text = reason; return; }
                    string oldAutoReset = _settings.AutoReset;
                    string oldFiesta = _settings.Fiesta, oldChamber = _settings.OneInTheChamber;
                    string oldInstaGib = _settings.InstaGib;
                    string oldLowTier = _settings.LowTier;
                    string oldNoImperialist = _settings.NoImperialist;
                    string oldPointGoal = _settings.PointGoal;
                    string oldTimeLimit = _settings.TimeLimit;
                    string oldTimeGoal = _settings.TimeGoal;
                    string oldFriendlyFire = _settings.FriendlyFire;
                    string oldAffinityWeapons = _settings.AffinityWeapons;
                    string oldEnhancedHunters = _settings.EnhancedHunters;
                    string oldShadowFreeze = _settings.ShadowFreeze;
                    string oldSpawnProtection = _settings.SpawnProtection;
                    string oldHunterRadar = _settings.HunterRadar;
                    string oldDamageLevel = _settings.DamageLevel;
                    _settings.AutoReset = autoReset.On ? "on" : "off";
                    _settings.Fiesta = fiesta.On ? "on" : "off";
                    _settings.OneInTheChamber = "off";
                    _settings.InstaGib = instaGib.On ? "on" : "off";
                    _settings.LowTier = lowTier.On ? "on" : "off";
                    _settings.NoImperialist = noImperialist.On ? "on" : "off";
                    _settings.PointGoal = score.Value;
                    _settings.TimeLimit = time.Value;
                    _settings.TimeGoal = objective.Value;
                    _settings.FriendlyFire = fire.On ? "on" : "off";
                    _settings.AffinityWeapons = affinity.On ? "on" : "off";
                    _settings.EnhancedHunters = enhancedHunters.On ? "on" : "off";
                    _settings.ShadowFreeze = freeze.On ? "on" : "off";
                    _settings.SpawnProtection = spawnProtection.On ? "on" : "off";
                    _settings.HunterRadar = radar.On ? "on" : "off";
                    _settings.DamageLevel = damage.Value;
                    try
                    {
                        GameState.CommitSettings(_settings);
                    }
                    catch (Exception ex)
                    {
                        // Do not leave a one-match-only in-memory configuration behind if
                        // the disk write fails. The error stays in this sheet so the player
                        // can retry or cancel without silently diverging from settings.json.
                        _settings.AutoReset = oldAutoReset;
                        _settings.Fiesta = oldFiesta; _settings.OneInTheChamber = oldChamber;
                        _settings.InstaGib = oldInstaGib;
                        _settings.LowTier = oldLowTier;
                        _settings.NoImperialist = oldNoImperialist;
                        _settings.PointGoal = oldPointGoal;
                        _settings.TimeLimit = oldTimeLimit;
                        _settings.TimeGoal = oldTimeGoal;
                        _settings.FriendlyFire = oldFriendlyFire;
                        _settings.AffinityWeapons = oldAffinityWeapons;
                        _settings.EnhancedHunters = oldEnhancedHunters;
                        _settings.ShadowFreeze = oldShadowFreeze;
                        _settings.SpawnProtection = oldSpawnProtection;
                        _settings.HunterRadar = oldHunterRadar;
                        _settings.DamageLevel = oldDamageLevel;
                        error.Text = $"Could not save rules: {ex.Message}";
                        return;
                    }
                    Mods.GameSettings.Apply(_settings);
                    var selected = Multiplayer.HunterRules.Sanitize(SelectedHunter(), lowTier.On);
                    var names = Multiplayer.HunterRules.Pool(lowTier.On).Select(h => h.ToString()).Append("Random").ToArray();
                    _hunter.SetItems(names, Array.IndexOf(names, selected.ToString()));
                    _overlays.Close();
                }, true)));
            _overlays.Show(new PrimePanel(body) { Padding = new Thickness(0) }, PrimeModalSize.Wide, fitContent: true);
        }
        private static string RoomName(string key) => Metadata.RoomMetadata.TryGetValue(key, out var meta) ? meta.InGameName ?? key : key;
        public void OnActivated() => Refresh();
        public void OnDeactivated() { }
        public void Refresh()
        {
            if (_room.Length == 0 && _rooms.Count > 0) _room = _rooms[0];
            _start.IsEnabled = _rooms.Count > 0;
            _map.Label = _room.Length == 0
                ? "NO ARENAS // SET UP GAME FILES"
                : RoomName(_room).ToUpperInvariant();
            _preview.Source = _room.Length == 0 ? null : MapShot.For(_room);
            RefreshMatchChrome();
            SelectSlot(_slot);
        }

        private void RefreshMatchChrome()
        {
            if (_room.Length == 0)
            {
                _arenaTitle.Text = "SELECT AN ARENA";
                _matchHero.SetArt(null);
            }
            else
            {
                _arenaTitle.Text = RoomName(_room).ToUpperInvariant();
                _matchHero.SetArt(MapShot.For(_room), 0.78);
            }

            string room = _room.Length == 0 ? "NO ARENA" : RoomName(_room).ToUpperInvariant();
            _matchSummary.Text =
                $"{_mode.Value.ToUpperInvariant()}  //  {_format.Value.ToUpperInvariant()}  //  "
                + $"{_bots.Value.ToUpperInvariant()}  //  {_skill.Value.ToUpperInvariant()}  //  {room}";
        }
    }
}
#endif
