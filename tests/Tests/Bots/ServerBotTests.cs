using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Diagnostics;
using System.Threading;
using System.Buffers.Binary;
using MphRead.Entities;
using MphRead.Reporting;
using MphRead.Identity;
using System.Linq;
using MphRead.Mods.Network;
using Xunit;
namespace MphRead.Tests;
[Collection("Match baseline globals")]
public sealed class ServerBotTests
{
    [Trait("RequiresGameContent", "true")]
    [Fact]
    public void UdpJoinWaitsForOneLiveBotThenReadiesItsReleasedSlot()
    {
        string data = Environment.GetEnvironmentVariable("GAME_DATA_DIRECTORY") ?? Path.Combine(Directory.GetCurrentDirectory(), "AMHE1");
        using var content = ServerContent.PreserveContext("AMHE1"); ServerContent.Open(data, "AMHE1");
        using var simulation = new ServerSimulation(new MatchRules(MatchMode.Battle, "MP1 SANCTORUS"), botFill: new(8));
        using var serverSocket = new NetTransport(0);
        var network = new ServerNetwork(serverSocket, simulation.Scene.Match.Rules);
        network.BotRosterEntry = simulation.Bots.Roster;
        network.CanClaimPlayerSlot = slot => simulation.Bots.Claim(slot, network, network.Tick);
        network.CancelBotClaim = simulation.Bots.CancelClaim;
        using var firstSocket = new NetTransport(0);
        using var first = new NetClient(firstSocket, new IPEndPoint(IPAddress.Loopback, serverSocket.LocalPort), "First", Hunter.Samus);
        using var joiningSocket = new NetTransport(0);
        using var joining = new NetClient(joiningSocket, new IPEndPoint(IPAddress.Loopback, serverSocket.LocalPort), "Joining", Hunter.Samus);
        uint tick = 1;
        void PumpUntil(Func<bool> done, bool includeJoining = false)
        {
            var timer = Stopwatch.StartNew();
            while (!done() && timer.ElapsedMilliseconds < 3000)
            {
                network.Poll(tick++); first.Poll(); if (includeJoining) joining.Poll(); Thread.Sleep(1);
            }
            Assert.True(done(), $"Handshake did not reach expected state: {first.Failure}; {joining.Failure}");
        }
        PumpUntil(() => first.Connection != null);
        Assert.True(first.Ready(network.MatchId));
        PumpUntil(() => network.Peers[0]!.Connection.State == NetConnectionState.Ready);
        simulation.Step(network, tick++);
        Assert.Equal(7, simulation.Bots.Count);
        Assert.All(Enumerable.Range(1, 7), slot => Assert.True(simulation.Scene.Players[slot].Health > 0));
        simulation.Scene.Match.Phase = MatchPhase.Playing; network.Phase = MatchPhase.Playing;
        ulong[] identities = Enumerable.Range(1, 7).Select(slot => simulation.Bots.Roster(slot)!.Value.ConnectionId).ToArray();
        PumpUntil(() => joining.AwaitingBotRetirement, true);
        Assert.Null(joining.Connection); Assert.Null(joining.Failure);
        int reserved = Enumerable.Range(0, 8).Single(network.HasPendingBotAdmission);
        Assert.Single(simulation.Bots.Participants.ToArray(), bot => bot?.RetirementRequested == true);
        double firstHeartbeat = (double)typeof(NetClient).GetField("_lastJoinPending", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(joining)!;
        // The actual 500ms join retry is sent over UDP and acknowledged without
        // allocating another slot or issuing another retirement request.
        PumpUntil(() => (double)typeof(NetClient).GetField("_lastJoinPending", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(joining)! > firstHeartbeat, true);
        Assert.Null(joining.Connection); Assert.Null(joining.Failure);
        Assert.Single(simulation.Bots.Participants.ToArray(), bot => bot?.RetirementRequested == true);
        simulation.Scene.Match.PrimeHunter = reserved;
        simulation.Scene.Players[reserved].Health = 0;
        PumpUntil(() => joining.Connection != null, true);
        Assert.Equal(reserved, joining.Accepted.Slot);
        Assert.Equal(-1, simulation.Scene.Match.PrimeHunter);
        Assert.Equal(6, simulation.Bots.Count);
        Assert.True(joining.Ready(network.MatchId));
        PumpUntil(() => network.Peers[reserved]!.Connection.State == NetConnectionState.Ready, true);
        simulation.Step(network, tick++);
        Assert.False(simulation.Scene.Players[reserved].IsBot);
        Assert.True(simulation.Scene.Players[reserved].LoadFlags.TestFlag(LoadFlags.Active));
        foreach (int slot in Enumerable.Range(1, 7).Where(slot => slot != reserved))
        {
            Assert.Equal(identities[slot - 1], simulation.Bots.Roster(slot)!.Value.ConnectionId);
            Assert.False(simulation.Bots.Participants[slot]!.RetirementRequested);
        }
    }

    [Fact]
    public void UdpPendingRejectsWrongNonceAndMalformedBodyWithoutRefreshingHeartbeat()
    {
        using var server = new NetTransport(0);
        using var socket = new NetTransport(0);
        using var client = new NetClient(socket, new IPEndPoint(IPAddress.Loopback, server.LocalPort), "Joining", Hunter.Samus, nonce: 123);
        byte[] discovery = new byte[1 + ServerStatusPacket.Size];
        discovery[0] = (byte)PacketType.StatusReply;
        new ServerStatusPacket { Match = new MatchStatePacket { Mode = (byte)GameMode.Battle,
            RoomKey = "MP1 SANCTORUS", NextRoomKey = "MP1 SANCTORUS" }, MaxPlayers = 8,
            Protocol = NetHeader.Version, ServerName = "TEST" }.Write(discovery.AsSpan(1));
        server.SendDatagram(new IPEndPoint(IPAddress.Loopback, socket.LocalPort), discovery);
        byte[] bytes = new byte[NetHeader.Size + 8];
        new NetHeader(NetMessageType.JoinPending, NetHeaderFlags.Unsequenced, 0, 0, 0, 0).Write(bytes);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(NetHeader.Size), 124);
        server.SendDatagram(new IPEndPoint(IPAddress.Loopback, socket.LocalPort), bytes);
        server.SendDatagram(new IPEndPoint(IPAddress.Loopback, socket.LocalPort), bytes.AsSpan(0, bytes.Length - 1));
        var timer = Stopwatch.StartNew();
        while (client.Rejected < 2 && timer.ElapsedMilliseconds < 3000) { client.Poll(); Thread.Sleep(1); }
        Assert.Equal(2, client.Rejected); Assert.False(client.AwaitingBotRetirement);
        Assert.Null(client.Connection); Assert.Null(client.Failure);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(NetHeader.Size), 123);
        server.SendDatagram(new IPEndPoint(IPAddress.Loopback, socket.LocalPort), bytes);
        while (!client.AwaitingBotRetirement && timer.ElapsedMilliseconds < 3000) { client.Poll(); Thread.Sleep(1); }
        Assert.True(client.AwaitingBotRetirement); Assert.Null(client.Connection);
    }

    [Fact]
    public void OnePendingHumanClaimsOnlyOneOfSevenBotsAndAdmitsWhenSafe()
    {
        using var transport = (INetTransport)Activator.CreateInstance(typeof(ServerNetwork).Assembly.GetType("MphRead.Mods.Network.UdpTransport")!, new object[] { 0 })!;
        var network = new ServerNetwork(transport, new MatchRules(MatchMode.Battle, "MP1 SANCTORUS"));
        var human = new ServerPeer(new NetConnection(100, new IPEndPoint(IPAddress.Loopback, 10001), 1, 0),
            new JoinPacket { Name = "Human", Hunter = Hunter.Samus, Nonce = 100 }, 0, 0);
        ((ServerPeer?[])typeof(ServerNetwork).GetField("_peers", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(network)!)[0] = human;
        bool[] requested = new bool[8]; bool safe = false; bool retired = false;
        network.BotRosterEntry = slot => slot > 0 && !(slot == 1 && retired)
            ? new NetRosterEntry((byte)slot, (ulong)(100 + slot), Hunter.Samus, 0, "BOT", IsBot: true) : null;
        network.CanClaimPlayerSlot = slot => { requested[slot] = true; if (safe) retired = true; return safe; };
        var endpoint = new IPEndPoint(IPAddress.Loopback, 10002);
        var join = new JoinPacket { Protocol = NetHeader.Version, Name = "Joining", Hunter = Hunter.Samus, Nonce = 200 };
        Assert.True(network.SubmitLegacyJoinForTesting(endpoint, join));
        Assert.Single(requested, value => value);
        Assert.True(network.HasPendingBotAdmission(1));
        for (int retry = 0; retry < 5; retry++) network.SubmitLegacyJoinForTesting(endpoint, join);
        Assert.Single(requested, value => value);
        Assert.Null(network.Peers[1]);
        safe = true;
        typeof(ServerNetwork).GetMethod("ProcessBotAdmissions", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(network, null);
        Assert.False(network.HasPendingBotAdmission(1));
        Assert.Equal("Joining", network.Peers[1]!.Name);
        Assert.Single(requested, value => value);
    }
    [Fact]
    public void ExpiredPendingIdentityReleasesReservationWithoutClaimingSlot()
    {
        using var transport = (INetTransport)Activator.CreateInstance(typeof(ServerNetwork).Assembly.GetType("MphRead.Mods.Network.UdpTransport")!, new object[] { 0 })!;
        var network = new ServerNetwork(transport, new MatchRules(MatchMode.Battle, "MP1 SANCTORUS"));
        int cancelled = -1;
        network.CancelBotClaim = slot => cancelled = slot;
        network.CanClaimPlayerSlot = _ => throw new Exception("Expired identity must not claim a slot.");
        var join = new JoinPacket { Protocol = NetHeader.Version, Name = "Expired", Hunter = Hunter.Samus, Nonce = 200 };
        var identity = new TicketIdentity(new PlayerId(Guid.NewGuid()), Guid.NewGuid(), 1);
        typeof(ServerNetwork).GetMethod("QueueBotAdmission", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(network, new object[] { new IPEndPoint(IPAddress.Loopback, 10002), join, identity, 1 });
        typeof(ServerNetwork).GetMethod("ProcessBotAdmissions", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(network, null);
        Assert.Equal(1, cancelled);
        Assert.False(network.HasPendingBotAdmission(1));
        Assert.Null(network.Peers[1]);
    }
    [Theory]
    [InlineData(0,0)] [InlineData(1,3)] [InlineData(2,2)] [InlineData(3,1)] [InlineData(4,0)] [InlineData(8,0)]
    public void TargetCountsHumansWithoutExceedingCapacity(int humans, int bots)
        => Assert.Equal(bots, new BotFillPolicy(4).DesiredBots(humans, 8));
    [Fact]
    public void InvalidSkillAndPopulationFailBeforeWorldMutation()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BotFillPolicy(9).Validate(8));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BotFillPolicy(4, 5).Validate(8));
    }

    [Theory]
    [InlineData(BotDifficulty.Beginner, 0)]
    [InlineData(BotDifficulty.Easy, 0)]
    [InlineData(BotDifficulty.Normal, 1)]
    [InlineData(BotDifficulty.Hard, 2)]
    [InlineData(BotDifficulty.Expert, 2)]
    public void FiveDifficultiesMapToSafeRetailBehaviorBands(
        BotDifficulty difficulty, int legacyLevel)
    {
        Assert.Equal(legacyLevel, difficulty.LegacyLevel());
        new BotFillPolicy(4, difficulty).Validate(8);
    }

    [Fact]
    public void ChargedFireCooldownIsSampledOnceAndHeldUntilItsDeadline()
    {
        const int delay = 10;
        const int fullCharge = 60;

        Assert.True(PlayerEntity.PlayerAiData.ShouldBeginChargedFireCooldown(
            active: false, chargeLevel: fullCharge, fullCharge));
        Assert.False(PlayerEntity.PlayerAiData.ShouldBeginChargedFireCooldown(
            active: true, chargeLevel: fullCharge, fullCharge));
        Assert.False(PlayerEntity.PlayerAiData.ShouldBeginChargedFireCooldown(
            active: false, chargeLevel: fullCharge - 1, fullCharge));

        Assert.True(PlayerEntity.PlayerAiData.ShouldHoldChargedFireCooldown(
            active: true, shooting: true, framesUp: 0, delay));
        Assert.True(PlayerEntity.PlayerAiData.ShouldHoldChargedFireCooldown(
            active: true, shooting: false, framesUp: 1, delay));
        Assert.True(PlayerEntity.PlayerAiData.ShouldHoldChargedFireCooldown(
            active: true, shooting: false, framesUp: delay, delay));
        Assert.False(PlayerEntity.PlayerAiData.ShouldHoldChargedFireCooldown(
            active: true, shooting: false, framesUp: delay + 1, delay));
        Assert.False(PlayerEntity.PlayerAiData.ShouldHoldChargedFireCooldown(
            active: false, shooting: true, framesUp: 0, delay));
    }

    [Trait("RequiresGameContent", "true")]
    [Fact]
    public void LiveBotsProduceAuthoritativeWeaponShots()
    {
        string data = Environment.GetEnvironmentVariable("GAME_DATA_DIRECTORY")
            ?? Path.Combine(Directory.GetCurrentDirectory(), "AMHE1");
        using var content = ServerContent.PreserveContext("AMHE1");
        ServerContent.Open(data, "AMHE1");
        using var simulation = new ServerSimulation(
            new MatchRules(MatchMode.Battle, "MP1 SANCTORUS"),
            botFill: new BotFillPolicy(4, BotDifficulty.Normal));
        using var transport = (INetTransport)Activator.CreateInstance(
            typeof(ServerNetwork).Assembly.GetType(
                "MphRead.Mods.Network.UdpTransport")!, new object[] { 0 })!;
        var network = new ServerNetwork(transport, simulation.Scene.Match.Rules);
        var connection = new NetConnection(100,
            new IPEndPoint(IPAddress.Loopback, 10001), 1, 0);
        connection.Ready(1);
        var peer = new ServerPeer(connection, new JoinPacket
        {
            Hunter = Hunter.Samus,
            Name = "Human",
            Nonce = 100
        }, 0, 0);
        ((ServerPeer?[])typeof(ServerNetwork).GetField("_peers",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(network)!)[0]
            = peer;

        simulation.Step(network, 0);
        Assert.Equal(3, simulation.Bots.Count);
        simulation.Scene.Match.Phase = MatchPhase.Playing;
        network.Phase = MatchPhase.Playing;
        long shotsBefore = simulation.Combat.ShotsConsidered;
        int[] shootIntentTicks = new int[4];
        int[] bipedTicks = new int[4];
        int[] targetTicks = new int[4];
        for (uint tick = 1; tick <= SimTicks.Hz * 20; tick++)
        {
            simulation.Step(network, tick);
            for (int slot = 1; slot <= 3; slot++)
            {
                PlayerEntity player = simulation.Scene.Players[slot];
                if (!player.IsAltForm) bipedTicks[slot]++;
                if (player.Controls.Shoot.IsDown || player.Controls.Shoot.IsPressed)
                    shootIntentTicks[slot]++;
                if (player.AiData.Flags2.TestFlag(AiFlags2.TargetPlayer))
                    targetTicks[slot]++;
            }
        }

        string botState = string.Join(" | ", Enumerable.Range(1, 3).Select(slot =>
        {
            PlayerEntity player = simulation.Scene.Players[slot];
            object? target = typeof(PlayerEntity.PlayerAiData).GetField("_targetPlayer",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(player.AiData);
            return $"slot={slot} hunter={player.Hunter} hp={player.Health} pos={player.Position} "
                + $"facing={player.CameraInfo.Facing} fov={player.CameraInfo.Fov} "
                + $"flags2={player.AiData.Flags2} target={(target as PlayerEntity)?.SlotIndex.ToString() ?? "none"} "
                + $"targetTicks={targetTicks[slot]} bipedTicks={bipedTicks[slot]} shootTicks={shootIntentTicks[slot]} "
                + $"weapon={player.CurrentWeapon} charge={player.EquipInfo.ChargeLevel}";
        }));
        var events = new CombatEvent[simulation.Combat.Count];
        int copied = simulation.Combat.CopyPending(events);
        CombatEvent[] botShots = events.AsSpan(0, copied).ToArray()
            .Where(value => value.Kind == CombatEventKind.Shot
                && value.Actor.Slot is >= 1 and <= 3
                && simulation.Scene.Players[value.Actor.Slot].IsBot)
            .ToArray();
        Assert.True(simulation.Combat.ShotsConsidered > shotsBefore
            && botShots.Length > 0,
            $"Active bots did not produce an authoritative weapon shot in 20 seconds. {botState}");
    }

    [Trait("RequiresGameContent", "true")]
    [Fact]
    public void ChargedBotReleasesOnceThenWaitsForOneFixedCooldown()
    {
        string data = Environment.GetEnvironmentVariable("GAME_DATA_DIRECTORY")
            ?? Path.Combine(Directory.GetCurrentDirectory(), "AMHE1");
        using var content = ServerContent.PreserveContext("AMHE1");
        ServerContent.Open(data, "AMHE1");
        using var simulation = new ServerSimulation(
            new MatchRules(MatchMode.Battle, "MP1 SANCTORUS"),
            botFill: new BotFillPolicy(2, BotDifficulty.Normal));
        using var transport = (INetTransport)Activator.CreateInstance(
            typeof(ServerNetwork).Assembly.GetType(
                "MphRead.Mods.Network.UdpTransport")!, new object[] { 0 })!;
        var network = new ServerNetwork(transport, simulation.Scene.Match.Rules);
        var connection = new NetConnection(100,
            new IPEndPoint(IPAddress.Loopback, 10001), 1, 0);
        connection.Ready(1);
        ((ServerPeer?[])typeof(ServerNetwork).GetField("_peers",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(network)!)[0]
            = new ServerPeer(connection, new JoinPacket
            {
                Hunter = Hunter.Samus,
                Name = "Human",
                Nonce = 100
            }, 0, 0);

        simulation.Step(network, 0);
        Assert.Single(simulation.Bots.Participants.ToArray(), bot => bot != null);
        simulation.Scene.Match.Phase = MatchPhase.Playing;
        network.Phase = MatchPhase.Playing;
        PlayerEntity bot = simulation.Scene.Players[1];
        PlayerEntity.PlayerAiData ai = bot.AiData;
        typeof(PlayerEntity.PlayerAiData).GetField("_forceDisable",
            BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(ai, true);
        typeof(PlayerEntity.PlayerAiData).GetField("_weapon1",
            BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(ai, 0);
        MethodInfo driveWeapon = typeof(PlayerEntity.PlayerAiData).GetMethod(
            "Func2143A40", BindingFlags.Instance | BindingFlags.NonPublic)!;
        FieldInfo cooldownActive = typeof(PlayerEntity.PlayerAiData).GetField(
            "_chargedFireCooldownActive",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        FieldInfo shotDelay = typeof(PlayerEntity.PlayerAiData).GetField(
            "_shotDelay", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var chargedReleases = new System.Collections.Generic.List<uint>();
        int? activeCooldownSample = null;
        int cooldownTransitions = 0;

        ai.Flags4 |= AiFlags4.Bit1;
        driveWeapon.Invoke(ai, null);
        for (uint tick = 1; tick <= SimTicks.Hz * 15; tick++)
        {
            simulation.Step(network, tick);
            if (bot.Flags1.TestFlag(PlayerFlags1.ShotCharged))
                chargedReleases.Add(tick);
            ai.Flags4 |= AiFlags4.Bit1;
            driveWeapon.Invoke(ai, null);
            if ((bool)cooldownActive.GetValue(ai)!)
            {
                int currentSample = (int)shotDelay.GetValue(ai)!;
                if (!activeCooldownSample.HasValue)
                {
                    activeCooldownSample = currentSample;
                    cooldownTransitions++;
                }
                Assert.Equal(activeCooldownSample.Value, currentSample);
            }
            else
            {
                activeCooldownSample = null;
            }
        }

        Assert.True(chargedReleases.Count >= 2,
            $"Expected two charged releases, observed [{string.Join(',', chargedReleases)}].");
        Assert.True(cooldownTransitions >= 2,
            $"Expected two sampled cooldown periods, observed {cooldownTransitions}.");
        int fullCharge = SimTicks.From30HzFrames(bot.EquipInfo.Weapon.FullCharge);
        Assert.All(chargedReleases.Zip(chargedReleases.Skip(1)), pair =>
            Assert.True(pair.Second - pair.First > fullCharge,
                $"Charged releases {pair.First} and {pair.Second} bypassed charge/cooldown."));
    }

    [Trait("RequiresGameContent", "true")]
    [Fact]
    public void ActualAiBotsHaveNoConnectionAndRetireOnlyAtSafeBoundary()
    {
        string data = Environment.GetEnvironmentVariable("GAME_DATA_DIRECTORY") ?? Path.Combine(Directory.GetCurrentDirectory(), "AMHE1");
        using var content = ServerContent.PreserveContext("AMHE1"); ServerContent.Open(data, "AMHE1");
        using var simulation = new ServerSimulation(new MatchRules(MatchMode.Battle, "MP1 SANCTORUS"), botFill: new(4));
        using var transport = (INetTransport)Activator.CreateInstance(typeof(ServerNetwork).Assembly.GetType("MphRead.Mods.Network.UdpTransport")!, new object[] { 0 })!;
        var network = new ServerNetwork(transport, simulation.Scene.Match.Rules);
        simulation.Reports = new MatchParticipantLedger(Guid.NewGuid(), Guid.NewGuid(), "test");
        var connection = new NetConnection(100, new IPEndPoint(IPAddress.Loopback, 10001), 1, 0);
        var peer = new ServerPeer(connection, new JoinPacket { Hunter = Hunter.Samus, Name = "Human", Nonce = 100 }, 0, 0);
        ((ServerPeer?[])typeof(ServerNetwork).GetField("_peers", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(network)!)[0] = peer;
        simulation.Bots.Update(network, 0); Assert.False(simulation.Bots.Occupied(1));
        connection.Ready(1);
        simulation.Step(network, 0);
        Assert.Equal(4, simulation.Scene.Players.ActiveCount); Assert.Equal(4, simulation.States.Length);
        for (int slot = 1; slot < 4; slot++)
        {
            Assert.Null(network.Peers[slot]); Assert.True(simulation.Scene.Players[slot].IsBot);
            Assert.True(simulation.Bots.Roster(slot)!.Value.IsBot);
            Assert.NotEqual(0ul, simulation.Scene.Players[slot].ServerCombatIdentity.ConnectionId);
        }
        simulation.Scene.Match.Phase = MatchPhase.Playing;
        Assert.False(simulation.Bots.Claim(1, network, 1)); Assert.True(simulation.Bots.Occupied(1));
        for (uint tick = 1; tick < 20; tick++) simulation.Step(network, tick);
        Assert.True(simulation.Scene.Players[1].IsBot);
        simulation.Scene.Match.PrimeHunter = 1;
        simulation.Scene.Players[1].Health = 0;
        Assert.True(simulation.Bots.Claim(1, network, 20)); Assert.False(simulation.Bots.Occupied(1));
        Assert.False(simulation.Scene.Players[1].LoadFlags.TestFlag(LoadFlags.Active));
        Assert.Equal(-1, simulation.Scene.Match.PrimeHunter);
        var peers = (ServerPeer?[])typeof(ServerNetwork).GetField("_peers", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(network)!;
        peers[0] = null;
        var nextConnection = new NetConnection(200, new IPEndPoint(IPAddress.Loopback, 10002), 1, 0); nextConnection.Ready(1);
        peers[4] = new ServerPeer(nextConnection, new JoinPacket {Hunter=Hunter.Samus,Name="Next",Nonce=200},4,0);
        simulation.Step(network,21);
        Assert.True(simulation.Bots.Occupied(0)); Assert.True(simulation.Scene.Players[0].IsBot);
        Assert.True(simulation.Scene.Players[0].LoadFlags.TestFlag(LoadFlags.Active));
        simulation.Scene.Match.CaptureResult(21);
        var report = simulation.Reports.Complete(simulation.Scene, 21)!;
        Assert.Equal(4, report.Participants.Count(p => p.Kind == ParticipantKind.Bot));
        Assert.All(report.Participants.Where(p => p.Kind == ParticipantKind.Bot), p => Assert.Null(p.PlayerId));
    }
    [Fact]
    public void BotRosterFlagIsExplicitAndRejectsUnknownBits()
    {
        byte[] wire = new byte[SessionRosterPacket.MaxSize];
        int length = SessionRosterPacket.Write(wire, 1, new[] { new NetRosterEntry(1,123,Hunter.Samus,1,"BOT 2",0,true) });
        var entries = new NetRosterEntry[8];
        Assert.True(SessionRosterPacket.TryRead(wire.AsSpan(0,length), entries, out _, out _)); Assert.True(entries[0].IsBot);
        wire[SessionRosterPacket.HeaderSize + 29] = 2;
        Assert.False(SessionRosterPacket.TryRead(wire.AsSpan(0,length), entries, out _, out _));
    }
    [Fact]
    public void TerminalBotIdentityUsesIndependentActiveAndBotBits()
    {
        var record = new WorldRecord(WorldRecordKind.PlayerIdentity, 1, 0, 0, OpenTK.Mathematics.Vector3.Zero,
            (uint)Hunter.Samus, 1, 3, 0, 0) { PlayerName = "BOT 2" };
        byte[] wire = new byte[WorldRecord.Size]; record.Write(wire);
        Assert.True(WorldRecord.TryRead(wire, out var decoded)); Assert.Equal(3u, decoded.C);
        (record with { C = 4 }).Write(wire); Assert.False(WorldRecord.TryRead(wire, out _));
    }

}
