using System;
using System.Collections.Immutable;
using MphRead.Entities;
using MphRead.Formats;

namespace MphRead
{
    public readonly record struct PlayerResultIdentity(Hunter Hunter, int TeamIndex, bool Active, string Nickname, bool IsBot = false);

    public enum MatchEndReason
    {
        TimeLimit,
        ScoreGoal,
        ObjectiveTimeGoal,
        Survival,
        Forced,
        CompletionMessage,
        InvalidTeams
    }

    /// <summary>An immutable snapshot of the authority's completed match. No live
    /// arrays, entities, or mutable statistics escape through this record.</summary>
    public sealed record MatchResult
    {
        public uint MatchId { get; }
        public MatchRules Rules { get; }
        public MatchEndReason EndReason { get; }
        public float CompletedAtSimulationTime { get; }
        public float RemainingMatchTime { get; }
        public int ActivePlayers { get; }
        public int PrimeHunter { get; }
        public ImmutableArray<PlayerMatchResult> Players { get; }
        public ImmutableArray<TeamMatchResult> Teams { get; }
        public ImmutableArray<int> ResultSlots { get; }

        internal MatchResult(MatchRuntime match, float completedAtSimulationTime, MatchEndReason endReason,
            ReadOnlySpan<PlayerResultIdentity> identities = default)
        {
            MatchId = match.MatchId;
            Rules = match.Rules;
            EndReason = endReason;
            CompletedAtSimulationTime = completedAtSimulationTime;
            RemainingMatchTime = match.MatchTime;
            ActivePlayers = match.ActivePlayers;
            PrimeHunter = match.PrimeHunter;
            var players = ImmutableArray.CreateBuilder<PlayerMatchResult>(PlayerEntity.SlotCapacity);
            var teams = ImmutableArray.CreateBuilder<TeamMatchResult>(PlayerEntity.SlotCapacity);
            for (int slot = 0; slot < PlayerEntity.SlotCapacity; slot++)
            {
                players.Add(new PlayerMatchResult(match, slot, match.Scene?.Players[slot], match.Scene?.Roster.Nicknames[slot] ?? $"Player{slot + 1}",
                    identities.IsEmpty ? null : identities[slot]));
                teams.Add(new TeamMatchResult(slot, match.TeamPoints[slot], match.TeamKills[slot],
                    match.TeamDeaths[slot], match.TeamTime[slot]));
            }
            Players = players.MoveToImmutable();
            Teams = teams.MoveToImmutable();
            ResultSlots = ImmutableArray.CreateRange(match.ResultSlots);
        }
    }

    public sealed record PlayerMatchResult
    {
        public int Slot { get; }
        public string Nickname { get; }
        public Hunter Hunter { get; }
        public int TeamIndex { get; }
        public bool Active { get; }
        public bool IsBot { get; }
        public int TeamStanding { get; }
        public int Stars { get; }
        public int Standings { get; }
        public int Points { get; }
        public int Kills { get; }
        public int DamageDealt { get; }
        // Authority/report metrics. The compact live result replica does not
        // carry these counters; career presentation reads the Backend ledger.
        public int BipedKills { get; }
        public int AltFormKills { get; }
        public int LongestKillStreak { get; }
        public int Assists { get; }
        public int Deaths { get; }
        public int BeamDamageMax { get; }
        public int BeamDamageDealt { get; }
        public int DamageCount { get; }
        public int AltDamageCount { get; }
        public int KillStreak { get; }
        public int Suicides { get; }
        public int FriendlyKills { get; }
        public int HeadshotKills { get; }
        public int OctolithScores { get; }
        public int OctolithDrops { get; }
        public int OctolithStops { get; }
        public int NodesCaptured { get; }
        public int NodesLost { get; }
        public int KillsAsPrime { get; }
        public int PrimesKilled { get; }
        // Seconds retain Survival's -1 MAX result sentinel.
        public float Time { get; }
        public ImmutableArray<int> BeamKills { get; }

        internal PlayerMatchResult(MatchRuntime match, int slot, PlayerEntity? player, string nickname, PlayerResultIdentity? identity = null)
        {
            Slot = slot;
            Nickname = identity?.Nickname ?? nickname;
            Hunter = identity?.Hunter ?? player?.Hunter ?? Hunter.Samus;
            TeamIndex = identity?.TeamIndex ?? player?.TeamIndex ?? -1;
            Active = identity?.Active ?? player?.LoadFlags.TestFlag(LoadFlags.Active) ?? false;
            IsBot = identity?.IsBot ?? player?.IsBot ?? false;
            TeamStanding = match.TeamStandings[slot];
            Stars = match.Stars[slot];
            Standings = match.Standings[slot];
            Points = match.Points[slot];
            Kills = match.Kills[slot];
            Deaths = match.Deaths[slot];
            Assists = match.Assists[slot];
            LongestKillStreak = match.LongestKillStreak[slot];
            DamageDealt = match.DamageDealt[slot];
            BipedKills = match.BipedKills[slot];
            AltFormKills = match.AltFormKills[slot];
            BeamDamageMax = match.BeamDamageMax[slot];
            BeamDamageDealt = match.BeamDamageDealt[slot];
            DamageCount = match.DamageCount[slot];
            AltDamageCount = match.AltDamageCount[slot];
            KillStreak = match.KillStreak[slot];
            Suicides = match.Suicides[slot];
            FriendlyKills = match.FriendlyKills[slot];
            HeadshotKills = match.HeadshotKills[slot];
            OctolithScores = match.OctolithScores[slot];
            OctolithDrops = match.OctolithDrops[slot];
            OctolithStops = match.OctolithStops[slot];
            NodesCaptured = match.NodesCaptured[slot];
            NodesLost = match.NodesLost[slot];
            KillsAsPrime = match.KillsAsPrime[slot];
            PrimesKilled = match.PrimesKilled[slot];
            Time = match.Time[slot];
            var beams = ImmutableArray.CreateBuilder<int>(9);
            for (int beam = 0; beam < 9; beam++) { beams.Add(match.BeamKills[slot, beam]); }
            BeamKills = beams.MoveToImmutable();
        }
    }

    public sealed record TeamMatchResult(int TeamIndex, int Points, int Kills, int Deaths, float Time);
}
