using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network
{
    public enum SnapshotPresentationMode : byte
    {
        Startup,
        Interpolated,
        HistoryHold,
        Extrapolated,
        ExtrapolationHold
    }

    /// <summary>A resolved picture time, invalidated when its history resets.</summary>
    public readonly record struct SnapshotPresentation(double Tick, uint MatchId,
        uint Generation, SnapshotPresentationMode Mode);

    /// <summary>
    /// A snapshot state sampled at the delayed presentation time. <see cref="State"/>
    /// retains the newest authoritative gameplay fields while <see cref="VisualSpeed"/>
    /// describes only the trajectory represented by the presented pose.
    /// </summary>
    public readonly record struct SnapshotPlayerPresentation(
        SnapshotPlayer State, Vector3 VisualSpeed);

    /// <summary>
    /// Single-writer presentation history. Add validated snapshots on receipt, then
    /// sample remote transforms using NetClock's continuous server tick estimate.
    /// Gameplay fields always come from the newest snapshot. No per-frame allocation.
    /// </summary>
    public sealed class SnapshotInterpolation
    {
        public const int Capacity = 32;
        public const double DefaultDelayTicks = NetworkTiming.InterpolationDelayTicks;
        public const double DefaultExtrapolationTicks = 3; // 50 ms
        private const int Slots = 8;
        private const double SequenceSpace = 4294967296.0;
        private const SnapshotPlayerFlags ContinuityFlags = SnapshotPlayerFlags.Active
            | SnapshotPlayerFlags.Spawned | SnapshotPlayerFlags.AltForm
            | SnapshotPlayerFlags.Morphing | SnapshotPlayerFlags.Unmorphing
            | SnapshotPlayerFlags.Spectating;

        private readonly SnapshotPlayer[] _players = new SnapshotPlayer[Capacity * Slots];
        private readonly uint[] _epochs = new uint[Capacity * Slots];
        private readonly uint[] _slotEpochs = new uint[Slots];
        private readonly double[] _ticks = new double[Capacity];
        private readonly long[] _receivedAt = new long[Capacity];
        private readonly byte[] _masks = new byte[Capacity];
        private int _newest = -1;
        private uint _lastTick;
        private uint _lastSequence;
        private uint _matchId;
        private uint _generation;
        private bool _hasPresented;
        private double _presentedTick;

        public int Count { get; private set; }
        public double DelayTicks { get; }
        public double MaxExtrapolationTicks { get; }
        public long LatestReceivedAt => Count == 0 ? 0 : _receivedAt[_newest];
        public long InterpolatedSamples { get; private set; }
        public long ExtrapolatedSamples { get; private set; }
        public long UnderrunSamples { get; private set; }
        public long HeldSamples { get; private set; }
        public double MaximumExtrapolationTicks { get; private set; }
        public bool HasPresented => _hasPresented;

        public SnapshotInterpolation(double delayTicks = DefaultDelayTicks,
            double maxExtrapolationTicks = DefaultExtrapolationTicks)
        {
            if (!Double.IsFinite(delayTicks) || delayTicks < 0 || delayTicks >= Capacity)
                throw new ArgumentOutOfRangeException(nameof(delayTicks));
            if (!Double.IsFinite(maxExtrapolationTicks) || maxExtrapolationTicks < 0
                || maxExtrapolationTicks > DefaultExtrapolationTicks)
                throw new ArgumentOutOfRangeException(nameof(maxExtrapolationTicks));
            DelayTicks = delayTicks;
            MaxExtrapolationTicks = maxExtrapolationTicks;
        }

        public void Reset()
        {
            Count = 0;
            _newest = -1;
            InterpolatedSamples = ExtrapolatedSamples = UnderrunSamples = HeldSamples = 0;
            MaximumExtrapolationTicks = 0;
            _generation++;
            _hasPresented = false;
            _presentedTick = 0;
            Array.Clear(_slotEpochs);
        }

        /// <summary>Returns false for stale/duplicate snapshots or invalid slot identities.</summary>
        public bool Add(in SnapshotPacket packet, ReadOnlySpan<SnapshotPlayer> players, long receivedAt)
        {
            if (players.Length > Slots || receivedAt < 0) return false;
            int mask = 0;
            foreach (ref readonly SnapshotPlayer player in players)
            {
                if (player.Slot >= Slots || player.ConnectionId == 0 || (mask & (1 << player.Slot)) != 0)
                    return false;
                mask |= 1 << player.Slot;
            }
            if (Count > 0 && packet.MatchId == _matchId
                && (!Sequence32.IsNewer(packet.Sequence, _lastSequence)
                    || !Sequence32.IsNewer(packet.ServerTick, _lastTick))) return false;
            if (Count > 0 && packet.MatchId != _matchId) Reset();

            double tick = Count == 0 ? packet.ServerTick
                : _ticks[_newest] + unchecked(packet.ServerTick - _lastTick);
            int next = (_newest + 1) % Capacity;
            foreach (ref readonly SnapshotPlayer player in players)
            {
                int slot = player.Slot;
                if (Count == 0 || (_masks[_newest] & (1 << slot)) == 0
                    || !Continuous(_players[_newest * Slots + slot], player))
                    _slotEpochs[slot]++;
                _players[next * Slots + slot] = player;
                _epochs[next * Slots + slot] = _slotEpochs[slot];
            }
            _ticks[next] = tick;
            _receivedAt[next] = receivedAt;
            _masks[next] = (byte)mask;
            _newest = next;
            _lastTick = packet.ServerTick;
            _lastSequence = packet.Sequence;
            _matchId = packet.MatchId;
            Count = Math.Min(Count + 1, Capacity);
            return true;
        }

        /// <summary>
        /// Resolve once per picture and pass the same value to every remote slot.
        /// A single startup snapshot has no established movement history. Later
        /// pictures clamp to buffered history and the bounded extrapolation endpoint.
        /// </summary>
        public bool TryPreparePresentation(double estimatedServerTick, out SnapshotPresentation presentation)
        {
            presentation = default;
            if (Count == 0 || !Double.IsFinite(estimatedServerTick)) return false;
            // Clock and history may have started on opposite sides of uint wrap.
            // Select the equivalent clock epoch nearest our newest snapshot.
            double clockDelta = (estimatedServerTick - _ticks[_newest]) % SequenceSpace;
            if (clockDelta >= SequenceSpace / 2) clockDelta -= SequenceSpace;
            else if (clockDelta < -SequenceSpace / 2) clockDelta += SequenceSpace;
            double target = _ticks[_newest] + clockDelta - DelayTicks;
            SnapshotPresentationMode mode;
            double oldest = _ticks[(_newest - Count + 1 + Capacity) % Capacity];
            if (Count == 1)
            {
                target = _ticks[_newest];
                mode = SnapshotPresentationMode.Startup;
            }
            else if (target < oldest)
            {
                target = oldest;
                mode = SnapshotPresentationMode.HistoryHold;
            }
            else if (target > _ticks[_newest])
            {
                double limit = _ticks[_newest] + MaxExtrapolationTicks;
                mode = target > limit ? SnapshotPresentationMode.ExtrapolationHold : SnapshotPresentationMode.Extrapolated;
                target = Math.Min(target, limit);
            }
            else mode = SnapshotPresentationMode.Interpolated;
            presentation = new SnapshotPresentation(target, _matchId, _generation, mode);
            return true;
        }

        /// <summary>Commit only after this picture was successfully presented.</summary>
        public bool MarkPresented(in SnapshotPresentation presentation)
        {
            if (!MatchesHistory(presentation)) return false;
            _presentedTick = presentation.Tick;
            _hasPresented = true;
            return true;
        }

        /// <summary>
        /// Input refers to the last displayed picture, unaffected by later packet
        /// arrivals. Before the first picture, the newest applied snapshot is the
        /// conservative fallback. The uint wire value floors sub-tick presentation.
        /// </summary>
        public bool TryCaptureViewTick(out uint tick)
        {
            tick = 0;
            if (Count == 0) return false;
            tick = unchecked((uint)(long)Math.Floor(_hasPresented ? _presentedTick : _ticks[_newest]));
            return true;
        }

        public bool TrySample(int slot, double estimatedServerTick, out SnapshotPlayer state)
        {
            state = default;
            return TryPreparePresentation(estimatedServerTick, out SnapshotPresentation presentation)
                && TrySample(slot, presentation, out state);
        }

        public bool TrySamplePresentation(int slot, double estimatedServerTick,
            out SnapshotPlayerPresentation presentation)
        {
            presentation = default;
            return TryPreparePresentation(estimatedServerTick, out SnapshotPresentation frame)
                && TrySamplePresentation(slot, frame, out presentation);
        }

        public bool TrySample(int slot, in SnapshotPresentation presentation, out SnapshotPlayer state)
        {
            state = default;
            if (!TrySamplePresentation(slot, presentation, out SnapshotPlayerPresentation sampled)) return false;
            state = sampled.State;
            return true;
        }

        public bool TrySamplePresentation(int slot, in SnapshotPresentation presentation,
            out SnapshotPlayerPresentation sampled)
        {
            sampled = default;
            if ((uint)slot >= Slots || !MatchesHistory(presentation)
                || (_masks[_newest] & (1 << slot)) == 0) return false;
            SnapshotPlayer state = _players[_newest * Slots + slot];
            if (state.Health == 0 || (state.Flags & SnapshotPlayerFlags.Spawned) == 0)
            {
                sampled = new SnapshotPlayerPresentation(state, Vector3.Zero);
                return true;
            }

            double target = presentation.Tick;
            if (presentation.Mode is SnapshotPresentationMode.Extrapolated or SnapshotPresentationMode.ExtrapolationHold)
            {
                double extrapolation = Math.Clamp(target - _ticks[_newest], 0, MaxExtrapolationTicks);
                // PlayerInput applies Speed / 2 each 60 Hz tick; wire Speed keeps
                // those engine units. Hold the bounded endpoint after 50 ms.
                state.Position += state.Speed * (float)(extrapolation * 0.5);
                UnderrunSamples++;
                if (presentation.Mode == SnapshotPresentationMode.Extrapolated) ExtrapolatedSamples++;
                else HeldSamples++;
                MaximumExtrapolationTicks = Math.Max(MaximumExtrapolationTicks, extrapolation);
                sampled = new SnapshotPlayerPresentation(state,
                    presentation.Mode == SnapshotPresentationMode.Extrapolated
                        && FiniteVector(state.Speed) ? state.Speed : Vector3.Zero);
                return true;
            }

            uint epoch = _epochs[_newest * Slots + slot];
            int later = _newest;
            for (int age = 0; age < Count; age++)
            {
                int index = (_newest - age + Capacity) % Capacity;
                if ((_masks[index] & (1 << slot)) == 0 || _epochs[index * Slots + slot] != epoch) break;
                if (_ticks[index] <= target)
                {
                    SnapshotPlayer before = _players[index * Slots + slot];
                    SnapshotPlayer after = _players[later * Slots + slot];
                    double span = _ticks[later] - _ticks[index];
                    float amount = span == 0 ? 0 : (float)((target - _ticks[index]) / span);
                    state.Position = Vector3.Lerp(before.Position, after.Position, amount);
                    state.Aim = Direction(before.Aim, after.Aim, amount);
                    state.Facing = Direction(before.Facing, after.Facing, amount);
                    if (presentation.Mode == SnapshotPresentationMode.Interpolated) InterpolatedSamples++;
                    else HeldSamples++;
                    Vector3 visualSpeed = Vector3.Zero;
                    if (presentation.Mode == SnapshotPresentationMode.Interpolated)
                    {
                        visualSpeed = span > 0
                            ? TrajectorySpeed(before, after, span)
                            : PreviousTrajectorySpeed(slot, epoch, later);
                    }
                    sampled = new SnapshotPlayerPresentation(state,
                        visualSpeed);
                    return true;
                }
                later = index;
            }
            // Startup or a discrete transition: never sample the previous life,
            // occupant or form merely to fill the interpolation delay.
            SnapshotPlayer first = _players[later * Slots + slot];
            state.Position = first.Position;
            state.Aim = first.Aim;
            state.Facing = first.Facing;
            HeldSamples++;
            sampled = new SnapshotPlayerPresentation(state, Vector3.Zero);
            return true;
        }

        private bool MatchesHistory(in SnapshotPresentation presentation)
            => Count > 0 && presentation.MatchId == _matchId && presentation.Generation == _generation
            && Double.IsFinite(presentation.Tick);

        private static bool Continuous(in SnapshotPlayer before, in SnapshotPlayer after)
            => before.ConnectionId == after.ConnectionId && before.Life == after.Life
            && before.Hunter == after.Hunter && before.TeamIndex == after.TeamIndex
            && (before.Flags & ContinuityFlags) == (after.Flags & ContinuityFlags)
            && (before.Health == 0) == (after.Health == 0);

        private static Vector3 Direction(Vector3 before, Vector3 after, float amount)
        {
            Vector3 value = Vector3.Lerp(before, after, amount);
            // Opposite directions have a zero midpoint; do not emit NaNs.
            return value.LengthSquared > 0.000001f ? value.Normalized() : after.Normalized();
        }

        private static Vector3 TrajectorySpeed(in SnapshotPlayer before,
            in SnapshotPlayer after, double tickSpan)
        {
            if (!Double.IsFinite(tickSpan) || tickSpan <= 0)
                return Vector3.Zero;
            Vector3 delta = after.Position - before.Position;
            if (!FiniteVector(delta)) return Vector3.Zero;
            float scale = (float)(2.0 / tickSpan);
            Vector3 speed = delta * scale;
            return FiniteVector(speed) ? speed : Vector3.Zero;
        }

        private Vector3 PreviousTrajectorySpeed(int slot, uint epoch, int endpoint)
        {
            // At the newest exact endpoint there is no later sample to form
            // the normal before/after pair. Use the adjacent continuous
            // segment instead, but never cross a slot epoch boundary.
            if (Count <= 1) return Vector3.Zero;
            int previous = (endpoint - 1 + Capacity) % Capacity;
            if ((_masks[previous] & (1 << slot)) == 0
                || _epochs[previous * Slots + slot] != epoch)
                return Vector3.Zero;
            return TrajectorySpeed(_players[previous * Slots + slot],
                _players[endpoint * Slots + slot], _ticks[endpoint] - _ticks[previous]);
        }

        private static bool FiniteVector(Vector3 value)
            => Single.IsFinite(value.X) && Single.IsFinite(value.Y) && Single.IsFinite(value.Z);
    }
}
