using System;
using System.Collections.Generic;
using System.IO;
using MphRead.Mods;
using Xunit;

namespace MphRead.Tests.Client;

public sealed class DebugLogTests
{
    [Fact]
    public void SanitizesSeparateAndInlineSensitiveOptions()
    {
        string sanitized = DebugLog.SanitizeArguments(new[]
        {
            "ProjectPrime", "--token=abc", "--resume-token", "def", "--room", "SANCTORUS"
        });

        Assert.Equal("ProjectPrime --token=<redacted> --resume-token <redacted> --room SANCTORUS", sanitized);
        Assert.DoesNotContain("abc", sanitized);
        Assert.DoesNotContain("def", sanitized);
    }

    [Fact]
    public void SanitizesUrlsAndConnectionStringValues()
    {
        string sanitized = DebugLog.SanitizeArguments(new[]
        {
            "https://user:pass@example.test/path?token=query-secret",
            "Host=db;Password=db-secret;User=prime"
        });

        Assert.DoesNotContain("user:pass", sanitized);
        Assert.DoesNotContain("query-secret", sanitized);
        Assert.DoesNotContain("db-secret", sanitized);
        Assert.Contains("<redacted>@example.test", sanitized);
        Assert.Contains("Password=<redacted>", sanitized);
    }

    [Fact]
    public void SessionPathsArePairedAndCollisionSafe()
    {
        string directory = Path.Combine("tmp", "logs");
        DateTime timestamp = new(2026, 9, 9, 12, 34, 56);
        var occupied = new HashSet<string>(StringComparer.Ordinal)
        {
            Path.Combine(directory, "ProjectPrime-20260909-123456.log")
        };

        (string managed, string native) = DebugLog.CreateSessionPaths(directory,
            "ProjectPrime", timestamp, occupied.Contains);
        Assert.EndsWith("ProjectPrime-20260909-123456-2.log", managed);
        Assert.EndsWith("ProjectPrime-20260909-123456-2-native.log", native);
        Assert.Equal(DebugLog.SessionKey(Path.GetFileName(managed)),
            DebugLog.SessionKey(Path.GetFileName(native)));
    }

    [Fact]
    public void ProcessEventHooksInstallAtMostOnce()
    {
        DebugLog.EnsureProcessEventHooks();
        int installed = DebugLog.ProcessEventHookInstallCount;
        DebugLog.EnsureProcessEventHooks();
        Assert.InRange(installed, 1, 1);
        Assert.Equal(installed, DebugLog.ProcessEventHookInstallCount);
    }
}
