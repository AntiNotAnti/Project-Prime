using System;
using OpenTK.Mathematics;

namespace MphRead.Entities;

/// <summary>
/// Pure deterministic geometry for a bot's imperfect observations. Never uses a
/// live hidden target transform when computing later contact positions.
/// </summary>
internal static class BotPerceptionMath
{
    internal static Vector3 QuantizedSoundCue(Vector3 listener, Vector3 emitter)
    {
        if (!Finite(listener) || !Finite(emitter))
        {
            return listener;
        }

        Vector3 delta = emitter - listener;
        float horizontal = MathF.Sqrt(delta.X * delta.X + delta.Z * delta.Z);
        if (horizontal < 0.001f)
        {
            return listener;
        }

        // 22.5-degree sectors and six-unit distance bands deliberately prevent
        // gunfire from becoming perfect positional radar through geometry.
        const float angleStep = MathF.PI / 8f;
        float angle = MathF.Atan2(delta.Z, delta.X);
        float quantizedAngle = MathF.Round(angle / angleStep) * angleStep;
        float quantizedRadius = MathF.Max(3f, MathF.Round(horizontal / 6f) * 6f);
        float height = Math.Clamp(MathF.Round(delta.Y / 4f) * 4f, -8f, 8f);
        return listener + new Vector3(
            MathF.Cos(quantizedAngle) * quantizedRadius, height,
            MathF.Sin(quantizedAngle) * quantizedRadius);
    }

    internal static Vector3 RememberedPosition(Vector3 lastSeenPosition,
        Vector3 lastSeenVelocity, int unseenFrames, int maxPredictFrames = 12)
    {
        if (!Finite(lastSeenPosition) || !Finite(lastSeenVelocity))
        {
            return lastSeenPosition;
        }
        // Native Speed uses twice the 60-Hz movement step. Only extrapolate a
        // handful of frames, then freeze the memory instead of wall-tracking.
        int frames = Math.Clamp(unseenFrames, 0, maxPredictFrames);
        Vector3 travel = lastSeenVelocity * (frames * 0.5f);
        float maxTravel = frames * 0.30f;
        if (travel.LengthSquared > maxTravel * maxTravel && maxTravel > 0)
        {
            travel = travel.Normalized() * maxTravel;
        }
        return lastSeenPosition + travel;
    }

    internal static Vector3 SmoothObservedVelocity(Vector3 previous, Vector3 observed,
        bool reacquired)
    {
        if (!Finite(observed)) return Vector3.Zero;
        if (reacquired || !Finite(previous)) return observed;
        // Reject one-frame jitter without missing genuine strafing changes.
        return previous * 0.60f + observed * 0.40f;
    }

    internal static float Confidence(int framesRemaining, int initialFrames, bool heardOnly)
    {
        if (initialFrames <= 0 || framesRemaining <= 0)
        {
            return 0;
        }
        float confidence = Math.Clamp(framesRemaining / (float)initialFrames, 0f, 1f);
        return heardOnly ? confidence * 0.42f : confidence;
    }

    private static bool Finite(Vector3 v)
        => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
