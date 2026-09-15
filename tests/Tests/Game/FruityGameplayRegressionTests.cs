using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

/// <summary>
/// Project Prime-native reproductions of the gameplay regressions found while
/// comparing the older Fruity implementation. These tests deliberately stop at
/// the existing admission, spawn, collision, and damage seams; they do not carry
/// any Fruity implementation code into the game.
/// </summary>
[Collection("Match baseline globals")]
public sealed class FruityGameplayRegressionTests
{
    [Trait("RequiresGameContent", "true")]
    [Fact]
    public void FreeForAllPlayersHaveUniqueGameplayTeamIdentity()
    {
        using var content = OpenContent();
        using var match = OpenMatch(new MatchRules(MatchMode.Battle, "MP1 SANCTORUS", maxPlayers: 4),
            (Hunter.Samus, "FFA Samus"), (Hunter.Kanden, "FFA Kanden"),
            (Hunter.Noxus, "FFA Noxus"), (Hunter.Trace, "FFA Trace"));

        Assert.Equal(4, match.Scene.Players.ActiveCount);
        var teamIndices = new HashSet<int>();
        for (int slot = 0; slot < 4; slot++)
        {
            ServerPeer? peer = match.Network.Peers[slot];
            Assert.NotNull(peer);
            PlayerEntity player = match.Scene.Players[slot];
            Assert.Equal((byte)slot, peer!.TeamIndex);
            Assert.Equal(slot, player.TeamIndex);
            teamIndices.Add(player.TeamIndex);
        }
        Assert.Equal(4, teamIndices.Count);
    }

    [Trait("RequiresGameContent", "true")]
    [Fact]
    public void FfaBombDamagesOtherSeat()
    {
        using var content = OpenContent();
        using var match = OpenMatch(new MatchRules(MatchMode.Battle, "MP1 SANCTORUS", maxPlayers: 2),
            (Hunter.Sylux, "FFA Sylux"), (Hunter.Samus, "FFA Samus"));
        Scene scene = match.Scene;
        PlayerEntity owner = scene.Players[0];
        PlayerEntity victim = scene.Players[1];
        AdvancePastSpawnProtection(scene, victim);
        RepositionVictim(owner, victim, distance: 1.0f);

        var metrics = new WeaponHarnessMetrics();
        BombEntity? bomb = BombEntity.Spawn(owner,
            Matrix4.CreateTranslation(victim.Position), scene);
        Assert.True(metrics.TryRecordShot(bomb != null, spawnedProjectiles: bomb == null ? 0 : 1));
        Assert.NotNull(bomb);
        Assert.True(bomb!.CombatShot.IsValid);
        Assert.False(scene.Services.Combat!.IsStaleSource(bomb));
        Assert.True(owner.TryRegisterLockjawBomb(bomb!));
        bomb!.Radius = 4;
        bomb.SelfRadius = 4;
        bomb.Damage = 10;
        bomb.EnemyDamage = 10;

        int previousHealth = victim.Health;
        bool alive = bomb.Process();
        int resolvedDamage = previousHealth - victim.Health;
        metrics.RecordHit(resolvedDamage, victim.Health == 0);
        metrics.AssertSane(projectilesArePellets: false);

        Assert.True(alive);
        Assert.True(resolvedDamage > 0,
            $"ownerTeam={owner.TeamIndex} victimTeam={victim.TeamIndex} "
            + $"ownerHealth={owner.Health} victimHealth={victim.Health} "
            + $"previousHealth={previousHealth} bombPosition={bomb.Position} "
            + $"victimPosition={victim.Position} victimSphere={victim.Volume.SpherePosition} "
            + $"radius={bomb.Radius} flags={bomb.Flags}");
        Assert.True(victim.Health < previousHealth);
        Assert.Equal(1, metrics.ResolvedHits);
        Assert.Equal(1, metrics.ConfirmedHits);
        Assert.Equal(0, metrics.Kills);
    }

    [Trait("RequiresGameContent", "true")]
    [Fact]
    public void FfaShockCoilLifeDrainRecognizesEnemy()
    {
        using var content = OpenContent();
        using var match = OpenMatch(new MatchRules(MatchMode.Battle, "MP1 SANCTORUS", maxPlayers: 2),
            (Hunter.Sylux, "FFA Sylux"), (Hunter.Samus, "FFA Samus"));
        Scene scene = match.Scene;
        PlayerEntity owner = scene.Players[0];
        PlayerEntity victim = scene.Players[1];
        AdvancePastSpawnProtection(scene, victim);
        // Keep the target inside the first continuous-beam segment. The
        // weapon-specific assertion is about affinity/life-drain resolution,
        // not projectile travel distance.
        // The continuous weapon's authored 10/32 damage accumulator resolves
        // on a deterministic even-frame boundary. Advancing only this match's
        // headless scene keeps the scenario repeatable without a global clock.
        // Eight frames lands on the first even accumulator boundary that
        // produces one authored Shock Coil damage point for this rig.
        for (int i = 0; i < 8; i++) scene.StepHeadlessFrame(advanceMatch: false);
        RepositionBeamScenario(scene, owner, victim, distance: 0.75f);
        owner.ModArmAffinityWeapon();
        owner.EquipInfo.InfiniteAmmo = true;
        owner.Health = owner.HealthMax - 5;
        victim.Health = victim.HealthMax;

        var metrics = new WeaponHarnessMetrics();
        BeamResultFlags spawnResult = BeamProjectileEntity.Spawn(owner, owner.EquipInfo,
            owner.Position, owner.FacingVector, BeamSpawnFlags.NoMuzzle, owner.NodeRef, scene);
        Assert.True(spawnResult.TestFlag(BeamResultFlags.Spawned));
        BeamProjectileEntity beam = Assert.Single(owner.EquipInfo.Beams,
            candidate => candidate.Lifespan > 0);
        Assert.True(beam.CombatShot.IsValid);
        Assert.False(scene.Services.Combat!.IsStaleSource(beam));
        Assert.Equal(BeamType.ShockCoil, beam.Beam);
        Assert.True(beam.Flags.TestFlag(BeamFlags.LifeDrain));
        // The real firing path acquires this target before the continuous
        // beam is processed. Pin the deterministic rig to that same target
        // after the production spawn seam; no target-search behavior is under
        // test here.
        beam.Target = victim;
        // Sanctorus' central pad has authored room geometry touching the
        // first 60 Hz beam segment. Suppress only room-surface resolution so
        // this rig isolates the production player-collision/affinity path.
        beam.Flags &= ~BeamFlags.SurfaceCollision;
        Assert.True(metrics.TryRecordShot(accepted: true, spawnedProjectiles: 1));

        int previousHealth = victim.Health;
        int previousOwnerHealth = owner.Health;
        _ = beam.Process();
        int resolvedDamage = previousHealth - victim.Health;
        metrics.RecordHit(resolvedDamage, victim.Health == 0);
        metrics.AssertSane(projectilesArePellets: false);

        Assert.True(resolvedDamage > 0,
            $"ownerTeam={owner.TeamIndex} victimTeam={victim.TeamIndex} "
            + $"ownerHealth={owner.Health} victimHealth={victim.Health} "
            + $"previousHealth={previousHealth} previousOwnerHealth={previousOwnerHealth} "
            + $"frame={scene.FrameCount} beamPosition={beam.Position} "
            + $"beamBack={beam.BackPosition} beamVelocity={beam.Velocity} "
            + $"victimPosition={victim.Position} victimSphere={victim.Volume.SpherePosition} "
            + $"flags={beam.Flags} damage={beam.Damage} cylinderRadius={beam.CylinderRadius} "
            + $"lifespan={beam.Lifespan} remaining={beam.RemainingLifeTicks} owner={beam.Owner}");
        Assert.True(owner.Health > previousOwnerHealth,
            $"life drain did not resolve for enemy seat: ownerTeam={owner.TeamIndex} "
            + $"victimTeam={victim.TeamIndex} ownerHealth={owner.Health} "
            + $"before={previousOwnerHealth} damage={resolvedDamage}");
        Assert.Equal(1, metrics.ResolvedHits);
        Assert.Equal(1, metrics.ConfirmedHits);
        Assert.Equal(0, metrics.Kills);
    }

    [Trait("RequiresGameContent", "true")]
    [Fact]
    public void TeamModeStillUsesConfiguredTeamIdentity()
    {
        using var content = OpenContent();
        using var match = OpenMatch(new MatchRules(MatchMode.TeamBattle, "MP1 SANCTORUS", maxPlayers: 2,
            teamBalancePolicy: TeamBalancePolicy.Locked, teamCount: 2),
            (Hunter.Samus, "Orange Samus"), (Hunter.Kanden, "Green Kanden"));

        ServerPeer? orangePeer = match.Network.Peers[0];
        ServerPeer? greenPeer = match.Network.Peers[1];
        Assert.NotNull(orangePeer);
        Assert.NotNull(greenPeer);
        Assert.Equal((byte)0, orangePeer!.TeamIndex);
        Assert.Equal((byte)1, greenPeer!.TeamIndex);
        Assert.Equal(0, match.Scene.Players[0].TeamIndex);
        Assert.Equal(1, match.Scene.Players[1].TeamIndex);
        Assert.Equal(Team.Orange, match.Scene.Players[0].Team);
        Assert.Equal(Team.Green, match.Scene.Players[1].Team);
    }

    [Fact]
    public void HarnessSanityMetricsSeparatePelletProjectilesFromShotAttempts()
    {
        var metrics = new WeaponHarnessMetrics();
        Assert.True(metrics.TryRecordShot(accepted: true, spawnedProjectiles: 3));
        metrics.RecordHit(resolvedDamage: 12, killed: false);

        metrics.AssertSane(projectilesArePellets: true);
        Assert.Equal(1, metrics.RequestedShots);
        Assert.Equal(1, metrics.AcceptedShots);
        Assert.Equal(3, metrics.SpawnedProjectiles);
        Assert.Equal(1, metrics.ResolvedHits);
        Assert.Equal(1, metrics.ConfirmedHits);
        Assert.Equal(0, metrics.Kills);
    }

    private static MatchHarness OpenMatch(MatchRules rules,
        params (Hunter Hunter, string Name)[] roster)
    {
        if (roster.Length == 0 || roster.Length > rules.MaxPlayers)
            throw new ArgumentOutOfRangeException(nameof(roster));

        var simulation = new ServerSimulation(rules);
        var transport = new RegressionTransport();
        var network = new ServerNetwork(transport, rules, matchId: 91);
        try
        {
            for (int slot = 0; slot < roster.Length; slot++)
            {
                (Hunter hunter, string name) = roster[slot];
                var join = new JoinPacket(NetHeader.Version, (ulong)(0x400 + slot), hunter, name);
                Assert.True(network.SubmitLegacyJoinForTesting(
                    new IPEndPoint(IPAddress.Loopback, 39000 + slot), join));
            }
            foreach (ServerPeer? peer in network.Peers)
            {
                if (peer == null) continue;
                Assert.True(peer.Connection.Ready(network.MatchId));
            }

            // Start in Playing so this focused harness observes the same
            // activation path as a running worker without waiting for a lobby
            // countdown. Admission still owns slot/team assignment.
            simulation.Scene.Match.Phase = MatchPhase.Playing;
            network.Phase = MatchPhase.Playing;
            simulation.Step(network, tick: 1);
            return new MatchHarness(simulation, network, transport);
        }
        catch
        {
            simulation.Dispose();
            transport.Dispose();
            throw;
        }
    }

    private static void RepositionVictim(PlayerEntity owner, PlayerEntity victim,
        float distance)
    {
        Vector3 facing = VectorMath.NormalizeHorizontalOr(owner.FacingVector,
            -Vector3.UnitZ);
        Vector3 position = owner.Position + facing * distance;
        victim.Reposition(position, -facing, owner.NodeRef);
        // Reposition updates the entity transform, while the collision volume
        // is refreshed by the same seam used for a replicated position update.
        victim.ModRefreshNodeRef(position);
    }

    private static void RepositionBeamScenario(Scene scene, PlayerEntity owner,
        PlayerEntity victim, float distance)
    {
        PlayerSpawnEntity? openSpawn = null;
        foreach (PlayerSpawnEntity candidate in scene.GetPlayerSpawnEntities())
        {
            if (candidate.Position.X * candidate.Position.X
                    + candidate.Position.Z * candidate.Position.Z < 16)
            {
                openSpawn = candidate;
                break;
            }
        }
        Assert.NotNull(openSpawn);
        // Stay above the floor collider at the central pad; the test is about
        // beam/player affinity resolution, so the target is intentionally kept
        // in a short unobstructed segment.
        Vector3 position = openSpawn!.Position.AddY(2);
        owner.Reposition(position, openSpawn.FacingVector, openSpawn.NodeRef);
        owner.ModRefreshNodeRef(position);
        RepositionVictim(owner, victim, distance);
    }

    private static void AdvancePastSpawnProtection(Scene scene, PlayerEntity player)
    {
        int protectionTicks = SimTicks.From30HzFrames(player.Values.SpawnInvulnerability);
        for (int tick = 0; tick <= protectionTicks; tick++)
        {
            scene.StepHeadlessFrame(advanceMatch: false);
        }
    }

    private static IDisposable OpenContent()
    {
        string? configured = Environment.GetEnvironmentVariable("GAME_DATA_DIRECTORY");
        string[] starts = configured is null
            ? new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory }
            : new[] { configured, Directory.GetCurrentDirectory(), AppContext.BaseDirectory };
        foreach (string start in starts)
        {
            DirectoryInfo? directory = new(Path.GetFullPath(start));
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, "AMHE1");
                if (File.Exists(Path.Combine(candidate, "_bin", "arm9.bin"))
                    && Directory.Exists(Path.Combine(candidate, "models"))
                    && Directory.Exists(Path.Combine(candidate, "levels")))
                {
                    IDisposable context = ServerContent.PreserveContext("AMHE1");
                    try
                    {
                        ServerContent.Open(candidate, "AMHE1");
                        return context;
                    }
                    catch
                    {
                        context.Dispose();
                        throw;
                    }
                }
                directory = directory.Parent;
            }
        }
        throw new DirectoryNotFoundException("AMHE1 extracted content was not found.");
    }

    private sealed class MatchHarness : IDisposable
    {
        private readonly ServerSimulation _simulation;
        private readonly RegressionTransport _transport;

        public MatchHarness(ServerSimulation simulation, ServerNetwork network,
            RegressionTransport transport)
        {
            _simulation = simulation;
            Network = network;
            _transport = transport;
        }

        public ServerNetwork Network { get; }
        public Scene Scene => _simulation.Scene;

        public void Dispose()
        {
            _simulation.Dispose();
            _transport.Dispose();
        }
    }

    /// <summary>
    /// A bounded, per-match counter set. A pellet weapon records one accepted
    /// shot and multiple spawned projectiles; it must not be judged by the
    /// non-pellet requested >= accepted >= spawned relationship.
    /// </summary>
    private sealed class WeaponHarnessMetrics
    {
        private const int MaximumShotAttempts = 64;
        private const int MaximumProjectilesPerShot = 8;
        private const int MaximumProjectiles = MaximumShotAttempts * MaximumProjectilesPerShot;
        private const int MaximumResolvedHits = MaximumProjectiles;

        public int RequestedShots { get; private set; }
        public int AcceptedShots { get; private set; }
        public int SpawnedProjectiles { get; private set; }
        public int ResolvedHits { get; private set; }
        public int ConfirmedHits { get; private set; }
        public int Kills { get; private set; }

        public bool TryRecordShot(bool accepted, int spawnedProjectiles)
        {
            if (spawnedProjectiles < 0 || spawnedProjectiles > MaximumProjectilesPerShot
                || RequestedShots >= MaximumShotAttempts
                || accepted && SpawnedProjectiles > MaximumProjectiles - spawnedProjectiles)
                return false;
            RequestedShots++;
            if (!accepted) return spawnedProjectiles == 0;
            AcceptedShots++;
            SpawnedProjectiles = checked(SpawnedProjectiles + spawnedProjectiles);
            return true;
        }

        public void RecordHit(int resolvedDamage, bool killed)
        {
            if (resolvedDamage <= 0) return;
            if (ResolvedHits >= MaximumResolvedHits) return;
            ResolvedHits++;
            ConfirmedHits++;
            if (killed) Kills++;
        }

        public void AssertSane(bool projectilesArePellets)
        {
            Assert.InRange(RequestedShots, 0, MaximumShotAttempts);
            Assert.InRange(AcceptedShots, 0, RequestedShots);
            Assert.InRange(SpawnedProjectiles, 0, MaximumProjectiles);
            // A splash/continuous projectile may resolve against more than
            // one seat; resolved hits intentionally have no generic ordering
            // relation to the projectile count.
            Assert.InRange(ResolvedHits, 0, MaximumResolvedHits);
            Assert.InRange(ConfirmedHits, 0, ResolvedHits);
            Assert.InRange(Kills, 0, ConfirmedHits);
            if (projectilesArePellets)
            {
                Assert.True(SpawnedProjectiles >= AcceptedShots);
            }
            else
            {
                Assert.True(AcceptedShots >= SpawnedProjectiles);
            }
        }
    }

    private sealed class RegressionTransport : INetTransport
    {
        public int LocalPort => 0;
        public long PacketsDropped => 0;
        public int QueuedPackets => 0;
        public int HeldIncomingPackets => 0;
        public int HeldOutgoingPackets => 0;
        public NetTrafficMetrics Metrics { get; } = new();

        public void Dispose() { }
        public void SetKeepAlive(IPEndPoint? target, ReadOnlySpan<byte> datagram = default) { }
        public void SetKeepAlives(ReadOnlySpan<NetKeepAlive> entries) { }
        public void AnswerPingsImmediately() { }
        public IEnumerable<ReceivedPacket> Drain()
            => Array.Empty<ReceivedPacket>();
        public void EnqueueForPlayback(byte[] data, int length)
            => throw new NotSupportedException();
        public void Send(IPEndPoint target, PacketType type,
            ReadOnlySpan<byte> payload, long extraHoldTicks = 0) { }
        public void SendDatagram(IPEndPoint target, ReadOnlySpan<byte> datagram) { }
        public void SendDatagram(IPEndPoint target, ReadOnlySpan<byte> datagram,
            long extraHoldTicks = 0) { }
    }
}
