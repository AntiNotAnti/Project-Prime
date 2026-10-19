using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Pages.Community;
using MphRead.Mods.Launcher.RmlUi.Render;
using ReFuel.Stb;

/// <summary>Headless real-PNG decode with injected synthetic download completions; no GL, HTTP or accounts.</summary>
internal static class CommunityPreviewAcceptance
{
    internal static int Run(string sourcePng)
    {
        byte[] png = File.ReadAllBytes(Path.GetFullPath(sourcePng));
        Check(png.Length <= 1048576 && png.Length >= 33 && png[25] == 2,
            "explicit CLI fixture is a bounded actual RGB PNG");
        RmlUiMapPreviewCache.ReadDimensions(png, out int width, out int height);
        string root = Path.Combine(Path.GetTempPath(), "prime-community-preview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = new HeldSource(); string directory = Path.Combine(root, "switch");
            using (var preview = new NativeCommunityPreview(source, directory))
            {
                var a = Entry('a'); var b = Entry('b');
                preview.Read(a);
                Wait(() => source.Requests.Count == 1, "first exact package request begins");
                var heldA = source.Requests.Single();
                Check(heldA.Hash == a.LatestHash, "preview source receives exact selected A package hash");
                var pendingB = preview.Read(b);
                Check(heldA.Token.IsCancellationRequested && pendingB.Image.Length == 0,
                    "switch to B cancels stalled A and immediately retires its image");
                for (int i = 0; i < 20; i++) preview.Read(b);
                Check(source.Requests.Count == 1, "stalled source A retains one download owner without parallel B requests");
                heldA.Completion.TrySetResult(png); // Fake deliberately ignores cancellation.
                View(preview, b, view => source.Requests.Count == 2 && view.Image.Length == 0,
                    "late canceled A cannot publish and newest B request begins");
                var heldB = source.Requests.Last();
                Check(heldB.Hash == b.LatestHash && (!Directory.Exists(directory)
                    || !Directory.EnumerateFiles(directory, "*.png", SearchOption.AllDirectories).Any()),
                    "late A neither publishes a source cache file nor replaces B identity");
                heldB.Completion.TrySetResult(png);
                var ready = View(preview, b, view => view.Image.Length > 0, "valid B publishes native RGBA texture");
                Check(ready.Status == "PREVIEW OF THE EXACT PUBLISHED REVISION" && File.Exists(ready.Image),
                    "ready status identifies exact published revision");
                VerifyRgba(ready.Image, png, width, height);
                for (int i = 0; i < 40; i++) preview.Read(b);
                Check(source.Requests.Count == 2, "unchanged selected hash is fetched exactly once");
                Check(preview.Read(b with { Installed = true }).Image.Length == 0,
                    "installed local entry retires remote preview without another download");
            }

            Reject(Array.Empty<byte>(), "zero bytes");
            Reject(new byte[1048577], "source exceeds one MiB");
            byte[] malformed = png.ToArray(); malformed[0] = 0; Reject(malformed, "invalid PNG signature");
            malformed = png.ToArray(); BinaryPrimitives.WriteUInt32BigEndian(malformed.AsSpan(8, 4), 12); Reject(malformed, "malformed IHDR size");
            malformed = png.ToArray(); BinaryPrimitives.WriteUInt32BigEndian(malformed.AsSpan(16, 4), 0); Reject(malformed, "zero dimension");
            malformed = png.ToArray(); BinaryPrimitives.WriteUInt32BigEndian(malformed.AsSpan(16, 4), 65536);
            BinaryPrimitives.WriteUInt32BigEndian(malformed.AsSpan(20, 4), 65536); Reject(malformed, "decoded RGBA exceeds sixty-four MiB");
            using (var undecodable = new NativeCommunityPreview(new FixedSource(png[..33]), Path.Combine(root, "decode-failure")))
            {
                var failed = View(undecodable, Entry('c'), view => view.Status.Contains("COULD NOT BE DECODED"),
                    "header-only malformed payload surfaces bounded decode failure", 9000);
                Check(failed.Image.Length == 0, "decoder failure never publishes a native image");
            }

            var disposalSource = new HeldSource(); string disposalDirectory = Path.Combine(root, "disposed");
            var disposed = new NativeCommunityPreview(disposalSource, disposalDirectory);
            disposed.Read(Entry('d')); Wait(() => disposalSource.Requests.Count == 1, "disposal fixture holds a source read");
            var held = disposalSource.Requests.Single(); disposed.Dispose();
            Check(held.Token.IsCancellationRequested, "disposal cancels held source request");
            held.Completion.TrySetResult(png);
            Wait(() => disposalSource.Returned == 1, "fake source returns after disposal");
            var work = (Task?)typeof(NativeCommunityPreview).GetField("_download", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(disposed);
            Wait(() => work == null || work.IsCompleted, "retired download observes cancellation before publication");
            Check(disposed.Read(Entry('d')).Image.Length == 0
                && (!Directory.Exists(disposalDirectory) || !Directory.EnumerateFiles(disposalDirectory, "*", SearchOption.AllDirectories).Any()),
                "disposed preview has no image or cache publication after stale completion");
            Console.WriteLine("COMMUNITY PREVIEW HEADLESS PASS scope=synthetic exact-hash source / actual supplied RGB PNG / no GL or HTTP");
            return 0;

            void Reject(byte[] bytes, string label)
            {
                string target = Path.Combine(root, Guid.NewGuid().ToString("N"));
                var reader = new FixedSource(bytes);
                using var cache = new NativeCommunityPreview(reader, target);
                var view = View(cache, Entry('e'), value => value.Status == "CREATOR PREVIEW IS NOT A SUPPORTED PNG", label + " rejected");
                Check(view.Image.Length == 0 && reader.Calls == 1
                    && (!Directory.Exists(target) || !Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories).Any()),
                    label + " cannot publish source or decoded cache");
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
    private static void VerifyRgba(string path, byte[] png, int width, int height)
    {
        byte[] tga = File.ReadAllBytes(path);
        Check(tga.Length == 18L + width * height * 4 && tga[2] == 2 && tga[16] == 32 && tga[17] == 0x28
            && BinaryPrimitives.ReadUInt16LittleEndian(tga.AsSpan(12, 2)) == width
            && BinaryPrimitives.ReadUInt16LittleEndian(tga.AsSpan(14, 2)) == height,
            "actual RGB PNG expands to complete top-origin thirty-two-bit native TGA");
        using var stream = new MemoryStream(png, false);
        using StbImage original = StbImage.Load(stream, StbiImageFormat.Rgba);
        byte[] rgba = new byte[width * height * 4]; Marshal.Copy(original.ImagePointer, rgba, 0, rgba.Length);
        for (int i = 0; i < rgba.Length; i += 4)
            if (tga[18 + i] != rgba[i + 2] || tga[19 + i] != rgba[i + 1]
                || tga[20 + i] != rgba[i] || tga[21 + i] != rgba[i + 3])
                throw new InvalidOperationException("Native TGA pixel differs from actual source RGBA at " + i / 4);
        Check(true, "every decoded B pixel retains source RGB and expanded alpha");
    }
    private static NativeCommunityEntry Entry(char hash) => new(Guid.Parse("11111111-1111-4111-8111-111111111111"),
        "SYNTHETIC EXACT PACKAGE", "FIXTURE", new string(hash, 64), 1, "Battle", 2, 8);
    private static NativeCommunityPreview.View View(NativeCommunityPreview preview, NativeCommunityEntry entry,
        Func<NativeCommunityPreview.View, bool> predicate, string label, int timeout = 6000)
    {
        NativeCommunityPreview.View result = default;
        Wait(() => { result = preview.Read(entry); return predicate(result); }, label, timeout);
        return result;
    }
    private static void Wait(Func<bool> predicate, string label, int timeout = 6000)
    {
        var timer = Stopwatch.StartNew();
        while (!predicate()) { if (timer.ElapsedMilliseconds > timeout) throw new TimeoutException(label); Thread.Sleep(5); }
        Check(true, label);
    }
    private static void Check(bool pass, string label)
    { if (!pass) throw new InvalidOperationException(label); Console.WriteLine("COMMUNITY PREVIEW HEADLESS PASS " + label); }
    private sealed record Pending(string Hash, CancellationToken Token, TaskCompletionSource<byte[]?> Completion);
    private sealed class HeldSource : INativeCommunityPreviewSource
    {
        internal readonly ConcurrentQueue<Pending> Requests = new();
        internal int Returned;
        public async Task<byte[]?> ReadPreviewAsync(string exactHash, CancellationToken cancellation)
        {
            var completion = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Requests.Enqueue(new(exactHash, cancellation, completion));
            byte[]? bytes = await completion.Task.ConfigureAwait(false); // Deliberately ignores cancellation.
            Interlocked.Increment(ref Returned); return bytes;
        }
    }
    private sealed class FixedSource(byte[] bytes) : INativeCommunityPreviewSource
    {
        internal int Calls;
        public Task<byte[]?> ReadPreviewAsync(string hash, CancellationToken cancellation)
        { Interlocked.Increment(ref Calls); return Task.FromResult<byte[]?>(bytes); }
    }
}
