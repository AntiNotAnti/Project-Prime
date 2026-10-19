using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Render;

/// <summary>Real owned DS portrait decoding and scoped-cache retirement; no GL or global Headless toggle.</summary>
internal static class LicensePortraitAcceptance
{
    internal static int Run(string userRoot)
    {
        Check(Path.IsPathFullyQualified(userRoot), "portrait fixture supplies an absolute private configured root");
        string root = Path.GetFullPath(userRoot), work = Path.Combine(root, "license-portrait-acceptance");
        Check(Directory.Exists(root) && File.Exists(Path.Combine(root, "paths.txt")) && !Directory.Exists(work),
            "portrait fixture has private paths and a fresh acceptance directory");
        Check(!Headless.Active, "actual portrait decode uses existing non-headless tool mode without changing global state");
        Directory.CreateDirectory(work);
        string oldRoot = GameFiles.Root, oldCwd = Directory.GetCurrentDirectory();
        string[] hunters = { "Samus", "Kanden", "Trace", "Sylux", "Noxus", "Spire", "Weavel" };
        string[] models = { "samusmost", "kandenmost", "tracemost", "syluxmost", "noxmost", "spiremost", "weavelmost" };
        var evidence = new List<object>();
        try
        {
            GameFiles.Root = root; Directory.SetCurrentDirectory(root);
            Check(GameFiles.Ready && Path.GetFullPath(Paths.FileSystem).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal),
                "all portrait sources belong to the explicitly private owned-file clone");
            var expected = new Dictionary<string, byte[]>(); var paths = new Dictionary<string, string>();
            var sourceHashes = models.ToDictionary(name => name, name => Sha(Path.Combine(Paths.FileSystem, "gamerscard", name + "_Model.bin")));
            using (var cache = new RmlUiLicenseArtworkCache())
            {
                for (int h = 0; h < hunters.Length; h++)
                {
                    Model model;
                    using (var lease = GameFiles.TryUseConfiguredFiles(root) ?? throw new InvalidOperationException("Private portrait source lease unavailable"))
                        model = Read.GetModelInstance(models[h], dir: MetaDir.GamersCard, noCache: true).Model;
                    Check(model.Meshes.Count == 1 && model.Materials.Count == 1 && model.Recolors.Count == 1,
                        hunters[h] + " original portrait uses one mesh material and default recolor");
                    var material = model.Materials[0]; var recolor = model.Recolors[0];
                    var texture = recolor.Textures[material.TextureId]; var pixels = recolor.GetPixels(material.TextureId, material.PaletteId);
                    Check(texture.Width == 32 && texture.Height == 32 && pixels.Count == 1024,
                        hunters[h] + " owned source decodes to original thirty-two square pixels");
                    string result = Wait(cache, hunters[h]); byte[] tga = File.ReadAllBytes(result);
                    Check(tga.Length == 4114 && tga[2] == 2 && tga[12] == 32 && tga[13] == 0
                        && tga[14] == 32 && tga[15] == 0 && tga[16] == 32 && tga[17] == 0x28,
                        hunters[h] + " native cache is bounded top-origin thirty-two-bit TGA with alpha");
                    for (int i = 0; i < pixels.Count; i++)
                    {
                        var p = pixels[i]; int offset = 18 + i * 4;
                        if (tga[offset] != p.Blue || tga[offset + 1] != p.Green || tga[offset + 2] != p.Red || tga[offset + 3] != p.Alpha)
                            throw new InvalidDataException(hunters[h] + " native pixel mismatch at " + i);
                    }
                    Check(true, hunters[h] + " all decoded RGB and native alpha bytes exactly match the owned source");
                    Check(Path.GetFileNameWithoutExtension(result) == Sha(result)
                        && Path.GetFullPath(result).StartsWith(Path.Combine(root, "rmlui-media", "license-portraits") + Path.DirectorySeparatorChar, StringComparison.Ordinal),
                        hunters[h] + " cache key is the exact native pixel digest inside private user data");
                    paths[models[h]] = result; expected[models[h]] = tga;
                    evidence.Add(new { hunter = hunters[h], model = models[h], sourceSha256 = sourceHashes[models[h]], result,
                        nativeSha256 = Sha(result), pixels = pixels.Count, alphaValues = pixels.Select(p => p.Alpha).Distinct().Order().ToArray() });
                }
                Check(cache.Read("Random") == "" && cache.Read("../samusmost") == "",
                    "unknown hunters and path-like input cannot expose arbitrary owned assets");
            }
            using (var warm = new RmlUiLicenseArtworkCache())
                Check(Wait(warm, "Samus") == paths["samusmost"], "new cache instance reuses only exact content-addressed source pixels");
            var before = Inventory(Path.Combine(root, "rmlui-media", "license-portraits"));
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                int active = 0, maximum = 0;
                byte[] Decode(string configured, string model, CancellationToken token)
                {
                    int count = Interlocked.Increment(ref active); maximum = Math.Max(maximum, count);
                    try { if (model == "samusmost") { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException(); } return expected[model]; }
                    finally { Interlocked.Decrement(ref active); }
                }
                using var cache = new RmlUiLicenseArtworkCache(decode: Decode);
                Check(cache.Read("Samus") == "" && entered.Wait(TimeSpan.FromSeconds(5)), "held portrait decode starts without blocking UI owner");
                var requestClock = Stopwatch.StartNew(); string switched = cache.Read("Trace");
                Check(switched == "" && requestClock.Elapsed < TimeSpan.FromMilliseconds(500),
                    "switching hunter retires old portrait without waiting for held decoder");
                release.Set();
                Check(Wait(cache, "Trace") == paths["tracemost"] && maximum == 1,
                    "late prior hunter cannot replace current portrait and each instance decodes serially");
            }
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            using (var returned = new ManualResetEventSlim())
            {
                string selectedRoot = root;
                byte[] Decode(string configured, string model, CancellationToken token)
                { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException(); returned.Set(); return expected[model]; }
                using var cache = new RmlUiLicenseArtworkCache(() => selectedRoot, Decode);
                cache.Read("Samus"); Check(entered.Wait(TimeSpan.FromSeconds(5)), "root-retirement fixture holds old configured source work");
                selectedRoot = Path.Combine(root, "retired-root");
                Check(cache.Read("Samus") == "", "configured root change immediately retires old portrait");
                release.Set(); Check(returned.Wait(TimeSpan.FromSeconds(5)), "retired root source returns after request cancellation");
                for (int i = 0; i < 30; i++) { Check(cache.Read("Samus") == "", "retired configured root cannot publish old image"); Thread.Sleep(2); }
            }
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            using (var returned = new ManualResetEventSlim())
            {
                byte[] Decode(string configured, string model, CancellationToken token)
                { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException(); returned.Set(); return expected[model]; }
                var cache = new RmlUiLicenseArtworkCache(decode: Decode); cache.Read("Spire");
                Check(entered.Wait(TimeSpan.FromSeconds(5)), "dispose fixture holds a portrait source read");
                var disposeClock = Stopwatch.StartNew(); cache.Dispose();
                Check(disposeClock.Elapsed < TimeSpan.FromMilliseconds(500), "owner disposal returns while source decoder remains held");
                release.Set(); Check(returned.Wait(TimeSpan.FromSeconds(5)), "held source returns after cache disposal");
                Check(cache.Read("Spire") == "", "disposed cache cannot expose a late portrait completion");
                Thread.Sleep(40);
            }
            Check(before.SequenceEqual(Inventory(Path.Combine(root, "rmlui-media", "license-portraits"))),
                "retired and disposed workers never rewrite or add cached artwork");
            Check(models.All(name => sourceHashes[name] == Sha(Path.Combine(Paths.FileSystem, "gamerscard", name + "_Model.bin"))),
                "all seven owned game files remain byte-identical after decoding and cache retirement");
            Save(true, null); return 0;
        }
        catch (Exception error) { Save(false, error.ToString()); throw; }
        finally { GameFiles.Root = oldRoot; Directory.SetCurrentDirectory(oldCwd); }
        void Save(bool passed, string? error) => File.WriteAllText(Path.Combine(work, "summary.json"), JsonSerializer.Serialize(new
        { passed, scope = "Actual private owned GamersCard source pixels to native TGA; seven portraits only; injected held decoder for retirement, no GL/global Headless mutation/game assets bundled", root, evidence, error }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static string Wait(RmlUiLicenseArtworkCache cache, string hunter)
    {
        var timer = Stopwatch.StartNew();
        do { string path = cache.Read(hunter); if (path.Length != 0) return path; Thread.Sleep(5); } while (timer.Elapsed < TimeSpan.FromSeconds(10));
        throw new TimeoutException("Portrait did not become available for " + hunter);
    }
    private static string Sha(string path)
    { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
    private static string[] Inventory(string path) => Directory.EnumerateFiles(path).OrderBy(p => p, StringComparer.Ordinal)
        .Select(p => Path.GetFileName(p) + ":" + Sha(p) + ":" + File.GetLastWriteTimeUtc(p).Ticks).ToArray();
    private static void Check(bool condition, string label)
    { if (!condition) throw new InvalidOperationException(label); Console.WriteLine("LICENSE PORTRAIT PASS " + label); }
}
