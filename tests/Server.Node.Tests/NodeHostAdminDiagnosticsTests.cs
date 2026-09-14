using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProjectPrime.Server.Node.Admin;
using ProjectPrime.Server.Node.Lobbies;
using ProjectPrime.Server.Node.Workers;
using Xunit;

namespace ProjectPrime.Server.Node.Tests;

public sealed class NodeHostAdminDiagnosticsTests
{
    [Fact]
    public async Task LifecycleDiagnosticsIsAuthenticatedBoundedAndSecretFree()
    {
        const string token = "test-only-host-lifecycle-diagnostics-token-0001";
        string tokenPath = Path.Combine(Path.GetTempPath(),
            "host-admin-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(tokenPath, token);
        try
        {
            await using var host = new NodeHostFixture(configure: builder =>
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Node:HostAdmin:TokenFile"] = tokenPath
                }));
            await host.App.StartAsync();
            using var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
                    certificate?.GetCertHashString() == host.CertificateThumbprint
            };
            using var client = new HttpClient(handler)
            {
                BaseAddress = new Uri(host.App.Urls.Single())
            };

            HttpResponseMessage unauthorized = await client.GetAsync("/v1/host/lifecycle");
            Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

            DefaultHttpContext nonTls = new();
            nonTls.Request.Scheme = "http";
            nonTls.RequestServices = host.App.Services;
            IResult httpsRequired = NodeHostAdminEndpoints.GetLifecycleDiagnostics(
                nonTls,
                host.App.Services.GetRequiredService<NodeHostAdminAuthorization>(),
                host.App.Services.GetRequiredService<LobbyManager>(),
                host.App.Services.GetRequiredService<NodeMatchCoordinator>(),
                host.App.Services.GetRequiredService<WorkerScheduler>(),
                host.App.Services.GetRequiredService<TimeProvider>());
            await httpsRequired.ExecuteAsync(nonTls);
            Assert.Equal(StatusCodes.Status403Forbidden, nonTls.Response.StatusCode);

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
            HttpResponseMessage[] responses = await Task.WhenAll(
                Enumerable.Range(0, 8).Select(_ =>
                    client.GetAsync("/v1/host/lifecycle")));
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK,
                response.StatusCode));

            string body = await responses[0].Content.ReadAsStringAsync();
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            Assert.Equal(JsonValueKind.Object, root.ValueKind);
            Assert.True(root.TryGetProperty("capturedAt", out _));
            Assert.All(new[] { "lobbies", "matches", "placements" }, name =>
            {
                Assert.True(root.TryGetProperty(name, out JsonElement value));
                Assert.Equal(JsonValueKind.Array, value.ValueKind);
                Assert.InRange(value.GetArrayLength(), 0, 4096);
            });
            Assert.DoesNotContain("token", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secret", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("nonce", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("exception", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("path", body, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(tokenPath);
        }
    }

    [Fact]
    public async Task LifecycleDiagnosticsIsNotMappedWhenHostAdminIsDisabled()
    {
        await using var host = new NodeHostFixture();
        await host.App.StartAsync();
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
                certificate?.GetCertHashString() == host.CertificateThumbprint
        };
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(host.App.Urls.Single())
        };

        HttpResponseMessage response = await client.GetAsync("/v1/host/lifecycle");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
