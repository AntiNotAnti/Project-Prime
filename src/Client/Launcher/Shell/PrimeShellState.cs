using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
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

    public PrimeShellState(PrimeNavigator? navigator = null)
    {
        Navigator = navigator ?? new PrimeNavigator();
        Navigator.Changed += NavigatorChanged;
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
    public PrimeNotification? Notification { get => _notification; private set => Set(ref _notification, value); }
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

    public void Notify(PrimeNotificationKind kind, string message)
        => Notification = new PrimeNotification(kind, message);

    public void ClearNotification() => Notification = null;

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

    public void Dispose() => Navigator.Changed -= NavigatorChanged;

    private void NavigatorChanged(object? sender, PrimeNavigationChangedEventArgs args)
        => OnPropertyChanged(nameof(CurrentRoute));

    private void Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (Equals(field, value)) return;
        field = value;
        OnPropertyChanged(property);
    }

    private void OnPropertyChanged(string? property)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
