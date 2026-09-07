using System;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

/// <summary>Real-content checks for regulation expiry and mode-specific overtime.</summary>
internal static class OvertimeCheck
{
    public static int Run(string[] args)
    {
        if (args.Length is < 2 or > 3)
        {
            Console.Error.WriteLine("Usage: nettest --overtime-check DATA [VERSION]");
            return 2;
        }

        try
        {
            ServerContent.Open(args[1], args.Length == 3 ? args[2] : "AMHE1");
            for (GameMode mode = GameMode.Battle; mode <= GameMode.PrimeHunter; mode++)
            {
                CheckRegulationExpiry(mode, tied: true);
                CheckRegulationExpiry(mode, tied: false);
            }
            CheckCaptureObjective();
            CheckContestedObjective(GameMode.Nodes);
            CheckContestedObjective(GameMode.Defender);
            CheckPrimeObjectiveTie();
            CheckSurvivalLives();
            CheckForcedEnd();
            Console.WriteLine("OVERTIME PASS modes=12 tie/non-tie phase-clock capture-lifecycle contested-objectives prime-goal survival-lives forced-terminal-once");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("OVERTIME FAIL " + error);
            return 1;
        }
    }

    private static void CheckRegulationExpiry(GameMode mode, bool tied)
    {
        using var fixture = new Fixture(mode);
        fixture.PrepareExpiry();
        if (!tied)
        {
            SetPrimaryLead(fixture, mode);
            fixture.Match.Flow.ProcessFrame();
            Require(fixture.Match.Phase == MatchPhase.Ending && fixture.Match.Result != null,
                $"{mode} non-tied expiry did not complete the match.");
            Require(fixture.Match.Result!.EndReason == MatchEndReason.TimeLimit,
                $"{mode} non-tied expiry ended as {fixture.Match.Result.EndReason}, not TimeLimit.");
            MatchResult result = fixture.Match.Result;
            fixture.Match.Flow.ProcessFrame();
            fixture.Match.CaptureResult(fixture.Scene.GlobalElapsedTime + 1);
            Require(ReferenceEquals(result, fixture.Match.Result),
                $"{mode} terminal result was replaced after completion.");
            Console.WriteLine($"OVERTIME {mode} non-tie terminal/result-once PASS");
            return;
        }

        float before = 1;
        fixture.Match.MatchTime = before;
        fixture.Match.Flow.ProcessFrame();
        fixture.Match.Flow.UpdateTime();
        Require(fixture.Match.Phase == MatchPhase.Playing && fixture.Match.MatchTime > 0
            && fixture.Match.MatchTime < before,
            $"{mode} regulation clock did not decrease while Playing.");

        fixture.PrepareExpiry();
        fixture.Match.Flow.ProcessFrame();
        MatchPeriod expected = mode is GameMode.Battle or GameMode.BattleTeams
            or GameMode.Survival or GameMode.SurvivalTeams
            ? MatchPeriod.SuddenDeath : MatchPeriod.Overtime;
        Require(fixture.Match.Phase == MatchPhase.Playing && fixture.Match.Period == expected
            && fixture.Match.MatchTime == -1 && fixture.Match.Result == null,
            $"{mode} tied expiry did not enter {expected}.");
        fixture.Match.Flow.UpdateTime();
        Require(fixture.Match.MatchTime == -1 && fixture.Match.Phase == MatchPhase.Playing,
            $"{mode} overtime clock was not monotonic/unlimited.");
        Console.WriteLine($"OVERTIME {mode} tie={expected} clock/phase PASS");
    }

    private static void SetPrimaryLead(Fixture fixture, GameMode mode)
    {
        MatchRuntime match = fixture.Match;
        switch (mode)
        {
            case GameMode.Survival:
            case GameMode.SurvivalTeams:
                match.TeamDeaths[1] = 1;
                break;
            case GameMode.Defender:
            case GameMode.DefenderTeams:
                match.TeamTime[0] = 2;
                match.TeamTime[1] = 1;
                break;
            case GameMode.PrimeHunter:
                match.Time[0] = 2;
                match.Time[1] = 1;
                break;
            default:
                match.TeamPoints[0] = 1;
                break;
        }
    }

    private static void CheckCaptureObjective()
    {
        using var fixture = new Fixture(GameMode.Capture);
        OctolithFlagEntity flag = AcquireEnemyFlag(fixture);
        Require(flag.Carrier == fixture.A && !flag.AtBase, "Capture fixture did not acquire an actual flag.");

        fixture.PrepareExpiry();
        SetPrimaryLead(fixture, GameMode.Capture);
        fixture.A.Health = 0;
        // The server release path invokes the same authoritative drop logic
        // without asking the dead presentation pose for a carry transform.
        flag.ReleaseServerPlayer(fixture.A);
        // AMHE1's headless player pose has no renderer-facing carry direction;
        // keep the released objective at its authored base for the return touch.
        flag.Position = flag.BasePosition.AddY(1.25f);
        Require(flag.Carrier == null && !flag.AtBase && fixture.Match.OctolithDrops[0] == 1,
            "A carried flag did not drop from a dead carrier.");
        fixture.Match.Flow.ProcessFrame();
        Require(fixture.Match.Period == MatchPeriod.Overtime && fixture.Match.MatchTime == -1,
            "A dropped flag did not keep a non-tied capture in overtime.");

        fixture.B.TeamIndex = flag.Data.TeamId;
        fixture.B.Health = 100;
        for (int attempt = 0; attempt < 3 && !flag.AtBase; attempt++)
        {
            Vector3 previous = fixture.B.Position;
            fixture.B.Position = flag.Position.AddY(-1.25f);
            fixture.B.PrevPosition = fixture.B.Position - Vector3.UnitX * 2;
            fixture.B.ModRefreshNodeRef(previous);
            flag.Process();
        }
        Require(flag.Carrier == null && flag.AtBase,
            "A same-team player did not return the dropped capture flag.");

        fixture.PrepareExpiry();
        SetPrimaryLead(fixture, GameMode.Capture);
        fixture.A.Health = fixture.B.Health = 100;
        fixture.Match.Flow.ProcessFrame();
        Require(fixture.Match.Phase == MatchPhase.Ending && fixture.Match.Result?.EndReason == MatchEndReason.TimeLimit,
            "A returned flag did not permit non-tied capture completion.");
        Console.WriteLine("OVERTIME Capture carried/drop/return PASS");
    }

    private static void CheckContestedObjective(GameMode mode)
    {
        using var fixture = new Fixture(mode);
        NodeDefenseEntity node = FirstNode(fixture);
        PutInside(fixture.A, node.Volume);
        PutInside(fixture.B, node.Volume);
        node.Process();
        Require(node.Contested, $"{mode} real node fixture did not become contested.");

        fixture.PrepareExpiry();
        SetPrimaryLead(fixture, mode);
        fixture.Match.Flow.ProcessFrame();
        Require(fixture.Match.Phase == MatchPhase.Playing && fixture.Match.Period == MatchPeriod.Overtime
            && fixture.Match.MatchTime == -1,
            $"{mode} contested objective did not prolong a non-tied expiry.");
        Console.WriteLine($"OVERTIME {mode} contested prolongation PASS");
    }

    private static void CheckPrimeObjectiveTie()
    {
        using var fixture = new Fixture(GameMode.PrimeHunter);
        fixture.Match.PrimeHunter = 0;
        fixture.Match.Time[0] = fixture.Match.Rules.LegacyTimeGoal - fixture.Scene.FrameTime;
        fixture.Match.Time[1] = fixture.Match.Rules.LegacyTimeGoal;
        fixture.Match.MatchTime = 0;
        fixture.Match.Flow.ProcessFrame();
        Require(fixture.Match.PendingEndReason == null && fixture.Match.Period == MatchPeriod.Overtime
            && fixture.Match.MatchTime == -1 && fixture.Match.Phase == MatchPhase.Playing,
            "A tied Prime objective goal did not enter overtime.");
        Console.WriteLine("OVERTIME PrimeHunter objective-goal tie PASS");
    }

    private static void CheckSurvivalLives()
    {
        using var fixture = new Fixture(GameMode.Survival);
        fixture.Match.Flow.ProcessFrame();
        fixture.Match.MatchTime = 0;
        fixture.Match.Flow.ProcessFrame();
        Require(fixture.Match.Period == MatchPeriod.SuddenDeath && fixture.Match.MatchTime == -1,
            "Tied Survival expiry did not enter sudden death.");

        fixture.Match.TeamDeaths[1] = 1;
        fixture.Match.Flow.ProcessFrame();
        Require(fixture.Match.Period == MatchPeriod.SuddenDeath && fixture.Match.MatchTime == -1,
            "A temporary Survival lives lead incorrectly ended sudden death.");

        fixture.B.Health = 0;
        fixture.Match.TeamDeaths[1] = fixture.Match.Rules.StartingLives + 1;
        fixture.Match.Flow.ProcessFrame();
        Require(fixture.Match.Phase == MatchPhase.Ending && fixture.Match.Result?.EndReason == MatchEndReason.Survival,
            "Survival did not end when only one eligible contender remained.");
        Console.WriteLine("OVERTIME Survival lives/sudden-death elimination PASS");
    }

    private static void CheckForcedEnd()
    {
        using var fixture = new Fixture(GameMode.Battle);
        fixture.PrepareExpiry();
        fixture.Match.ForceEndGame = true;
        fixture.Match.Flow.ProcessFrame();
        MatchResult result = fixture.Match.Result
            ?? throw new InvalidOperationException("Forced completion did not capture a result.");
        Require(fixture.Match.Phase == MatchPhase.Ending && result.EndReason == MatchEndReason.Forced,
            "Explicit forced completion did not end with Forced.");
        fixture.Match.Flow.ProcessFrame();
        Require(ReferenceEquals(result, fixture.Match.Result), "Forced result was replaced.");
        Console.WriteLine("OVERTIME forced completion/result-once PASS");
    }

    private static OctolithFlagEntity AcquireEnemyFlag(Fixture fixture)
    {
        foreach (OctolithFlagEntity flag in fixture.Scene.GetOctolithFlagEntities())
        {
            if (flag.Data.TeamId == fixture.A.TeamIndex) { continue; }
            Vector3 previous = fixture.A.Position;
            fixture.A.Position = flag.Position.AddY(-1.25f);
            fixture.A.PrevPosition = fixture.A.Position - Vector3.UnitX * 2;
            fixture.A.ModRefreshNodeRef(previous);
            fixture.B.Position = fixture.B.PrevPosition = flag.Position + Vector3.UnitX * 30;
            flag.Process();
            if (flag.Carrier == fixture.A) { return flag; }
        }
        throw new InvalidOperationException("No actual enemy Capture flag could be acquired.");
    }

    private static NodeDefenseEntity FirstNode(Fixture fixture)
    {
        foreach (NodeDefenseEntity node in fixture.Scene.GetNodeDefenseEntities()) { return node; }
        throw new InvalidOperationException($"No actual {fixture.Mode} node objective was loaded.");
    }

    private static void PutInside(PlayerEntity player, CollisionVolume volume)
    {
        Vector3 center = volume.Type switch
        {
            VolumeType.Sphere => volume.SpherePosition,
            VolumeType.Cylinder => volume.CylinderPosition + volume.CylinderVector * volume.CylinderDot / 2,
            _ => volume.BoxPosition + (volume.BoxVector1 * volume.BoxDot1
                + volume.BoxVector2 * volume.BoxDot2 + volume.BoxVector3 * volume.BoxDot3) / 2
        };
        Vector3 previous = player.Position;
        player.Position = center - (player.Volume.SpherePosition - previous);
        player.ModRefreshNodeRef(previous);
    }

    private static void Require(bool value, string detail)
    {
        if (!value) { throw new InvalidOperationException(detail); }
    }

    private sealed class Fixture : IDisposable
    {
        public GameMode Mode { get; }
        public Scene Scene { get; }
        public MatchRuntime Match => Scene.Match;
        public PlayerEntity A => PlayerEntity.Players[0];
        public PlayerEntity B => PlayerEntity.Players[1];

        public Fixture(GameMode mode)
        {
            Mode = mode;
            Scene = Scene.CreateHeadless();
            try
            {
                Scene.LoadServerRoom(MatchBaselineCheck.SelectRoom(mode), mode, players: 2,
                    roomPlayerCount: NetLaunch.RoomPlayerCount);
                A.ServerActivate(100, Hunter.Samus, 0);
                B.ServerActivate(200, Hunter.Kanden, 1);
                PlayerEntity.PlayerCount = 2;
                // Establish the real headless frame interval before the direct
                // flow calls below; no world step is used as a test oracle.
                Scene.StepHeadlessFrame(advanceMatch: false);
                Match.ApplyRules(Match.Rules.With(overtimePolicy: OvertimePolicy.ModeDefault,
                    timeLimit: TimeSpan.FromMinutes(15)));
                Match.ResetCompetitiveState();
                Match.Phase = MatchPhase.Playing;
                A.Health = B.Health = 100;
            }
            catch
            {
                Scene.CloseHeadless();
                throw;
            }
        }

        public void PrepareExpiry()
        {
            Match.ResetCompetitiveState();
            Match.Phase = MatchPhase.Playing;
            Match.Period = MatchPeriod.Regulation;
            Match.MatchTime = 0;
            A.Health = B.Health = 100;
            A.TeamIndex = 0;
            B.TeamIndex = 1;
        }

        public void Dispose() => Scene.CloseHeadless();
    }
}
