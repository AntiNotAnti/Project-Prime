using System;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

/// <summary>Real-content characterization of tick fences; no timing or balance overrides.</summary>
internal static class SimulationOrderingCheck
{
    public static int Run(string[] args)
    {
        if (args.Length != 2) { Console.Error.WriteLine("Usage: nettest --simulation-order DATA"); return 2; }
        try
        {
            ServerContent.Open(args[1], "AMHE1");
            MutualKill(GameMode.Battle);
            MutualKill(GameMode.Survival);
            HealthAndDamage();
            GoalAtExpiry();
            CaptureAndDeath();
            PrimeAtCompletion();
            ContestedExpiry();
            CountdownDisconnect();
            Console.WriteLine("SIMULATION_ORDER PASS mutual-kill survival pickup death score-expiry simultaneous-score flag prime contested countdown terminal-once");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("SIMULATION_ORDER FAIL " + error); return 1; }
    }

    private sealed class Fixture : IDisposable
    {
        public Scene Scene { get; } = Scene.CreateHeadless();
        public PlayerEntity A => Scene.Players[0];
        public PlayerEntity B => Scene.Players[1];
        public MatchRuntime Match => Scene.Match;
        public Fixture(GameMode mode)
        {
            try
            {
                Scene.LoadServerRoom(MatchBaselineCheck.SelectRoom(mode), mode, players: 2, roomPlayerCount: NetLaunch.RoomPlayerCount);
                A.ServerActivate(100, Hunter.Samus, 0);
                B.ServerActivate(200, Hunter.Kanden, 1);
                Scene.Players.ActiveCount = 2;
                Scene.StepHeadlessFrame(advanceMatch: false);
                Match.ApplyRules(Match.Rules.With(scoreGoal: 100, startingLives: 0, objectiveTimeGoal: TimeSpan.FromHours(1)));
                A.Health = B.Health = 100;
                A.IgnoreItemPickups = B.IgnoreItemPickups = true;
                Vector3 previous = B.Position;
                B.Position = B.PrevPosition = A.Position + Vector3.UnitX * 30;
                B.ModRefreshNodeRef(previous);
            }
            catch { Scene.CloseHeadless(); throw; }
        }
        public void DuringWorld(Action action)
        {
            Scene.AddEntity(new TickAction(Scene, action));
            Scene.StepHeadlessFrame(advanceMatch: false);
        }
        public void Dispose() => Scene.CloseHeadless();
    }

    // A fixture-only tail callback makes the authoritative mutation occur inside
    // UpdateScene before its standings/message fences, without creating a fake projectile.
    private sealed class TickAction(Scene scene, Action action) : EntityBase(EntityType.ListHead, scene)
    {
        private bool _ran;
        public override bool Process()
        {
            if (!_ran) { _ran = true; action(); }
            return true;
        }
    }

    private static void Kill(PlayerEntity victim, PlayerEntity attacker)
        => victim.TakeDamage(1000, DamageFlags.Death | DamageFlags.IgnoreInvuln, null, attacker);
    private static void Require(bool valid, string detail)
    { if (!valid) { throw new InvalidOperationException(detail); } }

    private static void MutualKill(GameMode mode)
    {
        using var f = new Fixture(mode);
        f.DuringWorld(() => { Kill(f.B, f.A); Kill(f.A, f.B); });
        Require(f.A.Health == 0 && f.B.Health == 0, "Both accepted lethal actions must resolve.");
        Require(f.Match.Kills[0] == 1 && f.Match.Kills[1] == 1 && f.Match.Deaths[0] == 1 && f.Match.Deaths[1] == 1,
            "A dead attacker still receives attribution for its already accepted lethal action.");
        Require(f.Match.Logic.ComparePlayers(0, 1) == 0, "Equal simultaneous outcomes retain a tied standing.");
        f.Match.MatchTime = 0;
        f.Match.Flow.ProcessFrame();
        MatchResult result = f.Match.Result ?? throw new InvalidOperationException("Missing terminal result.");
        Require(result.Players[0].Deaths == 1 && result.Players[1].Deaths == 1, "Terminal result lost a same-tick death.");
        f.Match.CaptureResult(f.Scene.GlobalElapsedTime + 1);
        Require(ReferenceEquals(result, f.Match.Result), "Terminal result was captured more than once.");
        Console.WriteLine($"ORDER {mode} mutual-kill/tied-standing/terminal-once PASS");
    }

    private static void HealthAndDamage()
    {
        using (var f = new Fixture(GameMode.Battle))
        {
            f.A.Health = 25;
            f.A.IgnoreItemPickups = false;
            AddHealth(f);
            f.DuringWorld(() => f.A.TakeDamage(50, DamageFlags.IgnoreInvuln, null, f.B));
            Require(f.A.Health > 0 && f.Match.Deaths[0] == 0, "Player pickup processing must precede the late projectile stage.");
        }
        using (var f = new Fixture(GameMode.Battle))
        {
            f.A.Health = 25;
            f.A.IgnoreItemPickups = false;
            AddHealth(f);
            Kill(f.A, f.B);
            f.Scene.StepHeadlessFrame(advanceMatch: false);
            Require(f.A.Health == 0 && f.Match.Deaths[0] == 1, "An already dead player must not collect health or resurrect.");
        }
        Console.WriteLine("ORDER health-before-late-damage/dead-player-pickup-rejection PASS");
    }

    private static void AddHealth(Fixture f)
    {
        var item = new ItemInstanceEntity(new ItemInstanceEntityData(f.A.Position.AddY(0.5f), ItemType.HealthSmall, 300), f.A.NodeRef, f.Scene);
        f.Scene.AddEntity(item);
        item.Initialize();
    }

    private static void GoalAtExpiry()
    {
        using (var f = new Fixture(GameMode.Battle))
        {
            f.Match.ApplyRules(f.Match.Rules.With(scoreGoal: 1));
            f.DuringWorld(() => { Kill(f.B, f.A); Kill(f.A, f.B); });
            f.Match.MatchTime = 0;
            f.Match.Flow.ProcessFrame();
            MatchResult result = f.Match.Result ?? throw new InvalidOperationException("Missing completed score result.");
            Require(result.EndReason == MatchEndReason.ScoreGoal, "An already completed score goal takes precedence over a zero clock.");
            Require(result.Players[0].Points == 1 && result.Players[1].Points == 1,
                "All same-step scores must be present at the completion fence.");
        }
        using (var f = new Fixture(GameMode.Battle))
        {
            int actions = 0;
            f.Scene.AddEntity(new TickAction(f.Scene, () => actions++));
            f.Match.MatchTime = 0;
            f.Scene.StepHeadlessFrame();
            Require(actions == 0 && f.Match.Result?.EndReason == MatchEndReason.TimeLimit,
                "Classic expiry must not process an additional world step.");
        }
        Console.WriteLine("ORDER simultaneous-score/score-goal-at-expiry/no-extra-world-step PASS");
    }

    private static OctolithFlagEntity Acquire(Fixture f)
    {
        foreach (OctolithFlagEntity flag in f.Scene.GetOctolithFlagEntities())
        {
            if (f.Match.Rules.Mode == MatchMode.Capture && flag.Data.TeamId == f.A.TeamIndex) { continue; }
            f.A.Position = flag.Position.AddY(-1.25f);
            f.A.PrevPosition = f.A.Position - Vector3.UnitX * 2;
            f.B.Position = f.B.PrevPosition = flag.Position + Vector3.UnitX * 30;
            flag.Process();
            if (flag.Carrier == f.A) { return flag; }
        }
        throw new InvalidOperationException("Could not acquire an actual objective.");
    }

    private static void CaptureAndDeath()
    {
        using (var f = new Fixture(GameMode.Capture))
        {
            OctolithFlagEntity flag = Acquire(f);
            f.DuringWorld(() => { flag.OnCaptured(); Kill(f.A, f.B); });
            Require(f.Match.OctolithScores[0] == 1 && f.Match.Deaths[0] == 1 && flag.Carrier == null,
                "A completed capture must survive a later death in the same world step.");
            Require(f.Match.OctolithDrops[0] == 0, "Captured objective must not be dropped again on death.");
        }
        using (var f = new Fixture(GameMode.Capture))
        {
            OctolithFlagEntity flag = Acquire(f);
            Kill(f.A, f.B);
            f.Scene.StepHeadlessFrame(advanceMatch: false);
            Require(flag.Carrier == null && f.A.OctolithFlag == null && f.Match.OctolithDrops[0] == 1
                && f.Match.OctolithScores[0] == 0,
                "A carrier already dead at the objective stage must drop once without scoring.");
        }
        Console.WriteLine("ORDER capture-before-death/death-before-objective/no-double-drop PASS");
    }

    private static void PrimeAtCompletion()
    {
        using var f = new Fixture(GameMode.PrimeHunter);
        f.Match.PrimeHunter = 1;
        f.DuringWorld(() => Kill(f.B, f.A));
        Require(f.Match.PrimeHunter == 0, "Lethal attribution must transfer Prime before completion.");
        f.Match.MatchTime = 0;
        f.Match.Flow.ProcessFrame();
        Require(f.Match.Result?.PrimeHunter == 0 && f.Match.Result.Players[0].Kills == 1,
            "Final result must include the completed Prime transfer.");
        Console.WriteLine("ORDER prime-transfer-at-completion PASS");
    }

    private static void ContestedExpiry()
    {
        using var f = new Fixture(GameMode.DefenderTeams);
        NodeDefenseEntity? found = null;
        foreach (NodeDefenseEntity candidate in f.Scene.GetNodeDefenseEntities()) { found = candidate; break; }
        NodeDefenseEntity node = found ?? throw new InvalidOperationException("No node objective.");
        CollisionVolume volume = node.Volume;
        Vector3 center = volume.Type switch
        {
            VolumeType.Sphere => volume.SpherePosition,
            VolumeType.Cylinder => volume.CylinderPosition + volume.CylinderVector * volume.CylinderDot / 2,
            _ => volume.BoxPosition + (volume.BoxVector1 * volume.BoxDot1 + volume.BoxVector2 * volume.BoxDot2 + volume.BoxVector3 * volume.BoxDot3) / 2
        };
        foreach (PlayerEntity player in new[] { f.A, f.B })
        {
            Vector3 previous = player.Position;
            player.Position = center - (player.Volume.SpherePosition - previous);
            player.ModRefreshNodeRef(previous);
        }
        node.Process();
        Require(node.Contested, "Fixture must have both teams occupying the zone.");
        float before = f.Match.TeamTime[0] + f.Match.TeamTime[1];
        f.Match.MatchTime = 0;
        f.Match.Flow.ProcessFrame();
        Require(f.Match.Phase == MatchPhase.Ending && f.Match.TeamTime[0] + f.Match.TeamTime[1] == before,
            "Classic expiry ends a contested zone without awarding an extra frame.");
        Console.WriteLine("ORDER classic-contested-zone-expiry PASS");
    }

    private static void CountdownDisconnect()
    {
        var match = new MatchRuntime(MatchRules.CreateDefault(MatchMode.Battle, "room"));
        var lifecycle = new MatchLifecycle(match);
        int resets = 0;
        lifecycle.AdvanceBeforeStep(10, true, () => resets++);
        uint revision = match.PhaseRevision;
        lifecycle.AdvanceBeforeStep(190, false, () => resets++);
        Require(match.Phase == MatchPhase.WaitingForPlayers && match.PhaseRevision != revision,
            "Lost quorum on the exact start deadline cancels countdown before Playing.");
        lifecycle.AdvanceBeforeStep(191, true, () => resets++);
        Require(resets == 2 && match.Phase == MatchPhase.Countdown, "Returning quorum requires another pristine reset.");
        Console.WriteLine("ORDER disconnect-at-countdown-deadline/reset-again PASS");
    }
}
