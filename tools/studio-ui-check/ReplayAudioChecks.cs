using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using MphRead.Mods.StudioReplay;
using ProjectPrime.Studio;
using ProjectPrime.Studio.Jobs;
using ProjectPrime.Studio.Replay;
using ProjectPrime.Studio.Settings;

internal static partial class Program
{
    private static async Task<StudioReplayAudioOptions> CheckReplayAudioControlsAsync(StudioWindow window, ReplayStudioDocument document,
        StudioPaths paths, string output, string data)
    {
        var session = document.Session!; var player = session.Player; var host = document.Host;
        var original = session.SnapshotAudioOptions(); var world = player.Snapshot();
        var originalRange = player.Status;
        var inspector = host.GetVisualDescendants().OfType<TabControl>().Single(control =>
            control.Items.OfType<TabItem>().Any(tab => tab.Header?.ToString() == "Export"));
        inspector.SelectedItem = inspector.Items.OfType<TabItem>().Single(tab => tab.Header?.ToString() == "Export");
        PumpLayout(window);
        var recorded = player.Events.FirstOrDefault() ?? throw new InvalidOperationException("Audio UI fixture has no recorded replay events.");
        string wave = Path.Combine(data, "ui-event-cue.wav"), invalid = Path.Combine(data, "invalid-event-cue.wav");
        WriteAudioCueWave(wave); File.WriteAllText(invalid, "This is not PCM WAV.");
        T Field<T>(string name) where T : Control => host.GetVisualDescendants().OfType<T>().Distinct().Single(control => control.Name == name);
        void Action(string name) => Field<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var events = Field<ComboBox>("ReplayAudioEvent"); var list = Field<ListBox>("ReplayAudioBindings");
        var gain = Field<NumericUpDown>("ReplayAudioGain"); var value = Field<TextBox>("ReplayAudioValue");
        try
        {
            Check(events.Items.Cast<object>().Select(item => item.ToString()).Contains(recorded.Type),
                "audio cue event picker contains an actual canonical recorded event type");
            var recordedChoices = events.ItemsSource;
            var timelineEvents = (ListBox)host.GetType().GetField("_events", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(host)!;
            var timelineItems = timelineEvents.ItemsSource;
            events.SelectedItem = recorded.Type; Field<TextBox>("ReplayAudioWavePath").Text = wave;
            Field<ComboBox>("ReplayAudioBus").SelectedItem = StudioAudioBus.Replay;
            gain.Value = .75m; value.Text = recorded.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var jobs = window.Jobs.Jobs.Select(job => job.Id).ToHashSet(); Action("ReplayAudioAdd"); await WaitCueJobAsync(jobs);
            var binding = session.AudioSettings.Bindings!.Single();
            Check(binding == new StudioAudioEventBinding(recorded.Type, wave, StudioAudioBus.Replay, recorded.Value, .75f)
                && window.Jobs.Jobs.Last(job => job.Title == "Validate replay audio cue").State == StudioJobState.Completed,
                "actual Add Cue control validates owned PCM as a central job and adopts exact event/value/bus/gain");
            list.SelectedIndex = 0;
            Check(events.SelectedItem?.ToString() == recorded.Type && Field<TextBox>("ReplayAudioWavePath").Text == wave
                && gain.Value == .75m && value.Text == recorded.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "selecting the actual cue row restores its editable controls");
            gain.Value = .5m; value.Text = ""; jobs = window.Jobs.Jobs.Select(job => job.Id).ToHashSet(); Action("ReplayAudioUpdate"); await WaitCueJobAsync(jobs);
            binding = session.AudioSettings.Bindings!.Single();
            Check(binding.Gain == .5f && binding.Value is null && list.Items.Count == 1,
                "actual Update Selected Cue replaces one binding and blank event value matches any value");
            Check(ReferenceEquals(events.ItemsSource, recordedChoices),
                "repeated cue preference edits retain the cached choices for the same prepared recording identity");
            await Task.Delay(250); PumpLayout(window);
            Check(ReferenceEquals(timelineEvents.ItemsSource, timelineItems),
                "ordinary refresh ticks retain timeline event items from the same ready source snapshot");
            var eventFilter = (TextBox)host.GetType().GetField("_eventFilter", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(host)!;
            string? originalFilter = eventFilter.Text; eventFilter.Text = "WeaponFired";
            await Task.Delay(20); PumpLayout(window); // Avalonia publishes TextChanged on its dispatcher.
            var filteredItems = timelineEvents.ItemsSource; await Task.Delay(250); PumpLayout(window);
            Check(timelineEvents.Items.Count == player.Events.Count(item => item.Type == "WeaponFired")
                && ReferenceEquals(timelineEvents.ItemsSource, filteredItems),
                "actual timeline filter retains matching recorded events across ordinary refresh ticks: "
                + JsonSerializer.Serialize(new { Actual=timelineEvents.Items.Count, Expected=player.Events.Count(item=>item.Type=="WeaponFired"),
                    Stable=ReferenceEquals(timelineEvents.ItemsSource,filteredItems), eventFilter.Text }));
            eventFilter.Text = originalFilter;
            Field<CheckBox>("ReplayAudioGameEvents").IsChecked = false;
            Field<CheckBox>("ReplayAudioCombatFeedback").IsChecked = false;
            foreach (string bus in new[] { "Game", "Combat", "Music" }) Field<Slider>("ReplayAudio" + bus + "Volume").Value = 0;
            Field<Slider>("ReplayAudioReplayVolume").Value = .8;
            var saved = session.SnapshotAudioOptions();
            using (var restored = new ReplayStudioSession(document.Path!, paths))
            {
                await PrepareAudioSessionAsync(restored);
                Check(JsonSerializer.Serialize(restored.AudioSettings) == JsonSerializer.Serialize(saved),
                    "source-bound audio preferences persist and restore after canonical preparation of the same recording");
            }
            bool immutable = false;
            try { ((IList<StudioAudioEventBinding>)saved.Bindings!)[0] = binding with { Gain = 3 }; }
            catch (NotSupportedException) { immutable = true; }
            Check(immutable && saved.Bindings![0].Gain == .5f, "audio snapshots expose a read-only binding collection");
            await CaptureAudioControlsAsync();

            // Intercept only process admission: the real Export Selection action still
            // writes the canonical immutable ticket. Native worker PCM is checked separately.
            var launch = player.ExportWorkerLauncher; StudioReplayExportTicket? ticket = null;
            player.ExportWorkerLauncher = path =>
            {
                ticket = JsonSerializer.Deserialize<StudioReplayExportTicket>(File.ReadAllText(path), new JsonSerializerOptions { IncludeFields = true })!;
                File.WriteAllText(ticket.StatusFile, JsonSerializer.Serialize(new StudioReplayExportStatus(ticket.Id, "Cancelled", 0, 1, null, ticket.Request.Directory)));
                return Task.CompletedTask;
            };
            try
            {
                player.SetRange(recorded.Frame, Math.Min(recorded.Frame + 2, player.Status.DurationFrames)); player.Advance(TimeSpan.Zero);
                Field<ComboBox>("ReplayExportResolution").SelectedIndex = 0;
                Field<ComboBox>("ReplayExportFps").SelectedItem = 60;
                Field<TextBox>("ReplayExportEncoder").Text = ""; Action("ReplayExportSelection");
                Check(ticket is not null && ticket.Request.Width == 1280 && ticket.Request.Height == 720
                    && ticket.Request.Audio!.Bindings!.Single() == binding && ticket.Request.Audio.Volumes!.Replay == .8f
                    && !ticket.Request.Audio.GameEvents && !ticket.Request.Audio.CombatFeedback,
                    "actual Export Selection control freezes validated cue settings in a canonical persisted ticket before worker admission");
                list.SelectedIndex = 0; Action("ReplayAudioRemove"); Field<Slider>("ReplayAudioReplayVolume").Value = 0;
                Check(session.AudioSettings.Bindings!.Count == 0 && ticket!.Request.Audio!.Bindings!.Single() == binding
                    && ticket.Request.Audio.Volumes!.Replay == .8f,
                    "removing a cue and muting the editor cannot alter an already queued export audio snapshot");
            }
            finally { player.ExportWorkerLauncher = launch; }

            Field<TextBox>("ReplayAudioWavePath").Text = invalid; jobs = window.Jobs.Jobs.Select(job => job.Id).ToHashSet(); Action("ReplayAudioAdd"); await WaitCueJobAsync(jobs);
            Check(session.AudioSettings.Bindings!.Count == 0 && window.Jobs.Jobs.Last(job => job.Title == "Validate replay audio cue").State == StudioJobState.Failed
                && host.GetVisualDescendants().OfType<TextBlock>().Any(text => text.IsEffectivelyVisible && text.Text?.Contains("RIFF WAV", StringComparison.Ordinal) == true),
                "malformed PCM cue fails its real central validation job, remains unadopted and shows an actionable error");
            Field<TextBox>("ReplayAudioWavePath").Text = wave; bool canceled = false;
            void CancelCue() { var job = window.Jobs.Jobs.LastOrDefault(job => job.Title == "Validate replay audio cue" && job.State == StudioJobState.Running); if (job is not null) job.Cancel(); }
            window.Jobs.Changed += CancelCue;
            try { await host.PutAudioBindingAsync(binding); } catch (OperationCanceledException) { canceled = true; }
            finally { window.Jobs.Changed -= CancelCue; }
            Check(canceled && session.AudioSettings.Bindings!.Count == 0 && !host.HasPendingJobs
                && window.Jobs.Jobs.Last(job => job.Title == "Validate replay audio cue").State == StudioJobState.Cancelled,
                "central cue cancellation drains real PCM validation and prevents stale settings adoption");
            session.SetAudioSettings(saved); string presentation = AudioPresentationPath(document.Path!, paths);
            string validPreferences = File.ReadAllText(presentation);
            var json = System.Text.Json.Nodes.JsonNode.Parse(validPreferences)!; json["AudioSourceHash"] = new string('0', 64);
            File.WriteAllText(presentation, json.ToJsonString());
            try
            {
                using var mismatched = new ReplayStudioSession(document.Path!, paths); await PrepareAudioSessionAsync(mismatched);
                Check(mismatched.AudioSettings.Bindings!.Count == 0,
                    "audio preferences bound to another recording hash are not adopted even at the same logical path");
            }
            finally { File.WriteAllText(presentation, validPreferences); }
            var after = player.Snapshot();
            Check(after.GameplayHash == world.GameplayHash && after.PresentationHash == world.PresentationHash && after.FullGraphHash == world.FullGraphHash,
                "actual cue editing, validation, queue snapshots and source-bound restoration preserve the entire canonical replay world");
            return saved;
        }
        finally
        {
            session.SetAudioSettings(original);
            player.SetRange(originalRange.ClipIn ?? 0, originalRange.ClipOut ?? originalRange.DurationFrames); player.Advance(TimeSpan.Zero);
        }

        async Task WaitCueJobAsync(HashSet<Guid> before)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while ((!window.Jobs.Jobs.Any(job => !before.Contains(job.Id)) || host.HasPendingJobs) && timer.Elapsed < TimeSpan.FromSeconds(10)) { await Task.Delay(1); PumpLayout(window); }
            Check(window.Jobs.Jobs.Any(job => !before.Contains(job.Id)) && !host.HasPendingJobs, "actual audio control validation settles its tracked central job");
        }
        async Task CaptureAudioControlsAsync()
        {
            foreach ((int width, int height, double scale) in new[] { (1280, 800, 1d), (1920, 1080, 1d), (1280, 800, 2d) })
            {
                window.Width = width; window.Height = height; window.SetRenderScaling(scale); PumpLayout(window);
                list.BringIntoView(); await Task.Delay(20); PumpLayout(window);
                CheckControlBounds(window, list, 100, 40, "actual event cue list remains readable inside the scrollable Export panel");
                var row = list.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text?.Contains("ui-event-cue.wav", StringComparison.Ordinal) == true);
                Check(row.TextWrapping == Avalonia.Media.TextWrapping.Wrap && row.Text!.Contains("Replay × 0.5", StringComparison.Ordinal)
                    && row.Bounds.Width <= list.Bounds.Width && row.Bounds.Height >= 20,
                    "selected cue row wraps the complete bus/gain and WAV name within the compact list width");
                CheckControlBounds(window, row, 50, 20, "complete wrapped cue row stays inside its allocated scroll viewport");
                using var image = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Audio cue controls did not render.");
                CheckImageContent(image, "audio cues"); string file = $"replay-studio-audio-cues-{width}x{height}" + (scale == 1 ? "" : "-2x") + ".png";
                image.Save(Path.Combine(output, file), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                Captures.Add(new { route = "replay-studio-audio-cues", file, width, height, scale, viewportBackend = "Headless Avalonia controls; native worker PCM is a separate acceptance gate" });
            }
        }
    }
    private static string AudioPresentationPath(string source, StudioPaths paths) => Path.Combine(paths.UserDataDirectory, "replay-presentation",
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(source)))).ToLowerInvariant() + ".json");
    private static async Task PrepareAudioSessionAsync(ReplayStudioSession session)
    {
        session.Player.OnGraphicsInitialize(256, 192); var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (!session.Player.Status.Ready && deadline.Elapsed < TimeSpan.FromSeconds(30))
        { session.Player.Advance(TimeSpan.Zero); if (session.Player.Status.State == "Error") throw new InvalidOperationException(session.Player.Status.Error); await Task.Delay(1); }
        Check(session.Player.Status.Ready, "audio preference restoration waits for a real prepared canonical source identity");
    }
    private static void WriteAudioCueWave(string path)
    {
        const int rate = 48000, frames = rate;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8); writer.Write(36 + frames * 4); writer.Write("WAVEfmt "u8); writer.Write(16); writer.Write((short)1); writer.Write((short)2);
        writer.Write(rate); writer.Write(rate * 4); writer.Write((short)4); writer.Write((short)16); writer.Write("data"u8); writer.Write(frames * 4);
        for (int frame = 0; frame < frames; frame++) { short sample = (short)(Math.Sin(frame * 2 * Math.PI * 440 / rate) * 8192); writer.Write(sample); writer.Write(sample); }
    }
}
