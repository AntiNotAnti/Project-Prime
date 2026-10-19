using System;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Culling;
using OpenTK.Mathematics;

namespace MphRead
{
    /// <summary>
    /// Render-only chase camera adapted from Second Hunt's HostCameraOffset.
    /// Simulation, hit traces, muzzle origins, recoil and input keep their
    /// canonical first-person view. No networked player state is altered.
    /// </summary>
    public partial class Scene
    {
        private const float ThirdPersonBack = 2.4f;
        private const float ThirdPersonUp = 0.35f;
        private const float ThirdPersonSide = 0.5f;
        private const float ThirdPersonWallMargin = 0.3f;
        private const float ThirdPersonBodyClearance = 0.8f;

        private float _thirdPersonCameraDistance = float.MaxValue;
        private Vector3 _thirdPersonCameraOffset = Vector3.Zero;
        // Scene-owned presentation history, never written into the player,
        // match snapshot, replay or simulation camera.
        private readonly ThirdPersonFormCameraHandoff _thirdPersonFormHandoff = new();

        /// <summary>True only for a live local third-person world draw.</summary>
        internal bool ModThirdPersonCameraActive { get; private set; }

        private void ModResetThirdPersonCameraFrame()
        {
            ModThirdPersonCameraActive = false;
            _thirdPersonCameraOffset = Vector3.Zero;
            if (_cameraMode != CameraMode.Player)
                _thirdPersonFormHandoff.Reset();
        }

        /// <summary>Shift only the render matrix, never CameraInfo or simulation.</summary>
        private void ModApplyThirdPersonCamera(ref Matrix4 view)
        {
            PlayerEntity player = this.Players.Main;
            // Cutscenes, spectators, replays and dead or unspawned players retain
            // their native camera, without inheriting any prior form transition.
            if (!GameState.Multiplayer || !GameState.ThirdPersonCamera
                || Services.IsReplica || Mods.Headless.Active
                || Mods.KillCam.Active || Mods.SpectatorMode.IsSpectating
                || Mods.Network.DemoPlayback.IsActive
                || GameState.MatchState != MatchState.InProgress
                || this.CameraSequences.Current != null
                || player.Health <= 0 || !player.LoadFlags.TestFlag(LoadFlags.Spawned))
            {
                _thirdPersonCameraDistance = float.MaxValue;
                _thirdPersonFormHandoff.Reset();
                return;
            }

            float determinant = view.Determinant;
            if (!float.IsFinite(determinant) || MathF.Abs(determinant) < 0.001f)
            {
                _thirdPersonCameraDistance = float.MaxValue;
                _thirdPersonFormHandoff.Reset();
                return;
            }

            // The native camera already handles the Morph Ball's own third-person
            // movement. Preserve it. Only biped/First receives the chase offset;
            // unlike the old code, Unmorphing does NOT temporarily disable it.
            // The presentation-only handoff below bridges both CameraType changes.
            Matrix4 nativeView = view;
            if (player.CameraType == CameraType.First && !player.IsAltForm)
            {
                Matrix4 inverse = view.Inverted();
                Vector3 eye = inverse.Row3.Xyz;
                Vector3 desired = inverse.Row2.Xyz.Normalized() * ThirdPersonBack
                    + inverse.Row1.Xyz.Normalized() * ThirdPersonUp
                    + inverse.Row0.Xyz.Normalized() * ThirdPersonSide;
                float distance = desired.Length;
                if (ThirdPersonFormCameraHandoff.IsFinite(eye)
                    && float.IsFinite(distance) && distance > 0.001f)
                {
                    Vector3 direction = desired / distance;
                    float allowed = distance;
                    CollisionResult result = default;
                    if (CollisionDetection.CheckBetweenPoints(eye,
                            eye + direction * (distance + ThirdPersonWallMargin),
                            TestFlags.Players, this, ref result))
                    {
                        allowed = Math.Clamp(result.Distance * (distance + ThirdPersonWallMargin)
                            - ThirdPersonWallMargin, 0, distance);
                    }

                    // Immediate pull-in prevents seeing through walls; easing out
                    // is independent of the underlying 60 Hz aim and input.
                    _thirdPersonCameraDistance = allowed < _thirdPersonCameraDistance
                        ? allowed
                        : _thirdPersonCameraDistance + (allowed - _thirdPersonCameraDistance) * 0.15f;

                    if (_thirdPersonCameraDistance >= ThirdPersonBodyClearance)
                    {
                        ModThirdPersonCameraActive = true;
                        Vector3 offset = direction * _thirdPersonCameraDistance;
                        view = Matrix4.CreateTranslation(-offset) * view;
                    }
                }
            }
            else
            {
                _thirdPersonCameraDistance = float.MaxValue;
            }

            // TotalSteps + Alpha follows real presentation frames (including
            // 120/240Hz draws) without advancing physics. Android uses the same
            // clock; tools driving Scene directly use its 60Hz simulation time.
            double clock = Mods.Render.FrameTiming.Active
                ? Mods.Render.FrameTiming.TotalSteps + Mods.Render.FrameTiming.Alpha
                : GlobalElapsedTime * Mods.Render.FrameTiming.SimulationHz;
            long generation = Mods.Render.FrameTiming.Active
                ? Mods.Render.FrameTiming.PresentationGeneration : 0;
            view = _thirdPersonFormHandoff.Present(view, player.CameraType, clock,
                generation, out bool blending);

            // A bridge between two individually collision-safe camera positions
            // can still cut through a wall around a corner. Clamp the intermediate
            // eye against the world, never the player's authoritative camera.
            if (blending)
            {
                Matrix4 inv = view.Inverted();
                Vector3 renderedEye = inv.Row3.Xyz;
                Vector3 source = player.Volume.SpherePosition;
                Vector3 ray = renderedEye - source;
                float length = ray.Length;
                if (ThirdPersonFormCameraHandoff.IsFinite(renderedEye)
                    && ThirdPersonFormCameraHandoff.IsFinite(source)
                    && float.IsFinite(length) && length > 0.001f)
                {
                    CollisionResult result = default;
                    if (CollisionDetection.CheckBetweenPoints(source, renderedEye,
                            TestFlags.Players, this, ref result))
                    {
                        float reach = Math.Clamp(length * result.Distance
                            - ThirdPersonWallMargin, 0f, length);
                        if (reach < length)
                        {
                            Vector3 clippedEye = source + ray * (reach / length);
                            view = Matrix4.LookAt(clippedEye,
                                clippedEye - inv.Row2.Xyz, inv.Row1.Xyz);
                            _thirdPersonFormHandoff.Remember(view);
                        }
                    }
                }
            }

            // Keep culling, billboards and lighting on exactly the same pose as
            // the interpolated view. The native viewmodel/aim pose stays intact.
            _thirdPersonCameraOffset = view.Inverted().Row3.Xyz
                - nativeView.Inverted().Row3.Xyz;
        }
    }

    /// <summary>
    /// A render-only bridge between biped shoulder and the native Morph Ball
    /// cameras. The native switch starts from the first-person *simulation* eye,
    /// not from the displayed shoulder, so its own easing cannot prevent a snap.
    /// This retains the last displayed pose and eases the two endpoints without
    /// changing CameraInfo, form flags, muzzle traces, or network state.
    /// </summary>
    internal sealed class ThirdPersonFormCameraHandoff
    {
        internal const double DurationTicks = 18.0; // 0.30 seconds at 60Hz
        private const float TeleportDistanceSquared = 16f * 16f;

        private bool _initialized;
        private bool _blending;
        private CameraType _lastCameraType;
        private Matrix4 _lastPresentedView;
        private Matrix4 _fromView;
        private double _startTick;
        private double _lastTick;
        private long _lastGeneration;

        internal void Reset()
        {
            _initialized = false;
            _blending = false;
        }

        internal void Remember(Matrix4 presented) => _lastPresentedView = presented;

        internal static bool IsFinite(Vector3 v) =>
            float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

        private static bool ValidView(Matrix4 view) =>
            float.IsFinite(view.Determinant) && MathF.Abs(view.Determinant) >= 0.001f;

        internal Matrix4 Present(Matrix4 desired, CameraType cameraType,
            double ticks, long generation, out bool blending)
        {
            blending = false;
            if (!ValidView(desired) || !double.IsFinite(ticks))
            {
                Reset();
                return desired;
            }

            if (!_initialized || _lastGeneration != generation
                || ticks < _lastTick || ticks - _lastTick > 60
                || (_lastPresentedView.Inverted().Row3.Xyz
                    - desired.Inverted().Row3.Xyz).LengthSquared > TeleportDistanceSquared)
            {
                _initialized = true;
                _blending = false;
                _lastGeneration = generation;
                _lastCameraType = cameraType;
                _lastTick = ticks;
                _lastPresentedView = desired;
                return desired;
            }

            if (cameraType != _lastCameraType)
            {
                // Reversing the morph while a transition is underway starts
                // from the actual picture, not the former target or native eye.
                _fromView = _lastPresentedView;
                _startTick = ticks;
                _blending = true;
            }

            Matrix4 presented = desired;
            if (_blending)
            {
                float fraction = (float)Math.Clamp(
                    (ticks - _startTick) / DurationTicks, 0, 1);
                if (fraction >= 1f)
                {
                    _blending = false;
                }
                else
                {
                    float eased = fraction * fraction * (3f - 2f * fraction);
                    if (TryBlend(_fromView, desired, eased, out Matrix4 halfway))
                    {
                        presented = halfway;
                        blending = true;
                    }
                }
            }

            _lastCameraType = cameraType;
            _lastTick = ticks;
            _lastPresentedView = presented;
            return presented;
        }

        private static bool TryBlend(Matrix4 from, Matrix4 to,
            float fraction, out Matrix4 view)
        {
            view = to;
            if (!ValidView(from) || !ValidView(to)) return false;
            Matrix4 fromInverse = from.Inverted();
            Matrix4 toInverse = to.Inverted();
            Vector3 eye = Vector3.Lerp(fromInverse.Row3.Xyz,
                toInverse.Row3.Xyz, fraction);
            if (!CameraInfo.ModInterpolateDirection(
                    -fromInverse.Row2.Xyz, -toInverse.Row2.Xyz,
                    fraction, out Vector3 forward)
                || !IsFinite(eye))
                return false;
            Vector3 up = Vector3.Lerp(fromInverse.Row1.Xyz,
                toInverse.Row1.Xyz, fraction);
            up -= forward * Vector3.Dot(up, forward);
            if (!IsFinite(up) || up.LengthSquared < 0.000001f)
            {
                Vector3 reference = MathF.Abs(forward.Y) < 0.99f
                    ? Vector3.UnitY : Vector3.UnitZ;
                up = reference - forward * Vector3.Dot(reference, forward);
            }
            if (!IsFinite(up) || up.LengthSquared < 0.000001f) return false;
            view = Matrix4.LookAt(eye, eye + forward, up.Normalized());
            return ValidView(view);
        }
    }
}
