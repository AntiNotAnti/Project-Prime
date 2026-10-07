using OpenTK.Mathematics;

namespace MphRead.Mods.Combat
{
    /// <summary>
    /// EU1.1 Dialanche collision samples. Visible rocks may animate at 60 Hz;
    /// combat samples at 30 Hz and consumes the prior native sample.
    /// </summary>
    internal sealed class DialancheCollisionHistory
    {
        internal readonly record struct Pose(Vector3 Left, Vector3 Right);
        private readonly record struct Sample(bool Valid, ulong Tick, Pose Pose);

        private Pose _initial;
        private Sample _previous;
        private Sample _latest;

        internal static bool IsNativeCollisionStep(ulong frame)
            => frame != 0 && (frame & 1UL) == 0;
        internal static ulong NativeTick(ulong frame) => frame / 2;

        internal void Reset(Vector3 position)
        {
            _initial = new Pose(position, position);
            _previous = default;
            _latest = default;
        }

        internal void Record(ulong tick, Vector3 left, Vector3 right)
        {
            if (_latest.Valid && tick < _latest.Tick) return;
            if (!_latest.Valid || tick != _latest.Tick) _previous = _latest;
            _latest = new Sample(true, tick, new Pose(left, right));
        }

        internal Pose PoseForHit(ulong tick)
        {
            if (_latest.Valid && _latest.Tick < tick) return _latest.Pose;
            if (_previous.Valid && _previous.Tick < tick) return _previous.Pose;
            return _initial;
        }

        internal void Translate(Vector3 delta)
        {
            _initial = new Pose(_initial.Left + delta, _initial.Right + delta);
            if (_previous.Valid)
                _previous = _previous with { Pose = new Pose(
                    _previous.Pose.Left + delta, _previous.Pose.Right + delta) };
            if (_latest.Valid)
                _latest = _latest with { Pose = new Pose(
                    _latest.Pose.Left + delta, _latest.Pose.Right + delta) };
        }
    }
}
