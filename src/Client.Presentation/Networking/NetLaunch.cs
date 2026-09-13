using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Entities;
using MphRead.Mods.Accounts;
using ProjectPrime.Server.Shared;

namespace MphRead.Mods.Network
{
    /// <summary>
    /// Shared client-side preparation for an authenticated Node handoff and
    /// the resulting Worker session. Lobby selection and match placement are
    /// owned by the Node; this class only consumes the Worker handoff.
    /// </summary>
    public static class NetLaunch
    {
        /// <summary>
        /// Explicit developer opt-in for one-tick ACK coalescing. The
        /// production default remains conservative; reconnects retain the
        /// setting on their existing <see cref="NetClient"/> instance.
        /// </summary>
        public static bool AckCoalescingEnabled
            => ParseOptIn(Environment.GetEnvironmentVariable("PROJECT_PRIME_ACK_COALESCING"));

        internal static bool ParseOptIn(string? value)
            => value?.Trim() switch
            {
                "1" or "true" or "True" or "TRUE" or "on" or "On" or "ON" => true,
                _ => false
            };

        public static Task<bool> JoinWorkerAsync(NodeMatchHandoff handoff, string playerName,
            CancellationToken cancel = default, int timeoutMs = 8000,
            bool? ackCoalescingEnabled = null)
        {
            if (timeoutMs is < 1 or > 30000) throw new ArgumentOutOfRangeException(nameof(timeoutMs));
            if (handoff.MatchId == Guid.Empty || handoff.WireMatchId == 0 || handoff.Nonce == 0
                || handoff.Port == 0 || !IsValidWorkerHost(handoff.Host)
                || handoff.Ticket is not { Length: > 0 and <= JoinPacket.MaxRoutedTicketBytes }
                || handoff.UdpAuthenticationEnabled && (handoff.AdmissionId == Guid.Empty
                    || handoff.AdmissionKey.Length != AdmissionKeyRules.Base64Length)
                || !handoff.UdpAuthenticationEnabled && (handoff.AdmissionId != Guid.Empty
                    || handoff.AdmissionKey.Length != 0))
                throw new ArgumentException("Invalid Worker handoff.");
            ClientOnlineRuntime runtime = ClientOnlineRuntime.Ensure();
            AuthoritativePlay? existing = runtime.Match?.Play;
            if (existing?.Client.Connection?.MatchId == handoff.WireMatchId
                && existing.Client.Failure == null && existing.Client.State is NetConnectionState.Loading or NetConnectionState.Ready or NetConnectionState.Playing)
                return Task.FromResult(true);
            if (existing != null)
            {
                // Replacing a Worker session is an owner transition, not a
                // playback reset. Release the scoped context so callbacks and
                // rejoin completions from the old match cannot survive.
                runtime.ReleaseMatch(existing, dispose: true);
            }
            return Task.Run(() => JoinCore(handoff.MatchId, handoff.Host, handoff.Port, playerName, handoff.Hunter,
                timeoutMs, cancel, handoff.Nonce, handoff.Ticket, handoff.Observer, handoff.WireMatchId,
                handoff.AdmissionId, handoff.AdmissionKey, handoff.UdpAuthenticationEnabled,
                ackCoalescingEnabled ?? AckCoalescingEnabled), cancel);
        }

        internal static bool IsValidWorkerHost(string host)
        {
            if (!IPAddress.TryParse(host, out IPAddress? address)
                || !String.Equals(address.ToString(), host,
                    address.AddressFamily == AddressFamily.InterNetworkV6
                        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                || address.IsIPv4MappedToIPv6
                || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
                return false;
            if (address.AddressFamily == AddressFamily.InterNetwork)
                return address.GetAddressBytes()[0] is > 0 and < 224;
            return address.AddressFamily == AddressFamily.InterNetworkV6
                && !address.IsIPv6Multicast;
        }

        private static bool JoinCore(Guid matchId, string address, int port, string playerName, Hunter hunter,
            int timeoutMs, CancellationToken cancel, ulong? nonce, string ticket, bool observer,
            uint wireMatchId, Guid admissionId, string admissionKey, bool udpAuthenticationEnabled,
            bool ackCoalescingEnabled)
        {
            AuthoritativePlay? play = null;
            byte[]? key = udpAuthenticationEnabled ? AdmissionKeyRules.Decode(admissionKey) : null;
            LastJoinError = String.Empty;
            try
            {
                cancel.ThrowIfCancellationRequested();
                play = new AuthoritativePlay(address, port, playerName, hunter, nonce, ticket, observer,
                    wireMatchId, admissionId, key, udpAuthenticationEnabled, ackCoalescingEnabled);
                var clock = Stopwatch.StartNew();
                bool announcedPending = false;
                while (play.Client.State == NetConnectionState.Connecting)
                {
                    cancel.ThrowIfCancellationRequested();
                    if (clock.ElapsedMilliseconds >= (play.Client.AwaitingBotRetirement ? Math.Max(timeoutMs, 30000) : timeoutMs))
                    {
                        throw new TimeoutException($"No admission from {address}:{port} before the connection deadline.");
                    }
                    play.Client.Poll();
                    if (play.Client.AwaitingBotRetirement && !announcedPending)
                    {
                        Console.WriteLine("[net] waiting for a bot to finish its current life (up to 30 seconds).");
                        announcedPending = true;
                    }
                    if (play.Client.Failure != null) { throw new ProgramException(play.Client.Failure); }
                    if (play.Client.State == NetConnectionState.Connecting) { Thread.Sleep(10); }
                }
                cancel.ThrowIfCancellationRequested();
                DisableCheatsForMatch();
                ClientOnlineRuntime.Current?.AdoptMatch(play, matchId);
                Console.WriteLine($"[net] joined {play.Client.Accepted.Room} ({play.Client.Accepted.Mode}), "
                    + $"slot {play.LocalSlot}, protocol {NetHeader.Version}");
                return true;
            }
            catch (Exception exception)
            {
                play?.Dispose();
                LastJoinError = exception is OperationCanceledException ? "Connection cancelled." : exception.Message;
                Console.WriteLine($"[net] {LastJoinError}");
                return false;
            }
            finally
            {
                if (key is not null) System.Security.Cryptography.CryptographicOperations.ZeroMemory(key);
            }
        }

        /// <summary>
        /// Why the last Worker handoff failed, in a sentence a player can act
        /// on. Empty before the first failure.
        /// </summary>
        public static string LastJoinError { get; private set; } = "";

        /// <summary>
        /// Remove local cheat overrides before prediction begins, so client
        /// controls follow the authoritative server's rules. Offline settings
        /// are restored on the next launcher session.
        /// </summary>
        public static void DisableCheatsForMatch()
        {
            var turnedOff = new List<string>();
            foreach (PropertyInfo property in typeof(Cheats).GetProperties(
                BindingFlags.Public | BindingFlags.Static))
            {
                if (property.PropertyType != typeof(bool) || !property.CanRead || !property.CanWrite)
                {
                    continue;
                }
                if ((bool)(property.GetValue(null) ?? false))
                {
                    turnedOff.Add(property.Name);
                    property.SetValue(null, false);
                }
            }
            if (turnedOff.Count > 0)
            {
                string list = String.Join(", ", turnedOff);
                Console.WriteLine($"[net] cheats are off while connected ({list})");
                NetLog.Event($"cheats disabled for this session: {list}");
            }
        }

        /// <summary>
        /// The room and mode this client should load, or null when the server
        /// has not said.
        /// </summary>
        public static (string RoomKey, GameMode Mode)? ServerRoom()
        {
            if (ClientOnlineRuntime.Current?.Match?.Play is { } play)
            {
                return (play.Client.Accepted.Room, play.Client.Accepted.Mode);
            }
            MatchStatePacket? state = NetSession.ServerMatch;
            if (state == null || state.Value.RoomKey.Length == 0)
            {
                return null;
            }
            GameMode mode = Enum.IsDefined(typeof(GameMode), state.Value.Mode)
                ? (GameMode)state.Value.Mode
                : GameMode.Battle;
            return (state.Value.RoomKey, mode);
        }

        /// <summary>
        /// Entity layer to load a networked room with.
        ///
        /// Derived from the frozen match capacity rather than how many players
        /// happen to be connected. Retail data provides layouts through four
        /// players; larger matches use that richest authored layer.
        /// </summary>
        public static int RoomPlayerCount
            => ClientOnlineRuntime.Current?.Match?.Client.Accepted.Rules.EntityLayerPlayerCount
                ?? NetConfig.RoomPlayerCount;

        /// <summary>
        /// Create one player entity per slot, before the room loads.
        ///
        /// All four, always. Scene.AddPlayer is inert once the room has
        /// loaded, and Scene.AddRoom only lists players that are SlotActive
        /// at that moment, so a slot not built and listed here can never be
        /// filled later -- which is exactly why a client that started alone
        /// could never materialise anybody who joined afterwards. The
        /// unoccupied ones are left inactive: invisible, unsimulated, and
        /// waiting for NetSlotManager to switch them on.
        /// </summary>
        /// <param name="localSlot">
        /// Which slot is "this machine's own player" -- defaults to
        /// <see cref="NetSession.LocalSlot"/> for a real connection. Replay
        /// playback passes -1 explicitly: there is no local player during
        /// playback, and without this every slot-0 hunter, recolour and
        /// occupancy check below silently clamped to slot 0 (from
        /// <c>Math.Max(NetSession.LocalSlot, 0)</c>, since LocalSlot stays
        /// -1 for the whole session) -- overwriting slot 0's real recorded
        /// hunter with whatever dummy one the playback call site passed, and
        /// exempting it alone from the "not occupied, clear Active" pass a
        /// few lines down.
        /// </param>
        public static void BuildPlayers(Scene scene, Hunter localHunter, int localRecolor,
            int teamId = -1, int? localSlot = null)
        {
            if (ClientOnlineRuntime.Current?.Match?.Play is { } play)
            {
                play.BuildPlayers(scene, Launcher.Hunters.Resolve(localHunter), localRecolor);
                return;
            }
            int resolvedSlot = localSlot ?? Math.Max(NetSession.LocalSlot, 0);
            NetSession.ApplyRoster(scene);
            ReplayPlayback.ApplyRoster(scene);
            scene.Players.MaxPlayers = PlayerEntity.SlotCapacity;
            for (int slot = 0; slot < scene.Players.MaxPlayers; slot++)
            {
                // Only this machine's hunter is a local choice. Everyone
                // else's comes from the server's roster, because it is their
                // choice, not a row in this client's menu: building slot N
                // from the menu's "player N" meant a client on slot 1 played
                // whatever its own P2 row said while announcing its P1 row,
                // and two clients sharing a settings file both ended up
                // showing the same hunter for everybody.
                Hunter hunter = slot == resolvedSlot ? localHunter : NetSession.SlotHunter[slot];
                scene.AddPlayer(hunter, slot == resolvedSlot ? localRecolor : 0, teamId);
            }
            for (int slot = 0; slot < scene.Players.MaxPlayers; slot++)
            {
                PlayerEntity? player = slot < scene.Players.Count
                    ? scene.Players[slot]
                    : null;
                if (player == null)
                {
                    continue;
                }
                // No AI anywhere in a networked match: Scene.AddPlayer flags
                // every player after the first as a bot, and PlayerAi would
                // then overwrite the Controls that relayed intent fills in.
                player.IsBot = false;
                player.BotLevel = 0;
                if (slot == resolvedSlot)
                {
                    continue;
                }
                bool occupied = slot < NetSession.SlotOccupied.Length
                    && NetSession.SlotOccupied[slot];
                if (!occupied)
                {
                    // Active only. SlotActive stays on so AddRoom lists the
                    // entity and OnLoad initialises it; without that the slot
                    // is absent from the scene rather than merely empty.
                    player.LoadFlags &= ~LoadFlags.Active;
                }
            }
            scene.Players.ActiveCount = 1;
            // Before AddRoom: the room loader initialises the camera and HUD
            // against scene.LocalPlayer!, so Main must already point at the
            // slot this client drives. A client on slot 1 that skipped this
            // was never its own main player -- its intro sequence never
            // ended, so it kept the spectator camera and never spawned.
            //
            // resolvedSlot itself may be -1 (replay playback, no local player
            // at all) -- Main still has to be a real array index, so this
            // falls back to slot 0 as a harmless placeholder that
            // SpectatorMode.Start immediately redirects once a real player
            // is available, same as it does after every subsequent cycle.
            int mainIndex = resolvedSlot >= 0 ? resolvedSlot : 0;
            scene.LocalPlayerSlot = mainIndex;
            Console.WriteLine($"[net] player slots built, main player = slot {mainIndex}");
            NetLog.Event($"player slots built, main = slot {mainIndex}");
        }
    }
}
