namespace MphRead.Mods.Network
{
    /// <summary>Semantic event markers shared by optional client and mandatory server recordings.</summary>
    internal sealed class ReplayEventIndexer
    {
        private bool _terminalWorldMarked;
        internal void Reset() => _terminalWorldMarked = false;

        internal ReplayMarker ForTerminalWorld(byte protocol, bool terminal)
        {
            if (protocol > 8 || !terminal || _terminalWorldMarked) return ReplayMarker.None;
            _terminalWorldMarked = true;
            return ReplayMarker.MatchEnd;
        }

        internal ReplayMarker ForEvent(in NetApplicationEvent message, byte protocol)
        {
            if (message.Type == ReliableEventType.Kill && KillEvent.TryRead(message.Payload.Span, out KillEvent kill))
            {
                ReplayMarker marker = ReplayMarker.Kill;
                if ((kill.Flags & KillEventFlags.Headshot) != 0) marker |= ReplayMarker.Headshot;
                return marker;
            }
            if (protocol <= 8 && message.Type == ReliableEventType.WorldEvent
                && WorldEvent.TryRead(message.Payload.Span, out WorldEvent value))
                return value.Kind switch
                {
                    WorldSignalKind.FlagCaptured => ReplayMarker.FlagCapture,
                    WorldSignalKind.NodeCaptured => ReplayMarker.NodeCapture,
                    WorldSignalKind.PrimeChanged => ReplayMarker.PrimeChange,
                    WorldSignalKind.MatchPoint => ReplayMarker.MatchPoint,
                    WorldSignalKind.OvertimeStarted => ReplayMarker.Overtime,
                    _ => ReplayMarker.None
                };
            if (protocol >= 9 && message.Type == ReliableEventType.MatchAward
                && MatchAwardPacket.TryRead(message.Payload.Span, out MatchAwardPacket packet)
                && MatchAwardPacketConversion.TryToAward(packet, out _))
            {
                ReplayMarker marker = ReplayMarker.Award;
                if (packet.Kind is MatchAwardKind.DoubleKill or MatchAwardKind.TripleKill)
                    marker |= ReplayMarker.MultiKill;
                return marker;
            }
            if (protocol >= 9 && message.Type == ReliableEventType.MatchSemantic
                && MatchSemanticEventPacket.TryRead(message.Payload.Span, out MatchSemanticEventPacket semantic)
                && MatchSemanticEventPacketConversion.TryToEvent(semantic, out _))
            {
                return semantic.Kind switch
                {
                    MatchEventKind.ObjectiveCaptured => ReplayMarker.FlagCapture,
                    MatchEventKind.NodeCaptured => ReplayMarker.NodeCapture,
                    MatchEventKind.PrimeChanged => ReplayMarker.PrimeChange,
                    MatchEventKind.MatchPointReached => ReplayMarker.MatchPoint,
                    MatchEventKind.OvertimeStarted => ReplayMarker.Overtime,
                    MatchEventKind.MatchEnded => ReplayMarker.MatchEnd,
                    _ => ReplayMarker.None
                };
            }
            return ReplayMarker.None;
        }

    }
}
