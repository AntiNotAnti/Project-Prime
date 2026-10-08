using System;
using System.Collections.Immutable;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher.Core
{
    public enum LobbyPumpOwner { Legacy, Native }
    public enum LobbyIntentKind
    {
        ToggleReady, StartMatch, Leave, NextHunter, NextSuit, Identify, SetTeam,
        SetHandicap, KickPlayer, TransferOwner, CloseLobby, RetryMap, ToggleSpectator,
        SendChat, AddBot, RemoveBot, UpdateBot, UpdateRules
    }

    /// <summary>A command tied to the lobby that produced its presentation.</summary>
    public readonly record struct LobbyIntent(Guid Lifetime, ushort ExpectedRevision, LobbyIntentKind Kind,
        byte TargetSlot = byte.MaxValue, sbyte Team = -1, Hunter Hunter = default,
        byte Color = 0, byte BotLevel = 1, byte DamageReduction = 0, string Text = "",
        MatchDefinition? Match = null, LobbyRuleFlags RuleFlags = default, uint DraftVersion = 0,
        uint ExpectedRosterRevision = 0, ushort TargetGeneration = 0);

    public readonly record struct LobbyActionResult(bool Accepted, string Message)
    {
        public static LobbyActionResult Ok => new(true, "");
        public static LobbyActionResult Reject(string message) => new(false, message);
    }

    public readonly record struct LobbyPlayerSnapshot(byte Slot, string Name, Hunter Hunter, byte Color,
        sbyte Team, bool Ready, ushort Ping, bool IsBot, bool IsSpectator, byte BotLevel,
        byte DamageReduction, MapAvailabilityState MapAvailability, ushort Generation = 0);

    /// <summary>Copied values only: no controls, mutable packets or roster arrays escape the session owner.</summary>
    public sealed record LobbySnapshot
    {
        public Guid Lifetime { get; init; }
        public ulong Version { get; init; }
        public LobbyContext? Context { get; init; }
        public bool Active { get; init; }
        public bool Persistent { get; init; }
        public bool Closed { get; init; }
        public bool Suspended { get; init; }
        public SessionPhase Phase { get; init; }
        public ushort SessionRevision { get; init; }
        public uint RosterRevision { get; init; }
        public ushort MatchId { get; init; }
        public ulong AuthorityEpoch { get; init; }
        public uint StartGeneration { get; init; }
        public StartStage StartStage { get; init; }
        public byte ExpectedParticipants { get; init; }
        public byte LoadedParticipants { get; init; }
        public byte WorldReadyParticipants { get; init; }
        public byte OwnerSlot { get; init; } = byte.MaxValue;
        public byte MaxPlayers { get; init; }
        public int LocalSlot { get; init; } = -1;
        public Hunter LocalHunter { get; init; }
        public byte LocalColor { get; init; }
        public string PlayerName { get; init; } = "";
        public MatchDefinition? Match { get; init; }
        public LobbyRuleFlags RuleFlags { get; init; }
        public bool CommandPending { get; init; }
        public string Message { get; init; } = "";
        public string CommandError { get; init; } = "";
        public bool RequiredMapReady { get; init; }
        public bool ShouldLoadMatch { get; init; }
        public MapAvailabilityState MapState { get; init; }
        public string MapMessage { get; init; } = "";
        public double CountdownSeconds { get; init; }
        public bool PreferSpectator { get; init; }
        public bool IdentityPending { get; init; }
        public bool SpectatorPending { get; init; }
        public string IdentityMessage { get; init; } = "";
        public string SpectatorMessage { get; init; } = "";
        public Hunter? AcknowledgedLocalHunter { get; init; }
        public byte? AcknowledgedLocalColor { get; init; }
        public bool? AcknowledgedSpectator { get; init; }
        public bool ContainsBots { get; init; }
        public bool RulesPending { get; init; }
        public string RulesError { get; init; } = "";
        public ImmutableArray<LobbyPlayerSnapshot> Players { get; init; } = ImmutableArray<LobbyPlayerSnapshot>.Empty;
        public ImmutableArray<string> Chat { get; init; } = ImmutableArray<string>.Empty;
        public bool IsOwner => LocalSlot >= 0 && OwnerSlot == LocalSlot;
        public bool CanEdit => Active && Phase == SessionPhase.Lobby && IsOwner;
    }

    /// <summary>Engine boundary, injectable without a renderer, network transport or game content.</summary>
    public interface ILobbySessionBackend
    {
        LobbySnapshot Capture();
        double Clock { get; }
        bool ShouldLoadMatch { get; }
        bool Refused { get; }
        bool TimedOut { get; }
        string RefusedMessage { get; }
        void Pump();
        void Stop();
        bool SendCommand(LobbyIntent intent);
        void Identify(Hunter hunter, byte color);
        void SetSpectator(bool spectator);
        void SendChat(string text);
        void RetryMap();
        LobbyActionResult ValidateRules(MatchDefinition match);
        void RulesAccepted(MatchDefinition match);
    }
}
