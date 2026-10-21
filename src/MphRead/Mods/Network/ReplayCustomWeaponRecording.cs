using System;
using System.IO;
using MphRead.Mods.CustomWeapons;

namespace MphRead.Mods.Network;

internal sealed partial class ReplayRecorder
{
    internal ReplayCustomWeaponContext? CustomWeaponContext { get; private set; }
    internal long AcceptedCustomWeaponFacts { get; private set; }
    internal bool CustomWeaponCaptureFailed { get; private set; }
    internal void FailCustomWeaponFacts() => CustomWeaponCaptureFailed = true;
    private ulong _customFactSequence, _customFactStateRevision;
    private ReplayCustomWeaponState? _customReplayState;
    private readonly CustomPickupItemState?[] _customRecordedPickups = new CustomPickupItemState?[64];
    internal void RetireCustomWeaponFacts()
    {
        CustomWeaponContext = null; _customFactSequence = _customFactStateRevision = 0;
        _customReplayState = null;
        Array.Clear(_customRecordedPickups);
    }
    private void ResetCustomWeaponFacts()
    { RetireCustomWeaponFacts(); CustomWeaponCaptureFailed = false; AcceptedCustomWeaponFacts = 0; }
    private bool CustomFactScope => !CustomWeaponCaptureFailed && CustomWeaponContext is { } context
        && context.Bootstrap.Start.MatchId == _matchId && context.Bootstrap.Start.AuthorityEpoch == _epoch;
    private bool HasCustomWeaponMatch => _match is { } match
        && (((MatchModifierFlags)MatchStatePacket.Read(match.Payload[1..]).RuleBits)
            & (MatchModifierFlags.RiftlineM7Available | MatchModifierFlags.PackagedWeaponAvailable)) != 0;
    private bool PublishCustom(ReplayCustomWeaponFact fact, uint recordingFrame)
    {
        if (CustomWeaponCaptureFailed) return false;
        try
        {
            if (_customReplayState == null || !_customReplayState.Accept(fact, recordingFrame))
                throw new InvalidDataException("Custom recorder state did not consume its next accepted fact.");
            PublishTransient(new(recordingFrame, fact.World, ReplayFactKind.CustomWeapon, fact.CopyBytes()));
            _customFactSequence = fact.Sequence; AcceptedCustomWeaponFacts++; return true;
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            // A failed observer may have seen a prefix. Close this capture;
            // never turn a replay sink failure into missing gameplay ACKs.
            CustomWeaponCaptureFailed = true; return false;
        }
    }
    internal bool AcceptCustomWeaponBaseline(ReplayCustomWeaponContext context, in CustomReplicaCapture capture,
        ReadOnlySpan<CustomReplicaSlot> slots, ReadOnlySpan<CustomReplicaFlight> flights, uint recordingFrame)
    {
        if (CustomWeaponCaptureFailed || !HasMatch || _matchId != context.Bootstrap.Start.MatchId
            || _epoch != context.Bootstrap.Start.AuthorityEpoch || _match is not { } match
            || (((MatchModifierFlags)MatchStatePacket.Read(match.Payload[1..]).RuleBits)
                & (MatchModifierFlags.RiftlineM7Available | MatchModifierFlags.PackagedWeaponAvailable))
                != (MatchModifierFlags.RiftlineM7Available | MatchModifierFlags.PackagedWeaponAvailable)
            || CustomWeaponContext?.Digest == context.Digest) return false;
        try
        {
            // Validate both values before publishing either half of this scope.
            var first = ReplayCustomWeaponFact.Bootstrap(context);
            var baseline = ReplayCustomWeaponFact.Baseline(context, 2, capture, slots, flights);
            RetireCustomWeaponFacts(); CustomWeaponContext = context; _customReplayState = new(context);
            return PublishCustom(first, recordingFrame) && PublishCustom(baseline, recordingFrame);
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException)
        { CustomWeaponCaptureFailed = true; return false; }
    }
    internal bool AcceptCustomWeaponState(ulong revision, uint world, ReadOnlySpan<CustomReplicaSlot> slots, uint recordingFrame)
    {
        if (!CustomFactScope || revision == 0 || revision <= _customFactStateRevision || _customFactSequence == ulong.MaxValue) return false;
        try
        {
            var fact = ReplayCustomWeaponFact.State(CustomWeaponContext!, _customFactSequence + 1, world, slots);
            if (!PublishCustom(fact, recordingFrame)) return false;
            _customFactStateRevision = revision; return true;
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException)
        { CustomWeaponCaptureFailed = true; return false; }
    }
    internal bool AcceptCustomWeaponEvent(in CustomReplicaEvent value, uint recordingFrame)
    {
        if (!CustomFactScope || _customFactSequence == ulong.MaxValue || value.Token == 0
            || (value.Kind == CustomReplicaKind.Launch ? value.World != value.Launch.World
                : value.Kind == CustomReplicaKind.Contact && value.World != value.Contact.Outcome.AuthorityFrame)) return false;
        try
        {
            var fact = value.Kind switch
            {
                CustomReplicaKind.Launch => ReplayCustomWeaponFact.Launch(CustomWeaponContext!, _customFactSequence + 1, value.Launch),
                CustomReplicaKind.Contact => ReplayCustomWeaponFact.Contact(CustomWeaponContext!, _customFactSequence + 1, value.Contact.Bootstrap, value.Contact.Outcome),
                _ => null
            };
            return fact != null && PublishCustom(fact, recordingFrame);
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException)
        { CustomWeaponCaptureFailed = true; return false; }
    }
    internal bool AcceptCustomWeaponPickup(int index, uint world, in CustomPickupItemState item, uint recordingFrame)
    {
        if (!CustomFactScope || (uint)index >= _customRecordedPickups.Length || _customFactSequence == ulong.MaxValue
            || _customRecordedPickups[index] == item) return false;
        try
        {
            var fact = ReplayCustomWeaponFact.Pickup(CustomWeaponContext!, _customFactSequence + 1, world, index, item);
            if (!PublishCustom(fact, recordingFrame)) return false;
            _customRecordedPickups[index] = item; return true;
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException)
        { CustomWeaponCaptureFailed = true; return false; }
    }
    private void AcceptCustomWeaponClock(uint world, uint recordingFrame)
    {
        if (!CustomFactScope || _customReplayState is not { HasBaseline: true } state || _customFactSequence == ulong.MaxValue
            || !NetLifecycleTracker.Newer(world, state.World)) return;
        try { PublishCustom(ReplayCustomWeaponFact.Clock(CustomWeaponContext!, _customFactSequence + 1, world), recordingFrame); }
        catch (Exception error) when (error is InvalidDataException or ArgumentException) { CustomWeaponCaptureFailed = true; }
    }
    // Immutable current values for later archive/world construction. The
    // original baseline is never relabelled as current state. No native/world
    // replay gate is opened and no mutable recorder owner is lent to callers.
    internal bool TryCaptureCustomWeaponState(out ReplayCustomWeaponDescriptor? descriptor, out byte[] checkpoint)
    {
        descriptor = null; checkpoint = Array.Empty<byte>();
        if (!CustomFactScope || _customReplayState is not { CompleteForRestore: true } state || state.Sequence != _customFactSequence) return false;
        descriptor = ReplayCustomWeaponDescriptor.ForInspection(CustomWeaponContext!); checkpoint = state.CaptureCheckpointBytes(); return true;
    }
    internal ReplayReplicaCheckpoint CaptureCustomLiveSeed(uint frame)
    {
        if (!TryCaptureCustomWeaponState(out var descriptor, out var current))
            throw new InvalidOperationException("Recording requires a complete accepted custom state.");
        var state = new ReplayReplicaState(); state.EnableCustomInspection(descriptor!);
        var records = new System.Collections.Generic.List<ReplayTimelineRecord>();
        try
        {
            SeedWorldCore(record => { record.Retain(); records.Add(record); });
            ReplayLiveWorld.SeedBootstrap(state, records, frame);
            state.InstallCustomLiveSeed(descriptor!, current, frame);
            return state.CaptureCheckpoint();
        }
        finally { foreach (var record in records) record.Release(); }
    }
}

public static partial class NetSession
{
    private static readonly CustomReplicaSlot[] _customReplaySlots = new CustomReplicaSlot[8];
    private static readonly CustomReplicaFlight[] _customReplayFlights = new CustomReplicaFlight[64];
    private static void CaptureCustomReplicaAccepted(CustomReplicaConsumer consumer, bool wasReady,
        ulong previousState, ulong previousEvents)
    {
        if (DemoPlayback.IsActive || !consumer.IsReady || _customReplicaWire is not { } wire
            || _weaponPreparation is not { Confirmed: true, Binding: not null, Error: null } package
            || !CurrentPackageKey(package.Key) || CurrentCustomPickupReplica() is not { } pickups) return;
        var recorder = ReplayCapture.Recorder;
        try
        {
            if (!wasReady)
            {
                var context = new ReplayCustomWeaponContext(new(wire.Bootstrap, package.Key.Configuration,
                    pickups.Layout.Digest, package.Binding.Manifest), wire.Definition, pickups.Layout);
                if (consumer.TryCopyBaseline(_customReplaySlots, _customReplayFlights, out var baseline)
                    && recorder.AcceptCustomWeaponBaseline(context, baseline, _customReplaySlots, _customReplayFlights, NetFrame))
                    CaptureCustomPickupBaseline(pickups);
                else recorder.FailCustomWeaponFacts();
            }
            // A recorder retired after baseline installation cannot reconstruct
            // current flights by relabelling the original baseline as current.
            if (recorder.CustomWeaponContext?.Bootstrap.RecipientBootstrap != wire.Bootstrap)
            { recorder.FailCustomWeaponFacts(); return; }
            if (consumer.StateRevision != previousState && consumer.TryCopyState(_customReplaySlots, out uint world))
                recorder.AcceptCustomWeaponState(consumer.StateRevision, world, _customReplaySlots, NetFrame);
            if (consumer.DeliveredEvents != previousEvents && consumer.LatestAcceptedEvent is { } fact)
                recorder.AcceptCustomWeaponEvent(fact, NetFrame);
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        { recorder.FailCustomWeaponFacts(); }
        finally { Array.Clear(_customReplaySlots); Array.Clear(_customReplayFlights); }
    }
    private static void CaptureCustomPickupBaseline(CustomPickupReplicaReceiver receiver)
    {
        for (int i = 0; i < receiver.Layout.Count; i++)
            if (receiver.TryAcceptedState(i, out var accepted))
                ReplayCapture.Recorder.AcceptCustomWeaponPickup(i, accepted.AuthorityFrame, accepted.Item, NetFrame);
    }
}
