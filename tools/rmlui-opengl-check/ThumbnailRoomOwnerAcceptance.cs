using System.Diagnostics;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.Launcher;

/// <summary>Real room rendering in the actual client worker, through the existing exact-room thumbnail owner.</summary>
internal static class ThumbnailRoomOwnerAcceptance
{
    internal static int Run(string[] args)
    {
        string engine = Path.GetFullPath(args[1]), room = args[2];
        string root = Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA") ?? "";
        if (!Path.IsPathFullyQualified(root) || !File.Exists(Path.Combine(root, "paths.txt"))
            || !File.Exists(engine) || !engine.EndsWith("ProjectPrime.dll", StringComparison.Ordinal))
            throw new ArgumentException("Supply an actual compiled client and explicit private configured userdata.");
        Directory.SetCurrentDirectory(root);
        GameFiles.Root = root;
        Check(GameFiles.Ready && !ThumbnailGenerator.Exists(room), "private real-game fixture begins with no selected-room thumbnail");
        var previous = ThumbnailHost.Current;
        var worker = new ActualClientWorker(engine, room, root);
        try
        {
            ThumbnailHost.Current = worker;
            int rendered = Task.Run(() => ThumbnailHost.RenderRoomAsync(room, root, Console.WriteLine)).GetAwaiter().GetResult();
            Check(rendered == 1 && worker.Invocations == 1 && ThumbnailGenerator.Exists(room),
                "existing exact-room owner produces only the selected room using the actual client renderer");
            byte[] png = File.ReadAllBytes(ThumbnailGenerator.PathFor(room));
            MphRead.Mods.Launcher.RmlUi.Render.RmlUiMapPreviewCache.ReadDimensions(png, out int width, out int height);
            Check(width > 0 && height > 0 && png.Length > 1000, "actual rendered room output is a nonempty bounded PNG");
            int skipped = Task.Run(() => ThumbnailHost.RenderRoomAsync(room, root, Console.WriteLine)).GetAwaiter().GetResult();
            Check(skipped == 0 && worker.Invocations == 1, "existing PNG prevents another rendering request");
            var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try
            {
                ThumbnailHost.RenderRoomAsync(room, root, Console.WriteLine, cancelled.Token).GetAwaiter().GetResult();
                throw new InvalidOperationException("Cancelled room request was admitted.");
            }
            catch (OperationCanceledException) { Check(worker.Invocations == 1, "pre-cancelled room request never reaches the renderer"); }
            try
            {
                ThumbnailHost.RenderRoomAsync(room, root + ".retired", Console.WriteLine).GetAwaiter().GetResult();
                throw new InvalidOperationException("Retired setup root was admitted.");
            }
            catch (InvalidOperationException error) when (error.Message == GameFiles.SetupWorking)
            { Check(worker.Invocations == 1, "retired setup identity never reaches the renderer"); }
            Console.WriteLine("THUMBNAIL ROOM OWNER PASS " + ThumbnailGenerator.PathFor(room));
            return 0;
        }
        finally { ThumbnailHost.Current = previous; }
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        Console.WriteLine("THUMBNAIL ROOM OWNER PASS " + name);
    }

    private sealed class ActualClientWorker(string engine, string expectedRoom, string root) : IThumbnailHost
    {
        internal int Invocations;
        public async Task<int> RenderAsync(IReadOnlyList<string> rooms, Action<string> report)
        {
            Check(rooms.Count == 1 && rooms[0] == expectedRoom, "exact room list reaches the injected worker transport");
            Invocations++;
            var info = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath!, WorkingDirectory = root,
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
            };
            info.ArgumentList.Add(engine);
            info.ArgumentList.Add("-thumbnail"); info.ArgumentList.Add(expectedRoom);
            info.ArgumentList.Add("-size"); info.ArgumentList.Add("960x540");
            info.Environment["PROJECT_PRIME_USER_DATA"] = root;
            info.Environment["PROJECT_PRIME_RMLUI_GL_SMOKE"] = "0";
            using var process = Process.Start(info) ?? throw new InvalidOperationException("Client renderer did not start.");
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(180));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch { if (!process.HasExited) process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); throw; }
            string output = await stdout, errors = await stderr;
            File.WriteAllText(Path.Combine(root, "actual-thumbnail-worker.stdout.log"), output);
            File.WriteAllText(Path.Combine(root, "actual-thumbnail-worker.stderr.log"), errors);
            Check(process.ExitCode == 0 && output.Contains("captured 1/1", StringComparison.Ordinal),
                "actual client worker reports one captured room and exits successfully");
            return ThumbnailGenerator.Exists(expectedRoom) ? 1 : 0;
        }
    }
}
