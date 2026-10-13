using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;

namespace MphRead.Mods.Render;

internal enum OpenGlCpuPass
{
    Preparation, DrawItems, CaptureWorld, ShadowCollection, ShadowSubmission,
    Shadow, WorldSetup, World, Outlines, SceneOverlays, PostProcess, Composite,
    Opaque, Decal, MarkTranslucent, RebuildDepth, TranslucentBehind, TranslucentFront, Ui, Hud,
    HudImagePrepare, TextureUpload, ProceduralPrepare, ProceduralSubmit, ItemSetup, GeometrySubmission
}

internal enum OpenGlFrameCounter
{
    ShadowClusterTests, ShadowClusterRejects, UnlitShaderFrames, ShadowMaterialReuses, ShadowObjectReuses, MeshObjectReuses, RoomIdentityReuses, RoomDrawTokenReuses, PlainShaderFrames, BatchPreflightFailureReuses, IndexedDraws, DisplayListCalls, ImmediateDraws, MergedDraws, BufferUploads,
    UploadBytes, BufferOrphans, ProgramBinds, StateSkips, UniformArrayUploads,
    UniformArrayBytes, UniformArraySkips, VaoDraws, AvoidedPointerCalls, SamplerWrites, SamplerSkips, DynamicBatches, DynamicMergedPrimitives,
    TextureUploads, TextureUploadBytes, HudGlyphCacheHits, HudGlyphCacheMisses, HudGlyphCacheFallbacks,
    HudSpriteCacheHits, HudSpriteCacheMisses, HudSpriteCacheFallbacks, DepthPreparedItems, RoomDescriptorHits, RoomDescriptorLookups, RoomBatchProofs, RoomBatchProofReuses,
    ProceduralDraws, ProceduralVertices, ProceduralItems, ProceduralMaterialReuses, ProceduralObjectReuses, ProceduralUnlitPreparations, DepthSnapshotCopies, DepthReplayDrawsSaved, WorldShaderVariantSwitches
}

/// <summary>Fixed storage, null for absent/invalid samples; no per-frame sorting.</summary>
internal sealed class OpenGlSampleWindow
{
    private readonly double[] _values;
    internal int Count { get; private set; }
    internal OpenGlSampleWindow(int capacity = 240) => _values = new double[capacity];
    internal bool Add(double value)
    {
        if (!double.IsFinite(value) || value < 0 || Count == _values.Length) return false;
        _values[Count++] = value;
        return true;
    }
    internal void Reset() => Count = 0;
    internal object Summary(bool bytes = false)
    {
        if (Count == 0)
            return bytes ? (object)new { samples = 0, p50Bytes = (double?)null, p95Bytes = (double?)null, p99Bytes = (double?)null }
                : new { samples = 0, p50Ms = (double?)null, p95Ms = (double?)null, p99Ms = (double?)null };
        double[] sorted = _values.AsSpan(0, Count).ToArray();
        Array.Sort(sorted);
        double P(int p) => sorted[OpenGlSubmissionProfiler.PercentileIndex(Count, p)];
        return bytes ? (object)new { samples = Count, p50Bytes = (double?)P(50), p95Bytes = (double?)P(95), p99Bytes = (double?)P(99) }
            : new { samples = Count, p50Ms = (double?)P(50), p95Ms = (double?)P(95), p99Ms = (double?)P(99) };
    }
}

/// <summary>
/// Frame-local inclusive CPU scopes. Nested scopes are deliberately not additive.
/// Complete only after a successful present; cancellation drops the entire window
/// so map changes and interrupted frames cannot become performance evidence.
/// </summary>
internal static class OpenGlPassProfiler
{
    [ThreadStatic] private static int _generation;
    [ThreadStatic] private static State? _state;
    private sealed class State
    {
        internal readonly long[] Ticks = new long[Enum.GetValues<OpenGlCpuPass>().Length];
        internal readonly bool[] Seen = new bool[Enum.GetValues<OpenGlCpuPass>().Length];
        internal readonly long[] Counters = new long[Enum.GetValues<OpenGlFrameCounter>().Length];
        internal readonly long[] Sums = new long[Enum.GetValues<OpenGlFrameCounter>().Length];
        internal readonly OpenGlSampleWindow[] Cpu = Array.ConvertAll(Enum.GetValues<OpenGlCpuPass>(), _ => new OpenGlSampleWindow());
        internal readonly OpenGlSampleWindow Allocations = new();
        internal bool Active;
        internal long Allocated;
        internal int Frames, Generation, Window, CurrentPass = -1;
    }
    internal static void BeginFrame()
    {
        if (!OpenGlSubmissionProfiler.Enabled) return;
        State s = _state ??= new State();
        if (s.Active) CancelFrame();
        Array.Clear(s.Ticks); Array.Clear(s.Seen); Array.Clear(s.Counters);
        OpenGlDrawAttribution.BeginFrame();
        s.CurrentPass = -1;
        s.Generation = ++_generation;
        s.Active = true;
        s.Allocated = GC.GetAllocatedBytesForCurrentThread();
    }
    internal static void CancelFrame()
    {
        if (_state is not { } s) return;
        OpenGlDrawAttribution.CancelFrame();
        s.Active = false; s.Generation = ++_generation; s.Frames = 0; s.CurrentPass = -1;
        foreach (var window in s.Cpu) window.Reset();
        s.Allocations.Reset(); Array.Clear(s.Sums);
    }
    internal static void ResetContext() { _generation++; _state = null; OpenGlDrawAttribution.ResetContext(); }
    internal static int CurrentPass => _state is { Active: true } s ? s.CurrentPass : -1;
    internal static Scope Measure(OpenGlCpuPass pass)
    {
        if (_state is not { Active: true } s) return default;
        var scope = new Scope(s.Generation, pass, s.CurrentPass, Stopwatch.GetTimestamp());
        s.CurrentPass = (int)pass;
        return scope;
    }
    internal readonly struct Scope : IDisposable
    {
        private readonly int _generation, _previousPass;
        private readonly OpenGlCpuPass _pass;
        private readonly long _start;
        internal Scope(int generation, OpenGlCpuPass pass, long start) : this(generation, pass, -1, start) { }
        internal Scope(int generation, OpenGlCpuPass pass, int previousPass, long start) { _generation = generation; _pass = pass; _previousPass = previousPass; _start = start; }
        public void Dispose()
        {
            if (_start == 0 || _state is not { Active: true } s || s.Generation != _generation) return;
            long elapsed = Stopwatch.GetTimestamp() - _start;
            if (elapsed < 0) { CancelFrame(); return; }
            s.Ticks[(int)_pass] += elapsed; s.Seen[(int)_pass] = true; s.CurrentPass = _previousPass;
        }
    }
    internal static void Count(OpenGlFrameCounter counter, long value = 1)
    {
        if (_state is not { Active: true } s) return;
        s.Counters[(int)counter] += value;
        if (counter == OpenGlFrameCounter.ImmediateDraws) OpenGlDrawAttribution.NoteImmediate(s.CurrentPass, value);
    }
    internal static void EndFrame()
    {
        if (_state is not { Active: true } s) return;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - s.Allocated;
        s.Active = false;
        if (allocated < 0) { CancelFrame(); return; }
        OpenGlDrawAttribution.EndFrame();
        s.Allocations.Add(allocated);
        for (int i = 0; i < s.Cpu.Length; i++)
            if (s.Seen[i]) s.Cpu[i].Add(s.Ticks[i] * 1000.0 / Stopwatch.Frequency);
        for (int i = 0; i < s.Sums.Length; i++) s.Sums[i] += s.Counters[i];
        if (++s.Frames < 240) return;
        var cpu = new Dictionary<string, object>();
        foreach (var pass in Enum.GetValues<OpenGlCpuPass>()) cpu[pass.ToString()] = s.Cpu[(int)pass].Summary();
        var counters = new Dictionary<string, double>();
        foreach (var counter in Enum.GetValues<OpenGlFrameCounter>()) counters[counter.ToString()] = s.Sums[(int)counter] / 240.0;
        Console.WriteLine("[glpasses-json] " + JsonSerializer.Serialize(new
        {
            schema = 1, samples = 240, window = ++s.Window, warmup = s.Window == 1,
            cpu, allocatedBytes = s.Allocations.Summary(bytes: true), counters,
            drawAttribution = OpenGlDrawAttribution.Summary(),
            // Byte estimates are not driver residency measurements.
#if !MPHREAD_SERVER && !ANDROID
            retainedBufferBytes = DesktopRetainedGeometry.ResidentBytes,
            standalonePromotedBufferBytes = DesktopRetainedGeometry.StandaloneBytes,
            standalonePromotedBufferHighWaterBytes = DesktopRetainedGeometry.StandaloneHighWaterBytes,
            retainedCpuArrayBytes = DesktopRetainedGeometry.CpuArrayBytes,
            retainedCpuArrayHighWaterBytes = DesktopRetainedGeometry.CpuArrayHighWaterBytes,
            combinedBytes = DesktopRetainedGeometry.CombinedBytes,
            negativeBatchIdBytes = DesktopRetainedGeometry.NegativeBatchIdBytes,
            originalDisplayListBytes = (long?)null,
            atlasEvictions = DesktopRetainedGeometry.EvictionCount,
            retainedBufferHighWaterBytes = DesktopRetainedGeometry.HighWaterBytes,
#endif
#if ANDROID
            dynamicBufferBytes = GlEs.DynamicBufferBytes,
            dynamicBufferHighWaterBytes = GlEs.DynamicBufferHighWaterBytes,
#endif
#if !MPHREAD_SERVER
            depthSnapshotBytes = DepthReplaySnapshot.ResidentBytes,
#endif
            gpuResidentBytes = (long?)null, temporaryFramebufferBytes = (long?)null
        }));
        CancelFrame();
    }
}
