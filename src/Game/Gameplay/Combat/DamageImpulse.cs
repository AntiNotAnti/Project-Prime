using OpenTK.Mathematics;

namespace MphRead;

/// <summary>The engine's damage-direction velocity transform, shared by authority and local prediction.</summary>
public static class DamageImpulse
{
    public static Vector3 Apply(Vector3 speed, Vector3 direction, bool altForm, bool halfturret)
    {
        if (halfturret)
            return speed;
        if (altForm)
            return speed + (direction * 0.4f).WithY(0);

        Vector3 result = speed + direction.WithY(0);
        if (direction.Y <= 0)
        {
            result.Y += direction.Y;
        }
        else if (result.Y < 0.25f)
        {
            result.Y += direction.Y;
            if (result.Y > 0.25f) result.Y = 0.25f;
        }
        return result;
    }
}
