using System.Net;
using System.Text.Json;
using FruityPrime.Server.Node.Discovery;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FruityPrime.Server.Node.Tests;

public sealed class NodeDirectoryReporterTests
{
    private static NodeDirectoryReporterOptions Options() => new(Guid.NewGuid(), new Uri("https://backend.example/"),
        new(Guid.NewGuid(), "Node", "us", "wss://node.example/v1/control", 9, "test-build", new string('a', 64), 100), "private-test-credential");
    private sealed class Handler : HttpMessageHandler
    {
        public readonly List<(HttpMethod Method, string Path, string? Scheme, string? Credential, string Node, JsonElement Body)> Requests = [];
        public readonly Queue<HttpStatusCode> Results = [];
        public bool Block;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Block) await Task.Delay(Timeout.Infinite, cancellationToken);
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Requests.Add((request.Method, request.RequestUri!.AbsolutePath, request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter, request.Headers.GetValues("X-Server-Id").Single(), document.RootElement.Clone()));
            return new(Results.TryDequeue(out var status) ? status : HttpStatusCode.OK);
        }
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 9, 8, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    [Fact]
    public async Task RegistersThenHeartbeatsExactCompatibleMetadataAndBoundedPopulation()
    {
        var options = Options(); using var handler = new Handler(); using var http = new HttpClient(handler); var clock = new Clock();
        int users = 4;
        using var reporter = new NodeDirectoryReporter(options, () => new(options.Registration.Incarnation, users, 2, 3),
            NullLogger<NodeDirectoryReporter>.Instance, http, clock);
        Assert.True(await reporter.PublishOnceAsync());
        Assert.Equal(2, handler.Requests.Count);
        var registration = handler.Requests[0];
        Assert.Equal(HttpMethod.Put, registration.Method); Assert.Equal("/v1/node/registration", registration.Path);
        Assert.Equal("Bearer", registration.Scheme); Assert.Equal("private-test-credential", registration.Credential);
        Assert.Equal(options.NodeId.ToString("D"), registration.Node);
        Assert.Equal(options.Registration.Incarnation, registration.Body.GetProperty("incarnation").GetGuid());
        Assert.Equal(9, registration.Body.GetProperty("protocolVersion").GetInt32());
        Assert.Equal(options.Registration.ContentHash, registration.Body.GetProperty("contentHash").GetString());
        Assert.Equal("wss://node.example/v1/control", registration.Body.GetProperty("publicControlUri").GetString());
        Assert.Equal(HttpMethod.Post, handler.Requests[1].Method);
        Assert.Equal("/v1/node/heartbeat", handler.Requests[1].Path);
        users = 5; Assert.True(await reporter.PublishOnceAsync());
        Assert.Equal(3, handler.Requests.Count); Assert.Equal(5, handler.Requests[2].Body.GetProperty("onlineUsers").GetInt32());
        Assert.Equal(clock.Now, reporter.LastSuccessfulHeartbeat); Assert.Equal(TimeSpan.FromSeconds(20), reporter.NextDelay());
        clock.Now = clock.Now.AddSeconds(61); Assert.True(await reporter.PublishOnceAsync());
        Assert.Equal(HttpMethod.Put, handler.Requests[3].Method);
        Assert.DoesNotContain("private-test-credential", options.ToString());
    }

    [Fact]
    public async Task BackendOutageAndRevocationRetryWithoutTouchingLiveStateAndRecoverRegistration()
    {
        var options = Options(); using var handler = new Handler(); using var http = new HttpClient(handler);
        int liveUsers = 6;
        using var reporter = new NodeDirectoryReporter(options, () => new(options.Registration.Incarnation, liveUsers, 1, 1),
            NullLogger<NodeDirectoryReporter>.Instance, http);
        Assert.True(await reporter.PublishOnceAsync());
        handler.Results.Enqueue(HttpStatusCode.ServiceUnavailable);
        Assert.False(await reporter.PublishOnceAsync());
        Assert.Equal(6, liveUsers); Assert.Equal(1, reporter.ConsecutiveFailures);
        Assert.InRange(reporter.NextDelay().TotalSeconds, .8, 1.2);
        handler.Results.Enqueue(HttpStatusCode.Unauthorized);
        Assert.False(await reporter.PublishOnceAsync());
        Assert.Equal(6, liveUsers);
        Assert.True(await reporter.PublishOnceAsync());
        Assert.Equal(HttpMethod.Put, handler.Requests[^2].Method);
        Assert.Equal(0, reporter.ConsecutiveFailures); Assert.Null(reporter.LastFailure);
        for (int i = 0; i < 20; i++) { handler.Results.Enqueue(HttpStatusCode.BadGateway); Assert.False(await reporter.PublishOnceAsync()); }
        Assert.Equal(16, reporter.ConsecutiveFailures); Assert.InRange(reporter.NextDelay().TotalSeconds, 24, 30);
    }

    [Fact]
    public async Task DeadlineAndCancellationBoundNetworkWork()
    {
        var original = Options();
        var options = new NodeDirectoryReporterOptions(original.NodeId, original.Backend, original.Registration, "secret")
            { RequestTimeout = TimeSpan.FromMilliseconds(100) };
        using var handler = new Handler { Block = true }; using var http = new HttpClient(handler);
        using var reporter = new NodeDirectoryReporter(options, () => new(options.Registration.Incarnation, 0, 0, 0),
            NullLogger<NodeDirectoryReporter>.Instance, http);
        Assert.False(await reporter.PublishOnceAsync().WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal("timeout", reporter.LastFailure);
        using var stop = new CancellationTokenSource(); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reporter.PublishOnceAsync(stop.Token));
        Assert.Equal(1, reporter.ConsecutiveFailures);
    }

    [Fact]
    public void CredentialsUseOneBoundedEnvironmentOrFileSource()
    {
        Assert.Equal("secret", NodeDirectoryReporterOptions.ReadCredential(name => name == "PRIME_NODE_DIRECTORY_SECRET" ? "secret" : null));
        Assert.Throws<ArgumentException>(() => NodeDirectoryReporterOptions.ReadCredential(_ => "both"));
        Assert.Throws<ArgumentException>(() => NodeDirectoryReporterOptions.ReadCredential(name => name == "PRIME_NODE_DIRECTORY_SECRET" ? "bad\r\nheader" : null));
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".secret");
        try
        {
            File.WriteAllText(path, "file-secret\n");
            Assert.Equal("file-secret", NodeDirectoryReporterOptions.ReadCredential(name => name == "PRIME_NODE_DIRECTORY_SECRET_FILE" ? path : null));
            File.WriteAllText(path, new string('a', 4097));
            Assert.Throws<ArgumentException>(() => NodeDirectoryReporterOptions.ReadCredential(name => name == "PRIME_NODE_DIRECTORY_SECRET_FILE" ? path : null));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task InvalidPopulationAndRedirectsFailClosed()
    {
        var options = Options(); using var handler = new Handler(); using var http = new HttpClient(handler);
        int users = 101;
        using var reporter = new NodeDirectoryReporter(options, () => new(options.Registration.Incarnation, users, 0, 0),
            NullLogger<NodeDirectoryReporter>.Instance, http);
        Assert.False(await reporter.PublishOnceAsync()); Assert.Empty(handler.Requests);
        users = 1; handler.Results.Enqueue(HttpStatusCode.Redirect);
        Assert.False(await reporter.PublishOnceAsync()); Assert.Single(handler.Requests);
        Assert.Throws<ArgumentException>(() => new NodeDirectoryReporterOptions(options.NodeId, new Uri("http://untrusted.example/"),
            options.Registration, "secret").Validate());
    }
}
