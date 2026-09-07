using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network
{
    /// <summary>A shot-local stream: recycling a projectile or playing an effect cannot change later pellets.</summary>
    public struct BeamSpread
    {
        private uint _state;
        public BeamSpread(uint seed) { _state = seed; }

        public Vector3 Next(Vector3 direction, Vector3 up, Vector3 right, uint maxSpread, float speed)
            => Velocity(direction, up, right, speed, Rng.CallRng(ref _state, maxSpread),
                Rng.CallRng(ref _state, 0x168000));

        public static Vector3 Velocity(Vector3 direction, Vector3 up, Vector3 right, float speed,
            uint polarSample, uint azimuthSample)
        {
            float angle1 = MathHelper.DegreesToRadians(polarSample / 4096f);
            float angle2 = MathHelper.DegreesToRadians(azimuthSample / 4096f);
            float sin1 = MathF.Sin(angle1), cos1 = MathF.Cos(angle1);
            float sin2 = MathF.Sin(angle2), cos2 = MathF.Cos(angle2);
            return new Vector3(
                direction.X * cos1 + (up.X * cos2 + right.X * sin2) * sin1,
                direction.Y * cos1 + (up.Y * cos2 + right.Y * sin2) * sin1,
                direction.Z * cos1 + (up.Z * cos2 + right.Z * sin2) * sin1) * speed;
        }
    }
}
