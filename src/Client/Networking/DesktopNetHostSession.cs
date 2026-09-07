using System;
using System.Threading;
using MphRead.Entities;

namespace MphRead.Mods.Network
{
    /// <summary>A local authoritative server owns a separate process; its player joins over UDP.</summary>
    internal sealed class DesktopNetHostSession : ILocalMatchHost
    {
        private readonly object _gate = new();
        private ServerProcess? _server;
        private CancellationTokenSource? _startup;
        private long _generation;
        public bool Running { get { lock (_gate) { return _server?.Running == true; } } }
        public string? LastError { get; private set; }

        /// <param name="listing">An explicitly selected directory, or null to keep the match unlisted.</param>
        public bool StartAndJoin(bool friendlyFire, int port, string playerName, Hunter hunter,
            string roomKey, GameMode mode, float timeLimit, int pointGoal,
            int maxPlayers = PlayerEntity.SlotCapacity,
            (string Host, int Port, string Name)? listing = null, bool practice = false,
            int? botMinimumParticipants = null, int botSkill = 1,
            MatchRules? configuredRules = null, LobbyPolicy? lobbyPolicy = null,
            int maxObservers = 4)
        {
            var cancel = new CancellationTokenSource();
            long generation;
            ServerProcess? previous;
            lock (_gate)
            {
                _startup?.Cancel();
                previous = _server;
                _server = null;
                generation = ++_generation;
                _startup = cancel;
                LastError = null;
            }
            try
            {
                previous?.Dispose();
                if (!Paths.AllPaths.TryGetValue(Paths.MphKey, out string? data) || String.IsNullOrWhiteSpace(data))
                {
                    throw new ProgramException("Hosting requires extracted game data. Select your game files in the launcher first.");
                }
                MapRotation rotation = MapRotation.SingleMatch(roomKey, mode, timeLimit, pointGoal,
                    configuredRules?.ObjectiveTimeGoal is { } objective
                        ? (float)objective.TotalSeconds : null);
                ServerProcess server;
                if (configuredRules is not null || botMinimumParticipants is not null
                    || lobbyPolicy is not null || maxObservers != 4)
                {
                    MatchRules rules = configuredRules
                        ?? rotation.Current.ToMatchRules(maxPlayers, friendlyFire);
                    int minimum = botMinimumParticipants ?? (practice ? Math.Min(4, maxPlayers) : 0);
                    var options = new HostedProcessOptions(rules,
                        lobbyPolicy ?? LobbyPolicy.PrivateHosted,
                        minimum, botSkill, maxObservers, 0, Guid.NewGuid(),
                        System.Net.IPAddress.Loopback);
                    server = ServerProcess.StartConfigured(data, Paths.MphKey, rotation,
                        port, maxPlayers, friendlyFire, listing, cancel.Token, practice, options);
                }
                else
                {
                    server = ServerProcess.Start(data, Paths.MphKey, rotation,
                        port, maxPlayers, friendlyFire, listing, cancel.Token, practice);
                }
                bool current;
                lock (_gate)
                {
                    current = generation == _generation;
                    if (current) { _server = server; }
                }
                if (!current)
                {
                    server.Dispose();
                    return false;
                }
                Guid ownerCapability = server.TakeOwnerCapability();
                if (!NetLaunch.Join("127.0.0.1", server.Port, playerName, hunter,
                    cancel: cancel.Token, ownerCapability: ownerCapability))
                {
                    throw new ProgramException(String.IsNullOrWhiteSpace(NetLaunch.LastJoinError)
                        ? "The local server started, but the player could not join it."
                        : NetLaunch.LastJoinError);
                }
                lock (_gate) { return generation == _generation; }
            }
            catch (Exception ex)
            {
                bool current;
                lock (_gate)
                {
                    current = generation == _generation;
                    if (current) { LastError = ex.Message; }
                }
                if (current) { Stop(generation); }
                return false;
            }
            finally
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_startup, cancel)) { _startup = null; }
                    cancel.Dispose();
                }
            }
        }

        public void Stop() => Stop(null);

        private void Stop(long? expectedGeneration)
        {
            ServerProcess? server;
            long stoppedGeneration;
            lock (_gate)
            {
                if (expectedGeneration.HasValue && expectedGeneration.Value != _generation) { return; }
                stoppedGeneration = ++_generation;
                _startup?.Cancel();
                _startup = null;
                server = _server;
                _server = null;
            }
            try { server?.Dispose(); }
            catch (Exception ex)
            {
                string error = "Could not stop the local server: " + ex.Message;
                lock (_gate)
                {
                    if (stoppedGeneration == _generation) { LastError = error; }
                }
                Console.Error.WriteLine("[net] " + error);
            }
        }
    }
}
