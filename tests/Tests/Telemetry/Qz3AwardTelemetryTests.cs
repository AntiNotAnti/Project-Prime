using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MphRead.Mods.Network;
using MphRead.Telemetry;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests.Telemetry;

[Collection("Match baseline globals")]
public sealed class Qz3AwardTelemetryTests
{
    [Fact]
    public void CollectorRecordsEveryPlanAwardKindAndKeepsPerKindCounters()
    {
        var collector = new TelemetryCollector(
            MatchRules.CreateDefault(MatchMode.Battle, "qz3-award-telemetry"), 17, 0, capacity: 32);
        SnapshotPlayer player = Snapshot();
        collector.Sample(0, new[] { player });

        foreach (MatchAwardKind kind in Enum.GetValues<MatchAwardKind>())
            collector.Award(Award(kind));

        MatchTelemetry result = collector.Complete(10, completed: true);
        Assert.Equal(8L, collector.AwardsRecorded);
        Assert.Equal(0L, collector.AwardsDropped);
        Assert.Equal(8, result.Events.Count(e => e.Kind == TelemetryKind.Award));
        Assert.Equal(Enumerable.Repeat(1L, 8), collector.AwardsByKind.ToArray());
        Assert.Equal(
            Enum.GetValues<MatchAwardKind>().Select(kind => (int)kind),
            result.Events.Where(e => e.Kind == TelemetryKind.Award).Select(e => e.Value));
    }

    [Fact]
    public void CollectorDropsAwardWhenItsBoundedEventBufferIsFull()
    {
        var collector = new TelemetryCollector(
            MatchRules.CreateDefault(MatchMode.Battle, "qz3-award-telemetry"), 18, 0, capacity: 1);
        collector.Sample(0, new[] { Snapshot() }); // The one bounded slot is a position sample.
        collector.Award(Award(MatchAwardKind.Assist, tick: 1));

        MatchTelemetry result = collector.Complete(1, completed: true);
        Assert.Equal(0L, collector.AwardsRecorded);
        Assert.Equal(1L, collector.AwardsDropped);
        Assert.Equal(1, result.DroppedEvents);
        Assert.DoesNotContain(result.Events, e => e.Kind == TelemetryKind.Award);
    }

    [Fact]
    public void CollectorRecordsNormalizedSemanticIdentityAndEntityBeforePlaying()
    {
        var collector = new TelemetryCollector(
            MatchRules.CreateDefault(MatchMode.Defender, "qz3-semantic-telemetry"), 18, 0, capacity: 4);
        MatchEvent value = new(77, 3, 18, 2, MatchEventKind.ObjectiveDefended,
            new CombatActor(0, 11, 1), new CombatActor(1, 22, 2), EntityId: 44,
            Team: 1, Flags: MatchEventFlags.Bot);

        collector.Semantic(value);

        TelemetryEvent stored = Assert.Single(collector.Complete(4, completed: false).Events);
        Assert.Equal(TelemetryKind.MatchSemantic, stored.Kind);
        Assert.Equal((int)MatchEventKind.ObjectiveDefended, stored.Value);
        Assert.Equal(77u, stored.SemanticId);
        Assert.Equal(44u, stored.Subject);
        Assert.Equal((byte)1, stored.OtherSlot);
        Assert.Equal(1L, collector.SemanticEventsRecorded);
        Assert.Equal(0L, collector.SemanticEventsDropped);
    }

    [Fact]
    public void TelemetryCommandReadsAwardsAndExcludesThemFromSpatialAggregation()
    {
        string directory = Path.Combine(Path.GetTempPath(), "prime-qz3-telemetry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string input = Path.Combine(directory, "award.telemetry.json.gz");
        string prefix = Path.Combine(directory, "aggregate");
        var telemetry = new MatchTelemetry(
            MatchTelemetry.CurrentFormat, Guid.NewGuid(), "qz3-command", MatchMode.Battle, 19, 0, 60, true, 0,
            new[]
            {
                new TelemetryEvent(0, TelemetryKind.Position, 0, 1, 8, 0, 8, Team: 0, Hunter: 0),
                // If treated as a position, this award would create an origin cell in the route SVG.
                new TelemetryEvent(1, TelemetryKind.Award, 0, 1, 0, 0, 0, Team: 0, Hunter: 255,
                    Value: (int)MatchAwardKind.Assist, Subject: 201),
                new TelemetryEvent(2, TelemetryKind.MatchSemantic, 0, 1, 0, 0, 0, Team: 0,
                    Value: (int)MatchEventKind.MatchPointReached, SemanticId: 301)
            });

        try
        {
            using (FileStream file = File.Create(input))
            using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
                JsonSerializer.Serialize(gzip, telemetry);

            Assembly tools = Assembly.Load("FruityPrimeTools");
            Type command = tools.GetType("MphRead.TelemetryCommand")!;
            MethodInfo read = command.GetMethod("Read", BindingFlags.Static | BindingFlags.NonPublic)!;
            var restored = Assert.IsType<MatchTelemetry>(read.Invoke(null, new object[] { input }));
            Assert.Contains(restored.Events, e => e.Kind == TelemetryKind.Award);
            Assert.Contains(restored.Events, e => e.Kind == TelemetryKind.MatchSemantic && e.SemanticId == 301);

            MethodInfo run = command.GetMethod("Run", BindingFlags.Static | BindingFlags.Public)!;
            Assert.Equal(0, Assert.IsType<int>(run.Invoke(null, new object[]
                { new[] { "telemetry", "routes", input, prefix } })));
            string csv = File.ReadAllText(prefix + ".csv");
            string svg = File.ReadAllText(prefix + ".svg");
            Assert.Contains("Award", csv);
            Assert.Contains("X=8, Z=8", svg);
            Assert.DoesNotContain("X=0, Z=0", svg);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static MatchAward Award(MatchAwardKind kind, uint tick = 10)
        => new(100 + (uint)kind, 200 + (uint)kind, 17, 1, tick, kind,
            new CombatActor(0, 11, 1), CombatActor.None);

    private static SnapshotPlayer Snapshot() => new()
    {
        Slot = 0,
        Hunter = Hunter.Samus,
        TeamIndex = 0,
        Life = 1,
        ConnectionId = 11,
        Health = 100,
        Position = Vector3.One,
        Flags = SnapshotPlayerFlags.Active | SnapshotPlayerFlags.Spawned
    };
}
