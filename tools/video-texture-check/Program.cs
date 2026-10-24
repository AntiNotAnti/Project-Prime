using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.MapGen;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Materials;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Desktop;
using SkiaSharp;

internal static class Program
{
    const int Width = 64, Height = 32, Rate = 12, Frames = 12;
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<object> Checks = new();
    static readonly HashSet<int> ObservedDecoderPids = new();
    static string Output = "";
    static string Ffmpeg = "ffmpeg", Ffprobe = "ffprobe";
    static int Passed;
    static string? Renderer;
    static string Selection = "full";

    static int Main(string[] args)
    {
        string Value(string flag, string fallback)
        {
            int index = Array.IndexOf(args, flag);
            return index < 0 ? fallback : index + 1 < args.Length ? args[index + 1] : throw new ArgumentException("Missing " + flag);
        }
        try
        {
            Output = Path.GetFullPath(Value("--output", "artifacts/video-texture-check/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")));
            if (Directory.Exists(Output) && Directory.EnumerateFileSystemEntries(Output).Any())
                throw new IOException("Use a new empty output directory; existing evidence is never overwritten.");
            Directory.CreateDirectory(Output);
            if (args.Contains("--cpu-only") && args.Contains("--retirement-only"))
                throw new ArgumentException("--cpu-only and --retirement-only select different acceptance sections.");
            Selection = args.Contains("--retirement-only") ? "retirement-only" : args.Contains("--cpu-only") ? "cpu-only" : "full";
            // Set before any AppPaths access: this acceptance run never loads or writes user settings.
            Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA", Path.Combine(Output, "user-data"));
            Ffmpeg = Value("--ffmpeg", "ffmpeg"); Ffprobe = Value("--ffprobe", "ffprobe");
            if (File.Exists(Ffmpeg)) Ffmpeg = Path.GetFullPath(Ffmpeg);
            if (File.Exists(Ffprobe)) Ffprobe = Path.GetFullPath(Ffprobe);
            foreach (string path in new[] { Ffmpeg, Ffprobe }.Where(Path.IsPathRooted))
                if (!File.Exists(path)) throw new FileNotFoundException("Missing acceptance prerequisite", path);
            string[] toolDirectories = new[] { Ffmpeg, Ffprobe }.Where(Path.IsPathRooted)
                .Select(p => Path.GetDirectoryName(p)!).Distinct().ToArray();
            Environment.SetEnvironmentVariable("PATH", string.Join(Path.PathSeparator, toolDirectories)
                + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"));
            File.WriteAllText(Path.Combine(Output, "ffmpeg-version.txt"), Run(Ffmpeg, "-version"));
            File.WriteAllText(Path.Combine(Output, "ffprobe-version.txt"), Run(Ffprobe, "-version"));
            foreach (bool commonCodec in args.Contains("--retirement-only") ? new[] { false } : new[] { false, true })
            {
                string clip = Fixture(commonCodec);
                MapDefinition definition = PackageFixture(clip);
                if (!args.Contains("--retirement-only"))
                {
                    DecoderChecks(clip, definition.Materials[0].VideoTexture!, commonCodec);
                    SourceChecks(definition, clip);
                }
                if (!args.Contains("--cpu-only")) GpuChecks(clip, definition, commonCodec, args.Contains("--retirement-only"));
            }
            Wait(() => ObservedDecoderPids.All(pid => !ProcessAlive(pid)));
            Check(ObservedDecoderPids.Count > 0 && ObservedDecoderPids.All(pid => !ProcessAlive(pid)),
                "All observed actual FFmpeg decoder PIDs have exited", ObservedDecoderPids.ToArray());
            WriteReport(true, null, args.Contains("--cpu-only"));
            Console.WriteLine($"VIDEO TEXTURE PASS {Passed}; scope={(args.Contains("--cpu-only") ? "CPU/package only; GPU NOT RUN" : "real FFmpeg, OpenGL upload/color encode and production Scene video bindings")}");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            if (Output.Length > 0 && Directory.Exists(Output)) WriteReport(false, error.ToString(), args.Contains("--cpu-only"));
            return 1;
        }
    }

    static void Check(bool condition, string name, object? evidence = null)
    {
        Checks.Add(new { passed = condition, name, evidence });
        Console.WriteLine($"VIDEO {(condition ? "PASS" : "FAIL")} {name}");
        if (!condition) throw new InvalidOperationException(name + ": " + JsonSerializer.Serialize(evidence));
        Passed++;
    }

    static void WriteReport(bool passed, string? error, bool cpuOnly)
        => File.WriteAllText(Path.Combine(Output, "report.json"), JsonSerializer.Serialize(new
        {
            passed, assertions = Passed, selection = Selection, cpuOnly, error, renderer = Renderer,
            assembly = typeof(Scene).Assembly.Location, mvid = typeof(Scene).Module.ModuleVersionId,
            sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Scene).Assembly.Location))),
            ffmpeg = Ffmpeg, ffprobe = Ffprobe, productionDecoder = MapVideoTools.Resolve("ffmpeg"), checks = Checks,
            observedDecoderProcesses = ObservedDecoderPids.Select(pid => new { pid, stillAlive = ProcessAlive(pid) }).ToArray(),
            limitations = new[] { "Scene video binding tests use production Start/Update/Resolve and teardown hooks; they do not run Map Studio, model import or a multiplayer match.",
                "Generated lossless RGB MP4 is original synthetic content; acceptance does not certify every third-party codec or physical Android playback." }
        }, new JsonSerializerOptions { WriteIndented = true }));

    static string Run(string executable, params string[] args)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in args) start.ArgumentList.Add(arg);
        using Process process = Process.Start(start) ?? throw new IOException("Cannot start " + executable);
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30000)) { process.Kill(true); throw new TimeoutException(executable); }
        string output = stdout.GetAwaiter().GetResult(), errors = stderr.GetAwaiter().GetResult();
        if (process.ExitCode != 0) throw new IOException(executable + " failed: " + errors);
        return output + errors;
    }

    static byte[] Pixels(int frame)
    {
        byte[] result = new byte[Width * Height * 3];
        for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++)
        {
            byte[] color = Color(frame, (x >= Width / 2 ? 1 : 0) + (y >= Height / 2 ? 2 : 0));
            color.CopyTo(result, (y * Width + x) * 3);
        }
        return result;
    }

    static byte[] Color(int frame, int quadrant) => quadrant switch
    {
        0 => new byte[] { (byte)(100 + frame * 10), 30, 50 },
        1 => new byte[] { 40, (byte)(100 + frame * 10), 70 },
        2 => new byte[] { 50, 60, (byte)(100 + frame * 10) },
        _ => new byte[] { 128, 128, 128 }
    };

    static string Fixture(bool commonCodec)
    {
        string name = commonCodec ? "h264-yuv420p" : "lossless-rgb";
        string raw = Path.Combine(Output, name + ".rgb"), clip = Path.Combine(Output, name + ".mp4");
        using (Stream stream = File.Create(raw)) for (int i = 0; i < Frames; i++) stream.Write(Pixels(i));
        File.WriteAllText(Path.Combine(Output, name + "-encode.log"), Run(Ffmpeg, "-nostdin", "-hide_banner", "-y",
            "-f", "rawvideo", "-pixel_format", "rgb24", "-video_size", $"{Width}x{Height}", "-framerate", Rate.ToString(),
            "-i", raw, "-an", "-frames:v", Frames.ToString(), "-c:v", commonCodec ? "libx264" : "libx264rgb",
            "-crf", commonCodec ? "18" : "0", "-preset", "ultrafast", "-pix_fmt", commonCodec ? "yuv420p" : "rgb24",
            "-movflags", "+faststart", clip));
        Check(File.Exists(clip) && new FileInfo(clip).Length > 100, "Real FFmpeg creates original " + name + " MP4");
        return clip;
    }

    static MapDefinition PackageFixture(string clip)
    {
        string name = Path.GetFileNameWithoutExtension(clip);
        string directory = Path.Combine(Output, "fixture-" + name); Directory.CreateDirectory(directory);
        Directory.CreateDirectory(Path.Combine(directory, "textures"));
        using (var writer = new BinaryWriter(File.Create(Path.Combine(directory, "textures/fallback.tex"))))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("FPTX"));
            foreach (ushort value in new ushort[] { 1, 1, 0, 8, 8, 1, 0 }) writer.Write(value);
            writer.Write((ushort)(15 | 15 << 5 | 15 << 10)); writer.Write(new byte[64]);
        }
        var definition = new MapDefinition { FormatVersion = 3, MapId = Guid.NewGuid(), Name = "VIDEO ACCEPTANCE", BaseDirectory = directory };
        string probeText = Run(Ffprobe, "-v", "error", "-protocol_whitelist", "file,pipe", "-f", "mov",
            "-select_streams", "v:0", "-show_entries", "stream=width,height,avg_frame_rate,nb_frames", "-of", "json", clip);
        File.WriteAllText(Path.Combine(Output, name + "-probe.json"), probeText);
        using var probe = JsonDocument.Parse(probeText);
        JsonElement stream = probe.RootElement.GetProperty("streams")[0].Clone();
        Check(stream.GetProperty("width").GetInt32() == Width && stream.GetProperty("height").GetInt32() == Height
            && stream.GetProperty("avg_frame_rate").GetString() == Rate + "/1"
            && stream.GetProperty("nb_frames").GetString() == Frames.ToString(),
            "Real ffprobe verifies synthetic clip dimensions, clock and frame count", stream);
        string relativeVideo = "video/" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(clip))).ToLowerInvariant() + ".mp4";
        Directory.CreateDirectory(Path.Combine(directory, "video"));
        File.Copy(clip, Path.Combine(directory, relativeVideo));
        var material = new MapMaterial { Id = Guid.NewGuid(), Name = "video", Texture = "textures/fallback.tex",
            VideoTexture = new MapVideoTexture { Source = relativeVideo, Width = Width, Height = Height,
                FramesPerSecond = Rate, FrameCount = Frames } };
        definition.Materials.Add(material);
        definition.Assets.Add(new() { Path = material.Texture, Kind = "texture" });
        definition.Assets.Add(new() { Path = relativeVideo, Kind = "video" });
        var validation = new MapValidationResult();
        MapAssets.Validate(definition, validation, checkFiles: true);
        Check(validation.IsValid && definition.Assets.Count(a => a.Kind == "video") == 1,
            "Production asset validation accepts content-addressed video metadata and native fallback");
        string project = Path.Combine(directory, "video.json"); definition.Save(project);
        definition = MapDefinition.Load(project);
        Check(definition.Materials[0].VideoTexture?.Source == relativeVideo, "Saved project reopens with video metadata");
        string package = Path.Combine(Output, name + ".ppmap");
        // Source-only packaging API, followed by the ordinary strict trust boundary.
        // No claim that this geometry-free fixture has passed runtime map compilation.
        MapPackageBuilder.WriteValidated(definition, package);
        var packaged = MapDefinition.Load(package);
        Check(packaged.BundlePath != null && packaged.Materials[0].VideoTexture?.Source == relativeVideo,
            "Strict .ppmap reader verifies package hash and restores video material");
        Check(MapAssets.Read(packaged, relativeVideo).SequenceEqual(File.ReadAllBytes(clip)), "Packaged encoded MP4 bytes are unchanged");
        return packaged;
    }

    static MapVideoTexture Metadata(MapVideoTexture original, bool? loop = null, string? group = null)
        => new() { Source = original.Source, Width = original.Width, Height = original.Height,
            FramesPerSecond = original.FramesPerSecond, FrameCount = original.FrameCount,
            Loop = loop ?? original.Loop, PlaybackGroup = group ?? original.PlaybackGroup };

    static void Wait(Func<bool> ready, Action? tick = null, int milliseconds = 10000)
    {
        var clock = Stopwatch.StartNew();
        while (!ready())
        {
            if (clock.ElapsedMilliseconds > milliseconds) throw new TimeoutException("Acceptance condition did not complete within " + milliseconds + " ms.");
            tick?.Invoke(); Thread.Sleep(5);
        }
    }

    static bool FrameMatches(byte[] pixels, int frame, bool commonCodec)
    {
        if (!commonCodec) return pixels.SequenceEqual(Pixels(frame));
        // 4:2:0 conversion and CRF18 are lossy. Compare all three channels in
        // the center of each independently generated region, away from chroma edges.
        // Eight code values is an explicit codec-only bound, never an RGB-path allowance.
        for (int q = 0; q < 4; q++)
        {
            int x = q % 2 == 0 ? Width / 4 : Width * 3 / 4;
            int y = q < 2 ? Height / 4 : Height * 3 / 4;
            if (!pixels.AsSpan((y * Width + x) * 3, 3).ToArray().Zip(Color(frame, q)).All(p => Math.Abs(p.First - p.Second) <= 8)) return false;
        }
        return true;
    }

    static void DecoderChecks(string clip, MapVideoTexture video, bool commonCodec)
    {
        using (var decoder = new MapVideoDecoder(Metadata(video, false), () => clip, Ffmpeg))
        {
            for (int index = 0; index < Frames; index++)
            {
                MapVideoDecoder.Frame? frame = null;
                Wait(() => decoder.TryTake(out frame) || decoder.Failure != null);
                Check(decoder.Failure == null && frame != null, "Decoder produces complete RGB frame " + index, decoder.Failure);
                Check(frame!.Index == index && FrameMatches(frame.Pixels, index, commonCodec),
                    (commonCodec ? "H264 yuv420p RGB centers within8 code values; frame order " : "Decoded exact RGB pixels and frame order ") + index);
                decoder.Recycle(frame);
            }
            Wait(() => decoder.Completion.IsCompleted);
            Check(decoder.Failure == null && decoder.FramesDecoded == Frames && decoder.MaximumQueued <= MapVideoDecoder.QueueCapacity,
                "Nonloop decoder reaches clean EOF within bounded queue", new { decoder.Failure, decoder.FramesDecoded, decoder.MaximumQueued });
        }
        var looping = new MapVideoDecoder(Metadata(video, true), () => clip, Ffmpeg);
        Wait(() => looping.FramesDecoded >= MapVideoDecoder.QueueCapacity || looping.Failure != null);
        Thread.Sleep(250);
        Check(looping.Failure == null && looping.MaximumQueued <= MapVideoDecoder.QueueCapacity
            && looping.FramesDecoded <= MapVideoDecoder.QueueCapacity,
            "Idle consumer applies real pipe/queue backpressure", new { looping.FramesDecoded, looping.MaximumQueued, looping.Failure });
        int loopingPid = DecoderPid(looping);
        long start = Stopwatch.GetTimestamp(); looping.Dispose();
        double disposeMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        Check(disposeMilliseconds < 250, "Decoder disposal does not wait on GL/caller thread", new { disposeMilliseconds, workerCompletedOnReturn = looping.Completion.IsCompleted });
        Wait(() => looping.Completion.IsCompleted);
        Check(looping.Completion.IsCompleted && !ProcessAlive(loopingPid), "Blocked looping FFmpeg child exits before worker completion", new { loopingPid });
        looping.Dispose();
        var missing = new MapVideoDecoder(video, () => clip, Path.Combine(Output, "missing-ffmpeg.exe"));
        Wait(() => missing.Completion.IsCompleted);
        Check(missing.Failure != null && missing.FailureStage == "decoder-discovery", "Missing decoder reports explicit discovery failure", missing.Failure);
        missing.Dispose();
        var wrong = Metadata(video, false); wrong.Width /= 2;
        using (var decoder = new MapVideoDecoder(wrong, () => clip, Ffmpeg))
        {
            Wait(() => decoder.Completion.IsCompleted);
            Check(decoder.Failure?.Contains("dimensions differ") == true && decoder.FramesDecoded == 0,
                "Actual decoded dimensions mismatch is rejected without resize/upload", decoder.Failure);
        }
        string corrupt = Path.Combine(Output, "corrupt.mp4"); File.WriteAllText(corrupt, "This is not an MP4");
        using (var decoder = new MapVideoDecoder(Metadata(video, false), () => corrupt, Ffmpeg))
        {
            Wait(() => decoder.Completion.IsCompleted);
            Check(decoder.Failure != null && decoder.FramesDecoded == 0, "Malformed MP4 fails rather than presenting fabricated pixels", decoder.Failure);
        }
    }

    static void SourceChecks(MapDefinition definition, string clip)
    {
        var store = new MapVideoSourceStore(); var video = definition.Materials[0].VideoTexture!;
        string[] paths = Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => store.Get(definition, video)))).GetAwaiter().GetResult();
        Check(store.SourceCount == 1 && paths.Distinct().Count() == 1, "Concurrent package consumers extract a shared MP4 exactly once");
        Check(File.ReadAllBytes(paths[0]).SequenceEqual(File.ReadAllBytes(clip)), "Worker extraction preserves encoded MP4 bytes");
        Check(MapVideoPlaybackKey.Create(definition, video) == MapVideoPlaybackKey.Create(definition, Metadata(video))
            && MapVideoPlaybackKey.Create(definition, video) != MapVideoPlaybackKey.Create(definition, Metadata(video, group: "independent")),
            "Playback identity shares matching clocks and separates explicit groups");
        string directory = store.DirectoryPath; store.DisposeAfter(Array.Empty<Task>());
        Wait(() => !Directory.Exists(directory)); Check(!Directory.Exists(directory), "Retired extraction directory is removed");
    }

    static object? Call(Scene scene, string method, params object?[] args)
    {
        try { return typeof(Scene).GetMethod(method, Hidden)!.Invoke(scene, args); }
        catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
    }
    static object Field(Scene scene, string field) => typeof(Scene).GetField(field, Hidden)!.GetValue(scene)!;
    static int DecoderPid(MapVideoDecoder decoder)
    {
        int pid = ((Process?)typeof(MapVideoDecoder).GetField("_process", Hidden)!.GetValue(decoder))?.Id
            ?? throw new InvalidOperationException("Live decoder has no observable child process.");
        ObservedDecoderPids.Add(pid); return pid;
    }
    static bool ProcessAlive(int pid)
    {
        try { using var process = Process.GetProcessById(pid); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }
    static MapVideoTexturePlayer Player(Scene scene, int binding)
    {
        object channel = ((IDictionary)Field(scene, "_mapVideoBindings"))[binding]!;
        return (MapVideoTexturePlayer)channel.GetType().GetField("Player", Hidden)!.GetValue(channel)!;
    }

    static void GpuChecks(string clip, MapDefinition definition, bool commonCodec, bool retirementOnly)
    {
        var settings = DesktopGlContext.Settings(background: true); settings.ClientSize = new(128, 96);
        settings.StartVisible = false; settings.StartFocused = false; settings.Title = "Video material acceptance";
        using var window = new NativeWindow(settings); using var graphics = new DesktopGraphicsSession(window);
        Renderer = GL.GetString(StringName.Renderer) + " / " + GL.GetString(StringName.Version);
        Check(!string.IsNullOrWhiteSpace(Renderer), "Real desktop OpenGL context created", Renderer);
        RenderOptions.VideoTextures = true; RenderOptions.VideoTextureMemoryMiB = 128;
        RenderOptions.TextureReplacements = false;
        RetirementChecks(definition);
        if (retirementOnly) return;
        DirectPlayerChecks(clip, definition.Materials[0].VideoTexture!, commonCodec);
        SceneBindingChecks(definition, commonCodec);
        Check(GL.GetError() == ErrorCode.NoError, "Acceptance finishes with no OpenGL errors");
    }

    static void RetirementChecks(MapDefinition definition)
    {
        RenderOptions.VideoTextureMemoryMiB = 64;
        MapVideoTexture video = definition.Materials[0].VideoTexture!;
        var key = MaterialAssetKey.Authored(definition.MapId, definition.Materials[0].Id);
        for (int attempt = 0; attempt < 32; attempt++)
        {
            var scene = new Scene(default, null!, null!, _ => { }, () => { }, initializeRuntime: false);
            MapVideoTexturePlayer? old = null;
            try
            {
                int source = (int)Call(scene, "AllocateTexture")!, replacement = (int)Call(scene, "AllocateTexture")!;
                Call(scene, "StartMapVideoTexture", source, key, definition, video); old = Player(scene, source);
                Wait(() => old.FramesUploaded > 0 || old.Failure != null, () => old.Update(false));
                Check(old.Failure == null, "Adversarial retirement starts real FFmpeg decoder " + attempt, old.Failure);
                int oldPid = DecoderPid(old.Decoder);
                long codec = Math.Max(MapVideoDecoder.InitialCodecMemoryReserve, old.Decoder.WorkingSetBytes);
                Call(scene, "ReleaseTexture", source);
                if (old.Decoder.Completion.IsCompleted) continue;
                Call(scene, "StartMapVideoTexture", replacement, key, definition, Metadata(video, group: "replacement-" + attempt));
                // Completion is monotonic. If still pending after the attempted
                // admission, it was pending throughout the real resource gate.
                if (old.Decoder.Completion.IsCompleted) continue;
                Check(scene.UniqueMapVideoCount == 0 && ((IDictionary)Field(scene, "_pendingMapVideos")).Contains(replacement),
                    "Pending real decoder retirement retains codec reservation and blocks overlapping64MiB replacement",
                    new { attempt, retiredCodecBytes = codec, retiredFixedBytes = scene.MapVideoFrameResidentBytes,
                        oldWorkerStillPending = !old.Decoder.Completion.IsCompleted, admittedChannels = scene.UniqueMapVideoCount });
                Wait(() => old.Decoder.Completion.IsCompleted); Call(scene, "CollectRetiredVideos");
                Check(!ProcessAlive(oldPid), "Retired worker completion confirms actual child PID exited", new { oldPid });
                Call(scene, "StartMapVideoTexture", replacement, key, definition, Metadata(video, group: "replacement-" + attempt));
                Check(scene.UniqueMapVideoCount == 1, "Worker completion releases retired codec reservation for valid replacement");
                RenderOptions.VideoTextureMemoryMiB = 128;
                return;
            }
            finally { scene.UnloadGl(); if (old != null) Wait(() => old.Decoder.Completion.IsCompleted); }
        }
        throw new InvalidOperationException("No genuinely pending retirement observed in32 real decoder cycles; accounting acceptance is incomplete.");
    }

    static byte[] TexturePixels(int binding)
    {
        GL.ActiveTexture(TextureUnit.Texture0); GL.BindTexture(TextureTarget.Texture2D, binding);
        GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
        byte[] data = new byte[Width * Height * 3]; GL.GetTexImage(TextureTarget.Texture2D, 0, PixelFormat.Rgb, PixelType.UnsignedByte, data);
        return data;
    }

    static void DirectPlayerChecks(string clip, MapVideoTexture video, bool commonCodec)
    {
        int texture = GL.GenTexture(), sentinel = GL.GenTexture(); bool linear = false;
        using var player = new MapVideoTexturePlayer(texture, Metadata(video, true), () => clip, Ffmpeg, () => linear);
        try
        {
            GL.ActiveTexture(TextureUnit.Texture0); GL.BindTexture(TextureTarget.Texture2D, sentinel);
            GL.ActiveTexture(TextureUnit.Texture3); GL.PixelStore(PixelStoreParameter.UnpackAlignment, 8);
            Wait(() => player.FramesUploaded > 0 || player.Failure != null, () => player.Update(false));
            Check(player.Failure == null && player.PresentedIndex == 0, "Real decoded first frame uploads through production texture player", player.Failure);
            DecoderPid(player.Decoder);
            Check(GL.GetInteger(GetPName.ActiveTexture) == (int)TextureUnit.Texture3
                && GL.GetInteger(GetPName.UnpackAlignment) == 8, "Upload restores active texture and unpack alignment");
            GL.ActiveTexture(TextureUnit.Texture0);
            Check(GL.GetInteger(GetPName.TextureBinding2D) == sentinel, "Upload restores prior texture binding on texture unit zero");
            Check(FrameMatches(TexturePixels(texture), 0, commonCodec), "Actual GPU texture retains full-color source pixels within codec bound");
            GL.GetTexLevelParameter(TextureTarget.Texture2D, 0, GetTextureParameter.TextureInternalFormat, out int format);
            Check(format == (int)PixelInternalFormat.Rgba8, "Encoded scene path uploads RGBA8 storage");
            GL.GetTexLevelParameter(TextureTarget.Texture2D, 6, GetTextureParameter.TextureWidth, out int mipWidth);
            Check(mipWidth == 1, "Actual video upload generates full mip chain");
            long pausedIndex = player.PresentedSequenceIndex, uploads = player.FramesUploaded;
            byte[] pausedPixels = TexturePixels(texture);
            var pause = Stopwatch.StartNew();
            while (pause.ElapsedMilliseconds < 400) { player.Update(true); Thread.Sleep(5); }
            Check(player.PresentedSequenceIndex == pausedIndex && player.FramesUploaded == uploads
                && TexturePixels(texture).SequenceEqual(pausedPixels), "Pause freezes presentation clock and real GPU pixels");
            Wait(() => player.PresentedSequenceIndex > pausedIndex || player.Failure != null, () => player.Update(false));
            Check(player.Failure == null && player.PresentedSequenceIndex <= pausedIndex + 2,
                "Resume continues from paused source time without accumulating pause duration");
            linear = true;
            long before = player.FramesUploaded;
            Wait(() => player.FramesUploaded > before || player.Failure != null, () => player.Update(false));
            TexturePixels(texture);
            GL.GetTexLevelParameter(TextureTarget.Texture2D, 0, GetTextureParameter.TextureInternalFormat, out format);
            Check(format == (int)PixelInternalFormat.Srgb8Alpha8 && FrameMatches(TexturePixels(texture), player.PresentedIndex, commonCodec),
                "Linear scene wrapper reallocates sRGB storage without changing encoded RGB values");
            byte[] roundtrip = EncodeVideoTexture(texture);
            Check(commonCodec ? FrameMatches(roundtrip, player.PresentedIndex, true)
                : roundtrip.Zip(Pixels(player.PresentedIndex)).All(p => Math.Abs(p.First - p.Second) <= 1),
                "Actual production scene encode shader roundtrips sRGB video without double gamma");
            Wait(() => player.LoopsCompleted >= 1 || player.Failure != null, () => player.Update(false));
            Check(player.Failure == null && player.LoopsCompleted >= 1
                && FrameMatches(TexturePixels(texture), player.PresentedIndex, commonCodec), "Real FFmpeg loop advances clocks and returns correct source frames");
            player.Dispose(); Wait(() => player.Decoder.Completion.IsCompleted);
            Check(player.Decoder.Completion.IsCompleted, "Texture player stop cancels looping decoder worker");
        }
        finally { GL.ActiveTexture(TextureUnit.Texture0); GL.BindTexture(TextureTarget.Texture2D, 0); GL.DeleteTexture(texture); GL.DeleteTexture(sentinel); }

        texture = GL.GenTexture();
        using var finite = new MapVideoTexturePlayer(texture, Metadata(video, false), () => clip, Ffmpeg);
        try
        {
            Wait(() => finite.PresentedIndex == Frames - 1 || finite.Failure != null, () => finite.Update(false));
            long count = finite.FramesUploaded;
            var hold = Stopwatch.StartNew(); while (hold.ElapsedMilliseconds < 250) { finite.Update(false); Thread.Sleep(5); }
            Check(finite.Failure == null && finite.Decoder.Completion.IsCompleted && finite.FramesUploaded == count
                && finite.PresentedIndex == Frames - 1 && FrameMatches(TexturePixels(texture), Frames - 1, commonCodec),
                "Nonloop playback holds final real frame after clean decoder EOF");
        }
        finally { finite.Dispose(); Wait(() => finite.Decoder.Completion.IsCompleted); GL.BindTexture(TextureTarget.Texture2D, 0); GL.DeleteTexture(texture); }
    }

    static void SceneBindingChecks(MapDefinition definition, bool commonCodec)
    {
        var scene = new Scene(default, null!, null!, _ => { }, () => { }, initializeRuntime: false);
        var video = definition.Materials[0].VideoTexture!;
        var materialKey = MaterialAssetKey.Authored(definition.MapId, definition.Materials[0].Id);
        void Tick() => Call(scene, "UpdateMapVideoTextures");
        string? extractedDirectory = null;
        try
        {
            int binding = (int)Call(scene, "AllocateTexture")!;
            Call(scene, "StartMapVideoTexture", binding, materialKey, definition, video);
            Check(scene.MapVideoBindingCount == 1 && scene.UniqueMapVideoCount == 1, "Production Scene starts a packaged video binding without editor/model APIs");
            var player = Player(scene, binding);
            Wait(() => player.FramesUploaded > 1 || player.Failure != null, Tick);
            Check(player.Failure == null, "Packaged source decodes and uploads through production Scene update", player.Failure);
            DecoderPid(player.Decoder);
            Tick();
            int sampled = (int)Call(scene, "ResolveMapVideoBinding", binding, true)!;
            byte[] picture = TexturePixels(sampled);
            WritePpm(Path.Combine(Output, commonCodec ? "h264-yuv420p-scene-texture.ppm" : "lossless-rgb-scene-texture.ppm"), picture, Width, Height);
            Check(sampled == player.Binding && sampled != binding && FrameMatches(picture, player.PresentedIndex, commonCodec),
                "Production Scene resolver samples the uploaded full-color video texture", new { binding, sampled, player.PresentedIndex });
            var store = (MapVideoSourceStore)Field(scene, "_mapVideoSources"); extractedDirectory = store.DirectoryPath;
            Check(scene.ExtractedMapVideoSourceCount == 1, "Actual Scene worker extracts packaged video once");
            long resident = scene.MapVideoFrameResidentBytes;
            int alias = (int)Call(scene, "AllocateTexture")!, independent = (int)Call(scene, "AllocateTexture")!;
            Call(scene, "StartMapVideoTexture", alias, materialKey, definition, video);
            Check(scene.UniqueMapVideoCount == 1 && scene.MapVideoBindingCount == 2
                && ReferenceEquals(player, Player(scene, alias)) && scene.MapVideoFrameResidentBytes == resident,
                "Second production material aliases exact same decoder/GPU texture without resident growth");
            Call(scene, "StartMapVideoTexture", independent, materialKey, definition, Metadata(video, group: "independent"));
            Check(scene.UniqueMapVideoCount == 2 && scene.MapVideoBindingCount == 3 && scene.MapVideoFrameResidentBytes == resident * 2,
                "Explicit playback group creates independent production channel and exact fixed-memory accounting");
            var independentPlayer = Player(scene, independent);
            Wait(() => independentPlayer.FramesUploaded > 0 || independentPlayer.Failure != null, Tick);
            Check(independentPlayer.Failure == null && scene.ExtractedMapVideoSourceCount == 1,
                "Independent playback still shares one package extraction");
            DecoderPid(independentPlayer.Decoder);
            Call(scene, "ReleaseTexture", alias);
            Check(scene.UniqueMapVideoCount == 2 && !player.Decoder.Completion.IsCompleted,
                "Releasing an alias preserves active shared playback");
            Call(scene, "ReleaseTexture", independent); Wait(() => independentPlayer.Decoder.Completion.IsCompleted);
            Call(scene, "CollectRetiredVideos");
            Check(scene.UniqueMapVideoCount == 1 && scene.MapVideoFrameResidentBytes == resident,
                "Last independent reference retires decoder/GPU and releases exact budget");
            int failedBinding = (int)Call(scene, "AllocateTexture")!;
            var missing = Metadata(video, group: "missing"); missing.Source = "video/not-declared.mp4";
            Call(scene, "StartMapVideoTexture", failedBinding, materialKey, definition, missing);
            var failedPlayer = Player(scene, failedBinding);
            Wait(() => failedPlayer.Decoder.Completion.IsCompleted); Tick();
            Check(failedPlayer.Failure != null && (int)Call(scene, "ResolveMapVideoBinding", failedBinding, false)! == failedBinding,
                "Runtime extraction failure keeps explicit untextured fallback instead of invalid canonical texture");
            Call(scene, "ReleaseTexture", failedBinding);
            RenderOptions.VideoTextures = false; Tick();
            Wait(() => player.Decoder.Completion.IsCompleted); Call(scene, "CollectRetiredVideos");
            Check(scene.UniqueMapVideoCount == 0 && scene.MapVideoBindingCount == 0 && scene.MapVideoFrameResidentBytes == 0,
                "Graphics video disable cancels playback and releases channel residency");
            Check((int)Call(scene, "ResolveMapVideoBinding", binding, false)! == binding, "Disabled binding resolves to native fallback");
            RenderOptions.VideoTextures = true; RenderOptions.VideoTextureMemoryMiB = 64; Tick();
            // This asset-free Scene has no model texture-source table. Rebind
            // explicitly after a setting refresh; do not claim model reattachment.
            Call(scene, "StartMapVideoTexture", binding, materialKey, definition, video);
            player = Player(scene, binding);
            Wait(() => player.FramesUploaded > 0 || player.Failure != null, Tick);
            int budgetBinding = (int)Call(scene, "AllocateTexture")!;
            var budgetVideo = Metadata(video, group: "budget");
            Call(scene, "StartMapVideoTexture", budgetBinding, materialKey, definition, budgetVideo);
            Check(scene.UniqueMapVideoCount == 1 && ((IDictionary)Field(scene, "_pendingMapVideos")).Contains(budgetBinding),
                "Valid64MiB budget blocks second independent decoder including both codec reserves",
                new { RenderOptions.VideoTextureMemoryMiB, scene.UniqueMapVideoCount, scene.MapVideoFrameResidentBytes });
            GL.BindTexture(TextureTarget.Texture2D, budgetBinding);
            byte[] fallback = new byte[8*8*4]; GL.GetTexImage(TextureTarget.Texture2D,0,PixelFormat.Rgba,PixelType.UnsignedByte,fallback);
            Check(fallback[0] == 123 && fallback[1] == 123 && fallback[2] == 123 && fallback[3] == 255,
                "Resource-blocked video retains deliberate gray opaque fallback pixels");
            var beforeBudgetRefresh = player;
            RenderOptions.VideoTextureMemoryMiB = 128; Tick();
            Wait(() => beforeBudgetRefresh.Decoder.Completion.IsCompleted); Call(scene, "CollectRetiredVideos");
            Call(scene, "StartMapVideoTexture", binding, materialKey, definition, video);
            Wait(() => scene.UniqueMapVideoCount == 2, Tick);
            Check(scene.UniqueMapVideoCount == 2, "Raising graphics memory budget automatically retries blocked material admission");
            var budgetPlayer = Player(scene, budgetBinding);
            Wait(() => budgetPlayer.FramesUploaded > 0 || budgetPlayer.Failure != null, Tick);
            Check(budgetPlayer.Failure == null, "Automatically retried independent source reaches real decoded frame");
            DecoderPid(budgetPlayer.Decoder);
            Call(scene, "ReleaseTexture", budgetBinding); Wait(() => budgetPlayer.Decoder.Completion.IsCompleted);
            player = Player(scene, binding); Wait(() => player.FramesUploaded > 0 || player.Failure != null, Tick);
            Check(player.Failure == null, "Retry after resource block reaches real decoded frame");
            int canonical = player.Binding;
            scene.UnloadGl(); Wait(() => player.Decoder.Completion.IsCompleted);
            if (extractedDirectory != null) Wait(() => !Directory.Exists(extractedDirectory));
            Check(scene.UniqueMapVideoCount == 0 && scene.MapVideoBindingCount == 0 && !GL.IsTexture(canonical)
                && !GL.IsTexture(binding), "Actual Scene unload removes video channels and owned canonical/fallback GPU textures");
            Check(extractedDirectory == null || !Directory.Exists(extractedDirectory), "Scene unload deletes extracted packaged MP4 after worker completion");
            scene.UnloadGl(); Check(GL.GetError() == ErrorCode.NoError, "Repeated scene teardown is safe and GL-error free");
        }
        finally
        {
            scene.UnloadGl(); GL.BindTexture(TextureTarget.Texture2D, 0);
        }
    }

    static byte[] EncodeVideoTexture(int video)
    {
        int previousDraw = GL.GetInteger(GetPName.DrawFramebufferBinding), previousRead = GL.GetInteger(GetPName.ReadFramebufferBinding);
        int previousProgram = GL.GetInteger(GetPName.CurrentProgram);
        int vertex = 0, fragment = 0, program = 0, target = 0, framebuffer = 0;
        GL.PushAttrib(AttribMask.AllAttribBits);
        try
        {
            int Compile(ShaderType type, string source)
            {
                int shader = GL.CreateShader(type); GL.ShaderSource(shader, source); GL.CompileShader(shader);
                GL.GetShader(shader, ShaderParameter.CompileStatus, out int okay);
                if (okay == 0) { string error = GL.GetShaderInfoLog(shader); GL.DeleteShader(shader); throw new InvalidOperationException(error); }
                return shader;
            }
            vertex = Compile(ShaderType.VertexShader, SceneColorEncodeShader.Vertex);
            fragment = Compile(ShaderType.FragmentShader, SceneColorEncodeShader.Fragment);
            program = GL.CreateProgram(); GL.AttachShader(program, vertex); GL.AttachShader(program, fragment); GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            if (linked == 0) throw new InvalidOperationException(GL.GetProgramInfoLog(program));
            target = GL.GenTexture(); GL.ActiveTexture(TextureUnit.Texture0); GL.BindTexture(TextureTarget.Texture2D, target);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, Width, Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            framebuffer = GL.GenFramebuffer(); GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, target, 0);
            Check(GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) == FramebufferErrorCode.FramebufferComplete,
                "Production video color encode target is complete");
            GL.Viewport(0, 0, Width, Height); GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Blend);
            GL.Disable(EnableCap.ScissorTest); GL.Disable(EnableCap.CullFace); GL.Disable(EnableCap.AlphaTest);
            GL.Disable((EnableCap)0x8DB9); GL.ColorMask(true,true,true,true);
            GL.UseProgram(program); GL.Uniform1(GL.GetUniformLocation(program, "tex"), 0);
            GL.BindTexture(TextureTarget.Texture2D, video);
            GL.Begin(PrimitiveType.Quads);
            GL.TexCoord2(0f,0f); GL.Vertex2(-1f,-1f); GL.TexCoord2(1f,0f); GL.Vertex2(1f,-1f);
            GL.TexCoord2(1f,1f); GL.Vertex2(1f,1f); GL.TexCoord2(0f,1f); GL.Vertex2(-1f,1f); GL.End();
            byte[] result = new byte[Width*Height*3]; GL.PixelStore(PixelStoreParameter.PackAlignment,1);
            GL.ReadPixels(0,0,Width,Height,PixelFormat.Rgb,PixelType.UnsignedByte,result);
            return result;
        }
        finally
        {
            GL.UseProgram(previousProgram); GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer,previousDraw);
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer,previousRead);
            if (framebuffer != 0) GL.DeleteFramebuffer(framebuffer); if (target != 0) GL.DeleteTexture(target);
            if (program != 0) GL.DeleteProgram(program); if (vertex != 0) GL.DeleteShader(vertex); if (fragment != 0) GL.DeleteShader(fragment);
            GL.PopAttrib();
        }
    }

    static void WritePpm(string path, byte[] pixels, int width, int height)
    {
        using var stream = File.Create(path);
        stream.Write(System.Text.Encoding.ASCII.GetBytes($"P6\n{width} {height}\n255\n"));
        for (int y = height - 1; y >= 0; y--) stream.Write(pixels.AsSpan(y * width * 3, width * 3));
        using var bitmap = new SKBitmap(width, height);
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            int i = ((height - 1 - y) * width + x) * 3;
            bitmap.SetPixel(x, y, new SKColor(pixels[i], pixels[i + 1], pixels[i + 2], 255));
        }
        using var image = SKImage.FromBitmap(bitmap); using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        using var png = File.Create(Path.ChangeExtension(path, ".png")); encoded.SaveTo(png);
    }
}
