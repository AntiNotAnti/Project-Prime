using System;
using System.Collections.Generic;
namespace MphRead.Mods.HeroModes;

internal readonly record struct CtzScoreColumn(string Top, string Bottom, float Left, float Width)
{
    internal float Right => Left + Width;
}

internal static class CtzScoreboardLayout
{
    // All geometry is in the existing 256x192 HUD plane, including dual-screen 5:3.
    internal const float HeaderY = 30, SubheaderY = 37, FirstTeamY = 49, RowHeight = 13;
    internal static CtzScoreColumn[] Columns(float left, float right, bool ping)
    {
        string[] top = ping ? ["HUNTER", "KILLS", "DEATHS", "HEALING", "HEALING", "DAMAGE", "PING"]
                            : ["HUNTER", "KILLS", "DEATHS", "HEALING", "HEALING", "DAMAGE"];
        string[] bottom = ping ? ["", "", "", "DONE", "RECEIVED", "TAKEN", "ms"]
                               : ["", "", "", "DONE", "RECEIVED", "TAKEN"];
        float[] weights = ping ? [65, 21, 24, 38, 40, 36, 20] : [74, 22, 24, 40, 43, 41];
        float sum = 244, x = left, scale = (right-left)/sum;
        var result = new CtzScoreColumn[top.Length];
        for (int i=0; i<top.Length; i++)
        {
            float w=weights[i]*scale;
            result[i]=new(top[i],bottom[i],x,w);
            x+=w;
        }
        return result;
    }
    internal static float FitScale(float preferred, float measuredWidth, float measuredHeight, float cellWidth, float cellHeight)
        => Math.Max(.001f, Math.Min(preferred, Math.Min((cellWidth-4)/Math.Max(.01f,measuredWidth), cellHeight/Math.Max(.01f,measuredHeight))));
}

// Enabled only by the opt-in renderer diagnostic. Values describe the actual
// bounded text calls, never a synthetic screenshot or replacement renderer.
internal static class CtzScoreboardEvidence
{
    internal static List<CtzScoreTextBounds>? Capture;
    internal static bool ForcePing;
}
internal readonly record struct CtzScoreTextBounds(string Text,float Left,float Top,float Width,float Height,float CellLeft,float CellWidth,float CellHeight);
