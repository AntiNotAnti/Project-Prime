using System;
using System.Collections.Generic;
using System.Linq;
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
        private int CombatantCount => _peers.Count(p => !p.Spectating) + _bots.Count;
        private ServerBotSlot? FindBot(int slot) => _bots.Find(b => b.SlotIndex == slot);
        private int TeamOccupants(int team, int excludeSlot = -1) =>
            _peers.Count(p => !p.Spectating && p.SlotIndex != excludeSlot && p.TeamIndex == team)
            + _bots.Count(b => b.SlotIndex != excludeSlot && b.TeamIndex == team);

        private bool BotMapAvailable(MatchDefinition match, out string reason)
        {
            reason = "";
            if (!match.MapIdentity.IsCustom) return true;
            // Validate the installed geometry's generated navigation, never a claimed metadata flag.
            try
            {
                var definition = CustomRooms.Definitions.FirstOrDefault(d => d.Name == match.RoomKey);
                if (definition != null)
                {
                    var analysis = new MapAnalysisResult("bots", MapCompiler.Compile(definition), navigation: true);
                    var graph = analysis.CreateNavigation();
                    if (analysis.Succeeded && graph != null && graph.Positions.Length > 1 && graph.Edges > 0)
                        return true;
                }
            }
            catch (Exception ex) { Log($"[bot] navigation validation failed: {ex.Message}"); }
            reason = "Bots unavailable: this map has no usable AI navigation data.";
            return false;
        }

        private LobbyResultCode ConfigureBot(LobbyCommandPacket command, bool update, out string reason)
        {
            reason = "";
            var bot = update ? FindBot(command.TargetSlot) : null;
            if (update && bot == null) { reason = "That bot has left."; return LobbyResultCode.TargetNotFound; }
            int slot = bot?.SlotIndex ?? NextFreeSlot();
            if (slot < 0) { reason = "All player slots are occupied."; return LobbyResultCode.ServerBusy; }
            if ((command.Hunter >= 7 && command.Hunter != (byte)Hunter.Random) || command.Color > 3 || command.BotLevel > 3)
            { reason = "Choose a valid hunter, suit and bot difficulty."; return LobbyResultCode.InvalidConfiguration; }
            if (!BotMapAvailable(CurrentDefinition, out reason)) return LobbyResultCode.MapUnavailable;
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
            int intentLength = intent.WriteNetwork(_scratch.AsSpan(1));
            foreach (var peer in _peers)
                _transport?.Send(peer.EndPoint, PacketType.SlotIntent, _scratch.AsSpan(0, 1 + intentLength));
        }
    }
}
