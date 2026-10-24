using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectPrime.CaptureZone;

public enum ZoneHunter : byte { Samus, Kanden, Spire, Noxus, Trace, Weavel, Sylux }
public enum ZoneLobbyPhase { Lobby, Combat, BetweenRounds }
public sealed record HeroDefinition(ZoneHunter Hunter, string Role, int MaxHealth, double DamageReduction,
    string Primary, string Identity, string Alternate, string WeaponStatus)
{
    public double EffectiveHealth => MaxHealth / (1 - DamageReduction);
}
public static class HeroCatalog
{
    // Initial targets from the user. Gameplay must consume these same definitions
    // when integration is complete; this module alone does not modify an engine.
    public static readonly IReadOnlyList<HeroDefinition> All = Array.AsReadOnly(new[] {
        new HeroDefinition(ZoneHunter.Samus,"MOBILE DPS / HYBRID",180,0,"Aether Pulse (mode-exclusive)","Mobile pressure and repositioning; not tank durability.","Original alternate form retained.","Mode-exclusive Aether Pulse primary; combat balance remains provisional."),
        new HeroDefinition(ZoneHunter.Kanden,"DAMAGE BRUISER",160,.10,"Volt Driver","Electrical pressure with160HP and10% damage reduction.","Original alternate form retained.","Native affinity mechanics with the shared hero-mode loadout restrictions."),
        new HeroDefinition(ZoneHunter.Spire,"FRONTLINE TANK",300,.20,"Magmaul","Durable objective presence with close-range area pressure.","Original alternate form retained.","Native affinity mechanics with the shared hero-mode loadout restrictions."),
        new HeroDefinition(ZoneHunter.Noxus,"CONTROL / SUPPORT",220,.10,"Judicator","Close-to-midrange control; no permanent-disable loop.","Original alternate form retained.","Allied ordinary and charged hits heal and reveal teammate health. Exact healing amounts and cooldown are supplied by the gameplay adapter."),
        new HeroDefinition(ZoneHunter.Trace,"PRECISION DPS",90,0,"Imperialist","Glass cannon. Fragile precision damage, not a durable point holder.","Original alternate form retained.","Damage, headshots and charge details pending engine verification."),
        new HeroDefinition(ZoneHunter.Weavel,"RANGED TANK / SUPPORT",260,.15,"Battlehammer - 3-round burst","Suppression and bounded impact displacement.","Split form retains the shared health budget; native-world validation is still required.","Three straight projectiles; exact implemented timings are supplied by the gameplay adapter."),
        new HeroDefinition(ZoneHunter.Sylux,"MOBILE SUSTAIN HYBRID",180,0,"Shock Coil","Close-range pressure and mobility with bounded sustain.","Original bomb limits, detonation rules and alternate form retained.","Sustain/damage interaction pending engine verification.")
    });
    public static HeroDefinition For(ZoneHunter hunter) => All.First(x => x.Hunter == hunter);
}
public sealed record RosterSlot(Guid PlayerId, string Name, int Team, ZoneHunter? Hunter = null,
    bool Ready = false, bool RespawnSelection = false);
public sealed record RosterSnapshot(long Revision, int TeamCapacity, ZoneLobbyPhase Phase, IReadOnlyList<RosterSlot> Slots);
public sealed record RosterResult(bool Accepted, string Message);

/// <summary>
/// Thread-safe domain logic for the eventual authoritative host. It accepts
/// authenticated session IDs supplied by its caller; it is not a network server.
/// No state is shared with ordinary Project Prime modes.
/// </summary>
public sealed class CaptureZoneRoster
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, RosterSlot> players = new();
    private long revision;
    private int capacity;
    private ZoneLobbyPhase phase;
    public CaptureZoneRoster(int teamCapacity = 4)
    {
        if (teamCapacity is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(teamCapacity));
        capacity = teamCapacity;
    }
    private RosterResult Deny(string message) => new(false, message);
    private RosterResult Accept(string message = "Selection updated.") { revision++; return new(true, message); }
    public RosterResult Join(Guid id, string name, int team)
    {
        lock (gate)
        {
            if (id == Guid.Empty || string.IsNullOrWhiteSpace(name) || name.Length > 32) return Deny("Invalid player identity.");
            if (team is < 0 or > 1) return Deny("Choose Team 1 or Team 2.");
            if (players.ContainsKey(id)) return Deny("Player is already connected.");
            if (players.Values.Count(p => p.Team == team) >= capacity) return Deny("Team is full.");
            players.Add(id, new(id, name.Trim(), team)); return Accept("Joined team.");
        }
    }
    public RosterResult Select(Guid id, ZoneHunter hunter)
    {
        lock (gate)
        {
            if (!Enum.IsDefined(hunter)) return Deny("Unknown hunter.");
            if (!players.TryGetValue(id, out var player)) return Deny("Player is not connected.");
            if (player.Team < 0) return Deny("Spectators cannot reserve hunters.");
            if (phase == ZoneLobbyPhase.Combat && !player.RespawnSelection) return Deny("Change hunters only during respawn selection.");
            var owner = players.Values.FirstOrDefault(p => p.PlayerId != id && p.Team == player.Team && p.Hunter == hunter);
            if (owner != null) return Deny($"Already selected by your teammate: {owner.Name}.");
            if (player.Hunter == hunter) return new(true, "Hunter already selected.");
            // Validate destination first. Failure never clears the prior reservation.
            players[id] = player with { Hunter = hunter, Ready = false }; return Accept();
        }
    }
    public RosterResult ChangeTeam(Guid id, int team)
    {
        lock (gate)
        {
            if (team is < 0 or > 1 || !players.TryGetValue(id, out var player)) return Deny("Invalid team or player.");
            if (phase == ZoneLobbyPhase.Combat) return Deny("Teams cannot change during combat.");
            if (player.Team == team) return new(true, "Already on this team.");
            if (players.Values.Count(p => p.Team == team) >= capacity) return Deny("Team is full.");
            if (player.Hunter.HasValue && players.Values.Any(p => p.Team == team && p.Hunter == player.Hunter)) return Deny("Hunter is already selected on that team.");
            players[id] = player with { Team = team, Ready = false, RespawnSelection = false }; return Accept("Team changed.");
        }
    }
    public RosterResult Spectate(Guid id)
    {
        lock (gate)
        {
            if (!players.TryGetValue(id, out var player)) return Deny("Player is not connected.");
            players[id] = player with { Team = -1, Hunter = null, Ready = false, RespawnSelection = false }; return Accept("Spectating; hunter released.");
        }
    }
    public RosterResult Disconnect(Guid id)
    {
        lock (gate) { return players.Remove(id) ? Accept("Disconnected; hunter released.") : Deny("Player is not connected."); }
    }
    public RosterResult Ready(Guid id, bool ready)
    {
        lock (gate)
        {
            if (!players.TryGetValue(id, out var player)) return Deny("Player is not connected.");
            if (player.Team < 0 || !player.Hunter.HasValue) return Deny("Choose a team and hunter first.");
            if (phase == ZoneLobbyPhase.Combat) return Deny("Lobby ready state is unavailable during combat.");
            players[id] = player with { Ready = ready }; return Accept("Ready state updated.");
        }
    }
    public void SetPhase(ZoneLobbyPhase value)
    {
        if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
        lock (gate) { phase = value; foreach(var p in players.Values.ToArray()) players[p.PlayerId] = p with { Ready = false, RespawnSelection = false }; revision++; }
    }
    // Only the trusted engine's death/respawn lifecycle may call this method.
    public RosterResult SetRespawnSelection(Guid id, bool allowed)
    {
        lock (gate)
        {
            if (!players.TryGetValue(id, out var player) || player.Team < 0) return Deny("Invalid playing slot.");
            players[id] = player with { RespawnSelection = allowed }; return Accept("Respawn selection state updated.");
        }
    }
    public RosterResult SetCapacity(int value)
    {
        lock(gate)
        {
            if(value is < 1 or > 4) return Deny("Team capacity must be 1-4.");
            if(phase == ZoneLobbyPhase.Combat || Enumerable.Range(0,2).Any(t => players.Values.Count(p => p.Team == t) > value)) return Deny("Capacity would invalidate the roster.");
            capacity=value; return Accept("Team capacity updated.");
        }
    }
    public RosterResult CanStart(bool requireReady = true)
    {
        lock(gate)
        {
            if(phase == ZoneLobbyPhase.Combat) return Deny("Round is already active.");
            var active=players.Values.Where(p => p.Team >= 0).ToArray();
            int a=active.Count(p=>p.Team==0), b=active.Count(p=>p.Team==1);
            if(a == 0 || a != b || a > capacity) return Deny("Use equal teams of 1-4 players.");
            if(active.Any(p=>!p.Hunter.HasValue)) return Deny("Every player must select a hunter.");
            if(active.GroupBy(p=>(p.Team,p.Hunter)).Any(g=>g.Count()>1)) return Deny("Duplicate hunter on a team.");
            if(requireReady && active.Any(p=>!p.Ready)) return Deny("Waiting for ready players.");
            return new(true, $"Ready for {a}v{b}.");
        }
    }
    public void Reset() { lock(gate) { players.Clear(); phase=ZoneLobbyPhase.Lobby; revision++; } }
    public RosterSnapshot Snapshot() { lock(gate) return new(revision,capacity,phase,Array.AsReadOnly(players.Values.ToArray())); }
}
