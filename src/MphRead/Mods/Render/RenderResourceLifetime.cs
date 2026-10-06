using System;
using System.Threading;

namespace MphRead.Mods.Render;

/// <summary>A terminal release always drops managed owners, even if native deletion fails.
/// Native deletion must be requested only by the graphics owner with a healthy context.</summary>
internal static class RenderResourceLifetime
{
    [ThreadStatic] private static bool? _nativeReleaseEligibility;
    internal static bool CanReleaseNativeInCurrentScope => _nativeReleaseEligibility != false;

    internal static void WithNativeReleaseEligibility(bool canReleaseNativeResources, Action cleanup)
    {
        bool? previous = _nativeReleaseEligibility;
        _nativeReleaseEligibility = canReleaseNativeResources && CanReleaseNativeInCurrentScope;
        try { cleanup(); }
        finally { _nativeReleaseEligibility = previous; }
    }

    internal static void Release(ref int released, bool canReleaseNativeResources,
        Action releaseNative, Action releaseManaged)
    {
        if (Interlocked.Exchange(ref released, 1) != 0) return;
        try
        {
            if (canReleaseNativeResources && CanReleaseNativeInCurrentScope) releaseNative();
        }
        finally { releaseManaged(); }
    }
}
