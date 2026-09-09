using MphRead.Mods.Network;
using MphRead.Hud;
using MphRead.Text;

namespace MphRead.Entities;

public partial class PlayerPresentation
{
    /// <summary>Shows only server-selected diagnostic tick facts.</summary>
    private void DrawHistoricalCollisionDebug(HistoricalCollisionDebugPacket packet)
    {
        string mode = packet.Mode == HistoricalCollisionDebugMode.History ? "HISTORY" : "DYNAMIC";
        string text = $"LAGCOMP {mode}  N{packet.CurrentTick} Q{packet.QueryTick} R{packet.RewindTicks}";
        DrawText2D(4, 4, Align.Left, 0, text, new ColorRgba(255, 220, 80, 255),
            maxLength: 52, scale: .52f);
        HistoricalCollisionDebugMetrics metrics = packet.Metrics;
        string counters = $"H{metrics.DynamicHistoryRecords} Q{metrics.DynamicHistoryQueries} M{metrics.DynamicHistoryMissing} "
            + $"D{metrics.HistoricalDoorQueries} F{metrics.HistoricalForceFieldQueries} "
            + $"P{metrics.HistoricalPlatformQueries} C{metrics.HistoricalGeometryChangedOutcome}";
        DrawText2D(4, 12, Align.Left, 0, counters, new ColorRgba(255, 220, 80, 255),
            maxLength: 72, scale: .42f);
    }
}
