using System;
using MphRead.Entities;

namespace MphRead.Mods.Network
{
    public sealed class ServerSimulation : IDisposable
    {
        private readonly ulong[] _activeConnections = new ulong[8];
        private readonly SnapshotPlayer[] _states = new SnapshotPlayer[8];
        private int _stateCount;
        public Scene Scene { get; }
        public ServerCombat Combat { get; }
        public ReadOnlySpan<SnapshotPlayer> States => _states.AsSpan(0, _stateCount);

        public ServerSimulation(RotationEntry entry, bool lagCompEnabled = true, bool projectileCatchUpEnabled = true)
        {
            Combat = new ServerCombat(lagCompEnabled, projectileCatchUpEnabled);
            Scene = Scene.CreateHeadless();
            try
            {
                Scene.LoadServerRoom(entry.RoomKey, entry.Mode, players: 8, roomPlayerCount: NetLaunch.RoomPlayerCount);
                WorldStateCapture.ValidateRoom(Scene);
                if (!ServerContentPack.HasObjectives(Scene, entry.Mode))
                {
                    throw new ProgramException($"{entry.RoomKey}/{entry.Mode} has no required objectives in the multiplayer entity layout.");
                }
                foreach (PlayerEntity player in Scene.GetPlayerEntities()) { player.ServerDeactivate(); }
                PlayerEntity.PlayerCount = 0;
                GameState.MatchTime = entry.TimeLimit > 0 ? entry.TimeLimit : -1;
                GameState.PointGoal = entry.PointGoal;
                GameState.CaptureSetupRules();
            }
            catch
            {
                Scene.CloseHeadless();
                throw;
            }
        }

        public void Step(ServerNetwork network, uint tick)
        {
            using var combatScope = Combat.Enter(tick);
            int active = 0;
            for (int slot = 0; slot < 8; slot++)
            {
                ServerPeer? peer = network.Peers[slot];
                PlayerEntity player = PlayerEntity.Players[slot];
                if (_activeConnections[slot] != 0 && _activeConnections[slot] != peer?.Connection.Id)
                {
                    WorldStateCapture.ReleasePlayer(Scene, player);
                    player.ServerDeactivate();
                    NetScoreboard.ForgetSlot(slot);
                    _activeConnections[slot] = 0;
                }
                if (peer?.Connection.State == NetConnectionState.Ready)
                {
                    NetScoreboard.ForgetSlot(slot);
                    GameState.Nicknames[slot] = peer.Name;
                    player.ServerActivate(peer.Connection.Id, peer.Hunter, GameState.Teams ? slot % 2 : slot);
                    _activeConnections[slot] = peer.Connection.Id;
                    peer.Connection.StartPlaying();
                }
                if (peer?.Connection.State == NetConnectionState.Playing)
                {
                    InputCommand input = peer.Inputs.Take(tick);
                    Combat.SetCommand(slot, input, peer.Connection.Metrics.SmoothedRttMs);
                    player.ApplyNetworkInput(input);
                    active++;
                }
            }
            PlayerEntity.PlayerCount = active;
            if (active > 0)
            {
                // A lone player may move while waiting for opponents. The
                // survival winner and match clock require at least two.
                Scene.StepHeadlessFrame(advanceMatch: active >= 2);
            }
            Combat.CatchUp.Drain();
            _stateCount = 0;
            for (int slot = 0; slot < 8; slot++)
            {
                if (_activeConnections[slot] != 0)
                {
                    PlayerEntity player = PlayerEntity.Players[slot];
                    player.ModRepairVectors();
                    SnapshotPlayer state = player.CaptureServerState();
                    _states[_stateCount++] = state;
                    // History tick T describes the same completed frame as
                    // snapshot T, including any death, respawn or form change.
                    Combat.History.Record(tick, player, state.ConnectionId, state.Life);
                }
            }
        }

        public void Dispose() => Scene.CloseHeadless();
    }
}
