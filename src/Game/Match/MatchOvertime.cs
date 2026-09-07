using System;
using MphRead.Entities;

namespace MphRead
{
    /// <summary>Opt-in primary-score overtime evaluated at the existing match-flow boundary.</summary>
    public static class MatchOvertime
    {
        public static void Evaluate(Scene scene, bool regulationExpired, MatchEndReason? explicitEnd)
        {
            MatchRuntime match = scene.Match;
            if (scene.Services.IsReplica || match.Phase != MatchPhase.Playing
                || match.Rules.OvertimePolicy == OvertimePolicy.Disabled) { return; }
            if (explicitEnd.HasValue) { match.PendingEndReason = explicitEnd; return; }
            if (match.ForceEndGame || match.PendingEndReason is MatchEndReason.Survival
                or MatchEndReason.Forced or MatchEndReason.CompletionMessage or MatchEndReason.InvalidTeams) { return; }
            bool active = match.Period != MatchPeriod.Regulation;
            if (!active && !regulationExpired && match.PendingEndReason != MatchEndReason.ObjectiveTimeGoal) { return; }

            bool tied = PrimaryTie(scene, out int contenders);
            bool liveObjective = HasLiveObjective(scene);
            // Survival preserves its existing shared lives and elimination logic. Once
            // sudden death starts, a temporary lives lead does not end the contest.
            bool continuePlay = liveObjective || (active && match.Rules.IsSurvival ? contenders > 1 : tied);
            if (continuePlay)
            {
                if (!active)
                {
                    match.Period = match.Rules.Mode is MatchMode.Battle or MatchMode.TeamBattle
                        or MatchMode.Survival or MatchMode.TeamSurvival ? MatchPeriod.SuddenDeath : MatchPeriod.Overtime;
                    match.PeriodStartTick = scene.Services.Combat?.Tick ?? unchecked((uint)scene.FrameCount);
                    scene.Services.PublishWorldSignal(scene, new(WorldSignalKind.OvertimeStarted, WorldSubjectKind.Match,
                        null, null, 255, OpenTK.Mathematics.Vector3.Zero, (uint)match.Period));
                }
                match.HasPhaseDeadline = false;
                match.PhaseEndTick = 0;
                match.MatchTime = -1; // existing unlimited-clock sentinel; Phase remains Playing
                match.PendingEndReason = null;
            }
            else if (active)
            {
                match.MatchTime = 0;
                match.PendingEndReason ??= MatchEndReason.TimeLimit;
            }
        }

        private static bool PrimaryTie(Scene scene, out int contenders)
        {
            MatchRuntime match = scene.Match;
            Span<bool> seen = stackalloc bool[PlayerEntity.SlotCapacity];
            seen.Clear();
            double best = Double.NegativeInfinity;
            int bestCount = 0;
            contenders = 0;
            foreach (PlayerEntity player in scene.GetPlayerEntities())
            {
                if (!player.LoadFlags.TestFlag(LoadFlags.Active)) { continue; }
                int team = player.TeamIndex;
                if ((uint)team >= PlayerEntity.SlotCapacity || seen[team]) { continue; }
                if (match.Rules.IsSurvival && player.Health <= 0 && match.TeamDeaths[team] > match.Rules.StartingLives) { continue; }
                seen[team] = true;
                contenders++;
                double value = match.Rules.Mode switch
                {
                    MatchMode.Survival or MatchMode.TeamSurvival => Math.Max(0, match.Rules.StartingLives + 1L - match.TeamDeaths[team]),
                    MatchMode.Defender or MatchMode.TeamDefender => match.TeamTime[team],
                    MatchMode.PrimeHunter => match.Time[player.SlotIndex],
                    _ => match.TeamPoints[team]
                };
                if (value > best) { best = value; bestCount = 1; }
                else if (value == best) { bestCount++; }
            }
            return bestCount > 1;
        }

        private static bool HasLiveObjective(Scene scene)
        {
            if (scene.Match.Rules.Mode == MatchMode.Capture)
            {
                foreach (OctolithFlagEntity flag in scene.GetOctolithFlagEntities())
                    if (!flag.AtBase || flag.Carrier != null) { return true; }
            }
            else if (scene.Match.Rules.Mode is MatchMode.Nodes or MatchMode.TeamNodes
                or MatchMode.Defender or MatchMode.TeamDefender)
            {
                foreach (NodeDefenseEntity node in scene.GetNodeDefenseEntities())
                    if (node.Contested) { return true; }
            }
            return false;
        }
    }
}
