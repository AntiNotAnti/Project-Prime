using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using FruityPrime.Server.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using MphRead.Backend.Data;
using MphRead.Backend.Tickets;
using MphRead.Identity;
using MphRead.Reporting;

namespace FruityPrime.WorkerSoak;

public sealed record SoakBackendPlayer(Guid PlayerId, string DisplayName);
public sealed record SoakIdentityLease(IReadOnlyList<Guid> PlayerIds, IReadOnlyList<Guid> ObserverIds,
    IReadOnlyList<(Guid PlayerId, Guid ObserverId)> Pairs);
public sealed record SoakBackendReport(Guid MatchId, string PayloadHash, int Bytes);
public sealed record SoakBackendCounts(int PersistedReports, long Attempts, long OutageResponses);
public sealed record SoakBackendStats(int PersistedReports, long Attempts, long OutageResponses,
    int PayloadHashMismatches, IReadOnlyList<SoakBackendReport> Reports, string DatabasePath);

/// <summary>The real Backend pipeline on loopback TLS, with a dedicated persistent SQLite
/// database under the soak output directory. Fault injection returns HTTP503 before ingestion;
/// recovery uses the unchanged production authentication/validation/ledger path.</summary>
public sealed class SoakBackend : IAsyncDisposable
{
    private readonly BackendFactory _factory;
    private readonly X509Certificate2 _certificate;
    private readonly HttpClient _client;
    private readonly HttpMatchReportTransport _transport;
    private readonly string _databasePath;
    private readonly ConcurrentDictionary<Guid, byte> _accepted = new();
    private int _outage;
    private readonly ConcurrentQueue<(Guid PlayerId, Guid ObserverId)> _players = new();
    private sealed record LeaseEntry(Guid ObserverId, SoakIdentityLease Lease);
    private readonly ConcurrentDictionary<Guid, LeaseEntry> _leasedPlayers = new();
    private readonly SemaphoreSlim _availablePlayers = new(0);
    private readonly NodeId _nodeId;
    private long _attempts, _outageResponses, _outageAttempts, _outageRecoveries;
    public IMatchReportTransport Transport => _transport;
    public Uri Endpoint { get; }
    public long OutageAttempts => Interlocked.Read(ref _outageAttempts);
    public long OutageRecoveries => Interlocked.Read(ref _outageRecoveries);
    public void SetOutage(bool unavailable)
    {
        int next = unavailable ? 1 : 0;
        int prior = Interlocked.Exchange(ref _outage, next);
        if (prior == next) return;
        if (next != 0) Interlocked.Increment(ref _outageAttempts);
        else Interlocked.Increment(ref _outageRecoveries);
    }

    private SoakBackend(NodeId nodeId, string secret, string directory)
    {
        if (nodeId.Value == Guid.Empty || string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
            throw new ArgumentException("A Node identity and strong ephemeral report secret are required.");
        if (!Path.IsPathFullyQualified(directory)) throw new ArgumentException("Soak output directory must be absolute.");
        _nodeId = nodeId;
        Directory.CreateDirectory(directory);
        _databasePath = Path.Combine(directory, "backend.sqlite");
        _certificate = Certificate();
        _factory = new BackendFactory(nodeId, secret, _databasePath, Pipeline);
        _factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(_certificate)));
        _factory.StartServer();
        var addresses = _factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses
            ?? throw new InvalidOperationException("Soak Backend did not expose its loopback listener.");
        Endpoint = new Uri(new Uri(addresses.Single()), "/v1/server/matches");
        if (Endpoint.Host != "127.0.0.1" || Endpoint.Scheme != "https") throw new InvalidOperationException("Soak Backend must remain loopback TLS.");
        byte[] pin = _certificate.GetCertHash(HashAlgorithmName.SHA256);
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback = (request, certificate, _, _) => request.RequestUri?.Host == "127.0.0.1"
                && certificate != null && CryptographicOperations.FixedTimeEquals(pin, certificate.GetCertHash(HashAlgorithmName.SHA256))
        };
        _client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        _transport = new HttpMatchReportTransport(nodeId.Value, Endpoint, secret, _client);
    }

    public static async Task<SoakBackend> CreateAsync(NodeId nodeId, string secret, string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        var backend = new SoakBackend(nodeId, secret, Path.Combine(outputDirectory, "backend"));
        try
        {
            await using var scope = backend._factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BackendDbContext>();
            await db.Database.EnsureCreatedAsync(cancellationToken);
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
            foreach (Guid id in await db.Matches.Select(m => m.MatchId).Take(4097).ToArrayAsync(cancellationToken))
                backend._accepted.TryAdd(id, 0);
            if (backend._accepted.Count > 4096) throw new InvalidOperationException("Soak report bound exceeded.");
            var identities = Enumerable.Range(0, 1024).Select(index =>
            {
                byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(nodeId.Value.ToString("N") + ":soak-player:" + index));
                return new SoakBackendPlayer(new Guid(hash.AsSpan(0, 16)), "Soak" + index.ToString("D4"));
            }).ToArray();
            await backend.RegisterPlayersAsync(identities, cancellationToken);
            for (int index = 0; index < identities.Length; index += 2)
            {
                backend._players.Enqueue((identities[index].PlayerId, identities[index + 1].PlayerId));
                backend._availablePlayers.Release();
            }
            return backend;
        }
        catch { await backend.DisposeAsync(); throw; }
    }

    /// <summary>Seeds only the isolated soak database, allowing frozen registered identities
    /// to pass the real report ledger's account-existence checks.</summary>
    public async Task RegisterPlayersAsync(IEnumerable<SoakBackendPlayer> players, CancellationToken cancellationToken = default)
    {
        var requested = players.ToArray();
        if (requested.Length > 1024 || requested.Any(p => p.PlayerId == Guid.Empty || string.IsNullOrWhiteSpace(p.DisplayName)
            || p.DisplayName.Length > 16) || requested.Select(p => p.PlayerId).Distinct().Count() != requested.Length)
            throw new ArgumentException("Invalid bounded soak player identities.");
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BackendDbContext>();
        if (await db.Users.CountAsync(cancellationToken) + requested.Length > 4096) throw new InvalidOperationException("Soak account bound exceeded.");
        foreach (var player in requested)
        {
            if (await db.Users.AnyAsync(p => p.Id == player.PlayerId, cancellationToken)) continue;
            string login = "soak-" + player.PlayerId.ToString("N");
            db.Users.Add(new HunterAccount { Id = player.PlayerId, UserName = login, NormalizedUserName = login.ToUpperInvariant(),
                Email = login + "@example.test", NormalizedEmail = login.ToUpperInvariant() + "@EXAMPLE.TEST", EmailConfirmed = true,
                SecurityStamp = Guid.NewGuid().ToString("N"), ConcurrencyStamp = Guid.NewGuid().ToString("N") });
            db.Profiles.Add(new PlayerProfile { PlayerId = player.PlayerId, DisplayName = player.DisplayName });
            db.Licenses.Add(new HunterLicense { PlayerId = player.PlayerId, CreatedAt = DateTimeOffset.UtcNow });
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<SoakIdentityLease> RentPlayersAsync(int players, int observers,
        CancellationToken cancellationToken = default)
    {
        if (players is < 1 or > 8 || observers is < 0 or > 16)
            throw new ArgumentOutOfRangeException(nameof(players));
        int count = Math.Max(players, observers);
        var pairs = new List<(Guid PlayerId, Guid ObserverId)>(count);
        var playerIds = new List<Guid>(players);
        var observerIds = new List<Guid>(observers);
        var lease = new SoakIdentityLease(playerIds, observerIds, pairs);
        try
        {
            for (int index = 0; index < count; index++)
            {
                await _availablePlayers.WaitAsync(cancellationToken);
                if (!_players.TryDequeue(out var pair) || !_leasedPlayers.TryAdd(pair.PlayerId, new(pair.ObserverId, lease)))
                    throw new InvalidOperationException("Soak identity pool invariant failed.");
                pairs.Add(pair);
            }
            playerIds.AddRange(pairs.Take(players).Select(pair => pair.PlayerId));
            observerIds.AddRange(pairs.Take(observers).Select(pair => pair.ObserverId));
            return lease;
        }
        catch
        {
            foreach (var pair in pairs) ReleasePlayers(pair.PlayerId, pair.ObserverId, lease);
            throw;
        }
    }
    public void ReleasePlayers(SoakIdentityLease lease)
    {
        foreach (var pair in lease.Pairs) ReleasePlayers(pair.PlayerId, pair.ObserverId, lease);
    }
    private void ReleasePlayers(Guid playerId, Guid observerId, SoakIdentityLease lease)
    {
        if (!_leasedPlayers.TryGetValue(playerId, out var current) || current.ObserverId != observerId
            || !ReferenceEquals(current.Lease, lease)
            || !((ICollection<KeyValuePair<Guid, LeaseEntry>>)_leasedPlayers).Remove(new(playerId, current)))
            throw new InvalidOperationException("Soak identity lease is not active.");
        _players.Enqueue((playerId, observerId)); _availablePlayers.Release();
    }

    /// <summary>Cheap periodic monitoring: no report payloads are loaded or hashed.
    /// Use StatsAsync at checkpoints/finalization for independent payload verification.</summary>
    public async Task<SoakBackendCounts> CountsAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BackendDbContext>();
        int reports = await db.Matches.CountAsync(cancellationToken);
        if (reports > 4096) throw new InvalidOperationException("Soak report bound exceeded.");
        return new(reports, Interlocked.Read(ref _attempts), Interlocked.Read(ref _outageResponses));
    }

    public async Task<SoakBackendStats> StatsAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BackendDbContext>();
        var reports = new List<SoakBackendReport>();
        int mismatches = 0;
        await foreach (var row in db.Matches.AsNoTracking().OrderBy(m => m.ProcessingOrder).AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            if (reports.Count == 4096) throw new InvalidOperationException("Soak report bound exceeded.");
            if (!String.Equals(Convert.ToHexString(SHA256.HashData(row.OriginalReport)), row.PayloadHash, StringComparison.OrdinalIgnoreCase)) mismatches++;
            reports.Add(new(row.MatchId, row.PayloadHash, row.OriginalReport.Length));
        }
        return new(reports.Count, Interlocked.Read(ref _attempts), Interlocked.Read(ref _outageResponses), mismatches, reports, _databasePath);
    }

    private void Pipeline(IApplicationBuilder app)
    {
        app.Use(async (context, next) =>
        {
            if (context.Request.Path != "/v1/server/matches") { await next(); return; }
            Interlocked.Increment(ref _attempts);
            if (Volatile.Read(ref _outage) != 0)
            {
                Interlocked.Increment(ref _outageResponses); context.Response.StatusCode = 503;
                context.Response.Headers.RetryAfter = "1"; return;
            }
            Guid.TryParse(context.Request.Headers["Idempotency-Key"], out Guid id);
            if (_accepted.Count >= 4096 && !_accepted.ContainsKey(id)) { context.Response.StatusCode = 507; return; }
            await next();
            if (context.Response.StatusCode is >= 200 and < 300 && id != Guid.Empty) _accepted.TryAdd(id, 0);
        });
    }

    public async ValueTask DisposeAsync()
    {
        _transport.Dispose(); _client.Dispose(); await _factory.DisposeAsync(); _certificate.Dispose(); _availablePlayers.Dispose();
    }

    private static X509Certificate2 Certificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=worker-soak-loopback", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder(); names.AddIpAddress(IPAddress.Loopback); request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(2));
    }

    private sealed class BackendFactory(NodeId nodeId, string secret, string databasePath, Action<IApplicationBuilder> pipeline)
        : WebApplicationFactory<MphRead.Backend.Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseContentRoot(SourceRoot());
            builder.UseSetting("ConnectionStrings:Backend", "Host=unused;Database=unused");
            builder.UseSetting("Accounts:RequireConfirmedEmail", "false");
            builder.ConfigureLogging(log => log.ClearProviders());
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<BackendDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<BackendDbContext>>();
                services.AddDbContext<BackendDbContext>((provider, options) =>
                {
                    options.UseSqlite(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = databasePath, DefaultTimeout = 30 }.ToString());
                    options.AddInterceptors(provider.GetServices<SaveChangesInterceptor>());
                });
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.Configure<GameServerOptions>(options =>
                {
                    options.Servers.Clear();
                    options.Servers.Add(new() { Id = nodeId.Value, Enabled = true, TrustClass = MatchTrustClass.VerifiedCasual,
                        ApiKeySha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))) });
                });
                services.AddSingleton<IStartupFilter>(new Filter(pipeline));
            });
        }
        private static string SourceRoot([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "../../src/Backend"));
    }
    private sealed class Filter(Action<IApplicationBuilder> pipeline) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app => { pipeline(app); next(app); };
    }
}
