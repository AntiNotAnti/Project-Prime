using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher;

/// <summary>Transfer and package verification are performed by the existing Community queue.</summary>
public interface INativeCommunityInstaller
{
    Task<NativeCommunityInstalled> InstallAsync(NativeCommunityEntry selected,
        CancellationToken cancellation);
    // Called only from the launcher's owner thread after a verified install.
    void Register(NativeCommunityInstalled installed);
}

public sealed record NativeCommunityInstalled(Guid MapId, string PackageHash,
    MapDefinition Definition);

public sealed record NativeCommunityInstallView(long Revision, bool Busy,
    string Status, Guid? CompletedMapId, string CompletedHash);

/// <summary>
/// Bounded native install handoff. Exact selection and immutable hash are
/// captured once; completions pass through an epoch and register on the UI
/// owner only. Closing a page cancels its wait, not the durable download job.
/// </summary>
public sealed class NativeCommunityInstallController : IDisposable
{
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly INativeCommunityInstaller _installer;
    private readonly ConcurrentQueue<(long Epoch, NativeCommunityInstalled? Result,
        string? Error)> _completed = new();
    private CancellationTokenSource? _active;
    private NativeCommunityEntry _expected;
    private long _epoch, _revision = 1;
    private bool _disposed, _busy;
    private string _status = "", _completedHash = "";
    private Guid? _completedMapId;

    public NativeCommunityInstallController(INativeCommunityInstaller installer)
        => _installer = installer ?? throw new ArgumentNullException(nameof(installer));

    public static bool IsVerifiedSelection(NativeCommunityEntry item)
        => item.MapId != Guid.Empty && item.LatestHash is { Length: 64 }
            && item.LatestHash.All(Uri.IsHexDigit);

    private void Owner()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(NativeCommunityInstallController));
        if (_owner != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Community install requires the launcher owner thread.");
    }

    public bool Begin(NativeCommunityEntry selected)
    {
        Owner();
        if (_busy || !IsVerifiedSelection(selected)) return false;
        Retire();
        _expected = selected;
        _completedHash = "";
        _completedMapId = null;
        var cancel = new CancellationTokenSource();
        _active = cancel;
        _busy = true;
        _status = "DOWNLOADING AND VERIFYING IMMUTABLE PACKAGE";
        _revision++;
        long epoch = _epoch;
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _installer.InstallAsync(selected, cancel.Token)
                    .ConfigureAwait(false);
                if (!cancel.IsCancellationRequested)
                    _completed.Enqueue((epoch, result, null));
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                // Do not expose HTTP response bodies, filesystem paths or tickets.
                if (!cancel.IsCancellationRequested)
                    _completed.Enqueue((epoch, null, "INSTALL FAILED // CHECK COMMUNITY SERVICE AND MAP FILES"));
            }
            finally { cancel.Dispose(); }
        });
        return true;
    }

    public void FailPreflight()
    {
        Owner();
        if (_busy) return;
        _status = "SET UP GAME FILES OR LEAVE THE ACTIVE MATCH BEFORE INSTALLING";
        _revision++;
    }

    private void Retire()
    {
        _epoch++;
        try { _active?.Cancel(); } catch (ObjectDisposedException) { }
        _active = null;
    }

    public void Cancel()
    {
        Owner();
        if (!_busy) return;
        Retire();
        _busy = false;
        _status = "INSTALL HANDOFF CANCELLED // DOWNLOAD RECEIPT RETAINED";
        _revision++;
    }

    public NativeCommunityInstallView Tick()
    {
        Owner();
        while (_completed.TryDequeue(out var finish))
        {
            if (finish.Epoch != _epoch) continue;
            _busy = false;
            _active = null;
            var result = finish.Result;
            if (result != null && result.Definition != null
                && result.MapId == _expected.MapId
                && result.Definition.MapId == _expected.MapId
                && result.PackageHash.Equals(_expected.LatestHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    _installer.Register(result);
                    _completedMapId = result.MapId;
                    _completedHash = result.PackageHash;
                    _status = "VERIFIED MAP INSTALLED // READY IN MAP PICKER";
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    _status = "ARCHIVE SAVED // MAP REGISTRATION UNAVAILABLE";
                }
            }
            else
            {
                _status = finish.Error ?? "PACKAGE IDENTITY DID NOT MATCH SELECTION";
            }
            _revision++;
        }
        return new(_revision, _busy, _status, _completedMapId, _completedHash);
    }

    public void Dispose()
    {
        if (_disposed) return;
        Owner();
        Retire();
        _disposed = true;
        while (_completed.TryDequeue(out _)) { }
    }
}
