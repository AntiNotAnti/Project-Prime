using System;
using System.IO;
using System.Text;
using MphRead.Mods.Network;

namespace MphRead.Combat
{
    internal static class ReplayFeedbackState
    {
        internal const int MaximumBytes = 256 * 1024;
        private static readonly UTF8Encoding Utf8 = new(false, true);
        internal static byte[] Capture(CombatFeedback combat, WorldFeedback world)
        {
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
            writer.Write((byte)3); combat.WriteReplay(writer, includeNotices: true,
                includeMarkerDetails: true); world.WriteReplay(writer);
            if (stream.Length > MaximumBytes) throw new InvalidDataException("Replay feedback exceeds its bound.");
            return stream.ToArray();
        }
        internal static bool Restore(ReadOnlySpan<byte> bytes, CombatFeedback combat, WorldFeedback world)
        {
            if (bytes.Length > MaximumBytes) return false;
            byte[] copy = bytes.ToArray();
            try
            {
                // Validate into isolated objects first: malformed data cannot partially replace live state.
                Read(copy, new CombatFeedback(), new WorldFeedback());
                Read(copy, combat, world);
                return true;
            }
            catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or OverflowException) { return false; }
        }
        private static void Read(byte[] bytes, CombatFeedback combat, WorldFeedback world)
        {
            using var stream = new MemoryStream(bytes, false); using var reader = new BinaryReader(stream);
            byte version = reader.ReadByte();
            if (version is not (1 or 2 or 3)) throw new InvalidDataException("Replay feedback version.");
            combat.ReadReplay(reader, includeNotices: version >= 2,
                includeMarkerDetails: version >= 3); world.ReadReplay(reader);
            if (stream.Position != stream.Length) throw new InvalidDataException("Replay feedback trailing bytes.");
        }
        internal static void Text(BinaryWriter writer, string text)
        {
            byte[] bytes = Utf8.GetBytes(text);
            if (bytes.Length > 2048) throw new InvalidDataException("Replay text too long.");
            writer.Write((ushort)bytes.Length); writer.Write(bytes);
        }
        internal static string Text(BinaryReader reader)
        {
            int size = reader.ReadUInt16();
            if (size > 2048) throw new InvalidDataException("Replay text too long.");
            byte[] bytes = reader.ReadBytes(size);
            if (bytes.Length != size) throw new EndOfStreamException();
            return Utf8.GetString(bytes);
        }
        internal static void Notice(BinaryWriter writer, CombatFeedbackNotice notice)
        {
            writer.Write(notice.IsValid);
            if (!notice.IsValid) return;
            writer.Write(notice.Tick);
            Text(writer, notice.Text);
        }
        internal static CombatFeedbackNotice Notice(BinaryReader reader)
        {
            if (!reader.ReadBoolean()) return default;
            uint tick = reader.ReadUInt32();
            return new(Text(reader), tick);
        }
        internal static int Count(BinaryReader reader, int maximum)
        {
            int count = reader.ReadInt32();
            return count >= 0 && count <= maximum ? count : throw new InvalidDataException("Replay collection too large.");
        }
        internal static void Actor(BinaryWriter writer, CombatActor actor)
        { writer.Write(actor.Slot); writer.Write(actor.ConnectionId); writer.Write(actor.Life); }
        internal static CombatActor Actor(BinaryReader reader)
        {
            var actor = new CombatActor(reader.ReadByte(), reader.ReadUInt64(), reader.ReadUInt32());
            return actor.IsValid || actor.IsNone ? actor : throw new InvalidDataException("Replay actor invalid.");
        }
        internal static void Damage(BinaryWriter writer, DamageHistoryEntry entry)
        { Actor(writer, entry.Source); writer.Write(entry.Tick); writer.Write(entry.Amount); writer.Write(entry.Weapon); Text(writer, entry.Text); }
        internal static DamageHistoryEntry Damage(BinaryReader reader)
            => new(Actor(reader), reader.ReadUInt32(), reader.ReadUInt16(), reader.ReadByte(), Text(reader));
    }

    public sealed partial class DamageHistory
    {
        internal void WriteReplay(BinaryWriter writer)
        {
            ReplayFeedbackState.Actor(writer, Life); writer.Write(Count);
            for (int i = 0; i < Count; i++) ReplayFeedbackState.Damage(writer, _entries[i]);
        }
        internal void ReadReplay(BinaryReader reader)
        {
            Life = ReplayFeedbackState.Actor(reader); Count = ReplayFeedbackState.Count(reader, Capacity);
            Array.Clear(_entries);
            for (int i = 0; i < Count; i++) _entries[i] = ReplayFeedbackState.Damage(reader);
        }
    }
    public sealed partial class RecapArchive
    {
        internal void WriteReplay(BinaryWriter writer)
        {
            writer.Write(Revision); writer.Write(Count);
            for (int i = 0; i < Count; i++)
            {
                LifeRecap life = _lives[i]; ReplayFeedbackState.Actor(writer, life.Life); writer.Write(life.DeathTick);
                ReplayFeedbackState.Text(writer, life.Heading); ReplayFeedbackState.Text(writer, life.Weapon);
                ReplayFeedbackState.Text(writer, life.Final); writer.Write(life.FinalDamage); writer.Write(life.HasKill);
                writer.Write(life.Count);
                for (int j = 0; j < life.Count; j++) ReplayFeedbackState.Damage(writer, life[j]);
            }
        }
        internal void ReadReplay(BinaryReader reader)
        {
            Revision = reader.ReadUInt32(); Count = ReplayFeedbackState.Count(reader, Capacity);
            foreach (LifeRecap life in _lives) life.Reset();
            for (int i = 0; i < Count; i++)
            {
                LifeRecap life = _lives[i]; life.Life = ReplayFeedbackState.Actor(reader); life.DeathTick = reader.ReadUInt32();
                life.Heading = ReplayFeedbackState.Text(reader); life.Weapon = ReplayFeedbackState.Text(reader);
                life.Final = ReplayFeedbackState.Text(reader); life.FinalDamage = reader.ReadUInt16(); life.HasKill = reader.ReadBoolean();
                int count = ReplayFeedbackState.Count(reader, DamageHistory.Capacity);
                for (int j = 0; j < count; j++) life.Add(ReplayFeedbackState.Damage(reader));
            }
        }
    }
    public sealed partial class CombatFeedback
    {
        internal void WriteReplay(BinaryWriter writer, bool includeNotices = false,
            bool includeMarkerDetails = false)
        {
            writer.Write(_match); writer.Write(_presentationTick); writer.Write(_phase);
            _combatEvents.WriteReplay(writer); _killEvents.WriteReplay(writer);
            writer.Write(_nameOrder.Count);
            foreach (ulong id in _nameOrder) { writer.Write(id); ReplayFeedbackState.Text(writer, _names[id]); }
            writer.Write(FeedCount);
            for (int i = 0; i < FeedCount; i++)
            {
                writer.Write(_feed[i].Tick); ReplayFeedbackState.Actor(writer, _feed[i].Killer);
                ReplayFeedbackState.Actor(writer, _feed[i].Victim); ReplayFeedbackState.Text(writer, _feed[i].Text);
            }
            ReplayFeedbackState.Actor(writer, Local);
            // Predicted markers are a local presentation artifact.  Mapping
            // one to None keeps the v1 checkpoint byte layout and ensures a
            // replay never replays speculative feedback as if it were a server
            // fact.
            HitMarkerKind replayMarker = State.Marker == HitMarkerKind.Predicted
                ? HitMarkerKind.None : State.Marker;
            writer.Write((byte)replayMarker); writer.Write(State.MarkerTick); writer.Write(State.MarkerSequence);
            writer.Write(State.Dead); writer.Write(State.FinalDamage);
            ReplayFeedbackState.Text(writer, State.RecapHeading); ReplayFeedbackState.Text(writer, State.RecapFinal);
            if (includeNotices)
            {
                ReplayFeedbackState.Notice(writer, State.HeadshotNotice);
                ReplayFeedbackState.Notice(writer, State.KillNotice);
            }
            if (includeMarkerDetails)
            {
                bool authoritativeMarker = replayMarker != HitMarkerKind.None;
                writer.Write(authoritativeMarker ? State.MarkerAudioSequence : 0);
                writer.Write(authoritativeMarker ? State.MarkerPulseSequence : 0);
                writer.Write(authoritativeMarker ? State.MarkerPulseTick : 0);
                writer.Write(authoritativeMarker ? State.MarkerHapticIdentity : 0);
                writer.Write((byte)(authoritativeMarker
                    ? State.MarkerAudioKind : HitMarkerKind.None));
                writer.Write(authoritativeMarker ? State.MarkerDamage : (ushort)0);
                writer.Write(authoritativeMarker ? State.MarkerHealth : (ushort)0);
                writer.Write(authoritativeMarker ? State.MarkerWeapon : (byte)0);
                writer.Write((ushort)(authoritativeMarker
                    ? State.MarkerFlags : CombatEventFlags.None));
                writer.Write(authoritativeMarker ? State.MarkerBurst : (byte)0);
            }
            History.WriteReplay(writer); Recaps.WriteReplay(writer);
        }
        internal void ReadReplay(BinaryReader reader, bool includeNotices = false,
            bool includeMarkerDetails = false)
        {
            _match = reader.ReadUInt32(); _presentationTick = reader.ReadUInt32(); _phase = reader.ReadUInt32();
            _combatEvents.ReadReplay(reader); _killEvents.ReadReplay(reader);
            _names.Clear(); _nameOrder.Clear();
            int names = ReplayFeedbackState.Count(reader, 32);
            for (int i = 0; i < names; i++)
            {
                ulong id = reader.ReadUInt64(); string name = ReplayFeedbackState.Text(reader);
                if (id == 0 || !_names.TryAdd(id, name)) throw new InvalidDataException("Replay duplicate name identity.");
                _nameOrder.Enqueue(id);
            }
            FeedCount = ReplayFeedbackState.Count(reader, FeedCapacity); Array.Clear(_feed);
            for (int i = 0; i < FeedCount; i++) _feed[i] = new(reader.ReadUInt32(),
                ReplayFeedbackState.Actor(reader), ReplayFeedbackState.Actor(reader), ReplayFeedbackState.Text(reader));
            Local = ReplayFeedbackState.Actor(reader);
            State.Marker = (HitMarkerKind)reader.ReadByte();
            if (State.Marker > HitMarkerKind.Kill) throw new InvalidDataException("Replay marker invalid.");
            State.MarkerTick = reader.ReadUInt32(); State.MarkerSequence = reader.ReadUInt32();
            State.MarkerAudioSequence = State.MarkerSequence;
            State.Dead = reader.ReadBoolean(); State.FinalDamage = reader.ReadUInt16();
            State.RecapHeading = ReplayFeedbackState.Text(reader); State.RecapFinal = ReplayFeedbackState.Text(reader);
            State.ClearNotices();
            if (includeNotices)
            {
                State.HeadshotNotice = ReplayFeedbackState.Notice(reader);
                State.KillNotice = ReplayFeedbackState.Notice(reader);
            }
            State.MarkerPulseSequence = State.MarkerSequence;
            State.MarkerPulseTick = State.MarkerTick;
            State.MarkerHapticIdentity = 0;
            State.MarkerAudioKind = State.Marker;
            State.MarkerDamage = State.MarkerHealth = 0;
            State.MarkerWeapon = 0;
            State.MarkerFlags = CombatEventFlags.None;
            State.MarkerBurst = State.Marker == HitMarkerKind.None ? (byte)0 : (byte)1;
            if (includeMarkerDetails)
            {
                State.MarkerAudioSequence = reader.ReadUInt32();
                State.MarkerPulseSequence = reader.ReadUInt32();
                State.MarkerPulseTick = reader.ReadUInt32();
                State.MarkerHapticIdentity = reader.ReadUInt32();
                State.MarkerAudioKind = (HitMarkerKind)reader.ReadByte();
                if (State.MarkerAudioKind > HitMarkerKind.Predicted)
                    throw new InvalidDataException("Replay marker audio kind invalid.");
                State.MarkerDamage = reader.ReadUInt16();
                State.MarkerHealth = reader.ReadUInt16();
                State.MarkerWeapon = reader.ReadByte();
                State.MarkerFlags = (CombatEventFlags)reader.ReadUInt16();
                if ((State.MarkerFlags & ~(CombatEventFlags)1023) != 0)
                    throw new InvalidDataException("Replay marker flags invalid.");
                State.MarkerBurst = reader.ReadByte();
                if (State.MarkerBurst > 8)
                    throw new InvalidDataException("Replay marker burst invalid.");
            }
            History.ReadReplay(reader); Recaps.ReadReplay(reader);
        }
    }
    public sealed partial class WorldFeedback
    {
        internal void WriteReplay(BinaryWriter writer)
        {
            writer.Write(_match); writer.Write(_phase); writer.Write(_newest); writer.Write(_hasId);
            for (int i = 0; i < _ids.Length; i++) { writer.Write(_seen[i]); writer.Write(_ids[i]); }
            writer.Write(LastEvent.IsValid);
            if (LastEvent.IsValid) { Span<byte> bytes = stackalloc byte[WorldEvent.Size]; LastEvent.Write(bytes); writer.Write(bytes); }
            ReplayFeedbackState.Text(writer, Message); writer.Write(Tick); writer.Write(Sequence);
        }
        internal void ReadReplay(BinaryReader reader)
        {
            _match = reader.ReadUInt32(); _phase = reader.ReadUInt32(); _newest = reader.ReadUInt32(); _hasId = reader.ReadBoolean();
            for (int i = 0; i < _ids.Length; i++) { _seen[i] = reader.ReadBoolean(); _ids[i] = reader.ReadUInt32(); }
            LastEvent = default;
            if (reader.ReadBoolean())
            {
                if (!WorldEvent.TryRead(reader.ReadBytes(WorldEvent.Size), out WorldEvent value)) throw new InvalidDataException("Replay world event invalid.");
                LastEvent = value;
            }
            Message = ReplayFeedbackState.Text(reader); Tick = reader.ReadUInt32(); Sequence = reader.ReadUInt32();
            // Notices are transient presentation work, not replay state. A
            // restored checkpoint must never inherit queued sounds from the
            // state that was seeking or being replaced.
            ClearPendingNotices();
            DroppedNotices = 0;
        }
    }
}
