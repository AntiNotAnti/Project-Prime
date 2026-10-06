using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher
{
    /// <summary>
    /// One directory row after this machine has asked that server directly.
    /// Presentation-neutral: no Avalonia types and no assumptions about how a
    /// browser lays rows out.
    /// </summary>
    public readonly record struct ServerBrowserEntry(
        MasterListing Listing,
        ServerStatus Status)
    {
        public string Name => Status.ServerName.Length > 0
            ? Status.ServerName
            : Listing.ServerName.Length > 0 ? Listing.ServerName : Listing.Endpoint;

        public string Endpoint => Listing.Endpoint;
        public bool Live => Status.Online;
        public bool Compatible => Status.Protocol == 0
            || Status.Protocol == NetConfig.ProtocolVersion;
    }

    public readonly record struct ServerDiscoveryResult(
        bool DirectoryAnswered,
        int Listed,
        int Live,
        string Message);

    public readonly record struct OnlineJoinResult(
        bool Joined,
        LaunchPlan Plan,
        string Error);

    public readonly record struct QuickPlaySearchResult(
        bool Found,
        ServerBrowserEntry Entry,
        ServerDiscoveryResult Discovery,
        string Message);

    public readonly record struct OnlinePopulationResult(
        bool DirectoryAnswered,
        int Players,
        int Servers);

    public readonly record struct SocialLobbyVerification(
        bool Verified,
        ServerBrowserEntry Entry,
        string Error);

    /// <summary>
    /// Application-layer server discovery and joining shared by every launcher
    /// presentation. The callbacks deliberately run off the UI thread; a GUI
    /// renderer marshals them onto its own dispatcher.
    /// </summary>
    public static class ServerBrowserService
    {
        internal const int MaximumConcurrentProbes = 8;
        public static async Task<ServerDiscoveryResult> DiscoverAsync(
            Action<ServerBrowserEntry>? onEntry = null,
            CancellationToken cancellationToken = default)
        {
            string host = LauncherPrefs.MasterHost;
            int port = LauncherPrefs.MasterPort;
            MasterListResult result;
            try
            {
                result = await Task.Run(
                    () => NetMasterClient.Query(host, port, cancellationToken: cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return new(false, 0, 0, "Cancelled.");
            }

            if (cancellationToken.IsCancellationRequested)
                return new(false, 0, 0, "Cancelled.");

            if (!result.Answered)
            {
                return new(false, 0, 0,
                    $"The directory at {host}:{port} did not answer.");
            }

            IReadOnlyList<MasterListing> listed =
                result.Servers ?? Array.Empty<MasterListing>();
            if (listed.Count == 0)
            {
                return new(true, 0, 0,
                    "The directory is online and has no servers listed.");
            }

            var entries = new ServerBrowserEntry[listed.Count];
            try
            {
                await ProbeListingsAsync(listed, async (index, listing, token) =>
                {
                    ServerStatus status = await Task.Run(() => NetStatus.Query(listing.Address, listing.Port,
                        allowJoinProbe: false, cancellationToken: token), token);
                    if (!status.Online)
                    {
                        // Confirm a dead row once: a lost reply must not erase
                        // a healthy server. This wait is cancellable as well.
                        await Task.Delay(100, token);
                        status = await Task.Run(() => NetStatus.Query(listing.Address, listing.Port,
                            allowJoinProbe: false, timeoutMs: 350, cancellationToken: token), token);
                    }
                    token.ThrowIfCancellationRequested();
                    var entry = new ServerBrowserEntry(listing, status);
                    entries[index] = entry;
                    if (entry.Live) onEntry?.Invoke(entry);
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return new(true, listed.Count, 0, "Cancelled.");
            }

            int live = entries.Count(e => e.Live);
            return new(true, listed.Count, live,
                live == 1
                    ? "1 server answered."
                    : $"{live} of {listed.Count} servers answered.");
        }

        // One worker per permitted outstanding probe, rather than one task/socket
        // per directory row. The callback also makes the bound independently testable.
        internal static Task ProbeListingsAsync(IReadOnlyList<MasterListing> listed,
            Func<int, MasterListing, CancellationToken, Task> probe, CancellationToken token)
        {
            int next = -1;
            async Task Worker()
            {
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    int index = Interlocked.Increment(ref next);
                    if (index >= listed.Count) return;
                    await probe(index, listed[index], token);
                }
            }
            return Task.WhenAll(Enumerable.Range(0, Math.Min(MaximumConcurrentProbes, listed.Count))
                .Select(_ => Worker()));
        }

        internal static bool CanQuickPlay(ServerBrowserEntry entry) => entry.Live && entry.Compatible
            && (entry.Status.MaxPlayers <= 0 || entry.Status.Players < entry.Status.MaxPlayers)
            && (entry.Status.Legacy || entry.Status.Phase == SessionPhase.Lobby
                || (entry.Status.Phase == SessionPhase.InMatch && entry.Status.AllowJoinInProgress));

        public static async Task<OnlinePopulationResult> CountOnlinePlayersAsync(
            CancellationToken cancellationToken = default)
        {
            var live = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            ServerDiscoveryResult discovery = await DiscoverAsync(entry =>
            {
                if (entry.Live && entry.Compatible)
                    live[entry.Endpoint] = Math.Max(0, entry.Status.Players);
            }, cancellationToken);

            return new OnlinePopulationResult(
                discovery.DirectoryAnswered,
                live.Values.Sum(),
                live.Count);
        }

        public static async Task<QuickPlaySearchResult> FindBestAsync(
            CancellationToken cancellationToken = default)
        {
            var found = new ConcurrentBag<ServerBrowserEntry>();
            ServerDiscoveryResult discovery = await DiscoverAsync(
                entry => found.Add(entry), cancellationToken);

            if (cancellationToken.IsCancellationRequested)
                return new(false, default, discovery, "Cancelled.");

            ServerBrowserEntry[] candidates = found
                .Where(CanQuickPlay)
                .OrderBy(entry => entry.Status.Latency < 0
                    ? Int32.MaxValue : entry.Status.Latency)
                .ThenByDescending(entry => entry.Status.Players)
                .ToArray();

            if (candidates.Length == 0)
            {
                return new(false, default, discovery,
                    discovery.DirectoryAnswered
                        ? "No compatible open server answered."
                        : discovery.Message);
            }

            ServerBrowserEntry best = candidates[0];
            string ping = best.Status.Latency >= 0
                ? $"{best.Status.Latency} ms"
                : "latency unavailable";
            return new(true, best, discovery,
                $"{best.Name} · {ping}");
        }

        public static Task<ServerStatus> ProbeAsync(string host, int port,
            bool allowJoinProbe = true, CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return NetStatus.Query(host, port, allowJoinProbe, cancellationToken: cancellationToken);
            }, cancellationToken);
        }

        /// <summary>
        /// Find the public directory row for the exact authority this client is
        /// already connected to. The client may itself be using 127.0.0.1 for a
        /// locally-hosted lobby; the directory row is the public endpoint a
        /// friend can actually dial.
        /// </summary>
        public static async Task<SocialLobbyVerification> FindListedLobbyAsync(
            ulong authorityEpoch, CancellationToken cancellationToken = default)
        {
            if (authorityEpoch == 0)
                return new(false, default, "The current lobby has no authority identity.");

            ServerBrowserEntry match = default;
            int matches = 0;
            ServerDiscoveryResult discovery = await DiscoverAsync(entry =>
            {
                if (!entry.Live || !entry.Compatible
                    || entry.Status.AuthorityEpoch != authorityEpoch
                    || !entry.Status.LobbyEnabled
                    || entry.Status.Phase != SessionPhase.Lobby)
                    return;
                Interlocked.Increment(ref matches);
                match = entry;
            }, cancellationToken);

            if (cancellationToken.IsCancellationRequested)
                return new(false, default, "Lobby verification cancelled.");
            if (!discovery.DirectoryAnswered)
                return new(false, default, "The public server directory did not answer.");
            if (matches == 0)
                return new(false, default,
                    "This lobby is not currently listed in the public server directory.");
            if (matches > 1)
                return new(false, default,
                    "The directory returned more than one server with this authority identity.");
            return new(true, match, "");
        }

        /// <summary>
        /// Treat a social locator as an untrusted hint until both the public
        /// directory and the live server independently agree with it.
        /// </summary>
        public static async Task<SocialLobbyVerification> VerifySocialLobbyAsync(
            string host, int port, ulong authorityEpoch, int protocol, string roomKey,
            CancellationToken cancellationToken = default)
        {
            if (String.IsNullOrWhiteSpace(host) || port is < 1 or > 65535
                || authorityEpoch == 0 || protocol != NetConfig.ProtocolVersion)
                return new(false, default, "The invite target is not compatible with this build.");

            MasterListResult directory = await Task.Run(
                () => NetMasterClient.Query(LauncherPrefs.MasterHost, LauncherPrefs.MasterPort,
                    cancellationToken: cancellationToken), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!directory.Answered)
                return new(false, default, "The public server directory did not answer.");

            MasterListing listing = default;
            int listedMatches = 0;
            foreach (MasterListing candidate in directory.Servers ?? Array.Empty<MasterListing>())
            {
                if (candidate.Port == port
                    && String.Equals(candidate.Address, host, StringComparison.OrdinalIgnoreCase))
                {
                    listing = candidate;
                    listedMatches++;
                }
            }
            if (listedMatches != 1)
                return new(false, default,
                    "That lobby is no longer present in the public server directory.");

            ServerStatus status = await ProbeAsync(host, port,
                allowJoinProbe: false, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!status.Online)
                return new(false, default, "The invited lobby is no longer online.");
            if (status.Protocol != NetConfig.ProtocolVersion
                || status.Protocol != protocol)
                return new(false, default, "The invited lobby is running a different protocol.");
            if (!status.LobbyEnabled || status.Phase != SessionPhase.Lobby)
                return new(false, default, "The invited server is no longer in its lobby.");
            if (status.AuthorityEpoch != authorityEpoch)
                return new(false, default,
                    "The server restarted after this invite was created. Refresh the invite.");
            if (!String.IsNullOrWhiteSpace(roomKey)
                && !String.Equals(status.RoomKey, roomKey, StringComparison.OrdinalIgnoreCase))
                return new(false, default,
                    "The lobby changed its active arena after this invite was created.");

            return new(true, new ServerBrowserEntry(listing, status), "");
        }

        public static async Task<OnlineJoinResult> JoinAsync(
            string host, int port, string playerName, Hunter hunter, int suit,
            CancellationToken cancellationToken = default, LobbyQueueClient? queuedAdmission = null,
            bool spectate = false)
        {
            host = host.Trim();
            playerName = playerName.Trim();
            if (host.Length == 0)
                return new(false, default, "Enter a server address.");
            if (playerName.Length == 0)
                playerName = "Player";

            suit = Math.Clamp(suit, 0, 3);
            LauncherPrefs.PlayerName = playerName;
            LauncherPrefs.LastHunter = hunter;
            LauncherPrefs.LastColor = suit;
            LauncherPrefs.ServerAddress = host;
            LauncherPrefs.ServerPort = port;
            LauncherPrefs.LastKind = (int)LaunchKind.Online;
            LauncherPrefs.Save();

            bool joined;
            try
            {
                joined = await Task.Run(() => NetLaunch.Connect(host, port,
                    playerName, hunter, color: suit,
                    cancellationToken: cancellationToken, queuedAdmission: queuedAdmission,
                    spectate: spectate), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                joined = false;
            }

            if (!joined)
            {
                string error = cancellationToken.IsCancellationRequested
                    ? "Join cancelled."
                    : NetLaunch.LastJoinError;
                NetSession.Stop();
                return new(false, default, error);
            }

            return new(true, new LaunchPlan
            {
                Kind = LaunchKind.Online,
                Hunter = hunter,
                PlayerName = playerName,
                RoomKey = "",
                Mode = GameMode.Battle,
                Port = port,
                Spectate = spectate
            }, "");
        }

        /// <summary>Parse host or host:port without mutating the fallback on error.</summary>
        public static bool TryParseEndpoint(string text, string fallbackHost,
            int fallbackPort, out string host, out int port)
        {
            host = fallbackHost;
            port = fallbackPort;
            text = text.Trim();
            if (text.Length == 0)
                return false;

            int colon = text.LastIndexOf(':');
            if (colon > 0)
            {
                if (colon == text.Length - 1
                    || !Int32.TryParse(text[(colon + 1)..],
                        System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out int parsed)
                    || parsed < 1 || parsed > 65535)
                    return false;
                host = text[..colon].Trim();
                port = parsed;
            }
            else
            {
                host = text;
            }
            return host.Length > 0 && !host.Contains(':');
        }
    }
}
