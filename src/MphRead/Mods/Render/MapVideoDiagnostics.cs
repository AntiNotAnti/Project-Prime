using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MphRead.Mods.MapGen;
using MphRead.Mods.Render.Materials;
using MphRead.Mods.Update;

namespace MphRead.Mods.Render;

/// <summary>Durable stage evidence, including failures before any decoder or GPU texture exists.</summary>
internal static class MapVideoDiagnostics
{
    private sealed record Status(int Binding, string Material, string Source, int Width, int Height,
        string State, string Stage, string Detail, DateTime Utc);
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Status> States = new(StringComparer.Ordinal);
    private static int _revision;
    private static bool _writing;
    private static object _statistics = new { };
    internal static string Summary
    {
        get
        {
            lock (Gate)
                return $"Video diagnostics: {States.Values.Count(s => s.State == "playing")} sampled material bindings, "
                    + $"{States.Values.Count(s => s.State is "failed" or "blocked")} failures/blocked bindings. "
                    + "Details: video-textures-last.json.";
        }
    }

    internal static void Report(Guid scene, int binding, MaterialAssetKey key, MapVideoTexture video,
        string state, string stage, string detail)
    {
        lock (Gate)
        {
            foreach (string old in States.Where(s => s.Value.State == "released").Select(s => s.Key).ToArray()) States.Remove(old);
            States[scene + "/" + binding] = new(binding, key.Value, video.Source, video.Width, video.Height,
                state, stage, detail, DateTime.UtcNow);
            _revision++;
            ScheduleWrite();
        }
        DebugLog.Line("video", $"material={key.Value} source={video.Source} binding={binding} state={state} stage={stage}: {detail}");
    }

    internal static void Statistics(object value)
    {
        lock (Gate) { _statistics = value; _revision++; ScheduleWrite(); }
    }

    internal static void EndScene(Guid scene)
    {
        lock (Gate)
        {
            foreach (string key in States.Keys.Where(k => k.StartsWith(scene + "/", StringComparison.Ordinal)).ToArray())
                States[key] = States[key] with { State = "released", Stage = "disposal", Utc = DateTime.UtcNow };
            _revision++; ScheduleWrite();
        }
    }

    private static void ScheduleWrite()
    {
        if (_writing) return;
        _writing = true;
        _ = Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(200).ConfigureAwait(false);
                int revision; object report;
                lock (Gate)
                {
                    revision = _revision;
                    report = new { build = BuildVersion.Display, executable = Environment.ProcessPath,
                        protocol = Network.NetConfig.ProtocolVersion, backend = "OpenGL",
                        decoder = Path.Combine(AppContext.BaseDirectory, "video", "ffmpeg.exe"),
                        videoEnabled = RenderOptions.VideoTextures, staticHdEnabled = RenderOptions.TextureReplacements,
                        staticTextureTier = RenderOptions.TextureQuality.ToString(),
                        frameMemoryBudgetMiB = RenderOptions.VideoTextureMemoryMiB,
                        materials = States.Values.ToArray(), statistics = _statistics,
                        note = "Playing means a production material draw sampled an uploaded video. This report alone does not measure visual correctness or distinct motion rate." };
                }
                try
                {
                    string directory = Platform.AppPaths.UserDataDirectory;
                    Directory.CreateDirectory(directory);
                    string path = Path.Combine(directory, "video-textures-last.json");
                    string temporary = path + ".tmp";
                    File.WriteAllText(temporary, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
                    File.Move(temporary, path, overwrite: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                { DebugLog.Line("video", "diagnostic report could not be saved: " + ex.Message); }
                lock (Gate) { if (revision == _revision) { _writing = false; return; } }
            }
        });
    }
}
