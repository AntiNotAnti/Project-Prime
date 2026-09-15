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
    [InlineData("https://accounts.example.test/", true)]
    [InlineData("https://accounts.example.test/base", true)]
    [InlineData("http://localhost:4711/", true)]
    [InlineData("http://127.0.0.1:4711/", true)]
    [InlineData("http://[::1]:4711/", true)]
    [InlineData("http://51.161.113.128:18085/", false)]
    public void BackendPolicyAllowsHttpsAndLoopbackHttp(string value, bool allowed)
        => Assert.Equal(allowed, AccountSession.IsAllowedBackend(new Uri(value)));

    [Fact]
    public void FreshLauncherUsesSecurePublicBackend()
        => Assert.Equal("https://rebooty.xyz/",
            MphRead.Mods.Launcher.LauncherPrefs.DefaultBackendAddress);

    [Fact]
    public void LauncherMigratesRetiredPublicHttpBackend()
    {
        string previousDirectory = MphRead.Mods.Launcher.LauncherPrefs.Directory;
        string previousBackend = MphRead.Mods.Launcher.LauncherPrefs.BackendAddress;
        string directory = Path.Combine(Path.GetTempPath(),
            "project-prime-backend-migration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            MphRead.Mods.Launcher.LauncherPrefs.Directory = directory;
            File.WriteAllText(Path.Combine(directory, "launcher.txt"),
                "backend_address=http://51.161.113.128:18085/\n");

            MphRead.Mods.Launcher.LauncherPrefs.Load();

            Assert.Equal("https://rebooty.xyz/",
                MphRead.Mods.Launcher.LauncherPrefs.BackendAddress);
            Assert.Contains("backend_address=https://rebooty.xyz/",
                File.ReadAllText(Path.Combine(directory, "launcher.txt")),
                StringComparison.Ordinal);
        }
        finally
        {
            MphRead.Mods.Launcher.LauncherPrefs.Directory = previousDirectory;
            MphRead.Mods.Launcher.LauncherPrefs.BackendAddress = previousBackend;
            Directory.Delete(directory, recursive: true);
        }
    }

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
            new HunterLicense(new PlayerId(player), "Hunter", 8, DateTimeOffset.UtcNow)
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

        await Assert.ThrowsAsync<AccountServiceException>(() =>
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

    [Fact]
    public async Task FailedSignInPreservesAnExistingRecoverableRefreshRecord()
    {
        var backend = new Uri("https://accounts.example.test/");
        var store = new FakeSecureSessionStore();
        store.Seed(backend.AbsoluteUri, SecureSessionRecordCodec.Encode(
            backend.AbsoluteUri, "recoverable-refresh"));
        using var session = new AccountSession(backend, new RecordingHandler((request, _) =>
            Task.FromResult(new HttpResponseMessage(request.RequestUri!.AbsolutePath == "/v1/auth/login"
                ? HttpStatusCode.Unauthorized : HttpStatusCode.InternalServerError))),
            sessionStore: store);

        await Assert.ThrowsAsync<AccountServiceException>(() =>
            session.SignInAsync("hunter@example.test", "wrong-password"));

        Assert.True(SecureSessionRecordCodec.TryDecode(backend.AbsoluteUri,
            store.Read(backend.AbsoluteUri)!, out string refresh));
        Assert.Equal("recoverable-refresh", refresh);
        Assert.Equal(0, store.DeleteCount);
    }

    [Fact]
    public async Task UnauthorizedIdentityLookupAfterRefreshPreservesRotatedRefreshMaterial()
    {
        var backend = new Uri("https://accounts.example.test/");
        var store = new FakeSecureSessionStore();
        store.Seed(backend.AbsoluteUri, SecureSessionRecordCodec.Encode(
            backend.AbsoluteUri, "first-refresh"));
        Guid player = Guid.NewGuid();
        using var session = new AccountSession(backend, new RecordingHandler((request, _) =>
            request.RequestUri!.AbsolutePath switch
            {
                "/v1/auth/refresh" => Task.FromResult(JsonResponse(new
                {
                    tokenType = "Bearer", accessToken = "rotated-access", expiresIn = 3600,
                    refreshToken = "rotated-refresh"
                })),
                "/v1/me" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)),
                _ => throw new InvalidOperationException($"Unexpected account path {request.RequestUri.AbsolutePath}.")
            }), sessionStore: store);

        AccountRestoreResult result = await session.RestoreDetailedAsync();

        Assert.Equal(AccountRestoreState.TemporarilyUnavailable, result.State);
        Assert.Equal(AccountFailureKind.InvalidCredential, result.Error?.Kind);
        Assert.True(SecureSessionRecordCodec.TryDecode(backend.AbsoluteUri,
            store.Read(backend.AbsoluteUri)!, out string refresh));
        Assert.Equal("rotated-refresh", refresh);
        Assert.Equal(0, store.DeleteCount);
        Assert.Null(session.Identity);
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

        AccountServiceException error = await Assert.ThrowsAsync<AccountServiceException>(() => session.RestoreAsync());
        Assert.Equal(AccountFailureKind.InvalidCredential, error.Kind);

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
        await session.SignOutAsync().WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task FailedServerRevocationStillClearsProtectedAndMemorySessions()
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
            "/v1/auth/revoke-sessions" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("{\"code\":\"service_unavailable\"}", Encoding.UTF8, "application/problem+json")
            }),
            _ => throw new InvalidOperationException($"Unexpected account path {request.RequestUri.AbsolutePath}.")
        });
        using var session = new AccountSession(backend, handler, sessionStore: store);
        await session.SignInAsync("hunter@example.test", "A-long-password-1!");

        AccountServiceException error = await Assert.ThrowsAsync<AccountServiceException>(
            () => session.RevokeSessionsAsync());

        Assert.Equal(AccountFailureKind.ServiceUnavailable, error.Kind);
        Assert.False(session.IsSignedIn);
        Assert.Null(session.Identity);
        Assert.Null(store.Read(backend.AbsoluteUri));
        Assert.False(store.LastDeleteCancellationToken.CanBeCanceled);
        RequestLog revoke = Assert.Single(handler.Snapshot(), request => request.Uri.AbsolutePath == "/v1/auth/revoke-sessions");
        Assert.Equal("Bearer access-token", revoke.Authorization);
        Assert.Empty(revoke.Body);
        await session.SignOutAsync().WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task SuccessfulServerRevocationRethrowsCleanupFailureAfterReleasingGate()
    {
        Guid player = Guid.NewGuid();
        var backend = new Uri("https://accounts.example.test/");
        var store = new FakeSecureSessionStore();
        var cleanupError = new IOException("The protected store is unavailable.");
        store.DeleteError = cleanupError;
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

        IOException error = await Assert.ThrowsAsync<IOException>(() => session.RevokeSessionsAsync());

        Assert.Same(cleanupError, error);
        Assert.False(session.IsSignedIn);
        Assert.Null(session.Identity);
        Assert.False(store.LastDeleteCancellationToken.CanBeCanceled);

        store.DeleteError = null;
        await session.SignOutAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Null(store.Read(backend.AbsoluteUri));
    }

    [Fact]
    public async Task FailedServerRevocationAggregatesCleanupFailureAfterReleasingGate()
    {
        Guid player = Guid.NewGuid();
        var backend = new Uri("https://accounts.example.test/");
        var store = new FakeSecureSessionStore();
        var cleanupError = new IOException("The protected store is unavailable.");
        store.DeleteError = cleanupError;
        var handler = new RecordingHandler((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/v1/auth/login" => Task.FromResult(JsonResponse(new
            {
                tokenType = "Bearer", accessToken = "access-token", expiresIn = 3600,
                refreshToken = "refresh-token"
            })),
            "/v1/me" => Task.FromResult(JsonResponse(new AccountIdentity(new PlayerId(player), true, true))),
            "/v1/auth/revoke-sessions" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("{\"code\":\"service_unavailable\"}", Encoding.UTF8, "application/problem+json")
            }),
            _ => throw new InvalidOperationException($"Unexpected account path {request.RequestUri.AbsolutePath}.")
        });
        using var session = new AccountSession(backend, handler, sessionStore: store);
        await session.SignInAsync("hunter@example.test", "A-long-password-1!");

        AggregateException error = await Assert.ThrowsAsync<AggregateException>(() => session.RevokeSessionsAsync());

        Assert.Equal(2, error.InnerExceptions.Count);
        Assert.IsType<AccountServiceException>(error.InnerExceptions[0]);
        Assert.Same(cleanupError, error.InnerExceptions[1]);
        Assert.False(session.IsSignedIn);
        Assert.Null(session.Identity);
        Assert.False(store.LastDeleteCancellationToken.CanBeCanceled);

        store.DeleteError = null;
        await session.SignOutAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Null(store.Read(backend.AbsoluteUri));
    }

    [Fact]
    public async Task CanceledServerRevocationStillClearsProtectedAndMemorySessions()
    {
        Guid player = Guid.NewGuid();
        var backend = new Uri("https://accounts.example.test/");
        var store = new FakeSecureSessionStore();
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new RecordingHandler(async (request, cancel) =>
        {
            switch (request.RequestUri!.AbsolutePath)
            {
                case "/v1/auth/login":
                    return JsonResponse(new
                    {
                        tokenType = "Bearer", accessToken = "access-token", expiresIn = 3600,
                        refreshToken = "refresh-token"
                    });
                case "/v1/me":
                    return JsonResponse(new AccountIdentity(new PlayerId(player), true, true));
                case "/v1/auth/revoke-sessions":
                    requestStarted.SetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancel);
                    throw new InvalidOperationException("The canceled revocation must not continue.");
                default:
                    throw new InvalidOperationException($"Unexpected account path {request.RequestUri.AbsolutePath}.");
            }
        });
        using var session = new AccountSession(backend, handler, sessionStore: store);
        await session.SignInAsync("hunter@example.test", "A-long-password-1!");
        using var stop = new CancellationTokenSource();

        Task revoke = session.RevokeSessionsAsync(stop.Token);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        stop.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => revoke);
        Assert.False(session.IsSignedIn);
        Assert.Null(session.Identity);
        Assert.Null(store.Read(backend.AbsoluteUri));
        Assert.False(store.LastDeleteCancellationToken.CanBeCanceled);

        await session.SignOutAsync().WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ServerRevocationUsesRotatedAccessTokenWhenRefreshIsRequired()
    {
        Guid player = Guid.NewGuid();
        var backend = new Uri("https://accounts.example.test/");
        var store = new FakeSecureSessionStore();
        var clock = new TestTimeProvider(DateTimeOffset.UtcNow);
        var handler = new RecordingHandler((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/v1/auth/login" => Task.FromResult(JsonResponse(new
            {
                tokenType = "Bearer", accessToken = "expiring-access-token", expiresIn = 10,
                refreshToken = "refresh-token"
            })),
            "/v1/auth/refresh" => Task.FromResult(JsonResponse(new
            {
                tokenType = "Bearer", accessToken = "rotated-access-token", expiresIn = 3600,
                refreshToken = "rotated-refresh-token"
            })),
            "/v1/me" => Task.FromResult(JsonResponse(new AccountIdentity(new PlayerId(player), true, true))),
            "/v1/auth/revoke-sessions" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)),
            _ => throw new InvalidOperationException($"Unexpected account path {request.RequestUri.AbsolutePath}.")
        });
        using var session = new AccountSession(backend, handler, clock, store);
        await session.SignInAsync("hunter@example.test", "A-long-password-1!");
        clock.Advance(TimeSpan.FromSeconds(1));

        await session.RevokeSessionsAsync();

        RequestLog refresh = Assert.Single(handler.Snapshot(), request => request.Uri.AbsolutePath == "/v1/auth/refresh");
        Assert.Contains("refresh-token", refresh.Body, StringComparison.Ordinal);
        RequestLog revoke = Assert.Single(handler.Snapshot(), request => request.Uri.AbsolutePath == "/v1/auth/revoke-sessions");
        Assert.Equal("Bearer rotated-access-token", revoke.Authorization);
        Assert.Empty(revoke.Body);
        Assert.Null(store.Read(backend.AbsoluteUri));
        await session.SignOutAsync().WaitAsync(TimeSpan.FromSeconds(2));
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
        Assert.Equal(2, handler.Snapshot().Length);
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
    }

    private static RecordingHandler SignedInHandler(Guid playerId, string accessToken = "access-token",
        string refreshToken = "refresh-token")
        => new((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/v1/auth/login" => Task.FromResult(JsonResponse(new
            {
                tokenType = "Bearer", accessToken, expiresIn = 3600, refreshToken
            })),
            "/v1/me" => Task.FromResult(JsonResponse(new AccountIdentity(new PlayerId(playerId), true, true))),
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
        public Exception? DeleteError { get; set; }
        public CancellationToken LastDeleteCancellationToken { get; private set; }

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
            LastDeleteCancellationToken = cancellationToken;
            if (DeleteError is { } error) throw error;
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
