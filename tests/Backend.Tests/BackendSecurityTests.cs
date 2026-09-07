using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using MphRead.Backend.Identity;
using MphRead.Backend.Tickets;
using Xunit;

namespace MphRead.Backend.Tests;

public sealed class BackendSecurityTests
{
    [Fact]
    public async Task GlobalProtectionBoundsConcurrentWorkWithoutRequestQuota()
    {
        using var factory = new BackendFactory();
        using var client = factory.CreateDatabaseClient();
        var limiter = factory.Services.GetRequiredService<ConcurrencyLimiter>();
        var held = new List<RateLimitLease>();
        try
        {
            for (int i = 0; i < 128; i++)
            {
                RateLimitLease lease = await limiter.AcquireAsync(1);
                Assert.True(lease.IsAcquired);
                held.Add(lease);
            }
            using RateLimitLease rejected = await limiter.AcquireAsync(1);
            Assert.False(rejected.IsAcquired);
        }
        finally
        {
            foreach (RateLimitLease lease in held) lease.Dispose();
        }

        for (int i = 0; i < 1_000; i++)
        {
            using RateLimitLease lease = await limiter.AcquireAsync(1);
            Assert.True(lease.IsAcquired);
        }
    }

    [Fact]
    public async Task EndpointLimitsAreSeparateAndUntrustedForwardedAddressesCannotRotatePartition()
    {
        using var factory = new BackendFactory();
        using var client = factory.CreateDatabaseClient();
        for (int i = 0; i < 20; i++)
        {
            using var login = new HttpRequestMessage(HttpMethod.Post, "/v1/auth/login")
            {
                Content = JsonContent.Create(new { Email = "invalid", Password = "invalid" })
            };
            login.Headers.Add("X-Forwarded-For", $"203.0.113.{i + 1}");
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(login)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await client.PostAsJsonAsync("/v1/auth/login", new { Email = "invalid", Password = "invalid" })).StatusCode);

        for (int i = 0; i < 20; i++)
        {
            Assert.Equal(HttpStatusCode.BadRequest,
                (await client.PostAsJsonAsync("/v1/auth/register", new { Email = "invalid", Password = "invalid", DisplayName = "" })).StatusCode);
        }
        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await client.PostAsJsonAsync("/v1/auth/register", new { Email = "invalid", Password = "invalid", DisplayName = "" })).StatusCode);
    }

    [Fact]
    public void PartitionUsesRouteAndAuthenticatedPlayerWithCanonicalIpFallback()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/v1/players/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/license";
        context.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:192.0.2.10");
        context.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask,
            RoutePatternFactory.Parse("/v1/players/{id}/license"), 0, EndpointMetadataCollection.Empty, "license"));
        Assert.Equal("GET:/v1/players/{id}/license|ip:192.0.2.10", BackendSecurity.EndpointPartitionKey(context));

        Guid player = Guid.NewGuid();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, player.ToString("D"))], "Bearer"));
        context.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.9");
        Assert.Equal($"GET:/v1/players/{{id}}/license|player:{player:D}", BackendSecurity.EndpointPartitionKey(context));
    }

    [Fact]
    public void ForwardingAcceptsOnlyExplicitCanonicalProxyAddresses()
    {
        var forwarding = new ForwardedHeadersOptions();
        BackendSecurity.ConfigureForwarding(forwarding, new BackendSecurityOptions
        {
            TrustedProxies = ["192.0.2.5", "2001:db8::1"]
        });
        Assert.Empty(forwarding.KnownIPNetworks);
        Assert.Equal([IPAddress.Parse("192.0.2.5"), IPAddress.Parse("2001:db8::1")], forwarding.KnownProxies);
        Assert.Throws<InvalidOperationException>(() => BackendSecurity.ValidateCommon(new BackendSecurityOptions
        {
            TrustedProxies = ["192.0.2.005"]
        }));
    }

    [Fact]
    public void DevelopmentHttpRequiresExplicitLoopbackModeOnBothEnds()
    {
        var context = new DefaultHttpContext();
        context.Connection.LocalIpAddress = IPAddress.Loopback;
        context.Connection.RemoteIpAddress = IPAddress.IPv6Loopback;
        Assert.False(BackendSecurity.IsExplicitLoopbackDevelopmentRequest(context, new BackendSecurityOptions()));
        Assert.True(BackendSecurity.IsExplicitLoopbackDevelopmentRequest(context,
            new BackendSecurityOptions { AllowLoopbackHttp = true }));
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.1");
        Assert.False(BackendSecurity.IsExplicitLoopbackDevelopmentRequest(context,
            new BackendSecurityOptions { AllowLoopbackHttp = true }));
    }

    [Fact]
    public void ProductionValidationRequiresSecureOriginsDurableKeysProvidersAndCredentials()
    {
        var security = new BackendSecurityOptions { PublicUrl = "https://backend.example.test" };
        var accounts = new AccountOptions { RequireConfirmedEmail = true, DataProtectionKeyPath = "/var/lib/prime-hunters/keys" };
        var tickets = new TicketOptions { Issuer = "https://backend.example.test", KeyId = "current", SigningKeyPemPath = "/run/secrets/ticket.pem" };
        var servers = new GameServerOptions { Servers = [new GameServerRegistration
        {
            Id = Guid.NewGuid(), Enabled = true,
            ApiKeySha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("independent-production-secret-a")))
        }] };
        BackendSecurity.ValidateProduction(security, accounts, tickets, servers, true, true);

        security.PublicUrl = "http://backend.example.test";
        Assert.Throws<InvalidOperationException>(() => BackendSecurity.ValidateProduction(
            security, accounts, tickets, servers, true, true));
        security.PublicUrl = "https://backend.example.test";
        accounts.DataProtectionKeyPath = "relative/keys";
        Assert.Throws<InvalidOperationException>(() => BackendSecurity.ValidateProduction(
            security, accounts, tickets, servers, true, true));
        accounts.DataProtectionKeyPath = "/var/lib/prime-hunters/keys";
        Assert.Throws<InvalidOperationException>(() => BackendSecurity.ValidateProduction(
            security, accounts, tickets, servers, false, true));
        Assert.Throws<InvalidOperationException>(() => BackendSecurity.ValidateProduction(
            security, accounts, tickets, servers, true, false));

        servers.Servers[0].ApiKeySha256 = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes("test-only-server-credential-not-for-production-123456")));
        Assert.Throws<InvalidOperationException>(() => BackendSecurity.ValidateProduction(
            security, accounts, tickets, servers, true, true));
    }
}
