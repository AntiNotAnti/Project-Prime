using System;
using MphRead.Formats;
using MphRead.Formats.Culling;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public partial class PlayerEntity
    {
        private bool _interpolatedPoseActive;
        private RenderPoseBackup _renderPoseBackup;
        private readonly Matrix4[] _renderKandenBackup = new Matrix4[5];
        private readonly Matrix4[] _renderTrailBackup = new Matrix4[_mbTrailSegments];
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

        /// <summary>
        /// Temporarily presents a remote snapshot. Pair with EndInterpolatedPose
        /// in a finally around Scene.GetDrawItems, before any simulation resumes.
        /// The renderer copies queued mesh matrices, so restoring here does not
        /// change the already queued image. Main-player prediction is separate.
        /// </summary>
        internal void BeginInterpolatedPose(in SnapshotPlayer state)
        {
            if (IsMainPlayer || state.Slot != SlotIndex) return;
            if (_interpolatedPoseActive)
                throw new InvalidOperationException("An interpolated player pose is already active.");

            _renderPoseBackup = new RenderPoseBackup
            {
                Position = _position, Transform = _transform, ModelTransform = _modelTransform,
                Facing = _facingVector, Up = _upVector, Aim = _gunVec1, Right = _gunVec2,
                HorizontalX = _field70, HorizontalZ = _field74, Volume = _volume,
                Node = NodeRef, NodeUnresolved = ModNodeUnresolved,
                SpireFacing = _spireAltFacing, SpireUp = _spireAltUp,
                SpireRockLeft = _spireRockPosL, SpireRockRight = _spireRockPosR,
                Kanden = Hunter == Hunter.Kanden && IsAltForm,
                Trail = Hunter == Hunter.Samus && IsAltForm,
                Ice = _frozenGfxTimer > 0 && !IsAltForm
            };
            if (_renderPoseBackup.Kanden) _kandenSegMtx.CopyTo(_renderKandenBackup, 0);
            if (_renderPoseBackup.Trail)
            {
                for (int i = 0; i < _mbTrailSegments; i++)
                    _renderTrailBackup[i] = _mbTrailMatrices[SlotIndex, i];
            }
            if (_renderPoseBackup.Ice) _bipedIceTransforms.CopyTo(_renderIceBackup, 0);
            _interpolatedPoseActive = true;

            Vector3 offset = state.Position - _position;
            Vector3 horizontal = new(state.Facing.X, 0, state.Facing.Z);
            if (horizontal.LengthSquared < 0.000001f)
                horizontal = new Vector3(_field70, 0, _field74);
            horizontal = horizontal.LengthSquared > 0.000001f ? horizontal.Normalized() : Vector3.UnitZ;
            float previousYaw = MathF.Atan2(_facingVector.X, _facingVector.Z);
            float nextYaw = MathF.Atan2(horizontal.X, horizontal.Z);
            Matrix4 rotation = Matrix4.CreateRotationY(nextYaw - previousYaw);

            // Bypass EntityBase's Transform/Position setters: they invalidate
            // collision-draw caches and decompose scale/rotation. Presentation
            // needs none of those mutations, nor camera/input/aim simulation.
            _position = state.Position;
            _facingVector = state.Facing;
            _gunVec1 = state.Aim;
            _field70 = horizontal.X;
            _field74 = horizontal.Z;
            _gunVec2 = new Vector3(horizontal.Z, 0, -horizontal.X);
            _upVector = Vector3.Cross(_facingVector, _gunVec2).Normalized();
            _transform = Matrix4.CreateScale(_scale) * GetTransformMatrix(_facingVector, _upVector, _position);
            _volume = CollisionVolume.Move(_volumeUnxf, _position);
            _modelTransform *= rotation;
            _spireAltFacing = Matrix.Vec3MultMtx3(_spireAltFacing, rotation);
            _spireAltUp = Matrix.Vec3MultMtx3(_spireAltUp, rotation);

            NodeRef node = ModWalkNodeRef(_scene, NodeRef, _renderPoseBackup.Position, _position);
            if (node == NodeRef.None) node = _scene.GetNodeRefByPosition(_position);
            if (node != NodeRef.None) NodeRef = node;
            ModNodeUnresolved = node == NodeRef.None;

            // These alt-form draws consume cached world matrices rather than
            // Position. Move them for this picture and copy back exactly later;
            // subtracting the offset would accumulate floating-point drift.
            if (_renderPoseBackup.Kanden)
            {
                for (int i = 0; i < _kandenSegMtx.Length; i++)
                {
                    Matrix4 original = _renderKandenBackup[i];
                    _kandenSegMtx[i] = original * rotation;
                    _kandenSegMtx[i].Row3.Xyz = _position + Matrix.Vec3MultMtx3(
                        original.Row3.Xyz - _renderPoseBackup.Position, rotation);
                }
            }
            if (_renderPoseBackup.Trail)
            {
                for (int i = 0; i < _mbTrailSegments; i++)
                    _mbTrailMatrices[SlotIndex, i].Row3.Xyz += offset;
            }
        }

        internal void EndInterpolatedPose()
        {
            if (!_interpolatedPoseActive) return;
            _position = _renderPoseBackup.Position;
            _transform = _renderPoseBackup.Transform;
            _modelTransform = _renderPoseBackup.ModelTransform;
            _facingVector = _renderPoseBackup.Facing;
            _upVector = _renderPoseBackup.Up;
            _gunVec1 = _renderPoseBackup.Aim;
            _gunVec2 = _renderPoseBackup.Right;
            _field70 = _renderPoseBackup.HorizontalX;
            _field74 = _renderPoseBackup.HorizontalZ;
            _volume = _renderPoseBackup.Volume;
            NodeRef = _renderPoseBackup.Node;
            ModNodeUnresolved = _renderPoseBackup.NodeUnresolved;
            _spireAltFacing = _renderPoseBackup.SpireFacing;
            _spireAltUp = _renderPoseBackup.SpireUp;
            // PlayerDraw itself updates these caches from the temporary pose.
            _spireRockPosL = _renderPoseBackup.SpireRockLeft;
            _spireRockPosR = _renderPoseBackup.SpireRockRight;
            if (_renderPoseBackup.Kanden) _renderKandenBackup.CopyTo(_kandenSegMtx, 0);
            if (_renderPoseBackup.Trail)
            {
                for (int i = 0; i < _mbTrailSegments; i++)
                    _mbTrailMatrices[SlotIndex, i] = _renderTrailBackup[i];
            }
            if (_renderPoseBackup.Ice) _renderIceBackup.CopyTo(_bipedIceTransforms, 0);
            _interpolatedPoseActive = false;
        }
    }
}
