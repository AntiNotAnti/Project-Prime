using System;

namespace MphRead.Mods.UI.Theme;

public static class UiMotion
{
    public static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan Standard = TimeSpan.FromMilliseconds(180);
    public static readonly TimeSpan Slow = TimeSpan.FromMilliseconds(260);

    public static TimeSpan Select(TimeSpan duration, bool reducedMotion)
        => reducedMotion ? TimeSpan.Zero : duration;
}
