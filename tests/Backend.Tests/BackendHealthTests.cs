using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MphRead.Backend.Data;
using Xunit;

namespace MphRead.Backend.Tests;

public sealed class BackendHealthTests
{
    [Fact]
    public async Task LivenessDoesNotRequireDatabaseAndReadinessFailsBeforeInitialization()
    {
        using var factory = new BackendFactory();
        using HttpClient client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
    }

    [Fact]
    public async Task LivenessBypassesRequestConcurrencySaturation()
    {
        using var factory = new BackendFactory();
        using HttpClient client = factory.CreateClient();
        var limiter = factory.Services.GetRequiredService<ConcurrencyLimiter>();
        var held = new List<RateLimitLease>();
        try
        {
            for (int i = 0; i < 128; i++) held.Add(await limiter.AcquireAsync(1));
            Assert.All(held, lease => Assert.True(lease.IsAcquired));
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        }
        finally
        {
            foreach (RateLimitLease lease in held) lease.Dispose();
        }
    }

    [Fact]
    public async Task LivenessStillRejectsPlainHttpOutsideTestingAndLoopbackDevelopment()
    {
        using var factory = new BackendFactory(environment: "Development");
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://198.51.100.10")
        });

        using HttpResponseMessage response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("https_required", await ProblemCode(response));
        Assert.NotEmpty(response.Headers.GetValues("X-Request-Id").Single());
    }

    [Fact]
    public async Task SlowBodyReaderHoldsLeaseWhileLivenessAndOtherRequestsStayBounded()
    {
        using var factory = new BackendFactory(configure: services =>
        {
            services.RemoveAll<ConcurrencyLimiter>();
            services.AddSingleton(new ConcurrencyLimiter(new ConcurrencyLimiterOptions
            {
                PermitLimit = 1, QueueLimit = 0
            }));
        });
        using HttpClient client = factory.CreateClient();
        ConcurrencyLimiter limiter = factory.Services.GetRequiredService<ConcurrencyLimiter>();
        using RateLimitLease held = await limiter.AcquireAsync(1);
        Assert.True(held.IsAcquired);
        using var body = new SlowBodyContent("{\"email\":\"slow@example.test\"}");
        body.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/auth/login") { Content = body };
        string requestId = Guid.NewGuid().ToString("D");
        request.Headers.Add("X-Request-Id", requestId);

        Task<HttpResponseMessage> blocked = client.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead);
        await body.FirstChunkWritten.Task.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            using HttpResponseMessage live = await client.GetAsync("/health/live");
            Assert.Equal(HttpStatusCode.OK, live.StatusCode);

            using HttpResponseMessage busy = await client.PostAsJsonAsync("/v1/auth/login",
                new { email = "other@example.test", password = "Strong-Test-Password123!" });
            Assert.Equal(HttpStatusCode.ServiceUnavailable, busy.StatusCode);
            Assert.NotEmpty(busy.Headers.GetValues("X-Request-Id").Single());

            held.Dispose();
            body.Release.TrySetResult(true);
            using HttpResponseMessage completed = await blocked.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(HttpStatusCode.Unauthorized, completed.StatusCode);
            Assert.Equal(requestId, completed.Headers.GetValues("X-Request-Id").Single());
        }
        finally
        {
            body.Release.TrySetResult(true);
        }
    }

    [Fact]
    public async Task ReadinessRequiresProjectionRebuildAndTurnsReadyAfterInitialization()
    {
        using var factory = new BackendFactory();
        using HttpClient client = factory.CreateDatabaseClient();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            BackendDbContext db = scope.ServiceProvider.GetRequiredService<BackendDbContext>();
            CareerProjectionState state = await db.ProjectionStates.SingleAsync(x => x.Id == 1);
            state.RebuildRequired = false;
            await db.SaveChangesAsync();
        }
        HttpResponseMessage response = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"status\":\"ready\"}", await response.Content.ReadAsStringAsync());
    }

    private sealed class SlowBodyContent(string value) : HttpContent
    {
        private readonly byte[] _bytes = Encoding.UTF8.GetBytes(value);
        public TaskCompletionSource<bool> FirstChunkWritten { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            await stream.WriteAsync(_bytes);
            FirstChunkWritten.TrySetResult(true);
            await Release.Task;
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private static async Task<string?> ProblemCode(HttpResponseMessage response)
    {
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    [PostgresFact]
    public async Task PostgreSqlReadinessRejectsUnknownMigrationHistory()
    {
        string admin = File.ReadAllText(Environment.GetEnvironmentVariable("PRIME_TEST_POSTGRES_FILE")!).Trim();
        using var factory = new BackendFactory(postgresConnection: admin);
        using HttpClient client = factory.CreateDatabaseClient();
        using var scope = factory.Services.CreateScope();
        BackendDbContext db = scope.ServiceProvider.GetRequiredService<BackendDbContext>();
        CareerProjectionState state = await db.ProjectionStates.SingleAsync(x => x.Id == 1);
        state.RebuildRequired = false;
        await db.SaveChangesAsync();
        Assert.True((await scope.ServiceProvider.GetRequiredService<BackendReadinessChecker>().CheckAsync()).Ready);

        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO prime."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            VALUES ('99999999999999_UnknownFutureMigration', '99.0.0')
            """);
        BackendReadinessResult result = await scope.ServiceProvider
            .GetRequiredService<BackendReadinessChecker>().CheckAsync();
        Assert.False(result.Ready);
        Assert.Equal("migration_sequence", result.Failure);
    }
}
