using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
namespace MphRead.Mods.Network
{
    /// <summary>Client-owned handle to a separately executing dedicated server.</summary>
    public sealed class ServerProcess : IDisposable
    {
        private readonly ServerProcessHost _host;
        private ServerProcess(ServerProcessHost host) => _host = host;
        public const int StartupTimeoutMs = ServerProcessHost.StartupTimeoutMs;
        public int Port => _host.Port;
        public int PeerCount => _host.PeerCount;
        public bool EverOccupied => _host.EverOccupied;
        public bool Running => _host.Running;
        public int? ExitCode => _host.ExitCode;
        public bool WasKilled => _host.WasKilled;
        public string RecentLog => _host.RecentLog;
        public static ServerProcess Start(string data, string version, MapRotation rotation,
            int port, int maxPlayers, bool friendlyFire,
            (string Host, int Port, string Name)? listing = null, CancellationToken cancel = default)
            => new(ServerProcessHost.Start(data, version, rotation, port, maxPlayers, friendlyFire, listing, cancel));
        public static async Task<ServerProcess> StartAsync(string data, string version, MapRotation rotation,
            int port, int maxPlayers, bool friendlyFire,
            (string Host, int Port, string Name)? listing = null, CancellationToken cancel = default)
            => new(await ServerProcessHost.StartAsync(data, version, rotation, port, maxPlayers, friendlyFire, listing, cancel).ConfigureAwait(false));
        internal static ProcessStartInfo CreateStartInfo(string mapDirectory) => ServerProcessHost.CreateStartInfo(mapDirectory);
        public void Dispose() => _host.Dispose();
    }
}
