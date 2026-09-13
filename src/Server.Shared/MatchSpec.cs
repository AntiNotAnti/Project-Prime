using System.Collections.Immutable;
using MphRead;
using MphRead.Cosmetics;
using MphRead.Identity;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;

namespace ProjectPrime.Server.Shared;

public readonly record struct WorkerId(Guid Value);
public readonly record struct NodeId(Guid Value);
public readonly record struct MatchId(Guid Value);
public readonly record struct LobbyId(Guid Value);
public readonly record struct WireMatchId(uint Value);
public enum SeatRole { Player, Bot, Observer }
public enum BotFillPolicy { Disabled, FillVacancies }
public enum ObserverPolicy { Disabled, Allowed }
public enum ReplayPolicy { Disabled, Record }
public enum TelemetryPolicy { Disabled, Record }

public sealed record MapRequirement(string StableId, string Version, string ContentHash,
    string ArtifactHash, long PackageSize, string MatchContentHash)
{
    public void Validate()
    {
        _ = MphRead.Mods.MapGen.MapIdentity.ValidateStableId(StableId);
        _ = MphRead.Mods.MapGen.MapVersion.Parse(Version);
        _ = MphRead.Mods.MapGen.MapHash.Validate(ContentHash, nameof(ContentHash));
        _ = MphRead.Mods.MapGen.MapHash.Validate(ArtifactHash, nameof(ArtifactHash));
        _ = MphRead.Mods.MapGen.MapHash.Validate(MatchContentHash, nameof(MatchContentHash));
        if (PackageSize is < 1 or > 268_435_456)
            throw new ArgumentOutOfRangeException(nameof(PackageSize));
    }

    public static string ComputeMatchContentHash(string baseContentHash, string stableId,
        string version, string contentHash, string buildVersion, byte protocolVersion)
        => MphRead.Mods.MapGen.MatchContentHash.Compute(baseContentHash,
            new MapContentIdentity(new MapIdentity(stableId,
                MapVersion.Parse(version)), contentHash),
            GameplayContentIdentity.Current(buildVersion, protocolVersion));

    public RoomContentRequirement ToRoomContentRequirement()
        => new(new MapContentIdentity(new MapIdentity(StableId,
            MapVersion.Parse(Version)), ContentHash), ArtifactHash, PackageSize,
            MatchContentHash);
}

public sealed record ContentIdentity(string MapKey, string ContentHash, string ContentVersion,
    string BuildVersion, byte ProtocolVersion, MapRequirement? RequiredMap = null);
public sealed record RosterSeat(byte SeatId, PlayerId? PlayerId, Guid? GuestSessionId,
    string DisplayName, Hunter Hunter, byte Team, SeatRole Role, bool RankingEligible,
    CosmeticLoadoutIds Cosmetics = default);

/// <summary>A frozen launch description; collections and existing Game rules are immutable.
/// No admission tokens or passwords belong in this contract.</summary>
public sealed record MatchSpec(MatchId MatchId, LobbyId LobbyId, NodeId NodeId, Guid NodeIncarnation,
    MatchRules Rules, ContentIdentity Content, MatchTrustClass TrustClass, Guid? TournamentId,
    Guid? RoundId, ImmutableArray<RosterSeat> Roster, BotFillPolicy BotFillPolicy,
    ObserverPolicy ObserverPolicy, ReplayPolicy ReplayPolicy, TelemetryPolicy TelemetryPolicy,
    uint Rng1Seed, uint Rng2Seed, MatchLifecycleEpoch LifecycleEpoch = default,
    BotDifficulty BotDifficulty = BotDifficulty.Normal)
{
    public void Validate()
    {
        ContractGuard.Id(MatchId.Value); ContractGuard.Id(LobbyId.Value);
        ContractGuard.Id(NodeId.Value); ContractGuard.Id(NodeIncarnation);
        if (Rules is null || Content is null) throw new ArgumentException("Rules and content are required.");
        // Reuse the authoritative Game rule validator, including wire limits.
        MatchRulesWire.Write(new byte[MatchRulesWire.Size], Rules);
        ContractGuard.Text(Content.MapKey, 128); ContractGuard.Text(Content.ContentHash, 128);
        ContractGuard.Text(Content.ContentVersion, 128); ContractGuard.Text(Content.BuildVersion, 128);
        Content.RequiredMap?.Validate();
        if (!String.Equals(Content.MapKey, Rules.RoomKey, StringComparison.Ordinal)) throw new ArgumentException("Content and rules map identities differ.");
        if (Content.ProtocolVersion == 0) throw new ArgumentException("Protocol is required.");
        ContractGuard.Defined(TrustClass); ContractGuard.Defined(BotFillPolicy);
        ContractGuard.Defined(BotDifficulty);
        ContractGuard.Defined(ObserverPolicy); ContractGuard.Defined(ReplayPolicy); ContractGuard.Defined(TelemetryPolicy);
        if (LifecycleEpoch.Value != 0) LifecycleEpoch.Validate();
        if (TournamentId == Guid.Empty || RoundId == Guid.Empty || RoundId.HasValue && !TournamentId.HasValue)
            throw new ArgumentException("Invalid tournament identity.");
        if (Roster.IsDefaultOrEmpty || Roster.Length > 32) throw new ArgumentException("Roster must contain 1..32 seats.");
        var seats = new HashSet<byte>(); var players = new HashSet<PlayerId>(); var guests = new HashSet<Guid>();
        int active = 0, playerSeats = 0, observerSeats = 0;
        foreach (var seat in Roster)
        {
            if (seat is null || seat.SeatId >= 32 || !seats.Add(seat.SeatId)) throw new ArgumentException("Duplicate or null seat.");
            ContractGuard.Text(seat.DisplayName, 64); ContractGuard.Defined(seat.Role);
            if (!Enum.IsDefined(seat.Hunter) || seat.Hunter > Hunter.Guardian
                || seat.Role != SeatRole.Observer && Rules.Teams && seat.Team >= Rules.TeamCount
                || seat.Role != SeatRole.Observer && seat.SeatId >= 8)
                throw new ArgumentException($"Seat {seat.SeatId} has invalid hunter/team assignment "
                    + $"(hunter {(byte)seat.Hunter}, team {seat.Team}, configured teams {Rules.TeamCount}).");
            if (seat.PlayerId is { } player && (player.IsEmpty || !players.Add(player))) throw new ArgumentException("Invalid player identity.");
            if (seat.GuestSessionId is { } guest && (guest == Guid.Empty || !guests.Add(guest))) throw new ArgumentException("Invalid guest identity.");
            if (seat.Role == SeatRole.Bot ? seat.PlayerId.HasValue || seat.GuestSessionId.HasValue : seat.PlayerId.HasValue == seat.GuestSessionId.HasValue)
                throw new ArgumentException("Seat identity does not match its role.");
            if ((seat.Role == SeatRole.Bot && seat.Cosmetics != CosmeticLoadoutIds.Default)
                || (seat.Role != SeatRole.Bot && !CosmeticCatalog.BuiltIn.IsValid(seat.Cosmetics, seat.Hunter)))
                throw new ArgumentException($"Seat {seat.SeatId} has an invalid cosmetic loadout.");
            if (seat.RankingEligible && (seat.Role != SeatRole.Player || !seat.PlayerId.HasValue)) throw new ArgumentException("Only registered players can rank.");
            if (seat.Role == SeatRole.Observer && ObserverPolicy == ObserverPolicy.Disabled) throw new ArgumentException("Observers are disabled.");
            if (seat.Role != SeatRole.Observer) active++;
            if (seat.Role == SeatRole.Player) playerSeats++;
            if (seat.Role == SeatRole.Observer) observerSeats++;
        }
        if (active > Rules.MaxPlayers || playerSeats > MultiplayerLimits.MaxPlayers
            || observerSeats > MultiplayerLimits.MaxObservers
            || playerSeats + observerSeats > MultiplayerLimits.MaxHumanConnections)
            throw new ArgumentException("Roster exceeds multiplayer capacity.");
    }
}

internal static class ContractGuard
{
    public static void Id(Guid value) { if (value == Guid.Empty) throw new ArgumentException("Identity is required."); }
    public static void Text(string? value, int maximum)
    { if (string.IsNullOrWhiteSpace(value) || value.Length > maximum || value.Any(char.IsControl)) throw new ArgumentException("Invalid bounded text."); }
    public static void Defined<T>(T value) where T : struct, Enum
    { if (!Enum.IsDefined(value)) throw new ArgumentException("Unknown enum value."); }
}
