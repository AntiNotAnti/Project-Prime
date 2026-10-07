using System;
using System.Collections.Immutable;
using System.Linq;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher.Core
{
    public readonly record struct LobbyTeamSnapshot(sbyte Index, string Name, int Occupants, int Capacity)
    {
        public int Available => Math.Max(0, Capacity - Occupants);
    }

    public sealed record LobbyPlayerPresentation(LobbyPlayerSnapshot Player, bool IsOwner, bool IsLocal,
        bool CanChangeTeam, bool CanKick, bool CanTransfer, bool CanSetHandicap,
        bool CanConfigureBot, bool CanRemoveBot);

    /// <summary>Toolkit-free capabilities derived from the same immutable server state shown to the player.</summary>
    public sealed record LobbyPresentation
    {
        public required LobbySnapshot Session { get; init; }
        public ImmutableArray<LobbyTeamSnapshot> Teams { get; init; } = ImmutableArray<LobbyTeamSnapshot>.Empty;
        public ImmutableArray<LobbyPlayerPresentation> Players { get; init; } = ImmutableArray<LobbyPlayerPresentation>.Empty;
        public ImmutableArray<Hunter> AllowedHunters { get; init; } = ImmutableArray<Hunter>.Empty;
        public bool CanStart { get; init; }
        public string StartReason { get; init; } = "";
        public bool CanReady { get; init; }
        public bool CanChangeIdentity { get; init; }
        public bool CanChangeSpectator { get; init; }
        public bool CanAddBot { get; init; }
        public bool CanCloseLobby { get; init; }
        public bool CanConfigureRules { get; init; }
        public bool TeamsLocked { get; init; }
        public int CombatantCount { get; init; }
        public int SpectatorCount { get; init; }
        public int BotCount { get; init; }
        public int ChatLimit => ChatPacket.MaxTextBytes;
        public bool UnicodeChatAvailable => false;
        public bool MapRotationEditingAvailable => false;
        public string MapRotationUnavailableReason => "The existing lobby protocol exposes one next arena. Rotation order is configured when creating a server.";

        public static LobbyPresentation From(LobbySnapshot state)
        {
            bool inLobby = state.Active && !state.Closed && !state.Suspended && state.Phase == SessionPhase.Lobby;
            bool available = inLobby && !state.CommandPending;
            bool owner = available && state.IsOwner;
            bool teams = state.Match is { } definition && GameState.IsTeamMode(definition.Mode);
            bool locked = state.RuleFlags.HasFlag(LobbyRuleFlags.LockTeams);
            TeamLayout layout = state.Match is { } match ? LobbyRules.ResolveTeamLayout(match) : default;
            var teamRows = ImmutableArray.CreateBuilder<LobbyTeamSnapshot>(layout.TeamCount);
            for (sbyte team = 0; team < layout.TeamCount; team++)
                teamRows.Add(new(team, $"Team {(char)('A' + team)}",
                    state.Players.Count(player => !player.IsSpectator && player.Team == team), layout.Capacity(team)));
            var players = state.Players.Select(player => new LobbyPlayerPresentation(player,
                player.Slot == state.OwnerSlot, player.Slot == state.LocalSlot,
                available && teams && !player.IsSpectator && (state.IsOwner || (player.Slot == state.LocalSlot && !locked)),
                owner && !player.IsBot && player.Slot != state.LocalSlot,
                owner && !player.IsBot && player.Slot != state.LocalSlot,
                owner, owner && player.IsBot, owner && player.IsBot)).ToImmutableArray();
            string startReason;
            bool valid = false;
            if (state.Match is not { } rules) startReason = "Waiting for server rules.";
            else if (!state.RequiredMapReady) startReason = String.IsNullOrWhiteSpace(state.MapMessage)
                ? "Waiting for the local arena download and preparation." : state.MapMessage;
            else valid = LobbyRules.Validate(rules, Roster(state),
                state.RuleFlags.HasFlag(LobbyRuleFlags.RequireReady), out startReason) == LobbyResultCode.Ok;
            return new()
            {
                Session = state, Teams = teamRows.MoveToImmutable(), Players = players,
                AllowedHunters = HunterRules.Pool(state.Match?.LowTier ?? false).ToImmutableArray(),
                CanStart = owner && valid && !state.RulesPending && !state.IdentityPending && !state.SpectatorPending,
                StartReason = !inLobby ? "Wait until the server returns to the lobby."
                    : !state.IsOwner ? "Only the lobby owner can start a match."
                    : state.CommandPending || state.RulesPending ? "Waiting for server acknowledgement."
                    : state.IdentityPending || state.SpectatorPending ? "Waiting for server to confirm Hunter or spectator selection." : startReason,
                CanReady = available && state.LocalSlot >= 0 && !state.PreferSpectator && !state.SpectatorPending
                    && state.RuleFlags.HasFlag(LobbyRuleFlags.RequireReady)
                    && !state.Players.Any(player => player.Slot == state.LocalSlot && player.IsSpectator),
                CanChangeIdentity = available && state.Players.Any(player => player.Slot == state.LocalSlot),
                CanChangeSpectator = available && state.Players.Any(player => player.Slot == state.LocalSlot) && !state.SpectatorPending,
                CanAddBot = owner && state.Players.Length < state.MaxPlayers,
                CanCloseLobby = owner, CanConfigureRules = owner, TeamsLocked = locked,
                CombatantCount = state.Players.Count(player => !player.IsSpectator),
                SpectatorCount = state.Players.Count(player => player.IsSpectator),
                BotCount = state.Players.Count(player => player.IsBot)
            };
        }

        public bool CanAssignTeam(byte targetSlot, sbyte team)
        {
            LobbyPlayerPresentation? player = Players.FirstOrDefault(row => row.Player.Slot == targetSlot);
            if (player?.CanChangeTeam != true) return false;
            if (team == -1) return true;
            if (team < 0 || team >= Teams.Length) return false;
            int occupants = Teams[team].Occupants - (player.Player.Team == team ? 1 : 0);
            return occupants < Teams[team].Capacity;
        }

        private static RosterPacket Roster(LobbySnapshot state)
        {
            RosterPacket roster = RosterPacket.Create();
            foreach (LobbyPlayerSnapshot player in state.Players.Take(RosterPacket.MaxSlots))
            {
                int index = roster.Count++;
                roster.Slots[index] = player.Slot; roster.Generations[index] = player.Generation;
                roster.Hunters[index] = (byte)player.Hunter; roster.Colors[index] = player.Color;
                roster.Names[index] = player.Name; roster.Teams[index] = player.Team;
                roster.LobbyReady[index] = player.Ready; roster.Roles[index] = player.IsSpectator ? (byte)1 : (byte)0;
                roster.Flags[index] = player.IsBot ? (byte)1 : (byte)0;
                roster.BotLevels[index] = player.BotLevel; roster.DamageReductions[index] = player.DamageReduction;
            }
            return roster;
        }
    }
}
