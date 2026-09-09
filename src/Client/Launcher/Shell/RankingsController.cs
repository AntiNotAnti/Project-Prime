using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead;
using MphRead.Identity;
using MphRead.Mods.Accounts;

namespace MphRead.Mods.Launcher.Gui;

public static class PrimeLeaderboardMetrics
{
    public static IReadOnlyList<string> All { get; } = new[]
    {
        "rp", "kills", "wins", "kd", "winPercentage", "headshots",
        "octolithScores", "nodesCaptured", "killsAsPrime"
    };

    public static string Label(string metric) => metric switch
    {
        "rp" => "Ranking Points",
        "kills" => "Kills",
        "wins" => "Wins",
        "kd" => "K/D",
        "winPercentage" => "Win Percentage",
        "headshots" => "Headshots",
        "octolithScores" => "Octolith Scores",
        "nodesCaptured" => "Nodes Captured",
        "killsAsPrime" => "Kills as Prime",
        _ => metric
    };
}

public sealed record PrimeLeaderboardRow(LeaderboardEntry Entry, bool IsCurrentPlayer);

public sealed record RankingsState(string Metric, Hunter? HunterFilter,
    ImmutableArray<PrimeLeaderboardRow> Rows, string? NextCursor, bool Loading,
    string? Error)
{
    public static RankingsState Initial => new("rp", null,
        ImmutableArray<PrimeLeaderboardRow>.Empty, null, false, null);

    public bool CanLoadMore => NextCursor is not null;
}

public interface IPrimeRankingQueries
{
    string BackendScope { get; }
    PlayerId? PlayerId { get; }
    Task<LeaderboardPage> GetLeaderboardAsync(string metric, string? cursor,
        Hunter? hunter, CancellationToken cancellationToken);
}

internal sealed class AccountRankingQueries : IPrimeRankingQueries
{
    private readonly AccountSession _account;
    public AccountRankingQueries(AccountSession account) => _account = account;
    public string BackendScope => _account.BackendScope;
    public PlayerId? PlayerId => _account.Identity?.PlayerId;
    public Task<LeaderboardPage> GetLeaderboardAsync(string metric, string? cursor,
        Hunter? hunter, CancellationToken cancellationToken)
        => _account.GetLeaderboardAsync(metric, cursor, cancellationToken, hunter);
}

/// <summary>Official leaderboard state with contract-aware filtering and pages.</summary>
public sealed class RankingsController : IDisposable
{
    private readonly PrimeShellState _shell;
    private readonly IPrimeRankingQueries? _providedQueries;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _request;
    private RankingsState _state = RankingsState.Initial;
    private long _generation;
    private int _disposed;

    public RankingsController(PrimeShellState shell, IPrimeRankingQueries? queries = null)
    {
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _providedQueries = queries;
        _shell.IdentityChanged += ShellIdentityChanged;
    }

    public RankingsState State => _state;
    public event EventHandler? Changed;

    public void SetMetric(string metric)
    {
        if (!PrimeLeaderboardMetrics.All.Contains(metric, StringComparer.Ordinal))
            throw new ArgumentException("Choose a supported leaderboard metric.", nameof(metric));
        if (_state.Metric == metric) return;
        if (metric == "rp" && _state.HunterFilter.HasValue)
            throw new ArgumentException("Ranking Points cannot be filtered by Hunter.", nameof(metric));
        CancelCurrentRequest();
        Publish(_state with { Metric = metric, NextCursor = null,
            Rows = ImmutableArray<PrimeLeaderboardRow>.Empty, Error = null });
    }

    public void SetHunterFilter(Hunter? hunter)
    {
        if (hunter is < Hunter.Samus or > Hunter.Weavel)
            throw new ArgumentOutOfRangeException(nameof(hunter));
        if (_state.Metric == "rp" && hunter.HasValue)
            throw new ArgumentException("Ranking Points cannot be filtered by Hunter.", nameof(hunter));
        CancelCurrentRequest();
        Publish(_state with { HunterFilter = hunter, NextCursor = null,
            Rows = ImmutableArray<PrimeLeaderboardRow>.Empty, Error = null });
    }

    public async Task<LeaderboardPage?> LoadAsync(bool next = false,
        CancellationToken cancellationToken = default)
    {
        if (next && _state.NextCursor is null) return null;
        IPrimeRankingQueries queries = ResolveQueries();
        if (queries.PlayerId is not { } player || player.IsEmpty)
            throw new InvalidOperationException("Sign in before viewing Rankings.");
        string? cursor = next ? _state.NextCursor : null;
        long generation = Interlocked.Increment(ref _generation);
        CancellationToken token = BeginRequest(cancellationToken);
        Publish(_state with { Loading = true, Error = null });
        try
        {
            LeaderboardPage page = await queries.GetLeaderboardAsync(_state.Metric, cursor,
                _state.HunterFilter, token).ConfigureAwait(false);
            if (token.IsCancellationRequested || generation != Interlocked.Read(ref _generation)
                || Volatile.Read(ref _disposed) != 0) return null;
            ImmutableArray<PrimeLeaderboardRow> rows = page.Entries.Select(entry =>
                new PrimeLeaderboardRow(entry, entry.PlayerId == player)).ToImmutableArray();
            Publish(_state with { Rows = next ? _state.Rows.AddRange(rows) : rows,
                NextCursor = page.NextCursor, Loading = false });
            return page;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return null; }
        catch (Exception error)
        {
            Publish(_state with { Loading = false, Error = error.Message });
            return null;
        }
    }

    public void Invalidate()
    {
        CancelCurrentRequest();
        Publish(_state with { Rows = ImmutableArray<PrimeLeaderboardRow>.Empty,
            NextCursor = null, Error = null });
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

    private IPrimeRankingQueries ResolveQueries()
    {
        if (_providedQueries != null) return _providedQueries;
        if (AccountSessions.Current is { IsSignedIn: true } account)
            return new AccountRankingQueries(account);
        throw new InvalidOperationException("Sign in before viewing Rankings.");
    }

    private CancellationToken BeginRequest(CancellationToken external)
    {
        _request?.Cancel();
        _request?.Dispose();
        _request = CancellationTokenSource.CreateLinkedTokenSource(external, _lifetime.Token);
        return _request.Token;
    }

    private void CancelCurrentRequest()
    {
        Interlocked.Increment(ref _generation);
        _request?.Cancel();
    }

    private void Publish(RankingsState state)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        _state = state;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
