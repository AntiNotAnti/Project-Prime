using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using MphRead.Mods.StudioReplay;

namespace ProjectPrime.Studio.Replay;

public sealed partial class ReplayStudioWorkspace
{
    private readonly CheckBox _offlineAudio = new() { Name = "ReplayAudioEnabled", Content = "Mix offline replay audio" };
    private readonly CheckBox _audioGameEvents = new() { Name = "ReplayAudioGameEvents", Content = "Canonical game event sounds" };
    private readonly CheckBox _audioCombatFeedback = new() { Name = "ReplayAudioCombatFeedback", Content = "Combat feedback sounds" };
    private readonly Slider _audioGameVolume = AudioVolume("ReplayAudioGameVolume");
    private readonly Slider _audioCombatVolume = AudioVolume("ReplayAudioCombatVolume");
    private readonly Slider _audioReplayVolume = AudioVolume("ReplayAudioReplayVolume");
    private readonly Slider _audioMusicVolume = AudioVolume("ReplayAudioMusicVolume");
    private readonly TextBox _audioMusic = new() { Name = "ReplayAudioMusicPath", PlaceholderText = "Optional music PCM .wav" };
    private readonly ComboBox _audioEvent = new() { Name = "ReplayAudioEvent", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _audioBus = new() { Name = "ReplayAudioBus", ItemsSource = Enum.GetValues<StudioAudioBus>(), SelectedItem = StudioAudioBus.Replay };
    private readonly TextBox _audioWave = new() { Name = "ReplayAudioWavePath", PlaceholderText = "Absolute PCM WAV path" };
    private readonly NumericUpDown _audioGain = new() { Name = "ReplayAudioGain", Minimum = 0, Maximum = 4, Value = 1, Increment = .1m };
    private readonly TextBox _audioValue = new() { Name = "ReplayAudioValue", PlaceholderText = "Optional event value (integer; blank matches any)" };
    private readonly TextBlock _audioValues = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ListBox _audioBindings = new() { Name = "ReplayAudioBindings", Height = 150 };
    private readonly TextBlock _audioBindingCount = new() { TextWrapping = TextWrapping.Wrap };
    private StudioReplayAudioOptions? _shownAudioSettings;
    private string _audioEventSignature = "";
    private string? _audioRecordedSourceHash;
    private readonly Dictionary<string, int[]> _audioRecordedValues = new(StringComparer.Ordinal);
    private bool _updatingAudioControls;

    private static Slider AudioVolume(string name) => new() { Name = name, Minimum = 0, Maximum = 2 };
    private Control AudioPanel()
    {
        var panel = Panel();
        _audioBindings.ItemTemplate = new FuncDataTemplate<AudioBindingItem>((item, _) => new TextBlock
        { Text = item?.ToString(), TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Stretch });
        _audioBindings.Styles.Add(new Style(selector => selector.OfType<ListBoxItem>())
        { Setters = { new Setter(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch) } });
        ScrollViewer.SetHorizontalScrollBarVisibility(_audioBindings, ScrollBarVisibility.Disabled);
        panel.Children.Add(_offlineAudio);
        panel.Children.Add(_audioGameEvents); panel.Children.Add(_audioCombatFeedback);
        foreach (var pair in new[] { ("Game volume", _audioGameVolume), ("Combat feedback volume", _audioCombatVolume),
            ("Replay cues volume", _audioReplayVolume), ("Music volume", _audioMusicVolume) })
        { panel.Children.Add(new TextBlock { Text = pair.Item1 }); panel.Children.Add(pair.Item2); }
        panel.Children.Add(_audioMusic);
        var chooseMusic = Button("Choose Music WAV…", async () =>
        {
            string? path = await ChooseAudioWaveAsync("Offline Music");
            if (path != null) { _audioMusic.Text = path; UpdateAudioPreferences(); }
        });
        chooseMusic.Name = "ReplayAudioChooseMusic"; panel.Children.Add(chooseMusic);
        panel.Children.Add(new TextBlock { Text = "Event sound cues", FontSize = 16 });
        panel.Children.Add(new TextBlock { Text = "Choose an event recorded in this replay and a mono/stereo PCM16 or float32 WAV. Replay cues use the Replay volume bus by default. An optional value narrows the match, such as a particular weapon.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(_audioEvent); panel.Children.Add(_audioWave);
        var chooseWave = Button("Choose Cue WAV…", async () => { if (await ChooseAudioWaveAsync("Replay Event Cue") is { } path) _audioWave.Text = path; });
        chooseWave.Name = "ReplayAudioChooseWave"; panel.Children.Add(chooseWave);
        panel.Children.Add(new TextBlock { Text = "Audio bus / cue gain" }); panel.Children.Add(_audioBus); panel.Children.Add(_audioGain); panel.Children.Add(_audioValue); panel.Children.Add(_audioValues);
        _audioEvent.SelectionChanged += (_, _) => RefreshAudioEventValues();
        var add = Button("Add Cue", async () => await PutAudioBindingAsync(AudioBindingFromControls())); add.Name = "ReplayAudioAdd"; panel.Children.Add(add);
        var update = Button("Update Selected Cue", async () =>
        {
            int index = (_audioBindings.SelectedItem as AudioBindingItem)?.Index ?? throw new InvalidOperationException("Select a cue to update.");
            await PutAudioBindingAsync(AudioBindingFromControls(), index);
        });
        update.Name = "ReplayAudioUpdate"; panel.Children.Add(update);
        panel.Children.Add(_audioBindingCount); panel.Children.Add(_audioBindings);
        var remove = Button("Remove Selected Cue", () =>
        {
            int index = (_audioBindings.SelectedItem as AudioBindingItem)?.Index ?? throw new InvalidOperationException("Select a cue to remove.");
            RemoveAudioBinding(index);
        });
        remove.Name = "ReplayAudioRemove"; panel.Children.Add(remove);
        _audioBindings.SelectionChanged += (_, _) =>
        {
            if (_audioBindings.SelectedItem is not AudioBindingItem item) return;
            var binding = item.Binding;
            _audioEvent.SelectedItem = binding.EventType; _audioWave.Text = binding.WaveFile;
            _audioBus.SelectedItem = binding.Bus; _audioGain.Value = (decimal)binding.Gain;
            _audioValue.Text = binding.Value?.ToString(CultureInfo.InvariantCulture) ?? "";
        };
        _audioBindings.KeyDown += (_, args) =>
        {
            if (args.Key != Avalonia.Input.Key.Delete || _audioBindings.SelectedItem is not AudioBindingItem item) return;
            try { RemoveAudioBinding(item.Index); args.Handled = true; }
            catch (Exception error) { AudioError(error); }
        };
        _offlineAudio.IsCheckedChanged += (_, _) => UpdateAudioPreferences();
        _audioGameEvents.IsCheckedChanged += (_, _) => UpdateAudioPreferences();
        _audioCombatFeedback.IsCheckedChanged += (_, _) => UpdateAudioPreferences();
        foreach (var volume in new[] { _audioGameVolume, _audioCombatVolume, _audioReplayVolume, _audioMusicVolume })
            volume.PropertyChanged += (_, args) => { if (args.Property == Slider.ValueProperty) UpdateAudioPreferences(); };
        _audioMusic.LostFocus += (_, _) => UpdateAudioPreferences();
        RefreshAudioControls();
        return panel;
    }
    private async Task<string?> ChooseAudioWaveAsync(string title)
    {
        var top = TopLevel.GetTopLevel(this); if (top == null) return null;
        var files = await top.StorageProvider.OpenFilePickerAsync(new() { Title = title, AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("PCM Wave") { Patterns = ["*.wav"] }] });
        try { return files.FirstOrDefault()?.TryGetLocalPath(); }
        finally { foreach (var file in files) file.Dispose(); }
    }
    private StudioAudioEventBinding AudioBindingFromControls()
    {
        int? value = null;
        if (!string.IsNullOrWhiteSpace(_audioValue.Text))
        {
            if (!int.TryParse(_audioValue.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                throw new ArgumentException("Event value must be an integer, or leave it blank to match any value.");
            value = parsed;
        }
        return new(_audioEvent.SelectedItem as string ?? throw new InvalidOperationException("Choose a recorded event type."),
            _audioWave.Text ?? "", (StudioAudioBus)(_audioBus.SelectedItem ?? StudioAudioBus.Replay), value, (float)(_audioGain.Value ?? 1));
    }
    /// <summary>The same bounded, cancellable validation and adoption used by the cue controls.</summary>
    public Task PutAudioBindingAsync(StudioAudioEventBinding binding, int? index = null)
    {
        if (_session == null) throw new InvalidOperationException("Open a replay before adding audio cues.");
        binding = ReplayStudioAudioSettings.ValidateBinding(binding);
        CacheAudioEvents();
        if (!_audioRecordedValues.ContainsKey(binding.EventType))
            throw new ArgumentException("Choose an event type recorded in this replay.");
        var previous = _session.AudioSettings;
        var list = (previous.Bindings ?? []).ToList();
        if (index is { } selected)
        { if ((uint)selected >= (uint)list.Count) throw new ArgumentOutOfRangeException(nameof(index)); list[selected] = binding; }
        else list.Add(binding);
        var proposed = ReplayStudioAudioSettings.Freeze(previous with { Bindings = list });
        return RunDocumentJobAsync("Validate replay audio cue", async token => await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            var pcm = StudioPcmAudio.ReadWave(binding.WaveFile);
            if (pcm.Frames == 0) throw new InvalidDataException("The cue WAV has no audio samples.");
            token.ThrowIfCancellationRequested();
        }, token), () =>
        {
            if (!ReferenceEquals(previous, _session.AudioSettings)) throw new InvalidOperationException("Audio settings changed during validation. Add the cue again.");
            _session.SetAudioSettings(proposed); RefreshAudioControls(); _audioBindings.SelectedIndex = index ?? list.Count - 1;
        });
    }
    public void RemoveAudioBinding(int index)
    {
        if (_disposed || !_acceptingJobs || _session == null) throw new InvalidOperationException("This replay is saving or closing.");
        var options = _session.AudioSettings; var list = (options.Bindings ?? []).ToList();
        if ((uint)index >= (uint)list.Count) throw new ArgumentOutOfRangeException(nameof(index));
        list.RemoveAt(index); _session.SetAudioSettings(options with { Bindings = list }); RefreshAudioControls();
    }
    private void UpdateAudioPreferences()
    {
        if (_updatingAudioControls || _disposed || _session == null) return;
        try
        {
            SnapshotAudioFromControls();
        }
        catch (Exception error) { AudioError(error); }
    }
    private StudioReplayAudioOptions SnapshotAudioFromControls()
    {
        if (_disposed || !_acceptingJobs || _session == null) throw new InvalidOperationException("This replay is saving or closing.");
        _session.SetAudioSettings(_session.AudioSettings with { Enabled = _offlineAudio.IsChecked == true,
            GameEvents = _audioGameEvents.IsChecked == true, CombatFeedback = _audioCombatFeedback.IsChecked == true,
            Volumes = new((float)_audioGameVolume.Value, (float)_audioCombatVolume.Value, (float)_audioReplayVolume.Value, (float)_audioMusicVolume.Value), MusicFile = _audioMusic.Text });
        return _session.SnapshotAudioOptions();
    }
    private void AudioError(Exception error) { _actionError = error.Message; _error.Text = error.Message; _error.IsVisible = true; }
    private void RefreshAudioControls()
    {
        if (_session == null) return;
        var options = _session.AudioSettings;
        bool eventsChanged = CacheAudioEvents();
        if (!eventsChanged && ReferenceEquals(options, _shownAudioSettings)) return;
        string[] types = _audioRecordedValues.Keys.Concat((options.Bindings ?? []).Select(item => item.EventType)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        string signature = string.Join("|", types);
        if (signature != _audioEventSignature)
        {
            string? selected = _audioEvent.SelectedItem as string;
            _audioEventSignature = signature; _audioEvent.ItemsSource = types;
            _audioEvent.SelectedItem = types.Contains(selected, StringComparer.Ordinal) ? selected : types.FirstOrDefault();
        }
        RefreshAudioEventValues();
        _shownAudioSettings = options; _updatingAudioControls = true;
        try
        {
            _offlineAudio.IsChecked = options.Enabled; _audioGameEvents.IsChecked = options.GameEvents; _audioCombatFeedback.IsChecked = options.CombatFeedback;
            var volumes = options.Volumes ?? new();
            _audioGameVolume.Value = volumes.Game; _audioCombatVolume.Value = volumes.Combat;
            _audioReplayVolume.Value = volumes.Replay; _audioMusicVolume.Value = volumes.Music; _audioMusic.Text = options.MusicFile;
            int selected = (_audioBindings.SelectedItem as AudioBindingItem)?.Index ?? -1;
            _audioBindings.ItemsSource = (options.Bindings ?? []).Select((binding, index) => new AudioBindingItem(index, binding)).ToArray();
            _audioBindings.SelectedIndex = Math.Min(selected, (options.Bindings?.Count ?? 0) - 1);
            _audioBindingCount.Text = $"{options.Bindings?.Count ?? 0} / 64 event cues";
        }
        finally { _updatingAudioControls = false; }
    }
    private void RefreshAudioEventValues()
    {
        int[] values = _audioEvent.SelectedItem is string type && _audioRecordedValues.TryGetValue(type, out var recorded) ? recorded : [];
        _audioValues.Text = values.Length == 0 ? "No matching recorded event values." : "Recorded values: "
            + string.Join(", ", values.Take(16)) + (values.Length > 16 ? "…" : "");
    }
    private bool CacheAudioEvents()
    {
        CacheRecordedEvents();
        string? hash = _recordedEventsSourceHash;
        if (hash == _audioRecordedSourceHash) return false;
        bool cleared = _audioRecordedSourceHash != null;
        if (cleared) { _audioRecordedValues.Clear(); _audioRecordedSourceHash = null; }
        if (hash == null) return cleared;
        foreach (var events in _recordedEvents.GroupBy(item => item.Type, StringComparer.Ordinal))
            _audioRecordedValues.Add(events.Key, events.Select(item => item.Value).Distinct().Order().ToArray());
        _audioRecordedSourceHash = hash;
        return true;
    }
    private sealed record AudioBindingItem(int Index, StudioAudioEventBinding Binding)
    {
        public override string ToString() => $"{Binding.EventType}{(Binding.Value is { } value ? " = " + value : " (any value)")} · {Binding.Bus} × {Binding.Gain:0.##}\n{Path.GetFileName(Binding.WaveFile)}";
    }
}
