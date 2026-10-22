using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Threading.Tasks;

namespace MphRead.Mods.Network
{
    /// <summary>
    /// The matches a machine is running on somebody else's behalf, on ports
    /// of its own.
    ///
    /// Lifted out of <see cref="MasterServer"/>, which is where it used to
    /// live and where it did not belong. Starting a match on a box is a
    /// property of *being that box*, not of being the directory: a player who
    /// wants their game hosted in Tokyo is asking Tokyo, and only a process
    /// running there can spawn a server there. Tying it to the directory
    /// meant either one region could host, or every region had to run a
    /// second directory that listed nothing and existed purely to be asked --
    /// which is a component invented to satisfy a layering mistake.
    ///
    /// So an ordinary <see cref="DedicatedServer"/> carries one of these and
    /// answers <see cref="PacketType.HostRequest"/> on its own port. There is
    /// still exactly one directory in the world; it keeps the list, and every
    /// server on that list can be asked to open a game beside itself.
    ///
    /// Each match it starts runs in a child server process. NetSession is
    /// static, so an extra match cannot share this process's simulation, but
    /// making a player authoritative is not an acceptable fallback: it gives
    /// that player zero-latency hit resolution. One process per hosted match
    /// gives every lobby its own server authority instead.
    /// </summary>
    public sealed class HostPool
    {
        private sealed class Hosted
        {
            public HostedServerProcess Process = null!;
            public int Port;
            public string Name = "";
            public double StartedAt;
            public bool ReadyLogged;
            /// <summary>When it last had anybody in it, so an abandoned game can be reaped.</summary>
            public double LastOccupied;
        }

        private readonly List<Hosted> _hosted = new();
        private readonly Dictionary<int, double> _cooling = new();
        private int _first;
        private int _last = -1;
        private double _parentSampleAt = -1;
        private double _parentCpuSeconds, _parentCpuCores;
        private bool _parentCpuSampled;

        /// <summary>Where this pool's log lines go, so a server and a directory read alike.</summary>
        public Action<string> Log { get; init; } = _ => { };

        /// <summary>
        /// Directory endpoint for hosted children, or null to keep them
        /// private. This is configuration, not a reporter object: constructing
        /// a temporary MasterReporter just to read its Host/Port duplicated
        /// ownership and made disposal ambiguous.
        /// </summary>
        public Func<(string Host, int Port)?>? ListingTarget { get; init; }

        /// <summary>Told when a game stops, so its listing can be dropped at once.</summary>
        public Action<int>? OnStopped { get; init; }

        /// <summary>See <see cref="MasterServer"/> for why a freed port waits.</summary>
        private const double PortCooldownSeconds = 5;

        /// <summary>How long a game may sit empty before anybody has ever joined.</summary>
        private const double StartupSeconds = 180;

        /// <summary>And how long once it has been played and everyone has left.</summary>
        private const double EmptySeconds = 45;

        private static int ConfiguredPositiveInt(string name)
            => int.TryParse(Environment.GetEnvironmentVariable(name), out int n) && n > 0 ? n : 0;

        private static double CpuBudget()
        {
            string? setting = Environment.GetEnvironmentVariable("PROJECT_PRIME_HOST_MAX_CPU_CORES");
            if (double.TryParse(setting, NumberStyles.Float, CultureInfo.InvariantCulture, out double cores)
                && double.IsFinite(cores) && cores > 0)
                return cores;
            // ProcessorCount follows the process/container CPU quota on .NET.
            return Math.Max(.85, Environment.ProcessorCount * .85);
        }

        private void SampleParentCpu(double now)
        {
            try
            {
                using Process parent = Process.GetCurrentProcess();
                double cpuSeconds = parent.TotalProcessorTime.TotalSeconds;
                if (_parentSampleAt >= 0 && now - _parentSampleAt >= .25)
                {
                    _parentCpuCores = Math.Max(0, (cpuSeconds - _parentCpuSeconds) / (now - _parentSampleAt));
                    _parentCpuSampled = true;
                }
                _parentSampleAt = now;
                _parentCpuSeconds = cpuSeconds;
            }
            catch (InvalidOperationException) { _parentCpuSampled = false; }
        }

        /// <summary>
        /// Fail closed for configured resource ceilings without claiming that
        /// idle-process RSS proves an active match's eventual memory cost.
        /// Default CPU headroom is advisory to measured samples; memory limits
        /// require an operator budget because container/free memory varies.
        /// </summary>
        private bool CanAdmit(out string reason)
        {
            int maxMatches = ConfiguredPositiveInt("PROJECT_PRIME_HOST_MAX_MATCHES");
            if (maxMatches > 0 && _hosted.Count >= maxMatches)
            {
                reason = "hosted match capacity limit reached";
                return false;
            }

            double busyCores = _parentCpuSampled ? _parentCpuCores : 0;
            int unsampled = 0;
            bool sampled = _parentCpuSampled;
            foreach (Hosted hosted in _hosted)
            {
                if (hosted.Process.HasCpuSample)
                {
                    busyCores += hosted.Process.CpuCoreUsage;
                    sampled = true;
                }
                else unsampled++;
            }
            // Prevent a burst of unsampled cold children from bypassing the
            // CPU admission decision before the first one-second poll.
            if (unsampled >= Math.Max(2, Environment.ProcessorCount))
            {
                reason = "new hosted matches are warming up; retry shortly";
                return false;
            }
            if (sampled && busyCores >= CpuBudget())
            {
                reason = "host CPU headroom exhausted; retry shortly";
                return false;
            }

            int budgetMiB = ConfiguredPositiveInt("PROJECT_PRIME_HOST_MAX_TOTAL_RSS_MB");
            if (budgetMiB > 0)
            {
                const long mib = 1024L * 1024;
                long resident = 0;
                long estimatedNewChild = 256 * mib;
                try
                {
                    using Process parent = Process.GetCurrentProcess();
                    resident = parent.WorkingSet64;
                }
                catch (InvalidOperationException) { resident = 0; }
                foreach (Hosted hosted in _hosted)
                {
                    long childResident = hosted.Process.ResidentBytes;
                    resident += childResident;
                    estimatedNewChild = Math.Max(estimatedNewChild, childResident);
                }
                // Conservative startup estimate plus 128 MiB spare for bursts.
                if (resident + estimatedNewChild + 128 * mib >= (long)budgetMiB * mib)
                {
                    reason = "host memory headroom exhausted; retry shortly";
                    return false;
                }
            }
            reason = "";
            return true;
        }

        public void SetPorts(int first, int last)
        {
            _first = first;
            _last = last;
        }

        public bool CanHost => _last >= _first && _first > 0;

        public int Count => _hosted.Count;
        public string? WeaponStoreDirectory { get; private set; }
        public void SetWeaponStore(string? directory)
        {
            if (directory == null) { WeaponStoreDirectory = null; return; }
            if (String.IsNullOrWhiteSpace(directory) || directory.Length > 4096 || System.Linq.Enumerable.Any(directory, char.IsControl))
                throw new ArgumentException("Expected a bounded approved weapon store directory.", nameof(directory));
            _ = new CustomWeapons.Packages.WeaponPackageStore(directory);
            WeaponStoreDirectory = System.IO.Path.GetFullPath(directory);
        }

        public string Describe() => CanHost
            ? $"can start games on ports {_first}-{_last} for players who cannot open one of their own"
            : "not starting games for anybody (no host port range)";

        /// <summary>
        /// Start a match for somebody, and say where it is listening.
        /// </summary>
        public HostReplyPacket Start(HostRequestPacket request, IPEndPoint asker, double now, HostedMapPreparation? hostedMaps = null)
            => StartCore(request, asker, now, hostedMaps, null);

        internal HostReplyPacket StartPrepared(HostRequestPacket request, IPEndPoint asker, double now, PreparedHostRequest prepared)
            => StartCore(request, asker, now, prepared.Maps, prepared.Weapon);

        private HostReplyPacket StartCore(HostRequestPacket request, IPEndPoint asker, double now,
            HostedMapPreparation? hostedMaps, PreparedLocalWeaponPackage? weapon)
        {
            if (request.RequiresWeaponPreparation
                && (weapon == null || !weapon.Matches(WeaponStoreDirectory, request.WeaponArchiveHash)))
                return new() { Reason = "The exact weapon package has not been prepared by this host." };
            if (!request.RequiresWeaponPreparation && weapon != null)
                return new() { Reason = "Unexpected weapon package preparation." };
            // No filesystem access. Verify that the worker's lease is still active
            // before reaping or selecting a port; keep it across child creation.
            CustomWeapons.Packages.WeaponPackageLease? launchLease;
            try { launchLease = weapon?.Retain(); }
            catch (ObjectDisposedException) { return new() { Reason = "Weapon package preparation expired." }; }
            using var retained = launchLease;
            // A child can die between regular server-loop reaps and this
            // request. Drop those dead reservations before choosing a port so
            // a retry is not refused by a process that no longer exists.
            ReapExited(now);
            if (!CanAdmit(out string capacityReason))
                return new HostReplyPacket { Reason = capacityReason };
            // Do not treat a public IP address as a player identity. Multiple
            // people behind one NAT legitimately share it, so replacing an
            // empty game merely because the next request came from that IP can
            // destroy somebody else's lobby. Abandoned games are bounded by
            // the normal startup/empty reaper instead.
            int port = FreePort(now);
            if (port < 0)
            {
                return new HostReplyPacket
                {
                    Reason = $"all {_last - _first + 1} game slots are busy"
                };
            }
            GameMode mode = Enum.IsDefined(typeof(GameMode), request.Mode)
                ? (GameMode)request.Mode
                : GameMode.Battle;
            string name = request.ServerName.Length > 0 ? request.ServerName : "Hosted game";
            Guid ownerToken = request.Policy == ServerSessionPolicy.Lobby || request.RequiresWeaponPreparation
                ? new Guid(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16))
                : Guid.Empty;
            (string Host, int Port)? listing = ListingTarget?.Invoke();
            HostedServerProcess? process = HostedServerProcess.Start(port, request, name,
                listing?.Host ?? NetMasterConfig.DefaultHost,
                listing?.Port ?? NetMasterConfig.DefaultPort,
                listed: listing.HasValue, ownerToken, out string reason, hostedMaps, weapon);
            if (process == null)
            {
                return new HostReplyPacket { Reason = reason };
            }
            var entry = new Hosted
            {
                Process = process,
                Port = port,
                Name = name,
                StartedAt = now,
                LastOccupied = now
            };
            _hosted.Add(entry);
            int mapCount = request.Rotation != null && request.Rotation.Count > 0
                ? request.Rotation.Count : 1;
            Log($"started \"{name}\" on port {port} for {asker.Address} "
                + $"({request.RoomKey}, {mode}, {mapCount} map(s), server authority)");
            return new HostReplyPacket { Started = true, Port = (ushort)port,
                Reason = "", OwnerToken = ownerToken };
        }

        private int FreePort(double now)
        {
            for (int port = _first; port <= _last; port++)
            {
                bool taken = false;
                for (int i = 0; i < _hosted.Count; i++)
                {
                    if (_hosted[i].Port == port)
                    {
                        taken = true;
                        break;
                    }
                }
                if (taken)
                {
                    continue;
                }
                if (_cooling.TryGetValue(port, out double freedAt))
                {
                    if (now - freedAt < PortCooldownSeconds)
                    {
                        continue;
                    }
                    _cooling.Remove(port);
                }
                if (!LocalServer.PortAvailable(port))
                {
                    continue;
                }
                return port;
            }
            return -1;
        }

        private void ReapExited(double now)
        {
            for (int i = _hosted.Count - 1; i >= 0; i--)
            {
                Hosted entry = _hosted[i];
                if (!entry.Process.Running)
                {
                    Stop(entry, "server process exited", now);
                }
            }
        }

        /// <summary>
        /// Shut down games nobody is playing. Without this a machine left
        /// running for a week has every port allocated to a match that ended
        /// on Tuesday.
        /// </summary>
        public void Reap(double now)
        {
            SampleParentCpu(now);
            for (int i = _hosted.Count - 1; i >= 0; i--)
            {
                Hosted entry = _hosted[i];
                if (!entry.Process.Running)
                {
                    Stop(entry, "server process exited", now);
                    continue;
                }
                entry.Process.SampleCpu(now);
                int players = entry.Process.ProbePlayers(now);
                if (!entry.ReadyLogged && entry.Process.Responsive)
                {
                    entry.ReadyLogged = true;
                    Log($"hosted child {entry.Port} ready in {now - entry.StartedAt:0.00}s, "
                        + $"RSS {entry.Process.ResidentBytes / (1024.0 * 1024.0):0.0} MiB");
                }
                if (entry.Process.Unresponsive(now))
                {
                    Stop(entry, "server stopped answering status probes", now);
                    continue;
                }
                if (players > 0)
                {
                    entry.LastOccupied = now;
                    continue;
                }
                bool played = entry.Process.EverOccupied;
                double grace = played ? EmptySeconds : StartupSeconds;
                if (now - entry.LastOccupied > grace)
                {
                    Stop(entry, played ? "everyone left" : "nobody joined", now);
                }
            }
        }

        public void StopAll(string why)
        {
            // Unlike ordinary reaping, service shutdown has to wait for the
            // owned children to finish. Signal all of them before waiting.
            var pending = new List<Task>(_hosted.Count);
            for (int i = _hosted.Count - 1; i >= 0; i--)
                pending.Add(Stop(_hosted[i], why));
            if (pending.Count > 0 && !Task.WaitAll(pending.ToArray(), 12000))
                Log("hosted child shutdown exceeded grace; workers will force-stop");
        }

        private Task Stop(Hosted entry, string why, double now = 0)
        {
            Log($"stopping \"{entry.Name}\" on port {entry.Port}: {why}");
            Task shutdown = entry.Process.ShutdownGracefully();
            _hosted.Remove(entry);
            _cooling[entry.Port] = now;
            OnStopped?.Invoke(entry.Port);
            return shutdown;
        }
    }
}
