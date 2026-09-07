using System;

namespace MphRead.Mods.Network
{
    /// <summary>Semantic event markers shared by optional client and mandatory server recordings.</summary>
    internal sealed class ReplayEventIndexer
    {
        private readonly CombatActor[] _lastKillActor = new CombatActor[8];
        private readonly uint[] _lastKillTick = new uint[8];
        internal void Reset() { Array.Clear(_lastKillActor); Array.Clear(_lastKillTick); }
        internal ReplayMarker ForEvent(in NetApplicationEvent message)
        {
            if (message.Type == ReliableEventType.Kill && KillEvent.TryRead(message.Payload.Span, out KillEvent kill))
            {
                ReplayMarker marker = ReplayMarker.Kill;
                if ((kill.Flags & KillEventFlags.Headshot) != 0) marker |= ReplayMarker.Headshot;
                if (kill.Killer.IsValid && (kill.Flags & (KillEventFlags.Suicide | KillEventFlags.TeamKill)) == 0)
                {
                    int slot = kill.Killer.Slot;
                    if (_lastKillActor[slot] == kill.Killer && (kill.Tick == _lastKillTick[slot] || Sequence32.IsNewer(kill.Tick, _lastKillTick[slot]))
                        && unchecked(kill.Tick - _lastKillTick[slot]) <= 180) marker |= ReplayMarker.MultiKill;
                    _lastKillActor[slot] = kill.Killer; _lastKillTick[slot] = kill.Tick;
                }
                return marker;
            }
            if (message.Type == ReliableEventType.WorldEvent && WorldEvent.TryRead(message.Payload.Span, out WorldEvent value))
                return value.Kind switch
                {
                    WorldSignalKind.FlagCaptured => ReplayMarker.FlagCapture,
                    WorldSignalKind.NodeCaptured => ReplayMarker.NodeCapture,
                    WorldSignalKind.PrimeChanged => ReplayMarker.PrimeChange,
                    WorldSignalKind.MatchPoint => ReplayMarker.MatchPoint,
                    WorldSignalKind.OvertimeStarted => ReplayMarker.Overtime,
                    _ => ReplayMarker.None
                };
            return ReplayMarker.None;
        }

    }
}
