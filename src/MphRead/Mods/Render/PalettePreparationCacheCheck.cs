using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace MphRead.Mods.Render;

internal static class PalettePreparationCacheCheck
{
    private static float[] Factors(float scale) => new[]
        { scale, scale, scale, 1f, scale, scale, scale, 1f, scale, scale, scale, 1f, 1f, 1f, 1f, 1f };
    // Exact original indexed/multiply path; the existing scene layout is reused.
    private static void Original(IReadOnlyList<float> source, float[] factors, float[] destination)
    {
        for (int i = 0; i < source.Count; i++)
        {
            float value = source[i];
            destination[i] = value * factors[i - (i / 16) * 16];
        }
    }
    private static bool Same(float[] a, float[] b, int count)
    {
        for (int i = 0; i < count; i++)
            if (BitConverter.SingleToInt32Bits(a[i]) != BitConverter.SingleToInt32Bits(b[i])) return false;
        return true;
    }
    internal static int Run()
    {
        int failures = 0;
        void Check(bool pass, string name)
        { Console.WriteLine($"[paletteprepcheck] {(pass ? "PASS" : "FAIL")}: {name}"); if (!pass) failures++; }
        Check(!OpenGlOptimizationPolicy.DefaultEnabled("PALETTE_PREP")
            && !OpenGlOptimizationPolicy.Select(true, false, null, false)
            && OpenGlOptimizationPolicy.Select(true, false, "1", false)
            && !OpenGlOptimizationPolicy.Select(true, true, "1", false),
            "preparation cache requires opt-in and respects forced legacy");
        var cache = new PalettePreparationCache();
        var source = new float[32];
        for (int i = 0; i < source.Length; i++) source[i] = (i - 9) / 7f;
        source[0] = -0f; source[1] = float.NaN; source[3] = float.PositiveInfinity;
        var first = new float[512]; var second = new float[512]; var expected = new float[512];
        Original(source, Factors(1.5f), expected);
        Check(cache.TryCopy(source, 1, 2, 1.5f, first, out bool hit) && !hit && Same(first, expected, 32),
            "first known palette preserves every original float bit");
        Check(cache.TryCopy(source, 1, 2, 1.5f, second, out hit) && hit && Same(second, expected, 32),
            "identical source revision length scale reuses exact arithmetic");
        second[5] = 99;
        cache.TryCopy(source, 1, 2, 1.5f, second, out hit);
        Check(hit && Same(first, expected, 32) && Same(second, expected, 32) && !ReferenceEquals(first, second),
            "item snapshots and private scratch do not share mutable arrays");
        float near = MathF.BitIncrement(1.5f);
        Original(source, Factors(near), expected);
        Check(cache.TryCopy(source, 1, 2, near, second, out hit) && !hit && Same(second, expected, 32),
            "near-equal scale forces new exact arithmetic");
        var other = (float[])source.Clone();
        Check(cache.TryCopy(other, 1, 2, near, second, out hit) && !hit,
            "equal-valued distinct source cannot impersonate a revision identity");
        source[5] += .25f;
        Original(source, Factors(near), expected);
        Check(cache.TryCopy(source, 2, 2, near, second, out hit) && !hit && Same(second, expected, 32),
            "changed revision recomputes current native values");
        cache.TryCopy(source, 2, 2, 0f, second, out _);
        float negativeZero = BitConverter.Int32BitsToSingle(unchecked((int)0x80000000));
        Original(source, Factors(negativeZero), expected);
        Check(cache.TryCopy(source, 2, 2, negativeZero, second, out hit) && !hit && Same(second, expected, 32),
            "signed-zero scales have distinct bitwise identity");
        var changingLength = new List<float>(new float[16]);
        cache.TryCopy(changingLength, 1, 1, 1, second, out _);
        changingLength.AddRange(new float[16]);
        Check(cache.TryCopy(changingLength, 1, 2, 1, second, out hit) && !hit,
            "palette count change invalidates the last preparation");
        Check(!cache.TryCopy(source, 0, 2, 1, second, out _) && !cache.HasSource,
            "unknown revision falls back and releases prior identity");
        source[7] = .33f;
        Original(source, Factors(1), expected);
        Check(!cache.TryCopy(source, 0, 2, 1, second, out _), "unversioned mutation stays on original path");
        Original(source, Factors(1), second);
        Check(Same(second, expected, 32), "unknown mutation fallback sees current values");
        Check(!cache.TryCopy(source, 2, 1, 1, second, out _)
            && !cache.TryCopy(source, 2, 0, 1, second, out _)
            && !cache.TryCopy(source, 2, 33, 1, second, out _)
            && !cache.TryCopy(source, 2, 2, float.NaN, second, out _)
            && !cache.TryCopy(source, 2, 2, float.PositiveInfinity, second, out _)
            && !cache.TryCopy(source, 2, 2, 1, new float[16], out _),
            "partial invalid oversized nonfinite and short-output inputs preserve fallback");
        var maximum = new float[512];
        for (int i = 0; i < maximum.Length; i++) maximum[i] = i / 17f;
        Original(maximum, Factors(.75f), expected);
        Check(cache.TryCopy(maximum, 1, 32, .75f, second, out hit) && !hit && Same(second, expected, 512),
            "fixed scratch covers the exact 32-matrix shader limit");
        cache.Clear();
        Check(!cache.HasSource && cache.TryCopy(maximum, 1, 32, .75f, second, out hit) && !hit,
            "collection boundary drops identity rather than borrowing last-frame preparation");
        var weak = DetachedSource(cache);
        cache.Clear(); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Check(!weak.IsAlive && !cache.HasSource, "clear releases the sole cached source reference");
        if (Environment.GetEnvironmentVariable("PROJECT_PRIME_GL_PALETTE_PREP_BENCH") == "1") Benchmark();
        return failures;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference DetachedSource(PalettePreparationCache cache)
    {
        var source = new float[16];
        cache.TryCopy(source, 1, 1, 1, new float[512], out _);
        return new WeakReference(source);
    }

    private readonly record struct Probe(double NanosecondsPerItem, long AllocatedBytes, double Checksum);
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Probe ProbePreparation(float[] source, float[] factors, float[] output,
        PalettePreparationCache cache, bool candidate, int collections)
    {
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int frame = 0; frame < collections; frame++)
        {
            cache.Clear();
            for (int item = 0; item < 8; item++)
                if (candidate) cache.TryCopy(source, 1, 16, 1.25f, output, out _);
                else Original(source, factors, output);
        }
        long elapsed = Stopwatch.GetTimestamp() - start;
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        double checksum = 0;
        foreach (float value in output) checksum += value;
        cache.Clear();
        return new Probe(elapsed * 1_000_000_000.0 / Stopwatch.Frequency / (collections * 8), allocated, checksum);
    }
    private static void Benchmark()
    {
        var source = new float[256];
        for (int i = 0; i < source.Length; i++) source[i] = (i - 127) / 37f;
        var factors = Factors(1.25f); var output = new float[256]; var cache = new PalettePreparationCache();
        ProbePreparation(source, factors, output, cache, false, 2000);
        ProbePreparation(source, factors, output, cache, true, 2000);
        var baseline = new Probe[5]; var candidate = new Probe[5];
        for (int run = 0; run < 5; run++)
        {
            if ((run & 1) == 0)
            { baseline[run] = ProbePreparation(source, factors, output, cache, false, 2000); candidate[run] = ProbePreparation(source, factors, output, cache, true, 2000); }
            else
            { candidate[run] = ProbePreparation(source, factors, output, cache, true, 2000); baseline[run] = ProbePreparation(source, factors, output, cache, false, 2000); }
        }
        Console.WriteLine("[paletteprep-benchmark-json] " + JsonSerializer.Serialize(new
        {
            schema = 1, sourceFloats = 256, itemsPerCollection = 8, collectionsPerRun = 2000,
            baseline, candidate, scope = "synthetic real preparation arithmetic/copy only; not rendering, native assets or rollout evidence",
            endToEndFrameBenefit = "UNVERIFIED"
        }));
    }
}
