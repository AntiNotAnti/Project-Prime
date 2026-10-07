using System.Diagnostics;
using MphRead.Platform;

namespace ProjectPrime.Studio.Replay;

/// <summary>Studio delegates incarnation checks to the canonical toolkit-free engine primitive.</summary>
internal static class ReplayExportWorkerIdentity
{
    public static string Capture(Process process) => ProcessLifetimeIdentity.Capture(process);
    public static ProcessLifetimePresence Assess(Process process, string? token, long? legacyUtcTicks = null)
        => ProcessLifetimeIdentity.Assess(process, token, legacyUtcTicks);
    public static bool Matches(Process process, string? token, long? legacyUtcTicks = null)
        => ProcessLifetimeIdentity.Matches(process, token, legacyUtcTicks);
}
