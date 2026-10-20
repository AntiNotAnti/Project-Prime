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
    Task<NativeCommunityInstalled> InstallAsync(NativeCommunityEntry selected,
        CancellationToken cancellation, string expectedRoot) => InstallAsync(selected, cancellation);
    // Called only from the launcher's owner thread after a verified install.
    void Register(NativeCommunityInstalled installed);
}

public sealed record NativeCommunityInstalled(Guid MapId, string PackageHash,
    MapDefinition Definition, MapContentIdentity? Identity = null);

public sealed record NativeCommunityHostSelection(string ConfiguredRoot, MapContentIdentity Identity);

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
    private string _expectedRoot = "";
    private bool _hostAfterInstall;
    private NativeCommunityHostSelection? _hostRequest;
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

    public bool Begin(NativeCommunityEntry selected, bool hostAfterInstall = false)
    {
        Owner();
        _hostRequest = null;
        if (_busy || !IsVerifiedSelection(selected)) return false;
        Retire();
        _expected = selected;
        string expectedRoot = _expectedRoot = GameFiles.Root;
        _hostAfterInstall = hostAfterInstall;
        _completedHash = "";
        _completedMapId = null;
        var cancel = new CancellationTokenSource();
        _active = cancel;
        _busy = true;
        _status = selected.Installed ? "VERIFYING INSTALLED PACKAGE" : "DOWNLOADING AND VERIFYING IMMUTABLE PACKAGE";
        _revision++;
        long epoch = _epoch;
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _installer.InstallAsync(selected, cancel.Token, expectedRoot)
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
        _hostRequest = null;
        if (!_busy) return;
        Retire();
        _busy = false;
        _status = "INSTALL HANDOFF CANCELLED // DOWNLOAD RECEIPT RETAINED";
        _revision++;
    }

    public NativeCommunityInstallView Tick()
    {
        Owner();
        if (!String.Equals(_expectedRoot, GameFiles.Root, StringComparison.Ordinal))
        {
            _hostRequest = null;
            if (_busy)
            {
                Retire();
                _busy = false;
                _status = "CONFIGURED GAME FILES CHANGED // INSTALL HANDOFF CANCELLED";
                _revision++;
            }
        }
        while (_completed.TryDequeue(out var finish))
        {
            if (finish.Epoch != _epoch) continue;
            _busy = false;
            _active = null;
            _hostRequest = null;
            var result = finish.Result;
            if (result != null && result.Definition != null
                && result.MapId == _expected.MapId
                && result.Definition.MapId == _expected.MapId
                && String.Equals(result.PackageHash, _expected.LatestHash,
                    StringComparison.OrdinalIgnoreCase)
                && (!_hostAfterInstall || ValidHostIdentity(result)))
            {
                try
                {
                    _installer.Register(result);
                    if (_hostAfterInstall)
                        _hostRequest = new(_expectedRoot, result.Identity!.Value);
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

    private static bool ValidHostIdentity(NativeCommunityInstalled result)
        => !result.Definition.RequiresPrivateSource && result.Identity is { } identity && identity.IsCustom
            && identity.MapId == result.MapId && identity.MapId != Guid.Empty
            && !String.IsNullOrWhiteSpace(identity.RoomKey)
            && String.Equals(identity.RoomKey, result.Definition.Name, StringComparison.OrdinalIgnoreCase)
            && !identity.ContentHash.IsZero && !identity.PackageHash.IsZero
            && MapHash256.TryParse(result.PackageHash, out var packageHash)
            && identity.PackageHash == packageHash;

    public bool TryTakeHostRequest(out NativeCommunityHostSelection? selection)
    {
        Owner();
        if (!String.Equals(_expectedRoot, GameFiles.Root, StringComparison.Ordinal))
            _hostRequest = null;
        selection = _hostRequest;
        _hostRequest = null;
        return selection != null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Owner();
        Retire();
        _hostRequest = null;
        _disposed = true;
        while (_completed.TryDequeue(out _)) { }
    }
}
