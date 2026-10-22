using System;

namespace MphRead.Mods.Replay;

internal readonly record struct ReplayExportSample(uint SimulationFrame, double Frame, float Alpha);

/// <summary>Rational output samples over an inclusive selection on a 60 Hz replay.
/// Integer arithmetic prevents cumulative drift; only the final render time is fractional.</summary>
internal readonly struct ReplayExportSampler
{
    internal uint StartFrame { get; }
    internal int Fps { get; }
    internal long Count { get; }

    internal ReplayExportSampler(uint start, uint end, int fps)
    {
        if (fps <= 0) throw new ArgumentOutOfRangeException(nameof(fps));
        if (end < start) throw new ArgumentOutOfRangeException(nameof(end));
        StartFrame = start;
        Fps = fps;
        Count = (long)(end - start) * fps / 60 + 1;
    }

    internal ReplayExportSample At(long index)
    {
        if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
        long offset = index * 60;
        uint whole = checked(StartFrame + (uint)(offset / Fps));
        long remainder = offset % Fps;
        return new(checked(whole + (remainder == 0 ? 0u : 1u)),
            whole + remainder / (double)Fps,
            remainder == 0 ? 1 : remainder / (float)Fps);
    }
}
