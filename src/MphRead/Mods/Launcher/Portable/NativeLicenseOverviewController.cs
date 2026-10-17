using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Launcher;

/// <summary>Never contains account tokens, emails, UUIDs or credentials.</summary>
public readonly record struct NativeLicenseOverview(
    bool Verified, string DisplayName, string FavoriteHunter, string Status,
    string PublicVisibility, int RatingPoints, string RatingTier,
    long MatchesPlayed, long Wins, long Losses, long Kills, long Deaths,
    long Assists, int RecentMatches);

public interface INativeLicenseOverviewReader
{
    Task<NativeLicenseOverview> LoadAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Read-only native Hunter License overview. An authenticated result is
/// delivered on the UI owner thread, fenced by request epoch and page lifetime.
/// </summary>
public sealed class NativeLicenseOverviewController : IDisposable
{
    private readonly record struct Completion(long Epoch, NativeLicenseOverview? Data,
        string? Error);
    private readonly INativeLicenseOverviewReader _reader;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly ConcurrentQueue<Completion> _complete = new();
    private CancellationTokenSource? _work;
    private long _epoch, _revision = 1;
    private bool _busy, _disposed;
    private string _status = "SYNC YOUR HUNTER LICENSE TO VIEW VERIFIED CAREER DATA";
    private NativeLicenseOverview _current;

    public NativeLicenseOverviewController(INativeLicenseOverviewReader reader)
        => _reader = reader ?? throw new ArgumentNullException(nameof(reader));

    public readonly record struct Snapshot(long Revision, bool Busy,
        NativeLicenseOverview Overview, string Status);

    public Snapshot Current()
    {
        Check();
        return new(_revision, _busy, _current, _status);
    }

    public void Refresh()
    {
        Check();
        Cancel();
        long epoch = _epoch;
        var cts = new CancellationTokenSource();
        _work = cts;
        _busy = true;
        _status = "CONTACTING THE LICENSE SERVICE";
        _revision++;
        _ = Run(epoch, cts);
    }

    private async Task Run(long epoch, CancellationTokenSource cts)
    {
        try
        {
            NativeLicenseOverview overview = await _reader.LoadAsync(cts.Token)
                .ConfigureAwait(false);
            _complete.Enqueue(new(epoch, overview, null));
        }
        catch (OperationCanceledException)
        {
            _complete.Enqueue(new(epoch, null, "LICENSE SYNC CANCELLED"));
        }
        catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException)
        {
            _complete.Enqueue(new(epoch, null, "LICENSE SYNC UNAVAILABLE: " + e.Message));
        }
        finally { cts.Dispose(); }
    }

    public Snapshot Tick()
    {
        Check();
        while (_complete.TryDequeue(out Completion next))
        {
            if (next.Epoch != _epoch) continue;
            _work = null;
            _busy = false;
            if (next.Data is { } record)
            {
                _current = record;
                _status = record.Verified
                    ? "VERIFIED CAREER RECORD // SERVER ACCEPTED MATCHES"
                    : String.IsNullOrWhiteSpace(record.Status)
                        ? "OFFLINE // CAREER DATA NOT VERIFIED" : record.Status;
            }
            else
            {
                // A failed refresh must not manufacture zero career totals.
                // Keep the last verified record and mark it stale.
                _status = next.Error ?? "LICENSE SERVICE UNAVAILABLE";
            }
            _revision++;
        }
        return Current();
    }

    public void Cancel()
    {
        Check();
        _epoch++;
        try { _work?.Cancel(); }
        catch (ObjectDisposedException) { }
        _work = null;
        _busy = false;
    }

    private void Check()
    {
        if (Environment.CurrentManagedThreadId != _owner)
            throw new InvalidOperationException("Hunter License UI is owned by the render thread.");
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        Check();
        Cancel();
        _disposed = true;
        while (_complete.TryDequeue(out _)) { }
    }
}
