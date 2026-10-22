using System;

namespace MphRead.Mods.Network;

/// <summary>Identity fence around an existing client join; never owns a second pump.</summary>
public sealed class NetClientJoinOwnership
{
    private static object _currentIncarnation = new();
    internal static void RetireIncarnation() => _currentIncarnation = new();
    private NetTransport? _candidate;
    private object? _incarnation;
    private NetRole _role;
    private bool _authority;
    internal void Capture()
    {
        lock (NetSession.ClientLifecycleGate)
        {
            _authority = false;
            _candidate = NetSession.EvidenceTransport;
            _incarnation = _currentIncarnation;
            _role = NetSession.Role;
        }
    }
    internal void CaptureAuthority()
    {
        lock (NetSession.ClientLifecycleGate)
        {
            if (!NetSession.IsServer || !NetSession.IsAuthority)
                throw new InvalidOperationException("There is no current server authority to capture.");
            _authority = true;
            _candidate = NetSession.EvidenceTransport;
            _incarnation = _currentIncarnation;
            _role = NetSession.Role;
        }
    }
    private bool Matches() => (_candidate != null || _authority && _role == NetRole.Server && NetSession.IsAuthority)
        && ReferenceEquals(_candidate, NetSession.EvidenceTransport)
        && ReferenceEquals(_incarnation, _currentIncarnation) && NetSession.Role == _role;
    public bool IsCurrent
    {
        get { lock (NetSession.ClientLifecycleGate) return Matches() && NetSession.IsClient; }
    }
    internal bool RunCurrent(Action action)
    {
        lock (NetSession.ClientLifecycleGate)
        {
            if (!IsCurrent) return false;
            action(); return IsCurrent;
        }
    }
    internal bool IsCurrentAuthority
    {
        get { lock (NetSession.ClientLifecycleGate) return _authority && Matches() && NetSession.IsServer && NetSession.IsAuthority; }
    }
    internal bool RunCurrentAuthority(Action action)
    {
        lock (NetSession.ClientLifecycleGate)
        {
            if (!IsCurrentAuthority) return false;
            action(); return true;
        }
    }
    public bool TryStopCurrent()
    {
        lock (NetSession.ClientLifecycleGate)
        {
            if (_authority || !Matches()) return false;
            NetSession.Stop(); return true;
        }
    }
}
