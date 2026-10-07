using System;
using System.IO;
using System.Diagnostics;
using System.Globalization;

namespace MphRead.Platform;

public enum ProcessLifetimePresence { Exited, VerifiedAlive, UnverifiedAlive }

/// <summary>Exact OS process incarnation; Linux UTC boot estimates differ between managed processes.</summary>
public static class ProcessLifetimeIdentity
{
    public static string Capture(Process process)
    {
        if (process.HasExited) throw new InvalidOperationException("The worker has exited.");
        string token;
        if (OperatingSystem.IsLinux() || OperatingSystem.IsAndroid())
        {
            string boot = File.ReadAllText("/proc/sys/kernel/random/boot_id").Trim();
            string stat = File.ReadAllText($"/proc/{process.Id}/stat");
            if (!TryLinuxToken(process.Id, boot, stat, out token, out bool exited))
                throw new InvalidDataException("The kernel process identity could not be read.");
            if (exited) throw new InvalidOperationException("The worker has exited.");
        }
        else
        {
            string platform = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : throw new PlatformNotSupportedException();
            token = $"{platform}:1:{process.Id.ToString(CultureInfo.InvariantCulture)}:{process.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture)}";
        }
        if (process.HasExited) throw new InvalidOperationException("The worker has exited.");
        return token;
    }

    public static ProcessLifetimePresence Assess(Process process, string? token, long? legacyUtcTicks = null)
        => AssessCore(process, token, legacyUtcTicks, null);

    internal static ProcessLifetimePresence AssessUsingIdentityReader(Process process, string? token, Func<Process, string> readIdentity, long? legacyUtcTicks = null)
        => AssessCore(process, token, legacyUtcTicks, readIdentity);

    private static ProcessLifetimePresence AssessCore(Process process, string? token, long? legacyUtcTicks, Func<Process, string>? readIdentity)
    {
        try
        {
            if (process.HasExited) return ProcessLifetimePresence.Exited;
            if (token == null)
            {
                // UTC timestamps in an older Linux record cannot establish
                // identity across managed processes. Reserve conservatively.
                if (OperatingSystem.IsLinux() || OperatingSystem.IsAndroid()) return ProcessLifetimePresence.UnverifiedAlive;
                if (legacyUtcTicks is not > 0) return ProcessLifetimePresence.UnverifiedAlive;
                return process.StartTime.ToUniversalTime().Ticks == legacyUtcTicks
                    ? ProcessLifetimePresence.VerifiedAlive : ProcessLifetimePresence.Exited;
            }
            string platform = OperatingSystem.IsLinux() || OperatingSystem.IsAndroid() ? "linux" : OperatingSystem.IsWindows() ? "windows" : "macos";
            if (!ValidToken(token, process.Id, platform)) return ProcessLifetimePresence.UnverifiedAlive;
            return string.Equals(token, readIdentity == null ? Capture(process) : readIdentity(process), StringComparison.Ordinal)
                ? ProcessLifetimePresence.VerifiedAlive : ProcessLifetimePresence.Exited;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        { return ProcessLifetimePresence.Exited; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
        {
            // A missing boot ID or inaccessible procfs is not evidence that
            // this PID exited. Only the process query can establish that.
            try { return process.HasExited ? ProcessLifetimePresence.Exited : ProcessLifetimePresence.UnverifiedAlive; }
            catch (Exception unavailable) when (unavailable is InvalidOperationException or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
            { return ProcessLifetimePresence.UnverifiedAlive; }
        }
    }

    public static bool Matches(Process process, string? token, long? legacyUtcTicks = null)
        => Assess(process, token, legacyUtcTicks) == ProcessLifetimePresence.VerifiedAlive;

    internal static bool TryLinuxToken(int expectedPid, string boot, string stat, out string token, out bool exited)
    {
        token = ""; exited = false;
        if (expectedPid <= 0 || boot.Length != 36 || !Guid.TryParseExact(boot, "D", out var bootId) || stat.Length > 8192) return false;
        int open = stat.IndexOf('('), close = stat.LastIndexOf(')');
        if (open <= 0 || close <= open || !int.TryParse(stat.AsSpan(0, open).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int pid) || pid != expectedPid) return false;
        // comm may contain spaces and parentheses. The fields after its final
        // closing parenthesis begin with field3; kernel starttime is field22.
        string[] fields = stat[(close + 1)..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 20 || fields[0].Length != 1 || !"RSDZTtWXxKPI".Contains(fields[0][0])
            || !ulong.TryParse(fields[19], NumberStyles.None, CultureInfo.InvariantCulture, out ulong start) || start == 0) return false;
        exited = fields[0] is "Z" or "X" or "x";
        token = $"linux:1:{bootId:D}:{pid.ToString(CultureInfo.InvariantCulture)}:{start.ToString(CultureInfo.InvariantCulture)}";
        return true;
    }

    private static bool ValidToken(string token, int pid, string platform)
    {
        if (token.Length > 128) return false;
        string[] fields = token.Split(':');
        if (fields.Length != (platform == "linux" ? 5 : 4) || fields[0] != platform || fields[1] != "1") return false;
        int pidIndex = platform == "linux" ? 3 : 2;
        if (fields[pidIndex] != pid.ToString(CultureInfo.InvariantCulture)) return false;
        if (platform == "linux" && (!Guid.TryParseExact(fields[2], "D", out var boot) || fields[2] != boot.ToString("D"))) return false;
        string ticks = fields[^1];
        return ulong.TryParse(ticks, NumberStyles.None, CultureInfo.InvariantCulture, out ulong start) && start > 0
            && ticks == start.ToString(CultureInfo.InvariantCulture) && (platform == "linux" || start <= (ulong)DateTime.MaxValue.Ticks);
    }
}
