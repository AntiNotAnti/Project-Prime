using System;
using System.Collections.Generic;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Combat
{
    /// <summary>Authoritative presentation only. A bounded ID window also accepts reordered events.</summary>
    public sealed partial class CombatFeedback
    {
        private const int DedupWindow = 512;
        public const int FeedCapacity = 4;
        public const uint FeedTicks = 300;
        private sealed class EventWindow
        {
            private readonly uint[] _ids = new uint[DedupWindow];
            private readonly bool[] _occupied = new bool[DedupWindow];
            private bool _hasEvent;
            private uint _newest;
            internal void WriteReplay(System.IO.BinaryWriter writer)
            {
                writer.Write(_hasEvent); writer.Write(_newest);
                for (int i = 0; i < DedupWindow; i++) { writer.Write(_occupied[i]); writer.Write(_ids[i]); }
            }
            internal void ReadReplay(System.IO.BinaryReader reader)
            {
                _hasEvent = reader.ReadBoolean(); _newest = reader.ReadUInt32();
                for (int i = 0; i < DedupWindow; i++) { _occupied[i] = reader.ReadBoolean(); _ids[i] = reader.ReadUInt32(); }
            }
            public void Reset() { _hasEvent = false; Array.Clear(_occupied); }
            public bool Accept(uint id)
            {
                if (_hasEvent && !Sequence32.IsNewer(id, _newest) && unchecked(_newest - id) >= DedupWindow) return false;
                int slot = (int)(id % DedupWindow);
                if (_occupied[slot] && _ids[slot] == id) return false;
                _occupied[slot] = true;
                _ids[slot] = id;
                if (!_hasEvent || Sequence32.IsNewer(id, _newest)) _newest = id;
                _hasEvent = true;
                return true;
            }
        }
        // Families share server IDs but have independent reliable admission queues.
        private readonly EventWindow _combatEvents = new(), _killEvents = new();
        private uint _match, _presentationTick, _phase;
        private readonly Dictionary<ulong, string> _names = new();
        private readonly Queue<ulong> _nameOrder = new();
        private readonly KillFeedEntry[] _feed = new KillFeedEntry[FeedCapacity];
        public int FeedCount { get; private set; }
        public KillFeedEntry FeedAt(int index) => _feed[index];
        public CombatActor Local { get; private set; } = CombatActor.None;
        public CombatFeedbackState State { get; } = new();
        public DamageHistory History { get; } = new();
        public RecapArchive Recaps { get; } = new();

        public void Bind(uint match, CombatActor local, ReadOnlySpan<NetRosterEntry> roster, uint presentationTick = 0, uint phaseRevision = 0)
        {
            _presentationTick = presentationTick;
            if (_phase != phaseRevision)
            {
                _phase = phaseRevision;
                _combatEvents.Reset();
                _killEvents.Reset();
                State.ClearNotices();
            }
            if (_match != match)
            {
                _match = match;
                _combatEvents.Reset();
                _killEvents.Reset();
                FeedCount = 0;
                Array.Clear(_feed);
                _names.Clear();
                _nameOrder.Clear();
                Recaps.Clear();
                Local = CombatActor.None;
                History.Bind(CombatActor.None);
                State.Reset();
            }
            if (Local != local)
            {
                if (Local.ConnectionId != local.ConnectionId || Local.Slot != local.Slot) Recaps.Clear();
                else if (Local.IsValid && History.Count > 0 && !State.Dead)
                    Recaps.Capture(Local, History, new CombatFeedbackState { Dead = true, RecapHeading = "Previous life damage" }, _presentationTick);
                Local = local;
                History.Bind(local);
                State.Reset();
            }
            foreach (NetRosterEntry entry in roster)
            {
                if (entry.ConnectionId == 0) continue;
                if (!_names.ContainsKey(entry.ConnectionId))
                {
                    if (_nameOrder.Count == 32) _names.Remove(_nameOrder.Dequeue());
                    _nameOrder.Enqueue(entry.ConnectionId);
                }
                _names[entry.ConnectionId] = entry.Name;
            }
        }
        private string Name(CombatActor actor) => actor.IsNone ? "Environment"
            : _names.TryGetValue(actor.ConnectionId, out string? name) ? name : "Unknown";
        public static string WeaponName(byte weapon) => weapon switch
        {
            0 => "Power Beam", 1 => "Volt Driver", 2 => "Missile", 3 => "Battlehammer", 4 => "Imperialist",
            5 => "Judicator", 6 => "Magmaul", 7 => "Shock Coil", 8 => "Omega Cannon", 9 => "Platform beam", 10 => "Environmental beam", _ => "Unknown"
        };
        private static int MarkerPriority(HitMarkerKind kind) => kind switch
        {
            HitMarkerKind.Kill => 4,
            HitMarkerKind.Headshot => 3,
            HitMarkerKind.Hit => 2,
            HitMarkerKind.Predicted => 1,
            _ => 0
        };

        public static uint MarkerDuration(HitMarkerKind kind) => kind switch
        {
            HitMarkerKind.Kill => 18,
            HitMarkerKind.Headshot => 16,
            HitMarkerKind.Hit => 12,
            HitMarkerKind.Predicted => 6,
            _ => 0
        };

        private void Marker(HitMarkerKind kind, uint tick, bool authoritative = true)
        {
            tick = ReceiptTick(tick);
            if (kind == HitMarkerKind.Kill && !CombatFeedbackSettings.KillConfirmation) return;
            if (kind == HitMarkerKind.Headshot && !CombatFeedbackSettings.HeadshotCue) kind = HitMarkerKind.Hit;
            // A stronger authoritative cue promotes a speculative or weaker
            // cue and refreshes the one marker lifetime.  A weaker event in an
            // active marker window cannot erase the stronger cue.
            if (State.Marker != HitMarkerKind.None
                && Age(tick, State.MarkerTick) < MarkerDuration(State.Marker)
                && MarkerPriority(kind) < MarkerPriority(State.Marker)) return;
            State.Marker = kind;
            State.MarkerTick = tick;
            State.MarkerSequence++;
            if (authoritative && kind != HitMarkerKind.Predicted)
                State.MarkerAudioSequence++;
        }

        /// <summary>
        /// Adds the local-only marker used by instant hit feedback.  The
        /// caller has already performed source/identity eligibility checks;
        /// this method intentionally does not touch health, history, score,
        /// or replay state.
        /// </summary>
        public void PresentPredictedHit(CombatActor attacker, CombatActor target, uint tick)
        {
            if (_match == 0 || CombatFeedbackSettings.Timing != HitMarkerTiming.Instant
                || attacker != Local || !attacker.IsValid || !target.IsValid || target == Local)
                return;
            Marker(HitMarkerKind.Predicted, tick, authoritative: false);
        }
        private uint ReceiptTick(uint tick) => Sequence32.IsNewer(_presentationTick, tick) ? _presentationTick : tick;
        public bool Process(in CombatEvent value, bool allowLocalHitMarker = true)
        {
            if (_match == 0 || !value.IsValid || !_combatEvents.Accept(value.Id)) return false;
            if (value.Kind != CombatEventKind.Damage || value.Amount == 0) return true;
            // Silent events are still legitimate history, but produce no confirmation cue.
            if (allowLocalHitMarker && value.Actor == Local && Local.IsValid && value.Target != Local
                && (value.Flags & CombatEventFlags.Silent) == 0)
            {
                bool headshot = (value.Flags & CombatEventFlags.Headshot) != 0;
                Marker(headshot ? HitMarkerKind.Headshot : HitMarkerKind.Hit, value.Tick);
                if (headshot && CombatFeedbackSettings.HeadshotCue)
                    State.HeadshotNotice = new("HEADSHOT!", ReceiptTick(value.Tick));
            }
            if (value.Target != Local) Recaps.ApplyLate(value, Name(value.Actor), WeaponName(value.Weapon));
            if (value.Target == Local && Local.IsValid)
            {
                History.Add(value, Name(value.Actor), WeaponName(value.Weapon));
                if (value.Health == 0)
                {
                    State.Dead = true;
                    State.RecapHeading = $"Eliminated by {Name(value.Actor)}";
                    State.FinalDamage = value.Amount;
                    State.RecapFinal = $"{WeaponName(value.Weapon)}  {value.Amount} damage";
                }
                if (State.Dead) Recaps.Capture(Local, History, State, value.Tick);
            }
            return true;
        }
        public bool Process(in KillEvent value)
        {
            if (!value.IsValid || value.MatchId != _match
                || (_phase != 0 && value.PhaseRevision != _phase)
                || !_killEvents.Accept(value.Id)) return false;
            string weapon = value.SourceKind switch
            {
                KillSourceKind.Bomb => "Bomb", KillSourceKind.Alt => "Alt form", KillSourceKind.Environment => "Environment",
                _ => WeaponName(value.Weapon)
            };
            string text = $"{Name(value.Killer)} > {Name(value.Victim)}  {weapon}";
            if ((value.Flags & KillEventFlags.Suicide) != 0) text = $"{Name(value.Victim)}  self elimination";
            if ((value.Flags & KillEventFlags.Headshot) != 0) text += " HS";
            if ((value.Flags & KillEventFlags.TeamKill) != 0) text += " TEAM";
            if ((value.Flags & KillEventFlags.Affinity) != 0) text += " AFF";
            if (value.Assists.Length > 0) text += $" +{value.Assists.Length}";
            if (FeedCount == FeedCapacity) Array.Copy(_feed, 1, _feed, 0, --FeedCount);
            _feed[FeedCount++] = new(ReceiptTick(value.Tick), value.Killer, value.Victim, text);
            if (value.Killer == Local && Local.IsValid && value.Victim != Local)
            {
                Marker(HitMarkerKind.Kill, value.Tick);
                if (CombatFeedbackSettings.KillConfirmation)
                {
                    State.KillNotice = new(
                        (value.Flags & KillEventFlags.TeamKill) != 0
                            ? $"YOU KILLED A TEAMMATE, ({Name(value.Victim)})!"
                            : (value.Flags & KillEventFlags.Headshot) != 0
                                ? $"YOUR HEADSHOT KILLED {Name(value.Victim)}!"
                                : $"YOU KILLED {Name(value.Victim)}!",
                        ReceiptTick(value.Tick));
                }
            }
            if (value.Victim != Local) Recaps.ApplyLate(value, Name(value.Killer), weapon);
            if (value.Victim == Local && Local.IsValid)
            {
                State.Dead = true;
                State.RecapHeading = $"Eliminated by {Name(value.Killer)}";
                State.RecapFinal = State.FinalDamage > 0 ? $"{weapon}  {State.FinalDamage} damage" : weapon;
                Recaps.Capture(Local, History, State, value.Tick, weapon);
            }
            return true;
        }
        public static uint Age(uint now, uint then) => Sequence32.IsNewer(then, now) ? 0 : unchecked(now - then);
        public HitMarkerKind VisibleMarker(uint tick) => CombatFeedbackSettings.HitMarkers == HitMarkerMode.Off
            || Age(tick, State.MarkerTick) >= MarkerDuration(State.Marker) ? HitMarkerKind.None : State.Marker;
        public bool IsHeadshotNoticeVisible(uint tick) => CombatFeedbackSettings.HeadshotCue
            && State.HeadshotNotice.IsValid
            && Age(tick, State.HeadshotNotice.Tick) < CombatFeedbackState.HeadshotNoticeTicks;
        public bool IsKillNoticeVisible(uint tick) => CombatFeedbackSettings.KillConfirmation
            && State.KillNotice.IsValid
            && Age(tick, State.KillNotice.Tick) < CombatFeedbackState.KillNoticeTicks;
        public static int DamageSector(Vector3 direction, Vector3 forward, Vector3 right)
        {
            Vector3 horizontalForward = new(forward.X, 0, forward.Z);
            Vector3 horizontalRight = new(right.X, 0, right.Z);
            if (horizontalForward.LengthSquared < .000001f || horizontalRight.LengthSquared < .000001f) return -1;
            horizontalForward.Normalize();
            horizontalRight.Normalize();
            float f = Vector3.Dot(new(direction.X, 0, direction.Z), horizontalForward);
            float r = Vector3.Dot(new(direction.X, 0, direction.Z), horizontalRight);
            if (!float.IsFinite(f) || !float.IsFinite(r) || f * f + r * r < .000001f) return -1;
            int sector = (int)MathF.Floor(MathF.Atan2(r, f) / (MathF.PI / 4) + .5f);
            return (sector + 8) % 8;
        }
    }
}
