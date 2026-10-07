using System;
using System.Linq;

namespace MphRead.Mods.Replay;

internal sealed partial class ReplayCameraTrack
{
    private ReplayCameraTrack? _windowSource;
    private uint _windowStart, _windowEnd;
    internal uint? WindowDuration => _windowSource == null ? null : _windowEnd - _windowStart;

    /// <summary>Retain the canonical evaluator's original controls and time
    /// interval. Cropping must not re-fit easing, spline neighbours or arc lengths.</summary>
    internal ReplayCameraTrack Crop(uint start, uint end)
    {
        if (start >= end || _windowSource != null && end > _windowEnd - _windowStart)
            throw new ArgumentOutOfRangeException(nameof(end));
        uint origin = _windowSource == null ? 0 : _windowStart;
        var result = new ReplayCameraTrack
        {
            _windowSource = (_windowSource ?? this).CloneTrack(),
            _windowStart = checked(origin + start), _windowEnd = checked(origin + end)
        };
        result.RebuildWindowKeys();
        return result;
    }

    private ReplayCameraTrack CloneTrack()
    {
        var copy = new ReplayCameraTrack();
        copy._keys.AddRange(_keys);
        copy._windowSource = _windowSource?.CloneTrack();
        copy._windowStart = _windowStart; copy._windowEnd = _windowEnd;
        return copy;
    }

    private void RestoreTrack(ReplayCameraTrack snapshot)
    {
        _keys.Clear(); _keys.AddRange(snapshot._keys);
        _windowSource = snapshot._windowSource;
        _windowStart = snapshot._windowStart; _windowEnd = snapshot._windowEnd;
    }
    internal bool Edit(Func<ReplayCameraTrack, bool> edit)
    {
        var before = CloneTrack(); bool committed = false;
        try { committed = edit(this); return committed; }
        finally { if (!committed) RestoreTrack(before); }
    }

    private bool SampleWindow(double frame, out ReplayCameraKeyframe sample, bool constantSpeed)
    {
        double relative = Math.Clamp(frame, 0, _windowEnd - _windowStart);
        bool sampled = _windowSource!.Sample(_windowStart + relative, out sample, constantSpeed);
        if (sampled) sample = sample with { Frame = (uint)Math.Clamp(frame, 0, uint.MaxValue) };
        return sampled;
    }

    private void RebuildWindowKeys()
    {
        _keys.Clear();
        var source = _windowSource!;
        if (source._keys.Count == 0) return;
        _keys.AddRange(source._keys.Where(k => k.Frame >= _windowStart && k.Frame <= _windowEnd)
            .Select(k => k with { Frame = k.Frame - _windowStart }));
        if (_windowStart > source._keys[0].Frame && _windowStart < source._keys[^1].Frame
            && _keys.All(k => k.Frame != 0) && source.Sample(_windowStart, out var first))
            _keys.Add(first with { Frame = 0 });
        uint duration = _windowEnd - _windowStart;
        if (_windowEnd > source._keys[0].Frame && _windowEnd < source._keys[^1].Frame
            && _keys.All(k => k.Frame != duration) && source.Sample(_windowEnd, out var last))
            _keys.Add(last with { Frame = duration });
        if (_keys.Count == 0 && source.Sample(_windowStart, out var held)) _keys.Add(held with { Frame = 0 });
        _keys.Sort((a, b) => a.Frame.CompareTo(b.Frame));
    }

    private bool PutWindowKey(ReplayCameraKeyframe key)
    {
        if (key.Frame > _windowEnd - _windowStart)
        { LastError = "Camera keyframe is outside the saved clip range."; return false; }
        bool changed = _windowSource!.Put(key with { Frame = checked(_windowStart + key.Frame) });
        LastError = _windowSource.LastError;
        if (changed) RebuildWindowKeys();
        return changed;
    }

    private bool RemoveWindowKeys(System.Collections.Generic.IEnumerable<uint> frames)
    {
        var selected = frames.Distinct().ToArray();
        if (selected.Length == 0 || selected.Any(frame => frame > _windowEnd - _windowStart
            || !_windowSource!.Keys.Any(key => key.Frame == _windowStart + frame)))
        {
            LastError = "Sampled clip boundaries preserve the original curve. Replace a boundary with an authored key before removing or moving it.";
            return false;
        }
        bool changed = _windowSource!.RemoveMany(selected.Select(frame => checked(_windowStart + frame)));
        LastError = _windowSource.LastError;
        if (changed) RebuildWindowKeys();
        return changed;
    }
}
