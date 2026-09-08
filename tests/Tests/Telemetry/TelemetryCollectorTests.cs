using System;
using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using MphRead.Entities;
using MphRead.Mods.Network;
using MphRead.Telemetry;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests.Telemetry;

[Collection("Match baseline globals")]
public sealed class TelemetryCollectorTests
{
    [Fact]
    public void MissingVictimPositionDropsKillRatherThanCreatingOriginHeatmapPoint()
    {
        var collector = new TelemetryCollector(MatchRules.CreateDefault(MatchMode.Battle, "telemetry-room"), 1, 0);
        var killer = Snapshot(0, Hunter.Trace, 0, 11, Vector3.One);
        collector.Sample(1, new[] { killer });
        collector.Kill(new KillEvent(1, 1, 1, 1, new CombatActor(0, 11, 1), new CombatActor(1, 22, 1),
            4, KillEventFlags.None, ImmutableArray<CombatActor>.Empty));
        var result = collector.Complete(1, false);
        Assert.Empty(result.Events); Assert.Equal(1, result.DroppedEvents);
    }

    [Fact]
    public void DepartedSlotCannotReuseZeroedHunterOrTeamMetadata()
    {
        var collector = new TelemetryCollector(MatchRules.CreateDefault(MatchMode.Battle, "telemetry-room"), 1, 0);
        SnapshotPlayer departed = Snapshot(0, Hunter.Trace, 1, 11, Vector3.One);
        SnapshotPlayer victim = Snapshot(1, Hunter.Kanden, 0, 22, Vector3.UnitX);
        collector.Sample(0, new[] { departed, victim });
        collector.Sample(1, new[] { victim });
        collector.Kill(new KillEvent(1, 1, 1, 1,
            new CombatActor(0, 11, 1), new CombatActor(1, 22, 1), 4,
            KillEventFlags.None, ImmutableArray<CombatActor>.Empty));
        TelemetryEvent kill = Assert.Single(collector.Complete(1, false).Events, e => e.Kind == TelemetryKind.Kill);
        Assert.Equal(0u, kill.Life); Assert.Equal((byte)255, kill.Hunter); Assert.Equal((byte)255, kill.Team);
        collector.Sample(60, new[] { departed, victim });
        Assert.Equal(2u, collector.Complete(60, false).Events.Last(e => e.Kind == TelemetryKind.Position && e.Slot == 0).Life);
    }

    [Fact]
    public void SamplesUseOneSecondCadenceLocalSlotEpochsAndBoundedDrops()
    {
        var rules = MatchRules.CreateDefault(MatchMode.Battle, "telemetry-room");
        var collector = new MphRead.Telemetry.TelemetryCollector(rules, 7, 99, capacity: 4);
        SnapshotPlayer player = Snapshot(0, Hunter.Samus, 1, 11, new(1, 2, 3));

        collector.Sample(0, new[] { player });
        collector.Sample(30, new[] { player });
        collector.Sample(60, new[] { player });
        player.Life = 2;
        player.ConnectionId = 22;
        player.Position = new(4, 5, 6);
        collector.Sample(120, new[] { player });
        collector.Sample(180, new[] { player });
        collector.Sample(240, new[] { player });
        collector.CommitTick(240, completed: true);

        MatchTelemetry match = collector.Complete(999, completed: true);
        TelemetryEvent[] positions = match.Events.Where(e => e.Kind == TelemetryKind.Position).ToArray();

        Assert.True(match.Completed);
        Assert.Equal(0u, match.StartTick);
        Assert.Equal(240u, match.EndTick);
        Assert.Equal(new uint[] { 1, 1, 2, 2 }, positions.Select(e => e.Life));
        Assert.Equal(1, match.DroppedEvents);
        Assert.DoesNotContain("ConnectionId", JsonSerializer.Serialize(match));
    }

    [Fact]
    public void ReusedSlotWithSameRawLifeCannotAttributeStaleActorFacts()
    {
        var rules = MatchRules.CreateDefault(MatchMode.Battle, "telemetry-room");
        var collector = new MphRead.Telemetry.TelemetryCollector(rules, 8, 0, capacity: 16);
        SnapshotPlayer original = Snapshot(0, Hunter.Samus, 0, 11, new(1, 0, 1));
        SnapshotPlayer victim = Snapshot(1, Hunter.Kanden, 1, 22, new(5, 0, 1));
        collector.Sample(0, new[] { original, victim });
        SnapshotPlayer replacement = Snapshot(0, Hunter.Trace, 0, 33, new(9, 0, 1));
        collector.Sample(60, new[] { replacement, victim });

        CombatActor stale = new(0, original.ConnectionId, original.Life);
        CombatActor currentVictim = new(1, victim.ConnectionId, victim.Life);
        Scene scene = Scene.CreateHeadless();
        try
        {
            collector.Combat(new CombatEvent(1, 61, 1, CombatEventKind.Damage, 4, CombatEventFlags.None,
                stale, currentVictim, 70, 10, victim.Position, Vector3.UnitX, 0, 0, 0), scene);
        }
        finally
        {
            scene.CloseHeadless();
        }
        collector.Kill(new KillEvent(2, 62, 8, 1, stale, currentVictim, 4,
            KillEventFlags.None, ImmutableArray<CombatActor>.Empty));
        collector.CommitTick(62, completed: true);

        MatchTelemetry match = collector.Complete(62, completed: true);
        TelemetryEvent damage = Assert.Single(match.Events, e => e.Kind == TelemetryKind.Damage);
        Assert.Equal((byte)255, damage.OtherHunter);
        TelemetryEvent kill = Assert.Single(match.Events, e => e.Kind == TelemetryKind.Kill);
        Assert.Equal((uint)0, kill.Life);
        Assert.Equal((byte)255, kill.Team);
        Assert.Equal((byte)255, kill.Hunter);
        Assert.Equal(victim.Position.X, kill.X);
        Assert.Equal(victim.Position.Z, kill.Z);
    }

    [Fact]
    public void NonPlayingSamplesDoNotLeakIntoAStartedMatch()
    {
        var collector = new MphRead.Telemetry.TelemetryCollector(
            MatchRules.CreateDefault(MatchMode.Battle, "telemetry-room"), 10, 400, capacity: 8);
        SnapshotPlayer player = Snapshot(0, Hunter.Samus, 0, 11, new(1, 0, 1));

        collector.Sample(0, new[] { player }, playing: false);
        collector.Sample(60, new[] { player }, playing: false);
        collector.Sample(120, new[] { player }, playing: true);
        collector.Sample(180, new[] { player }, playing: false);
        collector.CommitTick(180, completed: false);

        MatchTelemetry match = collector.Complete(180, completed: false);
        Assert.Single(match.Events);
        Assert.Equal(120u, match.StartTick);
        Assert.Equal(180u, match.EndTick);
    }

    [Fact]
    public void InitialSpawnFlushesAtFirstPlayingOnlyForMatchingFullActor()
    {
        var rules = MatchRules.CreateDefault(MatchMode.Battle, "telemetry-room");
        SnapshotPlayer original = Snapshot(0, Hunter.Samus, 0, 11, new(2, 1, 3));
        SnapshotPlayer replacement = Snapshot(0, Hunter.Trace, 0, 22, new(8, 1, 9));
        CombatEvent spawn = new(1, 15, 1, CombatEventKind.Spawn, 4, CombatEventFlags.None,
            CombatActor.None, new(0, original.ConnectionId, original.Life), 100, 0,
            original.Position, Vector3.UnitX, 0, 0, 0);
        Scene scene = Scene.CreateHeadless();
        try
        {
            var matching = new MphRead.Telemetry.TelemetryCollector(rules, 12, 0, capacity: 8);
            matching.Sample(0, new[] { original }, playing: false, scene);
            matching.Combat(spawn, scene);
            matching.Sample(120, new[] { original }, playing: true, scene);
            matching.CommitTick(120, completed: true);
            MatchTelemetry retained = matching.Complete(120, completed: true);
            TelemetryEvent flushed = Assert.Single(retained.Events, e => e.Kind == TelemetryKind.Spawn);
            Assert.Equal(120u, flushed.Tick);
            Assert.Equal(original.Position.X, flushed.X);
            Assert.Equal(original.Position.Z, flushed.Z);
            Assert.Equal((uint)1, flushed.Life);

            var replaced = new MphRead.Telemetry.TelemetryCollector(rules, 13, 0, capacity: 8);
            replaced.Sample(0, new[] { original }, playing: false, scene);
            replaced.Combat(spawn, scene);
            replaced.Sample(60, new[] { replacement }, playing: false, scene);
            replaced.Sample(120, new[] { replacement }, playing: true, scene);
            replaced.CommitTick(120, completed: true);
            MatchTelemetry discarded = replaced.Complete(120, completed: true);
            Assert.DoesNotContain(discarded.Events, e => e.Kind == TelemetryKind.Spawn);
        }
        finally
        {
            scene.CloseHeadless();
        }
    }

    [Fact]
    public void TerminalCommitStopsLaterEventsAndUsesCanonicalReportId()
    {
        var collector = new MphRead.Telemetry.TelemetryCollector(
            MatchRules.CreateDefault(MatchMode.Battle, "telemetry-room"), 11, 0, capacity: 8);
        SnapshotPlayer player = Snapshot(0, Hunter.Samus, 0, 11, new(1, 0, 1));
        collector.Sample(0, new[] { player });
        collector.CommitTick(0, completed: true);
        collector.Sample(60, new[] { player });
        collector.World(new WorldEvent(1, 60, 11, 1, WorldSubjectKind.Match,
            WorldSignalKind.OvertimeStarted, 255, 0, CombatActor.None, Vector3.Zero, A: 1));

        Guid reportId = Guid.NewGuid();
        MatchTelemetry match = collector.Complete(60, completed: true, reportId);
        Assert.True(match.Completed);
        Assert.Equal(reportId, match.Id);
        Assert.Single(match.Events);
        Assert.Equal(TelemetryKind.Position, match.Events[0].Kind);
        Assert.Equal(0u, match.EndTick);
    }

    [Fact]
    public void RecordsDamageSpawnKillDeathAndWorldFactsWithActorContext()
    {
        var rules = MatchRules.CreateDefault(MatchMode.Battle, "telemetry-room");
        var collector = new MphRead.Telemetry.TelemetryCollector(rules, 9, 0, capacity: 32);
        SnapshotPlayer victim = Snapshot(0, Hunter.Samus, 0, 11, new(3, 1, 4));
        SnapshotPlayer attacker = Snapshot(1, Hunter.Trace, 1, 22, new(13, 1, 4));
        collector.Sample(0, new[] { victim, attacker });
        CombatActor victimActor = new(0, victim.ConnectionId, victim.Life);
        CombatActor attackerActor = new(1, attacker.ConnectionId, attacker.Life);

        Scene scene = Scene.CreateHeadless();
        try
        {
            PlayerEntity target = scene.Players[0];
            target.TeamIndex = victim.TeamIndex;
            target.Health = victim.Health;
            target.Position = victim.Position;
            target.LoadFlags = LoadFlags.Active;
            scene.InsertEntity(target);
            PlayerEntity enemy = scene.Players[1];
            enemy.TeamIndex = attacker.TeamIndex;
            enemy.Health = attacker.Health;
            enemy.Position = attacker.Position;
            enemy.LoadFlags = LoadFlags.Active;
            scene.InsertEntity(enemy);
            collector.Combat(new CombatEvent(1, 5, 1, CombatEventKind.Spawn, 4, CombatEventFlags.None,
                attackerActor, victimActor, 100, 0, victim.Position, Vector3.UnitX, 0, 0, 0), scene);
            collector.Combat(new CombatEvent(2, 6, 2, CombatEventKind.Damage, 4, CombatEventFlags.Charged,
                attackerActor, victimActor, 70, 30, victim.Position, Vector3.UnitX, 0, 0, 0), scene);
        }
        finally
        {
            scene.CloseHeadless();
        }
        collector.Kill(new KillEvent(3, 7, 9, 1, attackerActor, victimActor, 4,
            KillEventFlags.Headshot, ImmutableArray<CombatActor>.Empty));
        collector.World(new WorldEvent(4, 8, 9, 1, WorldSubjectKind.Node,
            WorldSignalKind.NodeContested, 255, 33, CombatActor.None, new(3, 1, 4), A: 1));
        collector.CommitTick(8, completed: true);

        MatchTelemetry match = collector.Complete(8, completed: true);
        Assert.Contains(match.Events, e => e.Kind == TelemetryKind.Spawn && e.Weapon == 4 && e.OtherSlot == 1
            && e.EnemyDistance is > 9.9f and < 10.1f && e.VisibleEnemies == 1);
        Assert.Contains(match.Events, e => e.Kind == TelemetryKind.Damage && e.Value == 30
            && e.OtherSlot == 1 && e.OtherHunter == (byte)Hunter.Trace);
        Assert.Contains(match.Events, e => e.Kind == TelemetryKind.Death && e.Slot == 0 && e.Weapon == 4);
        Assert.Contains(match.Events, e => e.Kind == TelemetryKind.Kill && e.Slot == 1
            && e.OtherSlot == 0 && e.Value == (int)KillEventFlags.Headshot);
        Assert.Contains(match.Events, e => e.Kind == TelemetryKind.World && e.Value == (int)WorldSignalKind.NodeContested
            && e.Subject == 33 && e.Weapon == 1);
    }

    [Fact]
    public void WriterDrainsMatchAsGzipJsonWithStableEnvelope()
    {
        string directory = Path.Combine(Path.GetTempPath(), "prime-telemetry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Guid id = Guid.NewGuid();
        var match = new MatchTelemetry(1, id, "telemetry-room", MatchMode.Battle, 3, 10, 20, true, 2,
            new[] { new TelemetryEvent(10, TelemetryKind.Position, 0, 1, 1, 2, 3) });
        try
        {
            using (var writer = new MphRead.Telemetry.TelemetryWriter(directory))
                Assert.True(writer.TryWrite(match));

            string path = Path.Combine(directory, $"{id:D}.telemetry.json.gz");
            Assert.True(File.Exists(path));
            using var input = File.OpenRead(path);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            MatchTelemetry restored = JsonSerializer.Deserialize<MatchTelemetry>(gzip)!;
            Assert.Equal(match.Id, restored.Id);
            Assert.Equal(match.DroppedEvents, restored.DroppedEvents);
            Assert.Equal(match.Events, restored.Events);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static SnapshotPlayer Snapshot(byte slot, Hunter hunter, byte team, ulong connection,
        Vector3 position) => new()
    {
        Slot = slot, Hunter = hunter, TeamIndex = team, Life = 1, ConnectionId = connection,
        Health = 100, Position = position, Flags = SnapshotPlayerFlags.Active | SnapshotPlayerFlags.Spawned
    };
}
