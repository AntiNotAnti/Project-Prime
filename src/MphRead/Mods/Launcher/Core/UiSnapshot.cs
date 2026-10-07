using System;
using System.Threading;

namespace MphRead.Mods.Launcher.Core;

internal enum UiPageState { Loading, Ready, Empty, Offline, Unauthorized, Failed, Cancelled }

/// <summary>Presenter data is valid only for one page lifetime and a monotonically increasing revision.</summary>
internal readonly record struct UiSnapshotStamp(LauncherPage Page, Guid Lifetime, long Revision);

internal abstract record UiPageSnapshot(UiSnapshotStamp Stamp, UiPageState State);

/// <summary>Captured by background operations; completion still returns to the engine owner thread.</summary>
internal readonly record struct LauncherPageLifetime(LauncherRoute Route, Guid Id, CancellationToken Cancellation)
{
    public UiSnapshotStamp Stamp(long revision) => new(Route.Page, Id, revision);
}

/// <summary>Owner-thread snapshot gate. A presenter must bind again after navigation or device recreation.</summary>
internal sealed class UiSnapshotGate
{
    private LauncherPage _page;
    private Guid _lifetime;
    private CancellationToken _cancellation;
    private long _revision = -1;
    public long Revision => _revision;

    public void Bind(LauncherPageLifetime lifetime)
    {
        if (lifetime.Id == Guid.Empty || lifetime.Cancellation.IsCancellationRequested
            || !LauncherRouteCatalog.IsValid(lifetime.Route))
            throw new ArgumentException("Cannot bind an invalid or cancelled page lifetime.", nameof(lifetime));
        _page = lifetime.Route.Page;
        _lifetime = lifetime.Id;
        _cancellation = lifetime.Cancellation;
        _revision = -1;
    }

    public bool Accept(UiSnapshotStamp stamp)
    {
        if (_lifetime == Guid.Empty || _cancellation.IsCancellationRequested
            || stamp.Lifetime != _lifetime || stamp.Page != _page
            || stamp.Revision < 0 || stamp.Revision <= _revision) return false;
        _revision = stamp.Revision;
        return true;
    }

    public void Invalidate() { _lifetime = Guid.Empty; _cancellation = default; _revision = -1; }
}
