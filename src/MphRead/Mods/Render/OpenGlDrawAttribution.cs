using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace MphRead.Mods.Render;

internal enum OpenGlDrawOwner
{
    Unclassified, RoomMesh, PaletteMesh, SingleTransformMesh, FirstPerson,
    Particle, TrailSingle, TrailMulti, TrailStack, ProceduralShape, Hud
}

// Diagnostic CPU wall scopes include GL submission/backpressure. They do not
// identify GPU execution or pure CPU preparation. No scene/item references survive.
internal static class OpenGlDrawAttribution
{
    [ThreadStatic] private static State? _state;
    private const int OwnerCount = (int)OpenGlDrawOwner.Hud + 1;
    private const int PassCount = (int)OpenGlCpuPass.GeometrySubmission + 2;
    internal sealed class State
    {
        internal readonly long[] FrameImmediate = new long[OwnerCount * PassCount];
        internal readonly long[] ImmediateSums = new long[OwnerCount * PassCount];
        internal readonly long[] SetupTicks = new long[OwnerCount], GeometryTicks = new long[OwnerCount];
        internal readonly bool[] SetupSeen = new bool[OwnerCount], GeometrySeen = new bool[OwnerCount];
        internal readonly OpenGlSampleWindow[] Setup = Array.ConvertAll(Enum.GetValues<OpenGlDrawOwner>(), _ => new OpenGlSampleWindow());
        internal readonly OpenGlSampleWindow[] Geometry = Array.ConvertAll(Enum.GetValues<OpenGlDrawOwner>(), _ => new OpenGlSampleWindow());
        internal bool Active, HasOwner;
        internal int Generation, Frames, OwnerPass = -1;
        internal OpenGlDrawOwner Owner;
    }
    internal static OpenGlDrawOwner Classify(RenderItem item)
        => item.ViewModel ? OpenGlDrawOwner.FirstPerson : item.Type switch
        {
            RenderItemType.Mesh => item.RetainedRoomOwned ? OpenGlDrawOwner.RoomMesh
                : item.MatrixStackCount > 0 ? OpenGlDrawOwner.PaletteMesh : OpenGlDrawOwner.SingleTransformMesh,
            RenderItemType.Particle => OpenGlDrawOwner.Particle,
            RenderItemType.TrailSingle => OpenGlDrawOwner.TrailSingle,
            RenderItemType.TrailMulti => OpenGlDrawOwner.TrailMulti,
            RenderItemType.TrailStack => OpenGlDrawOwner.TrailStack,
            RenderItemType.Box or RenderItemType.Cylinder or RenderItemType.Sphere
                or RenderItemType.Quad or RenderItemType.Ngon => OpenGlDrawOwner.ProceduralShape,
            _ => OpenGlDrawOwner.Unclassified
        };
    internal static void BeginFrame()
    {
        State s = _state ??= new();
        Array.Clear(s.FrameImmediate); Array.Clear(s.SetupTicks); Array.Clear(s.GeometryTicks);
        Array.Clear(s.SetupSeen); Array.Clear(s.GeometrySeen);
        s.Generation++; s.Active = true; s.HasOwner = false; s.OwnerPass = -1;
    }
    internal static void CancelFrame()
    {
        if (_state is not { } s) return;
        s.Generation++; s.Active = false; s.HasOwner = false; s.Frames = 0;
        Array.Clear(s.ImmediateSums);
        foreach (var window in s.Setup) window.Reset();
        foreach (var window in s.Geometry) window.Reset();
    }
    internal static void ResetContext() => _state = null;
    internal static Scope Measure(RenderItem item, int pass, bool geometry)
    {
        if (_state is not { Active: true } s) return default;
        OpenGlDrawOwner kind = Classify(item);
        var scope = new Scope(s, kind, geometry);
        if (geometry) { s.Owner = kind; s.OwnerPass = pass; s.HasOwner = true; }
        return scope;
    }
    internal readonly struct Scope : IDisposable
    {
        private readonly State? _owner;
        private readonly int _generation, _previousPass;
        private readonly long _start;
        private readonly OpenGlDrawOwner _kind, _previousOwner;
        private readonly bool _geometry, _previousHasOwner;
        internal Scope(State owner, OpenGlDrawOwner kind, bool geometry)
        {
            _owner = owner; _generation = owner.Generation; _kind = kind; _geometry = geometry;
            _previousPass = owner.OwnerPass; _previousOwner = owner.Owner; _previousHasOwner = owner.HasOwner;
            _start = Stopwatch.GetTimestamp();
        }
        public void Dispose()
        {
            if (_owner is not { Active: true } s || s != _state || _generation != s.Generation) return;
            long ticks = Math.Max(0, Stopwatch.GetTimestamp() - _start);
            if (_geometry)
            {
                s.GeometryTicks[(int)_kind] += ticks; s.GeometrySeen[(int)_kind] = true;
                s.Owner = _previousOwner; s.OwnerPass = _previousPass; s.HasOwner = _previousHasOwner;
            }
            else { s.SetupTicks[(int)_kind] += ticks; s.SetupSeen[(int)_kind] = true; }
        }
    }
    internal static void NoteImmediate(int pass, long count)
    {
        if (_state is not { Active: true } s) return;
        OpenGlDrawOwner owner = s.HasOwner ? s.Owner
            : pass == (int)OpenGlCpuPass.Hud ? OpenGlDrawOwner.Hud : OpenGlDrawOwner.Unclassified;
        int sourcePass = s.HasOwner ? s.OwnerPass : pass;
        if (sourcePass < 0 || sourcePass >= PassCount - 1) sourcePass = PassCount - 1;
        s.FrameImmediate[sourcePass * OwnerCount + (int)owner] += count;
    }
    internal static void EndFrame()
    {
        if (_state is not { Active: true } s) return;
        s.Active = false; s.HasOwner = false;
        for (int i = 0; i < s.FrameImmediate.Length; i++) s.ImmediateSums[i] += s.FrameImmediate[i];
        for (int i = 0; i < OwnerCount; i++)
        {
            if (s.SetupSeen[i]) s.Setup[i].Add(s.SetupTicks[i] * 1000.0 / Stopwatch.Frequency);
            if (s.GeometrySeen[i]) s.Geometry[i].Add(s.GeometryTicks[i] * 1000.0 / Stopwatch.Frequency);
        }
        s.Frames++;
    }
    internal static object Summary()
    {
        var owners = new Dictionary<string, object>();
        var passes = new Dictionary<string, object>();
        long total = 0;
        State? s = _state;
        foreach (var kind in Enum.GetValues<OpenGlDrawOwner>())
            owners[kind.ToString()] = new
            {
                setupWall = s?.Setup[(int)kind].Summary(),
                geometryWall = s?.Geometry[(int)kind].Summary()
            };
        for (int pass = 0; pass < PassCount; pass++)
        {
            var counts = new Dictionary<string, double>();
            foreach (var kind in Enum.GetValues<OpenGlDrawOwner>())
            {
                long count = s?.ImmediateSums[pass * OwnerCount + (int)kind] ?? 0;
                total += count;
                if (count != 0) counts[kind.ToString()] = count / (double)(s?.Frames ?? 1);
            }
            if (counts.Count != 0) passes[pass < PassCount - 1 ? ((OpenGlCpuPass)pass).ToString() : "Unscoped"] = counts;
        }
        return new { frames = s?.Frames ?? 0, averageImmediateDraws = total / (double)Math.Max(1, s?.Frames ?? 0),
            averageImmediateDrawsByPassAndOwner = passes, cpuByOwner = owners,
            limits = "CPU scopes are inclusive and not additive; scopes include driver wall time; mesh ownership is metadata, not an immutable geometry proof. CPU samples include only frames containing that owner. Immediate bins exclude compiled-list commands and partition the existing executed ImmediateDraws counter." };
    }

    internal static int Check()
    {
        int failures = 0;
        void Assert(bool value, string name)
        { Console.WriteLine($"[drawattributioncheck] {(value ? "PASS" : "FAIL")}: {name}"); if (!value) failures++; }
        Assert(Classify(new() { Type = RenderItemType.Mesh, RetainedRoomOwned = true }) == OpenGlDrawOwner.RoomMesh, "room ownership is explicit metadata");
        Assert(Classify(new() { Type = RenderItemType.Mesh, MatrixStackCount = 3 }) == OpenGlDrawOwner.PaletteMesh, "matrix palette identifies mesh preparation");
        Assert(Classify(new() { Type = RenderItemType.Mesh }) == OpenGlDrawOwner.SingleTransformMesh, "unowned nonpalette mesh uses a single transform");
        Assert(Classify(new() { Type = RenderItemType.Mesh, ViewModel = true, MatrixStackCount = 3 }) == OpenGlDrawOwner.FirstPerson, "first-person owner wins over mesh preparation kind");
        Assert(OwnerCount == Enum.GetValues<OpenGlDrawOwner>().Length && PassCount - 1 == Enum.GetValues<OpenGlCpuPass>().Length,
            "fixed storage covers every current owner and pass plus unknown sources");
        State? prior = _state;
        try
        {
            _state = new(); BeginFrame();
            using (Measure(new() { Type = RenderItemType.Particle }, (int)OpenGlCpuPass.Opaque, true))
            {
                NoteImmediate(-1, 2);
                using (Measure(new() { Type = RenderItemType.TrailMulti }, (int)OpenGlCpuPass.TranslucentFront, true)) NoteImmediate(-1, 3);
                NoteImmediate(-1, 5);
            }
            NoteImmediate((int)OpenGlCpuPass.Hud, 7); NoteImmediate(-1, 11);
            EndFrame();
            State s = _state!;
            Assert(s.ImmediateSums[(int)OpenGlCpuPass.Opaque * OwnerCount + (int)OpenGlDrawOwner.Particle] == 7
                && s.ImmediateSums[(int)OpenGlCpuPass.TranslucentFront * OwnerCount + (int)OpenGlDrawOwner.TrailMulti] == 3,
                "nested ownership restores the outer source without double counting");
            Assert(s.ImmediateSums[(int)OpenGlCpuPass.Hud * OwnerCount + (int)OpenGlDrawOwner.Hud] == 7
                && s.ImmediateSums[(PassCount - 1) * OwnerCount] == 11, "HUD and unknown draws remain visible");
            long sum = 0; foreach (long count in s.ImmediateSums) sum += count;
            Assert(sum == 28, "all immediate sources conserve the executed draw total");
            BeginFrame(); var stale = Measure(new() { Type = RenderItemType.Particle }, 0, true);
            CancelFrame(); BeginFrame(); stale.Dispose(); NoteImmediate(-1, 1); EndFrame();
            Assert(_state!.ImmediateSums[(PassCount - 1) * OwnerCount] == 1 && _state.Frames == 1,
                "cancelled scopes cannot contaminate a replacement frame/window");
        }
        finally { _state = prior; }
        return failures;
    }
}
