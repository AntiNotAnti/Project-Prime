using System;
using System.Collections.Immutable;
using System.Linq;
using MphRead.Mods.Chat;
using MphRead.Mods.MapGen;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher.Core
{
    /// <summary>Translation to existing engine services; no UI toolkit or second network authority.</summary>
    internal sealed class NetLobbySessionBackend : ILobbySessionBackend
    {
        public double Clock => NetSession.Clock;
        public bool ShouldLoadMatch => NetSession.ShouldLoadMatch;
        public bool Refused => NetSession.Refused;
        public bool TimedOut => NetSession.SessionTimedOut;
        public string RefusedMessage => NetSession.RefusedReason.Describe("Server");
        public void Pump() => NetSession.Pump();
        public void Stop() { NetSession.Stop(); NetHostSession.Stop(); }

        public LobbySnapshot Capture()
        {
            SessionStatePacket? packet = NetSession.ServerSession;
            RosterPacket roster = NetSession.LobbyRoster();
            var players = ImmutableArray.CreateBuilder<LobbyPlayerSnapshot>(roster.Count);
            for (int i = 0; i < roster.Count; i++)
            {
                byte slot = roster.Slots[i];
                MapAvailabilityState availability = packet?.MapAvailability is { } states && slot < states.Length
                    ? states[slot] : MapAvailabilityState.Unknown;
                players.Add(new LobbyPlayerSnapshot(slot, roster.Names[i] ?? "", (Hunter)roster.Hunters[i],
                    roster.Colors[i], roster.Teams[i], roster.LobbyReady[i], roster.Pings[i], roster.IsBot(i),
                    roster.IsSpectator(i), roster.BotLevels[i], roster.DamageReductions[i], availability, roster.Generations[i]));
            }
            return new LobbySnapshot
            {
                Active = NetSession.Active, Persistent = NetSession.PersistentLobby,
                Phase = NetSession.SessionPhase, SessionRevision = packet?.Revision ?? 0,
                RosterRevision = roster.Revision,
                MatchId = packet?.MatchId ?? 0, AuthorityEpoch = packet?.AuthorityEpoch ?? 0,
                StartGeneration = packet?.StartGeneration ?? 0, StartStage = packet?.StartStage ?? default,
                ExpectedParticipants = packet?.ExpectedParticipants ?? 0,
                LoadedParticipants = packet?.LoadedParticipants ?? 0,
                WorldReadyParticipants = packet?.WorldReadyParticipants ?? 0,
                OwnerSlot = packet?.OwnerSlot ?? byte.MaxValue, MaxPlayers = packet?.MaxPlayers ?? 0,
                LocalSlot = NetSession.LocalSlot, LocalHunter = NetSession.LocalHunter,
                LocalColor = (byte)NetSession.LocalColor, PlayerName = NetSession.PlayerName,
                Match = packet?.Match, RuleFlags = packet?.RuleFlags ?? default,
                CommandPending = NetSession.LobbyCommandPending, Message = NetSession.LobbyMessage,
                RequiredMapReady = NetSession.RequiredMapReady,
                MapState = NetSession.MapPreparation?.State ?? MapAvailabilityState.Unknown,
                MapMessage = NetSession.MapPreparationMessage,
                CountdownSeconds = NetSession.StartCountdownRemainingSeconds,
                PreferSpectator = SpectatorMode.PreferSpectator, ContainsBots = NetSession.MatchContainsBots,
                Players = players.MoveToImmutable(), Chat = NetChat.History.ToImmutableArray()
            };
        }

        public bool SendCommand(LobbyIntent intent)
        {
            LobbyCommandType command = intent.Kind switch
            {
                LobbyIntentKind.ToggleReady => LobbyCommandType.SetReady,
                LobbyIntentKind.StartMatch => LobbyCommandType.StartMatch,
                LobbyIntentKind.SetTeam => LobbyCommandType.SetTeam,
                LobbyIntentKind.SetHandicap => LobbyCommandType.SetHandicap,
                LobbyIntentKind.KickPlayer => LobbyCommandType.KickPlayer,
                LobbyIntentKind.TransferOwner => LobbyCommandType.TransferOwner,
                LobbyIntentKind.CloseLobby => LobbyCommandType.CloseLobby,
                LobbyIntentKind.AddBot => LobbyCommandType.AddBot,
                LobbyIntentKind.RemoveBot => LobbyCommandType.RemoveBot,
                LobbyIntentKind.UpdateBot => LobbyCommandType.UpdateBot,
                LobbyIntentKind.UpdateRules => LobbyCommandType.UpdateMatch,
                _ => throw new ArgumentOutOfRangeException(nameof(intent))
            };
            SessionStatePacket? configuration = NetSession.ServerSession;
            if (intent.Kind == LobbyIntentKind.UpdateRules && configuration is { } current && intent.Match is { } match)
            {
                current.Match = match; current.RuleFlags = intent.RuleFlags; configuration = current;
            }
            bool ready = NetSession.LocalSlot >= 0 && !NetSession.SlotLobbyReady[NetSession.LocalSlot];
            return NetSession.SendLobbyCommand(command, intent.TargetSlot, intent.Team, ready, configuration,
                (byte)intent.Hunter, intent.Color, intent.BotLevel, intent.DamageReduction);
        }

        public void Identify(Hunter hunter, byte color)
        {
            NetSession.LocalHunter = hunter; NetSession.LocalColor = color;
            if (LauncherPrefs.LastHunter != hunter || LauncherPrefs.LastColor != color)
            {
                LauncherPrefs.LastHunter = hunter; LauncherPrefs.LastColor = color; LauncherPrefs.Save();
            }
            NetSession.SendIdentify();
        }
        public void SetSpectator(bool spectator)
        {
            if (SpectatorMode.PreferSpectator == spectator) NetSession.AnnounceSpectatorRole();
            else SpectatorMode.SetSessionPreference(spectator);
        }
        public void SendChat(string text) => NetChat.Send(text);
        public void RetryMap() => NetSession.RetryMapPreparation();

        public LobbyActionResult ValidateRules(MatchDefinition match)
        {
            if (LobbyRules.ValidateDefinition(match, out string reason) != LobbyResultCode.Ok
                || !MatchModifierRules.Validate(match, out reason)) return LobbyActionResult.Reject(reason);
            int max = NetSession.ServerSession?.MaxPlayers ?? 8;
            RosterPacket roster = NetSession.LobbyRoster();
            int combatants = Enumerable.Range(0, roster.Count).Count(i => !roster.IsSpectator(i));
            TeamLayout layout = LobbyRules.ResolveTeamLayout(match);
            if (layout.TeamCount > 0 && (layout.TotalPlayers < combatants
                || (LobbyRules.ExactTeams(match) && layout.TotalPlayers > max)))
                return LobbyActionResult.Reject("The matchup must fit connected players and the server limit.");
            int mapPlayers = LobbyRules.ExactTeams(match) ? layout.TotalPlayers : max;
            return MapModeCapabilities.Supports(match.RoomKey, match.Mode, LobbyRules.ResolveWorldProfile(match, max),
                out reason, mapPlayers) ? LobbyActionResult.Ok : LobbyActionResult.Reject(reason);
        }

        public void RulesAccepted(MatchDefinition match)
        {
            LauncherPrefs.LastLobbyMode = match.Mode;
            LauncherPrefs.LastLobbyTimeLimitSeconds = match.TimeLimitSeconds;
            LauncherPrefs.LastLobbyGoal = match.PointGoal;
            LauncherPrefs.Save();
        }
    }
}
