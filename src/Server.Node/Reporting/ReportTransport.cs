using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Reporting;

public enum ReportDeliveryKind { Accepted, Retry, Unauthorized, Rejected }
public readonly record struct ReportDelivery(ReportDeliveryKind Kind, TimeSpan? RetryAfter = null, string? Detail = null);
public interface IMatchReportTransport
{
    Task<ReportDelivery> SubmitAsync(Guid matchId, string hash, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken);
}

/// <summary>HTTPS reporter credentials never enter reports or diagnostic messages.</summary>
public sealed class HttpMatchReportTransport : IMatchReportTransport, IDisposable
{
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly Uri _endpoint;
    private readonly string _credential;
    private readonly Guid _server;
    public HttpMatchReportTransport(Guid server, Uri endpoint, string credential, HttpClient? client = null)
    {
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(endpoint.UserInfo)
            || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0) throw new ArgumentException("Reporting requires an absolute HTTPS endpoint without credentials, query, or fragment.");
        if (string.IsNullOrWhiteSpace(credential)) throw new ArgumentException("Reporter credential is required.");
        if (server == Guid.Empty) throw new ArgumentException("Server ID is required.");
        _server = server; _endpoint = endpoint; _credential = credential;
        _ownsClient = client == null;
        _client = client ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
    }
    public async Task<ReportDelivery> SubmitAsync(Guid matchId, string hash, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        cancellationToken = deadline.Token;
        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _credential);
        request.Headers.Add("X-Server-Id", _server.ToString("D"));
        request.Headers.Add("Idempotency-Key", matchId.ToString("D"));
        request.Headers.Add("X-Content-SHA256", hash);
        request.Content = new ReadOnlyMemoryContent(payload);
        request.Content.Headers.ContentType = new("application/json");
        using HttpResponseMessage response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) return new(ReportDeliveryKind.Unauthorized, Detail: "Reporter authentication refused");
        if ((int)response.StatusCode is 408 or 429 or >= 500)
        {
            TimeSpan? retry = response.Headers.RetryAfter?.Delta;
            if (retry == null && response.Headers.RetryAfter?.Date is { } date) retry = date - DateTimeOffset.UtcNow;
            return new(ReportDeliveryKind.Retry, retry, $"HTTP {(int)response.StatusCode}");
        }
        if (!response.IsSuccessStatusCode) return new(ReportDeliveryKind.Rejected, Detail: $"HTTP {(int)response.StatusCode}");
        // A generic 2xx is not proof that this exact immutable report was accepted.
        if (response.Content.Headers.ContentLength is > 4096) return new(ReportDeliveryKind.Rejected, Detail: "Oversized report receipt");
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        byte[] bytes = new byte[4097]; int length = 0;
        while (length < bytes.Length)
        {
            int count = await stream.ReadAsync(bytes.AsMemory(length), cancellationToken);
            if (count == 0) break; length += count;
        }
        if (length > 4096) return new(ReportDeliveryKind.Rejected, Detail: "Oversized report receipt");
        try
        {
            using JsonDocument json = JsonDocument.Parse(bytes.AsMemory(0, length));
            var root = json.RootElement;
            if (root.GetProperty("matchId").GetGuid() == matchId && root.GetProperty("payloadHash").GetString() == hash)
                return new(ReportDeliveryKind.Accepted);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or System.Collections.Generic.KeyNotFoundException) { }
        return new(ReportDeliveryKind.Rejected, Detail: "Receipt does not confirm report ID/hash");
    }
    public void Dispose() { if (_ownsClient) _client.Dispose(); }
}
