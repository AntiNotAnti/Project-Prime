using System;
using System.Collections.Generic;
using System.Linq;
using MphRead.Mods.Launcher;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher.RmlUi.Core;

/// <summary>Local, uncommitted menu choices for an existing offline match.</summary>
public readonly record struct RmlUiOfflineRequest(string RoomKey, GameMode Mode,
    Hunter Hunter, int Suit, int Bots, int BotLevel)
{
    public OfflineCharacterMode Character { get; init; }
    public bool ValidateForm(IReadOnlyList<string> availableRooms)
    {
        GameMode requestedMode = Mode; // A struct method cannot capture this in a lambda.
        return !String.IsNullOrWhiteSpace(RoomKey)
            && availableRooms.Contains(RoomKey, StringComparer.OrdinalIgnoreCase)
            && OfflineLaunch.Modes.Any(x => x.Mode == requestedMode)
            && (Character == OfflineCharacterMode.Native
                || Character == OfflineCharacterMode.TrooperWallClingPractice
                    && Hunter == Hunter.Weavel && Mode == GameMode.Battle && Bots == 0)
            && (uint)Hunter < 7 && Suit is >= 0 and <= 3
            && Bots is >= 0 and <= 7 && BotLevel is >= 0 and <= 3;
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
    public const int ArenaPageSize = 16;
    public RmlUiOfflineRequest Current { get; private set; }
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
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        _types = MatchTypeCatalog.GameTypes;
        Current = new(_rooms.FirstOrDefault() ?? "", GameMode.Battle,
            (uint)hunter < 7 ? hunter : Hunter.Samus,
            Math.Clamp(suit, 0, 3), Math.Clamp(bots, 0, 7), Math.Clamp(level, 0, 3));
    }

    public void RefreshArenas(IEnumerable<string> rooms)
    {
        string[] next = rooms.Where(x => !String.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        if (_rooms.SequenceEqual(next, StringComparer.OrdinalIgnoreCase)) return;
        _rooms = next;
        if (!_rooms.Contains(Current.RoomKey, StringComparer.OrdinalIgnoreCase))
            Current = Current with { RoomKey = _rooms.FirstOrDefault() ?? "" };
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
        _filtered = _rooms.Where(x => x.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
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

    private bool Step<T>(IReadOnlyList<T> options, T current, Action<T> set)
    {
        if (options.Count == 0) return false;
        int index = -1;
        for (int i = 0; i < options.Count; i++)
            if (EqualityComparer<T>.Default.Equals(options[i], current)) { index = i; break; }
        set(options[(index + 1) % options.Count]);
        return true;
    }

    public bool Change(int index)
    {
        switch (index)
        {
            case 0: return Step(_rooms, Current.RoomKey,
                value => Current = Current with { RoomKey = value });
            case 1:
            {
                MatchTypeDefinition current = _types[MatchTypeCatalog.BaseModeIndex(Current.Mode)];
                MatchFormat format = MatchTypeCatalog.FormatForMode(Current.Mode);
                return Step(_types, current, next =>
                {
                    format = MatchTypeCatalog.NormalizeFormat(next, format);
                    Current = Current with { Mode = next.Resolve(format) };
                });
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
            case 3: Current = Current with { Bots = (Current.Bots + 1) % 8 }; return true;
            case 4: Current = Current with { BotLevel = (Current.BotLevel + 1) % 4 }; return true;
            case 5: return Step(MphRead.Mods.Multiplayer.HunterRules.Pool(_lowTier), Current.Hunter,
                hunter => Current = Current with { Hunter = hunter });
            case 6: Current = Current with { Suit = (Current.Suit + 1) % 4 }; return true;
            case 16:
                Current = Current.Character == OfflineCharacterMode.Native
                    ? Current with { Character = OfflineCharacterMode.TrooperWallClingPractice,
                        Hunter = Hunter.Weavel, Bots = 0, Mode = GameMode.Battle }
                    : Current with { Character = OfflineCharacterMode.Native };
                return true;
            default: return false;
        }
    }

    public void SetLowTier(bool enabled)
    {
        _lowTier = enabled;
        Current = Current with { Hunter = MphRead.Mods.Multiplayer.HunterRules.Sanitize(Current.Hunter, enabled) };
    }

    public bool Valid => Current.ValidateForm(_rooms);
    public string Map => Current.RoomKey;
    public string Mode => MatchTypeCatalog.BaseLabel(Current.Mode);
    public string Matchup => MatchTypeCatalog.IsInternalTeamMode(Current.Mode)
        ? "TEAMS" : "FREE FOR ALL";
    public string Bots => Current.Bots + " BOTS";
    public string Difficulty => new[] { "EASY", "NORMAL", "HARD", "INSANE" }[Current.BotLevel];
    public string HunterName => Current.Hunter.ToString().ToUpperInvariant();
    public string CharacterName => Current.Character == OfflineCharacterMode.Native ? "NATIVE" : "TROOPER WALL-CLING";
    public string CharacterNotice => Current.Character == OfflineCharacterMode.Native ? ""
        : Valid ? "Weavel model and weapons. Jump into a tall wall to cling; Jump again to kick off. No drone, blaster or replays."
        : "Wall-cling practice requires Weavel, Battle and zero bots.";
    public string SuitName => "SUIT " + (Current.Suit + 1);
}
