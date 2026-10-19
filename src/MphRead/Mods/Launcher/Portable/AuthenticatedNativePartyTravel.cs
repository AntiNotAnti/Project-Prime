#if MPHREAD_AVALONIA
using System;
using System.Globalization;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher;

internal sealed partial class AuthenticatedNativePartyAuthority
{
    internal static bool IsCurrent(NativePartyJoinTarget target)
        => HunterLicenseClient.IsNativeAccountLeaseCurrent(target.AccountWitness);

    internal static bool MatchesLocalLobby(NativePartyLocalLobby local)
        => NetSession.Active && NetSession.IsClient && NetSession.PersistentLobby
        && !NetSession.SemanticLegacyPlayback && NetSession.LocalSlot >= 0
        && NetSession.ServerSession is { Phase: SessionPhase.Lobby } session
        && session.MatchId == local.MatchId && session.AuthorityEpoch == local.AuthorityEpoch
        && String.Equals(session.Match.RoomKey, local.RoomKey, StringComparison.Ordinal);

    public async Task<NativePartyTravelResult> TravelAsync(NativePartyTravelCommand command,
        CancellationToken cancellation)
    {
        if (!NativeSocialFriendsController.ValidPrimeId(command.ExpectedSelfPrimeId)
            || !Guid.TryParse(command.PartyId, out _) || command.ExpectedRevision is < 0 or > 9007199254740991L)
            throw new InvalidOperationException("Party witness is invalid.");
        if (!HunterLicenseClient.IsNativeAccountLeaseCurrent(command.AccountWitness))
            throw new InvalidOperationException("Party account changed.");
        if (command.Action is NativePartyTravelAction.Reserve or NativePartyTravelAction.CancelReservation)
            return await ReservationAsync(command, cancellation).ConfigureAwait(false);
        var request = new PartyWireRequest
        {
            Action = command.Action switch
            {
                NativePartyTravelAction.Publish => "publish_travel",
                NativePartyTravelAction.Follow => "follow_travel",
                NativePartyTravelAction.JoinLeader => "join_leader",
                NativePartyTravelAction.Decline => "decline_travel",
                NativePartyTravelAction.Clear => "clear_travel",
                _ => throw new ArgumentOutOfRangeException(nameof(command))
            },
            ExpectedSelfPrimeId = command.ExpectedSelfPrimeId, ExpectedRevision = command.ExpectedRevision,
            PartyId = command.PartyId, TravelId = command.TravelId, TravelRevision = command.TravelRevision
        };
        if (command.Action == NativePartyTravelAction.Publish)
        {
            if (command.LocalLobby is not { } local || !MatchesLocalLobby(local))
                throw new InvalidOperationException("The current lobby changed.");
            var listed = await ServerBrowserService.FindListedLobbyAsync(local.AuthorityEpoch, cancellation).ConfigureAwait(false);
            if (!listed.Verified || !MatchesLocalLobby(local)
                || listed.Entry.Status.Protocol != NetConfig.ProtocolVersion
                || !String.Equals(listed.Entry.Status.RoomKey, local.RoomKey, StringComparison.Ordinal))
                throw new InvalidOperationException("The current lobby could not be independently verified.");
            request.Host = listed.Entry.Listing.Address; request.Port = listed.Entry.Listing.Port;
            request.AuthorityEpoch = local.AuthorityEpoch.ToString(CultureInfo.InvariantCulture);
            request.Protocol = NetConfig.ProtocolVersion; request.RoomKey = local.RoomKey;
            request.ServerName = listed.Entry.Name.Length > 96 ? listed.Entry.Name[..96] : listed.Entry.Name;
        }
        var result = await InvokeAsync(request, cancellation, command.AccountWitness).ConfigureAwait(false);
        cancellation.ThrowIfCancellationRequested();
        if (!result.Value.Ok || result.Value.Snapshot == null)
            throw new InvalidOperationException("Party travel was rejected. Refresh the party.");
        NativePartySnapshot snapshot = ParseLeased(result.Value.Snapshot, result.Lease);
        if (snapshot.SelfPrimeId != command.ExpectedSelfPrimeId || snapshot.PartyId != command.PartyId
            || snapshot.ServerRevision != command.ExpectedRevision)
            throw new InvalidOperationException("Party identity or revision changed.");
        NativePartyJoinTarget? target = null;
        if (command.Action is NativePartyTravelAction.Follow or NativePartyTravelAction.JoinLeader)
        {
            if (snapshot.Travel is not { } travel || result.Value.Locator is not { } locator
                || travel.TravelId != command.TravelId || travel.Revision != command.TravelRevision
                || !UInt64.TryParse(locator.AuthorityEpoch, NumberStyles.None, CultureInfo.InvariantCulture, out ulong epoch))
                throw new InvalidOperationException("Party destination changed.");
            target = new(snapshot.SelfPrimeId, snapshot.PartyId, snapshot.ServerRevision,
                travel.TravelId, travel.Revision, locator.LobbyId, locator.Host, locator.Port,
                locator.Protocol, epoch, locator.RoomKey, locator.ExpiresAt < travel.ExpiresAt ? locator.ExpiresAt : travel.ExpiresAt)
                { AccountWitness = result.Lease, Reservation = snapshot.Reservation };
            if (!NativePartyTravelValidation.ValidTarget(target, DateTimeOffset.UtcNow)
                || target.LobbyId != travel.LobbyId || target.AuthorityEpoch != travel.AuthorityEpoch
                || target.RoomKey != travel.RoomKey || target.Protocol != NetConfig.ProtocolVersion)
                throw new InvalidOperationException("Party destination is invalid.");
        }
        if (command.LocalLobby is { } captured && !MatchesLocalLobby(captured))
            throw new InvalidOperationException("The current lobby changed.");
        return new(snapshot, target);
    }

    internal static NativePartyTravel? ParseTravel(PartyWireTravel? value)
    {
        if (value == null) return null;
        if (!Guid.TryParse(value.TravelId, out _) || value.Revision is < 1 or > 1000000
            || !Guid.TryParse(value.LobbyId, out _) || !NativeSocialFriendsController.ValidPrimeId(value.LeaderPrimeId)
            || !UInt64.TryParse(value.AuthorityEpoch, NumberStyles.None, CultureInfo.InvariantCulture, out ulong epoch) || epoch == 0
            || value.RoomKey is not { Length: > 0 and <= 128 } || value.ServerName is not { Length: <= 96 }
            || value.SelfStatus is not ("pending" or "following" or "joined" or "declined"))
            throw new InvalidOperationException("Party travel is invalid.");
        return new(value.TravelId, value.Revision, value.LeaderPrimeId, value.LobbyId, value.RoomKey,
            value.ServerName, epoch, value.ExpiresAt, value.IsLeader, value.SelfStatus);
    }

    internal async Task<bool> NoteJoinedAsync(NativePartyJoinTarget target, CancellationToken cancellation)
    {
        if (!IsCurrent(target) || !NativePartyTravelValidation.ValidTarget(target, DateTimeOffset.UtcNow)) return false;
        var result = await InvokeAsync(new PartyWireRequest { Action = "joined_travel",
            ExpectedSelfPrimeId = target.SelfPrimeId, ExpectedRevision = target.PartyRevision,
            PartyId = target.PartyId, TravelId = target.TravelId, TravelRevision = target.TravelRevision }, cancellation, target.AccountWitness).ConfigureAwait(false);
        return result.Value.Ok && IsCurrent(target);
    }

    internal sealed partial class PartyWireRequest
    {
        [JsonPropertyName("travel_id")] public string TravelId { get; set; } = "";
        [JsonPropertyName("travel_revision")] public int TravelRevision { get; set; }
        [JsonPropertyName("host")] public string Host { get; set; } = "";
        [JsonPropertyName("port")] public int? Port { get; set; }
        [JsonPropertyName("authority_epoch")] public string AuthorityEpoch { get; set; } = "";
        [JsonPropertyName("protocol")] public byte? Protocol { get; set; }
        [JsonPropertyName("room_key")] public string RoomKey { get; set; } = "";
        [JsonPropertyName("server_name")] public string ServerName { get; set; } = "";
    }
    internal sealed class PartyWireTravel
    {
        [JsonPropertyName("travel_id")] public string TravelId { get; set; } = "";
        [JsonPropertyName("revision")] public int Revision { get; set; }
        [JsonPropertyName("leader_prime_id")] public string LeaderPrimeId { get; set; } = "";
        [JsonPropertyName("lobby_id")] public string LobbyId { get; set; } = "";
        [JsonPropertyName("room_key")] public string RoomKey { get; set; } = "";
        [JsonPropertyName("server_name")] public string ServerName { get; set; } = "";
        [JsonPropertyName("authority_epoch")] public string AuthorityEpoch { get; set; } = "";
        [JsonPropertyName("expires_at")] public DateTimeOffset ExpiresAt { get; set; }
        [JsonPropertyName("is_leader")] public bool IsLeader { get; set; }
        [JsonPropertyName("self_status")] public string SelfStatus { get; set; } = "";
    }
    internal sealed class PartyWireLocator
    {
        [JsonPropertyName("lobby_id")] public string LobbyId { get; set; } = "";
        [JsonPropertyName("host")] public string Host { get; set; } = "";
        [JsonPropertyName("port")] public int Port { get; set; }
        [JsonPropertyName("protocol")] public byte Protocol { get; set; }
        [JsonPropertyName("room_key")] public string RoomKey { get; set; } = "";
        [JsonPropertyName("authority_epoch")] public string AuthorityEpoch { get; set; } = "";
        [JsonPropertyName("expires_at")] public DateTimeOffset ExpiresAt { get; set; }
    }
}
#endif
