using System;
using System.Linq;
using MphRead.Mods.Network;
using ProjectPrime.CaptureZone;

namespace MphRead.Mods.HeroModes;

public static class HeroRosterBridge
{
    public const byte SpectatorFlag = 2;
    // IDs are internal names for slots in an already authenticated host roster, never client identity claims.
    public static Guid SlotId(int slot) => new(slot + 1, 0, 0, new byte[8]);
    public static CaptureZoneRoster Create(RosterPacket roster, int capacity, ZoneLobbyPhase phase = ZoneLobbyPhase.Lobby)
    {
        var core = new CaptureZoneRoster(Math.Clamp(capacity, 1, 4));
        for (int i = 0; i < roster.Count; i++)
        {
            if ((roster.Flags[i] & SpectatorFlag) != 0) continue;
            string name = string.IsNullOrWhiteSpace(roster.Names[i]) ? $"Player {roster.Slots[i] + 1}" : roster.Names[i];
            if (name.Length > 32) name = name[..32];
            Guid id = SlotId(roster.Slots[i]);
            Require(core.Join(id, name, roster.Teams[i]));
            Require(core.Select(id, HeroRules.ToZone((Hunter)roster.Hunters[i])));
            Require(core.Ready(id, roster.LobbyReady[i] || roster.IsBot(i)));
        }
        if (phase != ZoneLobbyPhase.Lobby) core.SetPhase(phase);
        return core;
    }
    private static void Require(RosterResult result)
    { if (!result.Accepted) throw new InvalidOperationException(result.Message); }
    public static RosterSnapshot Snapshot(RosterPacket roster, int capacity)
    {
        var slots = Enumerable.Range(0, roster.Count).Select(i => new RosterSlot(SlotId(roster.Slots[i]),
            roster.Names[i], (roster.Flags[i] & SpectatorFlag) != 0 ? -1 : roster.Teams[i],
            (roster.Flags[i] & SpectatorFlag) != 0 || roster.Hunters[i] >= 7 ? null : HeroRules.ToZone((Hunter)roster.Hunters[i]),
            roster.LobbyReady[i])).ToArray();
        return new RosterSnapshot(roster.Revision, Math.Clamp(capacity, 1, 4), ZoneLobbyPhase.Lobby, slots);
    }
    public static LobbyResultCode ValidateStart(MatchDefinition match, RosterPacket roster, bool requireReady, out string reason)
    {
        try
        {
            var result = Create(roster, LobbyRules.TeamCapacity(match, 0)).CanStart(requireReady);
            reason = result.Message;
            return result.Accepted ? LobbyResultCode.Ok : LobbyResultCode.InvalidConfiguration;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        { reason = ex.Message; return LobbyResultCode.InvalidConfiguration; }
    }
}
