using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using ProjectPrime.Server.Node.Sessions;
using ProjectPrime.Server.Shared;

namespace ProjectPrime.Server.Node.Presence;

/// <summary>Configuration for the optional, Backend-owned presence publisher.
/// Its credential is the same Node credential used by directory registration;
/// no account or session secret is ever put in the report body.</summary>
public sealed class NodePresenceReporterOptions
{
    public Guid NodeId { get; }
    public Uri Backend { get; }
    public Guid Incarnation { get; }
    public string Region { get; }
    internal string Credential { get; }
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan KeepaliveInterval { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan MinimumReportInterval { get; init; } = TimeSpan.FromSeconds(2);

    public NodePresenceReporterOptions(Guid nodeId, Uri backend, Guid incarnation,
        string region, string credential)
    {
        NodeId = nodeId;
        Backend = backend;
        Incarnation = incarnation;
        Region = region;
        Credential = credential;
    }

    public void Validate()
    {
        if (NodeId == Guid.Empty || Incarnation == Guid.Empty
            || !Backend.IsAbsoluteUri || Backend.UserInfo.Length != 0
            || Backend.Query.Length != 0 || Backend.Fragment.Length != 0
            || Backend.Scheme != Uri.UriSchemeHttps
                && !(Backend.Scheme == Uri.UriSchemeHttp && Backend.IsLoopback)
            || !PresenceContract.IsValidRegion(Region)
            || Credential is not { Length: > 0 and <= 4096 }
            || Credential.Any(c => !char.IsAscii(c) || char.IsControl(c))
            || RequestTimeout < TimeSpan.FromMilliseconds(100)
            || RequestTimeout > TimeSpan.FromSeconds(10)
            || KeepaliveInterval < TimeSpan.FromSeconds(1)
            || KeepaliveInterval > TimeSpan.FromSeconds(15)
            || MinimumReportInterval < TimeSpan.FromSeconds(2)
            || MinimumReportInterval > KeepaliveInterval)
            throw new ArgumentException("Invalid Node presence reporter settings.");
    }

    public override string ToString()
        => $"NodePresenceReporterOptions {{ NodeId = {NodeId}, Incarnation = {Incarnation} }}";
}

/// <summary>
/// Publishes the Node's immutable public-session projection without running on
/// a Worker simulation lane. Revision changes are coalesced for two seconds;
/// an unchanged projection still receives a bounded keepalive at most every
/// fifteen seconds.
/// </summary>
public sealed class NodePresenceReporter : BackgroundService
{
    // A healthy projection is sampled at the same floor imposed on burst
    // reports. This keeps an idle Node from scanning every session four times
    // per second while still discovering ordinary presence changes promptly.
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private readonly NodePresenceReporterOptions _options;
    private readonly Func<NodePresenceSnapshot> _projection;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly TimeProvider _clock;
    private readonly ILogger<NodePresenceReporter> _logger;
    private readonly Func<bool> _registered;
    private readonly Func<NodeReadinessResult> _readiness;
    private readonly SemaphoreSlim _publish = new(1, 1);
    private DateTimeOffset? _lastReportAt;
    private long _lastReportedRevision = -1;
    private bool _wasRegistered;
    private bool _stopping;
    private int _failures;
    private string? _lastFailure;

    public NodePresenceReporter(NodePresenceReporterOptions options,
        Func<NodePresenceSnapshot> projection,
        ILogger<NodePresenceReporter>? logger = null, HttpClient? http = null,
        TimeProvider? clock = null, Func<bool>? registered = null,
        Func<NodeReadinessResult>? readiness = null)
    {
        ArgumentNullException.ThrowIfNull(projection);
        options.Validate();
        _options = options;
        _projection = projection;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<NodePresenceReporter>.Instance;
        _clock = clock ?? TimeProvider.System;
        _registered = registered ?? (() => true);
        _readiness = readiness ?? (() => NodeReadinessResult.Ready);
        _http = http ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = Timeout.InfiniteTimeSpan };
        _ownsHttp = http == null;
    }

    public int ConsecutiveFailures => Volatile.Read(ref _failures);
    public string? LastFailure => Volatile.Read(ref _lastFailure);
    public long LastReportedRevision => Interlocked.Read(ref _lastReportedRevision);
    public DateTimeOffset? LastReportAt => _lastReportAt;

    /// <summary>
    /// Attempts one report. A false result means the Node is not currently
    /// ready/registered, the coalescing window has not elapsed, or the
    /// Backend rejected/unreachable report; all cases remain non-authoritative.
    /// </summary>
    public async Task<bool> PublishOnceAsync(CancellationToken cancellationToken = default)
    {
        await _publish.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_stopping) return false;
            if (!_registered())
            {
                _wasRegistered = false;
                return false;
            }
            if (!_readiness().IsReady)
            {
                _wasRegistered = false;
                return false;
            }

            NodePresenceSnapshot snapshot = _projection();
            ValidateSnapshot(snapshot);
            DateTimeOffset now = _clock.GetUtcNow();
            bool registrationChanged = !_wasRegistered;
            bool changed = _lastReportedRevision < 0
                || snapshot.Revision != _lastReportedRevision
                || registrationChanged;
            _wasRegistered = true;
            bool keepaliveDue = _lastReportAt is not { } last
                || now - last >= _options.KeepaliveInterval;
            if (!changed && !keepaliveDue) return false;
            if (changed && !registrationChanged && _lastReportAt is { } previous
                && now - previous < _options.MinimumReportInterval)
                return false;

            await SendAsync(new NodePresenceReport(_options.Incarnation,
                snapshot.Revision, snapshot.Players), cancellationToken).ConfigureAwait(false);
            _lastReportAt = now;
            Interlocked.Exchange(ref _lastReportedRevision, snapshot.Revision);
            Volatile.Write(ref _failures, 0);
            Volatile.Write(ref _lastFailure, null);
            NodeDiagnostics.Directory(_logger, "presence", "success");
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            int failures = Math.Min(16, ConsecutiveFailures + 1);
            Volatile.Write(ref _failures, failures);
            string category = FailureCategory(error);
            Volatile.Write(ref _lastFailure, category);
            if (failures == 1 || failures == 16)
                NodeDiagnostics.Directory(_logger, "presence", category);
            return false;
        }
        finally
        {
            _publish.Release();
        }
    }

    public TimeSpan NextDelay()
    {
        if (ConsecutiveFailures == 0)
        {
            TimeSpan delay = PollInterval;
            if (_lastReportAt is { } last)
            {
                TimeSpan untilKeepalive = last + _options.KeepaliveInterval - _clock.GetUtcNow();
                if (untilKeepalive <= TimeSpan.Zero) return TimeSpan.Zero;
                if (untilKeepalive < delay) delay = untilKeepalive;
            }
            return delay;
        }
        double seconds = Math.Min(10, Math.Pow(2, Math.Min(ConsecutiveFailures - 1, 4)));
        return TimeSpan.FromSeconds(Math.Min(10,
            seconds * RandomNumberGenerator.GetInt32(800, 1201) / 1000.0));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await PublishOnceAsync(stoppingToken).ConfigureAwait(false);
                await Task.Delay(NextDelay(), _clock, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await _publish.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { _stopping = true; }
        finally { _publish.Release(); }
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    public override void Dispose()
    {
        base.Dispose();
        _publish.Dispose();
        if (_ownsHttp) _http.Dispose();
    }

    private async Task SendAsync(NodePresenceReport report,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_options.RequestTimeout);
        Uri baseUri = new(_options.Backend.AbsoluteUri.TrimEnd('/') + "/");
        using var request = new HttpRequestMessage(HttpMethod.Put,
            new Uri(baseUri, "v1/node/presence"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
            _options.Credential);
        request.Headers.Add("X-Server-Id", _options.NodeId.ToString("D"));
        request.Content = JsonContent.Create(report);
        using HttpResponseMessage response = await _http.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("Presence update was rejected.", null,
                response.StatusCode);
    }

    private void ValidateSnapshot(NodePresenceSnapshot snapshot)
    {
        if (snapshot.Revision < 0 || snapshot.TotalSessions < 0
            || snapshot.Players.IsDefault
            || snapshot.Players.Length > snapshot.TotalSessions
            || snapshot.Players.Length > PresenceContract.MaximumNodeEntries)
            throw new ArgumentException("Invalid Node presence projection.");
        foreach (NodePresenceEntry player in snapshot.Players)
            player.Validate();
    }

    private static string FailureCategory(Exception error)
        => error switch
        {
            OperationCanceledException => "timeout",
            HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } => "rate_limited",
            HttpRequestException { StatusCode: >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError } => "rejected",
            HttpRequestException => "transport",
            IOException => "transport",
            _ => "failure"
        };
}
