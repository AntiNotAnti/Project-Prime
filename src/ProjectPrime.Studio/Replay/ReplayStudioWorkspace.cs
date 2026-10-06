using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MphRead.Mods.StudioReplay;
using ProjectPrime.Studio.Shell;
using System.Numerics;

namespace ProjectPrime.Studio.Replay;

public sealed class ReplayStudioWorkspace : UserControl, IDisposable
{
    private readonly ReplayStudioSession? _session;
    private readonly List<ReplayViewportHost> _viewports = new();
    private readonly Grid _viewGrid = new();
    private readonly TextBlock _status = new() { Margin = new Thickness(8), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _error = new() { Foreground = Brushes.Orange, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    private readonly ListBox _events = new(), _markers = new(), _keys = new(), _exports = new();
    private readonly TextBox _label = new() { Watermark = "Marker name", Text = "Bookmark" };
    private readonly TextBox _tags = new() { Watermark = "Tags, separated by commas" };
    private readonly TextBox _position = new() { Text = "0, 5, 10", Watermark = "Position X, Y, Z" };
    private readonly TextBox _angles = new() { Text = "0, 0, 0", Watermark = "Yaw, pitch, roll degrees" };
    private readonly NumericUpDown _fov = new() { Minimum = 10, Maximum = 150, Value = 78, Increment = 1 };
    private readonly NumericUpDown _slot = new() { Minimum = 0, Maximum = 7, Value = 0, Increment = 1 };
    private readonly TextBox _combat = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 250 };
    private readonly ComboBox _shotSelection = new();
    private readonly ReplayStudioTimeline? _timeline;
    private ReplayCameraGraph? _cameraGraph;
    private readonly DispatcherTimer? _refresh;
    private readonly DispatcherTimer _viewTimer;
    private readonly System.Diagnostics.Stopwatch _viewClock = System.Diagnostics.Stopwatch.StartNew();
    private TimeSpan _lastViewTime;
    private int _eventCount = -1, _keyCount = -1;
    private string _markerSignature = "";
    private string? _actionError;
    private StudioReplayAnalysis? _analysis;
    private ReplayViewportHost? _comparisonViewport;
    private bool _disposed;
    public ReplayStudioWorkspace(IStudioDocument document)
    {
        _viewTimer = new DispatcherTimer(TimeSpan.FromSeconds(1d / 60), DispatcherPriority.Render, (_, _) =>
        {
            var now = _viewClock.Elapsed; var elapsed = now - _lastViewTime; _lastViewTime = now;
            for (int i = 0; i < _viewports.Count; i++) _viewports[i].RenderExternal(i == 0 ? elapsed : TimeSpan.Zero);
        });
        _session = (document as ReplayStudioDocument)?.Session;
        if (_session == null)
        {
            Content = StudioWorkspaceView.Create(document, "Replay Studio", "Open a .ppdemo recording or .ppclip selection to edit its camera, timeline, annotations and export. The recording remains immutable; edits save in sidecars.");
            return;
        }
        _timeline = new(_session.Player);
        var root = new DockPanel();
        var toolbar = new WrapPanel { Margin = new Thickness(8), Orientation = Orientation.Horizontal };
        toolbar.Children.Add(Button("Play / Pause", () => _session.Player.TogglePause()));
        toolbar.Children.Add(Button("Step", () => _session.Player.StepForward()));
        toolbar.Children.Add(Button("Restart", () => _session.Player.Seek(0)));
        var rate = new ComboBox { ItemsSource = new[] { .25f, .5f, 1, 2, 4 }, SelectedItem = 1f, Width = 90, Margin = new Thickness(4) };
        rate.SelectionChanged += (_, _) => { if (rate.SelectedItem is float value) _session.Player.SetRate(value); }; toolbar.Children.Add(rate);
        toolbar.Children.Add(Button("Mark In", () => _session.Player.MarkIn())); toolbar.Children.Add(Button("Mark Out", () => _session.Player.MarkOut()));
        toolbar.Children.Add(Button("Four Views", () => SetViews(_viewports.Count == 1)));
        toolbar.Children.Add(Button("Retry Viewport", () => { foreach (var viewport in _viewports) viewport.Retry(); }));
        DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        DockPanel.SetDock(_status, Dock.Bottom); root.Children.Add(_status);
        DockPanel.SetDock(_error, Dock.Bottom); root.Children.Add(_error);
        DockPanel.SetDock(_timeline, Dock.Bottom); root.Children.Add(_timeline);
        var inspector = new TabControl { Width = 330, Margin = new Thickness(4) };
        inspector.Items.Add(new TabItem { Header = "Camera", Content = CameraPanel() });
        inspector.Items.Add(new TabItem { Header = "Timeline", Content = TimelinePanel() });
        inspector.Items.Add(new TabItem { Header = "Combat", Content = CombatPanel() });
        inspector.Items.Add(new TabItem { Header = "Analysis", Content = AnalysisPanel() });
        inspector.Items.Add(new TabItem { Header = "Export", Content = ExportPanel() });
        DockPanel.SetDock(inspector, Dock.Right); root.Children.Add(inspector); root.Children.Add(_viewGrid);
        Content = root; SetViews(false);
        _refresh = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => Refresh());
        AttachedToVisualTree += (_, _) => { _refresh.Start(); _lastViewTime = _viewClock.Elapsed; _viewTimer.Start(); };
        DetachedFromVisualTree += (_, _) => { _refresh.Stop(); _viewTimer.Stop(); };
    }
    private Control CameraPanel()
    {
        var panel = Panel();
        var mode = new ComboBox { ItemsSource = Enum.GetValues<StudioReplayCameraMode>(), SelectedItem = _session!.Camera };
        mode.SelectionChanged += (_, _) => { if (mode.SelectedItem is StudioReplayCameraMode value) { _session.Camera = value; Persist(); } };
        panel.Children.Add(new TextBlock { Text = "Presentation camera" }); panel.Children.Add(mode);
        _slot.Value = _session.PlayerSlot;
        _position.Text = VectorText(_session.Position); _angles.Text = VectorText(Angles(_session.Rotation)); _fov.Value = (decimal)_session.Fov;
        _slot.ValueChanged += (_, _) => { _session.PlayerSlot = (int)(_slot.Value ?? 0); Persist(); };
        panel.Children.Add(new TextBlock { Text = "Player slot" }); panel.Children.Add(_slot);
        panel.Children.Add(new TextBlock { Text = "Free / authored camera position" }); panel.Children.Add(_position);
        panel.Children.Add(_angles); panel.Children.Add(new TextBlock { Text = "Field of view" }); panel.Children.Add(_fov);
        var interpolation = new ComboBox { ItemsSource = Enum.GetValues<StudioReplayCameraInterpolation>(), SelectedItem = StudioReplayCameraInterpolation.Spline };
        var ease = new ComboBox { ItemsSource = Enum.GetValues<StudioReplayCameraEase>(), SelectedItem = StudioReplayCameraEase.InOut };
        panel.Children.Add(interpolation); panel.Children.Add(ease);
        panel.Children.Add(Button("Apply Camera", ApplyCamera));
        panel.Children.Add(Button("Put Key at Current Frame", () =>
        {
            ApplyCamera(); Vector3 angles = ParseVector(_angles.Text);
            var radians = angles * (MathF.PI / 180);
            _session.Player.PutCameraKey(new(_session.Player.Status.Frame, _session.Position, Quaternion.CreateFromYawPitchRoll(radians.X, radians.Y, 0), _session.Fov,
                Roll: angles.Z, Interpolation: (StudioReplayCameraInterpolation)interpolation.SelectedItem!, Ease: (StudioReplayCameraEase)ease.SelectedItem!));
            RefreshKeys();
        }));
        panel.Children.Add(Check("Constant speed", _session.ConstantSpeed, value => { _session.ConstantSpeed = value; if (_cameraGraph is { } activeGraph) { activeGraph.ConstantSpeed = value; activeGraph.InvalidateVisual(); } Persist(); }));
        panel.Children.Add(Check("Avoid camera collision", _session.CollisionAvoidance, value => { _session.CollisionAvoidance = value; Persist(); }));
        panel.Children.Add(Check("Show authored / adjusted path", false, value => _session.Player.ShowCameraPath(value)));
        panel.Children.Add(Check("Game HUD", _session.GameHud, value => { _session.GameHud = value; Persist(); }));
        panel.Children.Add(Check("Replay overlay", _session.ReplayOverlay, value => { _session.ReplayOverlay = value; Persist(); }));
        _keys.Height = 180; _keys.SelectionMode = SelectionMode.Multiple;
        _keys.SelectionChanged += (_, _) =>
        {
            if (_keys.SelectedItem is KeyItem key)
            { _session.Player.Seek(key.Key.Frame); _position.Text = VectorText(key.Key.Position); var angles = Angles(key.Key.Rotation); angles.Z = key.Key.Roll; _angles.Text = VectorText(angles); _fov.Value = (decimal)key.Key.Fov; }
        };
        panel.Children.Add(_keys);
        panel.Children.Add(Button("Delete Selected Keys", () =>
        { _session.Player.RemoveCameraKeys(_keys.SelectedItems?.OfType<KeyItem>().Select(k => k.Key.Frame) ?? []); RefreshKeys(); }));
        var graph = new ReplayCameraGraph(_session.Player) { ConstantSpeed = _session.ConstantSpeed };
        _cameraGraph = graph;
        graph.Error += exception => { _actionError = exception.Message; _error.Text = exception.Message; _error.IsVisible = true; };
        var channel = new ComboBox { ItemsSource = Enum.GetValues<ReplayCameraGraphChannel>(), SelectedItem = ReplayCameraGraphChannel.Fov };
        channel.SelectionChanged += (_, _) => { graph.Channel = (ReplayCameraGraphChannel)channel.SelectedItem!; graph.ConstantSpeed = _session.ConstantSpeed; graph.InvalidateVisual(); };
        graph.SelectionChanged += () =>
        {
            _keys.SelectedItems?.Clear();
            foreach (var item in _keys.Items.OfType<KeyItem>().Where(k => graph.SelectedFrames.Contains(k.Key.Frame))) _keys.SelectedItems?.Add(item);
        };
        panel.Children.Add(channel); panel.Children.Add(graph);
        panel.Children.Add(new TextBlock { Text = "Drag the blue incoming or gold outgoing Bezier handle vertically. Alt-drag a key edits its incoming handle. Shift-drag a box selects keys.", TextWrapping = TextWrapping.Wrap });
        var inputHandle = new TextBox { Text = "0, 0, 0", Watermark = "Incoming position tangent X, Y, Z" };
        var outputHandle = new TextBox { Text = "0, 0, 0", Watermark = "Outgoing position tangent X, Y, Z" };
        var scalarHandles = new TextBox { Text = "0, 0, 0, 0", Watermark = "FOV in, FOV out, roll in, roll out (degrees)" };
        var lookAt = new NumericUpDown { Minimum = -1, Maximum = 7, Value = -1 };
        _keys.SelectionChanged += (_, _) =>
        {
            if (_keys.SelectedItem is not KeyItem item) return;
            inputHandle.Text = VectorText(item.Key.IncomingTangent ?? Vector3.Zero); outputHandle.Text = VectorText(item.Key.OutgoingTangent ?? Vector3.Zero);
            scalarHandles.Text = FormattableString.Invariant($"{item.Key.FovIncomingTangent}, {item.Key.FovOutgoingTangent}, {item.Key.RollIncomingTangent}, {item.Key.RollOutgoingTangent}");
            lookAt.Value = item.Key.LookAtSlot;
        };
        panel.Children.Add(new TextBlock { Text = "Selected key handles" }); panel.Children.Add(inputHandle); panel.Children.Add(outputHandle); panel.Children.Add(scalarHandles);
        panel.Children.Add(new TextBlock { Text = "Look-at player slot (-1 = camera rotation)" }); panel.Children.Add(lookAt);
        panel.Children.Add(Button("Apply Selected Bezier Handles / Look-at", () =>
        {
            float[] handles = (scalarHandles.Text ?? "").Split(',').Select(value => float.Parse(value, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            if (handles.Length != 4) throw new FormatException("Enter four handle values in degrees.");
            foreach (var item in _keys.SelectedItems?.OfType<KeyItem>().ToArray() ?? [])
                _session.Player.PutCameraKey(item.Key with { Interpolation = StudioReplayCameraInterpolation.Bezier,
                    IncomingTangent = ParseVector(inputHandle.Text), OutgoingTangent = ParseVector(outputHandle.Text),
                    FovIncomingTangent = handles[0], FovOutgoingTangent = handles[1], RollIncomingTangent = handles[2], RollOutgoingTangent = handles[3], LookAtSlot = (int)(lookAt.Value ?? -1) });
            RefreshKeys(); graph.InvalidateVisual();
        }));
        var translation = new TextBox { Text = "0, 0, 0", Watermark = "Translate selected keys X, Y, Z" };
        var rotation = new TextBox { Text = "0, 0, 0", Watermark = "Rotate selected keys yaw, pitch, roll" };
        var offset = new NumericUpDown { Minimum = -100000, Maximum = 100000, Value = 0 };
        panel.Children.Add(translation); panel.Children.Add(rotation); panel.Children.Add(offset);
        panel.Children.Add(Button("Transform Selected Keys", () =>
        {
            var angles = ParseVector(rotation.Text) * (MathF.PI / 180);
            _session.Player.TransformCameraKeys(_keys.SelectedItems?.OfType<KeyItem>().Select(k => k.Key.Frame) ?? [],
                ParseVector(translation.Text), Quaternion.CreateFromYawPitchRoll(angles.X, angles.Y, angles.Z), (int)(offset.Value ?? 0));
            RefreshKeys(); graph.InvalidateVisual();
        }));
        return new ScrollViewer { Content = panel };
    }
    private Control TimelinePanel()
    {
        var panel = Panel(); panel.Children.Add(_label);
        panel.Children.Add(Button("Add Bookmark", () => { _session!.Player.AddBookmark(_session.Player.Status.Frame, _label.Text ?? "Bookmark"); RefreshMarkers(); }));
        panel.Children.Add(Button("Save Range Annotation", () => { var range = Range(); _session!.Player.AddHighlight(range.Start, range.End, _label.Text ?? "Highlight"); RefreshMarkers(); }));
        panel.Children.Add(Button("Add Range to Reel", () => { var range = Range(); _session!.Player.AddReel(range.Start, range.End, _label.Text ?? "Segment"); RefreshMarkers(); }));
        panel.Children.Add(_tags); panel.Children.Add(Button("Save Tags", () => _session!.Player.SetOrganization((_tags.Text ?? "").Split(','), [])));
        _markers.Height = 150; _markers.SelectionChanged += (_, _) => { if (_markers.SelectedItem is MarkerItem marker) _session!.Player.Seek(marker.Marker.StartFrame); };
        panel.Children.Add(_markers);
        panel.Children.Add(Button("Delete Marker", () => { if (_markers.SelectedItem is MarkerItem marker) _session!.Player.RemoveMarker(marker.Marker); RefreshMarkers(); }));
        panel.Children.Add(Button("Trim Selected Reel to Marked Range", () =>
        { if (_markers.SelectedItem is MarkerItem { Marker.Track: "Reel" } marker) { var range = Range(); _session!.Player.TrimReel(marker.Marker.Id, range.Start, range.End); RefreshMarkers(); } }));
        var reelOrder = new WrapPanel();
        reelOrder.Children.Add(Button("Reel ↑", () => { if (_markers.SelectedItem is MarkerItem { Marker.Track: "Reel" } marker) _session!.Player.MoveReel(marker.Marker.Id, -1); RefreshMarkers(); }));
        reelOrder.Children.Add(Button("Reel ↓", () => { if (_markers.SelectedItem is MarkerItem { Marker.Track: "Reel" } marker) _session!.Player.MoveReel(marker.Marker.Id, 1); RefreshMarkers(); }));
        panel.Children.Add(reelOrder);
        var filter = new TextBox { Watermark = "Filter event tracks (kills, damage, shots…)" };
        filter.TextChanged += (_, _) => { _events.ItemsSource = _session!.Player.Events.Where(e => string.IsNullOrWhiteSpace(filter.Text) || e.Type.Contains(filter.Text, StringComparison.OrdinalIgnoreCase)).Select(e => new EventItem(e)); };
        panel.Children.Add(filter); _events.Height = 190;
        _events.SelectionChanged += (_, _) => { if (_events.SelectedItem is EventItem marker) _session!.Player.Seek(marker.Event.Frame); };
        panel.Children.Add(_events);
        return new ScrollViewer { Content = panel };
    }
    private Control CombatPanel()
    {
        var panel = Panel();
        panel.Children.Add(Button("Inspect Current Shot Facts", InspectCombat));
        _shotSelection.SelectionChanged += (_,_) => { if(_shotSelection.SelectedItem is CombatItem item) { _session!.CombatSelection=item.Fact; ShowCombat(item.Fact); } };
        panel.Children.Add(_shotSelection);
        panel.Children.Add(Check("What shooter saw", _session!.WhatShooterSaw, value =>
        {
            _session.WhatShooterSaw = value;
            Persist();
            if (_session.CombatSelection is not { } fact) return;
            _session.PlayerSlot = fact.Shooter;
            _session.Camera = value ? StudioReplayCameraMode.Player : StudioReplayCameraMode.Free;
            if (!value) _session.Position = fact.Impact + new Vector3(0, 3, 8);
            _session.Player.Seek(value ? fact.FireFrame : fact.RecordingFrame);
        }));
        panel.Children.Add(Check("Show rays / accepted impact / rewind volumes", false, value => _session!.CombatRays = value));
        panel.Children.Add(_combat);
        panel.Children.Add(Button("Show Shooter POV", () => { if (_session!.CombatSelection is { } fact) { _session.PlayerSlot = fact.Shooter; _session.Camera = StudioReplayCameraMode.Player; _session.Player.Seek(fact.FireFrame); } }));
        panel.Children.Add(Button("Show Authority Resolution", () => { if (_session!.CombatSelection is { } fact) { _session.Camera = StudioReplayCameraMode.Free; _session.Position = fact.Impact + new Vector3(0, 3, 8); _session.Player.Seek(fact.RecordingFrame); } }));
        panel.Children.Add(Button("Player Analytics", () => _combat.Text = string.Join("\n", _session!.Player.Analytics().Select(p => $"{p.Name}: {p.Kills} kills · {p.Deaths} deaths · {p.Damage} damage"))));
        return new ScrollViewer { Content = panel };
    }
    private Control AnalysisPanel()
    {
        var panel = Panel(); var heatmap = new ReplayHeatmapView();
        var kind = new ComboBox { ItemsSource = new[] { "Movement", "Deaths", "Kills", "Damage", "Weapons", "Engagements", "Spawns", "Spawn pressure", "Objectives", "Objective presence", "Imperialist sightlines", "Pickups", "Pickup routes" }, SelectedIndex = 0 };
        var actor = new NumericUpDown { Minimum = -1, Maximum = 7, Value = -1 };
        var rangeOnly = new CheckBox { Content = "Filter to marked range", IsChecked = false };
        var world = new CheckBox { Content = "Show 3D world overlay", IsChecked = false };
        void Filter()
        {
            var status = _session!.Player.Status;
            uint start = rangeOnly.IsChecked == true ? status.ClipIn ?? 0 : 0, end = rangeOnly.IsChecked == true ? status.ClipOut ?? status.DurationFrames : status.DurationFrames;
            int slot = (int)(actor.Value ?? -1); string type = (string)kind.SelectedItem!;
            var filtered = _analysis?.Samples.Where(s => s.Kind == type && (slot < 0 || s.Slot == slot) && s.Frame >= start && s.Frame <= end).ToArray() ?? [];
            heatmap.Samples = filtered; heatmap.InvalidateVisual();
            _session.Player.SetHeatmapOverlay(world.IsChecked == true ? filtered.OrderByDescending(s => s.Weight).Take(64) : []);
        }
        kind.SelectionChanged += (_, _) => Filter(); actor.ValueChanged += (_, _) => Filter(); rangeOnly.IsCheckedChanged += (_, _) => Filter(); world.IsCheckedChanged += (_, _) => Filter();
        panel.Children.Add(Button("Analyze Recorded Movement / Combat", async () => { _analysis = await _session!.Player.AnalyzeAsync(); Filter(); }));
        panel.Children.Add(kind); panel.Children.Add(new TextBlock { Text = "Player filter (-1 = all slots)" }); panel.Children.Add(actor); panel.Children.Add(rangeOnly); panel.Children.Add(world); panel.Children.Add(heatmap);
        panel.Children.Add(new TextBlock { Text = "Spawn pressure counts damage/death within three seconds of a recorded spawn. Objective presence samples recorded actor centers in map volumes or as carriers/Prime. Sightlines include zoomed Imperialist poses. Pickup routes follow recorded pickups and resource gains for ten seconds.", TextWrapping = TextWrapping.Wrap });
        var preview = new ContentControl { MinHeight = 200 };
        panel.Children.Add(Button("Open Replay B…", async () =>
        {
            var top = TopLevel.GetTopLevel(this); if (top == null) return;
            var files = await top.StorageProvider.OpenFilePickerAsync(new() { Title = "Compare Replay", AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Project Prime Replay") { Patterns = ["*.ppdemo", "*.ppclip"] }] });
            try
            {
                if (files.FirstOrDefault()?.TryGetLocalPath() is { } path)
                {
                    await StudioReplayPlayer.ValidateSourceAsync(path);
                    if (_session!.ComparisonPlayer is { } previous) _comparisonViewport?.ReleaseGraphicsSession(previous);
                    _comparisonViewport?.Dispose(); _session!.LoadComparison(path);
                    _comparisonViewport = new ReplayViewportHost(_session.ComparisonView()) { Height = 200, ViewFactory = _session.View };
                    preview.Content = _comparisonViewport;
                }
            }
            finally { foreach (var file in files) file.Dispose(); }
        }));
        panel.Children.Add(preview);
        var comparison = new TextBlock { TextWrapping = TextWrapping.Wrap };
        string Description(StudioReplayEvidenceComparison result) => $"Frame {result.World.Frame} · {(result.SameSource ? "same recording" : "different recordings")}\nBuild A {result.BuildA}\nBuild B {result.BuildB}\n"
            + $"Gameplay: {result.World.GameplayEqual}\nPresentation: {result.World.PresentationEqual}\nFull world graph: {result.World.FullGraphEqual}\nProjectiles: {result.ProjectilesEqual}\nAnimation: {result.AnimationsEqual}\nCamera: {result.CameraEqual}\nResolved shots: {result.ResolvedShotsEqual}\nCapture: {(result.RenderCapturesEqual is { } equal ? equal.ToString() : "unavailable")}\n"
            + string.Join("\n", result.World.Players.Select(p => $"P{p.Slot + 1}: position Δ {p.Distance:0.000000}"));
        StudioReplayEvidence CurrentEvidence(StudioReplayPlayer player,ReplayViewportHost? viewport)
        {
            var view = _session!.View(512,288) with { PresentationFrame = player.Status.Frame, PresentationAlpha = 1 };
            StudioReplayCapture? capture = null;
            if(viewport != null)
            {
                var original = viewport.ViewFactory;
                try { viewport.ViewFactory = (width,height)=>view with { Width=width,Height=height }; capture=viewport.Capture(512,288); }
                finally { viewport.ViewFactory=original; }
            }
            return player.Evidence(view,capture);
        }
        panel.Children.Add(Button("Compare Current Simulation Frame", () =>
        {
            _session!.CompareCurrentFrame(); // Reject while either owner is preparing or at a different frame.
            comparison.Text = Description(StudioReplayPlayer.CompareEvidence(CurrentEvidence(_session.Player,_viewports.FirstOrDefault()),CurrentEvidence(_session.ComparisonPlayer!,_comparisonViewport)));
            var first = _session.Player.Performance; var second = _session.ComparisonPlayer!.Performance;
            comparison.Text += $"\nSeek A: {first.CheckpointSource}, restore {first.SeekRestoreFrame}, {first.SeekSimulationSteps} steps\nSeek B: {second.CheckpointSource}, restore {second.SeekRestoreFrame}, {second.SeekSimulationSteps} steps\n"
                + $"Cache A: {_session.Player.Status.CheckpointCount} / {_session.Player.Status.CheckpointBytes:N0} bytes\nCache B: {_session.ComparisonPlayer.Status.CheckpointCount} / {_session.ComparisonPlayer.Status.CheckpointBytes:N0} bytes";
        }));
        panel.Children.Add(Button("Save Current Build Comparison Report…",async () =>
        {
            var top = TopLevel.GetTopLevel(this); if(top == null)return;
            using var file = await top.StorageProvider.SaveFilePickerAsync(new() { Title="Save Replay Build Report",SuggestedFileName="replay-build.json",DefaultExtension="json",ShowOverwritePrompt=true,
                FileTypeChoices=[new FilePickerFileType("Replay build report") { Patterns=["*.json"] }] });
            if(file?.TryGetLocalPath() is { } path)await StudioReplayPlayer.SaveEvidenceAsync(CurrentEvidence(_session!.Player,_viewports.FirstOrDefault()),path);
        }));
        panel.Children.Add(Button("Compare Old / New Build Report…",async () =>
        {
            var top = TopLevel.GetTopLevel(this); if(top == null)return;
            var files = await top.StorageProvider.OpenFilePickerAsync(new() { Title="Compare Replay Build Report",AllowMultiple=false,FileTypeFilter=[new FilePickerFileType("Replay build report") { Patterns=["*.json"] }] });
            try
            {
                if(files.FirstOrDefault()?.TryGetLocalPath() is not { } path)return;
                var saved = await StudioReplayPlayer.LoadEvidenceAsync(path);
                if(saved.World.Frame != _session!.Player.Status.Frame)throw new InvalidOperationException($"Seek to report frame {saved.World.Frame} before comparing.");
                comparison.Text = Description(StudioReplayPlayer.CompareEvidence(saved,CurrentEvidence(_session.Player,_viewports.FirstOrDefault())));
            }
            finally { foreach(var file in files)file.Dispose(); }
        }));
        panel.Children.Add(comparison); return new ScrollViewer { Content = panel };
    }
    private Control ExportPanel()
    {
        var panel = Panel();
        var fps = new ComboBox { ItemsSource = new[] { 24, 30, 48, 60, 90, 120, 144 }, SelectedItem = 60 };
        var resolution = new ComboBox { ItemsSource = new[] { "1280 × 720", "1920 × 1080", "2560 × 1440", "3840 × 2160" }, SelectedIndex = 1 };
        var encoder = new TextBox { Text = "ffmpeg", Watermark = "Encoder executable (blank for PNG frames)" };
        var offlineAudio = new CheckBox { Content = "Mix offline replay audio", IsChecked = true };
        var gameVolume = new Slider { Minimum = 0, Maximum = 2, Value = 1 };
        var combatVolume = new Slider { Minimum = 0, Maximum = 2, Value = 1 };
        var replayVolume = new Slider { Minimum = 0, Maximum = 2, Value = 1 };
        var musicVolume = new Slider { Minimum = 0, Maximum = 2, Value = .5 };
        var music = new TextBox { Watermark = "Optional music PCM .wav" };
        panel.Children.Add(new TextBlock { Text = "Output rate" }); panel.Children.Add(fps); panel.Children.Add(resolution); panel.Children.Add(encoder);
        panel.Children.Add(offlineAudio);
        panel.Children.Add(new TextBlock { Text = "Game volume" }); panel.Children.Add(gameVolume);
        panel.Children.Add(new TextBlock { Text = "Combat feedback volume" }); panel.Children.Add(combatVolume);
        panel.Children.Add(new TextBlock { Text = "Replay cues volume" }); panel.Children.Add(replayVolume);
        panel.Children.Add(new TextBlock { Text = "Music volume" }); panel.Children.Add(musicVolume); panel.Children.Add(music);
        panel.Children.Add(Button("Choose Music WAV…", async () =>
        {
            var top = TopLevel.GetTopLevel(this); if (top == null) return;
            var files = await top.StorageProvider.OpenFilePickerAsync(new() { Title = "Offline Music", AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("PCM Wave") { Patterns = ["*.wav"] }] });
            try { music.Text = files.FirstOrDefault()?.TryGetLocalPath(); } finally { foreach (var file in files) file.Dispose(); }
        }));
        panel.Children.Add(Button("Export Selection", () =>
        {
            var range = Range(); (int w, int h) = resolution.SelectedIndex switch { 0 => (1280, 720), 2 => (2560, 1440), 3 => (3840, 2160), _ => (1920, 1080) };
            _session!.Player.QueueExport(new(Path.Combine(_session.ExportDirectory, Guid.NewGuid().ToString("N")), range.Start, range.End, w, h,
                (int)fps.SelectedItem!, encoder.Text, Camera: _session.Camera, GameHud: _session.GameHud, ReplayOverlay: _session.ReplayOverlay,
                Audio: new(offlineAudio.IsChecked == true, new((float)gameVolume.Value, (float)combatVolume.Value, (float)replayVolume.Value, (float)musicVolume.Value), music.Text),
                View: _session.View(w, h)));
        }));
        panel.Children.Add(Button("Extract Immutable Clip…", async () =>
        {
            var top = TopLevel.GetTopLevel(this); if (top == null) return;
            using var file = await top.StorageProvider.SaveFilePickerAsync(new() { Title = "Extract Replay Clip", SuggestedFileName = "clip.ppdemo", DefaultExtension = "ppdemo", ShowOverwritePrompt = true,
                FileTypeChoices = [new FilePickerFileType("Project Prime Replay") { Patterns = ["*.ppdemo"] }] });
            string? path = file?.TryGetLocalPath(); if (path == null) return;
            var range = Range(); await _session!.Player.ExtractClipAsync(range.Start, range.End, path);
        }));
        _exports.Height = 240; panel.Children.Add(_exports);
        panel.Children.Add(Button("Cancel Selected Export", () => { if (_exports.SelectedItem is ExportItem export) _session!.Player.CancelExport(export.Job.Id); }));
        panel.Children.Add(Button("Export Portable Replay Bundle…", async () =>
        {
            if (await ChooseBundlePath("Portable Replay Bundle", "replay.ppreplay.zip") is { } path)
                await _session!.Player.ExportPortableAsync(path);
        }));
        panel.Children.Add(Button("Export Diagnostic Bundle…", async () =>
        {
            if (await ChooseBundlePath("Replay Diagnostic Bundle", "replay-diagnostics.zip") is { } path)
                await _session!.Player.ExportDiagnosticBundleAsync(path, ProjectPrime.Studio.Rendering.StudioGraphicsHost.Backend ?? "unavailable");
        }));
        panel.Children.Add(new TextBlock { Text = "Exports run in a separate Studio worker with its own passive player, native surface and offline PCM mix. Closing this document leaves the worker running; explicit Cancel stops that export.", TextWrapping = TextWrapping.Wrap });
        return new ScrollViewer { Content = panel };
    }
    private async Task<string?> ChooseBundlePath(string title, string name)
    {
        var top = TopLevel.GetTopLevel(this); if (top == null) return null;
        using var file = await top.StorageProvider.SaveFilePickerAsync(new() { Title = title, SuggestedFileName = name,
            DefaultExtension = "zip", ShowOverwritePrompt = true, FileTypeChoices = [new FilePickerFileType("ZIP bundle") { Patterns = ["*.zip"] }] });
        return file?.TryGetLocalPath();
    }
    private void SetViews(bool multiple)
    {
        foreach (var viewport in _viewports) viewport.Dispose(); _viewports.Clear(); _viewGrid.Children.Clear();
        _viewGrid.RowDefinitions.Clear(); _viewGrid.ColumnDefinitions.Clear();
        _viewGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star)); _viewGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        if (multiple) { _viewGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star)); _viewGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star)); }
        for (int i = 0; i < (multiple ? 4 : 1); i++)
        {
            int index = i;
            StudioReplayView View(int w, int h) => index switch
            {
                1 => _session!.View(w, h) with { Camera = StudioReplayCameraMode.Free, GameHud = false },
                2 => _session!.View(w, h) with { Camera = StudioReplayCameraMode.Player, PlayerSlot = _session.CombatSelection?.Target ?? Math.Min(7, _session.PlayerSlot + 1) },
                3 => _session!.View(w, h) with { Camera = StudioReplayCameraMode.Overview, GameHud = false },
                _ => _session!.View(w, h)
            };
            var viewport = new ReplayViewportHost(index == 0 ? _session! : _session!.SecondaryView(View)) { ViewFactory = View, AutomaticRenderingEnabled = false };
            Grid.SetRow(viewport, i / 2); Grid.SetColumn(viewport, i % 2); _viewGrid.Children.Add(viewport); _viewports.Add(viewport);
        }
    }
    private void ApplyCamera()
    {
        _session!.Position = ParseVector(_position.Text); Vector3 angles = ParseVector(_angles.Text) * (MathF.PI / 180);
        _session.Rotation = Quaternion.CreateFromYawPitchRoll(angles.X, angles.Y, angles.Z); _session.Fov = (float)(_fov.Value ?? 78); Persist();
    }
    private void Persist() => _session!.SavePresentation();
    private static Vector3 ParseVector(string? text)
    {
        string[] parts = (text ?? "").Split(',');
        if (parts.Length != 3) throw new FormatException("Enter three numbers separated by commas.");
        return new(float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture));
    }
    private static string VectorText(Vector3 value) => FormattableString.Invariant($"{value.X}, {value.Y}, {value.Z}");
    private static Vector3 Angles(Quaternion value) => new Vector3(
        MathF.Atan2(2*(value.W*value.Y+value.X*value.Z),1-2*(value.X*value.X+value.Y*value.Y)),
        MathF.Asin(Math.Clamp(2*(value.W*value.X-value.Y*value.Z),-1,1)),
        MathF.Atan2(2*(value.W*value.Z+value.X*value.Y),1-2*(value.X*value.X+value.Z*value.Z))) * (180/MathF.PI);
    private (uint Start, uint End) Range()
    {
        var status = _session!.Player.Status; uint start = status.ClipIn ?? 0, end = status.ClipOut ?? status.DurationFrames;
        if (start >= end) throw new InvalidOperationException("Mark an In and Out range with at least one frame."); return (start, end);
    }
    private void InspectCombat()
    {
        var facts = _session!.Player.CombatAt(_session.Player.Status.Frame);
        _shotSelection.ItemsSource = facts.Select(f=>new CombatItem(f)).ToArray(); _shotSelection.SelectedIndex=facts.Count==0?-1:0;
        _session.CombatSelection = facts.FirstOrDefault();
        if(facts.Count==0)_combat.Text="No resolved-shot facts at this frame. Select a damage/kill event and seek to its frame.";
        else ShowCombat(facts[0]);
    }
    private void ShowCombat(StudioReplayCombat fact)
    {
        string Pose(Vector3 value)=>fact.HasAuthoredPose?value.ToString():"unavailable";
        _combat.Text=$"Shot #{fact.ShotId} · Damage event #{fact.DamageEventId}\nShooter P{fact.Shooter + 1} → P{fact.Target + 1}\nRecording frame {fact.RecordingFrame} · Fire {fact.FireFrame}\nServer tick {fact.ServerTick} · Source {fact.SourceFrame}\n"
            + $"Muzzle {Pose(fact.Muzzle)}\nAim {Pose(fact.Aim)}\nProjectile {Pose(fact.Direction)}\nRewind {(fact.RewindFrame is { } rewind ? rewind.ToString("0.000") : "unavailable")} · Target {fact.RewindTarget?.ToString() ?? "unavailable"}\n"
            + $"Accepted impact {fact.Impact}\nDamage {fact.Damage} · Headshot {fact.Headshot} · Lethal {fact.Lethal}\nDamage direction {fact.DamageDirection?.ToString() ?? "unavailable"}\nSettlement {(fact.SettlementSeconds is { } seconds ? seconds.ToString("0.000")+" s" : "unavailable")}\n{fact.Explanation}";
    }
    private void Refresh()
    {
        if (_session == null) return; var status = _session.Player.Status;
        _status.Text = $"{status.State} · Frame {status.Frame:N0} / {status.DurationFrames:N0} · {status.Rate:0.##}× · {status.CheckpointCount} checkpoints ({status.CheckpointBytes / 1024:N0} KiB)";
        _error.Text = status.Error ?? _actionError; _error.IsVisible = _error.Text != null;
        if (_eventCount != _session.Player.Events.Count) { _eventCount = _session.Player.Events.Count; _events.ItemsSource = _session.Player.Events.Select(e => new EventItem(e)).ToArray(); }
        if (_keyCount != _session.Player.CameraKeys.Count) RefreshKeys();
        string signature = string.Join("|", _session.Player.Markers.Select(m => $"{m.Id}:{m.StartFrame}:{m.EndFrame}"));
        if (_markerSignature != signature) { _markerSignature = signature; RefreshMarkers(); }
        _exports.ItemsSource = _session.Player.Exports.Select(e => new ExportItem(e)).ToArray(); _timeline?.InvalidateVisual();
    }
    private void RefreshKeys() { _keyCount = _session!.Player.CameraKeys.Count; _keys.ItemsSource = _session.Player.CameraKeys.Select(k => new KeyItem(k)).ToArray(); }
    private void RefreshMarkers() => _markers.ItemsSource = _session!.Player.Markers.Select(m => new MarkerItem(m)).ToArray();
    private static StackPanel Panel() => new() { Spacing = 8, Margin = new Thickness(8) };
    private Button Button(string text, Action action)
    {
        var button = new Button { Content = text, Margin = new Thickness(3), HorizontalAlignment = HorizontalAlignment.Stretch };
        button.Click += (_, _) => { try { _actionError = null; action(); } catch (Exception ex) { _actionError = ex.Message; _error.Text = ex.Message; _error.IsVisible = true; } }; return button;
    }
    private Button Button(string text, Func<Task> action)
    {
        var button = new Button { Content = text, Margin = new Thickness(3), HorizontalAlignment = HorizontalAlignment.Stretch };
        button.Click += async (_, _) => { try { _actionError = null; await action(); } catch (Exception ex) { _actionError = ex.Message; _error.Text = ex.Message; _error.IsVisible = true; } }; return button;
    }
    private static CheckBox Check(string text, bool value, Action<bool> changed)
    { var box = new CheckBox { Content = text, IsChecked = value }; box.IsCheckedChanged += (_, _) => changed(box.IsChecked == true); return box; }
    private sealed record KeyItem(StudioReplayCameraKey Key) { public override string ToString() => $"Frame {Key.Frame} · FOV {Key.Fov:0.#} · {Key.Interpolation} / {Key.Ease}"; }
    private sealed record MarkerItem(StudioReplayMarker Marker) { public override string ToString() => $"{Marker.Track} · {Marker.StartFrame}–{Marker.EndFrame} · {Marker.Name}"; }
    private sealed record EventItem(StudioReplayEvent Event) { public override string ToString() => $"{Event.Frame} · {Event.Type} · P{Event.Actor + 1} → P{Event.Target + 1}"; }
    private sealed record CombatItem(StudioReplayCombat Fact) { public override string ToString()=>$"Shot #{Fact.ShotId}: P{Fact.Shooter+1} → P{Fact.Target+1}"; }
    private sealed record ExportItem(StudioReplayExportStatus Job) { public override string ToString() => $"{Job.State} · {Job.Frames}/{Job.TotalFrames}\n{Job.Error ?? Job.Directory}"; }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _refresh?.Stop(); _viewTimer.Stop(); _comparisonViewport?.Dispose(); foreach (var viewport in _viewports) viewport.Dispose(); _viewports.Clear();
    }
}
