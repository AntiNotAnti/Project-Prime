using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Network;
namespace MphRead.Mods.Launcher;
public readonly record struct SocialLobbyVerification(bool Verified, ServerBrowserEntry Entry, string Error);
public static partial class ServerBrowserService
{
        public static async Task<SocialLobbyVerification> FindListedLobbyAsync(
            ulong authorityEpoch, CancellationToken cancellationToken = default)
        {
            if (authorityEpoch == 0)
                return new(false, default, "The current lobby has no authority identity.");

            var matches = new ConcurrentBag<ServerBrowserEntry>();
            ServerDiscoveryResult discovery = await DiscoverAsync(entry =>
            {
                if (entry.Live && entry.Compatible
                    && entry.Status.AuthorityEpoch == authorityEpoch
                    && entry.Status.LobbyEnabled
                    && entry.Status.Phase == SessionPhase.Lobby)
                {
                    matches.Add(entry);
                }
            }, cancellationToken);

            if (cancellationToken.IsCancellationRequested)
                return new(false, default, "Lobby verification cancelled.");
            if (!discovery.DirectoryAnswered)
                return new(false, default, "The public server directory did not answer.");

            ServerBrowserEntry[] found = matches.ToArray();
            if (found.Length == 0)
                return new(false, default,
                    "This lobby is not currently listed in the public server directory.");
            if (found.Length > 1)
                return new(false, default,
                    "The directory returned more than one server with this authority identity.");
            return new(true, found[0], "");
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
                () => NetMasterClient.Query(LauncherPrefs.MasterHost, LauncherPrefs.MasterPort), cancellationToken);
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

}
