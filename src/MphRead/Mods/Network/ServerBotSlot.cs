using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MphRead.Entities;
using MphRead.Mods.MapGen;
using MphRead.Mods.Multiplayer;

namespace MphRead.Mods.Network
{
    internal sealed class ServerBotSlot
    {
        public int SlotIndex;
        public byte Hunter, Color, BotLevel;
        public byte DamageReduction;
        public sbyte TeamIndex;
        public string Name => $"BOT {((Hunter)Hunter).ToString().ToUpperInvariant()} {SlotIndex + 1}";
    }

    public sealed partial class DedicatedServer
    {
        private readonly List<ServerBotSlot> _bots = new();
        private bool _botAssistedMatch;
        private int OccupiedSlotCount => _peers.Count + _bots.Count;
        private ServerBotSlot? FindBot(int slot) => _bots.Find(b => b.SlotIndex == slot);
        private int TeamOccupants(int team, int excludeSlot = -1) =>
            _peers.Count(p => p.SlotIndex != excludeSlot && p.TeamIndex == team)
            + _bots.Count(b => b.SlotIndex != excludeSlot && b.TeamIndex == team);

        // Imported arenas can take a long time to compile and analyze. Never perform
        // that work in the server's UDP/lobby command loop: it would stall all peers,
        // lobby responses and bot administration until the entire map finishes.
        // These results are keyed by the immutable on-wire package identity, so a
        // replacement map never inherits an earlier version's navigation verdict.
        private sealed record BotNavigationCheck(string RoomKey, Task<bool> Work);
        private readonly Dictionary<NetworkMapIdentity, BotNavigationCheck> _botNavigationChecks = new();

        private void PrimeBotNavigation(MatchDefinition match)
        {
            if (!match.MapIdentity.IsCustom || _botNavigationChecks.ContainsKey(match.MapIdentity))
                return;
            var definition = CustomRooms.Definitions.FirstOrDefault(d =>
                String.Equals(d.Name, match.RoomKey, StringComparison.Ordinal));
            if (definition == null) return;
            // Only a handful of historical map versions are worth retaining in a
            // persistent lobby. Dropped tasks finish privately without publishing.
            if (_botNavigationChecks.Count >= 4) _botNavigationChecks.Clear();
            var work = Task.Run(() =>
            {
                // Check generated navigation against compiled geometry, not a
                // package-declared "supports bots" flag.
                var analysis = new MapAnalysisResult("bots",
                    MapCompiler.Compile(definition), navigation: true);
                var graph = analysis.CreateNavigation();
                return analysis.Succeeded && graph != null
                    && graph.Positions.Length > 1 && graph.Edges > 0;
            });
            _botNavigationChecks.Add(match.MapIdentity, new(match.RoomKey, work));
        }

        private LobbyResultCode BotMapAvailable(MatchDefinition match, out string reason)
        {
            reason = "";
            if (!match.MapIdentity.IsCustom) return LobbyResultCode.Ok;
            PrimeBotNavigation(match);
            if (!_botNavigationChecks.TryGetValue(match.MapIdentity, out var check)
                || !String.Equals(check.RoomKey, match.RoomKey, StringComparison.Ordinal))
            {
                reason = "Bots unavailable: the selected map has no installed navigation source.";
                return LobbyResultCode.MapUnavailable;
            }
            if (!check.Work.IsCompleted)
            {
                reason = "Preparing this custom map's bot navigation. Try again shortly.";
                return LobbyResultCode.ServerBusy;
            }
            if (check.Work.IsCompletedSuccessfully && check.Work.Result)
                return LobbyResultCode.Ok;
            if (check.Work.IsFaulted)
                Log($"[bot] navigation validation failed: {check.Work.Exception!.GetBaseException().Message}");
            reason = "Bots unavailable: this map has no usable AI navigation data.";
            return LobbyResultCode.MapUnavailable;
        }

        private LobbyResultCode ConfigureBot(LobbyCommandPacket command, bool update, out string reason)
        {
            reason = "";
            if (CurrentDefinition.RiftlineM7Available&&!CustomWeapons.CustomAuthorityCombatAdapter.AuthorityBotsEnabled)
            { reason = "Disable Riftline M7 availability before configuring bots."; return LobbyResultCode.InvalidConfiguration; }
            var bot = update ? FindBot(command.TargetSlot) : null;
            if (update && bot == null) { reason = "That bot has left."; return LobbyResultCode.TargetNotFound; }
            int slot = bot?.SlotIndex ?? NextFreeSlot();
            if (slot < 0) { reason = "All player slots are occupied."; return LobbyResultCode.ServerBusy; }
            if ((command.Hunter >= 7 && command.Hunter != (byte)Hunter.Random) || command.Color > 3 || command.BotLevel > 3)
            { reason = "Choose a valid hunter, suit and bot difficulty."; return LobbyResultCode.InvalidConfiguration; }
            LobbyResultCode botMap = BotMapAvailable(CurrentDefinition, out reason);
            if (botMap != LobbyResultCode.Ok) return botMap;
            var layout = LobbyRules.ResolveTeamLayout(CurrentDefinition);
            sbyte team = -1;
            if (layout.TeamCount > 0)
            {
                Span<int> counts = stackalloc int[4];
                for (int i = 0; i < 4; i++) counts[i] = TeamOccupants(i, slot);
                team = command.TeamIndex == -1 ? TeamRules.ChooseTeam(layout, counts) : command.TeamIndex;
                if (command.TeamIndex < -1 || team < 0 || team >= layout.TeamCount)
                { reason = "Choose a legal team with capacity."; return LobbyResultCode.InvalidTeam; }
                if (counts[team] >= layout.Capacity(team))
                { reason = "That team is full."; return LobbyResultCode.TeamFull; }
            }
            else if (command.TeamIndex != -1)
            { reason = "Free-for-all bots use automatic teams."; return LobbyResultCode.InvalidTeam; }
            if (bot == null) { bot = new ServerBotSlot { SlotIndex = slot }; _bots.Add(bot); }
            bot.Hunter = (byte)Multiplayer.HunterRules.Resolve((Hunter)command.Hunter, CurrentDefinition.LowTier);
            bot.Color = command.Color; bot.BotLevel = command.BotLevel; bot.TeamIndex = team;
            _slotGenerations[slot] = NetLifecycleTracker.Next(_slotGenerations[slot]);
            if (_phase == SessionPhase.InMatch) _botAssistedMatch = true;
            Log($"[bot] {(update ? "updated" : "added")} slot {slot} {(Hunter)bot.Hunter} difficulty={bot.BotLevel} team={team}");
            return LobbyResultCode.Ok;
        }

        private bool RemoveBot(int slot)
        {
            var bot = FindBot(slot);
            if (bot == null) return false;
            _bots.Remove(bot);
            _slotGenerations[slot] = NetLifecycleTracker.Next(_slotGenerations[slot]);
            Log($"[bot] removed slot {slot}");
            return true; // BroadcastRoster applies ordinary lifecycle cleanup to authority and clients.
        }

        private void RemoveAllBots()
        {
            foreach (var bot in _bots) _slotGenerations[bot.SlotIndex] = NetLifecycleTracker.Next(_slotGenerations[bot.SlotIndex]);
            _bots.Clear();
        }

        private void BroadcastBotIntent(int slot, IntentPacket intent)
        {
            if (FindBot(slot) == null || _phase != SessionPhase.InMatch) return;
            _scratch[0] = (byte)slot;
            int intentSize = CompactIntentCodec.Write(NetFireEvents.WithoutTurretOffers(intent), _scratch.AsSpan(1));
            foreach (var peer in _peers)
                _transport?.Send(peer.EndPoint, PacketType.SlotIntent, _scratch.AsSpan(0, 1 + intentSize));
        }
    }
}
