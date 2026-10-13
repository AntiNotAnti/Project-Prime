using System;
using System.Collections.Generic;

namespace MphRead.Mods.Render;

/// <summary>
/// Bounded CPU-side accounting of facade matrix-array requests. Program names
/// are diagnostic integers, never GL queries or retained rendering owners.
/// The two partitions independently conserve the same executed upload total.
/// </summary>
internal static class OpenGlUniformAttribution
{
    internal const int MaximumPrograms = 32;
    private const int ProgramSlots = MaximumPrograms + 2; // Unknown and overflow.
    private const int OwnerCount = (int)OpenGlDrawOwner.Hud + 1;
    private const int PassCount = (int)OpenGlCpuPass.PaletteCollection + 2;
    private const int OriginCount = (int)OpenGlItemOrigin.Geometry + 1;
    private const int Metrics = 4;
    private const int Requests = 0, Uploads = 1, Bytes = 2, Skips = 3;
    [ThreadStatic] private static State? _state;

    private sealed class State
    {
        internal readonly int[] Programs = new int[MaximumPrograms];
        internal readonly long[] FramePrograms = new long[ProgramSlots * Metrics];
        internal readonly long[] ProgramSums = new long[ProgramSlots * Metrics];
        internal readonly long[] FrameSources = new long[PassCount * OwnerCount * OriginCount * Metrics];
        internal readonly long[] SourceSums = new long[PassCount * OwnerCount * OriginCount * Metrics];
        internal int ProgramCount, Frames;
        internal bool Active;
    }

    internal static void BeginFrame()
    {
        State s = _state ??= new();
        Array.Clear(s.FramePrograms); Array.Clear(s.FrameSources);
        s.Active = true;
    }
    internal static void EndFrame()
    {
        if (_state is not { Active: true } s) return;
        s.Active = false;
        for (int i = 0; i < s.ProgramSums.Length; i++) s.ProgramSums[i] += s.FramePrograms[i];
        for (int i = 0; i < s.SourceSums.Length; i++) s.SourceSums[i] += s.FrameSources[i];
        s.Frames++;
    }
    internal static void CancelFrame()
    {
        if (_state is not { } s) return;
        s.Active = false; s.ProgramCount = s.Frames = 0;
        Array.Clear(s.Programs); Array.Clear(s.ProgramSums); Array.Clear(s.SourceSums);
        Array.Clear(s.FramePrograms); Array.Clear(s.FrameSources);
    }
    internal static void ResetContext() => _state = null;

    internal static void NoteMatrixArray(int program, int count, bool submitted)
    {
        if (_state is not { Active: true } s) return;
        int slot = 0;
        if (program > 0)
        {
            int index = Array.IndexOf(s.Programs, program, 0, s.ProgramCount);
            if (index >= 0) slot = index + 2;
            else if (s.ProgramCount < MaximumPrograms)
            { slot = s.ProgramCount + 2; s.Programs[s.ProgramCount++] = program; }
            else slot = 1;
        }
        var source = OpenGlDrawAttribution.Source(OpenGlPassProfiler.CurrentPass);
        int origin = (int)source.Origin;
        int sourceIndex = ((source.Pass * OwnerCount + (int)source.Owner) * OriginCount + origin) * Metrics;
        long bytes = count > 0 ? (long)count * 64 : 0;
        Add(s.FramePrograms, slot * Metrics, bytes, submitted);
        Add(s.FrameSources, sourceIndex, bytes, submitted);
    }
    private static void Add(long[] values, int index, long bytes, bool submitted)
    {
        values[index + Requests]++;
        values[index + (submitted ? Uploads : Skips)]++;
        if (submitted) values[index + Bytes] += bytes;
    }
    private static object Average(long[] values, int index, int frames) => new
    {
        requests = values[index + Requests] / (double)frames,
        uploads = values[index + Uploads] / (double)frames,
        bytes = values[index + Bytes] / (double)frames,
        skips = values[index + Skips] / (double)frames
    };
    internal static object Summary()
    {
        State? s = _state;
        int frames = Math.Max(1, s?.Frames ?? 0);
        var programs = new List<object>();
        var sources = new List<object>();
        if (s != null)
        {
            for (int slot = 0; slot < s.ProgramCount + 2; slot++)
                if (s.ProgramSums[slot * Metrics + Requests] != 0)
                    programs.Add(new
                    {
                        program = slot >= 2 ? (int?)s.Programs[slot - 2] : null,
                        status = slot == 0 ? "UNKNOWN" : slot == 1 ? "CAPACITY_OVERFLOW" : "TRACKED",
                        average = Average(s.ProgramSums, slot * Metrics, frames)
                    });
            for (int pass = 0; pass < PassCount; pass++)
                for (int owner = 0; owner < OwnerCount; owner++)
                    for (int origin = 0; origin < OriginCount; origin++)
                    {
                        int index = ((pass * OwnerCount + owner) * OriginCount + origin) * Metrics;
                        if (s.SourceSums[index + Requests] == 0) continue;
                        sources.Add(new
                        {
                            pass = pass < PassCount - 1 ? ((OpenGlCpuPass)pass).ToString() : "Unscoped",
                            owner = ((OpenGlDrawOwner)owner).ToString(),
                            origin = ((OpenGlItemOrigin)origin).ToString(),
                            average = Average(s.SourceSums, index, frames)
                        });
                    }
        }
        return new
        {
            schema = 1, frames = s?.Frames ?? 0, programCapacity = MaximumPrograms,
#if !MPHREAD_SERVER && !ANDROID
            legacyScalarBackend = "MEASURED_DESKTOP_FACADE",
#else
            legacyScalarBackend = "UNVERIFIED_PLATFORM",
#endif
            matrixArraysByProgram = programs, matrixArraysBySource = sources,
            limits = "Two independent partitions of facade matrix4-array requests, submissions, skips and count*64 payload bytes; not driver residency or GPU execution. Direct native bypasses and implicit GLES emulation uniforms are excluded. Program integers are scoped to the current context/window; overflow and unscoped owners stay explicit. Scalar counters exclude uncached array overloads; unsupported scalar backends remain UNVERIFIED. No per-location/value/redundancy witness is inferred."
        };
    }

    internal static int Check()
    {
        int failures = 0;
        void Check(bool pass, string name)
        { Console.WriteLine($"[uniformattributioncheck] {(pass ? "PASS" : "FAIL")}: {name}"); if (!pass) failures++; }
        State? prior = _state;
        try
        {
            _state = null;
            NoteMatrixArray(7, 2, true);
            Check(_state == null, "inactive upload observation allocates no state");
            BeginFrame();
            OpenGlDrawAttribution.BeginFrame();
            var palette = new RenderItem { Type = RenderItemType.Mesh, MatrixStackCount = 2 };
            using (OpenGlDrawAttribution.Measure(palette, (int)OpenGlCpuPass.Opaque, false))
            {
                NoteMatrixArray(7, 2, true); NoteMatrixArray(7, 2, false);
                using (OpenGlDrawAttribution.Measure(new() { Type = RenderItemType.Particle },
                    (int)OpenGlCpuPass.TranslucentFront, true)) NoteMatrixArray(8, 1, true);
                NoteMatrixArray(7, 1, true);
            }
            NoteMatrixArray(0, 1, true);
            EndFrame(); OpenGlDrawAttribution.EndFrame();
            State s = _state!;
            int setup = (((int)OpenGlCpuPass.Opaque * OwnerCount + (int)OpenGlDrawOwner.PaletteMesh)
                * OriginCount + (int)OpenGlItemOrigin.Setup) * Metrics;
            int geometry = (((int)OpenGlCpuPass.TranslucentFront * OwnerCount + (int)OpenGlDrawOwner.Particle)
                * OriginCount + (int)OpenGlItemOrigin.Geometry) * Metrics;
            int unknown = ((PassCount - 1) * OwnerCount * OriginCount) * Metrics;
            Check(s.SourceSums[setup + Requests] == 3 && s.SourceSums[setup + Uploads] == 2
                && s.SourceSums[setup + Skips] == 1 && s.SourceSums[setup + Bytes] == 192
                && s.SourceSums[geometry + Uploads] == 1 && s.SourceSums[geometry + Bytes] == 64
                && s.SourceSums[unknown + Uploads] == 1,
                "nested setup and geometry scopes restore exact source and unknown owner");
            Check(s.ProgramSums[2 * Metrics + Requests] == 3
                && s.ProgramSums[3 * Metrics + Uploads] == 1 && s.ProgramSums[Uploads] == 1,
                "program partition distinguishes executed uploads from palette skips");
            bool conserved = true;
            for (int metric = 0; metric < Metrics; metric++)
            {
                long programs = 0, sources = 0;
                for (int i = metric; i < s.ProgramSums.Length; i += Metrics) programs += s.ProgramSums[i];
                for (int i = metric; i < s.SourceSums.Length; i += Metrics) sources += s.SourceSums[i];
                conserved &= programs == sources;
            }
            Check(conserved, "source and program partitions conserve every operation and byte");
            CancelFrame(); BeginFrame();
            for (int i = 1; i <= MaximumPrograms + 5; i++) NoteMatrixArray(i, 1, true);
            EndFrame();
            Check(s.ProgramCount == MaximumPrograms && s.ProgramSums[Metrics + Uploads] == 5
                && s.ProgramSums[Metrics + Bytes] == 320, "program capacity overflow remains explicit and counted");
            BeginFrame(); NoteMatrixArray(1, 9, true); CancelFrame();
            Check(s.Frames == 0 && s.ProgramCount == 0 && Array.TrueForAll(s.SourceSums, value => value == 0)
                && Array.TrueForAll(s.FramePrograms, value => value == 0), "cancellation drops pending observations and partial windows");
            ResetContext(); NoteMatrixArray(9, 1, true);
            Check(_state == null, "context reset releases bounded numeric storage and rejects stale observations");
            Check(Array.TrueForAll(typeof(State).GetFields(System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic), field => field.FieldType.IsValueType
                    || field.FieldType == typeof(int[]) || field.FieldType == typeof(long[])),
                "diagnostic state retains only value fields and numeric arrays, never scene or item owners");
        }
        finally { _state = prior; OpenGlDrawAttribution.ResetContext(); }
        return failures;
    }
}
