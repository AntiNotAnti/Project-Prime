using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace MphRead.Mods.Network;

internal readonly record struct SocialLobbyMembershipReport(ulong AuthorityEpoch, uint ClientId,
    string CareerTicket, long ReporterSequence, bool LobbyEligible, bool Leave)
{
    public override string ToString() => "Server-owned Social membership report";
    internal bool Valid => AuthorityEpoch != 0 && ClientId != 0 && ReporterSequence > 0
        && CareerTicket is { Length: >= 20 and <= 768 }
        && CareerTicket.StartsWith("pp1.", StringComparison.Ordinal)
        && CareerTicket.All(c => c is >= '!' and <= '~');
}

internal readonly record struct SocialLobbyMembershipReceipt(bool Verified, Guid PlayerId);

/// <summary>HTTP only; the dedicated control owner supplies identity and consumes receipts.</summary>
internal sealed class SocialLobbyMembershipRelay
{
    private readonly HttpClient _http;
    private readonly Uri _endpoint;
    private readonly Func<HttpRequestMessage, bool> _authorize;
    internal SocialLobbyMembershipRelay(HttpClient http, Uri endpoint, Func<HttpRequestMessage, bool> authorize)
        => (_http, _endpoint, _authorize) = (http, endpoint, authorize);

    internal async Task<SocialLobbyMembershipReceipt> SendAsync(SocialLobbyMembershipReport report,
        CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!report.Valid || _endpoint.Scheme != Uri.UriSchemeHttps) return default;
        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint);
        if (!_authorize(request)) return default;
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            action = report.Leave ? "leave" : "heartbeat",
            authority_epoch = report.AuthorityEpoch.ToString(CultureInfo.InvariantCulture),
            client_id = report.ClientId,
            career_ticket = report.CareerTicket,
            reporter_sequence = report.ReporterSequence.ToString(CultureInfo.InvariantCulture),
            lobby_eligible = !report.Leave && report.LobbyEligible
        }), Encoding.UTF8, "application/json");
        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                cancellation).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return default;
            using var input = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
            var bytes = new byte[8193]; int count = 0;
            while (count < bytes.Length)
            {
                int read = await input.ReadAsync(bytes.AsMemory(count), cancellation).ConfigureAwait(false);
                if (read == 0) break;
                count += read;
            }
            if (count > 8192) return default;
            using var document = JsonDocument.Parse(bytes.AsMemory(0, count));
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True
                || !root.TryGetProperty("player_id", out var player) || player.ValueKind != JsonValueKind.String
                || !Guid.TryParse(player.GetString(), out var playerId) || playerId == Guid.Empty) return default;
            cancellation.ThrowIfCancellationRequested();
            return new(true, playerId);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException or IOException)
        { return default; }
    }
}
