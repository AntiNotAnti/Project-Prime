using MphRead.Combat;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

public sealed class BroadcastShotPlannerTests
{
    [Fact]
    public void PrecisionCombatUsesFirstPersonForExactActor()
    {
        ObservationPlayer player = Player(0) with { Weapon = BeamType.Imperialist };
        ObservationContext context = Context(120, [player, Player(1)],
            combatEvents: [Combat(119, player.Identity, CombatEventFlags.Headshot)]);
        var planner = new BroadcastShotPlanner();

        BroadcastShotSelection shot = planner.Select(context, BroadcastFocus.Player(0));

        Assert.Equal(BroadcastShotType.FirstPerson, shot.Shot);
        Assert.Equal(BroadcastShotReason.PrecisionCombat, shot.Reason);
        Assert.Equal(player.Identity, shot.FocusActor);
    }

    [Fact]
    public void ReusedSlotCannotSupplyPrecisionFacts()
    {
        CombatActor oldActor = new(0, 10, 1);
        ObservationPlayer current = Player(0) with
        {
            Identity = new CombatActor(0, 99, 2),
            Weapon = BeamType.Imperialist
        };
        ObservationContext context = Context(120, [current],
            combatEvents: [Combat(119, oldActor, CombatEventFlags.Headshot)]);
        var planner = new BroadcastShotPlanner();

        BroadcastShotSelection shot = planner.Select(context, BroadcastFocus.Player(0));

        Assert.Equal(BroadcastShotType.TightChase, shot.Shot);
        Assert.Equal(current.Identity, shot.FocusActor);
    }

    [Fact]
    public void PursuitAndCrowdedEncounterUseDifferentChaseWidths()
    {
        ObservationPlayer moving = Player(0) with { Velocity = new Vector3(.2f, 0, 0) };
        var planner = new BroadcastShotPlanner();

        BroadcastShotSelection pursuit = planner.Select(Context(20,
            [moving, Player(1) with { Position = new Vector3(4, 0, 0) }]),
            BroadcastFocus.Player(0));
        BroadcastShotSelection crowd = planner.Select(Context(21,
            [moving, Player(1), Player(2), Player(3)]),
            BroadcastFocus.Player(0));

        Assert.Equal(BroadcastShotType.TightChase, pursuit.Shot);
        Assert.Equal(BroadcastShotReason.Pursuit, pursuit.Reason);
        Assert.Equal(BroadcastShotType.WideChase, crowd.Shot);
        Assert.Equal(BroadcastShotReason.MultiPlayerEncounter, crowd.Reason);
    }

    [Fact]
    public void ContestedObjectiveUsesWideObjectiveShot()
    {
        var planner = new BroadcastShotPlanner();
        ObservationObjective objective = new(40, ObservationObjectiveKind.Node,
            Vector3.Zero, 0, Contested: true, CarrierSlot: -1, AtBase: false);
        ObservationContext context = Context(40, [Player(0)], [objective]);

        BroadcastShotSelection shot = planner.Select(context,
            BroadcastFocus.Objective(40));

        Assert.Equal(BroadcastShotType.ObjectiveWide, shot.Shot);
        Assert.Equal(BroadcastShotReason.ContestedObjective, shot.Reason);
    }

    [Fact]
    public void SelectionAndDiagnosticsAreDeterministic()
    {
        ObservationContext context = Context(60, [Player(0), Player(1), Player(2)]);
        var first = new BroadcastShotPlanner();
        var second = new BroadcastShotPlanner();

        BroadcastShotSelection firstShot = first.Select(context, BroadcastFocus.Player(0));
        BroadcastShotSelection secondShot = second.Select(context, BroadcastFocus.Player(0));
        first.UpdatePresentationDiagnostics(collisionAdjusted: true, transitionAge: 7);

        Assert.Equal(firstShot, secondShot);
        BroadcastShotPlannerDiagnostics diagnostics = first.CaptureDiagnostics();
        Assert.Equal(firstShot.Focus, diagnostics.Focus);
        Assert.Equal(firstShot.Shot, diagnostics.SelectedShot);
        Assert.True(diagnostics.CollisionAdjusted);
        Assert.Equal(7u, diagnostics.TransitionAge);
    }

    [Fact]
    public void CameraTransitionUsesFixedTickSmoothstepAndWrapSafeAge()
    {
        var transition = new BroadcastCameraTransition(
            new(new Vector3(0, 0, 0), new Vector3(0, 0, -1)),
            new(new Vector3(10, 0, 0), new Vector3(10, 0, -1)),
            StartTick: uint.MaxValue - 3, DurationTicks: 12,
            BroadcastCameraTransitionKind.Blend);
        BroadcastCameraPose current = new(new Vector3(10, 0, 0),
            new Vector3(10, 0, -1));

        Assert.Equal(0u, transition.Age(uint.MaxValue - 3));
        Assert.Equal(8u, transition.Age(4));
        Assert.False(transition.Complete(4));
        Assert.InRange(transition.Interpolate(current, 4).Position.X, 7.39f, 7.42f);
        Assert.True(transition.Complete(8));
    }

    [Fact]
    public void FirstPersonDirectorTargetRejectsReusedSlotOrFormerLife()
    {
        CombatActor expected = new(0, 10, 2);

        Assert.True(SpectatorCameraController.IsExactDirectorActor(expected,
            new CombatActor(0, 10, 2)));
        Assert.False(SpectatorCameraController.IsExactDirectorActor(expected,
            new CombatActor(0, 99, 2)));
        Assert.False(SpectatorCameraController.IsExactDirectorActor(expected,
            new CombatActor(0, 10, 3)));
        Assert.False(SpectatorCameraController.IsExactDirectorActor(
            CombatActor.None, expected));
    }

    private static ObservationContext Context(uint tick,
        ObservationPlayer[] players,
        ObservationObjective[]? objectives = null,
        CombatEvent[]? combatEvents = null)
        => ObservationContext.Create(ObservationSourceKind.LiveSpectator, tick,
            7, 2, MatchMode.Battle, MatchPhase.Playing,
            MatchPeriod.Regulation, players, objectives,
            combatEvents: combatEvents, matchTimeSeconds: 300);

    private static ObservationPlayer Player(int slot) => new ObservationPlayer(slot, $"P{slot}",
        Hunter.Samus, slot, Connected: true, Active: true, Alive: true,
        CarriesObjective: false, IsPrime: false, Health: 100,
        Weapon: BeamType.PowerBeam, AmmoUa: 40, AmmoMissiles: 5,
        Points: 0, Kills: 0, Deaths: 0, Assists: 0,
        Position: new Vector3(slot * 2, 0, 0), Facing: -Vector3.UnitZ)
        with { Identity = new CombatActor((byte)slot, (ulong)(10 + slot), 1) };

    private static CombatEvent Combat(uint tick, CombatActor actor,
        CombatEventFlags flags) => new(1, tick, 0, CombatEventKind.Damage,
        (byte)BeamType.Imperialist, flags, actor,
        new CombatActor(7, 77, 1), 100, 50, Vector3.Zero, Vector3.UnitZ,
        0, 0, 0);
}
