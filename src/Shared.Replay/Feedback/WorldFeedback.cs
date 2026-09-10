using MphRead.Mods.Network;

namespace MphRead.Combat
{
    public readonly record struct WorldFeedbackNotice(WorldEvent Event, uint ReceiptTick);

    /// <summary>Immediate messages from authority; full world snapshots still own all entity state.</summary>
    public sealed partial class WorldFeedback
    {
        internal const int NoticeCapacity = 32;
        private readonly uint[] _ids = new uint[512];
        private readonly bool[] _seen = new bool[512];
        private readonly WorldFeedbackNotice[] _notices = new WorldFeedbackNotice[NoticeCapacity];
        private uint _match, _phase, _newest;
        private bool _hasId;
        private int _noticeHead, _noticeCount;
        public WorldEvent LastEvent { get; private set; }
        public WorldSignalKind LastKind => LastEvent.Kind;
        public OpenTK.Mathematics.Vector3 LastPosition => LastEvent.Position;
        public string Message { get; private set; } = "";
        public uint Tick { get; private set; }
        public uint Sequence { get; private set; }
        public uint DroppedNotices { get; private set; }
        internal int PendingNoticeCount => _noticeCount;
        public void Bind(uint match, uint phase)
        {
            if (_match == match && _phase == phase) return;
            _match = match; _phase = phase; _hasId = false;
            System.Array.Clear(_seen);
            ClearPendingNotices();
            DroppedNotices = 0;
            Message = "";
        }
        public bool Process(in WorldEvent value, CombatActor local, uint receiptTick, bool pickupRespawnAnnouncements = false)
        {
            if (!value.IsValid || value.MatchId != _match || value.PhaseRevision != _phase) return false;
            if (_hasId && !Sequence32.IsNewer(value.Id, _newest) && unchecked(_newest - value.Id) >= 512) return false;
            int index = (int)(value.Id % 512);
            if (_seen[index] && _ids[index] == value.Id) return false;
            _seen[index] = true; _ids[index] = value.Id;
            if (!_hasId || Sequence32.IsNewer(value.Id, _newest)) _newest = value.Id;
            _hasId = true;
            string message = value.Kind switch
            {
                WorldSignalKind.PickupConsumed when value.Actor == local && local.IsValid => "PICKUP ACQUIRED",
                WorldSignalKind.PickupRespawned when pickupRespawnAnnouncements && IsMajorPickup(value.A) => "MAJOR PICKUP AVAILABLE",
                WorldSignalKind.FlagPickedUp => "FLAG TAKEN",
                WorldSignalKind.FlagDropped => "FLAG DROPPED",
                WorldSignalKind.FlagReset => "FLAG RETURNED",
                WorldSignalKind.FlagCaptured => "FLAG CAPTURED",
                WorldSignalKind.NodeCaptured => "NODE CAPTURED",
                WorldSignalKind.NodeContested when value.A != 0 => "NODE CONTESTED",
                WorldSignalKind.PrimeChanged => "PRIME HUNTER CHANGED",
                WorldSignalKind.DefenderStateChanged => "DEFENDER STATE CHANGED",
                WorldSignalKind.OvertimeStarted => "OVERTIME",
                WorldSignalKind.MatchPoint => "MATCH POINT",
                _ => ""
            };
            if (message.Length > 0)
            {
                LastEvent = value;
                Message = message; Tick = receiptTick; Sequence++;
                EnqueueNotice(value, receiptTick);
            }
            return true;
        }

        public bool TryDequeueNotice(out WorldFeedbackNotice notice)
        {
            if (_noticeCount == 0)
            {
                notice = default;
                return false;
            }
            notice = _notices[_noticeHead];
            _notices[_noticeHead] = default;
            _noticeHead = (_noticeHead + 1) % NoticeCapacity;
            _noticeCount--;
            return true;
        }

        public void ClearPendingNotices()
        {
            System.Array.Clear(_notices);
            _noticeHead = 0;
            _noticeCount = 0;
        }

        private void EnqueueNotice(in WorldEvent value, uint receiptTick)
        {
            var notice = new WorldFeedbackNotice(value, receiptTick);
            if (_noticeCount == NoticeCapacity)
            {
                _notices[_noticeHead] = notice;
                _noticeHead = (_noticeHead + 1) % NoticeCapacity;
                DroppedNotices++;
                return;
            }
            int index = (_noticeHead + _noticeCount) % NoticeCapacity;
            _notices[index] = notice;
            _noticeCount++;
        }

        private static bool IsMajorPickup(uint item) => (ItemType)item is ItemType.DoubleDamage
            or ItemType.Cloak or ItemType.Deathalt or ItemType.OmegaCannon;
    }
}
