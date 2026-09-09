using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Accounts;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

public sealed class NodeAccountSessionTests
{
    [Theory]
    [InlineData("wss://node.example/control", true)]
    [InlineData("ws://node.example/control", false)]
    [InlineData("wss://user:password@node.example/control", false)]
    [InlineData("wss://node.example/control?ticket=secret", false)]
    public async Task DirectoryPinsCompatibilityAndSecureControlEndpoint(string endpoint, bool valid)
    {
        var node = new NodeListing(Guid.NewGuid(), "Node", "us", endpoint, 9, "build", new string('a', 64), 10, 1, 1, 0, "community", DateTimeOffset.UtcNow);
        using var session = new AccountSession(new Uri("https://backend.example/"), new Handler(node));
        if (valid)
        {
            NodeListing[] entries = await session.GetNodesAsync(9, "build", new string('a', 64));
            Assert.Single(entries);
            Assert.Null(entries[0].MapKeys); // Old directory JSON has no catalog field.
        }
        else await Assert.ThrowsAsync<InvalidOperationException>(() => session.GetNodesAsync(9, "build", new string('a', 64)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.GetNodesAsync(8, "build", new string('a', 64)));
    }

    [Fact]
    public async Task DirectoryRejectsInvalidAdvertisedMapCatalogs()
    {
        foreach (string[] keys in new[]
        {
            new string[257],
            new[] { "" },
            new[] { "map\nkey" },
            new[] { "same", "same" },
            new[] { "map\u007fkey" }
        })
        {
            var node = new NodeListing(Guid.NewGuid(), "Node", "us", "wss://node.example/control", 9,
                "build", new string('a', 64), 10, 1, 1, 0, "community", DateTimeOffset.UtcNow, keys);
            using var session = new AccountSession(new Uri("https://backend.example/"), new Handler(node));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                session.GetNodesAsync(9, "build", new string('a', 64)));
        }
    }

    [Fact]
    public async Task LegacyDirectoryListingWithoutMapCatalogRemainsUnknown()
    {
        Guid nodeId = Guid.NewGuid();
        string json = JsonSerializer.Serialize(new[]
        {
            new
            {
                nodeId,
                name = "Node",
                region = "us",
                publicControlUri = "wss://node.example/control",
                protocolVersion = 9,
                buildVersion = "build",
                contentHash = new string('a', 64),
                capacity = 10,
                onlineUsers = 1,
                lobbyCount = 1,
                activeMatches = 0,
                trustClass = "community",
                lastHeartbeat = DateTimeOffset.UtcNow
            }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var session = new AccountSession(new Uri("https://backend.example/"), new RawJsonHandler(json));

        NodeListing listing = Assert.Single(await session.GetNodesAsync(9, "build", new string('a', 64)));
        Assert.Null(listing.MapKeys);
    }

    [Fact]
    public async Task SignedOutDirectoryRequestIsAnonymous()
    {
        Guid nodeId = Guid.NewGuid();
        var handler = new RecordingHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/v1/nodes", request.RequestUri!.AbsolutePath);
            Assert.Null(request.Headers.Authorization);
            return Task.FromResult(JsonResponse(new[]
            {
                new NodeListing(nodeId, "Node", "us", "wss://node.example/control", 9, "build", new string('a', 64),
                    10, 1, 1, 0, "community", DateTimeOffset.UtcNow)
            }));
        });
        using var session = new AccountSession(new Uri("https://backend.example/"), handler);

        Assert.False(session.IsSignedIn);
        Assert.Single(await session.GetNodesAsync(9, "build", new string('a', 64)));
    }

    [Fact]
    public async Task GuestAdmissionTrimsNameAndUsesAnonymousGuestEndpoint()
    {
        Guid nodeId = Guid.NewGuid();
        var ticket = new NodeAdmissionTicket("guest.ticket", DateTimeOffset.UtcNow.AddSeconds(30), nodeId,
            "wss://node.example/control");
        var handler = new RecordingHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/v1/guest-node-admissions", request.RequestUri!.AbsolutePath);
            Assert.Null(request.Headers.Authorization);
            return Task.FromResult(JsonResponse(ticket));
        });
        using var session = new AccountSession(new Uri("https://backend.example/"), handler);

        NodeAdmissionTicket result = await session.GetGuestNodeTicketAsync(nodeId, "  Guest  ");

        Assert.Equal(ticket, result);
        RequestLog requestLog = Assert.Single(handler.Snapshot());
        using JsonDocument body = JsonDocument.Parse(requestLog.Body);
        Assert.Equal(nodeId, body.RootElement.GetProperty("nodeId").GetGuid());
        Assert.Equal("Guest", body.RootElement.GetProperty("displayName").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("01234567890123456")]
    [InlineData("guest\nname")]
    [InlineData("guest\u007fname")]
    public async Task GuestAdmissionRejectsInvalidDisplayNamesBeforeSending(string displayName)
    {
        using var session = new AccountSession(new Uri("https://backend.example/"),
            new RecordingHandler((_, _) => throw new InvalidOperationException("No request expected.")));

        await Assert.ThrowsAsync<ArgumentException>(() => session.GetGuestNodeTicketAsync(Guid.NewGuid(), displayName));
    }

    [Fact]
    public async Task AccountAdmissionRemainsAuthorizedAndDoesNotUseGuestFallback()
    {
        Guid playerId = Guid.NewGuid(), nodeId = Guid.NewGuid();
        var ticket = new NodeAdmissionTicket("account.ticket", DateTimeOffset.UtcNow.AddSeconds(30), nodeId,
            "wss://node.example/control");
        var handler = new RecordingHandler((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/v1/auth/login" => Task.FromResult(JsonResponse(new
            {
                tokenType = "Bearer", accessToken = "account-access", expiresIn = 3600, refreshToken = "account-refresh"
            })),
            "/v1/me" => Task.FromResult(JsonResponse(new AccountIdentity(new MphRead.Identity.PlayerId(playerId), true, true))),
            "/v1/node-admissions" => Task.FromResult(AuthorizedTicket(request, ticket, "Bearer account-access")),
            "/v1/guest-node-admissions" => throw new InvalidOperationException("Guest fallback was attempted."),
            _ => throw new InvalidOperationException($"Unexpected account path {request.RequestUri.AbsolutePath}.")
        });
        using var session = new AccountSession(new Uri("https://backend.example/"), handler);
        await session.SignInAsync("hunter@example.test", "A-long-password-1!");

        NodeAdmissionTicket result = await NodeSessions.GetAdmissionAsync(session, nodeId);

        Assert.Equal(ticket, result);
        RequestLog requestLog = Assert.Single(handler.Snapshot(), x => x.Uri.AbsolutePath == "/v1/node-admissions");
        Assert.Equal("Bearer account-access", requestLog.Authorization);
        Assert.DoesNotContain(handler.Snapshot(), x => x.Uri.AbsolutePath == "/v1/guest-node-admissions");
    }

    [Fact]
    public async Task SignedInAdmissionFailureDoesNotFallBackToGuest()
    {
        Guid playerId = Guid.NewGuid(), nodeId = Guid.NewGuid();
        var handler = new RecordingHandler((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/v1/auth/login" => Task.FromResult(JsonResponse(new
            {
                tokenType = "Bearer", accessToken = "account-access", expiresIn = 3600, refreshToken = "account-refresh"
            })),
            "/v1/me" => Task.FromResult(JsonResponse(new AccountIdentity(new MphRead.Identity.PlayerId(playerId), true, true))),
            "/v1/node-admissions" => throw new HttpRequestException("account admission failed"),
            "/v1/guest-node-admissions" => throw new InvalidOperationException("Guest fallback was attempted."),
            _ => throw new InvalidOperationException($"Unexpected account path {request.RequestUri.AbsolutePath}.")
        });
        using var session = new AccountSession(new Uri("https://backend.example/"), handler);
        await session.SignInAsync("hunter@example.test", "A-long-password-1!");

        await Assert.ThrowsAsync<HttpRequestException>(() => NodeSessions.GetAdmissionAsync(session, nodeId));
        Assert.DoesNotContain(handler.Snapshot(), x => x.Uri.AbsolutePath == "/v1/guest-node-admissions");
    }

    private sealed class Handler(NodeListing listing) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("backend.example", request.RequestUri!.Host);
            Assert.Null(request.Headers.Authorization);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new[] { listing }) });
        }
    }

    private sealed class RawJsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
    }

    private static HttpResponseMessage AuthorizedTicket(HttpRequestMessage request, NodeAdmissionTicket ticket,
        string expectedAuthorization)
    {
        Assert.Equal(expectedAuthorization, request.Headers.Authorization?.ToString());
        return JsonResponse(ticket);
    }

    private static HttpResponseMessage JsonResponse<T>(T value)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                Encoding.UTF8, "application/json")
        };

    private sealed record RequestLog(HttpMethod Method, Uri Uri, string? Authorization, string Body);

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        private readonly object _gate = new();
        private readonly List<RequestLog> _requests = [];

        public RequestLog[] Snapshot()
        {
            lock (_gate) return _requests.ToArray();
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string body = request.Content == null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            lock (_gate) _requests.Add(new(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), body));
            return await responder(request, cancellationToken).ConfigureAwait(false);
        }
    }
}
