using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Network;

internal sealed record PartyReservationReceipt(bool Ok, string Status, Guid Player,
    Guid Leader, Guid[] Members, Guid[] Roster, DateTimeOffset ExpiresAt,
    DateTimeOffset ClaimExpiresAt, bool Capability)
{
    internal static readonly PartyReservationReceipt Refused = new(false, "unavailable", Guid.Empty,
        Guid.Empty, Array.Empty<Guid>(), Array.Empty<Guid>(), default, default, false);
}

/// <summary>Bounded worker-only adapter. Reporter credentials and HMAC verification
/// stay with their existing owners; no UDP identity is interpreted as an account.</summary>
internal sealed class SocialPartyReservationRelay(HttpClient http, Uri endpoint,
    Func<HttpRequestMessage,bool> authorize)
{
    internal Task<PartyReservationReceipt> CapabilityAsync(CancellationToken token)
        => SendAsync(new() { ["native_v1"] = true, ["action"] = "capability" }, token);

    internal Task<PartyReservationReceipt> ReportAsync(PartyReservationRequestPacket identity,
        string action, string reservation, DateTimeOffset operationDeadline, CancellationToken token,
        IReadOnlyList<(Guid Player, int Slot)>? assignments = null, DateTimeOffset expires = default,
        int slot = -1, ulong connection = 0, ulong claim = 0, bool selfCancel = false)
    {
        var body = new Dictionary<string,object?>
        {
            ["native_v1"] = true, ["action"] = action, ["request_id"] = identity.RequestId,
            ["party_id"] = identity.PartyId, ["party_revision"] = identity.PartyRevision,
            ["travel_id"] = identity.TravelId, ["travel_revision"] = identity.TravelRevision,
            ["match_id"] = identity.MatchId, ["authority_epoch"] = identity.AuthorityEpoch.ToString(CultureInfo.InvariantCulture),
            ["operation_generation"] = 1, ["operation_deadline"] = operationDeadline,
            ["client_id"] = identity.ClientId, ["career_ticket"] = identity.CareerTicket,
            ["purpose"] = identity.Purpose == PartyReservationPurpose.LeaderCreate ? "leader" : "member"
        };
        if (action != "validate") body["reservation_id"] = reservation;
        if (action == "activate")
        {
            body["assignments"] = assignments?.Select(x => new { player_id = x.Player.ToString("D"), slot = x.Slot }).ToArray();
            body["expires_at"] = expires;
        }
        if (action is "claim_admission" or "admitted")
        {
            body["slot"] = slot;
            body["connection_id"] = connection.ToString(CultureInfo.InvariantCulture);
            body["claim_nonce"] = claim.ToString(CultureInfo.InvariantCulture);
            if (action == "admitted") body["peer_created"] = true;
        }
        if (action == "cancel")
        {
            body["cancel_scope"] = selfCancel ? "self" : "group";
            body["status"] = "cancelled";
        }
        return SendAsync(body, token);
    }

    private async Task<PartyReservationReceipt> SendAsync(Dictionary<string,object?> body, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (endpoint.Scheme != Uri.UriSchemeHttps) return PartyReservationReceipt.Refused;
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        if (!authorize(request)) return PartyReservationReceipt.Refused;
        string payload = JsonSerializer.Serialize(body);
        if (Encoding.UTF8.GetByteCount(payload) > 8192) return PartyReservationReceipt.Refused;
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return PartyReservationReceipt.Refused;
            using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            byte[] bytes = new byte[8193]; int count = 0;
            while (count < bytes.Length)
            {
                int read = await input.ReadAsync(bytes.AsMemory(count), token).ConfigureAwait(false);
                if (read == 0) break;
                count += read;
            }
            if (count > 8192) return PartyReservationReceipt.Refused;
            using var document = JsonDocument.Parse(bytes.AsMemory(0, count));
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("ok", out var ok)
                || ok.ValueKind != JsonValueKind.True) return PartyReservationReceipt.Refused;
            string Text(string key) => root.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.String
                ? item.GetString() ?? "" : "";
            Guid Id(string key) => Guid.TryParse(Text(key), out var id) ? id : Guid.Empty;
            Guid[] Ids(string key)
            {
                if (!root.TryGetProperty(key, out var list) || list.ValueKind != JsonValueKind.Array || list.GetArrayLength() > 8) return Array.Empty<Guid>();
                var result = new List<Guid>();
                foreach (var item in list.EnumerateArray())
                    if (item.ValueKind != JsonValueKind.String || !Guid.TryParse(item.GetString(), out var id)
                        || id == Guid.Empty || result.Contains(id)) return Array.Empty<Guid>();
                    else result.Add(id);
                return result.ToArray();
            }
            DateTimeOffset Stamp(string key) => DateTimeOffset.TryParse(Text(key), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var stamp) ? stamp : default;
            token.ThrowIfCancellationRequested();
            return new(true, Text("status"), Id("player_id"), Id("leader_id"), Ids("members"), Ids("roster"),
                Stamp("expires_at"), Stamp("claim_expires_at"), root.TryGetProperty("native_v1", out var cap)
                    && cap.ValueKind == JsonValueKind.True && root.TryGetProperty("version", out var version) && version.TryGetInt32(out int v) && v == 1);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException or IOException or InvalidOperationException)
        { return PartyReservationReceipt.Refused; }
    }
}
