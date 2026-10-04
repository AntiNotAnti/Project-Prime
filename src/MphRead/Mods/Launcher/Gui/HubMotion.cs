#if MPHREAD_AVALONIA
using System;
using Avalonia.Controls;

namespace MphRead.Mods.Launcher.Gui
{
    /// <summary>Compatibility entry point for older hub call sites.</summary>
    internal static class HubMotion
    {
        public static void Enter(Control control, double lift = 10, int frames = 9)
        {
            PrimeMotion.Enter(control, lift, Math.Max(1, frames) / 60.0);
        }
    }
}
#endif
