using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Update;

/// <summary>Shared transport policy for the official update feed and assets.</summary>
internal static class UpdateTransport
{
    internal const int MaxRedirects = 5;
    internal const int MaxMetadataBytes = 256 * 1024;

    private static readonly string[] AllowedHosts =
    [
        "github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com",
        "githubusercontent.com"
    ];

    internal static bool IsAllowedUri(Uri? uri)
    {
        if (uri == null || uri.Scheme != Uri.UriSchemeHttps || !uri.IsAbsoluteUri)
            return false;
        string host = uri.Host;
        foreach (string allowed in AllowedHosts)
        {
            if (host.Equals(allowed, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith("." + allowed, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    internal static HttpClient CreateClient(HttpMessageHandler? handler,
        out bool disposeHandler)
    {
        disposeHandler = handler == null;
        handler ??= new HttpClientHandler { AllowAutoRedirect = false };
        var client = new HttpClient(handler, disposeHandler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"{Mods.Branding.FileName}/{BuildVersion.Display}");
        return client;
    }

    /// <summary>
    /// Send one request while inspecting every redirect ourselves. A custom
    /// handler is a test seam; production still applies this exact allowlist.
    /// </summary>
    internal static async Task<HttpResponseMessage> SendFollowingRedirectsAsync(
        HttpClient client, Uri original, CancellationToken cancellationToken)
    {
        Uri current = original;
        if (!IsAllowedUri(current))
            throw new InvalidOperationException("update address is not an allowed HTTPS host");

        for (int redirect = 0; redirect <= MaxRedirects; redirect++)
        {
            if (!IsAllowedUri(current))
                throw new InvalidOperationException("update redirect leaves the allowed HTTPS hosts");
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            HttpResponseMessage response = await client.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            Uri? observed = response.RequestMessage?.RequestUri;
            if (observed != null && !IsAllowedUri(observed))
            {
                response.Dispose();
                throw new InvalidOperationException("update response used an untrusted host");
            }

            if (!IsRedirect(response.StatusCode))
                return response;

            Uri? location = response.Headers.Location;
            response.Dispose();
            if (location == null)
                throw new InvalidOperationException("update redirect has no location");
            current = location.IsAbsoluteUri ? location : new Uri(current, location);
            if (!IsAllowedUri(current))
                throw new InvalidOperationException("update redirect leaves the allowed HTTPS hosts");
        }
        throw new InvalidOperationException("too many update redirects");
    }

    internal static bool IsRedirect(HttpStatusCode status) => status is
        HttpStatusCode.Moved or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod
        or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;
}
