using System;
using System.Diagnostics;

namespace MphRead.Mods.Input
{
    /// <summary>
    /// The input owner that supplied <see cref="ControllerCapabilitySnapshot"/>.
    /// This is deliberately a backend identity rather than a controller family:
    /// an Xbox-shaped controller can arrive through any of the desktop or
    /// Android bridges.
    /// </summary>
    public enum ControllerBackend
    {
        None,
        Sdl,
        Glfw,
        Android
    }

    /// <summary>
    /// An immutable description of the active controller and the capabilities
    /// that the owning platform can truthfully report.
    ///
    /// A nullable capability is intentional. The platform may know that a
    /// controller is connected without exposing a non-invasive query for one
    /// of its optional features. Unknown is kept distinct from unsupported so
    /// settings never disable a user's preference merely because a bridge could
    /// not answer.
    /// </summary>
    public readonly record struct ControllerCapabilitySnapshot(
        ControllerBackend Backend,
        bool IsBackendAvailable,
        bool IsConnected,
        ControllerFamily Family,
        bool? HasGyroscope,
        bool? HasRumble,
        bool? HasAnalogTriggers,
        string? DeviceId,
        string? DeviceName)
    {
        /// <summary>
        /// True only when this snapshot identifies a connected device and an
        /// active backend. A backend can remain available while no device is
        /// connected.
        /// </summary>
        public bool IsAvailable => IsBackendAvailable && IsConnected;

        /// <summary>No input backend is currently publishing a snapshot.</summary>
        public static ControllerCapabilitySnapshot Unavailable(
            ControllerBackend backend = ControllerBackend.None)
            => new(backend, false, false, ControllerFamily.Generic,
                null, null, null, null, null);

        /// <summary>
        /// A live backend with no active controller. Optional capabilities are
        /// cleared so a disconnected controller cannot leak into the next
        /// device's settings presentation.
        /// </summary>
        public static ControllerCapabilitySnapshot Disconnected(
            ControllerBackend backend, bool backendAvailable = true)
            => new(backend, backendAvailable, false, ControllerFamily.Generic,
                null, null, null, null, null);

        /// <summary>
        /// A connected device and the capabilities reported by its owner.
        /// Optional values should be null when the platform has no truthful
        /// non-invasive query; callers must not infer them from family or user
        /// preferences.
        /// </summary>
        public static ControllerCapabilitySnapshot Connected(
            ControllerBackend backend,
            string? deviceId,
            string? deviceName,
            ControllerFamily family = ControllerFamily.Generic,
            bool? hasGyroscope = null,
            bool? hasRumble = null,
            bool? hasAnalogTriggers = null)
            => new(backend, true, true, family, hasGyroscope, hasRumble,
                hasAnalogTriggers, deviceId, deviceName);
    }

    /// <summary>One immutable transition in the active capability snapshot.</summary>
    public sealed class ControllerCapabilityChangedEventArgs : EventArgs
    {
        public ControllerCapabilityChangedEventArgs(
            ControllerCapabilitySnapshot previous,
            ControllerCapabilitySnapshot current)
        {
            Previous = previous;
            Current = current;
        }

        public ControllerCapabilitySnapshot Previous { get; }
        public ControllerCapabilitySnapshot Current { get; }
    }

    /// <summary>
    /// Owns one platform publisher and exposes its last immutable snapshot.
    ///
    /// Publishers are leases rather than a public mutable singleton: replacing
    /// a backend retires the previous lease, and every later callback from that
    /// lease is rejected. Publication is synchronous and bounded to the event
    /// subscribers; no polling queue or unowned worker is introduced.
    /// </summary>
    public sealed class ControllerCapabilityStore
    {
        private readonly object _gate = new();
        private ControllerCapabilitySnapshot _current =
            ControllerCapabilitySnapshot.Unavailable();
        private ControllerCapabilityOwner? _owner;

        public ControllerCapabilitySnapshot Current
        {
            get
            {
                lock (_gate) return _current;
            }
        }

        public event EventHandler<ControllerCapabilityChangedEventArgs>? Changed;

        /// <summary>
        /// Acquire a publisher for a backend. A fallback owner cannot displace
        /// an active owner; an authoritative platform owner may replace it.
        /// Replacing an owner retires it before the new owner is returned, so a
        /// late callback from the old platform cannot revive its snapshot.
        /// </summary>
        internal ControllerCapabilityOwner? TryAcquire(
            ControllerBackend backend, bool replaceCurrent)
        {
            if (backend == ControllerBackend.None)
            {
                throw new ArgumentOutOfRangeException(nameof(backend));
            }

            EventHandler<ControllerCapabilityChangedEventArgs>? handlers = null;
            ControllerCapabilityChangedEventArgs? args = null;
            ControllerCapabilityOwner owner;
            lock (_gate)
            {
                if (_owner is { } existing)
                {
                    if (!replaceCurrent)
                    {
                        return existing.Backend == backend ? existing : null;
                    }
                    existing.MarkDisposedLocked();
                    ControllerCapabilitySnapshot unavailable =
                        ControllerCapabilitySnapshot.Unavailable(existing.Backend);
                    if (_current != unavailable)
                    {
                        ControllerCapabilitySnapshot previous = _current;
                        _current = unavailable;
                        args = new ControllerCapabilityChangedEventArgs(
                            previous, unavailable);
                        handlers = Changed;
                    }
                }

                owner = new ControllerCapabilityOwner(this, backend);
                _owner = owner;
            }

            RaiseChanged(this, handlers, args);
            return owner;
        }

        internal bool IsCurrent(ControllerCapabilityOwner owner)
        {
            lock (_gate) return ReferenceEquals(_owner, owner) && !owner.IsDisposed;
        }

        internal bool Publish(ControllerCapabilityOwner owner,
            ControllerCapabilitySnapshot snapshot)
        {
            EventHandler<ControllerCapabilityChangedEventArgs>? handlers;
            ControllerCapabilityChangedEventArgs? args;
            lock (_gate)
            {
                if (!ReferenceEquals(_owner, owner) || owner.IsDisposed
                    || snapshot.Backend != owner.Backend
                    || (snapshot.IsConnected && !snapshot.IsBackendAvailable))
                {
                    return false;
                }
                if (_current == snapshot) return true;
                ControllerCapabilitySnapshot previous = _current;
                _current = snapshot;
                args = new ControllerCapabilityChangedEventArgs(previous, snapshot);
                handlers = Changed;
            }

            RaiseChanged(this, handlers, args);
            return true;
        }

        internal void Release(ControllerCapabilityOwner owner)
        {
            EventHandler<ControllerCapabilityChangedEventArgs>? handlers = null;
            ControllerCapabilityChangedEventArgs? args = null;
            lock (_gate)
            {
                owner.MarkDisposedLocked();
                if (!ReferenceEquals(_owner, owner)) return;
                _owner = null;
                ControllerCapabilitySnapshot unavailable =
                    ControllerCapabilitySnapshot.Unavailable(owner.Backend);
                if (_current == unavailable) return;
                ControllerCapabilitySnapshot previous = _current;
                _current = unavailable;
                args = new ControllerCapabilityChangedEventArgs(previous, unavailable);
                handlers = Changed;
            }

            RaiseChanged(this, handlers, args);
        }

        private static void RaiseChanged(
            ControllerCapabilityStore sender,
            EventHandler<ControllerCapabilityChangedEventArgs>? handlers,
            ControllerCapabilityChangedEventArgs? args)
        {
            if (handlers == null || args == null) return;
            foreach (EventHandler<ControllerCapabilityChangedEventArgs> handler
                in handlers.GetInvocationList())
            {
                try
                {
                    handler(sender, args);
                }
                catch (Exception ex)
                {
                    // An optional settings observer must not break the native
                    // event pump that published the device transition.
                    Trace.WriteLine($"[input] capability observer failed: {ex.Message}");
                }
            }
        }
    }

    /// <summary>A process-wide read-only view used by launcher and settings UI.</summary>
    public static class ControllerCapabilities
    {
        public static ControllerCapabilityStore Shared { get; } = new();

        public static ControllerCapabilitySnapshot Current => Shared.Current;

        public static event EventHandler<ControllerCapabilityChangedEventArgs>? Changed
        {
            add => Shared.Changed += value;
            remove => Shared.Changed -= value;
        }

        internal static ControllerCapabilityOwner? TryAcquire(
            ControllerBackend backend, bool replaceCurrent)
            => Shared.TryAcquire(backend, replaceCurrent);
    }

    /// <summary>
    /// A platform-owned capability publication lease. It is intentionally
    /// internal: application code observes <see cref="ControllerCapabilities"/>
    /// rather than manufacturing snapshots itself.
    /// </summary>
    internal sealed class ControllerCapabilityOwner : IDisposable
    {
        private readonly ControllerCapabilityStore _store;
        private bool _disposed;

        internal ControllerCapabilityOwner(
            ControllerCapabilityStore store, ControllerBackend backend)
        {
            _store = store;
            Backend = backend;
        }

        internal ControllerBackend Backend { get; }
        internal bool IsDisposed => _disposed;
        internal bool IsCurrent => _store.IsCurrent(this);

        internal bool Publish(ControllerCapabilitySnapshot snapshot)
            => _store.Publish(this, snapshot);

        public void Dispose() => _store.Release(this);

        internal void MarkDisposedLocked() => _disposed = true;
    }
}
