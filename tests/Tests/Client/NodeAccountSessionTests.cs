using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Accounts;
using MphRead.Mods.Network;
using ProjectPrime.Server.Shared;
using Xunit;

namespace MphRead.Tests;

public sealed class NodeAccountSessionTests
{
    [Theory]
    [InlineData("wss://node.example/v1/control", true)]
    [InlineData("ws://node.example/v1/control", false)]
    [InlineData("wss://user:password@node.example/v1/control", false)]
    [InlineData("wss://node.example/v1/control?ticket=secret", false)]
    public async Task DirectoryPinsCompatibilityAndSecureControlEndpoint(string endpoint, bool valid)
    {
        var node = new NodeListing(Guid.NewGuid(), "Node", "us", endpoint, 9, "build", new string('a', 64), 10, 1, 1, 0, "community", DateTimeOffset.UtcNow);
        using var session = new AccountSession(new Uri("https://backend.example/"), new Handler(node));
        if (valid)
        {
            NodeListing[] entries = await session.GetNodesAsync(9, "build", new string('a', 64));
            Assert.Single(entries);
            Assert.Equal("wss://node.example/v1/control", entries[0].PublicControlUri);
        }
        else await Assert.ThrowsAsync<InvalidOperationException>(() => session.GetNodesAsync(9, "build", new string('a', 64)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.GetNodesAsync(8, "build", new string('a', 64)));
    }

    [Fact]
    public async Task DirectoryRejectsInvalidAdvertisedCatalogMetadata()
    {
        foreach ((long Revision, int Count, string? Hash) metadata in new[]
        {
            (0L, 1, null),
            (1L, 257, null),
            (1L, 1, "not-a-sha256")
        })
        {
            var node = new NodeListing(Guid.NewGuid(), "Node", "us", "wss://node.example/v1/control", 9,
                "build", new string('a', 64), 10, 1, 1, 0, "community", DateTimeOffset.UtcNow,
                MapCatalogRevision: metadata.Revision, MapCount: metadata.Count,
                MapCatalogHash: metadata.Hash);
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
                publicControlUri = "wss://node.example/v1/control",
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

        await Assert.ThrowsAsync<JsonException>(() =>
            session.GetNodesAsync(9, "build", new string('a', 64)));
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
            return Task.FromResult(JsonResponse(Page(new NodeListing(nodeId, "Node", "us",
                "wss://node.example/v1/control", 9, "build", new string('a', 64),
                10, 1, 1, 0, "community", DateTimeOffset.UtcNow))));
        });
        using var session = new AccountSession(new Uri("https://backend.example/"), handler);

        Assert.False(session.IsSignedIn);
        Assert.Single(await session.GetNodesAsync(9, "build", new string('a', 64)));
    }

    [Fact]
    public async Task DirectoryAssemblesBoundedRevisionPinnedPagesAtomically()
    {
        NodeListing[] expected = Enumerable.Range(0, NodeDirectoryContract.MaximumPageEntries + 3)
            .Select(index => new NodeListing(Guid.NewGuid(), $"Node-{index}", "us",
                "wss://node.example/v1/control", 9, "build", new string('a', 64),
                10, 1, 1, 0, "community", DateTimeOffset.UtcNow)).ToArray();
        var handler = new RecordingHandler((request, _) =>
        {
            int page = QueryInt(request.RequestUri!, "page");
            string? pinned = QueryValue(request.RequestUri!, "revision");
            Assert.Equal(page == 0 ? null : "41", pinned);
            NodeListing[] slice = expected.Skip(page * NodeDirectoryContract.MaximumPageEntries)
                .Take(NodeDirectoryContract.MaximumPageEntries).ToArray();
            return Task.FromResult(JsonResponse(Page(slice, 41, page, 2, expected.Length)));
        });
        using var session = new AccountSession(new Uri("https://backend.example/"), handler);

        NodeListing[] actual = await session.GetNodesAsync(9, "build", new string('a', 64));

        Assert.Equal(expected, actual);
        Assert.Equal(2, handler.Snapshot().Length);
    }

    [Fact]
    public async Task DirectoryRejectsDuplicateEntriesBeforePublishingSnapshot()
    {
        NodeListing node = new(Guid.NewGuid(), "Node", "us",
            "wss://node.example/v1/control", 9, "build", new string('a', 64),
            10, 1, 1, 0, "community", DateTimeOffset.UtcNow);
        NodeDirectoryEntry entry = new(node.NodeId, node.Name, node.Region,
            node.PublicControlUri, node.ProtocolVersion, node.BuildVersion,
            node.ContentHash, node.Capacity, node.OnlineUsers, node.LobbyCount,
            node.ActiveMatches, node.TrustClass, node.LastHeartbeat);
        using var session = new AccountSession(new Uri("https://backend.example/"),
            new RawJsonHandler(JsonSerializer.Serialize(new NodeDirectoryPage(1, 0, 1, 2,
                ImmutableArray.Create(entry, entry)), new JsonSerializerOptions(JsonSerializerDefaults.Web))));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.GetNodesAsync(9, "build", new string('a', 64)));
    }

    [Fact]
    public async Task GuestAdmissionTrimsNameAndUsesAnonymousGuestEndpoint()
    {
        Guid nodeId = Guid.NewGuid();
        var ticket = new NodeAdmissionTicket("guest.ticket", DateTimeOffset.UtcNow.AddSeconds(30), nodeId,
            "wss://node.example/v1/control");
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
            "wss://node.example/v1/control");
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
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(Page(listing))
            });
        }
    }

    private static NodeDirectoryPage Page(NodeListing listing, long revision = 1,
        int page = 0, int pageCount = 1, int? totalEntries = null)
        => Page(new[] { listing }, revision, page, pageCount, totalEntries);

    private static NodeDirectoryPage Page(IReadOnlyList<NodeListing> listings,
        long revision = 1, int page = 0, int pageCount = 1, int? totalEntries = null)
        => new(revision, page, pageCount, totalEntries ?? listings.Count,
            listings.Select(listing => new NodeDirectoryEntry(listing.NodeId, listing.Name,
                listing.Region, listing.PublicControlUri, listing.ProtocolVersion,
                listing.BuildVersion, listing.ContentHash, listing.Capacity,
                listing.OnlineUsers, listing.LobbyCount, listing.ActiveMatches,
                listing.TrustClass, listing.LastHeartbeat, listing.MapCatalogRevision,
                listing.MapCount, listing.MapCatalogHash)).ToImmutableArray());

    private static int QueryInt(Uri uri, string name)
        => int.Parse(QueryValue(uri, name) ?? throw new InvalidOperationException("Missing query value."));

    private static string? QueryValue(Uri uri, string name)
        => uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(pair => pair.Length == 2)
            .FirstOrDefault(pair => Uri.UnescapeDataString(pair[0]) == name) is { } value
            ? Uri.UnescapeDataString(value[1]) : null;

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
