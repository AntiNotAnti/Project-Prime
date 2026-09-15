using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Identity;
using MphRead.Mods.Accounts;
using MphRead.Mods.Launcher;

namespace MphRead.Mods.Launcher.Gui;

public enum HunterLicenseSection
{
    Overview,
    Hunters,
    Career,
    Matches
}

public sealed record HunterDossier(Hunter Hunter, string Name, string AffinityWeapon,
    string? ModelAsset, bool IsFavorite, bool IsMostPlayed, bool IsBest,
    string PreviewStatus);

public sealed record HunterLicensePageState(HunterLicense? License, CareerSummary? Career,
    ImmutableArray<MatchHistoryEntry> Matches, long? NextHistoryCursor,
    HunterLicenseSection Section, bool LoadingLicense, bool LoadingCareer,
    bool LoadingMatches, string? Error)
{
    public static HunterLicensePageState Initial => new(null, null,
        ImmutableArray<MatchHistoryEntry>.Empty, null, HunterLicenseSection.Overview,
        false, false, false, null);

    public bool HasMatches => !Matches.IsDefaultOrEmpty;
}

/// <summary>Minimal query boundary for deterministic license/controller tests.</summary>
public interface IPrimeCareerQueries
{
    string BackendScope { get; }
    PlayerId? PlayerId { get; }
    Task<HunterLicense> GetLicenseAsync(PlayerId player, CancellationToken cancellationToken);
    Task<CareerSummary> GetCareerAsync(PlayerId player, CancellationToken cancellationToken);
    Task<MatchHistoryPage> GetHistoryAsync(PlayerId player, long? before,
        CancellationToken cancellationToken);
    Task UpdateProfileAsync(string displayName, int favoriteHunter,
        CancellationToken cancellationToken);
}

internal sealed class AccountCareerQueries : IPrimeCareerQueries
{
    private readonly AccountSession _account;
    public AccountCareerQueries(AccountSession account) => _account = account;
    public string BackendScope => _account.BackendScope;
    public PlayerId? PlayerId => _account.Identity?.PlayerId;
    public Task<HunterLicense> GetLicenseAsync(PlayerId player, CancellationToken cancellationToken)
        => _account.GetLicenseAsync(player, cancellationToken);
    public Task<CareerSummary> GetCareerAsync(PlayerId player, CancellationToken cancellationToken)
        => _account.GetCareerAsync(player, cancellationToken);
    public Task<MatchHistoryPage> GetHistoryAsync(PlayerId player, long? before,
        CancellationToken cancellationToken)
        => _account.GetHistoryAsync(player, before, cancellationToken);
    public Task UpdateProfileAsync(string displayName, int favoriteHunter,
        CancellationToken cancellationToken)
        => _account.UpdateProfileAsync(displayName, favoriteHunter, cancellationToken);
}

/// <summary>
/// Account-scoped license/career/history state. The cache is bounded and keyed
/// by Backend origin plus PlayerId, so another account or Backend cannot reuse
/// the previous player's projection.
/// </summary>
public sealed class HunterLicenseController : IDisposable
{
    private readonly PrimeShellState _shell;
    private readonly IPrimeCareerQueries? _providedQueries;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _cacheLock = new();
    private readonly Dictionary<LicenseCacheKey, CachedLicense> _cache = new();
    private readonly LinkedList<LicenseCacheKey> _cacheOrder = new();
    private HunterLicensePageState _state = HunterLicensePageState.Initial;
    private CancellationTokenSource? _request;
    private long _requestGeneration;
    private bool _historyLoaded;
    private int _disposed;

    public HunterLicenseController(PrimeShellState shell, IPrimeCareerQueries? queries = null)
    {
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _providedQueries = queries;
        _shell.IdentityChanged += ShellIdentityChanged;
    }

    public HunterLicensePageState State => _state;
    public IReadOnlyList<HunterDossier> Hunters => BuildHunters(_state.License, _state.Career);
    public bool CanLoadMoreMatches => _state.NextHistoryCursor.HasValue;
    public event EventHandler? Changed;

    public async Task LoadOverviewAsync(CancellationToken cancellationToken = default)
    {
        IPrimeCareerQueries queries = ResolveQueries();
        PlayerId player = RequirePlayer(queries);
        CancellationToken token = BeginRequest(cancellationToken);
        long generation = Interlocked.Read(ref _requestGeneration);
        Publish(_state with { Section = HunterLicenseSection.Overview,
            LoadingLicense = true, LoadingCareer = true, Error = null });
        try
        {
            LicenseCacheKey key = new(queries.BackendScope, player);
            CachedLicense? cached = GetCached(key);
            HunterLicense license = cached?.License
                ?? await queries.GetLicenseAsync(player, token).ConfigureAwait(false);
            if (!IsCurrent(token, generation)) return;
            if (cached is not null && cached.Career is not null)
            {
                Publish(_state with { License = license, Career = cached.Career,
                    LoadingLicense = false, LoadingCareer = false });
                return;
            }
            Publish(_state with { License = license, LoadingLicense = false });
            CareerSummary career = await queries.GetCareerAsync(player, token).ConfigureAwait(false);
            if (!IsCurrent(token, generation)) return;
            PutCached(key, license, career);
            Publish(_state with { License = license, Career = career, LoadingCareer = false });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { Fail(error); }
    }

    public async Task LoadHuntersAsync(CancellationToken cancellationToken = default)
    {
        if (_state.Career is null)
            await LoadOverviewAsync(cancellationToken).ConfigureAwait(false);
        Publish(_state with { Section = HunterLicenseSection.Hunters });
    }

    public async Task LoadCareerAsync(CancellationToken cancellationToken = default)
    {
        await LoadOverviewAsync(cancellationToken).ConfigureAwait(false);
        if (!cancellationToken.IsCancellationRequested)
            Publish(_state with { Section = HunterLicenseSection.Career });
    }

    public async Task<MatchHistoryPage?> LoadMatchesAsync(bool older = false,
        CancellationToken cancellationToken = default)
    {
        IPrimeCareerQueries queries = ResolveQueries();
        PlayerId player = RequirePlayer(queries);
        long? cursor = older ? _state.NextHistoryCursor : null;
        if (older && cursor is null) return null;
        CancellationToken token = BeginRequest(cancellationToken);
        long generation = Interlocked.Read(ref _requestGeneration);
        Publish(_state with { Section = HunterLicenseSection.Matches,
            LoadingMatches = true, Error = null });
        try
        {
            MatchHistoryPage page = await queries.GetHistoryAsync(player, cursor, token)
                .ConfigureAwait(false);
            if (!IsCurrent(token, generation)) return null;
            ImmutableArray<MatchHistoryEntry> entries = older
                ? _state.Matches.AddRange(page.Entries)
                : page.Entries;
            Publish(_state with { Matches = entries, NextHistoryCursor = page.NextCursor,
                LoadingMatches = false });
            return page;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return null; }
        catch (Exception error) { Fail(error); return null; }
    }

    public async Task<MatchHistoryPage?> LoadRecentMatchesAsync(
        CancellationToken cancellationToken = default)
    {
        if (_historyLoaded) return null;
        IPrimeCareerQueries queries = ResolveQueries();
        PlayerId player = RequirePlayer(queries);
        CancellationToken token = BeginRequest(cancellationToken);
        long generation = Interlocked.Read(ref _requestGeneration);
        Publish(_state with { LoadingMatches = true, Error = null });
        try
        {
            MatchHistoryPage page = await queries.GetHistoryAsync(player, null, token)
                .ConfigureAwait(false);
            if (!IsCurrent(token, generation)) return null;
            _historyLoaded = true;
            Publish(_state with { Matches = page.Entries, NextHistoryCursor = page.NextCursor,
                LoadingMatches = false });
            return page;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return null; }
        catch (Exception error) { Fail(error); return null; }
    }

    public async Task<bool> SetFavoriteHunterAsync(Hunter hunter,
        CancellationToken cancellationToken = default)
    {
        if (!PlayableHunterCatalog.IsPlayable(hunter))
            throw new ArgumentOutOfRangeException(nameof(hunter));
        IPrimeCareerQueries queries = ResolveQueries();
        RequirePlayer(queries);
        try
        {
            await queries.UpdateProfileAsync(_state.License?.DisplayName
                ?? _shell.DisplayName, (int)hunter, cancellationToken).ConfigureAwait(false);
            Invalidate(queries.BackendScope, queries.PlayerId);
            await LoadOverviewAsync(cancellationToken).ConfigureAwait(false);
            _shell.NotifyTransient("hunter-favorite", PrimeNotificationKind.Success,
                "Favorite Hunter updated.");
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) { Fail(error); return false; }
    }

    public async Task<bool> UpdateDisplayNameAsync(string displayName,
        CancellationToken cancellationToken = default)
    {
        string value = displayName?.Trim() ?? "";
        if (value is not { Length: >= 1 and <= 16 })
            throw new ArgumentException("A display name must contain 1 to 16 characters.", nameof(displayName));
        IPrimeCareerQueries queries = ResolveQueries();
        RequirePlayer(queries);
        try
        {
            int favorite = _state.License?.FavoriteHunter ?? 0;
            await queries.UpdateProfileAsync(value, favorite, cancellationToken).ConfigureAwait(false);
            Invalidate(queries.BackendScope, queries.PlayerId);
            await LoadOverviewAsync(cancellationToken).ConfigureAwait(false);
            _shell.SetDisplayName(_state.License?.DisplayName ?? value);
            _shell.NotifyTransient("hunter-display-name", PrimeNotificationKind.Success,
                "Display name updated.");
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) { Fail(error); return false; }
    }

    public void Invalidate(string? backendScope = null, PlayerId? player = null)
    {
        Interlocked.Increment(ref _requestGeneration);
        _request?.Cancel();
        _historyLoaded = false;
        lock (_cacheLock)
        {
            LicenseCacheKey[] keys = _cache.Keys.Where(key =>
                (backendScope is null || key.BackendScope == backendScope)
                && (!player.HasValue || key.Player == player.Value)).ToArray();
            foreach (LicenseCacheKey key in keys)
            {
                if (_cache.Remove(key))
                {
                    LinkedListNode<LicenseCacheKey>? node = _cacheOrder.Find(key);
                    if (node != null) _cacheOrder.Remove(node);
                }
            }
        }
        Publish(_state with { License = null, Career = null,
            Matches = ImmutableArray<MatchHistoryEntry>.Empty, NextHistoryCursor = null });
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _shell.IdentityChanged -= ShellIdentityChanged;
        _request?.Cancel();
        _request?.Dispose();
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private void ShellIdentityChanged(object? sender, EventArgs args) => Invalidate();

    private IPrimeCareerQueries ResolveQueries()
    {
        if (_providedQueries != null) return _providedQueries;
        if (AccountSessions.Current is { IsSignedIn: true } account)
            return new AccountCareerQueries(account);
        throw new InvalidOperationException("Sign in before viewing the Hunter License.");
    }

    private PlayerId RequirePlayer(IPrimeCareerQueries queries)
        => queries.PlayerId is { } player && !player.IsEmpty
            ? player : throw new InvalidOperationException("Sign in before viewing the Hunter License.");

    private CancellationToken BeginRequest(CancellationToken external)
    {
        Interlocked.Increment(ref _requestGeneration);
        _request?.Cancel();
        _request?.Dispose();
        _request = CancellationTokenSource.CreateLinkedTokenSource(external, _lifetime.Token);
        return _request.Token;
    }

    private bool IsCurrent(CancellationToken token, long generation)
        => !token.IsCancellationRequested
            && generation == Interlocked.Read(ref _requestGeneration)
            && Volatile.Read(ref _disposed) == 0;

    private CachedLicense? GetCached(LicenseCacheKey key)
    {
        lock (_cacheLock)
        {
            if (!_cache.TryGetValue(key, out CachedLicense? value)) return null;
            LinkedListNode<LicenseCacheKey>? node = _cacheOrder.Find(key);
            if (node != null) { _cacheOrder.Remove(node); _cacheOrder.AddFirst(node); }
            return value;
        }
    }

    private void PutCached(LicenseCacheKey key, HunterLicense license, CareerSummary career)
    {
        lock (_cacheLock)
        {
            _cache[key] = new CachedLicense(license, career);
            LinkedListNode<LicenseCacheKey>? existing = _cacheOrder.Find(key);
            if (existing != null) _cacheOrder.Remove(existing);
            _cacheOrder.AddFirst(key);
            while (_cacheOrder.Count > 16)
            {
                LicenseCacheKey last = _cacheOrder.Last!.Value;
                _cacheOrder.RemoveLast();
                _cache.Remove(last);
            }
        }
    }

    private void Publish(HunterLicensePageState state)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        _state = state;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Fail(Exception error)
    {
        string message = error.Message.Length == 0 ? "Hunter License request failed." : error.Message;
        Publish(_state with { LoadingLicense = false, LoadingCareer = false,
            LoadingMatches = false, Error = message });
    }

    private static IReadOnlyList<HunterDossier> BuildHunters(HunterLicense? license,
        CareerSummary? career)
    {
        int? favorite = license?.FavoriteHunter;
        int? mostPlayed = ParseHunterChoice(career?.MostPlayedHunter);
        int? best = ParseHunterChoice(career?.BestHunter);
        var result = new List<HunterDossier>(PlayableHunterCatalog.Count);
        foreach (Hunter hunter in PlayableHunterCatalog.All)
        {
            BeamType affinity = Weapons.GetAffinityBeam(hunter);
            string? model = Metadata.HunterModels.TryGetValue(hunter, out var models)
                && models.Count > 2 ? models[0] : null;
            result.Add(new HunterDossier(hunter, hunter.ToString(),
                Metadata.WeaponNames[(int)affinity], model, favorite == (int)hunter,
                mostPlayed == (int)hunter, best == (int)hunter,
                model == null ? "No local preview available." : "Local game model available."));
        }
        return result;
    }

    private static int? ParseHunterChoice(CareerChoice? choice)
    {
        if (choice == null) return null;
        if (Int32.TryParse(choice.Key, NumberStyles.None, CultureInfo.InvariantCulture,
            out int numeric) && PlayableHunterCatalog.TryFromIndex(numeric, out _))
            return numeric;
        return Enum.TryParse<Hunter>(choice.Key, ignoreCase: true, out Hunter hunter)
            && PlayableHunterCatalog.IsPlayable(hunter) ? (int)hunter : null;
    }

    private readonly record struct LicenseCacheKey(string BackendScope, PlayerId Player);
    private sealed record CachedLicense(HunterLicense License, CareerSummary Career);
}
