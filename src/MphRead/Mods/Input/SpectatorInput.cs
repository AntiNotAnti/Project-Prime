using System;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
namespace MphRead.Mods.Input
{
    // A focus transfer discards hardware ownership until every held key/button
    // has been released. This also prevents a held fire button becoming a new
    // gameplay press when the window regains focus.
    internal sealed class HardwareFocusLatch
    {
        private bool _awaitNeutral;
        public void LoseFocus() => _awaitNeutral = true;
        public bool Poll(bool focused, bool anyKey, bool anyButton)
        {
            if (!focused) { _awaitNeutral = true; return false; }
            if (_awaitNeutral && !anyKey && !anyButton) _awaitNeutral = false;
            return !_awaitNeutral;
        }
    }

    internal static class SpectatorCameraMath
    {
        // Match the normal hunter mouse's four pixels per degree. Free view
        // has no hunter zoom, so only the ordinary sensitivity applies.
        public static (float Yaw, float Pitch) MouseLook(float x, float y)
            => (MathHelper.DegreesToRadians(x / 4f * InputSettings.MouseSensitivity
                    * (InputSettings.InvertMouseX ? -1 : 1)),
                MathHelper.DegreesToRadians(-y / 4f * InputSettings.MouseSensitivity
                    * (InputSettings.InvertMouseY ? -1 : 1)));

        public static Keys AscendKey(bool spectator, bool replay)
            => spectator || replay ? Keys.E : Keys.Space;

        public static void Rotate(ref Vector3 facing, out Vector3 right, out Vector3 up,
            float yawStep, float pitchStep, bool freeCamera, Vector3? previousRight = null)
        {
            if (!float.IsFinite(facing.X) || !float.IsFinite(facing.Y)
                || !float.IsFinite(facing.Z) || facing.LengthSquared < .0001f)
                facing = -Vector3.UnitZ;
            facing = facing.Normalized();
            float yaw;
            if (facing.X * facing.X + facing.Z * facing.Z < .00000001f)
            {
                // Facing has no reliable yaw at the poles. Preserve the last
                // horizontal right axis rather than letting atan2(0, 0), or
                // tiny floating-point noise, snap the camera to another yaw.
                Vector3 prior = previousRight ?? Vector3.UnitX;
                if (!float.IsFinite(prior.X) || !float.IsFinite(prior.Z)
                    || prior.X * prior.X + prior.Z * prior.Z < .00000001f)
                    prior = Vector3.UnitX;
                yaw = MathF.Atan2(prior.Z, prior.X);
            }
            else yaw = MathF.Atan2(facing.X, -facing.Z);
            yaw += yawStep;
            // Keep the free camera inside the hunter's normal safe pitch range.
            float limit = freeCamera ? MathF.Asin(.985f) : MathF.PI / 2 - .000001f;
            float pitch = Math.Clamp(MathF.Asin(Math.Clamp(facing.Y, -1f, 1f)) + pitchStep, -limit, limit);
            facing = new Vector3(MathF.Cos(pitch) * MathF.Sin(yaw), MathF.Sin(pitch),
                -MathF.Cos(pitch) * MathF.Cos(yaw));
            // Derive right from yaw, avoiding a shrinking cross product near
            // vertical. All three camera axes remain unit length.
            right = new Vector3(MathF.Cos(yaw), 0, MathF.Sin(yaw));
            up = Vector3.Cross(right, facing).Normalized();
        }
    }
    public readonly record struct SpectatorInput(bool NextPlayer, bool PreviousPlayer, bool ToggleView,
        bool Scoreboard, bool OpenMenu, float MoveX, float MoveY, float LookX, float LookY, float Ascend, float Descend)
    {
        internal static (float X, float Y) CameraLook(float aimX, float aimY)
            // Gameplay aim uses a negated horizontal convention because UpdateAimX
            // consumes turn in player space. The roam camera's positive yaw turns
            // right, so convert exactly once at the spectator boundary.
            => (-aimX, aimY);

        public static SpectatorInput ReadController(bool replay = false)
        {
            if (!GamepadContexts.Focused || GamepadContexts.Current != GamepadContext.Gameplay) return default;
            var pad = GamepadInput.State;
            var move = GamepadAnalog.ApplyRadialDeadZone(pad.LeftX, pad.LeftY, GamepadOptions.LeftInner, GamepadOptions.LeftOuter);
            var look = CameraLook(GamepadInput.AimDeltaX, GamepadInput.AimDeltaY);
            return new(GamepadInput.TakePress(GamepadButtons.RightBumper), GamepadInput.TakePress(GamepadButtons.LeftBumper),
                GamepadInput.TakePress(GamepadButtons.Y), pad.Down(GamepadButtons.Back), GamepadInput.TakePress(GamepadButtons.Start),
                move.X, move.Y, look.X, look.Y,
                pad.RightTrigger, pad.LeftTrigger);
        }
        public void ApplyView(bool replay = false)
        {
            if (NextPlayer) SpectatorMode.CycleNext();
            if (PreviousPlayer) SpectatorMode.CyclePrevious();
            if (ToggleView) SpectatorMode.ToggleView();
        }
    }
}
