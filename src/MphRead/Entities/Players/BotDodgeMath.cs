using System;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    /// <summary>Closest-approach prediction for physically visible incoming projectiles.</summary>
    internal static class BotDodgeMath
    {
        internal static bool ThreatensPosition(Vector3 origin, Vector3 relativeVelocity,
            Vector3 target, float radius, float horizonTicks,
            out float arrivalTicks, out Vector3 closest)
        {
            arrivalTicks = 0;
            closest = origin;
            if (!float.IsFinite(origin.X) || !float.IsFinite(origin.Y) || !float.IsFinite(origin.Z)
                || !float.IsFinite(relativeVelocity.X) || !float.IsFinite(relativeVelocity.Y)
                || !float.IsFinite(relativeVelocity.Z)
                || !float.IsFinite(target.X) || !float.IsFinite(target.Y) || !float.IsFinite(target.Z)
                || !float.IsFinite(radius) || !float.IsFinite(horizonTicks)
                || radius <= 0 || horizonTicks <= 0)
            {
                return false;
            }

            float velocitySqr = relativeVelocity.LengthSquared;
            if (velocitySqr < 0.0001f)
            {
                return false;
            }
            float t = Vector3.Dot(target - origin, relativeVelocity) / velocitySqr;
            if (t < 0 || t > horizonTicks)
            {
                return false;
            }
            Vector3 predicted = origin + relativeVelocity * t;
            if ((predicted - target).LengthSquared > radius * radius)
            {
                return false;
            }
            closest = predicted;
            arrivalTicks = t;
            return true;
        }
    }
}
