using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Tests.Fidelity;

internal sealed record FidelityAuthoritativeAction(uint Tick,
    Action<ServerSimulation, ServerNetwork> Apply);

internal sealed record FidelityScenarioSpec(string CaseId, MatchRules Rules, uint MaximumTicks,
    uint Seed1, uint Seed2, Func<uint, InputButtons> Input,
    IReadOnlyList<FidelityAuthoritativeAction> AuthoritativeActions,
    string SourceCommit, string ReferenceDigest, string ReproductionCommand)
{
    public void Validate()
    {
        if (String.IsNullOrWhiteSpace(CaseId) || Rules.MaxPlayers != 1
            || MaximumTicks is < 1 or > FidelityCaseParser.MaximumTicks
            || AuthoritativeActions.Count > FidelityCaseParser.MaximumActions
            || AuthoritativeActions.Any(action => action.Tick >= MaximumTicks))
            throw new ArgumentException("Fidelity scenario specification is invalid.");
        if (AuthoritativeActions.GroupBy(action => action.Tick).Any(group => group.Count() > 64))
            throw new ArgumentException("A fidelity tick cannot contain more than 64 authoritative actions.");
    }
}

internal sealed record FidelityObservation(uint Tick,
    IReadOnlyDictionary<string, string> Exact,
    IReadOnlyDictionary<string, double> Numeric);

internal sealed record FidelityRunResult(string CaseId, uint Seed1, uint Seed2,
    string SourceCommit, string ReferenceDigest, string ReproductionCommand,
    IReadOnlyList<FidelityObservation> Observations, string StateDigest);

internal static class FidelityScenarioRunner
{
    public static FidelityRunResult Run(FidelityScenarioSpec spec)
    {
        spec.Validate();
        using var harness = new Harness(spec.Rules, spec.Seed1, spec.Seed2);
        Dictionary<uint, FidelityAuthoritativeAction[]> actions = Actions(spec);
        var observations = new List<FidelityObservation>(checked((int)spec.MaximumTicks));
        for (uint tick = 0; tick < spec.MaximumTicks; tick++)
        {
            Step(harness, spec, actions, tick);
            observations.Add(Capture(harness.Simulation, tick));
        }
        return Result(spec, observations);
    }

    public static FidelityRunResult RunInterleaved(FidelityScenarioSpec primary,
        FidelityScenarioSpec other, uint disposeOtherBeforeTick)
    {
        primary.Validate(); other.Validate();
        if (disposeOtherBeforeTick >= primary.MaximumTicks)
            throw new ArgumentOutOfRangeException(nameof(disposeOtherBeforeTick));
        using var first = new Harness(primary.Rules, primary.Seed1, primary.Seed2);
        Harness? second = new(other.Rules, other.Seed1, other.Seed2);
        Dictionary<uint, FidelityAuthoritativeAction[]> firstActions = Actions(primary);
        Dictionary<uint, FidelityAuthoritativeAction[]> secondActions = Actions(other);
        var observations = new List<FidelityObservation>(checked((int)primary.MaximumTicks));
        try
        {
            for (uint tick = 0; tick < primary.MaximumTicks; tick++)
            {
                if (tick == disposeOtherBeforeTick)
                {
                    second!.Dispose();
                    second = null;
                }
                if (second != null && tick < other.MaximumTicks)
                    Step(second, other, secondActions, tick);
                Step(first, primary, firstActions, tick);
                observations.Add(Capture(first.Simulation, tick));
            }
        }
        finally { second?.Dispose(); }
        return Result(primary, observations);
    }

    private static Dictionary<uint, FidelityAuthoritativeAction[]> Actions(FidelityScenarioSpec spec)
        => spec.AuthoritativeActions.GroupBy(action => action.Tick)
            .ToDictionary(group => group.Key, group => group.ToArray());

    private static void Step(Harness harness, FidelityScenarioSpec spec,
        IReadOnlyDictionary<uint, FidelityAuthoritativeAction[]> actions, uint tick)
    {
        if (actions.TryGetValue(tick, out FidelityAuthoritativeAction[]? current))
            foreach (FidelityAuthoritativeAction action in current)
                action.Apply(harness.Simulation, harness.Network);
        harness.ApplyInput(tick, spec.Input(tick));
        harness.Simulation.Step(harness.Network, tick);
    }

    private static FidelityRunResult Result(FidelityScenarioSpec spec,
        List<FidelityObservation> observations) => new(spec.CaseId, spec.Seed1, spec.Seed2,
        spec.SourceCommit, spec.ReferenceDigest, spec.ReproductionCommand,
        observations.AsReadOnly(), Digest(observations));

    private static FidelityObservation Capture(ServerSimulation simulation, uint tick)
    {
        Scene scene = simulation.Scene;
        PlayerEntity player = scene.Players[0];
        PlayerMatchStats stats = scene.Match.Players[0];
        ItemSpawnEntity? spawner = FirstItemSpawner(scene);
        NodeDefenseEntity? node = FirstNode(scene);
        var exact = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["match.phase"] = scene.Match.Phase.ToString(),
            ["input.buttons"] = simulation.Combat.GetCommand(0).Buttons.ToString(),
            ["input.hasInput"] = player.Input.HasInput.ToString(CultureInfo.InvariantCulture),
            ["camera.active"] = (scene.CameraSequences.Current != null).ToString(CultureInfo.InvariantCulture),
            ["camera.blocksInput"] = (scene.CameraSequences.Current?.BlockInput ?? false).ToString(CultureInfo.InvariantCulture),
            ["player.active"] = player.Active.ToString(CultureInfo.InvariantCulture),
            ["player.altForm"] = player.IsAltForm.ToString(CultureInfo.InvariantCulture),
            ["player.weapon"] = player.CurrentWeapon.ToString(),
            ["item.activeCount"] = ActiveItemCount(scene).ToString(CultureInfo.InvariantCulture),
            ["projectile.activeCount"] = ActiveProjectileCount(scene).ToString(CultureInfo.InvariantCulture),
            ["pickup.first.itemActive"] = (spawner?.Item != null).ToString(CultureInfo.InvariantCulture),
            ["pickup.first.spawnCount"] = (spawner?.ServerSpawnCount ?? 0).ToString(CultureInfo.InvariantCulture),
            ["pickup.first.respawnTicks"] = (spawner?.ServerRespawnTicks ?? 0).ToString(CultureInfo.InvariantCulture),
            ["pickup.first.lastConsumer"] = (spawner?.LastConsumerSlot ?? byte.MaxValue).ToString(CultureInfo.InvariantCulture),
            ["objective.first.currentTeam"] = (node?.CurrentTeam ?? NodeDefenseEntity.NeutralTeam).ToString(CultureInfo.InvariantCulture),
            ["objective.first.occupyingTeam"] = (node?.OccupyingTeam ?? NodeDefenseEntity.NeutralTeam).ToString(CultureInfo.InvariantCulture),
            ["objective.first.occupied"] = (node?.IsOccupied ?? false).ToString(CultureInfo.InvariantCulture),
            ["objective.first.inProgress"] = (node?.InProgress ?? false).ToString(CultureInfo.InvariantCulture),
            ["world.sha256"] = WorldDigest(scene),
            ["players.sha256"] = PlayerStateDigest(simulation)
        };
        var numeric = new SortedDictionary<string, double>(StringComparer.Ordinal)
        {
            ["scene.frame"] = scene.FrameCount,
            ["scene.liveFrames"] = scene.LiveFrames,
            ["match.time"] = scene.Match.MatchTime,
            ["player.position.x"] = player.Position.X,
            ["player.position.y"] = player.Position.Y,
            ["player.position.z"] = player.Position.Z,
            ["player.speed.x"] = player.Speed.X,
            ["player.speed.y"] = player.Speed.Y,
            ["player.speed.z"] = player.Speed.Z,
            ["player.health"] = player.Health,
            ["player.respawnTicks"] = player.RespawnTimer,
            ["player.points"] = stats.Points,
            ["player.kills"] = stats.Kills,
            ["player.deaths"] = stats.Deaths,
            ["player.nodesCaptured"] = stats.NodesCaptured,
            ["objective.first.progress"] = node?.Progress ?? 0
        };
        if (numeric.Values.Any(value => !Double.IsFinite(value)))
            throw new InvalidDataException("Authoritative observation contains a non-finite value.");
        return new FidelityObservation(tick, exact, numeric);
    }

    private static int ActiveProjectileCount(Scene scene)
    {
        int count = 0;
        foreach (BeamProjectileEntity entity in scene.GetBeamProjectileEntities())
        {
            if (entity.Active)
                count++;
        }
        return count;
    }

    private static int ActiveItemCount(Scene scene)
    {
        int count = 0;
        foreach (ItemInstanceEntity item in scene.GetItemInstanceEntities())
        {
            if (item.DespawnTimer != 0)
                count++;
        }
        return count;
    }

    private static ItemSpawnEntity? FirstItemSpawner(Scene scene)
    {
        foreach (ItemSpawnEntity spawner in scene.GetItemSpawnEntities())
            return spawner;
        return null;
    }

    private static NodeDefenseEntity? FirstNode(Scene scene)
    {
        foreach (NodeDefenseEntity node in scene.GetNodeDefenseEntities())
            return node;
        return null;
    }

    private static string WorldDigest(Scene scene)
    {
        var capture = new WorldStateCapture();
        capture.Capture(scene, 1, 1, 0);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (WorldRecord record in capture.Records)
        {
            byte[] bytes = new byte[WorldRecord.Size];
            record.Write(bytes);
            hash.AppendData(bytes);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string PlayerStateDigest(ServerSimulation simulation)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (SnapshotPlayer captured in simulation.States)
        {
            SnapshotPlayer state = captured;
            state.ConnectionId = 0;
            byte[] bytes = new byte[SnapshotPlayer.Size];
            state.Write(bytes);
            hash.AppendData(bytes);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string Digest(IReadOnlyList<FidelityObservation> observations)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (FidelityObservation observation in observations)
        {
            Append(hash, observation.Tick.ToString(CultureInfo.InvariantCulture)); Append(hash, "\n");
            foreach ((string key, string value) in observation.Exact) Append(hash, $"e\0{key}\0{value}\n");
            foreach ((string key, double value) in observation.Numeric)
                Append(hash, $"n\0{key}\0{BitConverter.DoubleToInt64Bits(value):x16}\n");
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, string value) => hash.AppendData(Encoding.UTF8.GetBytes(value));

    private sealed class Harness : IDisposable
    {
        public ServerSimulation Simulation { get; }
        public ServerNetwork Network { get; }

        public Harness(MatchRules rules, uint seed1, uint seed2)
        {
            Simulation = new ServerSimulation(rules, botFill: new BotFillPolicy(), rng1: seed1, rng2: seed2);
            Network = new ServerNetwork(new SilentTransport(), rules);
            var connection = new NetConnection(100, new IPEndPoint(IPAddress.Loopback, 10001), 1, 0);
            connection.Ready(1);
            var peer = new ServerPeer(connection,
                new JoinPacket { Hunter = Hunter.Samus, Name = "Fidelity", Nonce = 100 }, 0, 0);
            FieldInfo field = typeof(ServerNetwork).GetField("_peers", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(typeof(ServerNetwork).FullName, "_peers");
            ((ServerPeer?[])field.GetValue(Network)!)[0] = peer;
        }

        public void ApplyInput(uint tick, InputButtons buttons) => Network.Peers[0]!.Inputs.Receive(
            [new InputCommand(tick, tick, tick, buttons, InputButtons.None, -Vector3.UnitZ, InputCommand.NoWeapon)], tick);
        public void Dispose() => Simulation.Dispose();
    }

    private sealed class SilentTransport : INetTransport
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
        public IEnumerable<ReceivedPacket> Drain() => Array.Empty<ReceivedPacket>();
        public void EnqueueForPlayback(byte[] data, int length) => throw new NotSupportedException();
        public void Send(IPEndPoint target, PacketType type, ReadOnlySpan<byte> payload, long extraHoldTicks = 0) { }
        public void SendDatagram(IPEndPoint target, ReadOnlySpan<byte> datagram, long extraHoldTicks) { }
        public void SendDatagram(IPEndPoint target, ReadOnlySpan<byte> datagram) { }
    }
}
