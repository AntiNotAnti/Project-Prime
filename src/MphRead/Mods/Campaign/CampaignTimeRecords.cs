using System;
using System.Globalization;

namespace MphRead.Mods.Campaign;

/// <summary>Optional fixed-step campaign observations. Missing history is never initialized by loading a save.</summary>
public static class CampaignTimeRecords
{
    public const int CurrentVersion = 1, TicksPerSecond = 60;
    public sealed class Record
    {
        public int Version { get; set; }
        public long? PlayTicks { get; set; }
        public long? GoreaFirstTicks { get; set; }
        public long? GoreaSecondTicks { get; set; }
    }
    public readonly record struct Snapshot(long? PlayTicks, long? GoreaTicks);
    internal static Record CreateNew() => new()
    {
        Version = CurrentVersion, PlayTicks = 0, GoreaFirstTicks = 0, GoreaSecondTicks = 0
    };

    /// <summary>Called once by the existing eligible 60 Hz elapsed-time owner, never by drawing or menus.</summary>
    public static void Advance(StorySave.SaveStats? stats, int roomId)
    {
        if (stats?.CampaignTime is not { Version: CurrentVersion } record) return;
        record.PlayTicks = Next(record.PlayTicks);
        if (roomId == CampaignNavigation.GoreaFirstRoomId) record.GoreaFirstTicks = Next(record.GoreaFirstTicks);
        else if (roomId == CampaignNavigation.GoreaSecondRoomId) record.GoreaSecondTicks = Next(record.GoreaSecondTicks);
    }
    private static long? Next(long? value) => value is >= 0 and < long.MaxValue ? value.Value + 1 : null;
    private static long? Known(long? value) => value is >= 0 ? value : null;
    public static Snapshot Capture(StorySave.SaveStats? stats)
    {
        if (stats?.CampaignTime is not { Version: CurrentVersion } record) return default;
        long? first = Known(record.GoreaFirstTicks), second = Known(record.GoreaSecondTicks);
        long? gorea = first is long a && second is long b && a <= long.MaxValue - b ? a + b : null;
        return new(Known(record.PlayTicks), gorea);
    }
    public static string FormatTotal(long? ticks) => Known(ticks) is long value
        ? string.Create(CultureInfo.InvariantCulture, $"{value / TicksPerSecond / 3600}h {value / TicksPerSecond % 3600 / 60:00}m")
        : "NOT AVAILABLE";
    /// <summary>The authored result format intentionally drops whole hours from the Gorea minutes field.</summary>
    public static string FormatGorea(long? ticks) => Known(ticks) is long value
        ? string.Create(CultureInfo.InvariantCulture, $"{value / TicksPerSecond % 3600 / 60}m {value / TicksPerSecond % 60:00}s")
        : "NOT AVAILABLE";
}
