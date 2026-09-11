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
using Microsoft.Extensions.Configuration;
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
    public void GuestPartitionIgnoresAuthenticationIdentityAndUsesCanonicalRemoteAddress()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:192.0.2.10");
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D"))], "Bearer"));
        string first = BackendSecurity.IpPartitionKey(context);

        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D"))], "Bearer"));
        Assert.Equal(first, BackendSecurity.IpPartitionKey(context));
        Assert.Equal("ip:192.0.2.10", first);
    }

    [Fact]
    public void PreAuthMachinePartitionIgnoresCallerControlledServerId()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:192.0.2.10");
        context.Request.Headers["X-Server-Id"] = Guid.NewGuid().ToString("D");
        string first = BackendSecurity.MachinePartitionKey(context);

        context.Request.Headers["X-Server-Id"] = Guid.NewGuid().ToString("D");
        Assert.Equal(first, BackendSecurity.MachinePartitionKey(context));
        Assert.Equal("machine-ip:192.0.2.10", first);
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
    public void RemoteDevelopmentHttpIsRetiredEvenWhenOptedIn()
    {
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(BackendSecurity.TemporaryDevelopmentBackendHost);
        Assert.False(BackendSecurity.IsExplicitRemoteHttpDevelopmentRequest(context, new BackendSecurityOptions()));
        Assert.False(BackendSecurity.IsExplicitRemoteHttpDevelopmentRequest(context,
            new BackendSecurityOptions { AllowRemoteHttp = true }));
        context.Request.Host = new HostString("51.161.113.127");
        Assert.False(BackendSecurity.IsExplicitRemoteHttpDevelopmentRequest(context,
            new BackendSecurityOptions { AllowRemoteHttp = true }));
        Assert.Throws<InvalidOperationException>(() => BackendSecurity.ValidateCommon(
            new BackendSecurityOptions { AllowRemoteHttp = true }));
    }

    [Fact]
    public void ProductionListenersRequireHttpsIncludingDefaultsAndMixedBindings()
    {
        Assert.Throws<InvalidOperationException>(() => ValidateListeners([]));
        Assert.Throws<InvalidOperationException>(() => ValidateListeners(new() { ["urls"] = "http://127.0.0.1:8080" }));
        Assert.Throws<InvalidOperationException>(() => ValidateListeners(new()
        {
            ["urls"] = "https://0.0.0.0:8443;http://127.0.0.1:8080"
        }));
        Assert.Throws<InvalidOperationException>(() => ValidateListeners(new() { ["HTTP_PORTS"] = "8080;8081" }));

        ValidateListeners(new() { ["urls"] = "https://0.0.0.0:8443" });
        ValidateListeners(new() { ["HTTPS_PORTS"] = "8443;8444" });
    }

    [Fact]
    public void KestrelEndpointsTakePrecedenceAndTrustedTlsTerminationIsExplicit()
    {
        ValidateListeners(new()
        {
            ["urls"] = "http://127.0.0.1:8080",
            ["Kestrel:Endpoints:Public:Url"] = "https://*:8443"
        });
        Assert.Throws<InvalidOperationException>(() => ValidateListeners(new()
        {
            ["urls"] = "https://*:8443",
            ["Kestrel:Endpoints:Public:Url"] = "http://*:8080"
        }));
        Assert.Throws<InvalidOperationException>(() => ValidateListeners(new()
        {
            ["Kestrel:Endpoints:Public:Protocols"] = "Http1"
        }));
        ValidateListeners(new() { ["urls"] = "http://*:8080" }, new BackendSecurityOptions
        {
            TrustedProxies = ["192.0.2.10"]
        });
    }

    [Fact]
    public void ProductionValidationRequiresSecureOriginsDurableKeysProvidersAndCredentials()
    {
        var security = new BackendSecurityOptions { PublicUrl = "https://backend.example.test" };
        var accounts = new AccountOptions { RequireConfirmedEmail = true, DataProtectionKeyPath = "/var/lib/project-prime/keys" };
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
        accounts.DataProtectionKeyPath = "/var/lib/project-prime/keys";
        Assert.Throws<InvalidOperationException>(() => BackendSecurity.ValidateProduction(
            security, accounts, tickets, servers, false, true));
        Assert.Throws<InvalidOperationException>(() => BackendSecurity.ValidateProduction(
            security, accounts, tickets, servers, true, false));

        servers.Servers[0].ApiKeySha256 = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes("test-only-server-credential-not-for-production-123456")));
        Assert.Throws<InvalidOperationException>(() => BackendSecurity.ValidateProduction(
            security, accounts, tickets, servers, true, true));
    }

    private static void ValidateListeners(Dictionary<string, string?> values,
        BackendSecurityOptions? security = null)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        BackendSecurity.ValidateProductionListeners(configuration, security ?? new());
    }
}
