using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest
{
    /// <summary>Real-content, real-UDP acceptance of the authoritative phase boundary.</summary>
    internal static class MatchPhaseCheck
    {
        public static int Run(string[] args)
        {
            if (args.Length != 2) { Console.Error.WriteLine("Usage: nettest --match-phases DATA_DIRECTORY"); return 2; }
            try
            {
                ServerContent.Open(args[1], "AMHE1");
                using (var fixture = new Fixture()) { fixture.Run(); }
                VerifyFrozenObjectives(MatchMode.Bounty);
                VerifyFrozenObjectives(MatchMode.Nodes);
                Console.WriteLine("MATCHPHASES PASS waiting/countdown pristine, reset, input epochs, team quorum, deadline cycle, replicated rules/world");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine("MATCHPHASES FAIL " + error); return 1; }
        }

        private static void VerifyFrozenObjectives(MatchMode mode)
        {
            string? selected = null;
            foreach (RoomMetadata room in Metadata.RoomList)
            {
                if (!room.Multiplayer || room.FirstHunt || room.EntityPath == null) { continue; }
                bool flag = false, flagBase = false, node = false;
                int layer = Metadata.GetMultiplayerEntityLayer(mode.ToLegacyMode(), NetLaunch.RoomPlayerCount);
                foreach (Entity entity in Read.GetEntities(room.EntityPath, layer, false))
                {
                    flag |= entity is Entity<OctolithFlagEntityData>;
                    flagBase |= entity is Entity<FlagBaseEntityData>;
                    node |= entity.Type == EntityType.NodeDefense;
                }
                if (mode == MatchMode.Bounty ? flag && flagBase : node) { selected = room.Name; break; }
            }
            Require(selected != null, $"No real-content objective room for {mode}.");
            using var simulation = new ServerSimulation(MatchRules.CreateDefault(mode, selected!));
            for (int i = 0; i < 120; i++) { simulation.Scene.StepHeadlessFrame(); }
            simulation.AssertPristineWorld();
            // Isolate the frame gate from admission; the UDP fixture above verifies admission/reset.
            simulation.Scene.Match.Phase = MatchPhase.Countdown;
            for (int i = 0; i < 180; i++) { simulation.Scene.StepHeadlessFrame(); }
            simulation.AssertPristineWorld();
            Console.WriteLine($"MATCHPHASES {mode} {selected} objective world frozen PASS");
        }

        private static void Require(bool value, string message)
        { if (!value) { throw new InvalidOperationException(message); } }

        private sealed class Fixture : IDisposable
        {
            private readonly MatchRules _rules = new(MatchMode.TeamBattle, "MP1 SANCTORUS", 4,
                TimeSpan.FromSeconds(1), scoreGoal: 0, objectiveTimeGoal: TimeSpan.FromSeconds(17),
                startingLives: 3, friendlyFire: true, affinityWeapons: true, playerRadar: true,
                octolithReset: true, damageLevel: 2);
            private readonly NetTransport _transport = new(0);
            private readonly NetTransport _firstTransport = new(0);
            private readonly NetTransport _secondTransport = new(0);
            private readonly ServerSimulation _simulation;
            private readonly ServerNetwork _network;
            private readonly NetClient _first;
            private readonly NetClient _second;
            private readonly ClientWorldState _world = new();
            private readonly WorldStateCapture _capture = new();
            private uint _tick;
            private uint _revision;
            private uint _sequence;

            public Fixture()
            {
                _simulation = new ServerSimulation(_rules);
                _network = new ServerNetwork(_transport, _rules);
                var endpoint = new IPEndPoint(IPAddress.Loopback, _transport.LocalPort);
                _first = new NetClient(_firstTransport, endpoint, "PHASE A", Hunter.Samus);
                _second = new NetClient(_secondTransport, endpoint, "PHASE B", Hunter.Kanden);
                _world.Reset(_network.MatchId);
                _first.WorldPacketValidator = WorldPacket.TryValidate;
                _first.WorldPacketReceived = body => _world.Receive(body);
            }

            private void Pump()
            {
                _first.Poll(); _second.Poll(); _network.Poll(_tick);
                _simulation.Step(_network, _tick);
                _first.Poll(); _second.Poll();
            }

            private void Until(Func<bool> predicate, string message)
            {
                var timer = Stopwatch.StartNew();
                while (!predicate() && timer.ElapsedMilliseconds < 5000) { Pump(); Thread.Sleep(1); }
                Require(predicate(), message);
            }

            private void VerifyWorld()
            {
                _capture.Capture(_simulation.Scene, _network.MatchId, ++_revision, _tick);
                var buffer = new byte[WorldPacket.MaxSize];
                ServerPeer peer = _network.Peers[_first.Accepted.Slot]!;
                for (int i = 0; i < _capture.BatchCount; i++)
                { int length = _capture.WriteBatch(buffer, i); _network.SendWorld(peer, buffer.AsSpan(0, length)); }
                Until(() => _world.HasState && _world.Revision == _revision, "World was not delivered over UDP.");
                Require(_capture.Records.SequenceEqual(_world.Records), "Client world differs from authoritative capture.");
                Require(_world.PhaseRevision == _simulation.Scene.Match.PhaseRevision, "Client phase epoch differs.");
            }

            public void Run()
            {
                Until(() => _first.State == NetConnectionState.Loading && _second.State == NetConnectionState.Loading, "Join handshake failed.");
                Require(_first.Accepted.Rules == _rules && _second.Accepted.Rules == _rules, "Join rules were not lossless.");
                Require(_first.Ready(_network.MatchId), "First ready failed.");
                Until(() => _network.Peers[_first.Accepted.Slot]?.Connection.State == NetConnectionState.Playing, "First activation failed.");
                MatchRuntime match = _simulation.Scene.Match;
                Require(match.Phase == MatchPhase.WaitingForPlayers, "One player started a multiplayer match.");
                ServerPeer first = _network.Peers[_first.Accepted.Slot]!;
                ServerPeer second = _network.Peers[_second.Accepted.Slot]!;
                for (int i = 0; i < 20; i++) { _tick++; Pump(); }
                _simulation.AssertPristineWorld();
                Require(match.MatchTime == 1, "Waiting consumed the match clock.");
                VerifyWorld();
                uint oldEpoch = match.PhaseRevision;
                var malicious = new[] { new InputCommand(++_sequence, _tick, _tick,
                    InputButtons.Forward | InputButtons.Shoot | InputButtons.Morph,
                    InputButtons.Shoot | InputButtons.Morph, Vector3.UnitZ, InputCommand.NoWeapon) };
                Require(_first.SendInputs(malicious, oldEpoch), "Could not stage preplay input.");
                for (int i = 0; i < 20; i++) { Pump(); Thread.Sleep(1); }
                for (int i = 0; i < 4; i++) { first.Inputs.Take(_tick); }
                Require(!first.Inputs.HasProcessed, "Prematch input reached the server input queue.");
                _simulation.AssertPristineWorld();
                match.Players[first.Slot].Points = 123;
                second.TeamIndex = first.TeamIndex; // Explicitly test team quorum independent of slot parity.
                Require(_second.Ready(_network.MatchId), "Second ready failed.");
                Until(() => second.Connection.State == NetConnectionState.Playing, "Second activation failed.");
                Require(match.Phase == MatchPhase.WaitingForPlayers, "Two players on one team started a team match.");
                second.TeamIndex = (byte)(1 - first.TeamIndex);
                Pump();
                Require(match.Phase == MatchPhase.Countdown && match.Players[first.Slot].Points == 0, "Countdown did not reset statistics.");
                uint firstLife = _simulation.States[0].Life;
                uint countdownEpoch = match.PhaseRevision;
                for (int i = 0; i < 30; i++) { _tick++; Pump(); }
                _simulation.AssertPristineWorld();
                VerifyWorld();
                // A loading connection ceases to count toward ready-player eligibility.
                second.Connection.BeginLoading(_network.MatchId);
                Pump();
                Require(match.Phase == MatchPhase.WaitingForPlayers, "Lost eligibility did not cancel countdown.");
                second.Connection.Ready(_network.MatchId);
                Pump();
                Require(match.Phase == MatchPhase.Countdown && _simulation.CountdownResets == 2, "Countdown restart did not reset again.");
                Require(_simulation.States[0].Life > firstLife, "Competitive reset reused the player's life identity.");
                _tick = match.PhaseEndTick;
                Pump();
                Require(match.Phase == MatchPhase.Playing && match.MatchTime == 1, "Playing clock did not start at its full limit.");
                Require(_network.Phase == match.Phase && _network.PhaseRevision == match.PhaseRevision, "Ingress phase differs from runtime.");
                Require(_first.SendInputs(malicious, countdownEpoch), "Could not stage stale epoch input.");
                for (int i = 0; i < 20; i++) { Pump(); Thread.Sleep(1); }
                Require(!first.Inputs.HasProcessed, "A countdown epoch crossed into Playing.");
                foreach (SnapshotPlayer player in _simulation.States)
                { Require(player.TeamIndex == _network.Peers[player.Slot]!.TeamIndex, "Player team differs from server assignment."); }
                VerifyWorld();
                match.Players[second.Slot].Points = 9;
                _tick = match.PhaseEndTick;
                Pump();
                Require(match.Phase == MatchPhase.Ending && match.Result != null, "Timed match did not capture a result.");
                MatchResult result = match.Result!;
                int points = match.Players[second.Slot].Points;
                string? nickname = GameState.Nicknames[second.Slot];
                second.Connection.BeginLoading(_network.MatchId);
                second.Connection.Ready(_network.MatchId);
                Pump();
                Require(second.Connection.State == NetConnectionState.Ready
                    && match.Players[second.Slot].Points == points
                    && GameState.Nicknames[second.Slot] == nickname,
                    "Terminal readiness changed competitive ownership.");
                _network.Remove(second.Slot);
                Pump();
                Require(match.Players[second.Slot].Points == points && ReferenceEquals(result, match.Result),
                    "Terminal departure changed completed statistics.");
                VerifyWorld();
                _tick = match.PhaseEndTick; Pump();
                Require(match.Phase == MatchPhase.Intermission && ReferenceEquals(result, match.Result), "Intermission replaced the result.");
                VerifyWorld();
                _tick = match.PhaseEndTick - 1; Pump();
                Require(!_simulation.Lifecycle.RotationDue, "Rotation ran early.");
                _tick++; Pump();
                Require(_simulation.Lifecycle.RotationDue, "Intermission did not finish.");
            }

            public void Dispose()
            {
                _first.Dispose(); _second.Dispose(); _firstTransport.Dispose(); _secondTransport.Dispose();
                _transport.Dispose(); _simulation.Dispose();
            }
        }
    }
}
