using System;
using System.Collections.Generic;
using System.Threading;

namespace MphRead.Mods.Launcher.Core;

internal enum LauncherNavigationMode { Push, Replace }
internal enum LauncherNavigationAction { Navigate, Back, NextTab, PreviousTab, CloseModal }
internal readonly record struct LauncherNavigationIntent(LauncherNavigationAction Action,
    LauncherRoute Route = default, Guid Modal = default);
internal enum LauncherNavigationOutcome { Unchanged, Changed, Blocked, ExitConfirmationRequired }
internal enum LauncherModalKind { Dialog, Confirmation, ExitConfirmation, Error, Busy }
internal enum LauncherModalPriority { Normal, Important, Critical }
internal readonly record struct LauncherFocus(Guid Lifetime, string Target);
internal sealed record LauncherModal(Guid Id, LauncherModalKind Kind, LauncherModalPriority Priority,
    bool CanDismiss, Guid Lifetime, CancellationToken Cancellation,
    LauncherFocus? RestoreFocus, LauncherFocus? Focus);
internal sealed record LauncherNavigationSnapshot(long Revision, LauncherRoute Current,
    LauncherPageLifetime Lifetime, LauncherFocus? Focus, IReadOnlyList<LauncherRoute> History,
    IReadOnlyList<LauncherModal> Modals)
{
    public LauncherModal? TopModal => Modals.Count == 0 ? null : Modals[^1];
}

/// <summary>
/// Engine-owner-thread navigation policy shared by presenters. It owns route and document lifetimes,
/// never a session, native document, toolkit control, or background callback dispatcher.
/// </summary>
internal sealed class LauncherRouter : IDisposable
{
    private sealed record HistoryEntry(LauncherRoute Route, string? Focus);
    private const int HistoryLimit = 64;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly List<HistoryEntry> _history = new();
    private readonly List<LauncherModal> _modals = new();
    private readonly Dictionary<Guid, CancellationTokenSource> _modalCancellations = new();
    private CancellationTokenSource _cancellation = new();
    private LauncherPageLifetime _lifetime;
    private LauncherFocus? _focus;
    private long _revision;
    private bool _disposed;
    private bool _retiringWork;

    public LauncherRouter(LauncherRoute initial = default)
    {
        RequireRoute(initial);
        _lifetime = new(initial, Guid.NewGuid(), _cancellation.Token);
    }

    public LauncherRoute Current => _lifetime.Route;
    public LauncherRoute? Previous => _history.Count == 0 ? null : _history[^1].Route;
    public LauncherPageLifetime Lifetime => _lifetime;
    public bool HasModal => _modals.Count != 0;
    public Func<LauncherRoute, bool>? CanNavigate { get; set; }
    public event Action<LauncherNavigationSnapshot>? Changed;
    public event Action<AggregateException>? CancellationFaulted;

    public LauncherNavigationSnapshot Snapshot() => new(_revision, Current, _lifetime, _focus,
        Array.AsReadOnly(_history.ConvertAll(entry => entry.Route).ToArray()),
        Array.AsReadOnly(_modals.ToArray()));

    public LauncherNavigationOutcome Dispatch(LauncherNavigationIntent intent) => intent.Action switch
    {
        LauncherNavigationAction.Navigate => Navigate(intent.Route),
        LauncherNavigationAction.Back => Back(),
        LauncherNavigationAction.NextTab => Step(1),
        LauncherNavigationAction.PreviousTab => Step(-1),
        LauncherNavigationAction.CloseModal => CloseModal(intent.Modal),
        _ => throw new ArgumentOutOfRangeException(nameof(intent))
    };

    public LauncherNavigationOutcome Navigate(LauncherRoute route,
        LauncherNavigationMode mode = LauncherNavigationMode.Push)
    {
        CheckOwner();
        RequireRoute(route);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (route == Current) return LauncherNavigationOutcome.Unchanged;
        if (HasModal) return LauncherNavigationOutcome.Blocked;
        Guid beforeGuard = _lifetime.Id;
        long beforeRevision = _revision;
        if (CanNavigate?.Invoke(route) == false || beforeGuard != _lifetime.Id
            || beforeRevision != _revision || HasModal)
            return LauncherNavigationOutcome.Blocked;
        if (mode == LauncherNavigationMode.Push)
        {
            _history.Add(new(Current, _focus?.Target));
            if (_history.Count > HistoryLimit) _history.RemoveAt(0);
        }
        Enter(route, null);
        return LauncherNavigationOutcome.Changed;
    }

    public LauncherNavigationOutcome Back()
    {
        CheckOwner();
        if (HasModal) return CloseModal(_modals[^1].Id);
        if (_history.Count == 0) return LauncherNavigationOutcome.ExitConfirmationRequired;
        HistoryEntry previous = _history[^1];
        Guid beforeGuard = _lifetime.Id;
        long beforeRevision = _revision;
        if (CanNavigate?.Invoke(previous.Route) == false || beforeGuard != _lifetime.Id
            || beforeRevision != _revision || HasModal)
            return LauncherNavigationOutcome.Blocked;
        _history.RemoveAt(_history.Count - 1);
        Enter(previous.Route, previous.Focus);
        return LauncherNavigationOutcome.Changed;
    }

    /// <summary>Authoritative leave/disconnect/start transitions discard pending page work and history.</summary>
    public void Reset(LauncherRoute route)
    {
        CheckOwner();
        RequireRoute(route);
        _history.Clear();
        ClearModals();
        Enter(route, null);
    }

    /// <summary>Device recreation or document replacement invalidates pending work without changing the route.</summary>
    public void RenewLifetime()
    {
        CheckOwner();
        string? focus = HasModal ? _modals[0].RestoreFocus?.Target : _focus?.Target;
        ClearModals();
        Enter(Current, focus);
    }

    public void Forget(LauncherPage page)
    {
        CheckOwner();
        if (_history.RemoveAll(entry => entry.Route.Page == page) > 0) Publish();
    }

    public void RememberFocus(string? target)
    {
        CheckOwner();
        if (target != null && (target.Length == 0 || target.IndexOf('\0') >= 0))
            throw new ArgumentException("Focus target must be a nonempty presenter identity.", nameof(target));
        Guid scope = HasModal ? _modals[^1].Lifetime : _lifetime.Id;
        LauncherFocus? focus = target == null ? null : new LauncherFocus(scope, target);
        if (focus == _focus) return;
        _focus = focus;
        if (HasModal) _modals[^1] = _modals[^1] with { Focus = _focus };
        Publish();
    }

    public LauncherModal OpenModal(LauncherModalKind kind, LauncherModalPriority priority = LauncherModalPriority.Normal,
        bool canDismiss = true, string? initialFocus = null)
    {
        CheckOwner();
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(priority)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (initialFocus != null && (initialFocus.Length == 0 || initialFocus.IndexOf('\0') >= 0))
            throw new ArgumentException("Invalid initial focus.", nameof(initialFocus));
        Guid lifetime = Guid.NewGuid();
        int index = _modals.FindLastIndex(entry => entry.Priority <= priority) + 1;
        LauncherFocus? restore = index == 0 ? (HasModal ? _modals[0].RestoreFocus : _focus)
            : _modals[index - 1].Focus;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Cancellation);
        var modal = new LauncherModal(Guid.NewGuid(), kind, priority, canDismiss, lifetime,
            cancellation.Token, restore, initialFocus == null ? null : new LauncherFocus(lifetime, initialFocus));
        _modalCancellations.Add(modal.Id, cancellation);
        _modals.Insert(index, modal);
        if (index == _modals.Count - 1) _focus = modal.Focus;
        Publish();
        return modal;
    }

    /// <summary>Only the foreground modal can close. Back cannot dismiss a required busy/confirmation layer.</summary>
    public LauncherNavigationOutcome CloseModal(Guid id, bool force = false)
    {
        CheckOwner();
        if (!HasModal || _modals[^1].Id != id) return LauncherNavigationOutcome.Unchanged;
        LauncherModal modal = _modals[^1];
        if (!modal.CanDismiss && !force) return LauncherNavigationOutcome.Blocked;
        _modals.RemoveAt(_modals.Count - 1);
        Guid focusScope = HasModal ? _modals[^1].Lifetime : _lifetime.Id;
        _focus = modal.RestoreFocus?.Lifetime == focusScope ? modal.RestoreFocus
            : HasModal ? _modals[^1].Focus : null;
        Retire(_modalCancellations[modal.Id]);
        _modalCancellations.Remove(modal.Id);
        Publish();
        return LauncherNavigationOutcome.Changed;
    }

    public bool IsCurrent(UiSnapshotStamp stamp) => !_disposed && stamp.Page == Current.Page
        && stamp.Lifetime == _lifetime.Id && stamp.Revision >= 0 && !_lifetime.Cancellation.IsCancellationRequested;

    private LauncherNavigationOutcome Step(int direction)
    {
        CheckOwner();
        IReadOnlyList<LauncherPage> tabs = LauncherRouteCatalog.Tabs;
        LauncherPage current = Current.Page == LauncherPage.Lobby ? LauncherPage.Play : Current.Page;
        int index = -1;
        for (int i = 0; i < tabs.Count; i++) if (tabs[i] == current) index = i;
        if (index < 0) index = 0;
        return Navigate(new(tabs[(index + direction + tabs.Count) % tabs.Count]));
    }

    private void Enter(LauncherRoute route, string? focus)
    {
        CancellationTokenSource retired = _cancellation;
        _cancellation = new();
        _lifetime = new(route, Guid.NewGuid(), _cancellation.Token);
        _focus = focus == null ? null : new LauncherFocus(_lifetime.Id, focus);
        // Publish the new lifetime before cancellation callbacks can inspect the router.
        Retire(retired);
        Publish();
    }

    private void ClearModals()
    {
        _modals.Clear();
        // Remove all ownership before running user cancellation callbacks.
        var retired = new List<CancellationTokenSource>(_modalCancellations.Values);
        _modalCancellations.Clear();
        foreach (CancellationTokenSource source in retired) Retire(source);
    }

    private void Retire(CancellationTokenSource source)
    {
        bool wasRetiring = _retiringWork;
        _retiringWork = true;
        try { source.Cancel(); }
        catch (AggregateException error) { CancellationFaulted?.Invoke(error); }
        finally { _retiringWork = wasRetiring; source.Dispose(); }
    }

    private void Publish() { _revision++; Changed?.Invoke(Snapshot()); }
    private static void RequireRoute(LauncherRoute route)
    {
        if (!LauncherRouteCatalog.IsValid(route)) throw new ArgumentException("Invalid launcher route.", nameof(route));
    }

    private void CheckOwner()
    {
        if (Environment.CurrentManagedThreadId != _owner)
            throw new InvalidOperationException("Launcher navigation must run on its engine owner thread.");
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_retiringWork)
            throw new InvalidOperationException("Cancellation callbacks must enqueue work instead of mutating launcher navigation.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        CheckOwner();
        _disposed = true;
        _history.Clear();
        ClearModals();
        Changed = null;
        Retire(_cancellation);
        CancellationFaulted = null;
    }
}
