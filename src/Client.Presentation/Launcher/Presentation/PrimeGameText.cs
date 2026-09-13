using System;
using System.Collections.Generic;
using System.Text;
using MphRead.Formats;
using MphRead.Mods.Accounts;
using ProjectPrime.Server.Shared;

namespace MphRead.Mods.Launcher.Presentation;

/// <summary>
/// Canonical player-facing names for game and lobby values.
///
/// These methods intentionally use exhaustive switches for the currently
/// reviewed contract values. A future named mode gets a bounded PascalCase
/// presentation, while an unknown numeric value is shown as unavailable rather
/// than leaking an internal identifier into the launcher.
/// </summary>
public static class PrimeGameText
{
    private static readonly IReadOnlyDictionary<string, string> MapLabels
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["MP1 SANCTORUS"] = "Data Shrine",
            ["MP2 HARVESTER"] = "Harvester",
            ["MP3 PROVING GROUND"] = "Combat Hall",
            ["MP4 HIGHGROUND - EXPANDED"] = "Elder Passage",
            ["MP4 HIGHGROUND"] = "High Ground",
            ["MP5 FUEL SLUICE"] = "Compression Chamber",
            ["MP6 HEADSHOT"] = "Head Shot",
            ["MP7 PROCESSOR CORE"] = "Processor Core",
            ["MP8 FIRE CONTROL"] = "Weapons Complex",
            ["MP9 CRYOCHASM"] = "Ice Hive",
            ["MP10 OVERLOAD"] = "Incubation Vault",
            ["MP11 BREAKTHROUGH"] = "Sanctorus",
            ["MP12 SIC TRANSIT"] = "Sic Transit",
            ["MP13 ACCELERATOR"] = "Fuel Stack",
            ["MP14 OUTER REACH"] = "Outer Reach",
            ["CTF1 FAULT LINE - EXPANDED"] = "Fault Line",
            ["CTF1_FAULT LINE"] = "Subterranean",
            ["AD1 TRANSFER LOCK BT"] = "Transfer Lock",
            ["AD1 TRANSFER LOCK DM"] = "Transfer Lock",
            ["AD2 MAGMA VENTS"] = "Council Chamber",
            ["AD2 ALINOS PERCH"] = "Alinos Perch",
            ["UNIT1 ALINOS LANDFALL"] = "Alinos Gateway",
            ["UNIT2 LANDING BAY"] = "Celestial Gateway",
            ["UNIT 3 VESPER STARPORT"] = "VDO Gateway",
            ["UNIT 4 ARCTERRA BASE"] = "Arcterra Gateway",
            ["Gorea Prison"] = "Oubliette",
            ["E3 FIRST HUNT"] = "Stasis Bunker",
            ["biodefense chamber 06"] = "Early Processor Core",
            ["biodefense chamber 05"] = "Early Stasis Bunker",
            ["biodefense chamber 03"] = "Early Head Shot",
            ["biodefense chamber 08"] = "Early Fuel Stack",
            ["biodefense chamber 04"] = "Early Sanctorus",
            ["biodefense chamber 07"] = "Early Sic Transit",
            ["Level MP1"] = "Trooper Module",
            ["Level MP2"] = "Assault Cradle",
            ["Level MP3"] = "Ancient Vestige",
            ["Level MP5"] = "Early Head Shot (First Hunt)",
            ["Level MP1b"] = "Trooper Module",
            ["E3 level"] = "Stasis Bunker (First Hunt)",
            // These names are used by older launcher capture fixtures as
            // already-resolved map keys; retaining them avoids re-expanding a
            // friendly name into a fabricated identifier.
            ["COMBAT HALL"] = "Combat Hall",
            ["ALINOS PERCH"] = "Alinos Perch"
        };

    public static string OutcomeLabel(CareerOutcome outcome)
        => outcome switch
        {
            CareerOutcome.FinishedWin => "Victory",
            CareerOutcome.FinishedLoss => "Defeat",
            CareerOutcome.Tie => "Draw",
            CareerOutcome.Forfeit => "Forfeit",
            CareerOutcome.DepartedGraceExpired => "Disconnected",
            CareerOutcome.NoContest => "No Contest",
            _ => "Outcome unavailable"
        };

    public static string ModeLabel(MatchMode mode)
        => mode switch
        {
            MatchMode.Battle => "Battle",
            MatchMode.TeamBattle => "Team Battle",
            MatchMode.Survival => "Survival",
            MatchMode.TeamSurvival => "Team Survival",
            MatchMode.Capture => "Capture",
            MatchMode.Bounty => "Bounty",
            MatchMode.TeamBounty => "Team Bounty",
            MatchMode.Nodes => "Nodes",
            MatchMode.TeamNodes => "Team Nodes",
            MatchMode.Defender => "Defender",
            MatchMode.TeamDefender => "Team Defender",
            MatchMode.PrimeHunter => "Prime Hunter",
            _ => FutureModeLabel(mode)
        };

    public static string BotDifficultyLabel(BotDifficulty difficulty)
        => difficulty switch
        {
            BotDifficulty.Beginner => "Beginner",
            BotDifficulty.Easy => "Easy",
            BotDifficulty.Normal => "Normal",
            BotDifficulty.Hard => "Hard",
            BotDifficulty.Expert => "Expert",
            _ => "Difficulty unavailable"
        };

    public static string HunterLabel(Hunter hunter)
        => hunter switch
        {
            Hunter.Samus => "Samus",
            Hunter.Kanden => "Kanden",
            Hunter.Trace => "Trace",
            Hunter.Sylux => "Sylux",
            Hunter.Noxus => "Noxus",
            Hunter.Spire => "Spire",
            Hunter.Weavel => "Weavel",
            Hunter.Guardian => "Guardian",
            Hunter.Random => "Random",
            _ => "Hunter unavailable"
        };

    public static string BeamLabel(BeamType beam)
        => beam switch
        {
            BeamType.None => "No weapon",
            BeamType.PowerBeam => "Power Beam",
            BeamType.VoltDriver => "Volt Driver",
            BeamType.Missile => "Missile",
            BeamType.Battlehammer => "Battlehammer",
            BeamType.Imperialist => "Imperialist",
            BeamType.Judicator => "Judicator",
            BeamType.Magmaul => "Magmaul",
            BeamType.ShockCoil => "Shock Coil",
            BeamType.OmegaCannon => "Omega Cannon",
            BeamType.Platform => "Platform",
            BeamType.Enemy => "Enemy",
            _ => "Weapon unavailable"
        };

    public static string LobbyPhaseLabel(LobbyPhase phase)
        => phase switch
        {
            LobbyPhase.Open => "Open",
            LobbyPhase.StartingMatch => "Starting",
            LobbyPhase.InMatch => "In match",
            LobbyPhase.PostMatch => "Between rounds",
            LobbyPhase.Closing => "Closing",
            _ => "Match state unavailable"
        };

    public static string SeatPolicyLabel(LobbySeatPolicy policy)
        => policy switch
        {
            LobbySeatPolicy.ImmediateSeat => "Immediate seat",
            LobbySeatPolicy.NextMatchSeat => "Next match seat",
            LobbySeatPolicy.ObserverUntilNextMatch => "Observer until next match",
            _ => "Seat policy unavailable"
        };

    /// <summary>
    /// Resolve a known content key to its in-game map name. Unknown keys are
    /// trimmed and preserved because they may be valid custom content IDs; no
    /// synthetic map name is generated for an unrecognized key.
    /// </summary>
    public static string MapName(string? mapKey)
    {
        if (String.IsNullOrWhiteSpace(mapKey)) return "Map unavailable";
        string key = mapKey.Trim();
        return MapLabels.TryGetValue(key, out string? label) ? label : key;
    }

    /// <summary>
    /// Format a bounded career favorite without treating a backend key as an
    /// enum display name. The supported dimensions are map, hunter, mode, and
    /// weapon; an omitted dimension remains compatible with the historic map
    /// choice helper.
    /// </summary>
    public static string CareerChoiceLabel(CareerChoice? choice, string? dimension = null)
    {
        if (choice is null) return "—";
        string kind = dimension?.Trim() ?? "";
        if (kind.Length == 0 || kind.Equals("map", StringComparison.OrdinalIgnoreCase))
            return MapName(choice.Key);

        if (kind.Equals("hunter", StringComparison.OrdinalIgnoreCase))
        {
            if (TryParseInt(choice.Key, byte.MinValue, byte.MaxValue, out int id))
                return HunterLabel((Hunter)id);
            return Enum.TryParse(choice.Key, ignoreCase: true, out Hunter hunter)
                ? HunterLabel(hunter) : "Hunter unavailable";
        }

        if (kind.Equals("mode", StringComparison.OrdinalIgnoreCase))
        {
            if (TryParseInt(choice.Key, byte.MinValue, byte.MaxValue, out int id))
                return ModeLabel((MatchMode)id);
            return Enum.TryParse(choice.Key, ignoreCase: true, out MatchMode mode)
                ? ModeLabel(mode) : "Mode unavailable";
        }

        if (kind.Equals("weapon", StringComparison.OrdinalIgnoreCase))
        {
            if (TryParseInt(choice.Key, sbyte.MinValue, sbyte.MaxValue, out int id))
                return BeamLabel((BeamType)id);
            return Enum.TryParse(choice.Key, ignoreCase: true, out BeamType beam)
                ? BeamLabel(beam) : "Weapon unavailable";
        }

        // Unexpected dimensions may contain internal backend keys. Keep the
        // fallback useful without exposing implementation identifiers.
        return "Favorite unavailable";
    }

    private static bool TryParseInt(string value, int minimum, int maximum,
        out int result)
        => Int32.TryParse(value, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out result)
            && result >= minimum && result <= maximum;

    private static string FutureModeLabel(MatchMode mode)
    {
        string? name = Enum.GetName(typeof(MatchMode), mode);
        if (name is null) return "Mode unavailable";

        string label = SplitPascalCase(name);
        return label.Length == 0 ? "Mode unavailable" : label;
    }

    private static string SplitPascalCase(string value)
    {
        var builder = new StringBuilder(value.Length + 8);
        for (int i = 0; i < value.Length; i++)
        {
            char current = value[i];
            if (!Char.IsLetterOrDigit(current))
            {
                AppendSpace(builder);
                continue;
            }

            bool startsWord = builder.Length > 0 && Char.IsUpper(current)
                && (Char.IsLower(value[i - 1]) || Char.IsDigit(value[i - 1])
                    || Char.IsUpper(value[i - 1]) && i + 1 < value.Length
                        && Char.IsLower(value[i + 1]));
            if (startsWord) AppendSpace(builder);
            builder.Append(current);
        }
        return builder.ToString().Trim();
    }

    private static void AppendSpace(StringBuilder builder)
    {
        if (builder.Length > 0 && builder[^1] != ' ')
            builder.Append(' ');
    }
}
