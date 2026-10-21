using System;
using System.IO;

namespace MphRead.Mods.Network;

internal enum ReplayCapturePhase : byte { Ordinary, PostWorld }

// A borrowed immutable fact with a capture-owner decision, not a second clock or
// a network envelope. Each asynchronous/queued owner must retain its own reference.
internal readonly record struct ReplayCaptureRecord(ReplayTimelineRecord Record, ReplayCapturePhase Phase)
{
    internal void Retain() => Record.Retain();
    internal void Release() => Record.Release();
}

// One simulation-thread owner classifies facts for BOTH private reconstruction
// and storage. Completion means a successfully reconstructed world, not an input
// receipt or an authority tick inferred from a packet payload.
internal sealed class ReplayCapturePhaseClock(uint completedFrame)
{
    internal uint CompletedFrame { get; private set; } = completedFrame;
    internal ReplayCapturePhase Classify(uint sourceFrame)
    {
        if (sourceFrame == CompletedFrame) return ReplayCapturePhase.PostWorld;
        if ((ulong)sourceFrame == (ulong)CompletedFrame + 1) return ReplayCapturePhase.Ordinary;
        throw new InvalidDataException("Capture facts must belong to the completed world or its following step.");
    }
    internal void Complete(uint frame)
    {
        if ((ulong)frame != (ulong)CompletedFrame + 1)
            throw new InvalidDataException("Capture worlds must complete contiguously.");
        CompletedFrame = frame;
    }
}
