using System;
namespace MphRead.Mods.HeroModes;

/// <summary>Presentation consumes synchronized state, never wall-clock time or gameplay mutations.</summary>
internal static class CtzPresentation
{
    internal const string ModeTitle="CAPTURE THE ZONE";
    internal static bool ShowRoundStartBanner(CtzSnapshot s)
        => s.Phase==CtzPhase.Active && s.RoundElapsedTicks<CtzRules.RoundStartBannerTicks;
    internal static string RoundTime(CtzSnapshot s)
        => $"{s.RoundElapsedTicks/3600:00}:{s.RoundElapsedTicks/60%60:00}";
    internal static string ObjectiveStatus(CtzSnapshot s) => s.Phase switch
    {
        CtzPhase.Countdown => $"ROUND {s.Round} STARTS IN {(s.Remaining+59)/60}",
        CtzPhase.Intermission => $"SWITCHING SIDES IN {(s.Remaining+59)/60}",
        CtzPhase.Finished => "",
        _ => s.Unlock>0 ? $"ZONE UNLOCKS IN {(s.Unlock+59)/60}" : s.Contested ? "CONTESTED"
            : s.Capturing>=0 ? $"{(s.Capturing==0?"RED":"BLUE")} CAPTURING {s.Capture*100/CtzRules.CaptureTicks}%"
            : s.Owner<0 ? "" : $"{(s.Owner==0?"RED":"BLUE")} CONTROLS THE ZONE"
    };
    internal static string TeamScore(CtzSnapshot s, int team)
        => $"{(team==0?"RED":"BLUE")}  {(team==0?s.Red:s.Blue)/60f:0}%   |   ROUNDS {(team==0?s.RedWins:s.BlueWins)}/2";
}
