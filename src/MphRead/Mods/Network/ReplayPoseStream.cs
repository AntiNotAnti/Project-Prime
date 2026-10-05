using System;
using System.Collections.Generic;
using System.IO;
using MphRead.Entities;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

/// <summary>A bounded presentation cursor over accepted snapshots and intents. It keeps
/// a short ordinary pose lookahead plus enough future intent history to recover repeated
/// FireEvents onto their authored frame, without advancing simulation, sockets or RNG.
/// No smoothing decision depends on arrival wall time or monitor refresh.</summary>
internal sealed class ReplayPoseStream : IDisposable
{
    // First-person replay needs enough snapshot history to reproduce the exact
    // server world named by a shooter's ACK, not merely the few frames needed
    // for ordinary high-refresh interpolation.
    private const uint PresentationHistoryFrames = NetUnlagged.HistoryFrames + 12;
    private const uint FireLookaheadFrames = NetFireEvents.RetentionFrames + 6;
    private const int MaximumPoseSamples = NetUnlagged.HistoryFrames + 32;
    private const int MaximumIntentSamples = 96;
    private const uint MaximumIntentAge = 30;

    private readonly record struct PoseSample(uint RecordingFrame, uint ServerTick, PlayerState State);
    private readonly record struct IntentSample(uint RecordingFrame, IntentPacket Intent);
    private readonly record struct FireLife(int Slot, ushort Generation, ushort Life);
    private readonly record struct ScheduledFire(uint RecordingFrame, int Slot,
        ushort Generation, ushort Life, FireEvent Event, IntentPacket Carrier);
    private sealed class FireIndex
    {
        internal ushort Generation, Life;
        internal uint LastShotId;
        internal bool Seen;
    }

    private readonly PassiveReplayScene _world;
    private readonly string? _path;
    private readonly ReplayTimelineClip? _clip;
    private readonly ReplayReplicaState _decoder = new();
    private readonly List<PoseSample>[] _poses = new List<PoseSample>[8];
    private readonly List<IntentSample>[] _intents = new List<IntentSample>[8];
    private readonly FireIndex[] _fireIndex = CreateFireIndex();
    private readonly HashSet<FireLife> _fireCapable = new();
    private readonly Dictionary<uint, List<ScheduledFire>> _fires = new();
    private readonly List<uint> _firePrune = new();
    private readonly ScheduledFire?[] _activeFire = new ScheduledFire?[8];
    private readonly bool[] _fireConsumed = new bool[8];
    private DemoReader? _reader;
    private DemoRecord? _pending;
    private int _index;
    private uint? _advanced, _firePrepared;
    private bool _initialized, _failed, _supportsFireEvents;
    internal string? LastError { get; private set; }
    internal ReplayPoseStream(PassiveReplayScene world, string path) : this(world) { _path = path; }
    internal ReplayPoseStream(PassiveReplayScene world, ReplayTimelineClip clip) : this(world)
    { _clip = clip; _supportsFireEvents = true; }
    private ReplayPoseStream(PassiveReplayScene world)
    {
        _world = world;
        for (int i = 0; i < 8; i++)
        {
            _poses[i] = new(MaximumPoseSamples);
            _intents[i] = new(MaximumIntentSamples);
        }
    }

    private static FireIndex[] CreateFireIndex()
    {
        var result = new FireIndex[8];
        for (int i = 0; i < result.Length; i++) result[i] = new();
        return result;
    }

    // Presentation capture can prepare the cursor once per simulation frame so
    // HUD sampling does not perform file decoding or allocate during drawing.
    internal bool Prepare()
    {
        if (_failed) return false;
        try { Advance(); return true; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
        { LastError = ex.Message; _failed = true; return false; }
    }

    internal bool Sample(int slot, float alpha, out Vector3 position, out Vector3 facing)
        => SampleAt(slot, Math.Max(0, _world.Session.RecordingFrame - 1d + alpha),
            out position, out facing);

    private double PresentedRecordingFrame(float alpha)
        => double.IsFinite(_world.Scene.ReplayPresentationFrame)
            ? _world.Session.RecordingPresentationFrame(_world.Scene.ReplayPresentationFrame)
            : Math.Max(0, _world.Session.RecordingFrame - 1d + alpha);

    internal bool SamplePresented(int slot, float alpha, out Vector3 position, out Vector3 facing)
        => SampleAt(slot, PresentedRecordingFrame(alpha), out position, out facing);

    /// <summary>
    /// Sample a player for the currently watched replay POV. In first-person,
    /// everybody except the watched shooter is drawn from the server frame named
    /// by that shooter's accepted ACK. The replay simulation itself is never
    /// rewound: this is presentation-only and therefore cannot change damage,
    /// projectiles, RNG, seeks or checkpoint state.
    /// </summary>
    internal bool SamplePresentedForViewer(int slot, int viewerSlot, bool shooterView,
        float alpha, out Vector3 position, out Vector3 facing)
    {
        double recordingFrame = PresentedRecordingFrame(alpha);
        if (shooterView && slot != viewerSlot
            && TryPerceivedServerFrame(viewerSlot, recordingFrame, out double serverFrame)
            && SampleServerAt(slot, serverFrame, out position, out facing))
        {
            return true;
        }
        return SampleAt(slot, recordingFrame, out position, out facing);
    }

    internal static double AcknowledgedServerFrame(in IntentPacket intent)
        => intent.AckFrame == 0 ? double.NaN : intent.AckFrame + intent.AckSubFrame / 256d;

    internal static uint FireSourceRecordingFrame(uint carrierRecordingFrame,
        uint carrierSourceFrame, uint fireSourceFrame)
    {
        uint age = unchecked(carrierSourceFrame - fireSourceFrame);
        if (age > NetFireEvents.RetentionFrames) return carrierRecordingFrame;
        return carrierRecordingFrame >= age ? carrierRecordingFrame - age : 0;
    }

    internal bool UsesFireEvents(PlayerEntity player)
    {
        if ((uint)player.SlotIndex >= 8 || !Prepare() || !_supportsFireEvents) return false;
        if (!_world.State.TryGetPlayer(player.SlotIndex, out var state)) return false;
        return _fireCapable.Contains(new(player.SlotIndex, state.SlotGeneration, state.LifeId));
    }

    internal bool HasPendingFire(PlayerEntity player)
    {
        PrepareFireFrame();
        int slot = player.SlotIndex;
        return UsesFireEvents(player) && (uint)slot < 8
            && _activeFire[slot] is { Event.Kind: not FireEventKind.TurretFire }
            && !_fireConsumed[slot];
    }

    internal uint ActiveFireShotId(PlayerEntity player)
    {
        PrepareFireFrame();
        int slot = player.SlotIndex;
        return (uint)slot < 8 && _activeFire[slot] is { } active
            ? active.Event.ShotId : 0;
    }

    internal bool TryActiveFire(PlayerEntity player, out FireEvent fire)
    {
        PrepareFireFrame();
        int slot = player.SlotIndex;
        if ((uint)slot < 8 && UsesFireEvents(player) && _activeFire[slot] is { } active)
        {
            fire = active.Event;
            return true;
        }
        fire = default;
        return false;
    }

    internal void BeginFire(PlayerEntity player, bool turret)
    {
        PrepareFireFrame();
        int slot = player.SlotIndex;
        if ((uint)slot >= 8 || !UsesFireEvents(player) || _activeFire[slot] is not { } active) return;
        if ((active.Event.Kind == FireEventKind.TurretFire) != turret || _fireConsumed[slot]) return;
        _fireConsumed[slot] = true;
    }

    internal void ApplyFireEvent(PlayerEntity player)
    {
        PrepareFireFrame();
        int slot = player.SlotIndex;
        if ((uint)slot >= 8 || !UsesFireEvents(player) || _activeFire[slot] is not { } active
            || active.Event.Kind == FireEventKind.TurretFire)
        {
            return;
        }

        FireEvent fire = active.Event;
        IntentPacket carrier = active.Carrier;
        player.ModSetWeapon((BeamType)fire.Weapon);
        player.ModSetAmmo(carrier.AmmoUa, carrier.AmmoMissiles);
        player.EquipInfo.ChargeLevel = fire.Charge;
        if (fire.Kind == FireEventKind.ContinuousTick)
        {
            player.ModContinuousFireTick = fire.ContinuousPhase;
            if (carrier.Target.IsSupplied) player.ModContinuousNetworkTarget = carrier.Target;
        }
        else if (fire.Weapon == (byte)BeamType.VoltDriver && carrier.Target.IsSupplied)
        {
            player.ModSetPendingHomingTarget(carrier.Target);
        }

        // Keep held input from the ordinary intent for charge/animation state,
        // but the edge that can actually spawn this shot comes from FireEvent.
        bool release = fire.Kind == FireEventKind.ReleaseFire;
        player.Controls.Shoot.IsReleased = release;
        player.Controls.Shoot.IsPressed = fire.Kind == FireEventKind.PressFire;
        if (release) player.Controls.Shoot.IsDown = false;
        else if (fire.Kind is FireEventKind.PressFire or FireEventKind.AutomaticFire
            or FireEventKind.ContinuousTick) player.Controls.Shoot.IsDown = true;
    }

    private void PrepareFireFrame()
    {
        uint frame = _world.Session.RecordingFrame;
        if (_firePrepared == frame) return;
        Prepare();
        _firePrepared = frame;
        Array.Clear(_activeFire);
        Array.Clear(_fireConsumed);
        if (!_supportsFireEvents || !_fires.TryGetValue(frame, out var fires)) return;

        foreach (var scheduled in fires)
        {
            int slot = scheduled.Slot;
            if ((uint)slot >= 8 || _activeFire[slot].HasValue
                || !_world.State.TryGetPlayer(slot, out var state)
                || state.SlotGeneration != scheduled.Generation
                || state.LifeId != scheduled.Life)
            {
                continue;
            }
            _activeFire[slot] = scheduled;
        }
    }

    private bool TryPerceivedServerFrame(int viewerSlot, double recordingFrame, out double serverFrame)
    {
        serverFrame = double.NaN;
        if ((uint)viewerSlot >= 8 || !Prepare()) return false;
        var samples = _intents[viewerSlot];
        if (samples.Count == 0 || samples[0].RecordingFrame > recordingFrame) return false;

        int left = 0;
        while (left + 1 < samples.Count && samples[left + 1].RecordingFrame <= recordingFrame) left++;
        IntentSample sample = samples[left];
        if (recordingFrame - sample.RecordingFrame > MaximumIntentAge) return false;

        IntentPacket intent = sample.Intent;
        if (intent.AckFrame == 0
            || (intent.Buttons & IntentButtons.InPlayState) != IntentButtons.InPlayState
            || !_world.State.TryGetPlayer(viewerSlot, out var current)
            || current.SlotGeneration != intent.SlotGeneration
            || current.LifeId != intent.LifeId)
        {
            return false;
        }

        serverFrame = AcknowledgedServerFrame(intent);
        if (!double.IsFinite(serverFrame)) return false;

        // Intents are not guaranteed one per simulation frame. Use the existing
        // replay lookahead to advance the perceived-world clock smoothly between
        // two accepted ACKs, but never interpolate across a lifecycle change,
        // a long delivery gap or a discontinuous server clock.
        if (left + 1 < samples.Count)
        {
            IntentSample next = samples[left + 1];
            IntentPacket future = next.Intent;
            uint recordingGap = next.RecordingFrame - sample.RecordingFrame;
            double futureServerFrame = AcknowledgedServerFrame(future);
            double serverGap = futureServerFrame - serverFrame;
            if (recordingGap is > 0 and <= 12
                && future.SlotGeneration == intent.SlotGeneration
                && future.LifeId == intent.LifeId
                && (future.Buttons & IntentButtons.InPlayState) == IntentButtons.InPlayState
                && double.IsFinite(futureServerFrame)
                && serverGap >= 0 && serverGap <= 12)
            {
                double t = Math.Clamp(
                    (recordingFrame - sample.RecordingFrame) / recordingGap, 0, 1);
                serverFrame += serverGap * t;
            }
        }
        return true;
    }

    internal bool SampleAt(int slot, double frame, out Vector3 position, out Vector3 facing)
    {
        position = facing = default;
        if ((uint)slot >= 8 || !Prepare()) return false;
        frame = Math.Clamp(frame, 0, _world.Session.RecordingPresentationFrame(_world.Session.LastFrame));
        var samples = _poses[slot];
        if (samples.Count == 0 || samples[0].RecordingFrame > frame) return false;

        int left = 0;
        while (left + 1 < samples.Count && samples[left + 1].RecordingFrame <= frame) left++;
        var a = samples[left];
        PlayerState value = a.State;
        position = value.Position; facing = value.Facing;
        if (left + 1 < samples.Count)
        {
            var b = samples[left + 1];
            if (b.RecordingFrame > a.RecordingFrame
                && b.RecordingFrame - a.RecordingFrame <= 12
                && CanBlend(a.State, b.State))
            {
                float t = (float)Math.Clamp(
                    (frame - a.RecordingFrame) / (b.RecordingFrame - a.RecordingFrame), 0, 1);
                position = Vector3.Lerp(a.State.Position, b.State.Position, t);
                var rotation = Quaternion.Slerp(ReplayCameraTrack.FacingRotation(a.State.Facing),
                    ReplayCameraTrack.FacingRotation(b.State.Facing), t);
                facing = Vector3.Transform(-Vector3.UnitZ, rotation);
            }
        }
        return FinishSample(slot, value, ref position, ref facing);
    }

    private bool SampleServerAt(int slot, double serverFrame, out Vector3 position, out Vector3 facing)
    {
        position = facing = default;
        if ((uint)slot >= 8 || !Prepare() || !double.IsFinite(serverFrame)) return false;
        var samples = _poses[slot];
        if (samples.Count == 0 || samples[0].ServerTick > serverFrame) return false;

        int left = 0;
        while (left + 1 < samples.Count && samples[left + 1].ServerTick <= serverFrame) left++;
        var a = samples[left];
        PlayerState value = a.State;
        position = value.Position; facing = value.Facing;
        if (left + 1 < samples.Count)
        {
            var b = samples[left + 1];
            if (b.ServerTick > a.ServerTick
                && b.ServerTick - a.ServerTick <= 12
                && CanBlend(a.State, b.State))
            {
                float t = (float)Math.Clamp(
                    (serverFrame - a.ServerTick) / (b.ServerTick - a.ServerTick), 0, 1);
                position = Vector3.Lerp(a.State.Position, b.State.Position, t);
                var rotation = Quaternion.Slerp(ReplayCameraTrack.FacingRotation(a.State.Facing),
                    ReplayCameraTrack.FacingRotation(b.State.Facing), t);
                facing = Vector3.Transform(-Vector3.UnitZ, rotation);
            }
        }
        return FinishSample(slot, value, ref position, ref facing);
    }

    private bool FinishSample(int slot, PlayerState value, ref Vector3 position, ref Vector3 facing)
    {
        // Draw history must never borrow a new occupant, life, death or form.
        if (!_world.State.TryGetPlayer(slot, out var current) || !SameLife(value, current)) return false;
        var actor = _world.Scene.Players.Items[slot];
        position = _world.Scene.PlayerReplication.InFormFor(actor, position,
            (value.Flags & PlayerState.FlagAltForm) != 0);
        return true;
    }

    private static bool SameLife(PlayerState a, PlayerState b) => a.SlotGeneration == b.SlotGeneration
        && a.LifeId == b.LifeId && (a.Health > 0) == (b.Health > 0)
        && (a.Flags & (PlayerState.FlagActive | PlayerState.FlagSpawned | PlayerState.FlagAltForm))
            == (b.Flags & (PlayerState.FlagActive | PlayerState.FlagSpawned | PlayerState.FlagAltForm));

    internal static bool CanBlend(PlayerState a, PlayerState b) => SameLife(a, b)
        && (a.Position - b.Position).LengthSquared <= 16;

    private void Advance()
    {
        uint frame = _world.Session.RecordingFrame;
        if (_advanced == frame) return;
        _advanced = frame;
        uint origin = _world.Session.Metadata?.OriginRecordingFrame ?? 0;
        if (!_initialized)
        {
            _initialized = true;
            _decoder.RestoreCheckpoint(_world.State.CaptureCheckpoint()); _decoder.Rewind();
            uint from = frame > PresentationHistoryFrames ? frame - PresentationHistoryFrames : 0;
            if (_path != null)
            {
                _reader = DemoReader.Open(_path, out var result)
                    ?? throw new InvalidDataException(result.ToString());
                _supportsFireEvents = _reader.ProtocolVersion >= 30;
                _pending = from > origin ? _reader.SeekAfter(from - origin - 1) : _reader.ReadNext();
            }
            else if (_clip != null)
            {
                foreach (var baseline in _clip.RestorePoint.Records)
                    if (baseline.Kind is ReplayFactKind.Snapshot or ReplayFactKind.Intent)
                        Accept(baseline.RecordingFrame, baseline.Payload);
                while (_index < _clip.Records.Count && _clip.Records[_index].RecordingFrame < from) _index++;
            }
        }

        int read = 0;
        while (true)
        {
            uint next;
            if (_reader != null && _pending is { } packet)
            {
                next = checked(origin + packet.Frame);
                if (next > (ulong)frame + FireLookaheadFrames) break;
                AcceptRecorded(next, packet.Data); _pending = _reader.ReadNext();
            }
            else if (_clip != null && _index < _clip.Records.Count)
            {
                var record = _clip.Records[_index]; next = record.RecordingFrame;
                if (next > (ulong)frame + FireLookaheadFrames) break;
                if (record.Kind is ReplayFactKind.Match or ReplayFactKind.Roster
                    or ReplayFactKind.Snapshot or ReplayFactKind.Intent)
                {
                    Accept(next, record.Payload);
                }
                _index++;
            }
            else break;
            if (++read > 8192)
                throw new InvalidDataException("Replay presentation lookahead exceeds its record bound.");
        }

        for (int slot = 0; slot < 8; slot++)
        {
            var poses = _poses[slot];
            if ((poses.Count == 0 || poses[0].RecordingFrame > frame)
                && _world.State.TryGetPlayer(slot, out var state))
            {
                poses.Insert(0, new(frame, _world.State.ServerTick, state));
            }
            while (poses.Count > 2
                && ((ulong)poses[1].RecordingFrame + PresentationHistoryFrames < frame
                    || poses.Count > MaximumPoseSamples))
            {
                poses.RemoveAt(0);
            }

            var intents = _intents[slot];
            if (intents.Count == 0 && _world.State.TryGetIntent(slot, out var intent))
                intents.Add(new(frame, intent));
            while (intents.Count > 1
                && ((ulong)intents[1].RecordingFrame + PresentationHistoryFrames < frame
                    || intents.Count > MaximumIntentSamples))
            {
                intents.RemoveAt(0);
            }
        }

        // Keep source-frame scheduling bounded on long recordings. The active
        // frame is never pruned, and retained history is deeper than the maximum
        // FireEvent recovery age, so seeks/rebuilds still reconstruct identically.
        if (frame % 120 == 0 && _fires.Count > 0)
        {
            _firePrune.Clear();
            foreach (uint scheduled in _fires.Keys)
                if ((ulong)scheduled + PresentationHistoryFrames < frame)
                    _firePrune.Add(scheduled);
            foreach (uint scheduled in _firePrune) _fires.Remove(scheduled);
        }
    }

    private void AcceptRecorded(uint frame, ReadOnlySpan<byte> packet)
    {
        try
        {
            ReadOnlySpan<byte> converted = ReplayIdentityCompatibility.Convert(packet, _reader!.ProtocolVersion);
            if (!converted.IsEmpty) Accept(frame, converted);
        }
        catch (InvalidDataException ex) when (ReplayIdentityCompatibility.BestEffort(_reader!.ProtocolVersion))
        {
            LastError = $"Legacy presentation record skipped at {frame}: {ex.Message}";
        }
    }

    private void Accept(uint frame, ReadOnlySpan<byte> packet)
    {
        if (packet.Length == 0 || packet[0] is 253 or 254 or 255) return;
        long accepted = _decoder.AcceptedPackets;
        _decoder.Accept(packet, frame);
        if (accepted == _decoder.AcceptedPackets) return;

        PacketType type = (PacketType)packet[0];
        if (type == PacketType.SlotIntent)
        {
            if (packet.Length < 2 + IntentPacket.Size) return;
            int slot = packet[1];
            if ((uint)slot >= 8) return;
            var list = _intents[slot];
            var intent = IntentPacket.Read(packet[2..]);
            if (list.Count > 0 && list[^1].RecordingFrame == frame)
                list[^1] = new(frame, intent);
            else
                list.Add(new(frame, intent));
            if (list.Count > MaximumIntentSamples) list.RemoveAt(0);
            IndexFireEvents(frame, slot, intent);
            return;
        }

        if (type != PacketType.Snapshot) return;
        uint serverTick = SnapshotHeader.Read(packet[1..]).Frame;
        for (int i = 0; i < 8; i++)
        {
            if (!_decoder.TryGetPlayer(i, out var state)) continue;
            var list = _poses[i];
            var sample = new PoseSample(frame, serverTick, state);
            if (list.Count > 0 && list[^1].RecordingFrame == frame) list[^1] = sample;
            else list.Add(sample);
            if (list.Count > MaximumPoseSamples) list.RemoveAt(0);
        }
    }

    private void IndexFireEvents(uint carrierRecordingFrame, int slot, in IntentPacket intent)
    {
        if (!_supportsFireEvents || !intent.HasFireEvents || !NetFireEvents.Validate(intent)) return;
        var life = new FireLife(slot, intent.SlotGeneration, intent.LifeId);
        _fireCapable.Add(life);

        FireIndex index = _fireIndex[slot];
        if (index.Generation != intent.SlotGeneration || index.Life != intent.LifeId)
        {
            index.Generation = intent.SlotGeneration;
            index.Life = intent.LifeId;
            index.LastShotId = 0;
            index.Seen = false;
        }

        for (int i = 0; i < intent.FireEventCount; i++)
        {
            FireEvent fire = intent.FireEvents[i];
            if (index.Seen && !NetLifecycleTracker.Newer(fire.ShotId, index.LastShotId)) continue;
            index.Seen = true;
            index.LastShotId = fire.ShotId;
            uint scheduledFrame = FireSourceRecordingFrame(
                carrierRecordingFrame, intent.Frame, fire.SourceFrame);
            if (!_fires.TryGetValue(scheduledFrame, out var list))
            {
                list = new();
                _fires.Add(scheduledFrame, list);
            }
            list.Add(new(scheduledFrame, slot, intent.SlotGeneration, intent.LifeId, fire, intent));
        }
    }

    public void Dispose() { _reader?.Dispose(); _reader = null; }
}
