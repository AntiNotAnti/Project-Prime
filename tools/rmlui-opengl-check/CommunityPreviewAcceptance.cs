using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Reflection;
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

            BoundedRows(root);

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
    private static void BoundedRows(string root)
    {
        byte[] landscape = PatternPng(1024, 512);
        byte[] portrait = PatternPng(513, 1025);
        Check(landscape.Length <= 1048576 && portrait.Length <= 1048576,
            "generated actual RGB landscape and portrait PNGs fit the source byte budget");
        using var downloads = new SemaphoreSlim(2, 2);
        using var decodes = new SemaphoreSlim(2, 2);
        // Hold both real decoder permits. Downloads may finish, but no decode
        // or native image publication may occur before the shared gate opens.
        decodes.Wait(); decodes.Wait();
        var source = new HeldSource();
        var entries = Enumerable.Range(0, 8).Select(i => Entry("01234567"[i])).ToArray();
        string directory = Path.Combine(root, "eight-shared-rows");
        var rows = Enumerable.Range(0, 8).Select(i => new NativeCommunityPreview(source,
            Path.Combine(directory, "row-" + i), 384, downloads, decodes)).ToArray();
        bool decodeHeld = true;
        try
        {
            for (int i = 0; i < rows.Length; i++) rows[i].Read(entries[i]);
            Wait(() => source.Requests.Count == 2, "eight rows start only two shared-gate source downloads");
            Thread.Sleep(40);
            Check(source.Requests.Count == 2 && source.PeakActive == 2 && downloads.CurrentCount == 0,
                "held source proves actual maximum two simultaneous downloads across eight rows");
            Pending retired = source.Requests.First();
            int recycled = Array.FindIndex(entries, e => e.LatestHash == retired.Hash);
            entries[recycled] = Entry('f');
            Check(rows[recycled].Read(entries[recycled]).Image.Length == 0 && retired.Token.IsCancellationRequested,
                "reused row cancels its old exact hash and immediately removes stale image");
            retired.Completion.TrySetResult(landscape); // deliberately ignores cancellation
            Wait(() =>
            {
                foreach (var pending in source.Requests) pending.Completion.TrySetResult(landscape);
                for (int i = 0; i < rows.Length; i++) rows[i].Read(entries[i]);
                return source.Requests.Count == 9 && source.Returned == 9
                    && Directory.Exists(directory) && Directory.EnumerateFiles(directory, "*.png", SearchOption.AllDirectories).Count() == 8;
            }, "all current eight hashes finish through two download permits while decoding is queued");
            Check(source.PeakActive <= 2 && !Directory.EnumerateFiles(directory, "*.tga", SearchOption.AllDirectories).Any()
                && rows.Select((row, i) => row.Read(entries[i]).Image.Length).All(length => length == 0),
                "held decode permits prevent all eight thumbnails and canceled old hash never publishes");
            decodes.Release(2); decodeHeld = false;
            string[] images = new string[8];
            Wait(() =>
            {
                for (int i = 0; i < rows.Length; i++) images[i] = rows[i].Read(entries[i]).Image;
                return images.All(image => image.Length > 0);
            }, "eight row thumbnails publish after two shared decoder permits become available");
            Check(source.Requests.Count == 9 && downloads.CurrentCount == 2 && decodes.CurrentCount == 2,
                "completed row work returns every download and decode permit without duplicate reads");
            foreach (string image in images) VerifyThumbnail(image, landscape, 1024, 512);
            for (int i = 0; i < rows.Length; i++)
                Check(rows[i].Read(entries[i]).Status == "PREVIEW OF THE EXACT PUBLISHED REVISION",
                    "row publishes only its current exact package after reuse: " + i);
        }
        finally
        {
            foreach (var row in rows) row.Dispose();
            foreach (var pending in source.Requests) pending.Completion.TrySetResult(landscape);
            if (decodeHeld) decodes.Release(2);
            foreach (var row in rows) WaitRetired(row);
        }
        using (var preview = new NativeCommunityPreview(new FixedSource(portrait), Path.Combine(root, "portrait-card"), 384, downloads, decodes))
        {
            var view = View(preview, Entry('b'), value => value.Image.Length > 0, "portrait row thumbnail becomes ready");
            VerifyThumbnail(view.Image, portrait, 513, 1025);
        }

        using var queuedDownloadGate = new SemaphoreSlim(0, 2);
        using var queuedDecodeGate = new SemaphoreSlim(0, 2);
        var queuedSource = new HeldSource();
        string queuedDirectory = Path.Combine(root, "disposed-download-queue");
        var queued = new NativeCommunityPreview(queuedSource, queuedDirectory, 384, queuedDownloadGate, queuedDecodeGate);
        queued.Read(Entry('c')); queued.Dispose(); WaitRetired(queued);
        Check(queuedSource.Requests.Count == 0 && queued.Read(Entry('c')).Image.Length == 0
            && !Directory.Exists(queuedDirectory), "disposing queued download cancels before source work or cache publication");
        string decodeDirectory = Path.Combine(root, "disposed-decode-queue");
        var queuedDecode = new NativeCommunityPreview(new FixedSource(landscape), decodeDirectory, 384, downloads, queuedDecodeGate);
        queuedDecode.Read(Entry('d'));
        Wait(() => { queuedDecode.Read(Entry('d')); return Directory.Exists(decodeDirectory)
            && Directory.EnumerateFiles(decodeDirectory, "*.png", SearchOption.AllDirectories).Any(); },
            "download completes while decoder waits for a shared permit");
        queuedDecode.Dispose(); WaitRetired(queuedDecode);
        Check(queuedDecode.Read(Entry('d')).Image.Length == 0
            && !Directory.EnumerateFiles(decodeDirectory, "*.tga", SearchOption.AllDirectories).Any()
            && queuedDecodeGate.CurrentCount == 0,
            "disposing queued decode publishes no native image and does not release an unowned permit");
    }

    private static void WaitRetired(NativeCommunityPreview preview)
    {
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        Wait(() =>
        {
            var download = (Task?)typeof(NativeCommunityPreview).GetField("_download", hidden)!.GetValue(preview);
            var cache = typeof(NativeCommunityPreview).GetField("_images", hidden)!.GetValue(preview)!;
            var decode = (Task?)cache.GetType().GetField("_work", hidden)!.GetValue(cache);
            return (download == null || download.IsCompleted) && (decode == null || decode.IsCompleted);
        }, "retired queued download/decoder observes cancellation before gate disposal");
    }

    private static void VerifyThumbnail(string path, byte[] png, int width, int height)
    {
        byte[] tga = File.ReadAllBytes(path);
        int tw = BinaryPrimitives.ReadUInt16LittleEndian(tga.AsSpan(12, 2));
        int th = BinaryPrimitives.ReadUInt16LittleEndian(tga.AsSpan(14, 2));
        double ratio = Math.Min(1, 384d / Math.Max(width, height));
        Check(tw == Math.Max(1, (int)(width * ratio)) && th == Math.Max(1, (int)(height * ratio))
            && Math.Max(tw, th) <= 384 && tga.Length == 18 + tw * th * 4
            && tga[2] == 2 && tga[16] == 32 && tga[17] == 0x28,
            "row TGA is proportional, top-origin RGBA and bounded to 384 pixels / 589824 decoded bytes");
        using var input = new MemoryStream(png, false);
        using StbImage original = StbImage.Load(input, StbiImageFormat.Rgba);
        foreach (var (x, y) in new[] { (0, 0), (tw - 1, 0), (0, th - 1), (tw - 1, th - 1), (tw / 2, th / 2) })
        {
            int source = (y * height / th * width + x * width / tw) * 4;
            byte[] rgba = new byte[4]; Marshal.Copy(IntPtr.Add(original.ImagePointer, source), rgba, 0, 4);
            int dest = 18 + (y * tw + x) * 4;
            Check(tga[dest] == rgba[2] && tga[dest + 1] == rgba[1] && tga[dest + 2] == rgba[0] && tga[dest + 3] == rgba[3],
                "thumbnail sample preserves actual source RGB/alpha at nearest-neighbor coordinate " + x + "," + y);
        }
    }

    private static byte[] PatternPng(int width, int height)
    {
        using var output = new MemoryStream();
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height); header[8] = 8; header[9] = 2;
        Chunk("IHDR", header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            byte[] row = new byte[1 + width * 3];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                { row[1 + x * 3] = (byte)(x / 4); row[2 + x * 3] = (byte)(y / 2); row[3 + x * 3] = (byte)(x + y); }
                zlib.Write(row);
            }
        }
        Chunk("IDAT", compressed.ToArray()); Chunk("IEND", Array.Empty<byte>());
        return output.ToArray();
        void Chunk(string kind, byte[] bytes)
        {
            byte[] name = System.Text.Encoding.ASCII.GetBytes(kind); byte[] number = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(number, bytes.Length); output.Write(number); output.Write(name); output.Write(bytes);
            uint crc = uint.MaxValue;
            foreach (byte value in name.Concat(bytes))
            { crc ^= value; for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0u); }
            BinaryPrimitives.WriteUInt32BigEndian(number, ~crc); output.Write(number);
        }
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
        private int _active, _peakActive;
        internal int PeakActive => Volatile.Read(ref _peakActive);
        public async Task<byte[]?> ReadPreviewAsync(string exactHash, CancellationToken cancellation)
        {
            var completion = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
            int active = Interlocked.Increment(ref _active);
            int peak;
            do { peak = Volatile.Read(ref _peakActive); } while (active > peak && Interlocked.CompareExchange(ref _peakActive, active, peak) != peak);
            Requests.Enqueue(new(exactHash, cancellation, completion));
            try { return await completion.Task.ConfigureAwait(false); } // Deliberately ignores cancellation.
            finally { Interlocked.Decrement(ref _active); Interlocked.Increment(ref Returned); }
        }
    }
    private sealed class FixedSource(byte[] bytes) : INativeCommunityPreviewSource
    {
        internal int Calls;
        public Task<byte[]?> ReadPreviewAsync(string hash, CancellationToken cancellation)
        { Interlocked.Increment(ref Calls); return Task.FromResult<byte[]?>(bytes); }
    }
}
