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

/// <summary>Real owned DS star artwork decoding and scoped-cache retirement; no GL or global Headless toggle.</summary>
internal static class LicenseStarArtAcceptance
{
    internal static int Run(string userRoot)
    {
        Check(Path.IsPathFullyQualified(userRoot), "star artwork fixture supplies an absolute private configured root");
        string root = Path.GetFullPath(userRoot), work = Path.Combine(root, "license-star-art-acceptance");
        Check(Directory.Exists(root) && File.Exists(Path.Combine(root, "paths.txt")) && !Directory.Exists(work),
            "star artwork fixture has private paths and a fresh acceptance directory");
        Check(!Headless.Active, "actual star artwork decode uses existing non-headless tool mode without changing global state");
        Directory.CreateDirectory(work);
        string oldRoot = GameFiles.Root, oldCwd = Directory.GetCurrentDirectory();
        string[] ranks = { "1", "2", "3", "4", "5" };
        string[] models = { "redstar", "bronzestar", "silverstar", "goldstar", "blackstar" };
        var evidence = new List<object>();
        try
        {
            GameFiles.Root = root; Directory.SetCurrentDirectory(root);
            Check(GameFiles.Ready && Path.GetFullPath(Paths.FileSystem).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal),
                "all star artwork sources belong to the explicitly private owned-file clone");
            var expected = new Dictionary<string, byte[]>(); var paths = new Dictionary<string, string>();
            var sourceHashes = models.ToDictionary(name => name, name => Sha(Path.Combine(Paths.FileSystem, "gamerscard", name + "_Model.bin")));
            using (var cache = new RmlUiLicenseArtworkCache(rankArtwork: true))
            {
                for (int h = 0; h < ranks.Length; h++)
                {
                    Model model;
                    using (var lease = GameFiles.TryUseConfiguredFiles(root) ?? throw new InvalidOperationException("Private star artwork source lease unavailable"))
                        model = Read.GetModelInstance(models[h], dir: MetaDir.GamersCard, noCache: true).Model;
                    Check(model.Meshes.Count == 1 && model.Materials.Count == 1 && model.Recolors.Count == 1,
                        ranks[h] + " original star artwork uses one mesh material and default recolor");
                    var material = model.Materials[0]; var recolor = model.Recolors[0];
                    var texture = recolor.Textures[material.TextureId]; var pixels = recolor.GetPixels(material.TextureId, material.PaletteId);
                    Check(texture.Width == 32 && texture.Height == 32 && pixels.Count == 1024,
                        ranks[h] + " owned source decodes to original thirty-two square pixels");
                    if (h == 4)
                    {
                        var crop = Enumerable.Range(0, 324).Select(i => pixels[(i / 18) * 32 + i % 18]).ToArray();
                        Check(crop.Any(p => p.Red == 0 && p.Green == 0 && p.Blue == 0 && p.Alpha > 0)
                            && crop.Any(p => p.Alpha == 0), "black rank preserves visible black and transparent source pixels");
                    }
                    string result = Wait(cache, ranks[h]); byte[] tga = File.ReadAllBytes(result);
                    Check(tga.Length == 1314 && tga[2] == 2 && tga[12] == 18 && tga[13] == 0
                        && tga[14] == 18 && tga[15] == 0 && tga[16] == 32 && tga[17] == 0x28,
                        ranks[h] + " native cache is bounded top-origin eighteen square thirty-two-bit TGA with alpha");
                    for (int i = 0; i < 18 * 18; i++)
                    {
                        var p = pixels[(i / 18) * 32 + i % 18]; int offset = 18 + i * 4;
                        if (tga[offset] != p.Blue || tga[offset + 1] != p.Green || tga[offset + 2] != p.Red || tga[offset + 3] != p.Alpha)
                            throw new InvalidDataException(ranks[h] + " native pixel mismatch at " + i);
                    }
                    Check(true, ranks[h] + " all decoded RGB and native alpha bytes exactly match the owned source");
                    Check(Path.GetFileNameWithoutExtension(result) == Sha(result)
                        && Path.GetFullPath(result).StartsWith(Path.Combine(root, "rmlui-media", "license-stars") + Path.DirectorySeparatorChar, StringComparison.Ordinal),
                        ranks[h] + " cache key is the exact native pixel digest inside private user data");
                    paths[models[h]] = result; expected[models[h]] = tga;
                    evidence.Add(new { rank = ranks[h], model = models[h], sourceSha256 = sourceHashes[models[h]], result,
                        nativeSha256 = Sha(result), pixels = pixels.Count, alphaValues = pixels.Select(p => p.Alpha).Distinct().Order().ToArray() });
                }
                Check(cache.Read("Random") == "" && cache.Read("../redstar") == "" && cache.Read("0") == "" && cache.Read("6") == "",
                    "unknown ranks and path-like input cannot expose arbitrary owned assets");
            }
            using (var warm = new RmlUiLicenseArtworkCache(rankArtwork: true))
                Check(Wait(warm, "1") == paths["redstar"], "new cache instance reuses only exact content-addressed source pixels");
            var before = Inventory(Path.Combine(root, "rmlui-media", "license-stars"));
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                int active = 0, maximum = 0;
                byte[] Decode(string configured, string model, CancellationToken token)
                {
                    int count = Interlocked.Increment(ref active); maximum = Math.Max(maximum, count);
                    try { if (model == "redstar") { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException(); } return expected[model]; }
                    finally { Interlocked.Decrement(ref active); }
                }
                using var cache = new RmlUiLicenseArtworkCache(decode: Decode, rankArtwork: true);
                Check(cache.Read("1") == "" && entered.Wait(TimeSpan.FromSeconds(5)), "held star artwork decode starts without blocking UI owner");
                var requestClock = Stopwatch.StartNew(); string switched = cache.Read("3");
                Check(switched == "" && requestClock.Elapsed < TimeSpan.FromMilliseconds(500),
                    "switching rank retires old star artwork without waiting for held decoder");
                release.Set();
                Check(Wait(cache, "3") == paths["silverstar"] && maximum == 1,
                    "late prior rank cannot replace current star artwork and each instance decodes serially");
            }
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            using (var returned = new ManualResetEventSlim())
            {
                string selectedRoot = root;
                byte[] Decode(string configured, string model, CancellationToken token)
                { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException(); returned.Set(); return expected[model]; }
                using var cache = new RmlUiLicenseArtworkCache(() => selectedRoot, Decode, rankArtwork: true);
                cache.Read("1"); Check(entered.Wait(TimeSpan.FromSeconds(5)), "root-retirement fixture holds old configured source work");
                selectedRoot = Path.Combine(root, "retired-root");
                Check(cache.Read("1") == "", "configured root change immediately retires old star artwork");
                release.Set(); Check(returned.Wait(TimeSpan.FromSeconds(5)), "retired root source returns after request cancellation");
                for (int i = 0; i < 30; i++) { Check(cache.Read("1") == "", "retired configured root cannot publish old image"); Thread.Sleep(2); }
            }
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            using (var returned = new ManualResetEventSlim())
            {
                byte[] Decode(string configured, string model, CancellationToken token)
                { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException(); returned.Set(); return expected[model]; }
                var cache = new RmlUiLicenseArtworkCache(decode: Decode, rankArtwork: true); cache.Read("4");
                Check(entered.Wait(TimeSpan.FromSeconds(5)), "dispose fixture holds a star artwork source read");
                var disposeClock = Stopwatch.StartNew(); cache.Dispose();
                Check(disposeClock.Elapsed < TimeSpan.FromMilliseconds(500), "owner disposal returns while source decoder remains held");
                release.Set(); Check(returned.Wait(TimeSpan.FromSeconds(5)), "held source returns after cache disposal");
                Check(cache.Read("4") == "", "disposed cache cannot expose a late star artwork completion");
                Thread.Sleep(40);
            }
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            using (var returned = new ManualResetEventSlim())
            {
                byte[] Decode(string configured, string model, CancellationToken token)
                { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException(); returned.Set(); return expected[model]; }
                using var cache = new RmlUiLicenseArtworkCache(decode: Decode, rankArtwork: true);
                cache.Read("5"); Check(entered.Wait(TimeSpan.FromSeconds(5)), "empty-rank fixture holds source work");
                Check(cache.Read("") == "", "empty rank retires pending artwork");
                release.Set(); Check(returned.Wait(TimeSpan.FromSeconds(5)), "retired empty-rank decoder returns");
                for (int i = 0; i < 30; i++) { Check(cache.Read("") == "", "empty rank cannot publish old image"); Thread.Sleep(2); }
            }
            Check(before.SequenceEqual(Inventory(Path.Combine(root, "rmlui-media", "license-stars"))),
                "retired and disposed workers never rewrite or add cached artwork");
            Check(models.All(name => sourceHashes[name] == Sha(Path.Combine(Paths.FileSystem, "gamerscard", name + "_Model.bin"))),
                "all five owned game files remain byte-identical after decoding and cache retirement");
            Save(true, null); return 0;
        }
        catch (Exception error) { Save(false, error.ToString()); throw; }
        finally { GameFiles.Root = oldRoot; Directory.SetCurrentDirectory(oldCwd); }
        void Save(bool passed, string? error) => File.WriteAllText(Path.Combine(work, "summary.json"), JsonSerializer.Serialize(new
        { passed, scope = "Actual private owned GamersCard source pixels to native TGA; five star rows only; injected held decoder for retirement, no GL/global Headless mutation/game assets bundled", root, evidence, error }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static string Wait(RmlUiLicenseArtworkCache cache, string rank)
    {
        var timer = Stopwatch.StartNew();
        do { string path = cache.Read(rank); if (path.Length != 0) return path; Thread.Sleep(5); } while (timer.Elapsed < TimeSpan.FromSeconds(10));
        throw new TimeoutException("Star artwork did not become available for " + rank);
    }
    private static string Sha(string path)
    { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
    private static string[] Inventory(string path) => Directory.EnumerateFiles(path).OrderBy(p => p, StringComparer.Ordinal)
        .Select(p => Path.GetFileName(p) + ":" + Sha(p) + ":" + File.GetLastWriteTimeUtc(p).Ticks).ToArray();
    private static void Check(bool condition, string label)
    { if (!condition) throw new InvalidOperationException(label); Console.WriteLine("LICENSE STAR ART PASS " + label); }
}
