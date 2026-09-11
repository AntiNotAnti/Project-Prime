using System.Net;
using System.Text.Json;
using Xunit;

namespace ProjectPrime.Server.Node.Tests;

/// <summary>
/// The Node HTTP surface is deliberately small. These tests exercise the
/// actual Kestrel routes so a presentation/client change cannot silently
/// turn a health, status, map, or control endpoint into an unbounded fallback.
/// </summary>
public sealed class NodeRouteContractTests
{
    [Fact]
    public async Task PublicHealthAndStatusRoutesReturnBoundedDtos()
    {
        await using var host = new NodeHostFixture();
        await host.App.StartAsync();
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(host.App.Urls.Single()) };

        using HttpResponseMessage health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("healthy", (await JsonDocument.ParseAsync(await health.Content.ReadAsStreamAsync()))
            .RootElement.GetProperty("status").GetString());

        using HttpResponseMessage live = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        using HttpResponseMessage ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        using HttpResponseMessage status = await client.GetAsync("/v1/status");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        byte[] statusBytes = await status.Content.ReadAsByteArrayAsync();
        Assert.InRange(statusBytes.Length, 1, 64 * 1024);
        using JsonDocument statusDocument = JsonDocument.Parse(statusBytes);
        Assert.Equal(host.NodeId, statusDocument.RootElement.GetProperty("nodeId").GetGuid());
        Assert.True(statusDocument.RootElement.TryGetProperty("mapCount", out _));
    }

    [Theory]
    [InlineData("POST", "/health")]
    [InlineData("POST", "/health/live")]
    [InlineData("POST", "/health/ready")]
    [InlineData("POST", "/v1/status")]
    [InlineData("POST", "/v1/maps/stable/version/hash")]
    [InlineData("POST", "/v1/control")]
    public async Task NodeRouteMethodsAreExplicit(string method, string path)
    {
        await using var host = new NodeHostFixture();
        await host.App.StartAsync();
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(host.App.Urls.Single()) };
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        using HttpResponseMessage response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task ControlRouteRequiresWebSocketUpgradeAndMapRouteReturnsStableNotFound()
    {
        await using var host = new NodeHostFixture();
        await host.App.StartAsync();
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(host.App.Urls.Single()) };

        using HttpResponseMessage control = await client.GetAsync("/v1/control");
        Assert.Equal(HttpStatusCode.BadRequest, control.StatusCode);
        using HttpResponseMessage missing = await client.GetAsync("/v1/maps/stable/version/hash");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}
