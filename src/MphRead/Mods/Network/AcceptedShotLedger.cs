using System;

namespace MphRead.Mods.Network;

public readonly record struct AcceptedShot(ShotKey Key, FireEvent Fire);
public enum ShotDisposition : byte { Pending, Fired, Rejected, Expired }
public enum ShotAdmission : byte { Accepted, Duplicate, Conflict, Overflow, Invalid, Expired }

/// <summary>Single bounded owner of admitted discrete shots and their terminal deduplication.
/// Life changes do not retire shots. Only stream or occupant changes invalidate ownership.</summary>
public sealed class AcceptedShotLedger
{
    public const int Capacity = 256;
    public const int RetentionFrames = 64;
    private struct Entry
    {
        public bool Present;
        public AcceptedShot Shot;
        public ShotDisposition Disposition;
        public string? Reason;
    }
    private readonly Entry[] _entries = new Entry[Capacity];
    private bool _seenFrame;
    private uint _frame;
    public Action<AcceptedShot, ShotDisposition, string>? OnDisposition { get; set; }
    public long Accepted { get; private set; }
    public long Fired { get; private set; }
    public long Rejected { get; private set; }
    public long Expired { get; private set; }
    public int Pending { get; private set; }
    public long Duplicate { get; private set; }
    public long Conflicts { get; private set; }
    public long Overflows { get; private set; }
    public long AdmissionRejected { get; private set; }

    private static bool Newer(uint value, uint baseline) => unchecked((int)(value - baseline)) > 0;
    private static bool TooOld(uint now, uint source) => !Newer(source, now)
        && unchecked(now - source) > RetentionFrames;
    private int Find(in ShotKey key)
    {
        for (int i = 0; i < Capacity; i++)
            if (_entries[i].Present && _entries[i].Shot.Key == key) return i;
        return -1;
    }
    public ShotAdmission Accept(in ShotKey key, in FireEvent fire, uint observedFrame)
    {
        int found = Find(key);
        if (found >= 0)
        {
            if (_entries[found].Shot.Fire == fire) { Duplicate++; return ShotAdmission.Duplicate; }
            Conflicts++; AdmissionRejected++; return ShotAdmission.Conflict;
        }
        if (key.ShotId == 0 || key.ShotId != fire.ShotId || key.LifeId == 0
            || key.Generation == 0 || key.ShooterSlot is < 0 or >= 8
            || fire.Kind == FireEventKind.ContinuousTick
            || (byte)fire.Kind > (byte)FireEventKind.TurretFire || Newer(fire.SourceFrame, observedFrame))
        { AdmissionRejected++; return ShotAdmission.Invalid; }
        Expire(observedFrame);
        if (TooOld(_frame, fire.SourceFrame))
        { AdmissionRejected++; return ShotAdmission.Expired; }
        int free = -1;
        for (int i = 0; i < Capacity; i++)
        {
            ref Entry entry = ref _entries[i];
            if (!entry.Present || entry.Disposition != ShotDisposition.Pending
                && TooOld(_frame, entry.Shot.Fire.SourceFrame)) { free = i; break; }
        }
        if (free < 0) { Overflows++; AdmissionRejected++; return ShotAdmission.Overflow; }
        _entries[free] = new Entry { Present = true, Shot = new AcceptedShot(key, fire) };
        Accepted++; Pending++;
        return ShotAdmission.Accepted;
    }
    public bool TryPeek(out AcceptedShot shot)
    {
        shot = default;
        if (Pending == 0) return false;
        bool found = false;
        foreach (Entry entry in _entries)
        {
            if (!entry.Present || entry.Disposition != ShotDisposition.Pending) continue;
            if (!found || Before(entry.Shot, shot)) { shot = entry.Shot; found = true; }
        }
        return found;
    }
    private static bool Before(in AcceptedShot candidate, in AcceptedShot current)
    {
        if (candidate.Fire.SourceFrame != current.Fire.SourceFrame)
            return Newer(current.Fire.SourceFrame, candidate.Fire.SourceFrame);
        if (candidate.Key.LifeId != current.Key.LifeId)
            return unchecked((short)(current.Key.LifeId - candidate.Key.LifeId)) > 0;
        if (candidate.Key.ShotId != current.Key.ShotId)
            return Newer(current.Key.ShotId, candidate.Key.ShotId);
        return candidate.Key.ShooterSlot < current.Key.ShooterSlot;
    }
    public bool Complete(in ShotKey key, ShotDisposition disposition, string reason)
    {
        if (disposition == ShotDisposition.Pending || !Enum.IsDefined(disposition)
            || string.IsNullOrWhiteSpace(reason)) return false;
        int index = Find(key);
        if (index < 0 || _entries[index].Disposition != ShotDisposition.Pending) return false;
        ref Entry entry = ref _entries[index];
        entry.Disposition = disposition; entry.Reason = reason; Pending--;
        if (disposition == ShotDisposition.Fired) Fired++;
        else if (disposition == ShotDisposition.Rejected) Rejected++;
        else Expired++;
        OnDisposition?.Invoke(entry.Shot, disposition, reason);
        return true;
    }
    /// <summary>Use only after retiring a stream or occupant, never at a life boundary.</summary>
    public void ResetObservationClock() { _seenFrame = false; _frame = 0; }
    public void Expire(uint sourceFrame)
    {
        if (_seenFrame && !Newer(sourceFrame, _frame)) return;
        _frame = sourceFrame; _seenFrame = true;
        if (Pending == 0) return;
        for (int i = 0; i < Capacity; i++)
            if (_entries[i].Present && _entries[i].Disposition == ShotDisposition.Pending
                && TooOld(_frame, _entries[i].Shot.Fire.SourceFrame))
                Complete(_entries[i].Shot.Key, ShotDisposition.Expired, "source retention elapsed");
    }
    public void Retire(ulong epoch, ushort match, int slot, ushort generation)
    {
        for (int i = 0; i < Capacity; i++)
        {
            ref Entry entry = ref _entries[i];
            if (!entry.Present) continue;
            ShotKey key = entry.Shot.Key;
            bool wrongStream = key.AuthorityEpoch != epoch || key.MatchId != match;
            bool wrongOccupant = key.ShooterSlot == slot && key.Generation != generation;
            if (!wrongStream && !wrongOccupant) continue;
            if (entry.Disposition == ShotDisposition.Pending)
                Complete(key, ShotDisposition.Rejected, wrongStream ? "stream retired" : "occupant retired");
            // Retired ownership cannot be admitted by the intake fence again.
            entry.Present = false;
        }
    }
    public bool TryGet(in ShotKey key, out AcceptedShot shot, out ShotDisposition disposition, out string? reason)
    {
        int index = Find(key);
        if (index < 0) { shot = default; disposition = default; reason = null; return false; }
        Entry entry = _entries[index]; shot = entry.Shot; disposition = entry.Disposition; reason = entry.Reason;
        return true;
    }
}
