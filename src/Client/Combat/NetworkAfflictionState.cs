using MphRead.Mods.Network;

namespace MphRead.Combat
{
    public readonly record struct AfflictionTimers(ushort Frozen, ushort Burn, ushort Disrupt);

    /// <summary>Durable presentation facts, anchored to authoritative ticks. Never runs gameplay.</summary>
    public sealed class NetworkAfflictionState
    {
        public CombatActor Identity { get; private set; } = CombatActor.None;
        public uint Tick { get; private set; }
        private bool _hasState, _snapshotAtTick, _hasEvent;
        private uint _eventId;
        private AfflictionTimers _timers;

        public void Reset()
        {
            Identity = CombatActor.None;
            Tick = 0;
            _hasState = _snapshotAtTick = _hasEvent = false;
            _eventId = 0;
            _timers = default;
        }

        public bool Reconcile(CombatActor identity, uint tick, AfflictionTimers timers)
        {
            if (!identity.IsValid) return false;
            if (identity != Identity)
            {
                Identity = identity;
                _hasState = _hasEvent = false;
            }
            if (_hasState && tick != Tick && !Sequence32.IsNewer(tick, Tick)) return false;
            Tick = tick;
            _timers = timers;
            _hasState = _snapshotAtTick = true;
            return true;
        }

        public bool Apply(in CombatEvent value)
        {
            if (value.Kind != CombatEventKind.Affliction || value.Target != Identity || !_hasState
                || (_hasEvent && !Sequence32.IsNewer(value.Id, _eventId))) return false;
            _eventId = value.Id;
            _hasEvent = true;
            if (value.Tick == Tick && _snapshotAtTick
                || value.Tick != Tick && !Sequence32.IsNewer(value.Tick, Tick)) return false;
            Tick = value.Tick;
            _timers = new(value.FrozenTicks, value.BurnTicks, value.DisruptTicks);
            _snapshotAtTick = false;
            return true;
        }

        public AfflictionTimers At(uint tick)
        {
            if (!_hasState) return default;
            uint elapsed = tick == Tick || !Sequence32.IsNewer(tick, Tick) ? 0 : unchecked(tick - Tick);
            ushort Remaining(ushort duration) => elapsed >= duration ? (ushort)0 : (ushort)(duration - elapsed);
            return new(Remaining(_timers.Frozen), Remaining(_timers.Burn), Remaining(_timers.Disrupt));
        }
    }
}
