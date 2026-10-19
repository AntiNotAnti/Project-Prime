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

        /// <summary>True only for a live local third-person world draw.</summary>
        internal bool ModThirdPersonCameraActive { get; private set; }

        private void ModResetThirdPersonCameraFrame()
        {
            ModThirdPersonCameraActive = false;
            _thirdPersonCameraOffset = Vector3.Zero;
        }

        /// <summary>Shift only the render matrix, never CameraInfo or simulation.</summary>
        private void ModApplyThirdPersonCamera(ref Matrix4 view)
        {
            PlayerEntity player = this.Players.Main;
            if (!GameState.Multiplayer || !GameState.ThirdPersonCamera
                || Services.IsReplica || Mods.Headless.Active
                || Mods.KillCam.Active || Mods.SpectatorMode.IsSpectating
                || Mods.Network.DemoPlayback.IsActive
                || GameState.MatchState != MatchState.InProgress
                || this.CameraSequences.Current != null
                || player.CameraType != CameraType.First
                || player.IsAltForm || player.IsMorphing || player.IsUnmorphing
                || player.Health <= 0 || !player.LoadFlags.TestFlag(LoadFlags.Spawned))
            {
                _thirdPersonCameraDistance = float.MaxValue;
                return;
            }

            float determinant = view.Determinant;
            if (!float.IsFinite(determinant) || MathF.Abs(determinant) < 0.001f)
            {
                _thirdPersonCameraDistance = float.MaxValue;
                return;
            }

            // Build offset from the same high-refresh, late-latched view that
            // draws the world. An extra simulation camera would reintroduce
            // the camera/cannon desynchronization fixed in Prime.
            Matrix4 inverse = view.Inverted();
            Vector3 eye = inverse.Row3.Xyz;
            Vector3 desired = inverse.Row2.Xyz.Normalized() * ThirdPersonBack
                + inverse.Row1.Xyz.Normalized() * ThirdPersonUp
                + inverse.Row0.Xyz.Normalized() * ThirdPersonSide;
            float distance = desired.Length;
            if (!float.IsFinite(eye.X) || !float.IsFinite(eye.Y) || !float.IsFinite(eye.Z)
                || !float.IsFinite(distance) || distance <= 0.001f)
            {
                _thirdPersonCameraDistance = float.MaxValue;
                return;
            }

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

            // Pull in instantly at a wall; ease out to the desired shoulder.
            _thirdPersonCameraDistance = allowed < _thirdPersonCameraDistance
                ? allowed
                : _thirdPersonCameraDistance + (allowed - _thirdPersonCameraDistance) * 0.15f;

            // Tight spaces revert to the regular camera, avoiding the inside
            // of the Hunter's head/body and arm-geometry overdraw.
            if (_thirdPersonCameraDistance < ThirdPersonBodyClearance)
                return;

            _thirdPersonCameraOffset = direction * _thirdPersonCameraDistance;
            ModThirdPersonCameraActive = true;
            view = Matrix4.CreateTranslation(-_thirdPersonCameraOffset) * view;
        }
    }
}
