using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using MphRead.Identity;

namespace MphRead.Mods.Launcher.Gui;

public enum PrimeInputDevice
{
    Unknown,
    KeyboardMouse,
    Gamepad,
    Touch
}

/// <summary>
/// Global shell facts only. Account, Node, and gameplay services retain their
/// own live objects; this state carries display-safe summaries and revisions.
/// </summary>
public sealed class PrimeShellState : INotifyPropertyChanged, IDisposable
{
    internal static readonly TimeSpan DefaultNotificationDuration = TimeSpan.FromSeconds(5);

    private bool _signedIn;
    private bool _guestSelected;
    private bool _accountEligible;
    private bool _backendConnected;
    private bool _nodeConnected;
    private bool _active;
    private string _displayName = "Guest";
    private string _nodeName = "";
    private string _nodeRegion = "";
    private string? _busyOperation;
    private PrimeNotification? _notification;
    private PrimeInputDevice _lastInputDevice = PrimeInputDevice.Unknown;
    private PlayerId? _playerId;
    private long _identityGeneration;
    private readonly object _notificationLock = new();
    private readonly Timer _notificationTimer;
    private readonly Dictionary<string, NotificationEntry> _notifications = new(
        StringComparer.Ordinal);
    private long _notificationSequence;
    private bool _disposed;

    public PrimeShellState(PrimeNavigator? navigator = null)
    {
        Navigator = navigator ?? new PrimeNavigator();
        Navigator.Changed += NavigatorChanged;
        _notificationTimer = new Timer(NotificationTimerElapsed, null,
            Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? IdentityChanged;

    public PrimeNavigator Navigator { get; }
    public PrimeRoute CurrentRoute => Navigator.CurrentRoute;
    public bool SignedIn { get => _signedIn; private set => Set(ref _signedIn, value); }
    public bool GuestSelected { get => _guestSelected; private set => Set(ref _guestSelected, value); }
    public bool HasNetworkIdentity => SignedIn || GuestSelected;
    public bool AccountEligible { get => _accountEligible; private set => Set(ref _accountEligible, value); }
    public bool BackendConnected { get => _backendConnected; private set => Set(ref _backendConnected, value); }
    public bool NodeConnected { get => _nodeConnected; private set => Set(ref _nodeConnected, value); }
    public bool IsActive { get => _active; private set => Set(ref _active, value); }
    public string DisplayName { get => _displayName; private set => Set(ref _displayName, value); }
    public string NodeName { get => _nodeName; private set => Set(ref _nodeName, value); }
    public string NodeRegion { get => _nodeRegion; private set => Set(ref _nodeRegion, value); }
    public string? BusyOperation { get => _busyOperation; private set => Set(ref _busyOperation, value); }
    public PrimeNotification? Notification
    {
        get { lock (_notificationLock) return _notification; }
    }
    public PrimeInputDevice LastInputDevice { get => _lastInputDevice; private set => Set(ref _lastInputDevice, value); }
    public PlayerId? PlayerId => _playerId;
    public long IdentityGeneration => _identityGeneration;

    public void SetIdentity(PlayerId playerId, string displayName, bool emailEligible)
    {
        if (playerId.IsEmpty) throw new ArgumentException("A player identity is required.", nameof(playerId));
        if (displayName is not { Length: >= 1 and <= 16 })
            throw new ArgumentException("A display name must contain 1 to 16 characters.", nameof(displayName));
        _playerId = playerId;
        GuestSelected = false;
        DisplayName = displayName;
        AccountEligible = emailEligible;
        SignedIn = true;
        BackendConnected = true;
        InvalidateIdentityCaches();
    }

    public void ClearIdentity()
    {
        _playerId = null;
        SignedIn = false;
        GuestSelected = false;
        AccountEligible = false;
        DisplayName = "Guest";
        InvalidateIdentityCaches();
    }

    /// <summary>
    /// Records an explicit anonymous-admission choice. A failed restore or
    /// sign-in never calls this method.
    /// </summary>
    public void SelectGuest(string displayName)
    {
        string value = displayName?.Trim() ?? "";
        if (value is not { Length: >= 1 and <= 16 })
            throw new ArgumentException("A guest display name must contain 1 to 16 characters.", nameof(displayName));
        _playerId = null;
        SignedIn = false;
        GuestSelected = true;
        AccountEligible = false;
        DisplayName = value;
        InvalidateIdentityCaches();
    }

    public void SetBackendStatus(bool connected) => BackendConnected = connected;

    public void SetDisplayName(string displayName)
    {
        if (!SignedIn) throw new InvalidOperationException("A signed-in identity is required.");
        if (displayName is not { Length: >= 1 and <= 16 })
            throw new ArgumentException("A display name must contain 1 to 16 characters.", nameof(displayName));
        DisplayName = displayName;
    }

    public void SetNodeStatus(bool connected, string? name = null, string? region = null)
    {
        NodeConnected = connected;
        NodeName = connected ? name?.Trim() ?? "" : "";
        NodeRegion = connected ? region?.Trim() ?? "" : "";
    }

    public void SetBusy(string? operation) => BusyOperation = operation;

    /// <summary>Compatibility helper for existing command producers.</summary>
    public void Notify(PrimeNotificationKind kind, string message)
        => NotifyTransient("command", kind, message);

    public void NotifyTransient(string key, PrimeNotificationKind kind, string message,
        TimeSpan? duration = null)
        => SetNotification(new PrimeNotification(key, kind, message,
            PrimeNotificationScope.Transient, duration ?? DefaultNotificationDuration));

    public void NotifyRoute(string key, PrimeNotificationKind kind, string message,
        TimeSpan? duration = null)
        => NotifyRoute(key, kind, message,
            PrimeRoutePresentation.Normalize(CurrentRoute), duration);

    public void NotifyRoute(string key, PrimeNotificationKind kind, string message,
        PrimeRoute route, TimeSpan? duration = null)
    {
        PrimeRoute owner = PrimeRoutePresentation.Normalize(route);
        if (owner != PrimeRoutePresentation.Normalize(CurrentRoute)) return;
        SetNotification(new PrimeNotification(key, kind, message,
            PrimeNotificationScope.Route, duration, owner));
    }

    public void NotifyGlobal(string key, PrimeNotificationKind kind, string message,
        TimeSpan? duration = null)
        => SetNotification(new PrimeNotification(key, kind, message,
            PrimeNotificationScope.Global, duration));

    public void ClearNotification(string? key = null)
    {
        bool changed;
        lock (_notificationLock)
        {
            PrimeNotification? previous = _notification;
            if (key is null) _notifications.Clear();
            else if (!_notifications.Remove(key)) return;
            SelectLatestNotificationLocked();
            ScheduleNotificationTimerLocked();
            changed = !Equals(previous, _notification);
        }
        if (changed) OnPropertyChanged(nameof(Notification));
    }

    public void DismissNotification()
    {
        string? key;
        lock (_notificationLock) key = _notification?.Key;
        if (key is not null) ClearNotification(key);
    }

    public void SetLastInputDevice(PrimeInputDevice device) => LastInputDevice = device;

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;

    /// <summary>Invalidates account-scoped caches without retaining credentials.</summary>
    public void InvalidateIdentityCaches()
    {
        _identityGeneration++;
        OnPropertyChanged(nameof(PlayerId));
        OnPropertyChanged(nameof(IdentityGeneration));
        IdentityChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        lock (_notificationLock)
        {
            if (_disposed) return;
            _disposed = true;
            _notifications.Clear();
            _notification = null;
            _notificationTimer.Change(Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);
        }
        Navigator.Changed -= NavigatorChanged;
        _notificationTimer.Dispose();
    }

    private void NavigatorChanged(object? sender, PrimeNavigationChangedEventArgs args)
    {
        ClearRouteNotification(args.Route);
        OnPropertyChanged(nameof(CurrentRoute));
    }

    private void SetNotification(PrimeNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        bool changed;
        lock (_notificationLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_notifications.TryGetValue(notification.Key, out NotificationEntry existing)
                && existing.Notification.Kind == notification.Kind
                && existing.Notification.Scope == notification.Scope
                && existing.Notification.Route == notification.Route
                && existing.Notification.Duration == notification.Duration
                && String.Equals(existing.Notification.Message, notification.Message,
                    StringComparison.Ordinal))
                return;
            PrimeNotification? previous = _notification;
            _notifications[notification.Key] = new NotificationEntry(notification,
                ++_notificationSequence);
            while (_notifications.Count > 16)
            {
                string oldest = _notifications.MinBy(pair => pair.Value.Sequence).Key;
                _notifications.Remove(oldest);
            }
            SelectLatestNotificationLocked();
            ScheduleNotificationTimerLocked();
            changed = !Equals(previous, _notification);
        }
        if (changed) OnPropertyChanged(nameof(Notification));
    }

    private void ClearRouteNotification(PrimeRoute route)
    {
        bool changed = false;
        PrimeRoute normalized = PrimeRoutePresentation.Normalize(route);
        lock (_notificationLock)
        {
            PrimeNotification? previous = _notification;
            string[] expiredRoutes = _notifications
                .Where(pair => pair.Value.Notification is
                    { Scope: PrimeNotificationScope.Route } current
                    && current.Route != normalized)
                .Select(pair => pair.Key).ToArray();
            foreach (string key in expiredRoutes)
                _notifications.Remove(key);
            if (expiredRoutes.Length > 0)
            {
                SelectLatestNotificationLocked();
                ScheduleNotificationTimerLocked();
                changed = !Equals(previous, _notification);
            }
        }
        if (changed) OnPropertyChanged(nameof(Notification));
    }

    private void NotificationTimerElapsed(object? state)
    {
        bool changed = false;
        lock (_notificationLock)
        {
            if (_disposed) return;
            PrimeNotification? previous = _notification;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            string[] expired = _notifications
                .Where(pair => pair.Value.Notification.ExpiresAt is { } expires
                    && expires <= now)
                .Select(pair => pair.Key).ToArray();
            foreach (string key in expired)
                _notifications.Remove(key);
            SelectLatestNotificationLocked();
            ScheduleNotificationTimerLocked();
            changed = !Equals(previous, _notification);
        }
        if (changed) OnPropertyChanged(nameof(Notification));
    }

    private void SelectLatestNotificationLocked()
        => _notification = _notifications.Count == 0 ? null
            : _notifications.MaxBy(pair => pair.Value.Sequence).Value.Notification;

    private void ScheduleNotificationTimerLocked()
    {
        DateTimeOffset? next = null;
        foreach (NotificationEntry entry in _notifications.Values)
        {
            if (entry.Notification.ExpiresAt is { } expires
                && (!next.HasValue || expires < next.Value))
                next = expires;
        }
        TimeSpan due = next is { } nextExpires
            ? MaxTimerDue(nextExpires - DateTimeOffset.UtcNow)
            : Timeout.InfiniteTimeSpan;
        _notificationTimer.Change(due, Timeout.InfiniteTimeSpan);
    }

    private static TimeSpan MaxTimerDue(TimeSpan due)
        => due <= TimeSpan.Zero ? TimeSpan.Zero
            : due > TimeSpan.FromMilliseconds(UInt32.MaxValue - 1)
                ? TimeSpan.FromMilliseconds(UInt32.MaxValue - 1) : due;

    private readonly record struct NotificationEntry(PrimeNotification Notification,
        long Sequence);

    private void Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (Equals(field, value)) return;
        field = value;
        OnPropertyChanged(property);
    }

    private void OnPropertyChanged(string? property)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
