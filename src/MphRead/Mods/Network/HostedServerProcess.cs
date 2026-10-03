using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Network
{
    /// <summary>
    /// A hosted lobby in its own server process.
    ///
    /// NetSession is intentionally static, so an in-process HostPool can only
    /// simulate the process's primary match. The old fallback made the first
    /// joining player authoritative for every extra lobby, giving that player
    /// zero-latency hit resolution. A child process gives each hosted match a
    /// separate NetSession and therefore a server authority of its own.
    /// </summary>
    internal sealed class HostedServerProcess : IDisposable
    {
        private readonly Process _process;
        private int _players;
        private readonly string? _library;
        private double _nextProbe;
        private Task<ServerStatus>? _probe;
        private double _lastResponsiveAt;
        private bool _everResponsive;
        private const double ProbeIntervalSeconds = 5.0;
        private const double UnresponsiveSeconds = 20.0;

        public int Port { get; }
        public bool EverOccupied { get; private set; }
        public bool Running
        {
            get
            {
                try { return !_process.HasExited; }
                catch (InvalidOperationException) { return false; }
            }
        }

        /// <summary>
        /// A child that used to answer status probes but has stopped doing so.
        /// Startup is deliberately exempt: cold map construction may take much
        /// longer than a probe timeout before the listener reaches its loop.
        /// </summary>
        public bool Unresponsive(double now) =>
            _everResponsive && now - _lastResponsiveAt >= UnresponsiveSeconds;

        private HostedServerProcess(Process process, int port)
        {
            _process = process;
            _library = LocalServer.HostedLibrary;
            Port = port;
        }

        public static HostedServerProcess? Start(int port, in HostRequestPacket request,
            string name, string masterHost, int masterPort, bool listed,
            Guid ownerToken, out string reason, HostedMapPreparation? hostedMaps = null)
        {
            try
            {
                HostedMapRequests.Validate(request);
                if (request.RequiresMapPreparation)
                {
                    if (hostedMaps == null)
                        throw new InvalidDataException("The host did not prepare the requested Community rotation.");

                    foreach (HostRotationEntry entry in HostedMapRequests.RequestedMaps(request)
                        .Where(entry => entry.IsCustom))
                    {
                        HostedMapArchive? archive = hostedMaps.Find(entry.RoomKey, entry.PackageHash);
                        if (archive == null || !File.Exists(archive.PackagePath)
                            || archive.Identity.PackageHash != entry.PackageHash)
                        {
                            throw new InvalidDataException(
                                "The host does not have the exact requested package for " + entry.RoomKey + ".");
                        }
                    }

                    if (request.MapIdentity.IsCustom)
                    {
                        HostedMapArchive? first = hostedMaps.Find(
                            request.RoomKey, request.MapIdentity.PackageHash);
                        if (first == null
                            || !first.Identity.Matches(request.MapIdentity.Content(request.RoomKey)))
                        {
                            throw new InvalidDataException("The hosted first package does not match the request.");
                        }
                    }
                }
                else if (request.MapIdentity != NetworkMapIdentity.ForRoom(request.RoomKey))
                {
                    throw new InvalidDataException("The hosted built-in map identity changed.");
                }
            }
            catch (Exception ex) { reason = ex.Message; return null; }
            GameMode mode = Enum.IsDefined(typeof(GameMode), request.Mode)
                ? (GameMode)request.Mode
                : GameMode.Battle;
            IReadOnlyList<(string RoomKey, GameMode Mode)> maps;
            if (request.Rotation != null && request.Rotation.Count > 0)
            {
                maps = request.Rotation
                    .Select(entry => (entry.RoomKey, entry.Mode))
                    .ToArray();
            }
            else
            {
                maps = new[] { (request.RoomKey, mode) };
            }

            // Built-in-only rotations need the same private library as custom
            // rotations. A null preparation makes LocalServer inherit the host's
            // entire map catalog and validate unrelated packages during startup.
            hostedMaps ??= new HostedMapPreparation(Array.Empty<HostedMapArchive>());
            int started = LocalServer.Start(name, maps,
                Math.Clamp((int)request.MaxPlayers, 2, MphRead.Entities.PlayerEntity.SlotCapacity),
                request.TimeLimit, request.PointGoal,
                masterHost, masterPort, listed,
                CancellationToken.None,
                lobby: request.Policy == ServerSessionPolicy.Lobby,
                requestedPort: port,
                ownerToken: ownerToken,
                format: request.Format,
                requireReady: request.RequireReady,
                allowJoinInProgress: request.AllowJoinInProgress,
                waitUntilReady: false, requiredMap: request.MapIdentity, hostedMaps: hostedMaps);
            Process? process = started < 0 ? null : LocalServer.DetachRunning();
            if (started < 0 || process == null)
            {
                LocalServer.CleanupHostedLibrary(LocalServer.HostedLibrary);
                reason = LocalServer.LastError ?? $"could not start server on port {port}";
                return null;
            }
            reason = "";
            return new HostedServerProcess(process, started);
        }

        /// <summary>
        /// Read occupancy from the child's ordinary status endpoint without
        /// ever blocking the parent authority loop. One background probe may
        /// be outstanding at a time; the server thread only consumes completed
        /// results and starts the next probe.
        /// </summary>
        public int ProbePlayers(double now, bool force = false)
        {
            if (!Running)
            {
                _players = 0;
                return 0;
            }

            Task<ServerStatus>? probe = _probe;
            if (probe is { IsCompleted: true })
            {
                _probe = null;
                try
                {
                    ServerStatus status = probe.GetAwaiter().GetResult();
                    if (status.Online)
                    {
                        _players = status.Players;
                        _lastResponsiveAt = now;
                        _everResponsive = true;
                        if (_players > 0) EverOccupied = true;
                    }
                }
                catch (Exception)
                {
                    // A failed health probe is not a match failure. Reaping
                    // uses the bounded unresponsive grace below.
                }
            }

            if (_probe == null && (force || now >= _nextProbe))
            {
                _nextProbe = now + ProbeIntervalSeconds;
                int port = Port;
                _probe = Task.Run(() => NetStatus.Query("127.0.0.1", port,
                    allowJoinProbe: false, timeoutMs: 150));
            }
            return _players;
        }

        public void Dispose()
        {
            bool stopped = false;
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit(2000);
                }
                stopped = _process.HasExited;
            }
            catch (Exception)
            {
                // Already gone, or the operating system reaped it first.
            }
            finally
            {
                _process.Dispose();
                if (stopped) LocalServer.CleanupHostedLibrary(_library);
            }
        }
    }
}
