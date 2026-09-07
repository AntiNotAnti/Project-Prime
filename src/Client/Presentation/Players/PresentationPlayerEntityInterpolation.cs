using System;
using MphRead.Formats;
using MphRead.Formats.Culling;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using MphRead.Hud;

namespace MphRead.Entities
{
    public partial class PlayerPresentation
    {
        private bool _interpolatedPoseActive;
        private RenderPoseBackup _renderPoseBackup;
        private readonly Matrix4[] _renderKandenBackup = new Matrix4[5];
        private readonly Matrix4[] _renderTrailBackup = new Matrix4[PlayerEntity._mbTrailSegments];
        private readonly Matrix4[] _renderIceBackup = new Matrix4[19];
        private struct RenderPoseBackup
        {
            public Vector3 Position, Facing, Up, Aim, Right;
            public Vector3 SpireFacing, SpireUp, SpireRockLeft, SpireRockRight;
            public Matrix4 Transform, ModelTransform;
            public CollisionVolume Volume;
            public NodeRef Node;
            public float HorizontalX, HorizontalZ;
            public bool NodeUnresolved, Kanden, Trail, Ice;
        }

        public void BeginInterpolatedPose(in SnapshotPlayer state)
        {
            if (_player.IsMainPlayer || state.Slot != _player.SlotIndex)
                return;
            if (_interpolatedPoseActive)
                throw new InvalidOperationException("An interpolated player pose is already active.");
            _renderPoseBackup = new RenderPoseBackup
            {
                Position = _player._position,
                Transform = _player._transform,
                ModelTransform = _player._modelTransform,
                Facing = _player._facingVector,
                Up = _player._upVector,
                Aim = _player._gunVec1,
                Right = _player._gunVec2,
                HorizontalX = _player._field70,
                HorizontalZ = _player._field74,
                Volume = _player._volume,
                Node = _player.NodeRef,
                NodeUnresolved = _player.ModNodeUnresolved,
                SpireFacing = _player._spireAltFacing,
                SpireUp = _player._spireAltUp,
                SpireRockLeft = _player._spireRockPosL,
                SpireRockRight = _player._spireRockPosR,
                Kanden = _player.Hunter == Hunter.Kanden && _player.IsAltForm,
                Trail = _player.Hunter == Hunter.Samus && _player.IsAltForm,
                Ice = _player._frozenGfxTimer > 0 && !_player.IsAltForm
            };
            if (_renderPoseBackup.Kanden)
                _player._kandenSegMtx.CopyTo(_renderKandenBackup, 0);
            if (_renderPoseBackup.Trail)
            {
                for (int i = 0; i < PlayerEntity._mbTrailSegments; i++)
                    _renderTrailBackup[i] = PlayerEntity._mbTrailMatrices[_player.SlotIndex, i];
            }

            if (_renderPoseBackup.Ice)
                _player._bipedIceTransforms.CopyTo(_renderIceBackup, 0);
            _interpolatedPoseActive = true;
            Vector3 offset = state.Position - _player._position;
            Vector3 horizontal = new(state.Facing.X, 0, state.Facing.Z);
            if (horizontal.LengthSquared < 0.000001f)
                horizontal = new Vector3(_player._field70, 0, _player._field74);
            horizontal = horizontal.LengthSquared > 0.000001f ? horizontal.Normalized() : Vector3.UnitZ;
            float previousYaw = MathF.Atan2(_player._facingVector.X, _player._facingVector.Z);
            float nextYaw = MathF.Atan2(horizontal.X, horizontal.Z);
            Matrix4 rotation = Matrix4.CreateRotationY(nextYaw - previousYaw);
            // Bypass EntityBase's Transform/Position setters: they invalidate
            // collision-draw caches and decompose scale/rotation. Presentation
            // needs none of those mutations, nor camera/input/aim simulation.
            _player._position = state.Position;
            _player._facingVector = state.Facing;
            _player._gunVec1 = state.Aim;
            _player._field70 = horizontal.X;
            _player._field74 = horizontal.Z;
            _player._gunVec2 = new Vector3(horizontal.Z, 0, -horizontal.X);
            _player._upVector = Vector3.Cross(_player._facingVector, _player._gunVec2).Normalized();
            _player._transform = Matrix4.CreateScale(_player._scale) * PlayerEntity.GetTransformMatrix(_player._facingVector, _player._upVector, _player._position);
            _player._volume = CollisionVolume.Move(_player._volumeUnxf, _player._position);
            _player._modelTransform *= rotation;
            _player._spireAltFacing = Matrix.Vec3MultMtx3(_player._spireAltFacing, rotation);
            _player._spireAltUp = Matrix.Vec3MultMtx3(_player._spireAltUp, rotation);
            NodeRef node = PlayerEntity.ModWalkNodeRef(_player._scene, _player.NodeRef, _renderPoseBackup.Position, _player._position);
            if (node == NodeRef.None)
                node = _player._scene.GetNodeRefByPosition(_player._position);
            if (node != NodeRef.None)
                _player.NodeRef = node;
            _player.ModNodeUnresolved = node == NodeRef.None;
            // These alt-form draws consume cached world matrices rather than
            // Position. Move them for this picture and copy back exactly later;
            // subtracting the offset would accumulate floating-point drift.
            if (_renderPoseBackup.Kanden)
            {
                for (int i = 0; i < _player._kandenSegMtx.Length; i++)
                {
                    Matrix4 original = _renderKandenBackup[i];
                    _player._kandenSegMtx[i] = original * rotation;
                    _player._kandenSegMtx[i].Row3.Xyz = _player._position + Matrix.Vec3MultMtx3(original.Row3.Xyz - _renderPoseBackup.Position, rotation);
                }
            }

            if (_renderPoseBackup.Trail)
            {
                for (int i = 0; i < PlayerEntity._mbTrailSegments; i++)
                    PlayerEntity._mbTrailMatrices[_player.SlotIndex, i].Row3.Xyz += offset;
            }
        }

        public void EndInterpolatedPose()
        {
            if (!_interpolatedPoseActive)
                return;
            _player._position = _renderPoseBackup.Position;
            _player._transform = _renderPoseBackup.Transform;
            _player._modelTransform = _renderPoseBackup.ModelTransform;
            _player._facingVector = _renderPoseBackup.Facing;
            _player._upVector = _renderPoseBackup.Up;
            _player._gunVec1 = _renderPoseBackup.Aim;
            _player._gunVec2 = _renderPoseBackup.Right;
            _player._field70 = _renderPoseBackup.HorizontalX;
            _player._field74 = _renderPoseBackup.HorizontalZ;
            _player._volume = _renderPoseBackup.Volume;
            _player.NodeRef = _renderPoseBackup.Node;
            _player.ModNodeUnresolved = _renderPoseBackup.NodeUnresolved;
            _player._spireAltFacing = _renderPoseBackup.SpireFacing;
            _player._spireAltUp = _renderPoseBackup.SpireUp;
            // PlayerDraw itself updates these caches from the temporary pose.
            _player._spireRockPosL = _renderPoseBackup.SpireRockLeft;
            _player._spireRockPosR = _renderPoseBackup.SpireRockRight;
            if (_renderPoseBackup.Kanden)
                _renderKandenBackup.CopyTo(_player._kandenSegMtx, 0);
            if (_renderPoseBackup.Trail)
            {
                for (int i = 0; i < PlayerEntity._mbTrailSegments; i++)
                    PlayerEntity._mbTrailMatrices[_player.SlotIndex, i] = _renderTrailBackup[i];
            }

            if (_renderPoseBackup.Ice)
                _renderIceBackup.CopyTo(_player._bipedIceTransforms, 0);
            _interpolatedPoseActive = false;
        }
    }
}
