using System;
using System.Globalization;

namespace MphRead.Droid;

/// <summary>Pure helpers for ART runtime-stat deltas; unsupported OEM stats remain unknown.</summary>
internal static class AndroidGcTelemetry
{
    internal static long? ParseCounter(string? raw)
        => long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n) && n >= 0
            ? n : null;

    internal static string CounterDelta(long? current, long? previous)
        => current.HasValue && previous.HasValue && current.Value >= previous.Value
            ? (current.Value - previous.Value).ToString(CultureInfo.InvariantCulture) : "n/a";
}
