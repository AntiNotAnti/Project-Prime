using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class NetworkActionIntegrationTests
{
    [Trait("RequiresGameContent", "true")]
    [Fact]
    public void Amhe1UdpEdgesReachAuthoritativeSimulationOnceUnderLossAndReordering()
    {
        using var content = OpenAmhe1();
        var rules = new MatchRules(MatchMode.Battle, "MP1 SANCTORUS", maxPlayers: 1);
        using var simulation = new ServerSimulation(rules);
        using var serverTransport = new NetTransport(0);
        using var link = new ImpairedLink(serverTransport.LocalPort, seed: 0x51A7);
        using var clientTransport = new NetTransport(0);
        using var client = new NetClient(clientTransport, link.Endpoint, "UDP-ACTIONS", Hunter.Samus);
        var server = new ServerNetwork(serverTransport, rules);

        Pump(server, client, () => client.Connection != null, 12);
        Assert.True(client.Ready(client.Accepted.MatchId));
        Pump(server, client, () => server.Peers[0]?.Connection.State == NetConnectionState.Ready, 12);

        StepAndSend(serverTransport, server, simulation, client, 1);
        simulation.Scene.Match.Phase = MatchPhase.Playing;
        server.Phase = MatchPhase.Playing;
        server.PhaseRevision = simulation.Scene.Match.PhaseRevision;

        StepAndSend(serverTransport, server, simulation, client, 2);
        ServerPeer peer = Assert.IsType<ServerPeer>(server.Find(client.Connection!.Id));
        Assert.Equal(NetConnectionState.Playing, peer.Connection.State);

        var history = new List<InputCommand>();
        var processed = new Dictionary<uint, InputCommand>();
        uint sequence = 0;
        uint currentTick = 3;

        // The stream's startup window is part of the live path. Fill it with
        // neutral samples before checking gameplay edges.
        for (int i = 0; i < 4; i++)
        {
            SendAndTickAt(serverTransport, server, simulation, client, history, ref sequence,
                ref currentTick, InputButtons.None, InputButtons.None);
            RecordProcessed(peer, simulation, processed);
        }

        InputButtons[] edges =
        {
            InputButtons.Jump,
            InputButtons.Morph,
            InputButtons.AltAttack,
            InputButtons.NextWeapon,
            InputButtons.PreviousWeapon,
            InputButtons.Boost,
            InputButtons.Spectate
        };
        var expected = new Dictionary<InputButtons, uint>();
        bool spectatingObserved = false;
        foreach (InputButtons edge in edges)
        {
            uint edgeSequence = sequence;
            expected.Add(edge, edgeSequence);
            SendAndTickAt(serverTransport, server, simulation, client, history, ref sequence,
                ref currentTick, edge, edge);
            RecordProcessed(peer, simulation, processed);
            spectatingObserved |= simulation.Scene.Players[peer.Slot].Flags2.TestFlag(PlayerFlags2.Spectating);
            SendAndTickAt(serverTransport, server, simulation, client, history, ref sequence,
                ref currentTick, InputButtons.None, InputButtons.None);
            RecordProcessed(peer, simulation, processed);
            spectatingObserved |= simulation.Scene.Players[peer.Slot].Flags2.TestFlag(PlayerFlags2.Spectating);
        }

        // Let the delayed/reordered bundles arrive and drain the exact command
        // sequence. Repeated history is bounded to InputBundle.Capacity.
        for (int i = 0; i < 36; i++)
        {
            SendAndTickAt(serverTransport, server, simulation, client, history, ref sequence,
                ref currentTick, InputButtons.None, InputButtons.None);
            RecordProcessed(peer, simulation, processed);
            spectatingObserved |= simulation.Scene.Players[peer.Slot].Flags2.TestFlag(PlayerFlags2.Spectating);
        }

        foreach ((InputButtons edge, uint edgeSequence) in expected)
        {
            Assert.True(processed.TryGetValue(edgeSequence, out InputCommand command),
                $"The UDP edge {edge} sequence {edgeSequence} was not applied.");
            Assert.Equal(edge, command.Buttons & edge);
            Assert.Equal(edge, command.Pressed & edge);
            int acceptedEdges = 0;
            foreach (InputCommand applied in processed.Values)
            {
                if ((applied.Pressed & edge) != InputButtons.None) acceptedEdges++;
            }
            Assert.Equal(1, acceptedEdges);
        }
        Assert.True(peer.Inputs.Duplicates > 0,
            "The impaired UDP path did not exercise redundant command history.");
        Assert.Equal(0, peer.Inputs.SkippedCommands);

        // Spectate is an observable server-side action, so retain one behavior
        // assertion beyond the command journal itself.
        Assert.True(spectatingObserved);
    }

    [Trait("RequiresGameContent", "true")]
    [Fact]
    public void RepeatedUdpDesiredWeaponWaitsThroughGunTransitionAndTracksPreviousWeapon()
    {
        using var content = OpenAmhe1();
        var rules = new MatchRules(MatchMode.Battle, "MP1 SANCTORUS", maxPlayers: 1);
        using var simulation = new ServerSimulation(rules);
        using var serverTransport = new NetTransport(0);
        using var link = new ImpairedLink(serverTransport.LocalPort, seed: 0xC0DE);
        using var clientTransport = new NetTransport(0);
        using var client = new NetClient(clientTransport, link.Endpoint, "UDP-WEAPONS", Hunter.Samus);
        var server = new ServerNetwork(serverTransport, rules);

        Pump(server, client, () => client.Connection != null, 12);
        Assert.True(client.Ready(client.Accepted.MatchId));
        Pump(server, client, () => server.Peers[0]?.Connection.State == NetConnectionState.Ready, 12);

        StepAndSend(serverTransport, server, simulation, client, 1);
        simulation.Scene.Match.Phase = MatchPhase.Playing;
        server.Phase = MatchPhase.Playing;
        server.PhaseRevision = simulation.Scene.Match.PhaseRevision;
        StepAndSend(serverTransport, server, simulation, client, 2);

        ServerPeer peer = Assert.IsType<ServerPeer>(server.Find(client.Connection!.Id));
        PlayerEntity player = simulation.Scene.Players[peer.Slot];
        var history = new List<InputCommand>();
        uint sequence = 0;
        uint tick = 3;
        for (int i = 0; i < 4; i++)
        {
            SendAndTickAt(serverTransport, server, simulation, client, history, ref sequence,
                ref tick, InputButtons.None, InputButtons.None);
        }

        Assert.Equal(BeamType.PowerBeam, player.CurrentWeapon);
        Assert.Equal(BeamType.PowerBeam, player.PreviousWeapon);

        // The first desired weapon is sent over the real UDP path together
        // with the cycle edge that selected it. The target must be the single
        // authoritative action; applying the edge too would equip Missile and
        // immediately cycle back to Power Beam. Subsequent redundant history
        // is intentionally neutral so the server must apply the sequence once
        // and keep normal availability/transition checks.
        SendAndTickAt(serverTransport, server, simulation, client, history, ref sequence,
            ref tick, InputButtons.NextWeapon, InputButtons.NextWeapon,
            (byte)BeamType.Missile);
        for (int i = 0; i < 18 && player.CurrentWeapon != BeamType.Missile; i++)
        {
            SendAndTickAt(serverTransport, server, simulation, client, history, ref sequence,
                ref tick, InputButtons.None, InputButtons.None);
        }
        Assert.Equal(BeamType.Missile, player.CurrentWeapon);
        Assert.Equal(BeamType.PowerBeam, player.PreviousWeapon);

        // Switch back to the non-missile weapon first. Missile's idle path
        // closes the launcher directly, so the ordinary weapon must be active
        // before the test can observe the UpDown transition.
        SendAndTickAt(serverTransport, server, simulation, client, history, ref sequence,
            ref tick, InputButtons.PreviousWeapon, InputButtons.PreviousWeapon,
            (byte)BeamType.PowerBeam);
        for (int i = 0; i < 18 && player.CurrentWeapon != BeamType.PowerBeam; i++)
        {
            SendAndTickAt(serverTransport, server, simulation, client, history, ref sequence,
                ref tick, InputButtons.None, InputButtons.None);
        }
        Assert.Equal(BeamType.PowerBeam, player.CurrentWeapon);
        Assert.Equal(BeamType.Missile, player.PreviousWeapon);

        // Let normal idle handling enter UpDown, then keep requesting the
        // newest weapon through that transition. The request must not change
        // CurrentWeapon while blocked, but must be accepted once legal.
        player._timeSinceInput = (ushort)(player.Values.GunIdleTime * SimTicks.TicksPer30HzFrame);
        SendAndTickAt(serverTransport, server, simulation, client, history, ref sequence,
            ref tick, InputButtons.None, InputButtons.None);
        Assert.Equal(GunAnimation.UpDown, player.GunAnimation);
        Assert.False(player._gunModel.AnimInfo.Flags[0].TestFlag(AnimFlags.Ended));
        var blockedCommands = new Dictionary<uint, InputCommand>();
        uint firstBlockedSequence = sequence;
        bool requestedDuringUpDown = false;
        for (int i = 0; i < 60 && !requestedDuringUpDown; i++)
        {
            Assert.Equal(GunAnimation.UpDown, player.GunAnimation);
            Assert.False(player._gunModel.AnimInfo.Flags[0].TestFlag(AnimFlags.Ended));
            SendAndTickAt(serverTransport, server, simulation, client, history, ref sequence,
                ref tick, InputButtons.None, InputButtons.None, (byte)BeamType.Missile);
            RecordProcessed(peer, simulation, blockedCommands);
            requestedDuringUpDown = blockedCommands.Any(pair => pair.Key >= firstBlockedSequence
                && pair.Value.DesiredWeapon == (byte)BeamType.Missile);
            Assert.Equal(GunAnimation.UpDown, player.GunAnimation);
            Assert.False(player._gunModel.AnimInfo.Flags[0].TestFlag(AnimFlags.Ended));
            Assert.Equal(BeamType.PowerBeam, player.CurrentWeapon);
            Assert.Equal(BeamType.Missile, player.PreviousWeapon);
        }
        Assert.True(requestedDuringUpDown,
            "No desired Missile command was processed while the unfinished UpDown animation blocked switching.");

        // Only after the server command journal proves the blocked request:
        // headless fixtures do not advance the native gun model's frame clock,
        // so complete this test-only animation and repeat the desired UDP input.
        player._gunModel.AnimInfo.Flags[0] |= AnimFlags.Ended;
        player._timeSinceInput = 1;

        for (int i = 0; i < 60 && player.CurrentWeapon != BeamType.Missile; i++)
        {
            SendAndTickAt(serverTransport, server, simulation, client, history, ref sequence,
                ref tick, InputButtons.None, InputButtons.None, (byte)BeamType.Missile);
        }

        Assert.True(player.CurrentWeapon == BeamType.Missile,
            $"UDP desired weapon did not survive transition: current={player.CurrentWeapon}, previous={player.PreviousWeapon}, animation={player.GunAnimation}, processed={peer.Inputs.LastProcessed}, late={peer.Inputs.LateCommands}, skipped={peer.Inputs.SkippedCommands}, starved={peer.Inputs.StarvedTicks}, tick={tick}");
        Assert.Equal(BeamType.PowerBeam, player.PreviousWeapon);
    }

    [Trait("RequiresGameContent", "true")]
    [Fact]
    public void SamusAffinityPickupAwardsMissilesAndAutoEquipsThem()
    {
        using var content = OpenAmhe1();
        var rules = new MatchRules(MatchMode.Battle, "MP1 SANCTORUS", maxPlayers: 1);
        using var simulation = new ServerSimulation(rules);
        using var serverTransport = new NetTransport(0);
        using var clientTransport = new NetTransport(0);
        using var client = new NetClient(clientTransport,
            new IPEndPoint(IPAddress.Loopback, serverTransport.LocalPort),
            "UDP-AFFINITY-PICKUP", Hunter.Samus);
        var server = new ServerNetwork(serverTransport, rules);

        Pump(server, client, () => client.Connection != null, 12);
        Assert.True(client.Ready(client.Accepted.MatchId));
        Pump(server, client, () => server.Peers[0]?.Connection.State == NetConnectionState.Ready, 12);

        StepAndSend(serverTransport, server, simulation, client, 1);
        simulation.Scene.Match.Phase = MatchPhase.Playing;
        server.Phase = MatchPhase.Playing;
        server.PhaseRevision = simulation.Scene.Match.PhaseRevision;
        StepAndSend(serverTransport, server, simulation, client, 2);

        ServerPeer peer = Assert.IsType<ServerPeer>(server.Find(client.Connection!.Id));
        PlayerEntity player = simulation.Scene.Players[peer.Slot];
        player.ModSetAmmo(0, 0);
        Assert.Equal(BeamType.PowerBeam, player.CurrentWeapon);

        ItemInstanceEntity? pickup = ItemSpawnEntity.SpawnItem(ItemType.AffinityWeapon,
            player.Position, player.NodeRef, SimTicks.From30HzFrames(60), simulation.Scene);
        Assert.NotNull(pickup);

        var history = new List<InputCommand>();
        uint sequence = 0;
        uint tick = 3;
        SendAndTickAt(serverTransport, server, simulation, client, history,
            ref sequence, ref tick, InputButtons.None, InputButtons.None,
            (byte)BeamType.PowerBeam);

        Assert.Equal(BeamType.Missile, player.CurrentWeapon);
        Assert.Equal(50, player.ModAmmo.Missiles);
    }

    [Trait("RequiresGameContent", "true")]
    [Fact]
    public void WeaponPickupRejectsStaleDesiredWeaponUntilClientObservesPickup()
    {
        using var content = OpenAmhe1();
        var rules = new MatchRules(MatchMode.Battle, "MP1 SANCTORUS", maxPlayers: 1);
        using var simulation = new ServerSimulation(rules);
        using var serverTransport = new NetTransport(0);
        using var clientTransport = new NetTransport(0);
        using var client = new NetClient(clientTransport,
            new IPEndPoint(IPAddress.Loopback, serverTransport.LocalPort),
            "UDP-PICKUP-WEAPON", Hunter.Samus);
        var server = new ServerNetwork(serverTransport, rules);

        Pump(server, client, () => client.Connection != null, 12);
        Assert.True(client.Ready(client.Accepted.MatchId));
        Pump(server, client, () => server.Peers[0]?.Connection.State == NetConnectionState.Ready, 12);

        StepAndSend(serverTransport, server, simulation, client, 1);
        simulation.Scene.Match.Phase = MatchPhase.Playing;
        server.Phase = MatchPhase.Playing;
        server.PhaseRevision = simulation.Scene.Match.PhaseRevision;
        StepAndSend(serverTransport, server, simulation, client, 2);

        ServerPeer peer = Assert.IsType<ServerPeer>(server.Find(client.Connection!.Id));
        PlayerEntity player = simulation.Scene.Players[peer.Slot];
        var history = new List<InputCommand>();
        uint sequence = 0;
        uint tick = 3;
        for (int i = 0; i < 4; i++)
        {
            SendAndTickAt(serverTransport, server, simulation, client, history,
                ref sequence, ref tick, InputButtons.None, InputButtons.None);
        }
        Assert.Equal(BeamType.PowerBeam, player.CurrentWeapon);

        ItemInstanceEntity? pickup = ItemSpawnEntity.SpawnItem(ItemType.OmegaCannon,
            player.Position, player.NodeRef, SimTicks.From30HzFrames(60), simulation.Scene);
        Assert.NotNull(pickup);
        uint pickupTick = tick;
        SendAndTickAt(serverTransport, server, simulation, client, history,
            ref sequence, ref tick, InputButtons.None, InputButtons.None,
            (byte)BeamType.PowerBeam);
        Assert.Equal(BeamType.OmegaCannon, player.CurrentWeapon);

        // This packet was authored from a snapshot before the authority
        // auto-equipped the pickup. It must not switch the server back.
        uint staleSequence = sequence++;
        player.ApplyNetworkInput(new InputCommand(staleSequence, staleSequence, pickupTick - 1,
            InputButtons.None, InputButtons.None, -Vector3.UnitZ,
            (byte)BeamType.PowerBeam, player.ServerCombatIdentity.Life));
        Assert.Equal(BeamType.OmegaCannon, player.CurrentWeapon);

        // Once the client has observed the pickup tick, choosing the old
        // weapon is a fresh player intent and remains legal.
        uint currentSequence = sequence++;
        player.ApplyNetworkInput(new InputCommand(currentSequence, currentSequence, pickupTick,
            InputButtons.None, InputButtons.None, -Vector3.UnitZ,
            (byte)BeamType.PowerBeam, player.ServerCombatIdentity.Life));
        Assert.Equal(BeamType.PowerBeam, player.CurrentWeapon);
    }

    [Trait("RequiresGameContent", "true")]
    [Fact]
    public void RespawnInputEpochRejectsDelayedOldLifeAndAllowsNewLifeShot()
    {
        using var content = OpenAmhe1();
        var rules = new MatchRules(MatchMode.Battle, "MP1 SANCTORUS", maxPlayers: 1);
        using var simulation = new ServerSimulation(rules);
        using var serverTransport = new NetTransport(0);
        using var clientTransport = new NetTransport(0);
        using var client = new NetClient(clientTransport,
            new IPEndPoint(IPAddress.Loopback, serverTransport.LocalPort),
            "UDP-LIFE-EPOCH", Hunter.Samus);
        var server = new ServerNetwork(serverTransport, rules);

        JoinAndReady(server, client);
        StepAndSend(serverTransport, server, simulation, client, 1);
        simulation.Scene.Match.Phase = MatchPhase.Playing;
        server.Phase = MatchPhase.Playing;
        server.PhaseRevision = simulation.Scene.Match.PhaseRevision;
        StepAndSend(serverTransport, server, simulation, client, 2);

        ServerPeer peer = Assert.IsType<ServerPeer>(server.Find(client.Connection!.Id));
        PlayerEntity player = simulation.Scene.Players[peer.Slot];
        uint oldLife = player.ServerCombatIdentity.Life;
        Assert.NotEqual(0u, oldLife);

        uint tick = 3;
        // Feed one complete, contiguous startup bundle directly into the real
        // peer stream. This keeps the fixture deterministic while still
        // traversing ServerInputStream.Receive -> Take -> ApplyNetworkInput.
        var startup = new InputCommand[InputBundle.Capacity];
        for (int i = 0; i < startup.Length; i++)
        {
            startup[i] = EpochCommand((uint)i, tick + (uint)i, oldLife);
        }
        peer.Inputs.Receive(startup, tick);
        for (int i = 0; i < InputBundle.Capacity + peer.Inputs.InputPlayoutTicks; i++)
        {
            simulation.Step(server, tick++);
        }
        Assert.Equal(7u, peer.Inputs.LastProcessed);

        uint respawnSequence = unchecked(peer.Inputs.LastProcessed + 1);
        player.TakeDamage((uint)player.Health,
            DamageFlags.Death | DamageFlags.NoDmgInvuln, null, null);
        player.RespawnTimer = 1;
        int shotsBeforeRespawn = ActiveShots(simulation.Scene, player);
        peer.Inputs.Receive(new[] { EpochCommand(respawnSequence, tick, oldLife,
            InputButtons.Shoot, InputButtons.Shoot) }, tick);
        simulation.Step(server, tick++);

        uint newLife = player.ServerCombatIdentity.Life;
        Assert.Equal(unchecked(oldLife + 1), newLife);
        Assert.Equal(shotsBeforeRespawn, ActiveShots(simulation.Scene, player));
        Assert.False(player.Controls.Shoot.IsDown);
        Assert.False(player.Controls.Shoot.IsPressed);
        Assert.Equal(InputButtons.None,
            simulation.Combat.GetCommand(peer.Slot).Buttons & (InputButtons.Shoot | InputButtons.AltAttack));

        // These commands are validly framed and have previously unseen (or
        // repeated/reordered) sequences, but belong to the dead life. The
        // matrix also covers a held continuous-style shot, a release, Missile,
        // Imperialist, and an applicable alt-form edge without depending on
        // weapon-pickup or animation setup for the negative assertions.
        uint staleSequence = unchecked(respawnSequence + 1);
        InputCommand[] staleCommands =
        {
            EpochCommand(staleSequence, tick, oldLife,
                InputButtons.Shoot, InputButtons.Shoot, (byte)BeamType.Missile),
            EpochCommand(staleSequence, tick, oldLife,
                InputButtons.Shoot, InputButtons.Shoot, (byte)BeamType.Missile),
            EpochCommand(unchecked(staleSequence + 2), tick, oldLife,
                InputButtons.Shoot, InputButtons.None, (byte)BeamType.Imperialist),
            EpochCommand(unchecked(staleSequence + 1), tick, oldLife),
            EpochCommand(unchecked(staleSequence + 3), tick, oldLife,
                InputButtons.AltAttack, InputButtons.AltAttack)
        };
        int shotsBeforeStale = ActiveShots(simulation.Scene, player);
        foreach (InputCommand stale in staleCommands)
        {
            peer.Inputs.Receive(new[] { stale }, tick);
            simulation.Step(server, tick++);
            Assert.Equal(shotsBeforeStale, ActiveShots(simulation.Scene, player));
            InputCommand journal = simulation.Combat.GetCommand(peer.Slot);
            Assert.Equal(newLife, journal.InputEpoch);
            Assert.Equal(InputButtons.None,
                journal.Buttons & (InputButtons.Shoot | InputButtons.AltAttack));
            Assert.Equal(InputButtons.None,
                journal.Pressed & (InputButtons.Shoot | InputButtons.AltAttack));
            Assert.Equal(InputCommand.NoWeapon, journal.DesiredWeapon);
        }
        Assert.True(peer.Inputs.StaleEpochCommands >= staleCommands.Length);

        // The stream never advances _next for rejected old-life commands, so
        // this exact current-life sequence is ready without a timing race.
        uint newSequence = respawnSequence + 1;
        peer.Inputs.Receive(new[] { EpochCommand(newSequence, tick, newLife,
            InputButtons.Shoot, InputButtons.Shoot) }, tick);
        simulation.Step(server, tick++);
        for (int i = 0; i < 6 && ActiveShots(simulation.Scene, player) == shotsBeforeStale; i++)
        {
            uint sequence = unchecked(newSequence + 1u + (uint)i);
            peer.Inputs.Receive(new[] { EpochCommand(sequence, tick, newLife,
                InputButtons.Shoot) }, tick);
            simulation.Step(server, tick++);
        }
        Assert.True(ActiveShots(simulation.Scene, player) > shotsBeforeStale,
            "A current-life shot was not spawned after stale-life input was fenced.");
    }

    [Trait("RequiresGameContent", "true")]
    [Fact]
    public void DisconnectingFlagPublishesTheCurrentSimulationTick()
    {
        using var content = OpenAmhe1();
        var rules = new MatchRules(MatchMode.Capture, "MP1 SANCTORUS", maxPlayers: 1);
        using var simulation = new ServerSimulation(rules);
        using var transport = new NetTransport(0);
        using var socket = new NetTransport(0);
        var endpoint = new IPEndPoint(IPAddress.Loopback, transport.LocalPort);
        var server = new ServerNetwork(transport, rules);
        using var client = new NetClient(socket, endpoint, "FLAG-CARRIER", Hunter.Samus);

        JoinAndReady(server, client);
        StepAndSend(transport, server, simulation, client, 1);
        simulation.Scene.Match.Phase = MatchPhase.Playing;
        server.Phase = MatchPhase.Playing;
        server.PhaseRevision = simulation.Scene.Match.PhaseRevision;
        StepAndSend(transport, server, simulation, client, 2);

        ServerPeer peer = Assert.IsType<ServerPeer>(server.Find(client.Connection!.Id));
        PlayerEntity carrier = simulation.Scene.Players[peer.Slot];
        Assert.True(float.IsFinite(carrier.Position.X) && float.IsFinite(carrier.Position.Z), carrier.Position.ToString());
        // Some AMHE1 spawn records omit a horizontal facing. Give this
        // fixture the canonical forward vector before carrying an objective;
        // the server's objective ownership and release path stay real.
        carrier.Reposition(carrier.Position, -Vector3.UnitZ, carrier.NodeRef);
        carrier._field70 = 0;
        carrier._field74 = -1;
        OctolithFlagEntity flag = AcquireEnemyFlag(simulation.Scene, carrier);
        Assert.Same(carrier, flag.Carrier);
        Assert.NotEqual(0u, simulation.Scene.Match.MatchId);
        Assert.NotEqual(0u, simulation.Scene.Match.PhaseRevision);
        Assert.True(carrier.ServerCombatIdentity.IsValid);
        var expectedDrop = new WorldEvent(1, 3, simulation.Scene.Match.MatchId,
            simulation.Scene.Match.PhaseRevision, WorldSubjectKind.Flag, WorldSignalKind.FlagDropped,
            (byte)carrier.TeamIndex, unchecked((uint)flag.Id), carrier.ServerCombatIdentity, flag.Position);
        Assert.True(expectedDrop.IsValid, expectedDrop.ToString());
        DrainWorld(simulation.Combat.World);

        Assert.True(client.Disconnect());
        Pump(server, client, () => server.Count == 0, 5);

        const uint disconnectTick = 3;
        simulation.Step(server, disconnectTick);

        WorldEvent dropped = default;
        bool found = false;
        while (simulation.Combat.World.TryPeek(out WorldEvent value))
        {
            simulation.Combat.World.Consume();
            if (value.Kind == WorldSignalKind.FlagDropped)
            {
                dropped = value;
                found = true;
            }
        }
        Assert.True(found, "Disconnecting the carrier did not publish a flag-drop event.");
        Assert.Equal(disconnectTick, dropped.Tick);
        Assert.Equal(peer.Connection.Id, dropped.Actor.ConnectionId);
        Assert.Equal(WorldSubjectKind.Flag, dropped.Subject);
    }

    private static void SendAndTickAt(INetTransport transport, ServerNetwork server,
        ServerSimulation simulation, NetClient client, List<InputCommand> history,
        ref uint sequence, ref uint tick, InputButtons buttons, InputButtons pressed,
        byte desiredWeapon = InputCommand.NoWeapon)
    {
        ServerPeer peer = Assert.IsType<ServerPeer>(server.Find(client.Connection?.Id ?? 0));
        uint inputEpoch = simulation.Scene.Players[peer.Slot].ServerCombatIdentity.Life;
        Assert.NotEqual(0u, inputEpoch);
        if (history.Count > 0 && history[^1].InputEpoch != inputEpoch)
        {
            history.Clear();
        }
        history.Add(new InputCommand(sequence, sequence, tick, buttons, pressed,
            -Vector3.UnitZ, desiredWeapon, inputEpoch));
        sequence++;
        int first = Math.Max(0, history.Count - InputBundle.Capacity);
        InputCommand[] bundle = history.GetRange(first, history.Count - first).ToArray();
        Assert.True(client.SendInputs(bundle, server.PhaseRevision));

        Thread.Sleep(16);
        server.Poll(tick);
        client.Poll();
        simulation.Step(server, tick);
        SendSnapshot(transport, server, simulation, client, tick);
        client.Poll();
        tick++;
    }

    private static InputCommand EpochCommand(uint sequence, uint tick, uint inputEpoch,
        InputButtons buttons = InputButtons.None, InputButtons pressed = InputButtons.None,
        byte desiredWeapon = InputCommand.NoWeapon)
        => new(sequence, tick, tick, buttons, pressed, -Vector3.UnitZ,
            desiredWeapon, inputEpoch);

    private static int ActiveShots(Scene scene, PlayerEntity owner)
    {
        int count = 0;
        foreach (BeamProjectileEntity beam in scene.GetBeamProjectileEntities())
        {
            if (beam.Owner == owner && beam.Lifespan > 0) count++;
        }
        return count;
    }

    private static void RecordProcessed(ServerPeer peer, ServerSimulation simulation,
        Dictionary<uint, InputCommand> processed)
    {
        if (peer.Inputs.HasProcessed && !processed.ContainsKey(peer.Inputs.LastProcessed))
        {
            processed.Add(peer.Inputs.LastProcessed, simulation.Combat.GetCommand(peer.Slot));
        }
    }

    private static void JoinAndReady(ServerNetwork server, NetClient client)
    {
        Pump(server, client, () => client.Connection != null, 8);
        Assert.True(client.Ready(client.Accepted.MatchId));
        Pump(server, client, () => server.Peers[0]?.Connection.State == NetConnectionState.Ready, 8);
    }

    private static void StepAndSend(INetTransport transport, ServerNetwork server,
        ServerSimulation simulation, NetClient client, uint tick)
    {
        server.Poll(tick);
        client.Poll();
        simulation.Step(server, tick);
        SendSnapshot(transport, server, simulation, client, tick);
        client.Poll();
    }

    private static void SendSnapshot(INetTransport transport, ServerNetwork server,
        ServerSimulation simulation, NetClient client, uint tick)
    {
        Span<byte> packet = stackalloc byte[NetConfig.MaxPacketSize];
        var snapshot = new SnapshotPacket(tick, tick, server.MatchId,
            0, false, simulation.Scene.Random.Rng1, simulation.Scene.Random.Rng2);
        int length = snapshot.Write(packet, simulation.States);
        ServerPeer? peer = server.Find(client.Connection?.Id ?? 0);
        if (peer?.Connection.State is NetConnectionState.Playing or NetConnectionState.Ready)
        {
            peer.Connection.Send(transport, NetMessageType.Snapshot, packet[..length]);
        }
    }

    private static void Pump(ServerNetwork server, NetClient client, Func<bool> done, int seconds)
    {
        var watch = Stopwatch.StartNew();
        uint tick = 0;
        while (watch.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            server.Poll(tick++);
            client.Poll();
            if (done()) return;
            Thread.Sleep(2);
        }
        Assert.Fail("UDP fixture did not reach its condition before the deadline.");
    }

    private static OctolithFlagEntity AcquireEnemyFlag(Scene scene, PlayerEntity carrier)
    {
        foreach (OctolithFlagEntity flag in scene.GetOctolithFlagEntities())
        {
            if (flag.Data.TeamId == carrier.TeamIndex) continue;
            // Keep the authored objective identity and team, while anchoring
            // the fixture at its finite authored base transform. Some AMHE1
            // flag instances carry an uninitialised live transform until the
            // first objective frame.
            Vector3 basePosition = flag.BasePosition;
            Assert.True(float.IsFinite(basePosition.X) && float.IsFinite(basePosition.Y)
                && float.IsFinite(basePosition.Z), $"flag={flag.Id} base={basePosition}");
            flag.Position = basePosition.AddY(1.25f);
            carrier.Position = flag.Position.AddY(-1.25f);
            carrier.PrevPosition = carrier.Position - Vector3.UnitX * 2;
            flag.Process();
            if (flag.Carrier == carrier) return flag;
        }
        throw new InvalidOperationException("AMHE1 did not expose an enemy flag pickup.");
    }

    private static void DrainWorld(ServerWorldEvents world)
    {
        while (world.TryPeek(out _)) world.Consume();
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

    // Test-only actual UDP proxy. It drops and duplicates datagrams and uses
    // bounded jitter so the input bundle's redundant history is exercised.
    private sealed class ImpairedLink : IDisposable
    {
        private readonly UdpClient _socket = new(new IPEndPoint(IPAddress.Loopback, 0));
        private readonly IPEndPoint _server;
        private readonly Thread _thread;
        private readonly Random _random;
        private readonly PriorityQueue<(byte[] Data, IPEndPoint Target), long> _pending = new();
        private volatile bool _running = true;
        public IPEndPoint Endpoint => (IPEndPoint)_socket.Client.LocalEndPoint!;

        public ImpairedLink(int serverPort, int seed)
        {
            _server = new IPEndPoint(IPAddress.Loopback, serverPort);
            _random = new Random(seed);
            _thread = new Thread(Run) { IsBackground = true };
            _thread.Start();
        }

        private void Run()
        {
            IPEndPoint? client = null;
            while (_running)
            {
                while (_socket.Available > 0)
                {
                    IPEndPoint from = new(IPAddress.Any, 0);
                    byte[] bytes = _socket.Receive(ref from);
                    IPEndPoint? target;
                    if (from.Equals(_server)) target = client;
                    else { client = from; target = _server; }
                    if (target == null || _random.Next(100) < 5) continue;
                    Queue(bytes, target);
                    if (_random.Next(100) < 10) Queue(bytes, target);
                }
                long now = Stopwatch.GetTimestamp();
                while (_pending.TryPeek(out _, out long due) && due <= now)
                {
                    var packet = _pending.Dequeue();
                    _socket.Send(packet.Data, packet.Target);
                }
                Thread.Sleep(1);
            }
        }

        private void Queue(byte[] data, IPEndPoint target)
        {
            if (_pending.Count >= 4096) return;
            long delay = (long)((0.012 + _random.NextDouble() * 0.018) * Stopwatch.Frequency);
            _pending.Enqueue((data, target), Stopwatch.GetTimestamp() + delay);
        }

        public void Dispose()
        {
            _running = false;
            Assert.True(_thread.Join(3000));
            _socket.Dispose();
        }
    }
}
