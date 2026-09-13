using System;
using MphRead.Combat;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    /// <summary>Bounded presentation-only ownership for the local visor impulse.</summary>
    internal sealed class VisorDamagePresentationState
    {
        internal const int DedupCapacity = 64;
        private readonly uint[] _eventIds = new uint[DedupCapacity];
        private readonly bool[] _occupied = new bool[DedupCapacity];
        private CombatActor _identity = CombatActor.None;
        private int _roomId = Int32.MinValue;
        private uint _latestTick;
        private Vector2 _latestDirection;
        private bool _hasDamage;

        public CombatActor Identity => _identity;
        public int RoomId => _roomId;
        public bool HasDamage => _hasDamage;

        public bool Observe(in CombatEvent value, CombatActor local, int roomId,
            Vector3 forward, Vector3 right)
        {
            Synchronize(local, roomId);
            if (!local.IsValid || value.Kind != CombatEventKind.Damage
                || value.Target != local || value.Health == 0)
                return false;

            int slot = (int)(value.Id % DedupCapacity);
            if (_occupied[slot] && _eventIds[slot] == value.Id) return false;
            _occupied[slot] = true;
            _eventIds[slot] = value.Id;

            if (!TryProject(value.Direction, forward, right,
                    out Vector2 screenDirection)) return false;
            if (!_hasDamage || value.Tick == _latestTick
                || Sequence32.IsNewer(value.Tick, _latestTick))
            {
                _latestTick = value.Tick;
                _latestDirection = screenDirection;
                _hasDamage = true;
            }
            return true;
        }

        public DamageVisorSample Sample(CombatActor local, int roomId,
            uint presentationTick, float renderFraction, DamageVisorProfile profile)
        {
            Synchronize(local, roomId);
            if (!float.IsFinite(renderFraction) || renderFraction < 0
                || renderFraction > 1)
                throw new ArgumentOutOfRangeException(nameof(renderFraction));
            if (!_hasDamage)
                return Inactive(profile.CenterClearRadius);

            uint age = CombatFeedback.Age(presentationTick, _latestTick);
            decimal elapsedTicks = ((decimal)age + (decimal)renderFraction)
                * TimeSpan.TicksPerSecond / SimTicks.Hz;
            TimeSpan elapsed = elapsedTicks >= TimeSpan.MaxValue.Ticks
                ? TimeSpan.MaxValue
                : TimeSpan.FromTicks(decimal.ToInt64(decimal.Floor(elapsedTicks)));
            return profile.Sample(elapsed, _latestDirection);
        }

        public void Synchronize(CombatActor local, int roomId)
        {
            if (local == _identity && roomId == _roomId) return;
            Reset();
            _identity = local;
            _roomId = roomId;
        }

        public void Reset()
        {
            Array.Clear(_occupied);
            _identity = CombatActor.None;
            _roomId = Int32.MinValue;
            _latestTick = 0;
            _latestDirection = Vector2.Zero;
            _hasDamage = false;
        }

        internal static bool TryProject(Vector3 direction, Vector3 forward,
            Vector3 right, out Vector2 screenDirection)
        {
            Vector3 horizontalForward = new(forward.X, 0, forward.Z);
            Vector3 horizontalRight = new(right.X, 0, right.Z);
            if (!Finite(direction) || !Finite(horizontalForward)
                || !Finite(horizontalRight)
                || horizontalForward.LengthSquared < 0.000001f
                || horizontalRight.LengthSquared < 0.000001f)
            {
                screenDirection = Vector2.Zero;
                return false;
            }
            horizontalForward.Normalize();
            horizontalRight.Normalize();
            float forwardAmount = Vector3.Dot(
                new Vector3(direction.X, 0, direction.Z), horizontalForward);
            float rightAmount = Vector3.Dot(
                new Vector3(direction.X, 0, direction.Z), horizontalRight);
            float lengthSquared = forwardAmount * forwardAmount
                + rightAmount * rightAmount;
            if (!float.IsFinite(lengthSquared) || lengthSquared < 0.000001f)
            {
                screenDirection = Vector2.Zero;
                return false;
            }
            float inverseLength = 1 / MathF.Sqrt(lengthSquared);
            // Positive Y is the top of the logical screen; the shader flips it
            // once when converting to its top-left texture coordinate system.
            screenDirection = new Vector2(rightAmount * inverseLength,
                forwardAmount * inverseLength);
            return true;
        }

        private static DamageVisorSample Inactive(float centerClearRadius)
            => new(Vector2.Zero, 0, 0, Vector3.Zero, 0, centerClearRadius);

        private static bool Finite(Vector3 value)
            => float.IsFinite(value.X) && float.IsFinite(value.Y)
                && float.IsFinite(value.Z);
    }

    public partial class PlayerPresentation
    {
        private readonly VisorDamagePresentationState _visorDamage = new();

        internal void ObserveVisorDamage(in CombatEvent value)
        {
            _visorDamage.Observe(value, Presentation.CombatFeedback.Local,
                _player._scene.RoomId, _player._gunVec1, _player._gunVec2);
        }

        internal void ResetVisorPresentation() => _visorDamage.Reset();

        internal RenderVisorState CaptureVisorState(uint presentationTick,
            float renderFraction, TimeSpan presentationTime)
        {
            CombatActor local = Presentation.CombatFeedback.Local;
            int roomId = _player._scene.RoomId;
            DamageVisorSample damage = _visorDamage.Sample(local, roomId,
                presentationTick, renderFraction, EnhancedVisorProfiles.Damage);
            float healthFraction = Math.Clamp(_player.Health
                / (float)Math.Max(_player.HealthMax, 1), 0, 1);
            ulong stablePlayerKey = local.IsValid
                ? local.ConnectionId ^ ((ulong)local.Life << 32) ^ local.Slot
                : (ulong)(uint)(_player.SlotIndex + 1);
            LowHealthVisorSample lowHealth = EnhancedVisorProfiles.LowHealth.Sample(
                presentationTime, healthFraction, stablePlayerKey);
            double seconds = PresentationTimeMath.Seconds(presentationTime);
            return new RenderVisorState(EnhancedVisorProfiles.Combat, damage,
                lowHealth,
                PresentationTimeMath.Fraction(seconds * 0.37),
                PresentationTimeMath.Fraction(seconds * 7.5));
        }
    }
}
