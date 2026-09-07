using System;
using System.Diagnostics;
using OpenTK.Mathematics;
namespace MphRead.Mods.Input
{
    /// <summary>One producer stream, non-consuming render reads, one fixed-step consumer.</summary>
    public sealed class RenderLookAccumulator
    {
        private readonly object _gate = new();
        private Vector2 _pending;
        private double _lastEvent;
        public const float MaxPendingPixels = 16384;
        public const double MaxAgeSeconds = 0.25;
        private static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        private void Expire(double now) { if (now - _lastEvent > MaxAgeSeconds) _pending = Vector2.Zero; }
        public void Add(float x, float y, double? seconds = null)
        {
            if (!float.IsFinite(x) || !float.IsFinite(y)) return;
            lock (_gate)
            {
                double now = seconds ?? Now;
                Expire(now);
                _pending.X = Math.Clamp(_pending.X + x, -MaxPendingPixels, MaxPendingPixels);
                _pending.Y = Math.Clamp(_pending.Y + y, -MaxPendingPixels, MaxPendingPixels);
                _lastEvent = now;
            }
        }
        public Vector2 Peek(double? seconds = null) { lock (_gate) { Expire(seconds ?? Now); return _pending; } }
        public Vector2 Consume(double? seconds = null) { lock (_gate) { Expire(seconds ?? Now); Vector2 value = _pending; _pending = Vector2.Zero; return value; } }
        public void Reset() { lock (_gate) _pending = Vector2.Zero; }
        /// <summary>Rotates only the render camera; gameplay aim and camera data remain owned by the fixed step.</summary>
        public static Matrix4 ApplyCameraLook(Matrix4 camera, Vector2 aim, float simulationPitch)
        {
            aim.Y = Math.Clamp(simulationPitch + aim.Y, -85, 85) - simulationPitch;
            Vector3 position = camera.Row3.Xyz;
            camera.Row3.Xyz = Vector3.Zero;
            camera = Matrix4.CreateRotationX(MathHelper.DegreesToRadians(aim.Y)) * camera
                * Matrix4.CreateRotationY(MathHelper.DegreesToRadians(aim.X));
            camera.Row3.Xyz = position;
            return camera;
        }
        public static Vector2 AimDegrees(Vector2 raw, float sensitivity, bool invertX, bool invertY, float zoomScale = 1)
            => new Vector2(-raw.X * (invertX ? -1 : 1), -raw.Y * (invertY ? -1 : 1)) * (sensitivity * zoomScale / 4);
    }
}
