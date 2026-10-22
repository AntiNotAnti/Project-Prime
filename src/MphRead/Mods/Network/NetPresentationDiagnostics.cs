using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

internal readonly record struct NetSlotPresentationSnapshot(int Slot, ulong Epoch, ushort Match,
    ushort Generation, ushort Life, long Samples, long Interpolated, long Held, long Unavailable,
    long Starved, long LongestStarvation, long AgeSamples, long AgeTotalFrames, long MaxAgeFrames,
    long[] AgeHistogram, long Corrections, double CorrectionDistance, float MaxCorrectionDistance);

/// <summary>Simulation-thread observations only. No pose, clock, or gameplay decisions.
/// Age is newest received authority frame minus sampled lower frame, not one-way network latency.
/// A stationary received pose is not starvation. Render-only samples never enter this denominator.</summary>
internal static class NetPresentationDiagnostics
{
    private sealed class Slot
    {
        internal ulong Epoch;
        internal ushort Match, Generation, Life;
        internal uint SampleFrame, CorrectionFrame;
        internal bool Seen, CorrectionSeen;
        internal long Samples, Interpolated, Held, Unavailable, Starved, Run, Longest,
            AgeSamples, AgeTotal, MaxAge, Corrections;
        internal double CorrectionDistance;
        internal float MaxCorrection;
        // Inclusive frame edges 0,1,2,4,8,16,32,64,128,256,overflow.
        internal readonly long[] Ages = new long[11];
        internal void Clear()
        {
            Epoch = 0; Match = Generation = Life = 0; Seen = CorrectionSeen = false;
            Samples = Interpolated = Held = Unavailable = Starved = Run = Longest = 0;
            AgeSamples = AgeTotal = MaxAge = Corrections = 0;
            CorrectionDistance = 0; MaxCorrection = 0; Array.Clear(Ages);
        }
    }
    private static readonly Slot[] Slots = Create();
    private static Slot[] Create()
    {
        var slots = new Slot[8];
        for (int i = 0; i < slots.Length; i++) slots[i] = new();
        return slots;
    }
    private static Slot? Current(int slot)
    {
        if ((uint)slot >= Slots.Length || slot == NetSession.LocalSlot) return null;
        ushort generation = NetPlayerLifecycle.Generation(slot), life = NetPlayerLifecycle.Get(slot);
        if (generation == 0 || life == 0) return null;
        Slot row = Slots[slot];
        if (row.Epoch != NetSession.AuthorityEpoch || row.Match != NetSession.CurrentMatchId
            || row.Generation != generation || row.Life != life)
        {
            row.Clear(); row.Epoch = NetSession.AuthorityEpoch; row.Match = NetSession.CurrentMatchId;
            row.Generation = generation; row.Life = life;
        }
        return row;
    }
    internal static void Sample(int slot, bool available, bool interpolated, bool starved, long? age)
    {
        Slot? row = Current(slot);
        if (row == null || (row.Seen && row.SampleFrame == NetSession.NetFrame)) return;
        row.Seen = true; row.SampleFrame = NetSession.NetFrame; row.Samples++;
        if (!available) row.Unavailable++;
        else if (interpolated) row.Interpolated++;
        else row.Held++;
        if (starved) { row.Starved++; row.Longest = Math.Max(row.Longest, ++row.Run); }
        else row.Run = 0;
        if (age is long frames)
        {
            frames = Math.Max(0, frames); row.AgeSamples++; row.AgeTotal += frames;
            row.MaxAge = Math.Max(row.MaxAge, frames);
            int bucket = 0;
            while (bucket < row.Ages.Length - 1 && frames > (bucket == 0 ? 0 : 1L << (bucket - 1))) bucket++;
            row.Ages[bucket]++;
        }
    }
    internal static void Correction(int slot, Vector3 before, Vector3 after)
    {
        Slot? row = Current(slot);
        if (row == null || (row.CorrectionSeen && row.CorrectionFrame == NetSession.NetFrame)) return;
        float distance = (after - before).Length;
        if (!float.IsFinite(distance)) return;
        row.CorrectionSeen = true; row.CorrectionFrame = NetSession.NetFrame;
        row.Corrections++; row.CorrectionDistance += distance;
        row.MaxCorrection = Math.Max(row.MaxCorrection, distance);
    }
    internal static NetSlotPresentationSnapshot[] Capture()
    {
        var result = new NetSlotPresentationSnapshot[Slots.Length];
        for (int i = 0; i < Slots.Length; i++)
        {
            Slot row = Slots[i];
            result[i] = new(i, row.Epoch, row.Match, row.Generation, row.Life,
                row.Samples, row.Interpolated, row.Held, row.Unavailable, row.Starved, row.Longest,
                row.AgeSamples, row.AgeTotal, row.MaxAge, (long[])row.Ages.Clone(),
                row.Corrections, row.CorrectionDistance, row.MaxCorrection);
        }
        return result;
    }
    internal static void ResetSlot(int slot) { if ((uint)slot < Slots.Length) Slots[slot].Clear(); }
    internal static void Reset() { foreach (Slot row in Slots) row.Clear(); }
}
