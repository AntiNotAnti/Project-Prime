using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
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
    private sealed record NativeAudioCueCommand(string Wave, bool Muted, string CaptureDirectory);
    private sealed record NativeAudioCueReport(Guid Id, string TicketPath, StudioReplayExportRequest Request,
        StudioAudioEventBinding Adopted, StudioJobState ValidationState, bool EditorChangedAfterQueue,
        string GameplayHash, string PresentationHash);

    private static async Task<bool> TryHandleNativeAudioCommandAsync(StudioWindow window, StudioPaths paths, string command)
    {
        const string prefix = "replay-audio-queue ";
        if (!command.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var request = JsonSerializer.Deserialize<NativeAudioCueCommand>(command[prefix.Length..])!;
        var document = (ReplayStudioDocument)window.Documents.ActiveDocument!;
        var session = document.Session!; var host = document.Host; var player = session.Player;
        var before = player.Snapshot();
        var recorded = player.Events.Single(item => item.Type == "WeaponFired" && item.Frame == 2 && item.Value == 7);
        var inspector = host.GetVisualDescendants().OfType<TabControl>().Single(control =>
            control.Items.OfType<TabItem>().Any(tab => tab.Header?.ToString() == "Export"));
        inspector.SelectedItem = inspector.Items.OfType<TabItem>().Single(tab => tab.Header?.ToString() == "Export");
        window.UpdateLayout();
        T Field<T>(string name) where T : Control => host.GetVisualDescendants().OfType<T>().Distinct().Single(control => control.Name == name);
        void Action(string name) => Field<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Field<CheckBox>("ReplayAudioEnabled").IsChecked = true;
        Field<CheckBox>("ReplayAudioGameEvents").IsChecked = false;
        Field<CheckBox>("ReplayAudioCombatFeedback").IsChecked = false;
        foreach (string bus in new[] { "Game", "Combat", "Music" }) Field<Slider>("ReplayAudio" + bus + "Volume").Value = 0;
        Field<Slider>("ReplayAudioReplayVolume").Value = request.Muted ? 0 : 1;
        Field<TextBox>("ReplayAudioMusicPath").Text = "";
        Field<ComboBox>("ReplayAudioEvent").SelectedItem = recorded.Type;
        Field<TextBox>("ReplayAudioWavePath").Text = request.Wave;
        Field<ComboBox>("ReplayAudioBus").SelectedItem = StudioAudioBus.Replay;
        Field<NumericUpDown>("ReplayAudioGain").Value = 1;
        Field<TextBox>("ReplayAudioValue").Text = "7";
        var list = Field<ListBox>("ReplayAudioBindings");
        while (session.AudioSettings.Bindings!.Count != 0) { list.SelectedIndex = 0; Action("ReplayAudioRemove"); }
        int jobCount = window.Jobs.Jobs.Count;
        Action("ReplayAudioAdd");
        var timer = Stopwatch.StartNew();
        while ((window.Jobs.Jobs.Count <= jobCount || host.HasPendingJobs) && timer.Elapsed < TimeSpan.FromSeconds(15)) await Task.Delay(1);
        var binding = session.AudioSettings.Bindings?.SingleOrDefault()
            ?? throw new InvalidOperationException("Actual audio cue Add control did not adopt a validated binding. "
                + JsonSerializer.Serialize(window.Jobs.Jobs.Where(job => job.Title == "Validate replay audio cue").Select(job => new { job.State, job.Error })));
        var validation = window.Jobs.Jobs.Last(job => job.Title == "Validate replay audio cue");
        if (!request.Muted)
        {
            window.Width=1920; window.Height=1080; await Task.Delay(100); window.UpdateLayout();
            list.SelectedIndex=0;
            var scroll=Field<ComboBox>("ReplayAudioEvent").GetVisualAncestors().OfType<ScrollViewer>().First();
            async Task CaptureControlsAsync(string name, string first, string[] expected)
            {
                var control=Field<Control>(first); var content=(Control)scroll.Content!;
                var origin=control.TranslatePoint(default,content)??throw new InvalidOperationException("Audio controls have no actual scroll origin.");
                scroll.Offset=new Avalonia.Vector(0,Math.Max(0,origin.Y-8)); await Task.Delay(50); window.UpdateLayout();
                foreach(string field in expected)
                {
                    var target=Field<Control>(field);var position=target.TranslatePoint(default,scroll);
                    if(position is not {} point || !target.IsEffectivelyVisible || point.Y<0
                        || point.Y+target.Bounds.Height>scroll.Viewport.Height+2)
                        throw new InvalidOperationException("Native audio capture clips required control: "+field);
                }
                double scale=window.RenderScaling;
                using var bitmap=new Avalonia.Media.Imaging.RenderTargetBitmap(new((int)Math.Round(window.ClientSize.Width*scale),(int)Math.Round(window.ClientSize.Height*scale)),new Avalonia.Vector(96*scale,96*scale));
                bitmap.Render(window);bitmap.Save(Path.Combine(request.CaptureDirectory,name+".png"),new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            }
            await CaptureControlsAsync("native-export-audio-volumes","ReplayAudioEnabled",
                ["ReplayAudioEnabled","ReplayAudioGameEvents","ReplayAudioCombatFeedback","ReplayAudioGameVolume","ReplayAudioCombatVolume","ReplayAudioReplayVolume","ReplayAudioMusicVolume"]);
            await CaptureControlsAsync("native-export-event-cue","ReplayAudioEvent",
                ["ReplayAudioEvent","ReplayAudioWavePath","ReplayAudioBus","ReplayAudioGain","ReplayAudioValue","ReplayAudioAdd","ReplayAudioUpdate"]);
            await CaptureControlsAsync("native-export-selected-cue","ReplayAudioBindings",
                ["ReplayAudioBindings","ReplayAudioRemove"]);
            File.WriteAllText(Path.Combine(request.CaptureDirectory,"native-export-controls-capture-scope.json"),JsonSerializer.Serialize(new
            {Scope="Rendered Avalonia pixels of actual native hosted Export controls at measured window size/scaling; native viewport and OS composite pixels are separate gates.",
                window.ClientSize,window.RenderScaling,SelectedEvent=recorded,SelectedBinding=binding},new JsonSerializerOptions{WriteIndented=true}));
        }
        player.SetRange(recorded.Frame, recorded.Frame + 2); player.Advance(TimeSpan.Zero);
        Field<ComboBox>("ReplayExportResolution").SelectedIndex = 0;
        Field<ComboBox>("ReplayExportFps").SelectedItem = 60;
        Field<TextBox>("ReplayExportEncoder").Text = "";
        var previousExports = player.Exports.Select(job => job.Id).ToHashSet();
        Action("ReplayExportSelection");
        Guid id = player.Exports.Single(job => !previousExports.Contains(job.Id)).Id;
        string ticketPath = Path.Combine(paths.BuildCacheDirectory, "replay", "exports", id.ToString("N"), "ticket.json");
        var ticket = JsonSerializer.Deserialize<StudioReplayExportTicket>(File.ReadAllText(ticketPath), new JsonSerializerOptions { IncludeFields = true })!;
        list.SelectedIndex = 0; Action("ReplayAudioRemove");
        Field<Slider>("ReplayAudioReplayVolume").Value = request.Muted ? 1 : 0;
        var after = player.Snapshot();
        if (before.GameplayHash != after.GameplayHash || before.PresentationHash != after.PresentationHash)
            throw new InvalidOperationException("Actual audio controls changed canonical playback state.");
        Console.WriteLine("REPLAY-AUDIO-QUEUED " + JsonSerializer.Serialize(new NativeAudioCueReport(id, ticketPath,
            ticket.Request, binding, validation.State, session.AudioSettings.Bindings!.Count == 0
                && session.AudioSettings.Volumes!.Replay != ticket.Request.Audio!.Volumes!.Replay,
            after.GameplayHash, after.PresentationHash), new JsonSerializerOptions { IncludeFields = true }));
        Console.Out.Flush(); return true;
    }

    private static async Task CheckNativeReplayAudioAsync(string directory, string assets, string fixture, string output)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(output);
        string source = Path.Combine(directory, "native-event-cues.ppdemo"), wave = Path.Combine(output, "event-cue-source.wav");
        byte[] original = File.ReadAllBytes(fixture); ReplayAudioFixture.Create(fixture, source); WriteTestWave(wave);
        byte[] authored = File.ReadAllBytes(source);
        string profile = Path.Combine(directory, "native-audio-profile"); Directory.CreateDirectory(profile);
        new StudioSettingsStore(new(AppContext.BaseDirectory, profile)).SaveSettings(new() { GamePathsFile = Path.GetFullPath(assets) });
        File.WriteAllText(Path.Combine(output, "native-replay-audio-input-identity.json"), JsonSerializer.Serialize(new
        {
            Scope="Actual native Studio Add Cue and Export Selection controls, independent renderer workers and offline Replay bus PCM; owned metadata event fixture.",
            InputPath=Path.GetFullPath(fixture), InputSha256=Hash(original), AuthoredSha256=Hash(authored), CueWaveSha256=Hash(File.ReadAllBytes(wave)),
            EngineSha256=Hash(File.ReadAllBytes(typeof(StudioReplayPlayer).Assembly.Location)),
            StudioSha256=Hash(File.ReadAllBytes(typeof(StudioWindow).Assembly.Location)),
            HarnessSha256=Hash(File.ReadAllBytes(typeof(Program).Assembly.Location)), CapturedUtc=DateTime.UtcNow
        }, new JsonSerializerOptions { WriteIndented=true }));
        using Process studio = await StartNativeProbeAsync(profile, ["--replay", source]);
        var queued = new List<Guid>();
        try
        {
            await WaitForNativeReplayAsync(studio, 1);
            foreach (bool muted in new[] { false, true })
            {
                string name = muted ? "muted" : "audible";
                var report = JsonSerializer.Deserialize<NativeAudioCueReport>((await NativeCommandAsync(studio,
                    "replay-audio-queue " + JsonSerializer.Serialize(new NativeAudioCueCommand(wave, muted, output)), "REPLAY-AUDIO-QUEUED "))[20..],
                    new JsonSerializerOptions { IncludeFields=true })!;
                queued.Add(report.Id);
                var request = report.Request; var audio = request.Audio!;
                Check(report.ValidationState == StudioJobState.Completed && report.Adopted == new StudioAudioEventBinding("WeaponFired", wave, StudioAudioBus.Replay, 7, 1),
                    "actual native Add Cue control validates recorded event PCM through observable central job");
                Check(request.Width == 1280 && request.Height == 720 && request.Fps == 60 && request.StartFrame == 2 && request.EndFrame == 4
                    && audio.Enabled && !audio.GameEvents && !audio.CombatFeedback && audio.MusicFile is null
                    && audio.Bindings!.Single() == report.Adopted && audio.Volumes == new StudioAudioVolumes(0, 0, muted ? 0 : 1, 0),
                    "actual Export Selection control persists exact event/value/Replay-bus settings with other audio buses disabled");
                Check(report.EditorChangedAfterQueue,
                    "native editor removes cue and changes volume after admission while queued ticket retains its frozen audio preferences");
                File.Copy(report.TicketPath, Path.Combine(output, name + "-ticket.json"));
                var terminal = await WaitNativeExportAsync(profile, report.Id);
                Check(terminal.State == "Complete" && terminal.Frames == 3 && terminal.TotalFrames == 3,
                    "actual native event cue export completes every canonical inclusive sample");
                string pcmPath = Path.Combine(terminal.Directory, "offline.wav");
                var pcm = StudioPcmAudio.ReadWave(pcmPath);
                double energy = pcm.Samples.Select(sample => (double)sample * sample).Average();
                int nonzero = pcm.Samples.Count(sample => sample != 0);
                Check(pcm.SampleRate == 48000 && pcm.Channels == 2 && pcm.Frames == 2400,
                    "native audio cue worker emits exact requested stereo PCM duration");
                Check(muted ? nonzero == 0 && energy == 0 : nonzero > pcm.Samples.Length * .9 && Math.Sqrt(energy) > .1,
                    muted ? "Replay volume zero yields exact silence with all other buses disabled" : "actual recorded event binding produces measured audible Replay-bus PCM energy");
                File.Copy(pcmPath, Path.Combine(output, name + "-offline.wav"));
                File.Copy(Path.Combine(terminal.Directory, "frame_00000000.png"), Path.Combine(output, name + "-native-frame.png"));
                CheckNativePng(Path.Combine(output, name + "-native-frame.png"), 1280, 720,
                    "actual cue export worker retains nonflat canonical native GPU frame output");
                await WaitForNativeAudioCleanupAsync(profile, report.Id);
                string durable = NativeExportRoot(profile, report.Id);
                File.Copy(Path.Combine(durable, "status.json"), Path.Combine(output, name + "-status.json"));
                File.Copy(Path.Combine(durable, "worker.log"), Path.Combine(output, name + "-worker.log"));
                File.WriteAllText(Path.Combine(output, name + "-pcm-proof.json"), JsonSerializer.Serialize(new
                { report, terminal, pcm.SampleRate, pcm.Channels, pcm.Frames, NonzeroSamples=nonzero, Rms=Math.Sqrt(energy) },
                    new JsonSerializerOptions { WriteIndented=true, IncludeFields=true }));
            }
            Check(original.SequenceEqual(File.ReadAllBytes(fixture)) && authored.SequenceEqual(File.ReadAllBytes(source)),
                "actual audio cue controls and native workers preserve both original recording and owned event fixture bytes");
            await CloseNativeProbeAsync(studio);
            await WaitNativeWorkerLeaseReleaseAsync();
        }
        finally
        {
            foreach (Guid id in queued)
            {
                string root = NativeExportRoot(profile, id); var status = ReadNativeExport(profile, id);
                if (Directory.Exists(root) && status?.State is not ("Complete" or "Cancelled" or "Failed"))
                { File.WriteAllText(Path.Combine(root, "cancel"), "cancel"); try { await WaitNativeExportAsync(profile, id); } catch (Exception) { } }
            }
            if (!studio.HasExited) { studio.Kill(entireProcessTree:true); await studio.WaitForExitAsync(); }
        }
        static string Hash(byte[] bytes) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
    }
    private static async Task WaitForNativeAudioCleanupAsync(string profile, Guid id)
    {
        var timer = Stopwatch.StartNew(); string root = NativeExportRoot(profile, id);
        while (Directory.Exists(Path.Combine(root, "cache")) && timer.Elapsed < TimeSpan.FromSeconds(15)) await Task.Delay(25);
        Check(!Directory.Exists(Path.Combine(root, "cache")) && File.Exists(Path.Combine(root, "worker.log")),
            "actual cue worker releases private scratch after native shutdown and retains its durable log");
    }
}
