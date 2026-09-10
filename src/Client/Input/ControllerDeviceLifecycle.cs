using System;

namespace MphRead.Mods.Input
{
    /// <summary>Result of an activity-owned controller device transition.</summary>
    internal enum ControllerDeviceTransitionKind
    {
        Ignored,
        Connected,
        Replaced,
        Changed,
        Disconnected
    }

    internal readonly record struct ControllerDeviceTransition(
        ControllerDeviceTransitionKind Kind, int DeviceId)
    {
        internal bool HasDevice => Kind is ControllerDeviceTransitionKind.Connected
            or ControllerDeviceTransitionKind.Replaced
            or ControllerDeviceTransitionKind.Changed;
    }

    /// <summary>
    /// Tracks the one Android input device that owns the shared gamepad state.
    ///
    /// Android's device listener can deliver callbacks after unregistering a
    /// listener, so the activity lifecycle is part of this state machine. The
    /// pure transition object keeps removal, replacement, and teardown
    /// deterministic and testable without loading Android assemblies.
    /// </summary>
    internal sealed class ControllerDeviceLifecycle
    {
        private readonly object _gate = new();
        private bool _active;
        private int? _activeDeviceId;

        internal bool IsActive
        {
            get
            {
                lock (_gate) return _active;
            }
        }

        internal int? ActiveDeviceId
        {
            get
            {
                lock (_gate) return _activeDeviceId;
            }
        }

        internal void Activate()
        {
            lock (_gate) _active = true;
        }

        /// <summary>
        /// End the activity's input-listener epoch and forget the active pad.
        /// A callback from a previous epoch returns <see cref="Ignored"/>.
        /// </summary>
        internal ControllerDeviceTransition Deactivate()
        {
            lock (_gate)
            {
                int? deviceId = _activeDeviceId;
                _active = false;
                _activeDeviceId = null;
                return deviceId is { } id
                    ? new ControllerDeviceTransition(
                        ControllerDeviceTransitionKind.Disconnected, id)
                    : default;
            }
        }

        /// <summary>
        /// Observe the first input event from a device. If another device
        /// starts producing input it becomes the active source, and callers
        /// must clear held buttons before applying that event.
        /// </summary>
        internal ControllerDeviceTransition ObserveInput(int deviceId)
        {
            lock (_gate)
            {
                if (!_active) return default;
                if (_activeDeviceId is null)
                {
                    _activeDeviceId = deviceId;
                    return new ControllerDeviceTransition(
                        ControllerDeviceTransitionKind.Connected, deviceId);
                }
                if (_activeDeviceId == deviceId)
                {
                    return new ControllerDeviceTransition(
                        ControllerDeviceTransitionKind.Changed, deviceId);
                }
                _activeDeviceId = deviceId;
                return new ControllerDeviceTransition(
                    ControllerDeviceTransitionKind.Replaced, deviceId);
            }
        }

        /// <summary>
        /// Claim a newly-added gamepad only while no active device exists.
        /// A second pad may be connected without displacing the current one;
        /// its first input event will be handled by <see cref="ObserveInput"/>.
        /// </summary>
        internal ControllerDeviceTransition ObserveAdded(int deviceId)
        {
            lock (_gate)
            {
                if (!_active) return default;
                if (_activeDeviceId == deviceId)
                {
                    return new ControllerDeviceTransition(
                        ControllerDeviceTransitionKind.Changed, deviceId);
                }
                if (_activeDeviceId is not null) return default;
                _activeDeviceId = deviceId;
                return new ControllerDeviceTransition(
                    ControllerDeviceTransitionKind.Connected, deviceId);
            }
        }

        internal ControllerDeviceTransition ObserveChanged(int deviceId)
        {
            lock (_gate)
            {
                if (!_active || _activeDeviceId != deviceId) return default;
                return new ControllerDeviceTransition(
                    ControllerDeviceTransitionKind.Changed, deviceId);
            }
        }

        /// <summary>
        /// Forget the active device while keeping the activity epoch alive.
        /// Focus loss uses this because the next input event may reconnect the
        /// same device without a new Activity instance.
        /// </summary>
        internal ControllerDeviceTransition Disconnect()
        {
            lock (_gate)
            {
                if (!_active || _activeDeviceId is not { } id) return default;
                _activeDeviceId = null;
                return new ControllerDeviceTransition(
                    ControllerDeviceTransitionKind.Disconnected, id);
            }
        }

        /// <summary>
        /// Clear only the device named by Android. A delayed removal callback
        /// for an older pad cannot clear a replacement that is already active.
        /// </summary>
        internal ControllerDeviceTransition ObserveRemoved(int deviceId)
        {
            lock (_gate)
            {
                if (!_active || _activeDeviceId != deviceId) return default;
                _activeDeviceId = null;
                return new ControllerDeviceTransition(
                    ControllerDeviceTransitionKind.Disconnected, deviceId);
            }
        }
    }

    /// <summary>
    /// Retains metadata learned from Android's input-device listener while
    /// ordinary key and motion events continue to identify only a device ID.
    /// A missing field never downgrades a value already reported for that ID.
    /// </summary>
    internal sealed class ControllerDeviceMetadataCache
    {
        private readonly object _gate = new();
        private int? _deviceId;
        private string? _deviceName;
        private bool? _hasAnalogTriggers;

        internal ControllerDeviceMetadata Remember(
            int deviceId, string? deviceName, bool? hasAnalogTriggers)
        {
            lock (_gate)
            {
                if (_deviceId != deviceId)
                {
                    _deviceName = null;
                    _hasAnalogTriggers = null;
                }
                _deviceId = deviceId;
                if (!string.IsNullOrWhiteSpace(deviceName))
                {
                    _deviceName = deviceName;
                }
                if (hasAnalogTriggers.HasValue)
                {
                    _hasAnalogTriggers = hasAnalogTriggers;
                }
                return new ControllerDeviceMetadata(
                    deviceId, _deviceName, _hasAnalogTriggers);
            }
        }

        internal bool TryGet(int deviceId, out ControllerDeviceMetadata metadata)
        {
            lock (_gate)
            {
                if (_deviceId != deviceId)
                {
                    metadata = default;
                    return false;
                }
                metadata = new ControllerDeviceMetadata(
                    deviceId, _deviceName, _hasAnalogTriggers);
                return true;
            }
        }

        internal void Clear()
        {
            lock (_gate)
            {
                _deviceId = null;
                _deviceName = null;
                _hasAnalogTriggers = null;
            }
        }
    }

    internal readonly record struct ControllerDeviceMetadata(
        int DeviceId, string? DeviceName, bool? HasAnalogTriggers);
}
