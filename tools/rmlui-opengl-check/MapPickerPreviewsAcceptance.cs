using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Render;
using ReFuel.Stb;

/// <summary>Headless injected thumbnail work and real PNG decoders; no GL, HTTP, engine render or owned-asset writes.</summary>
internal static class MapPickerPreviewsAcceptance
{
    internal static int Run(string sourcePng)
    {
        string supplied = Path.GetFullPath(sourcePng); byte[] real = File.ReadAllBytes(supplied);
        string sourceHash = Hash(real);
        RmlUiMapPreviewCache.ReadDimensions(real, out _, out _);
        string temporary = Path.Combine(Path.GetTempPath(), "prime-map-picker-previews-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            DecodeSixAndReorder(); SerialRenderAndPageChange(); RootChange(); DisposeHeldRender();
            Check(Hash(File.ReadAllBytes(supplied)) == sourceHash, "supplied real PNG remains read-only and unchanged");
            Console.WriteLine("MAP PICKER PREVIEWS HEADLESS PASS scope=temp PNGs / real decoders / injected held rendering / no GL or HTTP");
            return 0;
        }
        finally { Directory.Delete(temporary, recursive: true); }

        void DecodeSixAndReorder()
        {
            string root = Path.Combine(temporary, "six"); var renderer = new Renderer();
            string[] rooms = Enumerable.Range(0, 6).Select(i => "ROOM" + i).ToArray();
            byte[][] pngs = rooms.Select((_, i) => i == 0 ? real : Solid((byte)(i * 29), (byte)(220 - i * 17), (byte)(i * 33))).ToArray();
            for (int i = 0; i < rooms.Length; i++) Write(Source(root, rooms[i]), pngs[i]);
            var observed = new ConcurrentQueue<string>(); int owner = Environment.CurrentManagedThreadId;
            using var previews = new RmlUiMapPickerPreviews(() => root, room =>
            { CheckOwner(owner); observed.Enqueue(room); return Source(root, room); }, renderer.Render);
            Check(previews.Read(rooms.Append("SEVENTH").ToArray()).Length == 6, "visible card requests cap to six decoder slots");
            var ready = ReadUntil(previews, rooms, images => images.All(i => i.Image.Length > 0), "all six existing PNG cards decode asynchronously");
            for (int i = 0; i < 6; i++) Verify(ready[i].Image, pngs[i]);
            Check(renderer.Requests.Count == 0 && !observed.Contains("SEVENTH"), "existing previews never render and hidden seventh card is untouched");
            string[] swapped = { rooms[5], rooms[2], rooms[0], rooms[4], rooms[1], rooms[3] };
            var first = previews.Read(swapped);
            VerifyVisible(first, swapped, room => pngs[Array.IndexOf(rooms, room)]);
            var reordered = ReadUntil(previews, swapped, images => images.All(i => i.Image.Length > 0), "page reorder resolves six current room identities");
            VerifyVisible(reordered, swapped, room => pngs[Array.IndexOf(rooms, room)]);
            Check(previews.Read(Array.Empty<string>()).Length == 0, "empty page retires every visible card slot");
        }
        void SerialRenderAndPageChange()
        {
            string root = Path.Combine(temporary, "serial"); var renderer = new Renderer();
            using var previews = new RmlUiMapPickerPreviews(() => root, room => Source(root, room), renderer.Render);
            previews.Read(new[] { "OLD-A" }); Wait(() => renderer.Requests.Count == 1, "missing first thumbnail starts injected render");
            var old = renderer.Requests.Single(); var current = new[] { "B0", "B1" };
            var changed = previews.Read(current);
            Check(old.Token.IsCancellationRequested && changed.All(i => i.Image.Length == 0), "page change cancels old rendering and clears old slot images");
            for (int i = 0; i < 30; i++) Check(previews.Read(current).All(p => p.Image.Length == 0), "held old render cannot appear in new page slots");
            Check(renderer.Requests.Count == 1 && renderer.MaximumActive == 1, "held canceled request keeps rendering serialized until it retires");
            old.Completion.TrySetResult(Solid(240, 20, 20)); // Deliberately late and ignores cancellation.
            ReadUntil(previews, current, _ => renderer.Requests.Count == 2, "new page render begins only after old render completion");
            var b0 = renderer.Requests.Last(); Check(b0.Room == "B0" && b0.Root == root, "render callback captures current room and root");
            byte[] blue = Solid(20, 60, 240), green = Solid(20, 230, 50);
            b0.Completion.TrySetResult(blue);
            ReadUntil(previews, current, _ => renderer.Requests.Count == 3, "second visible missing card renders after first retires");
            var b1 = renderer.Requests.Last(); Check(b1.Room == "B1", "serialized renderer advances to next current card");
            b1.Completion.TrySetResult(green);
            var ready = ReadUntil(previews, current, a => a.All(p => p.Image.Length > 0), "new page cards receive their own completed PNG images");
            Verify(ready[0].Image, blue); Verify(ready[1].Image, green);
            Check(renderer.MaximumActive == 1 && renderer.Requests.Count == 3, "all page transitions retain at most one thumbnail render");
            previews.Read(new[] { "FAILED" }); Wait(() => renderer.Requests.Count == 4, "missing next page starts one render attempt");
            renderer.Requests.Last().Completion.TrySetResult(null);
            ReadUntil(previews, new[] { "FAILED" }, a => a[0].Status == "PREVIEW UNAVAILABLE", "failed render has explicit unavailable placeholder");
            for (int i = 0; i < 30; i++) previews.Read(new[] { "FAILED" });
            Check(renderer.Requests.Count == 4, "failed visible thumbnail does not restart rendering every frame");
        }
        void RootChange()
        {
            string oldRoot = Path.Combine(temporary, "root-old"), root = oldRoot; var renderer = new Renderer();
            using var previews = new RmlUiMapPickerPreviews(() => root, room => Source(root, room), renderer.Render);
            string[] rooms = { "SAME-ROOM" }; previews.Read(rooms);
            Wait(() => renderer.Requests.Count == 1, "old root holds synthetic render"); var old = renderer.Requests.Single();
            root = Path.Combine(temporary, "root-new"); byte[] blue = Solid(15, 80, 230); Write(Source(root, rooms[0]), blue);
            var changed = previews.Read(rooms);
            Check(old.Token.IsCancellationRequested && changed.All(p => p.Image.Length == 0), "root change cancels old work and discards old decoder ownership");
            var ready = ReadUntil(previews, rooms, a => a[0].Image.Length > 0, "new root decodes existing current image despite held old render");
            Verify(ready[0].Image, blue);
            Check(Path.GetFullPath(ready[0].Image).StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.Ordinal),
                "new root owns the published decoder cache path");
            old.Completion.TrySetResult(Solid(245, 10, 10));
            Wait(() => renderer.Returned == 1, "old root render deliberately completes after cancellation");
            for (int i = 0; i < 30; i++) Verify(previews.Read(rooms)[0].Image, blue);
            Check(renderer.Requests.Count == 1 && renderer.MaximumActive == 1 && File.Exists(Source(oldRoot, rooms[0])),
                "late old-root source cannot overwrite new root card identity or launch extra rendering");
        }
        void DisposeHeldRender()
        {
            string root = Path.Combine(temporary, "disposed"); var renderer = new Renderer();
            var previews = new RmlUiMapPickerPreviews(() => root, room => Source(root, room), renderer.Render);
            string[] rooms = { "RETIRED" }; previews.Read(rooms); Wait(() => renderer.Requests.Count == 1, "disposal fixture holds thumbnail render");
            var held = renderer.Requests.Single(); previews.Dispose();
            Check(held.Token.IsCancellationRequested && previews.Read(rooms).Length == 0, "disposal cancels source and permanently retires card array");
            held.Completion.TrySetResult(Solid(180, 20, 160));
            Wait(() => renderer.Returned == 1, "fake source returns after wrapper disposal");
            var work = (Task?)typeof(RmlUiMapPickerPreviews).GetField("_work", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(previews);
            Wait(() => work == null || work.IsCompleted, "retired render task settles without UI callback");
            string decoded = Path.Combine(root, "rmlui-media", "map-previews");
            Check(previews.Read(rooms).Length == 0 && (!Directory.Exists(decoded) || !Directory.EnumerateFiles(decoded, "*.tga").Any()),
                "late disposed render cannot publish a decoded image or regain visible slots");
            previews.Dispose();
        }
    }
    private static RmlUiArenaPreview[] ReadUntil(RmlUiMapPickerPreviews previews, string[] rooms,
        Func<RmlUiArenaPreview[], bool> predicate, string label)
    { RmlUiArenaPreview[] result = Array.Empty<RmlUiArenaPreview>(); Wait(() => { result = previews.Read(rooms); return predicate(result); }, label); return result; }
    private static void VerifyVisible(RmlUiArenaPreview[] images, string[] rooms, Func<string, byte[]> expected)
    { for (int i = 0; i < images.Length; i++) if (images[i].Image.Length > 0) Verify(images[i].Image, expected(rooms[i])); }
    private static void Verify(string image, byte[] png)
    {
        Check(image.Length > 0 && File.Exists(image), "published slot resolves a real native texture file");
        RmlUiMapPreviewCache.ReadDimensions(png, out int width, out int height);
        byte[] tga = File.ReadAllBytes(image); Check(tga.Length == 18 + width * height * 4 && tga[16] == 32 && tga[17] == 0x28,
            "current source decodes to complete top-origin RGBA texture");
        using var stream = new MemoryStream(png, false); using StbImage original = StbImage.Load(stream, StbiImageFormat.Rgba);
        byte[] rgba = new byte[width * height * 4]; Marshal.Copy(original.ImagePointer, rgba, 0, rgba.Length);
        for (int i = 0; i < rgba.Length; i += 4)
            if (tga[18 + i] != rgba[i + 2] || tga[19 + i] != rgba[i + 1] || tga[20 + i] != rgba[i] || tga[21 + i] != rgba[i + 3])
                throw new InvalidOperationException("Stale or wrong-slot PNG pixels published at pixel " + i / 4);
    }
    private static string Source(string root, string room) => Path.Combine(root, "thumbnails", room + ".png");
    private static void Write(string path, byte[] bytes) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes); }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static void CheckOwner(int owner) { if (Environment.CurrentManagedThreadId != owner) throw new InvalidOperationException("Source path callback escaped read owner"); }
    private static void Wait(Func<bool> predicate, string label)
    { var timer = Stopwatch.StartNew(); while (!predicate()) { if (timer.ElapsedMilliseconds > 6000) throw new TimeoutException(label); Thread.Sleep(5); } Check(true, label); }
    private static void Check(bool pass, string label)
    { if (!pass) throw new InvalidOperationException(label); Console.WriteLine("MAP PICKER PREVIEWS HEADLESS PASS " + label); }
    private sealed record Request(string Room, string Root, CancellationToken Token, TaskCompletionSource<byte[]?> Completion);
    private sealed class Renderer
    {
        internal readonly ConcurrentQueue<Request> Requests = new(); internal int Active, MaximumActive, Returned;
        internal async Task<int> Render(string room, string root, CancellationToken token)
        {
            int active = Interlocked.Increment(ref Active); int seen;
            do { seen = Volatile.Read(ref MaximumActive); if (seen >= active) break; } while (Interlocked.CompareExchange(ref MaximumActive, active, seen) != seen);
            var completion = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously); Requests.Enqueue(new(room, root, token, completion));
            try
            {
                byte[]? result = await completion.Task.ConfigureAwait(false); // Deliberately ignores cancellation.
                if (result != null) Write(Source(root, room), result);
                Interlocked.Increment(ref Returned); return result == null ? 1 : 0;
            }
            finally { Interlocked.Decrement(ref Active); }
        }
    }
    // Tiny distinct RGBA fixtures let wrong-slot publication be detected by pixels.
    private static byte[] Solid(byte red, byte green, byte blue)
    {
        using var png = new MemoryStream(); png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        byte[] header = new byte[13]; BinaryPrimitives.WriteInt32BigEndian(header, 1); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), 1); header[8] = 8; header[9] = 6;
        Chunk("IHDR", header);
        using var compressed = new MemoryStream(); using (var z = new ZLibStream(compressed, CompressionLevel.SmallestSize, true)) z.Write(new byte[] { 0, red, green, blue, 255 });
        Chunk("IDAT", compressed.ToArray()); Chunk("IEND", Array.Empty<byte>()); return png.ToArray();
        void Chunk(string tag, byte[] data)
        {
            Span<byte> number = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(number, data.Length); png.Write(number);
            byte[] type = System.Text.Encoding.ASCII.GetBytes(tag); png.Write(type); png.Write(data); uint crc = 0xffffffff;
            foreach (byte value in type.Concat(data)) { crc ^= value; for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0); }
            BinaryPrimitives.WriteUInt32BigEndian(number, ~crc); png.Write(number);
        }
    }
}
