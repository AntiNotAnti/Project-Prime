using System;
using MphRead;

namespace MphRead.NetTest;

/// <summary>
/// Shared command-line/profile plumbing for content-backed diagnostics.  The
/// default remains Classic so existing scripts retain their exact behavior;
/// Balanced is an explicit opt-in at the diagnostic boundary.
/// </summary>
internal static class BalanceProfileOptions
{
    internal const string BalancedToken = "balanced";
    private static readonly string[] ClassicParseArgs =
        ["--weapon-policy", "AMHE1-DATA"];
    private static readonly string[] BalancedParseArgs =
        ["--catch-up", "AMHE1-DATA", "AMHE1", BalancedToken];
    private static readonly string[] BalancedFlagParseArgs =
        ["--homing", "AMHE1-DATA", "--balanced"];

    /// <summary>
    /// Parses the small legacy shape used by the deterministic content checks:
    /// <c>COMMAND DATA [VERSION] [balanced]</c>.  The trailing token is kept
    /// for the older commands whose invocations are commonly positional.
    /// </summary>
    internal static bool TryParseDataVersion(string[] args,
        out string data, out string version, out bool balanced)
    {
        data = String.Empty;
        version = "AMHE1";
        balanced = false;
        if (args.Length < 2 || args.Length > 4)
        {
            return false;
        }

        int index = 1;
        data = args[index++];
        if (String.IsNullOrWhiteSpace(data) || data.StartsWith('-'))
        {
            return false;
        }

        if (index < args.Length && !IsBalanceToken(args[index]))
        {
            version = args[index++];
            if (String.IsNullOrWhiteSpace(version) || version.StartsWith('-'))
            {
                return false;
            }
        }

        if (index < args.Length && IsBalanceToken(args[index]))
        {
            balanced = true;
            index++;
        }
        return index == args.Length;
    }

    internal static bool TryGetTrailingProfile(string[] args, int requiredLength,
        out bool balanced)
    {
        balanced = false;
        if (args.Length == requiredLength)
        {
            return true;
        }
        if (args.Length != requiredLength + 1 || !IsBalanceToken(args[^1]))
        {
            return false;
        }
        balanced = true;
        return true;
    }

    internal static MatchRules CreateRules(string room, GameMode mode, bool balanced)
    {
        MatchRules rules = MatchRules.CreateDefault(mode.ToMatchMode(), room);
        return balanced ? rules.With(balancedMode: true) : rules;
    }

    internal static MatchRules CreateRules(string room, MatchMode mode, bool balanced)
    {
        MatchRules rules = MatchRules.CreateDefault(mode, room);
        return balanced ? rules.With(balancedMode: true) : rules;
    }

    internal static int SelfTest()
    {
        int cases = 0;
        Require(TryParseDataVersion(ClassicParseArgs,
            out string classicData, out string classicVersion, out bool classic)
            && classicData == "AMHE1-DATA" && classicVersion == "AMHE1" && !classic,
            "Classic positional profile parse changed.");
        cases++;

        Require(TryParseDataVersion(BalancedParseArgs,
            out string balancedData, out string balancedVersion, out bool balanced)
            && balancedData == "AMHE1-DATA" && balancedVersion == "AMHE1" && balanced,
            "Balanced trailing profile parse failed.");
        cases++;

        Require(TryParseDataVersion(BalancedFlagParseArgs,
            out _, out _, out bool flagBalanced) && flagBalanced,
            "Balanced flag profile parse failed.");
        cases++;

        MatchRules classicRules = CreateRules("MP1 SANCTORUS", GameMode.Battle, false);
        MatchRules balancedRules = CreateRules("MP1 SANCTORUS", GameMode.Battle, true);
        Require(!classicRules.BalancedMode && balancedRules.BalancedMode
            && classicRules.Mode == balancedRules.Mode
            && classicRules.RoomKey == balancedRules.RoomKey,
            "Profile selector changed non-balance match identity.");
        cases++;

        Console.WriteLine($"BALANCEPROFILE PASS cases={cases} classic=0 balanced=1");
        return 0;
    }

    private static bool IsBalanceToken(string value)
        => String.Equals(value, BalancedToken, StringComparison.OrdinalIgnoreCase)
            || String.Equals(value, "--balanced", StringComparison.OrdinalIgnoreCase);

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
