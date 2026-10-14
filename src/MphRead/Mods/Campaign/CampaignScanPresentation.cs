using System;

namespace MphRead.Mods.Campaign
{
    /// <summary>
    /// Render-only scan progress quantization at native HUD's 120 units/sec.
    /// Does not determine target eligibility, scan completion, logbook writes
    /// or gameplay duration: PlayerScan retains those authoritative decisions.
    /// </summary>
    public static class CampaignScanPresentation
    {
        public static (int Total, int Filled) Meter(float duration, float elapsed)
        {
            if (!Single.IsFinite(duration) || duration <= 0)
                return (1, 1);
            // Avoid overflowing HUD integer meters for malformed scan tables.
            int total = Math.Clamp((int)MathF.Ceiling(Math.Min(duration, 300f) * 120f), 1, 36000);
            float safeElapsed = Single.IsFinite(elapsed) ? Math.Clamp(elapsed, 0, duration) : 0;
            int filled = Math.Clamp((int)MathF.Round(Math.Min(safeElapsed, 300f) * 120f),
                0, total);
            return (total, filled);
        }
    }
}
