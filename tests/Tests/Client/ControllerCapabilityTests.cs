using System;
using System.Collections.Generic;
using MphRead.Mods.Input;
using Xunit;

namespace MphRead.Tests;

public sealed class ControllerCapabilityTests
{
    [Fact]
    public void UnsupportedBackendAndDisconnectedDeviceRemainDistinct()
    {
        var store = new ControllerCapabilityStore();
        using ControllerCapabilityOwner owner = store.TryAcquire(
            ControllerBackend.Glfw, replaceCurrent: false)!;

        Assert.True(owner.Publish(ControllerCapabilitySnapshot.Unavailable(
            ControllerBackend.Glfw)));
        Assert.Equal(ControllerBackend.Glfw, store.Current.Backend);
        Assert.False(store.Current.IsBackendAvailable);
        Assert.False(store.Current.IsConnected);
        Assert.False(store.Current.IsAvailable);
        Assert.Null(store.Current.HasGyroscope);

        Assert.True(owner.Publish(ControllerCapabilitySnapshot.Disconnected(
            ControllerBackend.Glfw)));
        Assert.True(store.Current.IsBackendAvailable);
        Assert.False(store.Current.IsConnected);
        Assert.Null(store.Current.HasRumble);
    }

    [Fact]
    public void UnknownOptionalCapabilitiesAreNotInferredFromFamily()
    {
        var store = new ControllerCapabilityStore();
        using ControllerCapabilityOwner owner = store.TryAcquire(
            ControllerBackend.Sdl, replaceCurrent: false)!;

        var before = new ControllerCapabilitySnapshot(
            ControllerBackend.Sdl,
            IsBackendAvailable: true,
            IsConnected: true,
            Family: ControllerFamily.Xbox,
            HasGyroscope: null,
            HasRumble: null,
            HasAnalogTriggers: null,
            DeviceId: "gamepad:1",
            DeviceName: "Xbox-compatible pad");
        Assert.True(owner.Publish(before));
        Assert.Null(store.Current.HasGyroscope);
        Assert.Null(store.Current.HasRumble);
        Assert.Null(store.Current.HasAnalogTriggers);

        Assert.True(owner.Publish(ControllerCapabilitySnapshot.Connected(
            ControllerBackend.Sdl,
            "gamepad:2",
            "mapped pad",
            ControllerFamily.Xbox,
            hasGyroscope: false,
            hasRumble: false,
            hasAnalogTriggers: false)));
        Assert.False(store.Current.HasGyroscope);
        Assert.False(store.Current.HasRumble);
        Assert.False(store.Current.HasAnalogTriggers);
    }

    [Fact]
    public void DeviceReplacementRetiresOldOwnerAndRejectsLateCallback()
    {
        var store = new ControllerCapabilityStore();
        var changes = new List<ControllerCapabilityChangedEventArgs>();
        store.Changed += (_, args) => changes.Add(args);
        ControllerCapabilityOwner oldOwner = store.TryAcquire(
            ControllerBackend.Sdl, replaceCurrent: false)!;
        Assert.True(oldOwner.Publish(ControllerCapabilitySnapshot.Connected(
            ControllerBackend.Sdl, "gamepad:1", "first")));

        ControllerCapabilityOwner replacement = store.TryAcquire(
            ControllerBackend.Android, replaceCurrent: true)!;
        Assert.False(oldOwner.Publish(ControllerCapabilitySnapshot.Connected(
            ControllerBackend.Sdl, "gamepad:stale", "stale")));
        Assert.True(replacement.Publish(ControllerCapabilitySnapshot.Connected(
            ControllerBackend.Android, "device:2", "second")));

        Assert.Equal(ControllerBackend.Android, store.Current.Backend);
        Assert.Equal("device:2", store.Current.DeviceId);
        Assert.Equal(3, changes.Count);
        Assert.Equal(ControllerCapabilitySnapshot.Unavailable(ControllerBackend.Sdl),
            changes[1].Current);
        oldOwner.Dispose();
        replacement.Dispose();
    }

    [Fact]
    public void IdenticalSnapshotsDoNotRaiseRepeatedEvents()
    {
        var store = new ControllerCapabilityStore();
        using ControllerCapabilityOwner owner = store.TryAcquire(
            ControllerBackend.Sdl, replaceCurrent: false)!;
        var changes = new List<ControllerCapabilityChangedEventArgs>();
        store.Changed += (_, args) => changes.Add(args);

        ControllerCapabilitySnapshot snapshot = ControllerCapabilitySnapshot.Connected(
            ControllerBackend.Sdl, "gamepad:1", "pad", ControllerFamily.PlayStation,
            hasGyroscope: false, hasRumble: null, hasAnalogTriggers: true);
        Assert.True(owner.Publish(snapshot));
        Assert.True(owner.Publish(snapshot));
        Assert.Single(changes);
        Assert.Equal(ControllerCapabilitySnapshot.Unavailable(),
            changes[0].Previous);
        Assert.Equal(snapshot, changes[0].Current);

        Assert.True(owner.Publish(snapshot with { DeviceName = "pad (renamed)" }));
        Assert.Equal(2, changes.Count);
    }

    [Fact]
    public void DisposePublishesUnavailableAndPreventsRevival()
    {
        var store = new ControllerCapabilityStore();
        ControllerCapabilityOwner owner = store.TryAcquire(
            ControllerBackend.Sdl, replaceCurrent: false)!;
        Assert.True(owner.Publish(ControllerCapabilitySnapshot.Connected(
            ControllerBackend.Sdl, "gamepad:1", "pad")));

        int events = 0;
        store.Changed += (_, _) => events++;
        owner.Dispose();

        Assert.Equal(ControllerCapabilitySnapshot.Unavailable(ControllerBackend.Sdl),
            store.Current);
        Assert.Equal(1, events);
        Assert.False(owner.Publish(ControllerCapabilitySnapshot.Connected(
            ControllerBackend.Sdl, "gamepad:late", "late")));
        Assert.Equal(1, events);
    }

    [Fact]
    public void AndroidDeviceLifecycleClearsRemovedPadBeforeReplacement()
    {
        var lifecycle = new ControllerDeviceLifecycle();
        lifecycle.Activate();

        Assert.Equal(ControllerDeviceTransitionKind.Connected,
            lifecycle.ObserveAdded(11).Kind);
        Assert.Equal(ControllerDeviceTransitionKind.Changed,
            lifecycle.ObserveInput(11).Kind);
        Assert.Equal(ControllerDeviceTransitionKind.Ignored,
            lifecycle.ObserveRemoved(12).Kind);
        Assert.Equal(ControllerDeviceTransitionKind.Disconnected,
            lifecycle.ObserveRemoved(11).Kind);

        Assert.Equal(ControllerDeviceTransitionKind.Connected,
            lifecycle.ObserveAdded(22).Kind);
        Assert.Equal(ControllerDeviceTransitionKind.Ignored,
            lifecycle.ObserveRemoved(11).Kind);
        Assert.Equal(22, lifecycle.ActiveDeviceId);
    }

    [Fact]
    public void AndroidDeviceLifecycleRejectsLateCallbacksAfterTeardown()
    {
        var lifecycle = new ControllerDeviceLifecycle();
        lifecycle.Activate();
        Assert.Equal(ControllerDeviceTransitionKind.Connected,
            lifecycle.ObserveInput(7).Kind);

        Assert.Equal(ControllerDeviceTransitionKind.Disconnected,
            lifecycle.Deactivate().Kind);
        Assert.False(lifecycle.IsActive);
        Assert.Null(lifecycle.ActiveDeviceId);
        Assert.Equal(ControllerDeviceTransitionKind.Ignored,
            lifecycle.ObserveRemoved(7).Kind);
        Assert.Equal(ControllerDeviceTransitionKind.Ignored,
            lifecycle.ObserveInput(8).Kind);

        lifecycle.Activate();
        Assert.Equal(ControllerDeviceTransitionKind.Connected,
            lifecycle.ObserveInput(8).Kind);
    }

    [Fact]
    public void AndroidOrdinaryInputRetainsAuthoritativeDeviceMetadata()
    {
        var metadata = new ControllerDeviceMetadataCache();
        ControllerDeviceMetadata reported = metadata.Remember(
            11, "Pro Controller", hasAnalogTriggers: true);

        Assert.Equal("Pro Controller", reported.DeviceName);
        Assert.True(reported.HasAnalogTriggers);

        // Key and motion callbacks carry only the device ID. They must not
        // replace listener metadata with the generic/null fallback.
        metadata.Remember(11, deviceName: null, hasAnalogTriggers: null);
        Assert.True(metadata.TryGet(11, out ControllerDeviceMetadata fromKey));
        Assert.Equal("Pro Controller", fromKey.DeviceName);
        Assert.True(fromKey.HasAnalogTriggers);
        Assert.True(metadata.TryGet(11, out ControllerDeviceMetadata fromMotion));
        Assert.Equal("Pro Controller", fromMotion.DeviceName);
        Assert.True(fromMotion.HasAnalogTriggers);

        metadata.Clear();
        Assert.False(metadata.TryGet(11, out _));
    }
}
