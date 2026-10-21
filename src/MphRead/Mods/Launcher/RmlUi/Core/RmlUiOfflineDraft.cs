using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using MphRead.Mods.Launcher;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher.RmlUi.Core;

/// <summary>Local, uncommitted menu choices for an existing offline match.</summary>
public readonly record struct RmlUiOfflineRequest(string RoomKey, GameMode Mode,
    Hunter Hunter, int Suit, int Bots, int BotLevel)
{
    public OfflineCharacterMode Character { get; init; }
    public ImmutableArray<OfflineBotSettings> BotRoster { get; init; }
    public int LocalTeam { get; init; }
    public bool ValidateForm(IReadOnlyList<string> availableRooms)
    {
        GameMode requestedMode = Mode; // A struct method cannot capture this in a lambda.
        return !String.IsNullOrWhiteSpace(RoomKey)
            && availableRooms.Contains(RoomKey, StringComparer.OrdinalIgnoreCase)
            && OfflineLaunch.Modes.Any(x => x.Mode == requestedMode)
            && (Character == OfflineCharacterMode.Native
                || Hunter == Hunter.Weavel && Mode == GameMode.Battle
                    && (Character is OfflineCharacterMode.TrooperWallClingPractice or OfflineCharacterMode.TrooperScoutDronePractice && Bots == 0
                        || Character is OfflineCharacterMode.TrooperScoutDroneCombatPractice or OfflineCharacterMode.TrooperArsenalPractice))
            && (uint)Hunter < 7 && Suit is >= 0 and <= 3
            && Bots is >= 0 and <= 7 && BotLevel is >= 0 and <= 3
            && LocalTeam is >= 0 and <= 1 && OfflineBotSettings.Validate(BotRoster, Bots, lowTier: false);
    }
}

/// <summary>
/// The native page's draft never writes prefs or touches simulation. The
/// same OfflineLaunch.Create and StartScreen.Finish path remain authoritative.
/// </summary>
public sealed class RmlUiOfflineDraft
{
    private string[] _rooms;
    private readonly MatchTypeDefinition[] _types;
    private string[] _filtered = Array.Empty<string>();
    private int _arenaPage;
    private bool _arenaOpen;
    private bool _lowTier;
    private int _selectedBot;
    public const int ArenaPageSize = 6;
    public RmlUiOfflineRequest Current { get; private set; }
    public int SelectedBot => Math.Min(_selectedBot, Math.Max(0, Current.Bots - 1));
    public OfflineBotSettings Bot => Current.BotRoster[SelectedBot];
    public bool HasBots => Current.Bots > 0;
    public bool Teams => GameState.IsTeamMode(Current.Mode);
    public string BotSelection => HasBots ? $"BOT {SelectedBot + 1} / {Current.Bots}" : "NO BOTS";
    public string BotTeam => Bot.Team < 0 ? "AUTO" : "TEAM " + (Bot.Team + 1);
    public bool ArenaOpen => _arenaOpen;
    public int ArenaPage => _arenaPage;
    public int ArenaPageCount => Math.Max(1, (_filtered.Length + ArenaPageSize - 1) / ArenaPageSize);
    public bool ArenaHasPrevious => _arenaPage > 0;
    public bool ArenaHasNext => _arenaPage + 1 < ArenaPageCount;
    public int ArenaCount => _filtered.Length;

    public RmlUiOfflineDraft(IEnumerable<string> rooms, Hunter hunter, int suit, int bots, int level)
    {
        ArgumentNullException.ThrowIfNull(rooms);
        _rooms = rooms.Where(x => !String.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(LauncherRoomDisplayName.Get, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        _types = MatchTypeCatalog.GameTypes;
        Current = new(LauncherPrefs.PreferredMap(_rooms), GameMode.Battle,
            (uint)hunter < 7 ? hunter : Hunter.Samus,
            Math.Clamp(suit, 0, 3), Math.Clamp(bots, 0, 7), Math.Clamp(level, 0, 3));
        EnsureBotRoster();
    }

    public void RefreshArenas(IEnumerable<string> rooms)
    {
        string[] next = rooms.Where(x => !String.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(LauncherRoomDisplayName.Get, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        if (_rooms.SequenceEqual(next, StringComparer.OrdinalIgnoreCase)) return;
        _rooms = next;
        if (!_rooms.Contains(Current.RoomKey, StringComparer.OrdinalIgnoreCase))
            Current = Current with { RoomKey = LauncherPrefs.PreferredMap(_rooms) };
        if (_arenaOpen) SearchArena("");
    }

    public bool OpenArena()
    {
        if (_arenaOpen) return false;
        _arenaOpen = true;
        SearchArena("");
        return true;
    }

    public bool CloseArena()
    {
        if (!_arenaOpen) return false;
        _arenaOpen = false;
        return true;
    }

    public bool SearchArena(string search)
    {
        if (!_arenaOpen || search.Length > 80) return false;
        string query = search.Trim();
        _filtered = _rooms.Where(x => LauncherRoomDisplayName.Get(x).Contains(query, StringComparison.OrdinalIgnoreCase)
            || x.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        _arenaPage = 0;
        return true;
    }

    public bool ArenaTurn(int direction)
    {
        if (!_arenaOpen || (direction != 1 && direction != -1)
            || (direction == 1 && !ArenaHasNext) || (direction == -1 && !ArenaHasPrevious))
            return false;
        _arenaPage += direction;
        return true;
    }

    public string? ArenaChoice(int slot)
    {
        if (!_arenaOpen || slot is < 0 or >= ArenaPageSize) return null;
        int index = _arenaPage * ArenaPageSize + slot;
        return index < _filtered.Length ? _filtered[index] : null;
    }

    public bool SelectArena(int slot)
    {
        string? room = ArenaChoice(slot);
        if (room == null) return false;
        Current = Current with { RoomKey = room };
        _arenaOpen = false;
        return true;
    }

    private bool Step<T>(IReadOnlyList<T> options, T current, Action<T> set, int direction)
    {
        if (options.Count == 0) return false;
        int index = -1;
        for (int i = 0; i < options.Count; i++)
            if (EqualityComparer<T>.Default.Equals(options[i], current)) { index = i; break; }
        set(options[(index + direction + options.Count) % options.Count]);
        return true;
    }

    private void EnsureBotRoster()
    {
        if (!Current.BotRoster.IsDefaultOrEmpty) return;
        Current = Current with { BotRoster = Enumerable.Repeat(
            new OfflineBotSettings(Hunter.Random, 0, Current.BotLevel), 7).ToImmutableArray() };
    }

    private bool ChangeBot(int index, int direction)
    {
        if (!HasBots) return false;
        if (index == 17)
        {
            _selectedBot = (SelectedBot + direction + Current.Bots) % Current.Bots;
            return true;
        }
        OfflineBotSettings bot = Bot;
        switch (index)
        {
            case 18:
                var pool = MphRead.Mods.Multiplayer.HunterRules.Pool(_lowTier).Append(Hunter.Random).ToArray();
                bot = bot with { Hunter = pool[(Array.IndexOf(pool, bot.Hunter) + direction + pool.Length) % pool.Length] }; break;
            case 19: bot = bot with { Suit = (bot.Suit + direction + 4) % 4 }; break;
            case 20: bot = bot with { Level = (bot.Level + direction + 4) % 4 }; break;
            case 21:
                if (!Teams) return false;
                bot = bot with { Team = (bot.Team + 1 + direction + 3) % 3 - 1 }; break;
            case 22:
                bot = bot with { Handicap = (byte)((bot.Handicap + direction * PlayerHandicap.Step + 60) % 60) }; break;
            default: return false;
        }
        Current = Current with { BotRoster = Current.BotRoster.SetItem(SelectedBot, bot) };
        return true;
    }

    public bool Change(int index, bool previous = false)
    {
        EnsureBotRoster();
        int direction = previous ? -1 : 1;
        if (index is >= 17 and <= 22) return ChangeBot(index, direction);
        switch (index)
        {
            case 0: return Step(_rooms, Current.RoomKey,
                value => Current = Current with { RoomKey = value }, direction);
            case 1:
            {
                MatchTypeDefinition current = _types[MatchTypeCatalog.BaseModeIndex(Current.Mode)];
                MatchFormat format = MatchTypeCatalog.FormatForMode(Current.Mode);
                return Step(_types, current, next =>
                {
                    format = MatchTypeCatalog.NormalizeFormat(next, format);
                    Current = Current with { Mode = next.Resolve(format) };
                }, direction);
            }
            case 2:
            {
                MatchTypeDefinition type = _types[MatchTypeCatalog.BaseModeIndex(Current.Mode)];
                if (type.FfaOnly || type.TeamOnly) return false;
                MatchFormat next = MatchTypeCatalog.FormatForMode(Current.Mode) == MatchFormat.FreeForAll
                    ? MatchFormat.Auto : MatchFormat.FreeForAll;
                Current = Current with { Mode = type.Resolve(next) };
                return true;
            }
            case 3:
                Current = Current with { Bots = (Current.Bots + direction + 8) % 8 };
                _selectedBot = SelectedBot; return true;
            case 4:
                int level = (Current.BotLevel + direction + 4) % 4;
                Current = Current with { BotLevel = level,
                    BotRoster = Current.BotRoster.Select(bot => bot with { Level = level }).ToImmutableArray() };
                return true;
            case 5: return Step(MphRead.Mods.Multiplayer.HunterRules.Pool(_lowTier), Current.Hunter,
                hunter => Current = Current with { Hunter = hunter }, direction);
            case 6: Current = Current with { Suit = (Current.Suit + direction + 4) % 4 }; return true;
            case 16:
                var characters = Enum.GetValues<OfflineCharacterMode>();
                OfflineCharacterMode nextCharacter = characters[(Array.IndexOf(characters,Current.Character)+direction+characters.Length)%characters.Length];
                Current = Current with { Character = nextCharacter };
                if (nextCharacter != OfflineCharacterMode.Native)
                    Current = Current with { Hunter = Hunter.Weavel,
                        Bots = nextCharacter is OfflineCharacterMode.TrooperScoutDroneCombatPractice or OfflineCharacterMode.TrooperArsenalPractice ? Math.Max(1, Current.Bots) : 0,
                        Mode = GameMode.Battle };
                return true;
            case 23:
                if (!Teams) return false;
                Current = Current with { LocalTeam = 1 - Current.LocalTeam }; return true;
            default: return false;
        }
    }

    public void SetLowTier(bool enabled)
    {
        _lowTier = enabled;
        Current = Current with { Hunter = MphRead.Mods.Multiplayer.HunterRules.Sanitize(Current.Hunter, enabled) };
        EnsureBotRoster();
        for (int i = 0; i < Current.BotRoster.Length; i++)
        {
            var bot = Current.BotRoster[i];
            if (bot.Hunter != Hunter.Random)
                Current = Current with { BotRoster = Current.BotRoster.SetItem(i, bot with {
                    Hunter = MphRead.Mods.Multiplayer.HunterRules.Sanitize(bot.Hunter, enabled) }) };
        }
    }

    public bool Valid => Current.ValidateForm(_rooms)
        && OfflineBotSettings.Validate(Current.BotRoster, Current.Bots, _lowTier);
    public string Map => LauncherRoomDisplayName.Get(Current.RoomKey);
    public string Mode => MatchTypeCatalog.BaseLabel(Current.Mode);
    public string Matchup => MatchTypeCatalog.IsInternalTeamMode(Current.Mode)
        ? "TEAMS" : "FREE FOR ALL";
    public string Bots => Current.Bots + " BOTS";
    public string Difficulty => new[] { "EASY", "NORMAL", "HARD", "INSANE" }[Current.BotLevel];
    public string HunterName => Current.Hunter.ToString().ToUpperInvariant();
    public string CharacterName => Current.Character switch
    {
        OfflineCharacterMode.Native => "NATIVE",
        OfflineCharacterMode.TrooperWallClingPractice => "TROOPER WALL-CLING",
        OfflineCharacterMode.TrooperScoutDronePractice => "TROOPER SCOUT DRONE",
        OfflineCharacterMode.TrooperScoutDroneCombatPractice => "DRONE COMBAT PRACTICE",
        OfflineCharacterMode.TrooperArsenalPractice => "TROOPER ARSENAL",
        _ => "UNKNOWN"
    };
    public string CharacterNotice => Current.Character == OfflineCharacterMode.Native ? ""
        : !Valid ? Current.Character == OfflineCharacterMode.TrooperArsenalPractice
            ? "Trooper arsenal practice requires Weavel, Battle and zero to seven native opponents."
            : Current.Character == OfflineCharacterMode.TrooperScoutDroneCombatPractice
            ? "Drone combat practice requires Weavel, Battle and zero to seven native opponents."
            : "Trooper practice requires Weavel, Battle and zero bots."
        : Current.Character == OfflineCharacterMode.TrooperArsenalPractice
            ? "Hunters BODY selects your Trooper body and Blaster cannon; DRONE selects its flying appearance. Native keeps native models. Unsupported states use native fallback. Native bolts. Private Blaster: 60 shared UA each life; Fire for bursts, cycle to reselect. Wall-cling, drone and laser. No replays."
        : Current.Character is OfflineCharacterMode.TrooperScoutDronePractice or OfflineCharacterMode.TrooperScoutDroneCombatPractice
            ? "Native fallback models. Morph to split; Jump to hover, Alt Attack for a burst, Zoom for a laser. No blaster or replays."
            : "Weavel model and weapons. Jump into a tall wall to cling; Jump again to kick off. No drone, blaster or replays.";
    public string SuitName => "SUIT " + (Current.Suit + 1);
}
