using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using MphRead.Entities;

namespace MphRead.Mods.Network
{
    /// <summary>
    /// Everything a networked session needs done between "join" and "run",
    /// shared by the Windows launcher and the -connect command line.
    ///
    /// It lives here rather than in the launcher because none of it is
    /// Windows-specific and because two copies of this sequence is exactly
    /// how the two entry points drifted apart before: the launcher built its
    /// player slots one way, the test harness another, and the bug only
    /// existed in the path nobody was testing.
    /// </summary>
    public static class NetLaunch
    {
        /// <summary>
        /// Join the authoritative server before loading a scene. The server
        /// assigns the slot, match identity, room, and game mode. Cancellation
        /// releases the new session without creating or mutating game entities.
        /// </summary>
        public static bool Join(string address, int port, string playerName, Hunter hunter,
            int timeoutMs = 8000, CancellationToken cancel = default)
        {
            AuthoritativePlay? play = null;
            LastJoinError = String.Empty;
            try
            {
                cancel.ThrowIfCancellationRequested();
                play = new AuthoritativePlay(address, port, playerName, hunter);
                var clock = Stopwatch.StartNew();
                while (play.Client.State == NetConnectionState.Connecting)
                {
                    cancel.ThrowIfCancellationRequested();
                    if (clock.ElapsedMilliseconds >= timeoutMs)
                    {
                        throw new TimeoutException($"No admission from {address}:{port} before the connection deadline.");
                    }
                    play.Client.Poll();
                    if (play.Client.Failure != null) { throw new ProgramException(play.Client.Failure); }
                    if (play.Client.State == NetConnectionState.Connecting) { Thread.Sleep(10); }
                }
                cancel.ThrowIfCancellationRequested();
                DisableCheatsForMatch();
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
        }

        /// <summary>
        /// Why the last <see cref="Join"/> failed, in a sentence a player can
        /// act on. Empty before the first failure.
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
            if (AuthoritativePlay.Current is { } play)
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
        /// Fixed rather than derived from how many players happen to be
        /// connected: SceneSetup picks the room's entity layout from the
        /// player count, so a client that joined alone and one that joined
        /// into a full match would lay out different spawn points, doors and
        /// items for the same map. Everyone loads the two-player layout, so
        /// everyone gets the same world.
        /// </summary>
        public const int RoomPlayerCount = 2;

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
        /// <see cref="NetSession.LocalSlot"/> for a real connection. Demo
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
            if (AuthoritativePlay.Current is { } play)
            {
                play.BuildPlayers(scene, Launcher.Hunters.Resolve(localHunter), localRecolor);
                return;
            }
            int resolvedSlot = localSlot ?? Math.Max(NetSession.LocalSlot, 0);
            for (int slot = 0; slot < PlayerEntity.MaxPlayers; slot++)
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
            for (int slot = 0; slot < PlayerEntity.MaxPlayers; slot++)
            {
                PlayerEntity? player = slot < PlayerEntity.Players.Count
                    ? PlayerEntity.Players[slot]
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
            PlayerEntity.PlayerCount = 1;
            // Before AddRoom: the room loader initialises the camera and HUD
            // against PlayerEntity.Main, so Main must already point at the
            // slot this client drives. A client on slot 1 that skipped this
            // was never its own main player -- its intro sequence never
            // ended, so it kept the spectator camera and never spawned.
            //
            // resolvedSlot itself may be -1 (demo playback, no local player
            // at all) -- Main still has to be a real array index, so this
            // falls back to slot 0 as a harmless placeholder that
            // SpectatorMode.Start immediately redirects once a real player
            // is available, same as it does after every subsequent cycle.
            int mainIndex = resolvedSlot >= 0 ? resolvedSlot : 0;
            PlayerEntity.MainPlayerIndex = mainIndex;
            Console.WriteLine($"[net] player slots built, main player = slot {mainIndex}");
            NetLog.Event($"player slots built, main = slot {mainIndex}");
        }
    }
}
