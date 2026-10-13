using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using ReFuel.Stb;
using MphRead.Mods;

// Run only as an isolated child: the baseline intentionally reproduces a native
// callback-on-collected-delegate fatal error. No GL, audio or game references.
if (args.Length != 2 || args[0] is not ("baseline" or "candidate")) throw new ArgumentException("baseline|candidate output-directory");
bool baseline = args[0] == "baseline";
string output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
const int width = 2048, height = 1024;
byte[] pixels = new byte[width * height * 4];
uint random = 12345;
for (int i = 0; i < pixels.Length; i++)
{ random ^= random << 13; random ^= random >> 17; random ^= random << 5; pixels[i] = (byte)random; }
int stop = 0, collections = 0;
var pressure = new Thread(() =>
{
    while (Volatile.Read(ref stop) == 0)
    { GC.Collect(2, GCCollectionMode.Forced, blocking: true); Interlocked.Increment(ref collections); Thread.Yield(); }
}) { IsBackground = true };
Console.WriteLine($"PNGCALLBACK BEGIN mode={args[0]} runtime={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
pressure.Start();
try
{
    for (int index = 0; index < 8; index++)
    {
        Console.WriteLine($"PNGCALLBACK write={index} collections={Volatile.Read(ref collections)}");
        using var stream = File.Create(Path.Combine(output, $"frame-{index}.png"));
        Write(pixels, width, height, stream, baseline, index % 2 == 0 ? StbiImageFormat.Rgb : StbiImageFormat.Rgba);
    }
}
finally { Volatile.Write(ref stop, 1); pressure.Join(); }
Console.WriteLine($"PNGCALLBACK PASS writes=8 collections={collections}");
if (collections == 0) throw new InvalidOperationException("GC pressure was vacuous.");
if (!baseline)
{
    for (int index = 0; index < 8; index++)
    {
        var format = index % 2 == 0 ? StbiImageFormat.Rgb : StbiImageFormat.Rgba;
        int stride = width * (int)format;
        StbImage.FlipVerticallyOnLoad = false;
        using var decoded = StbImage.Load((ReadOnlySpan<byte>)File.ReadAllBytes(Path.Combine(output, $"frame-{index}.png")), format);
        for (int y = 0; y < height; y++)
            if (!decoded.AsSpan<byte>().Slice(y * stride, stride).SequenceEqual(pixels.AsSpan((height - y - 1) * stride, stride)))
                throw new InvalidOperationException("GC-pressure pixels/alpha/orientation failed.");
    }
    Console.WriteLine("PNGCALLBACK pressure RGB/RGBA pixels/alpha/orientation=True");
    Verify(output);
}

[MethodImpl(MethodImplOptions.NoInlining)]
static void Write(byte[] pixels, int width, int height, Stream destination, bool baseline, StbiImageFormat format)
{
    StbImage.FlipVerticallyOnSave = true;
    if (baseline) StbImage.WritePng<byte>(pixels, width, height, format, destination);
    else PngEncoder.Write<byte>(pixels, width, height, format, destination);
}

static void Verify(string output)
{
    foreach (var format in new[] { StbiImageFormat.Grey, StbiImageFormat.GreyAlpha, StbiImageFormat.Rgb, StbiImageFormat.Rgba })
    foreach (bool flip in new[] { false, true })
    {
        const int w = 13, h = 11; int components = (int)format, stride = w * components;
        byte[] source = new byte[stride * h];
        for (int i = 0; i < source.Length; i++) source[i] = (byte)(i * 37 + i / stride * 11);
        // Explicit transparent, partial and opaque alpha; preserve RGB under alpha0.
        if (components is 2 or 4)
            for (int pixel = 0; pixel < w * h; pixel++) source[pixel * components + components - 1] = (byte)(pixel % 3 switch { 0 => 0, 1 => 127, _ => 255 });
        StbImage.FlipVerticallyOnSave = flip;
        using var reference = new MemoryStream();
        if (!GC.TryStartNoGCRegion(64 * 1024 * 1024)) throw new InvalidOperationException("Cannot protect package parity reference.");
        try { StbImage.WritePng<byte>(source, w, h, format, reference); }
        finally { GC.EndNoGCRegion(); }
        using var candidate = new MemoryStream();
        PngEncoder.Write<byte>(source, w, h, format, candidate);
        if (!reference.ToArray().AsSpan().SequenceEqual(candidate.ToArray())) throw new InvalidOperationException("PNG byte parity failed.");
        StbImage.FlipVerticallyOnLoad = false;
        using var decoded = StbImage.Load((ReadOnlySpan<byte>)candidate.ToArray(), format);
        if (decoded.Width != w || decoded.Height != h || decoded.Format != format) throw new InvalidOperationException("PNG shape/format failed.");
        for (int y = 0; y < h; y++)
            if (!decoded.AsSpan<byte>().Slice(y * stride, stride).SequenceEqual(source.AsSpan((flip ? h - y - 1 : y) * stride, stride)))
                throw new InvalidOperationException("PNG pixels/alpha/orientation failed.");
        File.WriteAllBytes(Path.Combine(output, $"parity-{format}-{flip}.png"), candidate.ToArray());
        Console.WriteLine($"PNGCALLBACK parity format={format} flip={flip} pngBytes=True pixels=True alpha=True orientation=True");
    }
    var failure = new IOException("injected stream failure");
    using var sink = new ThrowingStream(failure);
    bool caught = false;
    try { PngEncoder.Write<byte>(new byte[12], 2, 2, StbiImageFormat.Rgb, sink); }
    catch (IOException error) { caught = ReferenceEquals(error, failure); }
    if (!caught || sink.Disposed) throw new InvalidOperationException("Stream failure identity/ownership failed.");
    using var reuse = new MemoryStream();
    PngEncoder.Write<byte>(new byte[12], 2, 2, StbiImageFormat.Rgb, reuse);
    if (reuse.Length == 0 || !reuse.CanWrite) throw new InvalidOperationException("Encoder reuse/destination ownership failed.");
    Console.WriteLine("PNGCALLBACK PASS formats=4 flipPolicies=2 streamFailureIdentity=True callerStreamOwned=True reuse=True");
}

sealed class ThrowingStream(IOException failure) : MemoryStream
{
    internal bool Disposed { get; private set; }
    public override void Write(ReadOnlySpan<byte> buffer) => throw failure;
    protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
}
