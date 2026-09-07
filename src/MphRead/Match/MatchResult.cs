using System;
using System.Collections.Immutable;
using MphRead.Entities;
using MphRead.Formats;

namespace MphRead
{
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

        internal MatchResult(MatchRuntime match, float completedAtSimulationTime, MatchEndReason endReason)
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
                players.Add(new PlayerMatchResult(match, slot, PlayerEntity.Players[slot], GameState.Nicknames[slot]));
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
        public int TeamStanding { get; }
        public int Stars { get; }
        public int Standings { get; }
        public int Points { get; }
        public int Kills { get; }
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

        internal PlayerMatchResult(MatchRuntime match, int slot, PlayerEntity player, string nickname)
        {
            Slot = slot;
            Nickname = nickname;
            Hunter = player.Hunter;
            TeamIndex = player.TeamIndex;
            Active = player.LoadFlags.TestFlag(LoadFlags.Active);
            TeamStanding = match.TeamStandings[slot];
            Stars = match.Stars[slot];
            Standings = match.Standings[slot];
            Points = match.Points[slot];
            Kills = match.Kills[slot];
            Deaths = match.Deaths[slot];
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
