using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using ProjectPrime.Server.Shared;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class LateJoinTests
{
    [Fact]
    public void CommandLineRulesAndWireKeepLateJoinPolicyCoherent()
    {
        Assert.Null(ServerLateJoinOptions.Parse(null));
        Assert.Throws<ArgumentException>(() => ServerLateJoinOptions.Parse(null, present: true));
        Assert.Throws<ArgumentException>(() => ServerLateJoinOptions.Parse("unknown", present: true));

        foreach ((string text, LateJoinPolicy policy) in new[]
        {
            ("IMMEDIATE", LateJoinPolicy.JoinImmediately),
            ("Next", LateJoinPolicy.SpectateUntilNextMatch),
            ("disabled", LateJoinPolicy.Disabled)
        })
        {
            Assert.Equal(policy, ServerLateJoinOptions.Parse(text, present: true));
            var rules = new MatchRules(MatchMode.Battle, "MP1 SANCTORUS", maxPlayers: 3,
                lateJoinPolicy: policy);
            byte[] bytes = new byte[MatchRulesWire.Size];
            MatchRulesWire.Write(bytes, rules);
            Assert.Equal((byte)policy, bytes[70]);
            Assert.True(MatchRulesWire.TryRead(bytes, out MatchRules parsed));
            Assert.Equal(policy, parsed.LateJoinPolicy);
        }
    }

    [Fact]
    public void LateJoinWireRejectsNonzeroReservedExtensionBytes()
    {
        var rules = new MatchRules(MatchMode.Battle, "MP1 SANCTORUS",
            lateJoinPolicy: LateJoinPolicy.SpectateUntilNextMatch);
        byte[] bytes = new byte[MatchRulesWire.Size];
        MatchRulesWire.Write(bytes, rules);
        Assert.True(MatchRulesWire.TryRead(bytes, out _));
        foreach ((int index, byte value) in new[]
        {
            (71, (byte)4), // RulesetPreset.Custom is the final assigned value.
            (78, (byte)2), // RankingEligibility.VerifiedServerOnly is the final assigned value.
            (83, (byte)2) // PowerupsEnabled accepts only zero or one.
        })
        {
            byte previous = bytes[index];
            bytes[index] = value;
            Assert.False(MatchRulesWire.TryRead(bytes, out _));
            bytes[index] = previous;
        }
    }

    [Fact]
    public void WaitingForMatchSnapshotHasNoGameplayBodyAndLocksRejoin()
    {
        using Scene scene = new Scene();
        var waiting = new SnapshotPlayer
        {
            Slot = 2,
            Hunter = Hunter.Samus,
            TeamIndex = 0,
            Life = 1,
            ConnectionId = 0x1122334455667788,
            Aim = -Vector3.UnitZ,
            Facing = -Vector3.UnitZ,
            Flags = SnapshotPlayerFlags.Spectating | SnapshotPlayerFlags.WaitingForMatch
        };
        byte[] bytes = new byte[SnapshotPlayer.Size];
        waiting.Write(bytes);
        Assert.True(SnapshotPlayer.TryRead(bytes, out SnapshotPlayer parsed));
        Assert.Equal(waiting.Flags, parsed.Flags);
        Assert.Equal(0, parsed.Health);
        Assert.False((parsed.Flags & (SnapshotPlayerFlags.Active | SnapshotPlayerFlags.Spawned)) != 0);

        SpectatorMode.Reset();
        try
        {
            // Keep this synthetic client-only check independent of whichever
            // headless scene a neighboring test left in the process globals.
            foreach (PlayerEntity player in scene.Players)
            {
                if (player == null) continue;
                player.LoadFlags = LoadFlags.None;
                player.Health = 0;
            }
            MethodInfo apply = typeof(SpectatorMode).GetMethod("ApplyWaitingForMatch",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            apply.Invoke(null, new object[] { scene, true });
            Assert.True(SpectatorMode.WaitingForNextMatch);
            SpectatorMode.Rejoin(scene);
            Assert.True(SpectatorMode.WaitingForNextMatch);
            apply.Invoke(null, new object[] { scene, false });
            Assert.False(SpectatorMode.WaitingForNextMatch);
        }
        finally
        {
            SpectatorMode.Reset();
        }
    }

    [Theory]
    [InlineData(LateJoinPolicy.JoinImmediately, false, true)]
    [InlineData(LateJoinPolicy.SpectateUntilNextMatch, true, true)]
    [InlineData(LateJoinPolicy.Disabled, false, false)]
    public void LoopbackAdmissionAppliesPolicyAfterPlaying(LateJoinPolicy policy,
        bool waiting, bool accepted)
    {
        var rules = new MatchRules(MatchMode.Battle, "MP1 SANCTORUS", maxPlayers: 2,
            lateJoinPolicy: policy);
        using var transport = new NetTransport(0);
        using var firstSocket = new NetTransport(0);
        using var lateSocket = new NetTransport(0);
        var endpoint = new IPEndPoint(IPAddress.Loopback, transport.LocalPort);
        var server = new ServerNetwork(transport, rules)
        {
            Phase = MatchPhase.WaitingForPlayers
        };
        using var first = new NetClient(firstSocket, endpoint, "FIRST", Hunter.Samus);
        JoinAndReady(server, first);
        server.Phase = MatchPhase.Playing;
        server.PhaseRevision = 1;
        ServerPeer firstPeer = Assert.IsType<ServerPeer>(server.Find(first.Connection!.Id));
        firstPeer.Connection.StartPlaying();
        using var late = new NetClient(lateSocket, endpoint, "LATE", Hunter.Kanden);
        Pump(server, new[] { first, late }, () => late.Connection != null || late.Failure != null);

        if (!accepted)
        {
            Assert.Null(late.Connection);
            Assert.Contains("disabled", late.Failure!, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, server.Count);
            return;
        }

        ServerPeer latePeer = Assert.IsType<ServerPeer>(server.Find(late.Connection!.Id));
        Assert.Equal(waiting, latePeer.WaitingForNextMatch);
        Assert.Equal(2, server.Count);
    }

    [Fact]
    public void ReturningParticipantBypassesDisabledLateJoinPolicyOnSameEndpoint()
    {
        var rules = new MatchRules(MatchMode.Battle, "MP1 SANCTORUS", maxPlayers: 1,
            lateJoinPolicy: LateJoinPolicy.Disabled);
        using var transport = new NetTransport(0);
        using var socket = new NetTransport(0);
        var endpoint = new IPEndPoint(IPAddress.Loopback, transport.LocalPort);
        var server = new ServerNetwork(transport, rules)
        {
            Phase = MatchPhase.WaitingForPlayers
        };
        using var client = new NetClient(socket, endpoint, "RECONNECT", Hunter.Samus);
        JoinAndReady(server, client);
        server.Phase = MatchPhase.Playing;
        server.PhaseRevision = 1;
        ServerPeer peer = Assert.IsType<ServerPeer>(server.Find(client.Connection!.Id));
        peer.Connection.StartPlaying();
        peer.HasParticipated = true;
        ulong oldId = peer.Connection.Id;
        client.Reconnect();
        Pump(server, new[] { client }, () => client.Connection != null && client.Connection.Id != oldId);
        Assert.NotNull(client.Connection);
        Assert.NotEqual(oldId, client.Connection!.Id);
        Assert.False(Assert.IsType<ServerPeer>(server.Find(client.Connection.Id)).WaitingForNextMatch);
        Assert.Equal(1, server.Count);
    }

    [Trait("RequiresGameContent", "true")]
    [Fact]
    public void DisconnectReconnectRestoresParticipatingSlotTeamAndStats()
    {
        using var content = OpenAmhe1();
        var rules = new MatchRules(MatchMode.Battle, "MP1 SANCTORUS", maxPlayers: 1,
            lateJoinPolicy: LateJoinPolicy.Disabled);
        using var simulation = new ServerSimulation(rules);
        using var transport = new NetTransport(0);
        using var socket = new NetTransport(0);
        var endpoint = new IPEndPoint(IPAddress.Loopback, transport.LocalPort);
        var server = new ServerNetwork(transport, rules);
        using var client = new NetClient(socket, endpoint, "RETURN", Hunter.Samus);

        JoinAndReady(server, client);
        StepAndSend(transport, server, simulation, new[] { client }, 1);
        simulation.Scene.Match.Phase = MatchPhase.Playing;
        server.Phase = MatchPhase.Playing;
        server.PhaseRevision = simulation.Scene.Match.PhaseRevision;
        StepAndSend(transport, server, simulation, new[] { client }, 2);

        ServerPeer original = Assert.IsType<ServerPeer>(server.Find(client.Connection!.Id));
        int slot = original.Slot;
        byte team = original.TeamIndex;
        ulong oldConnectionId = original.Connection.Id;
        PlayerMatchStats stats = simulation.Scene.Match.Players[slot];
        stats.Points = 5;
        stats.Kills = 3;
        stats.Deaths = 2;

        Assert.True(client.Disconnect());
        Pump(server, new[] { client }, () => server.Count == 0);
        simulation.Step(server, 3); // Release the body while preserving the reservation's scoreboard.
        Assert.Equal(5, stats.Points);
        Assert.Equal(3, stats.Kills);
        Assert.Equal(2, stats.Deaths);

        client.Reconnect();
        JoinAndReady(server, client);
        ServerPeer returning = Assert.IsType<ServerPeer>(server.Find(client.Connection!.Id));
        Assert.NotEqual(oldConnectionId, returning.Connection.Id);
        Assert.Equal(slot, returning.Slot);
        Assert.Equal(team, returning.TeamIndex);
        Assert.True(returning.ReturningParticipant);

        StepAndSend(transport, server, simulation, new[] { client }, 4);
        Assert.True(returning.HasParticipated);
        Assert.Equal(5, stats.Points);
        Assert.Equal(3, stats.Kills);
        Assert.Equal(2, stats.Deaths);
        Assert.True(simulation.Scene.Players[slot].LoadFlags.TestFlag(LoadFlags.Active));
    }

    [Fact]
    public void ForeignEndpointCannotConsumeReconnectReservation()
    {
        var rules = new MatchRules(MatchMode.Battle, "MP1 SANCTORUS", maxPlayers: 1,
            lateJoinPolicy: LateJoinPolicy.Disabled);
        using var transport = new NetTransport(0);
        using var socket = new NetTransport(0);
        using var foreignSocket = new NetTransport(0);
        var endpoint = new IPEndPoint(IPAddress.Loopback, transport.LocalPort);
        var server = new ServerNetwork(transport, rules);
        using var client = new NetClient(socket, endpoint, "RETURN", Hunter.Samus);
        JoinAndReady(server, client);
        server.Phase = MatchPhase.Playing;
        server.PhaseRevision = 1;
        ServerPeer peer = Assert.IsType<ServerPeer>(server.Find(client.Connection!.Id));
        peer.Connection.StartPlaying();
        peer.HasParticipated = true;
        ulong oldConnectionId = peer.Connection.Id;
        byte oldTeam = peer.TeamIndex;
        Assert.True(client.Disconnect());
        Pump(server, new[] { client }, () => server.Count == 0);

        SendJoin(foreignSocket, endpoint, "RETURN", Hunter.Samus, oldConnectionId);
        long rejected = server.Rejected;
        for (int attempt = 0; attempt < 100 && server.Rejected == rejected; attempt++)
        {
            server.Poll(1);
            if (server.Rejected == rejected) Thread.Sleep(1);
        }
        Assert.True(server.Rejected > rejected);
        Assert.Equal(0, server.Count);

        client.Reconnect();
        JoinAndReady(server, client);
        ServerPeer returning = Assert.IsType<ServerPeer>(server.Find(client.Connection!.Id));
        Assert.Equal(oldTeam, returning.TeamIndex);
        Assert.True(returning.ReturningParticipant);
    }

    [Fact]
    public void ExpiredReconnectReservationIsNotAdmittedAsLateJoin()
    {
        var rules = new MatchRules(MatchMode.Battle, "MP1 SANCTORUS", maxPlayers: 1,
            lateJoinPolicy: LateJoinPolicy.Disabled);
        using var transport = new NetTransport(0);
        using var socket = new NetTransport(0);
        var endpoint = new IPEndPoint(IPAddress.Loopback, transport.LocalPort);
        var server = new ServerNetwork(transport, rules);
        using var client = new NetClient(socket, endpoint, "EXPIRED", Hunter.Samus);
        JoinAndReady(server, client);
        server.Phase = MatchPhase.Playing;
        server.PhaseRevision = 1;
        ServerPeer peer = Assert.IsType<ServerPeer>(server.Find(client.Connection!.Id));
        peer.Connection.StartPlaying();
        peer.HasParticipated = true;
        Assert.True(client.Disconnect());
        Pump(server, new[] { client }, () => server.Count == 0);

        client.Reconnect();
        Pump(server, new[] { client }, () => client.Connection != null || client.Failure != null,
            initialTick: ReconnectPolicy.WorkerReservationTicks + 1);
        Assert.Null(client.Connection);
        Assert.Contains("disabled", client.Failure!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, server.Count);
    }

    [Fact]
    public void RotationClearsReconnectReservationBeforeNewMatchAdmission()
    {
        var rules = new MatchRules(MatchMode.Battle, "MP1 SANCTORUS", maxPlayers: 1,
            lateJoinPolicy: LateJoinPolicy.Disabled);
        using var transport = new NetTransport(0);
        using var socket = new NetTransport(0);
        var endpoint = new IPEndPoint(IPAddress.Loopback, transport.LocalPort);
        var server = new ServerNetwork(transport, rules);
        using var client = new NetClient(socket, endpoint, "ROTATION", Hunter.Samus);
        JoinAndReady(server, client);
        server.Phase = MatchPhase.Playing;
        server.PhaseRevision = 1;
        ServerPeer peer = Assert.IsType<ServerPeer>(server.Find(client.Connection!.Id));
        peer.Connection.StartPlaying();
        peer.HasParticipated = true;
        Assert.True(client.Disconnect());
        Pump(server, new[] { client }, () => server.Count == 0);

        server.ChangeMatch(2, rules, 1);
        server.Phase = MatchPhase.Playing;
        server.PhaseRevision = 1;
        client.Reconnect();
        Pump(server, new[] { client }, () => client.Connection != null || client.Failure != null,
            initialTick: 1);
        Assert.Null(client.Connection);
        Assert.Contains("disabled", client.Failure!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, server.Count);
    }

    [Fact]
    public void NonParticipantDisconnectDoesNotReserveAReconnectSlot()
    {
        var rules = new MatchRules(MatchMode.Battle, "MP1 SANCTORUS", maxPlayers: 1,
            lateJoinPolicy: LateJoinPolicy.Disabled);
        using var transport = new NetTransport(0);
        using var socket = new NetTransport(0);
        var endpoint = new IPEndPoint(IPAddress.Loopback, transport.LocalPort);
        var server = new ServerNetwork(transport, rules);
        using var client = new NetClient(socket, endpoint, "LOADING", Hunter.Samus);
        JoinAndReady(server, client);
        server.Phase = MatchPhase.Playing;
        server.PhaseRevision = 1;
        ServerPeer peer = Assert.IsType<ServerPeer>(server.Find(client.Connection!.Id));
        Assert.False(peer.HasParticipated);
        Assert.True(client.Disconnect());
        Pump(server, new[] { client }, () => server.Count == 0);

        client.Reconnect();
        Pump(server, new[] { client }, () => client.Connection != null || client.Failure != null);
        Assert.Null(client.Connection);
        Assert.Contains("disabled", client.Failure!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, server.Count);
    }

    [Trait("RequiresGameContent", "true")]
    [Fact]
    public void EliminatedSurvivalParticipantReconnectsAsWaitingSpectator()
    {
        using var content = OpenAmhe1();
        var rules = MatchRules.CreateDefault(MatchMode.Survival, "MP1 SANCTORUS", maxPlayers: 1);
        using var simulation = new ServerSimulation(rules);
        using var transport = new NetTransport(0);
        using var socket = new NetTransport(0);
        var endpoint = new IPEndPoint(IPAddress.Loopback, transport.LocalPort);
        var server = new ServerNetwork(transport, rules);
        using var client = new NetClient(socket, endpoint, "SURVIVOR", Hunter.Samus);

        JoinAndReady(server, client);
        StepAndSend(transport, server, simulation, new[] { client }, 1);
        simulation.Scene.Match.Phase = MatchPhase.Playing;
        server.Phase = MatchPhase.Playing;
        server.PhaseRevision = simulation.Scene.Match.PhaseRevision;
        StepAndSend(transport, server, simulation, new[] { client }, 2);
        ServerPeer original = Assert.IsType<ServerPeer>(server.Find(client.Connection!.Id));
        int slot = original.Slot;
        PlayerMatchStats stats = simulation.Scene.Match.Players[slot];
        stats.Points = 2;
        stats.Deaths = rules.LegacyPointGoal + 1;
        simulation.Scene.Match.TeamDeaths[slot] = rules.LegacyPointGoal + 1;

        Assert.True(client.Disconnect());
        Pump(server, new[] { client }, () => server.Count == 0);
        simulation.Step(server, 3);
        client.Reconnect();
        JoinAndReady(server, client);
        ServerPeer returning = Assert.IsType<ServerPeer>(server.Find(client.Connection!.Id));
        StepAndSend(transport, server, simulation, new[] { client }, 4);

        Assert.True(returning.ReturningParticipant);
        Assert.True(returning.WaitingForNextMatch);
        Assert.Equal(NetConnectionState.Playing, returning.Connection.State);
        Assert.False(simulation.Scene.Players[slot].LoadFlags.TestFlag(LoadFlags.Active));
        Assert.Equal(0, simulation.Scene.Players.ActiveCount);
        Assert.Equal(2, stats.Points);
        Assert.Equal(rules.LegacyPointGoal + 1, stats.Deaths);
    }

    [Fact]
    public void AdmissionDuringCountdownRemainsLoadingUntilPlaying()
    {
        var rules = new MatchRules(MatchMode.Battle, "MP1 SANCTORUS", maxPlayers: 2,
            lateJoinPolicy: LateJoinPolicy.SpectateUntilNextMatch);
        using var transport = new NetTransport(0);
        using var socket = new NetTransport(0);
        var endpoint = new IPEndPoint(IPAddress.Loopback, transport.LocalPort);
        var server = new ServerNetwork(transport, rules)
        {
            Phase = MatchPhase.Countdown,
            PhaseRevision = 2
        };
        using var client = new NetClient(socket, endpoint, "COUNTDOWN", Hunter.Samus);
        Pump(server, new[] { client }, () => client.Connection != null);
        Assert.Equal(NetConnectionState.Loading, client.State);
        Assert.True(client.Ready(client.Accepted.MatchId));
        Pump(server, new[] { client }, () => server.Peers[0]?.Connection.State == NetConnectionState.Ready);
        Assert.Equal(NetConnectionState.Ready, server.Peers[0]!.Connection.State);
        Assert.False(server.Peers[0]!.WaitingForNextMatch);
    }

    [Trait("RequiresGameContent", "true")]
    [Fact]
    public void Amhe1LoadingDuringCountdownReadyAfterPlayingBecomesWaitingSpectator()
    {
        using var content = OpenAmhe1();
        var rules = new MatchRules(MatchMode.Battle, "MP1 SANCTORUS", maxPlayers: 3,
            lateJoinPolicy: LateJoinPolicy.SpectateUntilNextMatch);
        using var simulation = new ServerSimulation(rules);
        using var transport = new NetTransport(0);
        using var firstSocket = new NetTransport(0);
        using var secondSocket = new NetTransport(0);
        using var lateSocket = new NetTransport(0);
        var endpoint = new IPEndPoint(IPAddress.Loopback, transport.LocalPort);
        var server = new ServerNetwork(transport, rules);
        using var first = new NetClient(firstSocket, endpoint, "FIRST", Hunter.Samus);
        using var second = new NetClient(secondSocket, endpoint, "SECOND", Hunter.Kanden);
        JoinAndReady(server, first, second);
        StepAndSend(transport, server, simulation, new[] { first, second }, 1);
        Assert.Equal(MatchPhase.Countdown, simulation.Scene.Match.Phase);
        Assert.Equal(2, simulation.Scene.Players.ActiveCount);

        using var late = new NetClient(lateSocket, endpoint, "LOADING", Hunter.Spire);
        Pump(server, new[] { first, second, late }, () => late.Connection != null);
        Assert.Equal(NetConnectionState.Loading, late.State);
        ServerPeer latePeer = Assert.IsType<ServerPeer>(server.Find(late.Connection!.Id));
        Assert.False(latePeer.WaitingForNextMatch);

        simulation.Scene.Match.Phase = MatchPhase.Playing;
        server.Phase = MatchPhase.Playing;
        server.PhaseRevision = simulation.Scene.Match.PhaseRevision;
        StepAndSend(transport, server, simulation, new[] { first, second, late }, 2);
        Assert.Equal(NetConnectionState.Loading, latePeer.Connection.State);
        Assert.Equal(2, simulation.Scene.Players.ActiveCount);

        Assert.True(late.Ready(late.Accepted.MatchId));
        Pump(server, new[] { first, second, late }, () =>
            server.Find(late.Connection!.Id)?.Connection.State == NetConnectionState.Ready);
        StepAndSend(transport, server, simulation, new[] { first, second, late }, 3);
        Assert.Equal(NetConnectionState.Playing, latePeer.Connection.State);
        Assert.True(latePeer.WaitingForNextMatch);
        Assert.False(simulation.Scene.Players[latePeer.Slot].LoadFlags.TestFlag(LoadFlags.Active));
        Assert.Equal(2, simulation.Scene.Players.ActiveCount);
    }

    [Trait("RequiresGameContent", "true")]
    [Fact]
    public void Amhe1SimulationKeepsNextJoinOutOfCurrentMatchAndActivatesAfterRotation()
    {
        using var content = OpenAmhe1();
        var rules = new MatchRules(MatchMode.Battle, "MP1 SANCTORUS", maxPlayers: 3,
            lateJoinPolicy: LateJoinPolicy.SpectateUntilNextMatch);
        using var simulation = new ServerSimulation(rules);
        using var transport = new NetTransport(0);
        using var firstSocket = new NetTransport(0);
        using var secondSocket = new NetTransport(0);
        using var lateSocket = new NetTransport(0);
        var endpoint = new IPEndPoint(IPAddress.Loopback, transport.LocalPort);
        var server = new ServerNetwork(transport, rules)
        {
            Phase = MatchPhase.WaitingForPlayers
        };
        using var first = new NetClient(firstSocket, endpoint, "FIRST", Hunter.Samus);
        using var second = new NetClient(secondSocket, endpoint, "SECOND", Hunter.Kanden);
        JoinAndReady(server, first, second);
        StepAndSend(transport, server, simulation, new[] { first, second }, 1);
        Assert.Equal(2, simulation.Scene.Players.ActiveCount);
        simulation.Scene.Match.Phase = MatchPhase.Playing;
        server.Phase = MatchPhase.Playing;
        server.PhaseRevision = simulation.Scene.Match.PhaseRevision;
        StepAndSend(transport, server, simulation, new[] { first, second }, 2);
        Assert.Equal(2, simulation.Scene.Players.ActiveCount);
        Assert.All(new[] { first, second }, client =>
            Assert.Equal(NetConnectionState.Playing, client.State));

        using var late = new NetClient(lateSocket, endpoint, "LATE", Hunter.Spire);
        Pump(server, new[] { first, second, late }, () => late.Connection != null);
        Assert.True(late.Ready(late.Accepted.MatchId));
        Pump(server, new[] { first, second, late }, () =>
            server.Find(late.Connection!.Id)?.Connection.State == NetConnectionState.Ready);
        ServerPeer latePeer = Assert.IsType<ServerPeer>(server.Find(late.Connection!.Id));
        Assert.True(latePeer.WaitingForNextMatch);
        StepAndSend(transport, server, simulation, new[] { first, second, late }, 2);
        Assert.NotEmpty(simulation.States.ToArray());
        Pump(server, new[] { first, second, late }, () => late.HasSnapshot);
        SnapshotPlayer waiting = Assert.Single(late.SnapshotPlayers.ToArray(),
            state => state.Slot == latePeer.Slot);
        Assert.Equal(latePeer.Slot, waiting.Slot);
        Assert.Equal(latePeer.Connection.Id, waiting.ConnectionId);
        Assert.Equal(SnapshotPlayerFlags.Spectating | SnapshotPlayerFlags.WaitingForMatch, waiting.Flags);
        Assert.False((waiting.Flags & (SnapshotPlayerFlags.Active | SnapshotPlayerFlags.Spawned)) != 0);
        Assert.Equal(2, simulation.Scene.Players.ActiveCount);
        Assert.False(simulation.Scene.Players[latePeer.Slot].LoadFlags.TestFlag(LoadFlags.Active));
        Assert.Equal(0, simulation.Scene.Match.Players[latePeer.Slot].Points);
        Assert.Equal(0, simulation.Scene.Match.Players[latePeer.Slot].Kills);

        var command = new InputCommand(1, 2, 2, InputButtons.Forward,
            InputButtons.Forward, -Vector3.UnitZ, InputCommand.NoWeapon);
        Assert.True(late.SendInputs(new[] { command }, server.PhaseRevision));
        Thread.Sleep(2);
        server.Poll(3);
        StepAndSend(transport, server, simulation, new[] { first, second, late }, 3);
        StepAndSend(transport, server, simulation, new[] { first, second, late }, 4);
        StepAndSend(transport, server, simulation, new[] { first, second, late }, 5);
        Assert.True(latePeer.Inputs.HasProcessed);
        Assert.False(simulation.Scene.Players[latePeer.Slot].LoadFlags.TestFlag(LoadFlags.Active));
        Assert.Equal(2, simulation.Scene.Players.ActiveCount);

        simulation.Scene.Match.Phase = MatchPhase.Intermission;
        server.Phase = MatchPhase.Intermission;
        server.ChangeMatch(2, rules, 4);
        using var nextSimulation = new ServerSimulation(rules);
        server.Phase = MatchPhase.WaitingForPlayers;
        server.PhaseRevision = 1;
        Pump(server, new[] { first, second, late }, () =>
            first.State == NetConnectionState.Loading && second.State == NetConnectionState.Loading
                && late.State == NetConnectionState.Loading);
        foreach (NetClient client in new[] { first, second, late })
        {
            Assert.True(client.Ready(2));
        }
        Pump(server, new[] { first, second, late }, () => AllReady(server));
        StepAndSend(transport, server, nextSimulation, new[] { first, second, late }, 5);
        Assert.Equal(3, nextSimulation.Scene.Players.ActiveCount);
        Assert.Equal(2, simulation.Scene.Players.ActiveCount);
        Assert.False(simulation.Scene.Players[latePeer.Slot].LoadFlags.TestFlag(LoadFlags.Active));
        nextSimulation.Scene.Match.Phase = MatchPhase.Playing;
        server.Phase = MatchPhase.Playing;
        server.PhaseRevision = nextSimulation.Scene.Match.PhaseRevision;
        StepAndSend(transport, server, nextSimulation, new[] { first, second, late }, 6);
        Assert.False(latePeer.WaitingForNextMatch);
        Assert.True(nextSimulation.Scene.Players[latePeer.Slot].LoadFlags.TestFlag(LoadFlags.Active));
        Assert.Equal(3, nextSimulation.Scene.Players.ActiveCount);
        Assert.Contains(nextSimulation.States.ToArray(), state =>
            state.Slot == latePeer.Slot && (state.Flags & SnapshotPlayerFlags.Active) != 0);
    }

    private static void JoinAndReady(ServerNetwork server, params NetClient[] clients)
    {
        Pump(server, clients, () => Array.TrueForAll(clients, client => client.Connection != null));
        foreach (NetClient client in clients) { Assert.True(client.Ready(client.Accepted.MatchId)); }
        Pump(server, clients, () => Array.TrueForAll(clients,
            client => server.Find(client.Connection!.Id)?.Connection.State == NetConnectionState.Ready));
    }

    private static void StepAndSend(INetTransport transport, ServerNetwork server, ServerSimulation simulation,
        NetClient[] clients, uint tick)
    {
        server.Poll(tick);
        foreach (NetClient client in clients) { client.Poll(); }
        simulation.Step(server, tick);
        Span<byte> packet = stackalloc byte[NetConfig.MaxPacketSize];
        var snapshot = new SnapshotPacket(tick, tick, server.MatchId,
            0, false, simulation.Scene.Random.Rng1, simulation.Scene.Random.Rng2);
        int length = snapshot.Write(packet, simulation.States);
        Span<SnapshotPlayer> decoded = stackalloc SnapshotPlayer[8];
        Assert.True(SnapshotPacket.TryRead(packet[..length], decoded, out SnapshotPacket parsed, out int count));
        Assert.Equal(server.MatchId, parsed.MatchId);
        Assert.Equal(simulation.States.Length, count);
        foreach (ServerPeer? peer in server.Peers)
        {
            if (peer is { Connection.State: NetConnectionState.Playing or NetConnectionState.Ready })
            {
                peer.Connection.Send(transport, NetMessageType.Snapshot, packet[..length]);
            }
        }
        // UDP delivery may complete after SendDatagram returns. Wait for the
        // snapshot under test before asserting the client's admission state.
        foreach (NetClient client in clients)
        {
            if (client.Connection == null || server.Find(client.Connection.Id)?.Connection.State
                is not (NetConnectionState.Playing or NetConnectionState.Ready)) continue;
            var watch = Stopwatch.StartNew();
            do
            {
                client.Poll();
                if (client.HasSnapshot && (client.Snapshot.Sequence == tick
                    || Sequence32.IsNewer(client.Snapshot.Sequence, tick))) break;
                Thread.Sleep(1);
            } while (watch.Elapsed < TimeSpan.FromSeconds(5));
            Assert.True(client.HasSnapshot && (client.Snapshot.Sequence == tick
                || Sequence32.IsNewer(client.Snapshot.Sequence, tick)), "Loopback snapshot was not received.");
        }
    }

    private static void SendJoin(NetTransport transport, IPEndPoint endpoint, string name,
        Hunter hunter, ulong previousConnectionId)
    {
        Span<byte> datagram = stackalloc byte[NetHeader.Size + JoinPacket.Size];
        new NetHeader(NetMessageType.Join, NetHeaderFlags.Unsequenced, 0, 0, 0, 0).Write(datagram);
        new JoinPacket(NetHeader.Version, NetConnection.NewIdentity(), hunter, name,
            previousConnectionId).Write(datagram[NetHeader.Size..]);
        transport.SendDatagram(endpoint, datagram);
    }

    private static void Pump(ServerNetwork server, NetClient[] clients, Func<bool> done,
        uint initialTick = 0)
    {
        var watch = Stopwatch.StartNew();
        uint tick = initialTick;
        while (watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            server.Poll(tick++);
            foreach (NetClient client in clients) { client.Poll(); }
            if (done()) return;
            Thread.Sleep(1);
        }
        Assert.Fail("Late-join loopback condition timed out.");
    }

    private static bool AllReady(ServerNetwork server)
    {
        foreach (ServerPeer? peer in server.Peers)
        {
            if (peer != null && peer.Connection.State != NetConnectionState.Ready) return false;
        }
        return server.Count > 0;
    }

    private static IDisposable OpenAmhe1()
    {
        string data = FindAmhe1();
        IDisposable saved = ServerContent.PreserveContext("AMHE1");
        try
        {
            ServerContent.Open(data, "AMHE1");
            return saved;
        }
        catch
        {
            saved.Dispose();
            throw;
        }
    }

    private static string FindAmhe1()
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
                    && Directory.Exists(Path.Combine(candidate, "levels"))) return candidate;
                directory = directory.Parent;
            }
        }
        throw new DirectoryNotFoundException("AMHE1 extracted content was not found.");
    }
}
