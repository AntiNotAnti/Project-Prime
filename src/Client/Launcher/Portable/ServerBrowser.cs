using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher
{
    public enum ServerSort { Ping, Population }
    public enum ServerGroup { All, Favorites, Recent }
    public sealed class ServerBrowserEntry
    {
        public MasterListing Listing { get; }
        public ServerStatus Status { get; set; }
        public ServerBrowserEntry(MasterListing listing) => Listing = listing;
    }
    public readonly record struct ServerBrowserFilter(GameMode? Mode, bool HideFull, bool HideIncompatible, int MaxPing, ServerSort Sort, ServerGroup Group);
    public static class ServerBrowser
    {
        public static bool Full(ServerStatus status) => status.HasSessionState
            ? status.JoinDisposition == ServerJoinDisposition.Full
            : status.MaxPlayers > 0 && Math.Max(0, status.Players - status.Bots) >= status.MaxPlayers;
        public static bool CanJoin(ServerStatus status) => status.Compatible && status.Online
            && (!status.HasSessionState || status.JoinDisposition is ServerJoinDisposition.JoinNow or ServerJoinDisposition.JoinLobby);
        public static string JoinLabel(ServerStatus status)
        {
            if (!status.Online || !status.Compatible) return "Closed";
            if (!status.HasSessionState) return Full(status) ? "Full" : "Join now";
            return status.JoinDisposition switch
            {
                ServerJoinDisposition.JoinNow => "Join now",
                ServerJoinDisposition.JoinLobby => "Join lobby",
                ServerJoinDisposition.Spectate => "Spectate",
                ServerJoinDisposition.WaitForNextMatch => "Wait for next match",
                ServerJoinDisposition.Full => "Full",
                _ => "Closed"
            };
        }
        public static IEnumerable<ServerBrowserEntry> Select(IEnumerable<ServerBrowserEntry> entries, ServerBrowserFilter filter, ServerBrowserPreferences preferences)
        {
            var selected = entries.Where(e => (!filter.Mode.HasValue || e.Status.Mode == filter.Mode)
                && (!filter.HideFull || !Full(e.Status)) && (!filter.HideIncompatible || e.Status.Compatible)
                && (filter.MaxPing <= 0 || e.Status.Latency >= 0 && e.Status.Latency <= filter.MaxPing)
                && (filter.Group == ServerGroup.All || filter.Group == ServerGroup.Favorites && preferences.IsFavorite(e.Listing.Endpoint)
                    || filter.Group == ServerGroup.Recent && preferences.Recent.Contains(e.Listing.Endpoint)));
            return filter.Sort == ServerSort.Population
                ? selected.OrderByDescending(e => e.Status.Players).ThenBy(e => Ping(e.Status)).ThenBy(e => e.Listing.Endpoint, StringComparer.Ordinal)
                : selected.OrderBy(e => Ping(e.Status)).ThenByDescending(e => e.Status.Players).ThenBy(e => e.Listing.Endpoint, StringComparer.Ordinal);
        }
        private static int Ping(ServerStatus status) => status.Latency < 0 ? int.MaxValue : status.Latency;
        public static ServerBrowserEntry? QuickJoin(IEnumerable<ServerBrowserEntry> entries, GameMode? preferredMode)
            => entries.Where(e => CanJoin(e.Status) && !Full(e.Status) && e.Status.Latency >= 0
                    && e.Status.MaxPlayers > 0)
                .OrderBy(e => e.Status.Latency + (e.Status.Players == 0 ? 100 : 0) - Math.Min(e.Status.Players, 8) * 5
                    + (preferredMode.HasValue && e.Status.Mode != preferredMode ? 100 : 0))
                .ThenBy(e => e.Listing.Endpoint, StringComparer.Ordinal).FirstOrDefault();
        public static string Details(ServerStatus s)
        {
            string time = s.TimeRemaining < 0 ? "unlimited" : $"{(int)s.TimeRemaining / 60}:{(int)s.TimeRemaining % 60:00}";
            string phase = s.HasSessionState ? $"{s.Phase} | {JoinLabel(s)}" : JoinLabel(s);
            string lobby = s.HasSessionState && s.Phase == AuthoritativeSessionPhase.Lobby
                ? $" | lobby {s.LobbyPlayers} players, {s.LobbyObservers} spectators, {s.ReadyPlayers} ready"
                : "";
            string locks = s.HasSessionState
                ? $" | ranked lock {(s.RankedLocked ? "on" : "off")} | tournament lock {(s.TournamentLocked ? "on" : "off")}" : "";
            return $"{NetStatus.ModeName(s.Mode)} | {s.RoomKey} | {Math.Max(0, s.Players - s.Bots)} humans + {s.Bots} bots / {s.MaxPlayers} | {s.Latency} ms | {phase} | {time} remaining{lobby}\n"
                + (s.HasRules ? $"{s.RulesetPreset} rules | FF {(s.FriendlyFire ? "on" : "off")} | radar {(s.PlayerRadar ? "on" : "off")} | spawn {s.SpawnPolicy} | overtime {s.OvertimePolicy} | late join {s.LateJoinPolicy}"
                    : "Rules not advertised")
                + $" | spectators {s.Observers}/{s.MaxObservers}, delay {s.ObserverDelaySeconds}s"
                + (s.RequiresTicket ? " | sign-in required" : " | guest access")
                + locks + " | verification not checked";
        }
    }
    public sealed class ServerBrowserPreferences
    {
        public List<string> Favorites { get; set; } = new();
        public List<string> Recent { get; set; } = new();
        public bool IsFavorite(string endpoint) => Favorites.Contains(endpoint);
        public void ToggleFavorite(string endpoint)
        {
            if (Favorites.Remove(endpoint)) return;
            if (Favorites.Count == 32) Favorites.RemoveAt(0);
            Favorites.Add(endpoint);
        }
        public void Visited(string endpoint)
        {
            Recent.Remove(endpoint);
            Recent.Insert(0, endpoint);
            if (Recent.Count > 16) Recent.RemoveAt(16);
        }
        private static string Path => System.IO.Path.Combine(LauncherPrefs.Directory, "server-browser.json");
        public static ServerBrowserPreferences Load()
        {
            try
            {
                if (!File.Exists(Path)) return new();
                var value = JsonSerializer.Deserialize<ServerBrowserPreferences>(File.ReadAllText(Path)) ?? new();
                value.Favorites = (value.Favorites ?? new()).Where(Valid).Distinct().Take(32).ToList();
                value.Recent = (value.Recent ?? new()).Where(Valid).Distinct().Take(16).ToList();
                return value;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
            { Console.WriteLine($"[browser] could not read preferences: {e.Message}"); return new(); }
        }
        private static bool Valid(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 260 && !value.Any(char.IsControl);
        public void Save()
        {
            try
            {
                Directory.CreateDirectory(LauncherPrefs.Directory);
                File.WriteAllText(Path, JsonSerializer.Serialize(this));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { Console.WriteLine($"[browser] could not save preferences: {e.Message}"); }
        }
    }
}
