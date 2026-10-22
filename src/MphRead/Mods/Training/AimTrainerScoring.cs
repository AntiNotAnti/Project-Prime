using System;
namespace MphRead.Mods.Training;
public static class AimTrainerScoring
{
    public static int Hit(bool headshot, bool headsOnly, bool firstShot, int acquisitionFrames, int streak)
    {
        if (headsOnly) return headshot ? 100 : 0;
        return ((headshot ? 100 : 50) + (firstShot ? 25 : 0) + Math.Clamp(60 - acquisitionFrames, 0, 60))
            * (1 + Math.Min(Math.Max(0, streak) / 10, 3));
    }
}
