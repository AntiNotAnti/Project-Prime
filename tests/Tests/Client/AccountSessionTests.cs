using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Identity;
using MphRead.Mods.Accounts;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests.Client;

public sealed class AccountSessionTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string RegisteredAddress = "127.0.0.1";
    private const int RegisteredPort = 27888;

    [Theory]
    [InlineData("https://accounts.example.test/")]
    [InlineData("https://accounts.example.test/base")]
    [InlineData("http://localhost:4711/")]
    [InlineData("http://127.0.0.1:4711/")]
    [InlineData("http://[::1]:4711/")]
    [InlineData("http://51.161.113.128:18085/")]
    public void BackendPolicyAllowsHttpsAndLoopbackHttp(string value)
        => Assert.True(AccountSession.IsAllowedBackend(new Uri(value)));

    [Fact]
    public void FreshLauncherUsesConfiguredDevelopmentBackendPort()
        => Assert.Equal("http://51.161.113.128:18085/",
            MphRead.Mods.Launcher.LauncherPrefs.DefaultBackendAddress);

    [Fact]
    public void BackendPolicyRejectsCredentialsQueriesFragmentsNonLoopbackHttpAndRelativeUris()
    {
        Assert.False(AccountSession.IsAllowedBackend(null));
        Assert.False(AccountSession.IsAllowedBackend(new Uri("http://accounts.example.test/")));
        Assert.False(AccountSession.IsAllowedBackend(new Uri("http://51.161.113.127/")));
        Assert.False(AccountSession.IsAllowedBackend(new Uri("ftp://accounts.example.test/")));
        Assert.False(AccountSession.IsAllowedBackend(new Uri("https://user:password@accounts.example.test/")));
        Assert.False(AccountSession.IsAllowedBackend(new Uri("https://accounts.example.test/?redirect=http://localhost/")));
        Assert.False(AccountSession.IsAllowedBackend(new Uri("https://accounts.example.test/#fragment")));
        Assert.False(AccountSession.IsAllowedBackend(new Uri("accounts", UriKind.Relative)));
    }

    [Fact]
    public void TicketEndpointRequiresCanonicalPublicIpv4AndValidPort()
    {
        Guid server = Guid.NewGuid();
        Guid incarnation = Guid.NewGuid();
        DateTimeOffset expiry = DateTimeOffset.UtcNow.AddMinutes(5);
        Assert.True(new GameTicket("ticket", expiry, server, incarnation, RegisteredAddress, 1).TryGetEndpoint(out IPEndPoint? first));
        Assert.Equal(1, first!.Port);
        Assert.True(new GameTicket("ticket", expiry, server, incarnation, RegisteredAddress, 65535).TryGetEndpoint(out IPEndPoint? last));
        Assert.Equal(65535, last!.Port);

        foreach ((string Address, int Port) invalid in new[]
        {
            ("", RegisteredPort),
            ("127.000.000.001", RegisteredPort),
            ("0.1.2.3", RegisteredPort),
            ("224.0.0.1", RegisteredPort),
            ("255.255.255.255", RegisteredPort),
            ("::1", RegisteredPort),
            (RegisteredAddress, 0),
            (RegisteredAddress, 65536)
        })
        {
            Assert.False(new GameTicket("ticket", expiry, server, incarnation, invalid.Address, invalid.Port).TryGetEndpoint(out _),
                $"{invalid.Address}:{invalid.Port} should not be accepted as a game endpoint.");
        }
    }

    [Fact]
    public void ConstructorNormalizesBackendBasePath()
    {
        using var session = new AccountSession(new Uri("https://accounts.example.test/base"),
            new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent))));

        Assert.Equal(new Uri("https://accounts.example.test/base/"), session.Backend);
    }

    [Fact]
    public async Task LicenseResponseMustMatchRequestedPlayerIdentity()
    {
        Guid requested = Guid.NewGuid();
        var response = new HunterLicense(new PlayerId(Guid.NewGuid()), "Hunter", 0, DateTimeOffset.UtcNow);
        using var session = new AccountSession(new Uri("https://accounts.example.test/"),
            new RecordingHandler((_, _) => Task.FromResult(JsonResponse(response))));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.GetLicenseAsync(new PlayerId(requested)));

        Assert.Contains("invalid Hunter License", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LicenseResponseRejectsInvalidProfileFields()
    {
        Guid player = Guid.NewGuid();
        foreach (HunterLicense response in new[]
        {
            new HunterLicense(new PlayerId(player), "", 0, DateTimeOffset.UtcNow),
            new HunterLicense(new PlayerId(player), " Hunter", 0, DateTimeOffset.UtcNow),
            new HunterLicense(new PlayerId(player), "Hunter", 7, DateTimeOffset.UtcNow)
        })
        {
            using var session = new AccountSession(new Uri("https://accounts.example.test/"),
                new RecordingHandler((_, _) => Task.FromResult(JsonResponse(response))));

            await Assert.ThrowsAsync<InvalidOperationException>(() => session.GetLicenseAsync(new PlayerId(player)));
        }
    }

    [Fact]
    public async Task LicenseResponseMapsAuthoritativeRatingSummary()
    {
        PlayerId player = new(Guid.NewGuid());
        DateTimeOffset joined = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var response = new HunterLicense(player, "Hunter", 2, joined, 750, 5,
            "Legendary Hunter", null, 8, "PairwiseNormalizedV1");
        using var session = new AccountSession(new Uri("https://accounts.example.test/"),
            new RecordingHandler((_, _) => Task.FromResult(JsonResponse(response))));

        HunterLicense license = await session.GetLicenseAsync(player);

        Assert.Equal(750, license.Points);
        Assert.Equal(5, license.Tier);
        Assert.Equal("Legendary Hunter", license.Title);
        Assert.Null(license.NextThreshold);
        Assert.Equal(8, license.LastOfficialDelta);
        Assert.Equal("PairwiseNormalizedV1", license.Policy);
    }

    [Fact]
    public async Task DefaultHandlerDoesNotFollowBackendRedirects()
    {
        await using var server = new RedirectServer();
        using var session = new AccountSession(server.Endpoint);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            session.RegisterAsync("hunter@example.test", "A-long-password-1!", "Hunter"));

        Assert.Equal(1, server.RequestCount);
    }

    [Fact]
    public async Task DeclaredOversizedJsonResponseIsRejected()
    {
        var handler = new RecordingHandler((_, _) =>
        {
            byte[] bytes = Encoding.UTF8.GetBytes(new string('x', 65_537));
            return Task.FromResult<HttpResponseMessage>(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
            });
        });
        using var session = new AccountSession(new Uri("https://accounts.example.test/"), handler);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.RegisterAsync("hunter@example.test", "A-long-password-1!", "Hunter"));

        Assert.Contains("too large", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StreamedOversizedJsonResponseIsRejected()
    {
        var handler = new RecordingHandler((_, _) =>
        {
            byte[] bytes = Encoding.UTF8.GetBytes(new string('x', 65_537));
            return Task.FromResult<HttpResponseMessage>(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new UnknownLengthContent(bytes)
            });
        });
        using var session = new AccountSession(new Uri("https://accounts.example.test/"), handler);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.RegisterAsync("hunter@example.test", "A-long-password-1!", "Hunter"));

        Assert.Contains("too large", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConcurrentSignInsAreSerialized()
    {
        Guid playerId = Guid.NewGuid();
        int loginCount = 0;
        var handler = new RecordingHandler(async (request, cancel) =>
        {
            switch (request.RequestUri!.AbsolutePath)
            {
                case "/v1/auth/login":
                    await Task.Delay(20, cancel);
                    Interlocked.Increment(ref loginCount);
                    return JsonResponse(new
                    {
                        tokenType = "Bearer",
                        accessToken = "access-token",
                        expiresIn = 3600,
                        refreshToken = "refresh-token"
                    });
                case "/v1/me":
                    await Task.Delay(10, cancel);
                    return JsonResponse(new AccountIdentity(new PlayerId(playerId), true, true));
                default:
                    throw new InvalidOperationException($"Unexpected account path {request.RequestUri.AbsolutePath}.");
            }
        });
        using var session = new AccountSession(new Uri("https://accounts.example.test/"), handler);

        await Task.WhenAll(
            session.SignInAsync("one@example.test", "A-long-password-1!"),
            session.SignInAsync("two@example.test", "A-long-password-1!"));

        Assert.Equal(2, loginCount);
        Assert.Equal(1, handler.MaximumConcurrentRequests);
        Assert.True(session.IsSignedIn);
        Assert.Equal(new PlayerId(playerId), session.Identity!.PlayerId);
    }

    [Fact]
    public async Task ConcurrentExpiredTicketRequestsPerformOneSerializedRefresh()
    {
        var time = new TestTimeProvider(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Guid playerId = Guid.NewGuid();
        Guid serverId = Guid.NewGuid();
        Guid incarnation = Guid.NewGuid();
        int refreshCount = 0;
        var handler = new RecordingHandler(async (request, cancel) =>
        {
            switch (request.RequestUri!.AbsolutePath)
            {
                case "/v1/auth/login":
                    return JsonResponse(new
                    {
                        tokenType = "Bearer",
                        accessToken = "access-before-refresh",
                        expiresIn = 60,
                        refreshToken = "refresh-token"
                    });
                case "/v1/me":
                    return JsonResponse(new AccountIdentity(new PlayerId(playerId), true, true));
                case "/v1/auth/refresh":
                    Assert.Null(request.Headers.Authorization);
                    Assert.Equal(1, Interlocked.Increment(ref refreshCount));
                    await Task.Delay(40, cancel);
                    return JsonResponse(new
                    {
                        tokenType = "Bearer",
                        accessToken = "access-after-refresh",
                        expiresIn = 3600,
                        refreshToken = "refresh-token-2"
                    });
                case "/v1/game-tickets":
                    await Task.Delay(5, cancel);
                    return JsonResponse(new GameTicket("aaa.bbb.ccc", time.GetUtcNow().AddMinutes(5), serverId, incarnation,
                        RegisteredAddress, RegisteredPort));
                default:
                    throw new InvalidOperationException($"Unexpected account path {request.RequestUri.AbsolutePath}.");
            }
        });
        using var session = new AccountSession(new Uri("https://accounts.example.test/"), handler, time);
        await session.SignInAsync("hunter@example.test", "A-long-password-1!");
        time.Advance(TimeSpan.FromSeconds(40));

        GameTicket[] tickets = await Task.WhenAll(Enumerable.Range(1, 8)
            .Select(n => session.GetTicketAsync(serverId, (ulong)n)));

        Assert.Equal(1, refreshCount);
        Assert.All(tickets, ticket => Assert.Equal("aaa.bbb.ccc", ticket.Ticket));
        Assert.All(handler.Snapshot().Where(x => x.Uri.AbsolutePath == "/v1/game-tickets"),
            request => Assert.Equal("Bearer access-after-refresh", request.Authorization));
    }

    [Fact]
    public async Task SignInStoresOnlyScopedRefreshMaterialAndRestoreRotatesIt()
    {
        const string password = "A-long-password-1!";
        const string email = "hunter@example.test";
        const string firstAccess = "account-access-secret";
        const string firstRefresh = "account-refresh-secret";
        const string secondAccess = "rotated-access-secret";
        const string secondRefresh = "rotated-refresh-secret";
        var backend = new Uri("https://accounts.example.test/base/");
        Guid player = Guid.NewGuid();
        var store = new FakeSecureSessionStore();
        using (var signedIn = new AccountSession(backend, new RecordingHandler((request, _) =>
            request.RequestUri!.AbsolutePath switch
            {
                "/base/v1/auth/login" => Task.FromResult(JsonResponse(new
                {
                    tokenType = "Bearer", accessToken = firstAccess, expiresIn = 3600,
                    refreshToken = firstRefresh
                })),
                "/base/v1/me" => Task.FromResult(JsonResponse(new AccountIdentity(new PlayerId(player), true, true))),
                _ => throw new InvalidOperationException($"Unexpected account path {request.RequestUri.AbsolutePath}.")
            }), sessionStore: store))
        {
            await signedIn.SignInAsync(email, password);
        }

        byte[]? storedValue = store.Read(backend.AbsoluteUri);
        Assert.NotNull(storedValue);
        byte[] stored = storedValue;
        string text = Encoding.UTF8.GetString(stored);
        Assert.Contains(firstRefresh, text, StringComparison.Ordinal);
        Assert.DoesNotContain(firstAccess, text, StringComparison.Ordinal);
        Assert.DoesNotContain(password, text, StringComparison.Ordinal);
        Assert.DoesNotContain(email, text, StringComparison.Ordinal);
        Assert.Contains(backend.AbsoluteUri, text, StringComparison.Ordinal);

        var restoreHandler = new RecordingHandler(async (request, cancel) =>
        {
            if (request.RequestUri!.AbsolutePath == "/base/v1/auth/refresh")
            {
                Assert.Null(request.Headers.Authorization);
                Assert.Contains(firstRefresh, await request.Content!.ReadAsStringAsync(cancel), StringComparison.Ordinal);
                return JsonResponse(new
                {
                    tokenType = "Bearer", accessToken = secondAccess, expiresIn = 3600,
                    refreshToken = secondRefresh
                });
            }
            if (request.RequestUri.AbsolutePath == "/base/v1/me")
            {
                Assert.Equal("Bearer " + secondAccess, request.Headers.Authorization?.ToString());
                return JsonResponse(new AccountIdentity(new PlayerId(player), true, true));
            }
            throw new InvalidOperationException($"Unexpected account path {request.RequestUri.AbsolutePath}.");
        });
        using var restored = new AccountSession(backend, restoreHandler, sessionStore: store);

        Assert.True(await restored.RestoreAsync());
        Assert.True(restored.IsSignedIn);
        Assert.Equal(new PlayerId(player), restored.Identity!.PlayerId);
        storedValue = store.Read(backend.AbsoluteUri);
        Assert.NotNull(storedValue);
        text = Encoding.UTF8.GetString(storedValue);
        Assert.Contains(secondRefresh, text, StringComparison.Ordinal);
        Assert.DoesNotContain(firstRefresh, text, StringComparison.Ordinal);
        Assert.DoesNotContain(secondAccess, text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"version\":2,\"backendScope\":\"https://accounts.example.test/\",\"refreshToken\":\"refresh\"}")]
    [InlineData("{\"version\":1,\"backendScope\":\"https://other.example.test/\",\"refreshToken\":\"refresh\"}")]
    [InlineData("{\"version\":1,\"backendScope\":\"https://accounts.example.test/\",\"refreshToken\":\"refresh\",\"extra\":true}")]
    [InlineData("{\"version\":1,\"version\":1,\"backendScope\":\"https://accounts.example.test/\",\"refreshToken\":\"refresh\"}")]
    public async Task RestoreRejectsAndDeletesNonCurrentNonScopedOrNonStrictRecords(string json)
    {
        var backend = new Uri("https://accounts.example.test/");
        var store = new FakeSecureSessionStore();
        store.Seed(backend.AbsoluteUri, Encoding.UTF8.GetBytes(json));
        using var session = new AccountSession(backend,
            new RecordingHandler((_, _) => throw new InvalidOperationException("No request expected.")),
            sessionStore: store);

        Assert.False(await session.RestoreAsync());
        Assert.Null(store.Read(backend.AbsoluteUri));
        Assert.Equal(1, store.DeleteCount);
    }

    [Fact]
    public async Task FailedStoredRefreshClearsProtectedAndMemorySessions()
    {
        var backend = new Uri("https://accounts.example.test/");
        var store = new FakeSecureSessionStore();
        store.Seed(backend.AbsoluteUri, SecureSessionRecordCodec.Encode(backend.AbsoluteUri, "expired-refresh"));
        using var session = new AccountSession(backend, new RecordingHandler((request, _) =>
            Task.FromResult(new HttpResponseMessage(request.RequestUri!.AbsolutePath == "/v1/auth/refresh"
                ? HttpStatusCode.Unauthorized : HttpStatusCode.InternalServerError))), sessionStore: store);

        await Assert.ThrowsAsync<HttpRequestException>(() => session.RestoreAsync());

        Assert.False(session.IsSignedIn);
        Assert.Null(session.Identity);
        Assert.Null(store.Read(backend.AbsoluteUri));
    }

    [Fact]
    public async Task RestoreCancellationPreservesProtectedRefreshMaterial()
    {
        var backend = new Uri("https://accounts.example.test/");
        var store = new FakeSecureSessionStore();
        store.Seed(backend.AbsoluteUri, SecureSessionRecordCodec.Encode(backend.AbsoluteUri, "refresh-token"));
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var session = new AccountSession(backend, new RecordingHandler(async (request, cancel) =>
        {
            Assert.Equal("/v1/auth/refresh", request.RequestUri!.AbsolutePath);
            requestStarted.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancel);
            throw new InvalidOperationException("The canceled refresh must not continue.");
        }), sessionStore: store);
        using var stop = new CancellationTokenSource();

        Task restore = session.RestoreAsync(stop.Token);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        stop.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => restore);
        Assert.NotNull(store.Read(backend.AbsoluteUri));
        Assert.False(session.IsSignedIn);
    }

    [Fact]
    public async Task SignOutAndServerRevocationDeleteProtectedRefreshMaterial()
    {
        Guid player = Guid.NewGuid();
        var backend = new Uri("https://accounts.example.test/");
        var store = new FakeSecureSessionStore();
        var handler = new RecordingHandler((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/v1/auth/login" => Task.FromResult(JsonResponse(new
            {
                tokenType = "Bearer", accessToken = "access-token", expiresIn = 3600,
                refreshToken = "refresh-token"
            })),
            "/v1/me" => Task.FromResult(JsonResponse(new AccountIdentity(new PlayerId(player), true, true))),
            "/v1/auth/revoke-sessions" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)),
            _ => throw new InvalidOperationException($"Unexpected account path {request.RequestUri.AbsolutePath}.")
        });
        using var session = new AccountSession(backend, handler, sessionStore: store);
        await session.SignInAsync("hunter@example.test", "A-long-password-1!");
        Assert.NotNull(store.Read(backend.AbsoluteUri));

        await session.SignOutAsync();
        Assert.Null(store.Read(backend.AbsoluteUri));
        await session.SignInAsync("hunter@example.test", "A-long-password-1!");
        await session.RevokeSessionsAsync();

        Assert.Null(store.Read(backend.AbsoluteUri));
        Assert.False(session.IsSignedIn);
        RequestLog revoke = Assert.Single(handler.Snapshot(), request => request.Uri.AbsolutePath == "/v1/auth/revoke-sessions");
        Assert.Equal("Bearer access-token", revoke.Authorization);
        Assert.DoesNotContain("refresh-token", revoke.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProfileUpdatesUseCurrentIdentityAndCannotChooseAnotherOwner()
    {
        Guid playerId = Guid.NewGuid();
        var handler = new RecordingHandler((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/v1/auth/login" => Task.FromResult(JsonResponse(new
            {
                tokenType = "Bearer", accessToken = "access-token", expiresIn = 3600, refreshToken = "refresh-token"
            })),
            "/v1/me" => Task.FromResult(JsonResponse(new AccountIdentity(new PlayerId(playerId), true, true))),
            "/v1/me/profile" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)),
            _ => throw new InvalidOperationException($"Unexpected account path {request.RequestUri.AbsolutePath}.")
        });
        using var session = new AccountSession(new Uri("https://accounts.example.test/"), handler);
        await session.SignInAsync("hunter@example.test", "A-long-password-1!");

        await session.UpdateProfileAsync("Renamed Hunter", 6);

        RequestLog profile = Assert.Single(handler.Snapshot(), x => x.Uri.AbsolutePath == "/v1/me/profile");
        Assert.Equal(HttpMethod.Patch, profile.Method);
        Assert.Equal("Bearer access-token", profile.Authorization);
        using JsonDocument body = JsonDocument.Parse(profile.Body);
        Assert.Equal(2, body.RootElement.EnumerateObject().Count());
        Assert.Equal("Renamed Hunter", body.RootElement.GetProperty("displayName").GetString());
        Assert.Equal(6, body.RootElement.GetProperty("favoriteHunter").GetInt32());
        Assert.False(body.RootElement.TryGetProperty("playerId", out _));
        Assert.Equal(new PlayerId(playerId), session.Identity!.PlayerId);
    }

    [Fact]
    public async Task SignOutClearsIdentityAndPreventsFurtherAuthenticatedRequests()
    {
        Guid playerId = Guid.NewGuid();
        var handler = SignedInHandler(playerId);
        using var session = new AccountSession(new Uri("https://accounts.example.test/"), handler);
        await session.SignInAsync("hunter@example.test", "A-long-password-1!");

        await session.SignOutAsync();

        Assert.False(session.IsSignedIn);
        Assert.Null(session.Identity);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.UpdateProfileAsync("Renamed", 2));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.GetTicketAsync(Guid.NewGuid(), 1));
        Assert.Equal(2, handler.Snapshot().Length);
    }

    [Fact]
    public async Task TicketResponseMustMatchServerAndStayWithinBounds()
    {
        var time = new TestTimeProvider(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Guid playerId = Guid.NewGuid();
        Guid serverId = Guid.NewGuid();
        Guid incarnation = Guid.NewGuid();
        var cases = new (string Name, GameTicket Ticket, bool Valid)[]
        {
            ("wrong server", new("valid.ticket", time.GetUtcNow().AddMinutes(1), Guid.NewGuid(), incarnation), false),
            ("missing incarnation", new("valid.ticket", time.GetUtcNow().AddMinutes(1), serverId, Guid.Empty), false),
            ("expired", new("valid.ticket", time.GetUtcNow(), serverId, incarnation), false),
            ("missing ticket", new("", time.GetUtcNow().AddMinutes(1), serverId, incarnation), false),
            ("too long", new(new string('a', 964), time.GetUtcNow().AddMinutes(1), serverId, incarnation), false),
            ("maximum length", new(new string('a', 963), time.GetUtcNow().AddMinutes(1), serverId, incarnation,
                RegisteredAddress, RegisteredPort), true)
        };

        foreach (var testCase in cases)
        {
            var handler = SignedInHandler(playerId, ticket: _ => JsonResponse(testCase.Ticket));
            using var session = new AccountSession(new Uri("https://accounts.example.test/"), handler, time);
            await session.SignInAsync("hunter@example.test", "A-long-password-1!");

            if (testCase.Valid)
            {
                GameTicket result = await session.GetTicketAsync(serverId, 1);
                Assert.Equal(testCase.Ticket, result);
            }
            else
            {
                InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    session.GetTicketAsync(serverId, 1));
                Assert.Contains("invalid game ticket", error.Message, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public async Task TicketResponseRejectsMissingOrUnsafeRegisteredEndpoint()
    {
        var time = new TestTimeProvider(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Guid playerId = Guid.NewGuid();
        Guid serverId = Guid.NewGuid();
        Guid incarnation = Guid.NewGuid();
        foreach ((string Address, int Port) endpoint in new[]
        {
            ("", 0),
            ("127.000.000.001", RegisteredPort),
            ("224.0.0.1", RegisteredPort),
            (RegisteredAddress, 0),
            (RegisteredAddress, 65536)
        })
        {
            GameTicket ticket = new("valid.ticket", time.GetUtcNow().AddMinutes(1), serverId, incarnation,
                endpoint.Address, endpoint.Port);
            using var session = new AccountSession(new Uri("https://accounts.example.test/"),
                SignedInHandler(playerId, ticket: _ => JsonResponse(ticket)), time);
            await session.SignInAsync("hunter@example.test", "A-long-password-1!");
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.GetTicketAsync(serverId, 1));
        }
    }

    [Fact]
    public void TicketDestinationPinRequiresRegisteredAddressAndExactPort()
    {
        Guid serverId = Guid.NewGuid();
        Guid incarnation = Guid.NewGuid();
        var ticket = new GameTicket("valid.ticket", DateTimeOffset.UtcNow.AddMinutes(5), serverId, incarnation,
            "198.51.100.8", RegisteredPort);
        IPAddress first = IPAddress.Parse("192.0.2.4");
        IPAddress second = IPAddress.Parse("198.51.100.8");

        Assert.Equal(second.ToString(), NetLaunch.PinTicketDestination(ticket, new[] { first, second }, RegisteredPort));
        Assert.Throws<InvalidOperationException>(() =>
            NetLaunch.PinTicketDestination(ticket, new[] { first }, RegisteredPort));
        Assert.Throws<InvalidOperationException>(() =>
            NetLaunch.PinTicketDestination(ticket, new[] { first, second }, RegisteredPort + 1));
    }

    [Fact]
    public async Task TicketRequestRejectsEmptyServerAndNonce()
    {
        using var session = new AccountSession(new Uri("https://accounts.example.test/"),
            new RecordingHandler((_, _) => throw new InvalidOperationException("No request expected.")));

        await Assert.ThrowsAsync<ArgumentException>(() => session.GetTicketAsync(Guid.Empty, 1));
        await Assert.ThrowsAsync<ArgumentException>(() => session.GetTicketAsync(Guid.NewGuid(), 0));
    }

    [Fact]
    public async Task GameJoinCarriesOnlyShortLivedTicketAndNeverAccountCredentials()
    {
        var time = new TestTimeProvider(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Guid playerId = Guid.NewGuid();
        Guid serverId = Guid.NewGuid();
        Guid incarnation = Guid.NewGuid();
        const string password = "A-long-password-1!";
        const string accessToken = "account-access-secret";
        const string refreshToken = "account-refresh-secret";
        const string ticketText = "aaa.bbb.ccc";
        var handler = SignedInHandler(playerId, accessToken, refreshToken,
            ticket: _ => JsonResponse(new GameTicket(ticketText, time.GetUtcNow().AddMinutes(5), serverId, incarnation,
                RegisteredAddress, RegisteredPort)));
        using var session = new AccountSession(new Uri("https://accounts.example.test/"), handler, time);
        await session.SignInAsync("hunter@example.test", password);
        GameTicket ticket = await session.GetTicketAsync(serverId, 42);

        RequestLog[] requests = handler.Snapshot();
        Assert.All(requests, request => Assert.StartsWith("/v1/", request.Uri.AbsolutePath, StringComparison.Ordinal));
        RequestLog ticketRequest = Assert.Single(requests, x => x.Uri.AbsolutePath == "/v1/game-tickets");
        Assert.Equal($"Bearer {accessToken}", ticketRequest.Authorization);
        Assert.DoesNotContain(password, ticketRequest.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(refreshToken, ticketRequest.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(accessToken, ticketRequest.Body, StringComparison.Ordinal);

        var join = new JoinPacket(NetHeader.Version, 42, Hunter.Samus, "Hunter", Ticket: ticket.Ticket);
        byte[] joinBytes = new byte[join.EncodedSize];
        join.Write(joinBytes);
        string gameServerWire = Encoding.ASCII.GetString(joinBytes);
        Assert.Contains(ticketText, gameServerWire, StringComparison.Ordinal);
        Assert.DoesNotContain(password, gameServerWire, StringComparison.Ordinal);
        Assert.DoesNotContain(accessToken, gameServerWire, StringComparison.Ordinal);
        Assert.DoesNotContain(refreshToken, gameServerWire, StringComparison.Ordinal);
        Assert.DoesNotContain("Bearer", gameServerWire, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CredentialsAreClearedOnDisposeAndRedactedFromDiagnostics()
    {
        Guid playerId = Guid.NewGuid();
        const string accessToken = "account-access-secret";
        const string refreshToken = "account-refresh-secret";
        var handler = SignedInHandler(playerId, accessToken, refreshToken);
        var session = new AccountSession(new Uri("https://accounts.example.test/"), handler);
        await session.SignInAsync("hunter@example.test", "A-long-password-1!");

        Assert.DoesNotContain(accessToken, session.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(refreshToken, session.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(accessToken, handler.Snapshot().Single(x => x.Uri.AbsolutePath == "/v1/me").Body,
            StringComparison.Ordinal);

        session.Dispose();

        Assert.False(session.IsSignedIn);
        Assert.Null(session.Identity);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.GetTicketAsync(Guid.NewGuid(), 1));
    }

    private static RecordingHandler SignedInHandler(Guid playerId, string accessToken = "access-token",
        string refreshToken = "refresh-token", Func<HttpRequestMessage, HttpResponseMessage>? ticket = null)
        => new((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/v1/auth/login" => Task.FromResult(JsonResponse(new
            {
                tokenType = "Bearer", accessToken, expiresIn = 3600, refreshToken
            })),
            "/v1/me" => Task.FromResult(JsonResponse(new AccountIdentity(new PlayerId(playerId), true, true))),
            "/v1/game-tickets" => Task.FromResult(ticket?.Invoke(request)
                ?? JsonResponse(new GameTicket("aaa.bbb.ccc", DateTimeOffset.UtcNow.AddMinutes(5),
                    Guid.NewGuid(), Guid.NewGuid(), RegisteredAddress, RegisteredPort))),
            _ => throw new InvalidOperationException($"Unexpected account path {request.RequestUri.AbsolutePath}.")
        });

    private static HttpResponseMessage JsonResponse<T>(T value)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(value, Json), Encoding.UTF8, "application/json")
        };

    private sealed record RequestLog(HttpMethod Method, Uri Uri, string? Authorization, string Body);

    private sealed class FakeSecureSessionStore : ISecureSessionStore
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, byte[]> _records = new(StringComparer.Ordinal);
        private int _deletes;
        public int DeleteCount => Volatile.Read(ref _deletes);

        public byte[]? Read(string scope)
        {
            lock (_gate) return _records.TryGetValue(scope, out byte[]? value) ? value.ToArray() : null;
        }

        public void Seed(string scope, byte[] value)
        {
            lock (_gate) _records[scope] = value.ToArray();
        }

        public ValueTask<byte[]?> ReadAsync(string backendScope, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Read(backendScope));
        }

        public ValueTask WriteAsync(string backendScope, ReadOnlyMemory<byte> record,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate) _records[backendScope] = record.ToArray();
            return ValueTask.CompletedTask;
        }

        public ValueTask DeleteAsync(string backendScope, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate) _records.Remove(backendScope);
            Interlocked.Increment(ref _deletes);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        private readonly object _lock = new();
        private readonly List<RequestLog> _requests = [];
        private int _active;
        private int _maximumConcurrentRequests;

        public int MaximumConcurrentRequests => Volatile.Read(ref _maximumConcurrentRequests);

        public RequestLog[] Snapshot()
        {
            lock (_lock) return _requests.ToArray();
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            int active = Interlocked.Increment(ref _active);
            while (active > Volatile.Read(ref _maximumConcurrentRequests)
                && Interlocked.CompareExchange(ref _maximumConcurrentRequests, active,
                    Volatile.Read(ref _maximumConcurrentRequests)) != active)
            {
                // CompareExchange retries until this request's observed maximum is recorded.
            }

            try
            {
                string body = request.Content == null
                    ? ""
                    : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                lock (_lock)
                {
                    _requests.Add(new RequestLog(request.Method, request.RequestUri!,
                        request.Headers.Authorization?.ToString(), body));
                }
                return await responder(request, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }
    }

    private sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan amount) => _now += amount;
    }

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => stream.WriteAsync(bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class RedirectServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _serve;
        private int _requestCount;

        public RedirectServer()
        {
            _listener.Start();
            int port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Endpoint = new Uri($"http://127.0.0.1:{port}/");
            _serve = ServeAsync();
        }

        public Uri Endpoint { get; }
        public int RequestCount => Volatile.Read(ref _requestCount);

        private async Task ServeAsync()
        {
            try
            {
                while (true)
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
                    _ = HandleAsync(client);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (ObjectDisposedException) when (_stop.IsCancellationRequested) { }
        }

        private async Task HandleAsync(TcpClient client)
        {
            using (client)
            using (NetworkStream stream = client.GetStream())
            {
                if (!await ReadHeadersAsync(stream, _stop.Token).ConfigureAwait(false)) return;
                int request = Interlocked.Increment(ref _requestCount);
                string response = request == 1
                    ? $"HTTP/1.1 302 Found\r\nLocation: {Endpoint}followed\r\nConnection: close\r\nContent-Length: 0\r\n\r\n"
                    : "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Type: application/json\r\nContent-Length: 73\r\n\r\n{\"playerId\":\"11111111-1111-1111-1111-111111111111\",\"confirmationRequired\":false}";
                byte[] bytes = Encoding.ASCII.GetBytes(response);
                await stream.WriteAsync(bytes, _stop.Token).ConfigureAwait(false);
            }
        }

        private static async Task<bool> ReadHeadersAsync(NetworkStream stream, CancellationToken cancel)
        {
            var received = new MemoryStream();
            byte[] buffer = new byte[4096];
            while (received.Length < 64 * 1024)
            {
                int read = await stream.ReadAsync(buffer, cancel).ConfigureAwait(false);
                if (read == 0) return false;
                received.Write(buffer, 0, read);
                if (FindHeaderEnd(received.GetBuffer(), checked((int)received.Length)) >= 0) return true;
            }
            return false;
        }

        private static int FindHeaderEnd(byte[] bytes, int length)
        {
            for (int i = 3; i < length; i++)
            {
                if (bytes[i - 3] == '\r' && bytes[i - 2] == '\n'
                    && bytes[i - 1] == '\r' && bytes[i] == '\n') return i + 1;
            }
            return -1;
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Stop();
            try { await _serve.ConfigureAwait(false); }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            _stop.Dispose();
        }
    }
}
