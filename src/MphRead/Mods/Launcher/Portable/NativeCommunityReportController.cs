using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher;

public interface INativeCommunityReportAuthority
{
    Task<object> PrepareAsync(NativeCommunityEntry selection, object? catalogWitness, CancellationToken token);
    bool IsCurrentLease(object lease);
    Task SubmitAsync(NativeCommunityEntry selection, string reason, string details, object lease, CancellationToken token);
}

/// <summary>One explicitly submitted report for an immutable selection and verified account.</summary>
public sealed class NativeCommunityReportController : IDisposable
{
    private readonly record struct Completion(long Epoch, object? Lease, bool Submitted, bool Failed);
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly INativeCommunityReportAuthority _authority;
    private readonly ConcurrentQueue<Completion> _done = new();
    private NativeCommunityEntry? _selected;
    private object? _lease;
    private CancellationTokenSource? _work;
    private bool _disposed, _busy, _submitted;
    private int _reason;
    private long _epoch, _revision;
    private string _status = "";

    public NativeCommunityReportController(INativeCommunityReportAuthority authority)
        => _authority = authority ?? throw new ArgumentNullException(nameof(authority));
    public sealed record View(long Revision, NativeCommunityEntry? Selection, int Reason,
        bool Busy, bool CanSubmit, bool Submitted, string Status);

    public bool Open(NativeCommunityEntry selection, object? catalogWitness)
    {
        Owner();
        if (!NativeCommunityInstallController.IsVerifiedSelection(selection) || selection.Installed
            || selection.Version is null || selection.Version.Length > 64) return false;
        Cancel(); _selected = selection; _reason = 0; _submitted = false; _busy = true;
        _status = "VERIFYING REPORTING ACCOUNT"; _revision++;
        long epoch = _epoch; var cancel = _work = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try
            {
                object lease = await _authority.PrepareAsync(selection, catalogWitness, cancel.Token).ConfigureAwait(false);
                if (!cancel.IsCancellationRequested) _done.Enqueue(new(epoch, lease, false, false));
            }
            catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
            { if (!cancel.IsCancellationRequested) _done.Enqueue(new(epoch, null, false, true)); }
            finally { cancel.Dispose(); }
        });
        return true;
    }

    public bool Reason(int index)
    {
        Owner();
        if (_busy || _submitted || _selected == null || index < 0 || index >= MapCreatorCatalog.ReportReasons.Length) return false;
        _reason = index; _revision++; return true;
    }

    public bool Submit(string details)
    {
        Owner(); Tick();
        if (_busy || _submitted || _selected is not { } selected || _lease is not { } lease
            || details is null || details.Length > 4000 || !_authority.IsCurrentLease(lease)) return false;
        _busy = true; _status = "SUBMITTING REPORT // WAITING FOR SERVICE"; _revision++;
        long epoch = _epoch; string reason = MapCreatorCatalog.ReportReasons[_reason];
        var cancel = _work = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try
            {
                await _authority.SubmitAsync(selected, reason, details, lease, cancel.Token).ConfigureAwait(false);
                if (!cancel.IsCancellationRequested) _done.Enqueue(new(epoch, lease, true, false));
            }
            catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
            { if (!cancel.IsCancellationRequested) _done.Enqueue(new(epoch, lease, true, true)); }
            finally { cancel.Dispose(); }
        });
        return true;
    }

    public View Tick()
    {
        Owner();
        if (_lease != null && !_authority.IsCurrentLease(_lease)) RetireAccount();
        while (_done.TryDequeue(out var result))
        {
            if (result.Epoch != _epoch) continue;
            _busy = false; _work = null;
            if (result.Lease != null && !_authority.IsCurrentLease(result.Lease)) { RetireAccount(); continue; }
            if (result.Failed)
                _status = result.Submitted ? "REPORT UNAVAILABLE // VERIFY ACCOUNT AND RETRY"
                    : "ACCOUNT UNAVAILABLE // CLOSE AND REOPEN TO VERIFY";
            else
            {
                _lease = result.Lease; _submitted = result.Submitted;
                _status = _submitted ? "REPORT SUBMITTED FOR MODERATOR REVIEW"
                    : "Choose a reason and optionally describe the issue. Submit sends this report for moderator review.";
            }
            _revision++;
        }
        return new(_revision, _selected, _reason, _busy,
            !_busy && !_submitted && _selected != null && _lease != null, _submitted, _status);
    }

    private void RetireAccount()
    {
        Cancel(); _status = "ACCOUNT CHANGED // CLOSE AND REOPEN TO VERIFY"; _revision++;
    }
    public void Cancel()
    {
        Owner(); _epoch++;
        try { _work?.Cancel(); } catch (ObjectDisposedException) { }
        _work = null; _lease = null; _selected = null; _busy = false; _submitted = false; _revision++;
    }
    private void Owner()
    {
        if (Environment.CurrentManagedThreadId != _owner)
            throw new InvalidOperationException("Community reports require the launcher owner thread.");
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
    public void Dispose()
    {
        if (_disposed) return;
        Cancel(); _disposed = true;
        while (_done.TryDequeue(out _)) { }
    }
}
