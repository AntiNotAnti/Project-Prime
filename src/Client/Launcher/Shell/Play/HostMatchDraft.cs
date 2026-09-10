using System;
using System.Globalization;
using FruityPrime.Server.Shared;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>
/// Bounded, editable values for the Host/Edit Match form.  It is deliberately
/// separate from <see cref="LobbySnapshot"/>: a draft is never authoritative
/// until the controller sends it and the Node returns a newer snapshot.
/// </summary>
internal sealed class HostMatchDraft
{
    public Guid? SourceLobbyId { get; set; }
    public string Name { get; set; } = "Hunters";
    public int PlayerLimit { get; set; } = 8;
    public int ObserverLimit { get; set; } = 16;
    public LobbySeatPolicy SeatPolicy { get; set; } = LobbySeatPolicy.ImmediateSeat;
    public string MapKey { get; set; } = "";
    public MatchMode Mode { get; set; } = MatchMode.Battle;
    public int BotCount { get; set; }

    public string TimeLimitText { get; set; } = "Default";
    public string ScoreGoalText { get; set; } = "Default";
    public string StartingLivesText { get; set; } = "Default";
    public string ObjectiveTimeGoalText { get; set; } = "Default";
    public int? DamageLevel { get; set; }
    public bool? FriendlyFire { get; set; }
    public bool? AffinityWeapons { get; set; }
    public bool? PlayerRadar { get; set; }
    public bool? OctolithReset { get; set; }

    public static HostMatchDraft FromLobby(LobbySnapshot lobby)
    {
        ArgumentNullException.ThrowIfNull(lobby);
        LobbyRulesOptions rules = LobbyRuleApplicability.RulesFromLobby(lobby);
        return new HostMatchDraft
        {
            SourceLobbyId = lobby.LobbyId,
            Name = lobby.Name,
            PlayerLimit = lobby.PlayerLimit,
            ObserverLimit = lobby.ObserverLimit,
            SeatPolicy = lobby.SeatPolicy,
            MapKey = lobby.MapKey,
            Mode = lobby.Mode,
            BotCount = lobby.BotCount,
            TimeLimitText = FormatTime(rules.TimeLimitSeconds),
            ScoreGoalText = FormatNumber(rules.ScoreGoal),
            StartingLivesText = FormatNumber(rules.StartingLives),
            ObjectiveTimeGoalText = FormatTime(rules.ObjectiveTimeGoalSeconds),
            DamageLevel = rules.DamageLevel,
            FriendlyFire = rules.FriendlyFire,
            AffinityWeapons = rules.AffinityWeapons,
            PlayerRadar = rules.PlayerRadar,
            OctolithReset = rules.OctolithReset
        };
    }

    public bool TryBuildRules(out LobbyRulesOptions rules, out string error)
    {
        LobbyRuleApplicability applicability = LobbyRuleApplicability.For(Mode);
        if (!TryParseTime(TimeLimitText, out int? time))
        {
            rules = LobbyRulesOptions.Empty;
            error = "Time limit must be Default, seconds, or m:ss between 0:01 and 60:00.";
            return false;
        }

        int? score = null;
        if (applicability.ScoreGoal && !TryParseNumber(ScoreGoalText, out score))
        {
            rules = LobbyRulesOptions.Empty;
            error = "Score limit must be Default or a whole number between 1 and 65535.";
            return false;
        }

        int? lives = null;
        if (applicability.StartingLives && !TryParseNumber(StartingLivesText, out lives))
        {
            rules = LobbyRulesOptions.Empty;
            error = "Lives must be Default or a whole number between 1 and 65535.";
            return false;
        }

        int? objective = null;
        if (applicability.ObjectiveTimeGoal
            && !TryParseTime(ObjectiveTimeGoalText, out objective))
        {
            rules = LobbyRulesOptions.Empty;
            error = "Objective time must be Default, seconds, or m:ss between 0:01 and 60:00.";
            return false;
        }

        if (DamageLevel is < 0 or > 2)
        {
            rules = LobbyRulesOptions.Empty;
            error = "Damage level is not supported by this match configuration.";
            return false;
        }

        try
        {
            rules = new LobbyRulesOptions(time, score, lives, objective, DamageLevel,
                FriendlyFire, AffinityWeapons, PlayerRadar, OctolithReset).ForMode(Mode);
            error = "";
            return true;
        }
        catch (ArgumentException exception)
        {
            rules = LobbyRulesOptions.Empty;
            error = exception.Message;
            return false;
        }
    }

    internal static bool TryParseTime(string? text, out int? seconds)
    {
        string value = text?.Trim() ?? "";
        if (value.Length == 0 || value.Equals("Default", StringComparison.OrdinalIgnoreCase))
        {
            seconds = null;
            return true;
        }
        if (Int32.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture,
            out int rawSeconds))
        {
            seconds = rawSeconds is >= 1 and <= 3600 ? rawSeconds : null;
            return seconds.HasValue;
        }

        string[] parts = value.Split(':');
        if (parts.Length == 2
            && Int32.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture,
                out int minutes)
            && Int32.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture,
                out int remainder)
            && minutes >= 0 && remainder is >= 0 and < 60)
        {
            long total = (long)minutes * 60 + remainder;
            seconds = total is >= 1 and <= 3600 ? (int)total : null;
            return seconds.HasValue;
        }
        seconds = null;
        return false;
    }

    internal static bool TryParseNumber(string? text, out int? number)
    {
        string value = text?.Trim() ?? "";
        if (value.Length == 0 || value.Equals("Default", StringComparison.OrdinalIgnoreCase))
        {
            number = null;
            return true;
        }
        if (Int32.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture,
            out int parsed) && parsed is >= 1 and <= ushort.MaxValue)
        {
            number = parsed;
            return true;
        }
        number = null;
        return false;
    }

    internal static string FormatTime(int? seconds)
        => seconds is { } value ? $"{value / 60}:{value % 60:00}" : "Default";

    internal static string FormatNumber(int? value)
        => value?.ToString(CultureInfo.InvariantCulture) ?? "Default";
}
