using System;
using System.Collections.Generic;
using MphRead.Entities;

namespace MphRead.Mods.Network
{
    public sealed class ServerSimulation : IDisposable
    {
        private readonly ulong[] _activeConnections = new ulong[8];
        private readonly SnapshotPlayer[] _states = new SnapshotPlayer[8];
        private readonly WorldStateCapture _pristineCapture = new();
        private readonly WorldRecord[] _pristineWorld;
        private readonly Action _resetForCountdown;
        private readonly uint _initialRng1;
        private readonly uint _initialRng2;
        private ServerNetwork? _network;
        private int _stateCount;
        public Scene Scene { get; }
        public ServerCombat Combat { get; }
        public MatchLifecycle Lifecycle { get; }
        public ReadOnlySpan<SnapshotPlayer> States => _states.AsSpan(0, _stateCount);
        internal int CountdownResets { get; private set; }

        public ServerSimulation(RotationEntry entry, bool lagCompEnabled = true, bool projectileCatchUpEnabled = true)
            : this(entry.ToMatchRules(), lagCompEnabled, projectileCatchUpEnabled)
        {
        }

        public ServerSimulation(MatchRules rules, bool lagCompEnabled = true, bool projectileCatchUpEnabled = true)
        {
            MatchLifecycle.ValidateRules(rules);
            Combat = new ServerCombat(lagCompEnabled, projectileCatchUpEnabled);
            Scene = Scene.CreateHeadless();
            try
            {
                Scene.LoadServerRoom(rules.RoomKey, rules.Mode.ToLegacyMode(), players: 8,
                    roomPlayerCount: NetLaunch.RoomPlayerCount);
                WorldStateCapture.ValidateRoom(Scene);
                if (!ServerContentPack.HasObjectives(Scene, rules.Mode.ToLegacyMode()))
                {
                    throw new ProgramException($"{rules.RoomKey}/{rules.Mode} has no required objectives in the multiplayer entity layout.");
                }
                foreach (PlayerEntity player in Scene.GetPlayerEntities()) { player.ServerDeactivate(); }
                PlayerEntity.PlayerCount = 0;
                PlayerEntity.MaxPlayers = rules.MaxPlayers;
                Scene.Match.ApplyRules(rules);
                Scene.Match.RadarPlayers = rules.PlayerRadar;
                Lifecycle = new MatchLifecycle(Scene.Match);
                _initialRng1 = Rng.Rng1;
                _initialRng2 = Rng.Rng2;
                _pristineWorld = CaptureCompetitiveWorld();
                _resetForCountdown = ResetForCountdown;
            }
            catch
            {
                Scene.CloseHeadless();
                throw;
            }
        }

        public void Step(ServerNetwork network, uint tick)
        {
            _network = network;
            Scene.Match.MatchId = network.MatchId;
            int active = 0;
            uint teams = 0;
            for (int slot = 0; slot < 8; slot++)
            {
                ServerPeer? peer = network.Peers[slot];
                PlayerEntity player = PlayerEntity.Players[slot];
                if (_activeConnections[slot] != 0 && _activeConnections[slot] != peer?.Connection.Id)
                {
                    if (Scene.Match.Result == null)
                    {
                        WorldStateCapture.ReleasePlayer(Scene, player);
                        NetScoreboard.ForgetSlot(Scene, slot);
                    }
                    player.ServerDeactivate();
                    _activeConnections[slot] = 0;
                }
                if (peer?.Connection.State == NetConnectionState.Ready && Scene.Match.Result == null)
                {
                    NetScoreboard.ForgetSlot(Scene, slot);
                    GameState.Nicknames[slot] = peer.Name;
                    player.ServerActivate(peer.Connection.Id, peer.Hunter, peer.TeamIndex);
                    _activeConnections[slot] = peer.Connection.Id;
                    peer.Connection.StartPlaying();
                }
                if (peer?.Connection.State == NetConnectionState.Playing)
                {
                    active++;
                    if (peer.TeamIndex < 2) { teams |= 1u << peer.TeamIndex; }
                }
            }
            PlayerEntity.PlayerCount = active;
            bool eligible = active >= (Scene.Match.Rules.MaxPlayers == 1 ? 1 : 2)
                && (!Scene.Match.Rules.Teams || teams == 3);
            MatchPhase previousPhase = Scene.Match.Phase;
            Lifecycle.AdvanceBeforeStep(tick, eligible, _resetForCountdown);
            if (Scene.Match.Phase != previousPhase) { DiscardInputs(network); }
            PublishPhase(network);
            if (Scene.Match.Phase == MatchPhase.Playing)
            {
                using var combatScope = Combat.Enter(tick);
                for (int slot = 0; slot < 8; slot++)
                {
                    ServerPeer? peer = network.Peers[slot];
                    if (peer?.Connection.State != NetConnectionState.Playing) { continue; }
                    InputCommand input = peer.Inputs.Take(tick);
                    Combat.SetCommand(slot, input, peer.Connection.Metrics.SmoothedRttMs);
                    PlayerEntity.Players[slot].ApplyNetworkInput(input);
                }
                Scene.StepHeadlessFrame();
                if (Scene.Match.Phase == MatchPhase.Playing) { Combat.CatchUp.Drain(); }
                else { Combat.CatchUp.Clear(); }
            }
            Lifecycle.ObserveCompletion(tick);
            PublishPhase(network);
            _stateCount = 0;
            for (int slot = 0; slot < 8; slot++)
            {
                if (_activeConnections[slot] == 0) { continue; }
                PlayerEntity player = PlayerEntity.Players[slot];
                player.ModRepairVectors();
                SnapshotPlayer state = player.CaptureServerState();
                _states[_stateCount++] = state;
                if (Scene.Match.Phase == MatchPhase.Playing)
                {
                    Combat.History.Record(tick, player, state.ConnectionId, state.Life);
                }
            }
        }

        private void PublishPhase(ServerNetwork network)
        {
            network.Phase = Scene.Match.Phase;
            network.PhaseRevision = Scene.Match.PhaseRevision;
        }

        private void ResetForCountdown()
        {
            ServerNetwork network = _network ?? throw new InvalidOperationException("Countdown requires a server session.");
            AssertPristineWorld();
            Combat.Reset();
            Scene.Match.ResetCompetitiveState();
            Scene.Match.Flow.ResetProgress();
            for (int slot = 0; slot < 8; slot++) { PlayerEntity.Players[slot].ServerDeactivate(); }
            Rng.SetRng1(_initialRng1);
            Rng.SetRng2(_initialRng2);
            for (int slot = 0; slot < 8; slot++)
            {
                ServerPeer? peer = network.Peers[slot];
                if (peer?.Connection.State == NetConnectionState.Playing)
                {
                    PlayerEntity.Players[slot].ServerActivate(peer.Connection.Id, peer.Hunter, peer.TeamIndex);
                }
            }
            DiscardInputs(network);
            AssertPristineWorld();
            CountdownResets++;
        }

        private static void DiscardInputs(ServerNetwork network)
        {
            foreach (ServerPeer? peer in network.Peers)
            {
                if (peer != null) { peer.Inputs = new ServerInputStream(); }
            }
            foreach (PlayerEntity player in PlayerEntity.Players) { player.Controls.ClearAll(); }
        }

        private WorldRecord[] CaptureCompetitiveWorld()
        {
            _pristineCapture.Capture(Scene, Scene.Match.MatchId == 0 ? 1u : Scene.Match.MatchId, 0, 0);
            var records = new List<WorldRecord>();
            foreach (WorldRecord record in _pristineCapture.Records)
            {
                if (record.Kind is WorldRecordKind.Item or WorldRecordKind.Spawner or WorldRecordKind.Node or WorldRecordKind.Flag)
                {
                    records.Add(record);
                }
            }
            return records.ToArray();
        }

        internal void AssertPristineWorld()
        {
            if (Scene.FrameCount != 0 || Scene.LiveFrames != 0
                || !CaptureCompetitiveWorld().AsSpan().SequenceEqual(_pristineWorld))
            {
                throw new InvalidOperationException("The waiting world changed before the competitive reset.");
            }
        }

        public void Dispose() => Scene.CloseHeadless();
    }
}
