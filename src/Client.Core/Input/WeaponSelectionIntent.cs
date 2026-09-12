using System;

namespace MphRead.Mods.Input
{
    /// <summary>Input intent only. Actual equip success is observed from the unchanged simulation.</summary>
    public sealed class WeaponSelectionIntent
    {
        public const byte None = 255;
        public byte Desired { get; private set; } = None;
        public byte Previous { get; private set; } = None;
        private byte _current = None;
        private ulong _connection;
        private uint _life;
        public void Observe(byte equipped, bool alive, ulong connection, uint life)
        {
            if (!alive || connection != _connection || life != _life)
            {
                Desired = Previous = None;
                _current = equipped;
                _connection = connection;
                _life = life;
                return;
            }
            if (equipped != _current)
            {
                if (_current <= 8) Previous = _current;
                _current = equipped;
            }
            if (Desired == equipped) Desired = None;
        }
        public void Request(byte weapon, int availableMask)
        {
            if (weapon > 8 || (availableMask & (1 << weapon)) == 0) return;
            Desired = weapon == _current ? None : weapon;
        }
        public byte Pending(bool legal, int availableMask)
        {
            if (Desired <= 8 && (availableMask & (1 << Desired)) == 0) Desired = None;
            return legal ? Desired : None;
        }
        public void Cancel() => Desired = None;
    }

    public sealed class WeaponRadialSelection
    {
        private static readonly byte[] Order = { 0, 2, 1, 3, 4, 5, 6, 7, 8 };
        public bool Open { get; private set; }
        public byte Preview { get; private set; } = WeaponSelectionIntent.None;
        private bool _cancelled;
        public byte Update(bool held, bool cancel, float x, float y, float deadzone, int available)
        {
            if (!held)
            {
                byte result = Open && !_cancelled ? Preview : WeaponSelectionIntent.None;
                Open = _cancelled = false;
                Preview = WeaponSelectionIntent.None;
                return result;
            }
            if (!Open) { Preview = WeaponSelectionIntent.None; _cancelled = false; }
            Open = true;
            if (cancel) _cancelled = true;
            if (_cancelled) { Preview = WeaponSelectionIntent.None; return WeaponSelectionIntent.None; }
            if (float.IsFinite(x) && float.IsFinite(y) && x * x + y * y > deadzone * deadzone)
            {
                float angle = MathF.Atan2(x, y);
                if (angle < 0) angle += MathF.PI * 2;
                int wedge = (int)MathF.Floor(angle / (MathF.PI * 2 / 9) + .5f) % 9;
                byte weapon = Order[wedge];
                Preview = (available & (1 << weapon)) != 0 ? weapon : WeaponSelectionIntent.None;
            }
            return WeaponSelectionIntent.None;
        }
        public static OpenTK.Mathematics.Vector2 SectorPoint(int wedge, float fraction, float radiusX, float radiusY)
        {
            if (wedge < 0 || wedge >= 9 || fraction < 0 || fraction > 1) throw new ArgumentOutOfRangeException();
            float angle = (wedge - .5f + fraction) * (MathF.PI * 2 / 9);
            return new(MathF.Sin(angle) * radiusX, -MathF.Cos(angle) * radiusY);
        }
        public static byte WeaponAt(int wedge) => Order[wedge];
        public void Reset() { Open = _cancelled = false; Preview = WeaponSelectionIntent.None; }
    }
}
