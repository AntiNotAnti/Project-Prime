using System;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Culling;
using MphRead.Mods.Render;
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

        private float _thirdPersonCameraDistance = float.MaxValue;
        private Vector3 _thirdPersonCameraOffset = Vector3.Zero;
        private double _thirdPersonCameraLastTick = double.NaN;
        private long _thirdPersonCameraLastGeneration;
        // The world ray is immutable between 60Hz simulation ticks. Cache its
        // expensive terrain trace, but reproject it for every rendered pose.
        private ulong _thirdPersonReticleTraceFrame = ulong.MaxValue;
        private Vector3 _thirdPersonReticleTraceMuzzle;
        private Vector3 _thirdPersonReticleTraceAim;
        private Vector3 _thirdPersonReticleTraceTarget;
        // A stationary shoulder pose submits the same wall ray many times at
        // 120/240Hz. Reuse only byte-identical rays from this simulation tick;
        // changed pose or world step always gets a fresh occlusion answer.
        private ulong _thirdPersonWallTraceFrame = ulong.MaxValue;
        private Vector3 _thirdPersonWallTraceStart;
        private Vector3 _thirdPersonWallTraceEnd;
        private float _thirdPersonWallTraceDistance;
        // Scene-owned presentation history, never written into the player,
        // match snapshot, replay or simulation camera.
        private readonly ThirdPersonFormCameraHandoff _thirdPersonFormHandoff = new();
        private Vector3 _thirdPersonRenderedEye;
        private bool _thirdPersonRenderedEyeValid;

        /// <summary>Near-body opacity is local, visual and never affects other Hunters.</summary>
        internal float ModThirdPersonLocalBodyOpacity { get; private set; } = 1f;

        /// <summary>
        /// A new life is a hard camera boundary. Present the first eligible
        /// third-person pose immediately, never an old corpse/intro viewpoint.
        /// </summary>
        internal void ModResetThirdPersonSpawnCamera(PlayerEntity player)
        {
            if (!ReferenceEquals(player, Players.Main)) return;
            _thirdPersonCameraDistance = float.MaxValue;
            _thirdPersonCameraLastTick = double.NaN;
            _thirdPersonWallTraceFrame = ulong.MaxValue;
            _thirdPersonReticleTraceFrame = ulong.MaxValue;
            _thirdPersonCameraOffset = Vector3.Zero;
            _thirdPersonRenderedEyeValid = false;
            ModThirdPersonCameraActive = false;
            ModThirdPersonLocalBodyOpacity = 1f;
            _thirdPersonFormHandoff.Reset();
        }

        /// <summary>True only for a live local third-person world draw.</summary>
        internal bool ModThirdPersonCameraActive { get; private set; }

        private void ModResetThirdPersonCameraFrame()
        {
            ModThirdPersonCameraActive = false;
            ModThirdPersonLocalBodyOpacity = 1f;
            _thirdPersonCameraOffset = Vector3.Zero;
            _thirdPersonRenderedEyeValid = false;
            if (_cameraMode != CameraMode.Player)
                _thirdPersonFormHandoff.Reset();
        }

        /// <summary>
        /// Project a projectile's existing native muzzle-to-aim line into the
        /// *displayed* over-the-shoulder camera. This affects HUD only, not the
        /// shot sent to the server or the historical replay reticle.
        /// </summary>
        internal bool ModTryGetThirdPersonShotReticle(Vector3 muzzle, Vector3 aim,
            out Vector2 position)
        {
            position = new Vector2(0.5f, 0.5f);
            if (!ModThirdPersonCameraActive || _cameraMode != CameraMode.Player
                || !GameState.Multiplayer || !GameState.ThirdPersonCamera
                || Services.IsReplica || Mods.Headless.Active
                || this.Players.Main.CameraType != CameraType.First
                || !ThirdPersonReticleProjection.TryShotRay(muzzle, aim,
                    out Vector3 direction))
                return false;

            if (_thirdPersonReticleTraceFrame != FrameCount
                || _thirdPersonReticleTraceMuzzle != muzzle
                || _thirdPersonReticleTraceAim != aim)
            {
                _thirdPersonReticleTraceFrame = FrameCount;
                _thirdPersonReticleTraceMuzzle = muzzle;
                _thirdPersonReticleTraceAim = aim;
                Vector3 far = ThirdPersonReticleProjection.Target(muzzle,
                    direction, 1f);
                CollisionResult impact = default;
                float contact = CollisionDetection.CheckBetweenPoints(muzzle, far,
                    TestFlags.Beams, this, ref impact)
                    ? impact.Distance : 1f;
                _thirdPersonReticleTraceTarget = ThirdPersonReticleProjection.Target(
                    muzzle, direction, contact);
            }
            return ThirdPersonReticleProjection.TryProject(
                _thirdPersonReticleTraceTarget, _viewMatrix,
                _perspectiveMatrix, out position);
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
                _thirdPersonCameraLastTick = double.NaN;
                _thirdPersonRenderedEyeValid = false;
                _thirdPersonFormHandoff.Reset();
                return;
            }

            float determinant = view.Determinant;
            if (!float.IsFinite(determinant) || MathF.Abs(determinant) < 0.001f)
            {
                _thirdPersonCameraDistance = float.MaxValue;
                _thirdPersonCameraLastTick = double.NaN;
                _thirdPersonRenderedEyeValid = false;
                _thirdPersonFormHandoff.Reset();
                return;
            }

            // The native camera already handles the Morph Ball's own third-person
            // movement. Preserve it. Only biped/First receives the chase offset;
            // unlike the old code, Unmorphing does NOT temporarily disable it.
            // The presentation-only handoff below bridges both CameraType changes.
            // Resolve the native eye exactly once. The previous implementation
            // inverted this matrix again at the end of every frame and also
            // re-read the first-person camera from Renderer.
            Matrix4 nativeInverse = view.Inverted();
            Vector3 nativeEye = nativeInverse.Row3.Xyz;
            Vector3 desiredEye = nativeEye;
            double clock = Mods.Render.FrameTiming.Active
                ? Mods.Render.FrameTiming.TotalSteps + Mods.Render.FrameTiming.Alpha
                : GlobalElapsedTime * Mods.Render.FrameTiming.SimulationHz;
            long generation = Mods.Render.FrameTiming.Active
                ? Mods.Render.FrameTiming.PresentationGeneration : 0;
            if (ThirdPersonCameraPolicy.ShowShoulder(
                    true, player.CameraType, player.IsAltForm))
            {
                Vector3 eye = nativeEye;
                Vector3 desired = nativeInverse.Row2.Xyz.Normalized() * ThirdPersonBack
                    + nativeInverse.Row1.Xyz.Normalized() * ThirdPersonUp
                    + nativeInverse.Row0.Xyz.Normalized() * ThirdPersonSide;
                float distance = desired.Length;
                if (ThirdPersonFormCameraHandoff.IsFinite(eye)
                    && float.IsFinite(distance) && distance > 0.001f)
                {
                    Vector3 direction = desired / distance;
                    Vector3 endpoint = eye
                        + direction * (distance + ThirdPersonWallMargin);
                    float allowed;
                    if (_thirdPersonWallTraceFrame == FrameCount
                        && _thirdPersonWallTraceStart == eye
                        && _thirdPersonWallTraceEnd == endpoint)
                    {
                        allowed = _thirdPersonWallTraceDistance;
                    }
                    else
                    {
                        allowed = distance;
                        CollisionResult result = default;
                        if (CollisionDetection.CheckBetweenPoints(eye, endpoint,
                                TestFlags.Players, this, ref result))
                        {
                            allowed = Math.Clamp(result.Distance
                                * (distance + ThirdPersonWallMargin)
                                - ThirdPersonWallMargin, 0, distance);
                        }
                        _thirdPersonWallTraceFrame = FrameCount;
                        _thirdPersonWallTraceStart = eye;
                        _thirdPersonWallTraceEnd = endpoint;
                        _thirdPersonWallTraceDistance = allowed;
                    }

                    // Snap toward walls, but release using elapsed *simulation*
                    // time rather than a fixed percentage for every draw.
                    // The old 0.15-per-draw relaxed ~4x faster at 240Hz.
                    if (_thirdPersonCameraLastGeneration != generation
                        || !double.IsFinite(_thirdPersonCameraLastTick)
                        || clock < _thirdPersonCameraLastTick
                        || clock - _thirdPersonCameraLastTick > 60)
                        _thirdPersonCameraDistance = float.MaxValue;
                    double elapsedTicks = double.IsFinite(_thirdPersonCameraLastTick)
                        ? clock - _thirdPersonCameraLastTick : 1d;
                    _thirdPersonCameraLastTick = clock;
                    _thirdPersonCameraLastGeneration = generation;
                    float release = ThirdPersonReticleProjection.ReleaseFraction(elapsedTicks);
                    _thirdPersonCameraDistance = allowed < _thirdPersonCameraDistance
                        ? allowed
                        : _thirdPersonCameraDistance + (allowed - _thirdPersonCameraDistance) * release;

                    // Never abruptly restore the first-person camera just
                    // because a spawn pad or wall compresses the shoulder.
                    // Keep the same camera mode, fade the local body near
                    // the lens, and restore full opacity as clearance opens.
                    ModThirdPersonCameraActive = true;
                    ModThirdPersonLocalBodyOpacity =
                        ThirdPersonCameraPolicy.LocalBodyOpacity(_thirdPersonCameraDistance);
                    Vector3 offset = direction * _thirdPersonCameraDistance;
                    desiredEye = nativeEye + offset;
                    view = Matrix4.CreateTranslation(-offset) * view;
                }
            }
            else
            {
                _thirdPersonCameraDistance = float.MaxValue;
                _thirdPersonCameraLastTick = double.NaN;
            }

            // Reuse the same display clock for morph-camera interpolation.
            view = _thirdPersonFormHandoff.Present(view, desiredEye,
                player.CameraType, clock, generation,
                out bool blending, out Vector3 presentedEye);

            // A bridge between two individually collision-safe camera positions
            // can still cut through a wall around a corner. Clamp the intermediate
            // eye against the world, never the player's authoritative camera.
            if (blending)
            {
                Matrix4 inv = view.Inverted();
                Vector3 source = player.Volume.SpherePosition;
                Vector3 ray = presentedEye - source;
                float length = ray.Length;
                if (ThirdPersonFormCameraHandoff.IsFinite(presentedEye)
                    && ThirdPersonFormCameraHandoff.IsFinite(source)
                    && float.IsFinite(length) && length > 0.001f)
                {
                    CollisionResult result = default;
                    if (CollisionDetection.CheckBetweenPoints(source, presentedEye,
                            TestFlags.Players, this, ref result))
                    {
                        float reach = Math.Clamp(length * result.Distance
                            - ThirdPersonWallMargin, 0f, length);
                        if (reach < length)
                        {
                            Vector3 clippedEye = source + ray * (reach / length);
                            view = Matrix4.LookAt(clippedEye,
                                clippedEye - inv.Row2.Xyz, inv.Row1.Xyz);
                            presentedEye = clippedEye;
                            _thirdPersonFormHandoff.Remember(view, clippedEye);
                        }
                    }
                }
            }

            // Keep culling, billboards and lighting on exactly the same pose as
            // the interpolated view. The native viewmodel/aim pose stays intact.
            _thirdPersonCameraOffset = presentedEye - nativeEye;
            // Only bypass the ordinary first-person camera position reader
            // when the third-person view or form transition actually owns it.
            _thirdPersonRenderedEye = presentedEye;
            _thirdPersonRenderedEyeValid = ModThirdPersonCameraActive || blending;
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

        private Vector3 _lastPresentedEye;

        internal void Remember(Matrix4 presented) =>
            Remember(presented, presented.Inverted().Row3.Xyz);

        internal void Remember(Matrix4 presented, Vector3 eye)
        {
            _lastPresentedView = presented;
            _lastPresentedEye = eye;
        }

        internal static bool IsFinite(Vector3 v) =>
            float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

        private static bool ValidView(Matrix4 view) =>
            float.IsFinite(view.Determinant) && MathF.Abs(view.Determinant) >= 0.001f;

        internal Matrix4 Present(Matrix4 desired, CameraType cameraType,
            double ticks, long generation, out bool blending) =>
            Present(desired, desired.Inverted().Row3.Xyz, cameraType,
                ticks, generation, out blending, out _);

        // Caller has already resolved the native camera eye; this overload
        // avoids the prior two inverse-matrix operations per draw.
        internal Matrix4 Present(Matrix4 desired, Vector3 desiredEye,
            CameraType cameraType, double ticks, long generation,
            out bool blending, out Vector3 presentedEye)
        {
            blending = false;
            presentedEye = desiredEye;
            if (!ValidView(desired) || !IsFinite(desiredEye)
                || !double.IsFinite(ticks))
            {
                Reset();
                return desired;
            }

            if (!_initialized || _lastGeneration != generation
                || ticks < _lastTick || ticks - _lastTick > 60
                || (_lastPresentedEye - desiredEye).LengthSquared > TeleportDistanceSquared)
            {
                _initialized = true;
                _blending = false;
                _lastGeneration = generation;
                _lastCameraType = cameraType;
                _lastTick = ticks;
                Remember(desired, desiredEye);
                return desired;
            }

            if (cameraType != _lastCameraType)
            {
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
                    _blending = false;
                else
                {
                    float eased = fraction * fraction * (3f - 2f * fraction);
                    if (TryBlend(_fromView, desired, eased,
                            out Matrix4 halfway, out Vector3 midwayEye))
                    {
                        presented = halfway;
                        presentedEye = midwayEye;
                        blending = true;
                    }
                }
            }

            _lastCameraType = cameraType;
            _lastTick = ticks;
            Remember(presented, presentedEye);
            return presented;
        }

        private static bool TryBlend(Matrix4 from, Matrix4 to,
            float fraction, out Matrix4 view, out Vector3 eye)
        {
            view = to;
            eye = Vector3.Zero;
            if (!ValidView(from) || !ValidView(to)) return false;
            Matrix4 fromInverse = from.Inverted();
            Matrix4 toInverse = to.Inverted();
            eye = Vector3.Lerp(fromInverse.Row3.Xyz,
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
