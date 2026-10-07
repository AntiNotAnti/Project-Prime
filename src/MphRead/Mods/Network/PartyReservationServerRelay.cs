using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Network;

/// <summary>
/// Server-only bridge for validating social party reservation requests.
/// The dedicated reporter credential never crosses into a game client.
/// </summary>
internal static class PartyReservationServerRelay
{
    private const string DefaultUrl =
        "https://hwcjaygoistufktorbmf.supabase.co/functions/v1/social-party-reservation-server";
    private const string PublishableKey =
        "sb_publishable_EVT45OPl638kA_j8vZ0ebg_sw3aWaVz";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(8)
    };

    private static string Url =>
        Environment.GetEnvironmentVariable("PROJECT_PRIME_PARTY_RESERVATION_SERVER_URL")
        ?? DefaultUrl;

    private static string ServerKey =>
        Environment.GetEnvironmentVariable("PROJECT_PRIME_CAREER_SERVER_KEY")
        ?? "";

    internal static bool Enabled => ServerKey.Length >= 32;

    internal static async Task<PartyReservationValidation> ValidateAsync(
        Guid requestId, ulong authorityEpoch, uint clientId, string careerTicket,
        CancellationToken cancellationToken = default)
    {
        if (!Enabled || requestId == Guid.Empty || authorityEpoch == 0
            || clientId == 0 || careerTicket.Length is < 20 or > 768)
        {
            return PartyReservationValidation.Fail("reservation_validation_unavailable");
        }

        JsonDocument? document = await PostAsync(new
        {
            action = "validate",
            request_id = requestId,
            authority_epoch = authorityEpoch.ToString(CultureInfo.InvariantCulture),
            client_id = clientId,
            career_ticket = careerTicket
        }, cancellationToken).ConfigureAwait(false);

        if (document == null)
            return PartyReservationValidation.Fail("reservation_validation_failed");

        using (document)
        {
            JsonElement root = document.RootElement;
            if (!GetBool(root, "ok"))
                return PartyReservationValidation.Fail(GetString(root, "status"));

            if (!Guid.TryParse(GetString(root, "player_id"), out Guid playerId)
                || !Guid.TryParse(GetString(root, "party_id"), out Guid partyId)
                || !Guid.TryParse(GetString(root, "leader_id"), out Guid leaderId)
                || !UInt64.TryParse(GetString(root, "authority_epoch"),
                    NumberStyles.None, CultureInfo.InvariantCulture, out ulong epoch)
                || epoch != authorityEpoch)
            {
                return PartyReservationValidation.Fail("reservation_validation_malformed");
            }

            int requested = root.TryGetProperty("requested_count", out JsonElement count)
                && count.TryGetInt32(out int parsedCount) ? parsedCount : 0;
            if (requested is < 1 or > 8
                || !root.TryGetProperty("members", out JsonElement membersElement)
                || membersElement.ValueKind != JsonValueKind.Array)
            {
                return PartyReservationValidation.Fail("reservation_validation_malformed");
            }

            var members = new List<Guid>(requested);
            foreach (JsonElement item in membersElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String
                    || !Guid.TryParse(item.GetString(), out Guid member)
                    || member == Guid.Empty || members.Contains(member))
                {
                    return PartyReservationValidation.Fail("reservation_validation_malformed");
                }
                members.Add(member);
            }

            if (members.Count != requested || !members.Contains(playerId))
                return PartyReservationValidation.Fail("reservation_validation_malformed");

            Guid serverReservation = Guid.Empty;
            string reservationText = GetString(root, "server_reservation_id");
            if (reservationText.Length > 0
                && !Guid.TryParse(reservationText, out serverReservation))
            {
                return PartyReservationValidation.Fail("reservation_validation_malformed");
            }

            return new PartyReservationValidation(
                true,
                GetString(root, "status"),
                requestId,
                partyId,
                leaderId,
                playerId,
                epoch,
                requested,
                members.ToArray(),
                serverReservation);
        }
    }

    internal static async Task<bool> ActivateAsync(
        Guid requestId, Guid reservationId, ulong authorityEpoch,
        IReadOnlyDictionary<Guid, int> slots, int seconds,
        CancellationToken cancellationToken = default)
    {
        if (!Enabled || requestId == Guid.Empty || reservationId == Guid.Empty
            || authorityEpoch == 0 || slots.Count is < 1 or > 8)
            return false;

        var assignments = new List<object>(slots.Count);
        foreach ((Guid playerId, int slot) in slots)
        {
            if (playerId == Guid.Empty || slot is < 0 or > 7)
                return false;
            assignments.Add(new
            {
                player_id = playerId,
                slot
            });
        }

        JsonDocument? document = await PostAsync(new
        {
            action = "activate",
            request_id = requestId,
            reservation_id = reservationId,
            authority_epoch = authorityEpoch.ToString(CultureInfo.InvariantCulture),
            assignments,
            expires_in_seconds = Math.Clamp(seconds, 10, 60)
        }, cancellationToken).ConfigureAwait(false);

        if (document == null) return false;
        using (document) return GetBool(document.RootElement, "ok");
    }

    internal static async Task<bool> AdmittedAsync(
        Guid requestId, Guid reservationId, Guid playerId,
        CancellationToken cancellationToken = default)
    {
        if (!Enabled || requestId == Guid.Empty || reservationId == Guid.Empty
            || playerId == Guid.Empty)
            return false;

        JsonDocument? document = await PostAsync(new
        {
            action = "admitted",
            request_id = requestId,
            reservation_id = reservationId,
            player_id = playerId
        }, cancellationToken).ConfigureAwait(false);

        if (document == null) return false;
        using (document) return GetBool(document.RootElement, "ok");
    }

    internal static async Task<bool> CancelAsync(
        Guid requestId, Guid reservationId, string status,
        CancellationToken cancellationToken = default)
    {
        if (!Enabled || requestId == Guid.Empty
            || status is not ("rejected" or "cancelled" or "expired"))
            return false;

        JsonDocument? document = await PostAsync(new
        {
            action = "cancel",
            request_id = requestId,
            reservation_id = reservationId,
            status
        }, cancellationToken).ConfigureAwait(false);

        if (document == null) return false;
        using (document) return GetBool(document.RootElement, "ok");
    }

    private static async Task<JsonDocument?> PostAsync(
        object body, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Url);
            request.Headers.TryAddWithoutValidation(
                "Authorization", "Bearer " + ServerKey);
            request.Headers.TryAddWithoutValidation("apikey", PublishableKey);
            request.Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json");

            using HttpResponseMessage response =
                await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            string text = await response.Content.ReadAsStringAsync(
                cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[social] party reservation relay refused "
                    + $"({(int)response.StatusCode}): {Trim(text, 220)}");
                return null;
            }
            return JsonDocument.Parse(text);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException
            or TaskCanceledException or JsonException)
        {
            Console.WriteLine("[social] party reservation relay deferred: "
                + ex.Message);
            return null;
        }
    }

    private static bool GetBool(JsonElement root, string name)
        => root.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.True;

    private static string GetString(JsonElement root, string name)
        => root.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? "" : "";

    private static string Trim(string text, int max)
    {
        text = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length <= max ? text : text[..max] + "...";
    }
}

internal readonly record struct PartyReservationValidation(
    bool Valid,
    string Status,
    Guid RequestId,
    Guid PartyId,
    Guid LeaderId,
    Guid PlayerId,
    ulong AuthorityEpoch,
    int RequestedCount,
    Guid[] Members,
    Guid ExistingReservationId)
{
    internal static PartyReservationValidation Fail(string status)
        => new(false, status, Guid.Empty, Guid.Empty, Guid.Empty,
            Guid.Empty, 0, 0, Array.Empty<Guid>(), Guid.Empty);
}
