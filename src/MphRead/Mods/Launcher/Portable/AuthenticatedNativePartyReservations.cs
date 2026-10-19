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
    internal static NativePartyReservation? ParseReservation(PartyWireReservation? value)
    {
        if (value == null) return null;
        if (!value.NativeV1 || !UInt64.TryParse(value.AuthorityEpoch, NumberStyles.None, CultureInfo.InvariantCulture, out ulong epoch))
            throw new InvalidOperationException("Party reservation proof is invalid.");
        var result = new NativePartyReservation(value.RequestId, value.Generation, value.PartyRevision,
            value.TravelId, value.TravelRevision, value.MatchId, epoch, value.ExpiresAt,
            value.Status, value.RequestedCount, value.CancellationPending);
        if (!NativePartyReservationValidation.Valid(result)) throw new InvalidOperationException("Party reservation is invalid.");
        return result;
    }
    internal async Task<NativePartyReservation?> ReadReservationAsync(NativePartyJoinTarget target,
        ushort match, CancellationToken token)
    {
        if (!IsCurrent(target) || match == 0) throw new InvalidOperationException("Party account or lobby changed.");
        var response = await InvokeAsync(new PartyWireRequest { Action = "reservation_snapshot",
            ExpectedSelfPrimeId = target.SelfPrimeId, ExpectedRevision = target.PartyRevision,
            PartyId = target.PartyId, TravelId = target.TravelId, TravelRevision = target.TravelRevision,
            MatchId = match, AuthorityEpoch = target.AuthorityEpoch.ToString(CultureInfo.InvariantCulture) }, token, target.AccountWitness).ConfigureAwait(false);
        if (!response.Value.Ok) throw new InvalidOperationException("Party reservation could not be verified.");
        var reservation = ParseReservation(response.Value.Reservation);
        if (reservation != null && (reservation.MatchId != match || reservation.AuthorityEpoch != target.AuthorityEpoch
            || reservation.TravelId != target.TravelId || reservation.TravelRevision != target.TravelRevision))
            throw new InvalidOperationException("Party reservation changed.");
        return reservation;
    }
    private async Task<NativePartyTravelResult> ReservationAsync(NativePartyTravelCommand command, CancellationToken token)
    {
        bool reserve = command.Action == NativePartyTravelAction.Reserve;
        var request = new PartyWireRequest { Action = reserve ? "reserve_intent" : "cancel_reservation",
            ExpectedSelfPrimeId = command.ExpectedSelfPrimeId, ExpectedRevision = command.ExpectedRevision,
            PartyId = command.PartyId, TravelId = command.TravelId, TravelRevision = command.TravelRevision,
            RequestId = reserve ? Guid.NewGuid().ToString("D") : command.Reservation?.RequestId ?? "",
            OperationGeneration = command.Reservation?.OperationGeneration ?? 1 };
        bool completed = false;
        try
        {
        if (reserve)
        {
            if (command.LocalLobby is not { } local || !MatchesLocalLobby(local)) throw new InvalidOperationException("Current leader lobby changed.");
            var listed = await ServerBrowserService.FindListedLobbyAsync(local.AuthorityEpoch, token).ConfigureAwait(false);
            if (!listed.Verified || !listed.Entry.Status.PartyReservationsV1 || listed.Entry.Status.MatchId != local.MatchId
                || listed.Entry.Status.RoomKey != local.RoomKey || !MatchesLocalLobby(local))
                throw new InvalidOperationException("This lobby does not support verified group reservations.");
            request.MatchId = local.MatchId; request.AuthorityEpoch = local.AuthorityEpoch.ToString(CultureInfo.InvariantCulture);
            request.Host = listed.Entry.Listing.Address; request.Port = listed.Entry.Listing.Port;
            request.Protocol = NetConfig.ProtocolVersion; request.RoomKey = local.RoomKey;
        }
        else if (command.Reservation is { } existing)
        { request.MatchId = existing.MatchId; request.AuthorityEpoch = existing.AuthorityEpoch.ToString(CultureInfo.InvariantCulture); }
        else throw new InvalidOperationException("There is no current reservation.");
        var result = await InvokeAsync(request, token, command.AccountWitness).ConfigureAwait(false);
        if (!result.Value.Ok) throw new InvalidOperationException("Party reservation was refused.");
        var reservation = ParseReservation(result.Value.Reservation);
        if (reserve)
        {
            if (reservation is not { Active: true } || command.LocalLobby is not { } local || !MatchesLocalLobby(local)
                || reservation.RequestId != request.RequestId || reservation.OperationGeneration != 1
                || reservation.PartyRevision != command.ExpectedRevision || reservation.TravelId != command.TravelId
                || reservation.TravelRevision != command.TravelRevision || reservation.MatchId != local.MatchId
                || reservation.AuthorityEpoch != local.AuthorityEpoch || reservation.ExpiresAt <= DateTimeOffset.UtcNow
                || !HunterLicenseClient.IsNativeAccountLeaseCurrent(command.AccountWitness))
                throw new InvalidOperationException("Party reservation changed.");
            string ticket = await WaitForPartyTicket(command.AccountWitness, token).ConfigureAwait(false);
            var packet = new PartyReservationRequestPacket(1, PartyReservationPurpose.LeaderCreate,
                NetConnection.NewId(), NetSession.ClientId, 0, reservation.RequestId, command.PartyId,
                (ulong)reservation.PartyRevision, reservation.TravelId, (uint)reservation.TravelRevision,
                reservation.MatchId, reservation.AuthorityEpoch, ticket);
            if (!await HunterLicenseClient.ExecuteNativeLeaseActionAsync(command.AccountWitness,
                () => NetSession.BeginNativePartyReservation(packet, command.AccountWitness!), token).ConfigureAwait(false))
                throw new InvalidOperationException("Leader account or lobby changed before reservation.");
        }
        else if (command.Reservation is { } old)
            await HunterLicenseClient.ExecuteNativeLeaseActionAsync(command.AccountWitness,
                () => NetSession.CancelNativePartyReservation(old.RequestId, command.AccountWitness), token).ConfigureAwait(false);
        var next = await InvokeAsync(new PartyWireRequest { Action = "snapshot" }, token, command.AccountWitness).ConfigureAwait(false);
        if (!next.Value.Ok || next.Value.Snapshot == null) throw new InvalidOperationException("Party refresh failed.");
        NativePartySnapshot snapshot = ParseLeased(next.Value.Snapshot, next.Lease) with { Reservation = reservation };
        completed = true; return new(snapshot, null);
        }
        finally
        {
            if (reserve && !completed && request.MatchId > 0)
            {
                NetSession.CancelNativePartyReservation(request.RequestId, command.AccountWitness);
                _ = CleanupReservationIntent(request, command.AccountWitness);
            }
        }
    }
    private static async Task CleanupReservationIntent(PartyWireRequest request, object? lease)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            if (!HunterLicenseClient.IsNativeAccountLeaseCurrent(lease)) return;
            await InvokeAsync(new PartyWireRequest { Action = "cancel_reservation", ExpectedSelfPrimeId = request.ExpectedSelfPrimeId,
                ExpectedRevision = request.ExpectedRevision, PartyId = request.PartyId, TravelId = request.TravelId,
                TravelRevision = request.TravelRevision, MatchId = request.MatchId, AuthorityEpoch = request.AuthorityEpoch,
                RequestId = request.RequestId, OperationGeneration = request.OperationGeneration }, timeout.Token, lease).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException) { }
    }
    internal static async Task<string> WaitForPartyTicket(object? lease, CancellationToken token)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        do
        {
            token.ThrowIfCancellationRequested();
            if (!HunterLicenseClient.IsNativeAccountLeaseCurrent(lease)) throw new InvalidOperationException("Party account changed.");
            if (HunterLicenseClient.TryGetNativeCareerTicket(NetSession.ClientId, lease, out string ticket)) return ticket;
            await Task.Delay(100, token).ConfigureAwait(false);
        } while (DateTimeOffset.UtcNow < deadline);
        throw new InvalidOperationException("Career identity is not ready. Refresh Party and retry.");
    }
    internal sealed partial class PartyWireRequest
    {
        [JsonPropertyName("match_id")] public ushort? MatchId { get; set; }
        [JsonPropertyName("request_id")] public string RequestId { get; set; } = "";
        [JsonPropertyName("operation_generation")] public long OperationGeneration { get; set; } = 1;
    }
    internal sealed class PartyWireReservation
    {
        [JsonPropertyName("native_v1")] public bool NativeV1 { get; set; }
        [JsonPropertyName("request_id")] public string RequestId { get; set; } = "";
        [JsonPropertyName("operation_generation")] public long Generation { get; set; }
        [JsonPropertyName("party_revision")] public long PartyRevision { get; set; }
        [JsonPropertyName("travel_id")] public string TravelId { get; set; } = "";
        [JsonPropertyName("travel_revision")] public int TravelRevision { get; set; }
        [JsonPropertyName("match_id")] public ushort MatchId { get; set; }
        [JsonPropertyName("authority_epoch")] public string AuthorityEpoch { get; set; } = "";
        [JsonPropertyName("expires_at")] public DateTimeOffset ExpiresAt { get; set; }
        [JsonPropertyName("status")] public string Status { get; set; } = "";
        [JsonPropertyName("requested_count")] public int RequestedCount { get; set; }
        [JsonPropertyName("cancellation_pending")] public bool CancellationPending { get; set; }
    }
}
#endif
