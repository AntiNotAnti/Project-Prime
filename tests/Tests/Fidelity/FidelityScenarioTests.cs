using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests.Fidelity;

[Collection("Match baseline globals")]
public sealed class FidelityScenarioTests
{
    private const string ReferenceDigest = "f64b99e2f976b30e5a667156bd7878f8a43c741bb1a5e02be1c6f40adc513226";
    private const string SourceCommit = "600bb003e3419edbfedec6c553f73d9e07dea3e1";
    private const string Reproduction = "dotnet test tests/Tests/Tests.csproj -c Release --filter FullyQualifiedName~FidelityScenarioTests";

    [Fact, Trait("Category", "FidelitySmoke")]
    public void ComparerReportsTheEarliestTickAcrossRuleOrder()
    {
        FidelityObservation[] expectedObservations =
        [
            Observation(0, 0, 0), Observation(1, 1, 1), Observation(2, 2, 2)
        ];
        FidelityObservation[] actualObservations =
        [
            Observation(0, 0, 0), Observation(1, 1, 9), Observation(2, 9, 2)
        ];
        FidelityRunResult expected = Synthetic(expectedObservations);
        FidelityRunResult actual = Synthetic(actualObservations);

        FidelityDivergence? divergence = FidelityRunComparer.FirstDivergence(expected, actual,
        [
            new("later", FidelityComparisonMode.Exact),
            new("earlier", FidelityComparisonMode.Exact)
        ]);

        Assert.NotNull(divergence);
        Assert.Equal((uint)1, divergence.Tick);
        Assert.Equal("earlier", divergence.Field);
        Assert.Throws<ArgumentException>(() => FidelityRunComparer.FirstDivergence(expected, actual,
            [new("later", FidelityComparisonMode.Exact), new("later", FidelityComparisonMode.Exact)]));
    }

    [Fact, Trait("Category", "FidelitySmoke"), Trait("RequiresGameContent", "true")]
    public void IdleSpawnAndMovementTraceIsRepeatableAndSupportsDeclaredComparisonModes()
    {
        using IDisposable content = OpenContent();
        FidelityScenarioSpec spec = MovementSpec("FID-SIM-001", 12345, 67890);

        FidelityRunResult expected = FidelityScenarioRunner.Run(spec);
        FidelityRunResult actual = FidelityScenarioRunner.Run(spec);

        Assert.Equal(expected.StateDigest, actual.StateDigest);
        Assert.Null(FidelityRunComparer.FirstDivergence(expected, actual,
        [
            new("world.sha256", FidelityComparisonMode.Exact),
            new("player.position.x", FidelityComparisonMode.Tolerant, 0.000001, "world units", "Floating-point observation tolerance."),
            new("scene.frame", FidelityComparisonMode.InvariantNondecreasing)
        ]));
        FidelityObservation before = actual.Observations[400];
        FidelityObservation after = actual.Observations[^1];
        double displacement = new[] { "x", "y", "z" }
            .Sum(axis => Math.Abs(after.Numeric[$"player.position.{axis}"] - before.Numeric[$"player.position.{axis}"]));
        Assert.True(displacement > 0.001,
            $"The real authoritative player path did not move after forward input: before={Position(before)}, after={Position(after)}, phase={after.Exact["match.phase"]}, input={after.Exact["input.buttons"]}, hasInput={after.Exact["input.hasInput"]}, camera={after.Exact["camera.active"]}/{after.Exact["camera.blocksInput"]}, frame={after.Numeric["scene.frame"]}.");
    }

    [Fact, Trait("Category", "FidelitySmoke"), Trait("RequiresGameContent", "true")]
    public void InterleavedOtherMatchAndItsDisposalCannotAlterPrimaryTrace()
    {
        using IDisposable content = OpenContent();
        FidelityScenarioSpec primary = MovementSpec("FID-SIM-002", 12345, 67890);
        FidelityScenarioSpec other = MovementSpec("FID-SIM-OTHER-001", 991, 337);

        FidelityRunResult alone = FidelityScenarioRunner.Run(primary);
        FidelityRunResult interleaved = FidelityScenarioRunner.RunInterleaved(primary, other, disposeOtherBeforeTick: 150);

        Assert.Equal(alone.StateDigest, interleaved.StateDigest);
        Assert.Null(FidelityRunComparer.FirstDivergence(alone, interleaved,
        [
            new("world.sha256", FidelityComparisonMode.Exact),
            new("players.sha256", FidelityComparisonMode.Exact),
            new("match.time", FidelityComparisonMode.Exact)
        ]));
    }

    [Fact, Trait("Category", "FidelitySmoke"), Trait("RequiresGameContent", "true")]
    public void DeliberateMismatchReportsFirstDivergentTickAndContext()
    {
        using IDisposable content = OpenContent();
        FidelityRunResult expected = FidelityScenarioRunner.Run(MovementSpec("FID-SIM-003", 12345, 67890));
        var observations = expected.Observations.ToArray();
        FidelityObservation original = observations[240];
        var numeric = new SortedDictionary<string, double>(
            original.Numeric.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            StringComparer.Ordinal)
        {
            ["player.health"] = original.Numeric["player.health"] - 1
        };
        observations[240] = original with { Numeric = numeric };
        FidelityRunResult actual = expected with { Observations = observations };

        FidelityDivergence? divergence = FidelityRunComparer.FirstDivergence(expected, actual,
            [new("player.health", FidelityComparisonMode.Exact)]);

        Assert.NotNull(divergence);
        Assert.Equal((uint)240, divergence.Tick);
        Assert.Equal("player.health", divergence.Field);
        Assert.Equal(3, divergence.PrecedingValues.Count);
        Assert.Equal(SourceCommit, divergence.SourceCommit);
        Assert.Equal(ReferenceDigest, divergence.ReferenceDigest);
        Assert.Equal(Reproduction, divergence.ReproductionCommand);
    }

    [Fact, Trait("Category", "FidelitySmoke"), Trait("RequiresGameContent", "true")]
    public void DeathAndRespawnUseTheRealAuthoritativePlayerLifecycle()
    {
        using IDisposable content = OpenContent();
        FidelityScenarioSpec spec = Spec("FID-SPAWN-001", MatchMode.Battle, 430,
            tick => tick == 401 ? InputButtons.Shoot : InputButtons.None,
            [
                new(400, (simulation, _) => simulation.Scene.Players[0].TakeDamage(1,
                    DamageFlags.Death | DamageFlags.NoSfx | DamageFlags.NoDmgInvuln, null, null)),
                new(401, (simulation, _) => simulation.Scene.Players[0].RespawnTimer = 1)
            ]);

        FidelityRunResult result = FidelityScenarioRunner.Run(spec);

        Assert.Equal(0, result.Observations[400].Numeric["player.health"]);
        Assert.True(result.Observations[400].Numeric["player.respawnTicks"] > 0);
        Assert.True(result.Observations[^1].Numeric["player.health"] > 0);
        Assert.Equal("True", result.Observations[^1].Exact["player.active"]);
    }

    [Fact, Trait("Category", "FidelitySmoke"), Trait("RequiresGameContent", "true")]
    public void DirectDamageAndProjectileLifecycleUseAuthoritativeGameplayPaths()
    {
        using IDisposable content = OpenContent();
        FidelityRunResult damaged = FidelityScenarioRunner.Run(Spec("FID-DAMAGE-001", MatchMode.Battle, 440,
            _ => InputButtons.None,
            [new(400, (simulation, _) => simulation.Scene.Players[0].TakeDamage(10,
                DamageFlags.NoSfx | DamageFlags.NoDmgInvuln, null, null))]));
        double healthBefore = damaged.Observations[399].Numeric["player.health"];
        Assert.Equal(healthBefore - 10, damaged.Observations[400].Numeric["player.health"]);

        FidelityRunResult projectile = FidelityScenarioRunner.Run(Spec("FID-PROJECTILE-001", MatchMode.Battle, 900,
            _ => InputButtons.None,
            [new(400, (simulation, _) =>
            {
                PlayerEntity player = simulation.Scene.Players[0];
                BeamResultFlags result = BeamProjectileEntity.Spawn(player, player.EquipInfo,
                    player.Position.AddY(1), Vector3.UnitY, BeamSpawnFlags.NoMuzzle,
                    player.NodeRef, simulation.Scene);
                if (!result.TestFlag(BeamResultFlags.Spawned))
                    throw new InvalidDataException($"Authoritative projectile spawn failed: {result}.");
            })]));
        Assert.Contains(projectile.Observations.Skip(400), observation =>
            Int32.Parse(observation.Exact["projectile.activeCount"]) > 0);
        Assert.Equal("0", projectile.Observations[^1].Exact["projectile.activeCount"]);
    }

    [Fact, Trait("Category", "FidelitySmoke"), Trait("RequiresGameContent", "true")]
    public void PickupConsumptionAndRespawnUseTheRealSpawnerLifecycle()
    {
        using IDisposable content = OpenContent();
        FidelityScenarioSpec spec = Spec("FID-PICKUP-001", MatchMode.Battle, 5000,
            _ => InputButtons.None,
            [new(400, (simulation, _) =>
            {
                foreach (ItemSpawnEntity spawner in simulation.Scene.GetItemSpawnEntities())
                {
                    if (spawner.Item == null)
                        continue;
                    spawner.Item.OnPickedUp(simulation.Scene.Players[0]);
                    return;
                }
                throw new InvalidDataException("The AMHE1 scenario has no spawned pickup to consume.");
            })]);

        FidelityRunResult result = FidelityScenarioRunner.Run(spec);

        Assert.Equal("0", result.Observations[400].Exact["pickup.first.lastConsumer"]);
        Assert.Contains(result.Observations.Skip(400), observation => observation.Exact["pickup.first.itemActive"] == "False");
        int initialSpawns = Int32.Parse(result.Observations[399].Exact["pickup.first.spawnCount"]);
        Assert.Contains(result.Observations.Skip(401), observation =>
            Int32.Parse(observation.Exact["pickup.first.spawnCount"]) > initialSpawns);
    }

    [Fact, Trait("Category", "FidelitySmoke"), Trait("RequiresGameContent", "true")]
    public void ObjectiveCaptureAndScoreTransitionUseTheRealNodeStateMachine()
    {
        using IDisposable content = OpenContent();
        var actions = new List<FidelityAuthoritativeAction>();
        for (uint tick = 400; tick < 1030; tick++)
        {
            actions.Add(new(tick, (simulation, _) =>
            {
                foreach (NodeDefenseEntity node in simulation.Scene.GetNodeDefenseEntities())
                {
                    PlayerEntity player = simulation.Scene.Players[0];
                    player.Reposition(node.Position, -Vector3.UnitZ, node.NodeRef);
                    return;
                }
                throw new InvalidDataException("The AMHE1 scenario has no node objective.");
            }));
        }
        FidelityRunResult result = FidelityScenarioRunner.Run(Spec("FID-OBJECTIVE-001", MatchMode.Nodes, 1100,
            _ => InputButtons.None, actions));

        Assert.Contains(result.Observations.Skip(400), observation => observation.Numeric["objective.first.progress"] > 0);
        Assert.Contains(result.Observations, observation => observation.Numeric["player.nodesCaptured"] > 0);
        Assert.Equal("0", result.Observations[^1].Exact["objective.first.currentTeam"]);
    }

    private static FidelityScenarioSpec MovementSpec(string caseId, uint seed1, uint seed2)
        => Spec(caseId, MatchMode.Battle, 700,
            tick => tick >= 400 ? InputButtons.Forward : InputButtons.None,
            [new(399, (simulation, _) =>
            {
                PlayerEntity player = simulation.Scene.Players[0];
                player.Reposition(player.Position, -Vector3.UnitZ, player.NodeRef);
                player._field70 = 0;
                player._field74 = -1;
            })],
            seed1: seed1, seed2: seed2);

    private static string Position(FidelityObservation observation)
        => $"({observation.Numeric["player.position.x"]:R},{observation.Numeric["player.position.y"]:R},{observation.Numeric["player.position.z"]:R})";

    private static FidelityObservation Observation(uint tick, double later, double earlier)
        => new(tick, new SortedDictionary<string, string>(StringComparer.Ordinal),
            new SortedDictionary<string, double>(StringComparer.Ordinal)
            {
                ["later"] = later,
                ["earlier"] = earlier
            });

    private static FidelityRunResult Synthetic(IReadOnlyList<FidelityObservation> observations)
        => new("FID-SIM-900", 1, 2, SourceCommit, ReferenceDigest, Reproduction,
            observations, "synthetic");

    private static FidelityScenarioSpec Spec(string caseId, MatchMode mode, uint maximumTicks,
        Func<uint, InputButtons> input,
        IReadOnlyList<FidelityAuthoritativeAction>? actions = null,
        uint seed1 = 12345, uint seed2 = 67890)
        => new(caseId, new MatchRules(mode, "MP1 SANCTORUS", maxPlayers: 1), maximumTicks,
            seed1, seed2, input, actions ?? Array.Empty<FidelityAuthoritativeAction>(),
            SourceCommit, ReferenceDigest, Reproduction);

    private static IDisposable OpenContent()
    {
        string directory = Environment.GetEnvironmentVariable("GAME_DATA_DIRECTORY")
            ?? Path.Combine(Directory.GetCurrentDirectory(), "AMHE1");
        IDisposable context = ServerContent.PreserveContext("AMHE1");
        try
        {
            ServerContent.Open(directory, "AMHE1");
            return context;
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }
}
