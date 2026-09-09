using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using FruityPrime.Server.Node.Lobbies;
using FruityPrime.Server.Node.Sessions;
using FruityPrime.Server.Node.Workers;
using FruityPrime.Server.Shared;

namespace FruityPrime.Server.Node.Discovery;

public sealed class NodeDirectorySettings
{
    public bool Enabled { get; set; }
    public string BackendUri { get; set; } = "http://51.161.113.128:18085/";
    public string PublicControlUri { get; set; } = "";
    public string Name { get; set; } = "";
    public string Region { get; set; } = "";
}

public sealed record NodeDirectoryRegistration(Guid Incarnation, string Name, string Region, string PublicControlUri,
    int ProtocolVersion, string BuildVersion, string ContentHash, int Capacity, string[]? MapKeys = null);
public sealed record NodeDirectoryHeartbeat(Guid Incarnation, int OnlineUsers, int LobbyCount, int ActiveMatches);

public sealed class NodeDirectoryReporterOptions
{
    private const string TemporaryDevelopmentBackendHost = "51.161.113.128";
    public Guid NodeId { get; }
    public Uri Backend { get; }
    public NodeDirectoryRegistration Registration { get; }
    private readonly string[]? _mapKeys;
    internal string Credential { get; }
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(20);
    public override string ToString() => $"NodeDirectoryReporterOptions {{ NodeId = {NodeId} }}";
    public NodeDirectoryReporterOptions(Guid nodeId, Uri backend, NodeDirectoryRegistration registration, string credential)
    {
        NodeId = nodeId; Backend = backend;
        _mapKeys = registration.MapKeys?.OrderBy(key => key, StringComparer.Ordinal).ToArray();
        Registration = registration with { MapKeys = _mapKeys?.ToArray() };
        Credential = credential;
    }
    internal NodeDirectoryRegistration SnapshotRegistration()
        => Registration with { MapKeys = _mapKeys?.ToArray() };
    public void Validate()
    {
        static bool Text(string? value, int max) => value is { Length: > 0 } && value.Length <= max
            && value.All(c => Char.IsAscii(c) && !Char.IsControl(c));
        static bool MapKeyText(string? value) => value is { Length: > 0 and <= 128 }
            && !String.IsNullOrWhiteSpace(value) && value.All(c => c is >= ' ' and <= '~');
        if (NodeId == Guid.Empty || !Backend.IsAbsoluteUri || Backend.UserInfo.Length != 0
            || Backend.Query.Length != 0 || Backend.Fragment.Length != 0
            || Backend.Scheme != "https" && !(Backend.Scheme == "http"
                && (Backend.IsLoopback || string.Equals(Backend.Host, TemporaryDevelopmentBackendHost,
                    StringComparison.OrdinalIgnoreCase)))
            || Registration.Incarnation == Guid.Empty || !Text(Registration.Name, 64) || !Text(Registration.Region, 32)
            || !Text(Registration.BuildVersion, 64) || Registration.ProtocolVersion is < 1 or > 65535
            || Registration.ContentHash is not { Length: 64 } || !Registration.ContentHash.All(Char.IsAsciiHexDigit)
            || Registration.Capacity is < 1 or > 10000 || Registration.PublicControlUri is not { Length: <= 256 }
            || !Uri.TryCreate(Registration.PublicControlUri, UriKind.Absolute, out Uri? control)
            || control.Scheme != "wss" || control.UserInfo.Length != 0 || control.Query.Length != 0 || control.Fragment.Length != 0
            || Registration.MapKeys is { Length: > 256 }
            || Registration.MapKeys is { } keys && (keys.Any(key => !MapKeyText(key))
                || keys.Distinct(StringComparer.Ordinal).Count() != keys.Length)
            || !Text(Credential, 4096) || String.IsNullOrWhiteSpace(Credential)
            || RequestTimeout < TimeSpan.FromMilliseconds(100) || RequestTimeout > TimeSpan.FromSeconds(10)
            || HeartbeatInterval < TimeSpan.FromSeconds(5) || HeartbeatInterval > TimeSpan.FromSeconds(30))
            throw new ArgumentException("Invalid Node directory settings or credential.");
    }

    public static string ReadCredential(Func<string, string?>? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariable;
        string? value = environment("PRIME_NODE_DIRECTORY_SECRET");
        string? file = environment("PRIME_NODE_DIRECTORY_SECRET_FILE");
        if (!String.IsNullOrEmpty(value) && !String.IsNullOrEmpty(file))
            throw new ArgumentException("Configure one Node directory credential source.");
        if (!String.IsNullOrEmpty(file))
        {
            var info = new FileInfo(file);
            if (!info.Exists || info.Length > 4096) throw new ArgumentException("Node directory credential file is missing or exceeds its bound.");
            value = File.ReadAllText(file).TrimEnd('\r', '\n');
        }
        if (String.IsNullOrWhiteSpace(value) || value.Length > 4096 || value.Any(c => !Char.IsAscii(c) || Char.IsControl(c)))
            throw new ArgumentException("Node directory credential is missing or invalid.");
        return value;
    }
}

/// <summary>Discovery is advisory. HTTP failure changes only reporting state, never live session/match authority.</summary>
public sealed class NodeDirectoryReporter : BackgroundService
{
    private readonly NodeDirectoryReporterOptions _options;
    private readonly Func<NodeDirectoryHeartbeat> _population;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly TimeProvider _clock;
    private readonly ILogger<NodeDirectoryReporter> _logger;
    private bool _registered;
    private DateTimeOffset _registerDue;
    private long _lastSuccess;
    private int _failures;
    private readonly SemaphoreSlim _publish = new(1);
    public DateTimeOffset? LastSuccessfulHeartbeat => Interlocked.Read(ref _lastSuccess) is long value && value != 0
        ? DateTimeOffset.FromUnixTimeMilliseconds(value) : null;
    public int ConsecutiveFailures => Volatile.Read(ref _failures);
    public string? LastFailure { get; private set; }

    public NodeDirectoryReporter(NodeDirectoryReporterOptions options, Func<NodeDirectoryHeartbeat> population,
        ILogger<NodeDirectoryReporter> logger, HttpClient? http = null, TimeProvider? clock = null)
    {
        options.Validate(); _options = options; _population = population; _logger = logger; _clock = clock ?? TimeProvider.System;
        _http = http ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
        _ownsHttp = http == null;
    }

    public async Task<bool> PublishOnceAsync(CancellationToken cancellationToken = default)
    {
        await _publish.WaitAsync(cancellationToken);
        try
        {
            NodeDirectoryHeartbeat population = _population();
            if (population.Incarnation != _options.Registration.Incarnation || population.OnlineUsers < 0
                || population.OnlineUsers > _options.Registration.Capacity || population.LobbyCount is < 0 or > 10000
                || population.ActiveMatches is < 0 or > 10000)
                throw new InvalidOperationException("Invalid Node directory population snapshot.");
            if (!_registered || _clock.GetUtcNow() >= _registerDue)
            {
                await SendAsync(HttpMethod.Put, "v1/node/registration", _options.SnapshotRegistration(), cancellationToken);
                _registered = true; _registerDue = _clock.GetUtcNow().AddSeconds(60);
            }
            await SendAsync(HttpMethod.Post, "v1/node/heartbeat", population, cancellationToken);
            Interlocked.Exchange(ref _lastSuccess, _clock.GetUtcNow().ToUnixTimeMilliseconds());
            Volatile.Write(ref _failures, 0); LastFailure = null;
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            _registered = false;
            LastFailure = error is OperationCanceledException ? "timeout" : error.GetType().Name;
            int failures = Math.Min(16, ConsecutiveFailures + 1); Volatile.Write(ref _failures, failures);
            // Never log exception text, request headers, bodies, or operator credentials.
            if (failures == 1 || failures == 16) _logger.LogWarning("Node directory update failed ({Category}); live sessions continue.", LastFailure);
            return false;
        }
        finally { _publish.Release(); }
    }

    private async Task SendAsync<T>(HttpMethod method, string relative, T body, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_options.RequestTimeout);
        var baseUri = new Uri(_options.Backend.AbsoluteUri.TrimEnd('/') + "/");
        using var request = new HttpRequestMessage(method, new Uri(baseUri, relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.Credential);
        request.Headers.Add("X-Server-Id", _options.NodeId.ToString("D"));
        request.Content = JsonContent.Create(body);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException("Directory rejected update.", null, response.StatusCode);
    }

    public TimeSpan NextDelay()
    {
        if (ConsecutiveFailures == 0) return _options.HeartbeatInterval;
        double seconds = Math.Min(30, Math.Pow(2, Math.Min(ConsecutiveFailures - 1, 5)));
        return TimeSpan.FromSeconds(Math.Min(30, seconds * RandomNumberGenerator.GetInt32(800, 1201) / 1000.0));
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await PublishOnceAsync(stoppingToken);
                await Task.Delay(NextDelay(), _clock, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
    public override void Dispose()
    {
        base.Dispose();
        if (_ownsHttp) _http.Dispose();
    }
}

public static class NodeDirectoryRegistrationExtensions
{
    public static IServiceCollection AddNodeDirectoryReporter(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection("Node:Directory").Get<NodeDirectorySettings>() ?? new();
        if (!settings.Enabled) return services;
        string credential = NodeDirectoryReporterOptions.ReadCredential();
        services.AddSingleton(sp =>
        {
            var workers = sp.GetRequiredService<WorkerManager>();
            var catalog = sp.GetRequiredService<NodeContentCatalog>();
            string[] mapKeys = catalog.Maps.OrderBy(key => key, StringComparer.Ordinal).ToArray();
            var content = mapKeys.Select(catalog.Get).Select(value => (value.ProtocolVersion, value.BuildVersion, value.ContentHash)).Distinct().ToArray();
            if (content.Length != 1) throw new ArgumentException("Directory publication requires exactly one Node content/build identity.");
            var identity = content[0];
            var registration = new NodeDirectoryRegistration(workers.NodeIncarnation, settings.Name, settings.Region,
                settings.PublicControlUri, identity.ProtocolVersion, identity.BuildVersion, identity.ContentHash,
                configuration.GetValue("Node:MaximumSessions", 1024), mapKeys);
            var options = new NodeDirectoryReporterOptions(workers.NodeId.Value, new Uri(settings.BackendUri, UriKind.Absolute), registration, credential);
            var sessions = sp.GetRequiredService<NodeSessionManager>();
            var lobbies = sp.GetRequiredService<LobbyManager>();
            return new NodeDirectoryReporter(options, () => new NodeDirectoryHeartbeat(workers.NodeIncarnation,
                sessions.Count, lobbies.Count, workers.Snapshot().Sum(worker => worker.Matches.Count(pair =>
                    pair.Value is MatchStatus.Starting or MatchStatus.Ready or MatchStatus.Running))),
                sp.GetRequiredService<ILogger<NodeDirectoryReporter>>(), clock: sp.GetRequiredService<TimeProvider>());
        });
        services.AddHostedService(sp => sp.GetRequiredService<NodeDirectoryReporter>());
        return services;
    }
}
