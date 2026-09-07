using System;
using System.Diagnostics;
using MphRead.Effects;
using MphRead.Formats;
using MphRead.Formats.Culling;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public sealed class HalfturretEntityPresentation : EntityPresentation
    {
        private readonly HalfturretEntity _turret;
        public HalfturretEntityPresentation(EntityBase entity, ScenePresentation presentation) : base(entity, presentation)
        {
            _turret = (HalfturretEntity)entity;
        }

        public override void GetDrawInfo()
        {
            if (!IsVisible(_turret.NodeRef))
            {
                return;
            }

            ModelInstance inst = _turret._models[0];
            Model model = inst.Model;
            AnimationInfo animInfo = inst.AnimInfo;
            if (_turret._timeSinceDamage < _turret.Owner.Values.DamageFlashTime * 2) // todo: FPS stuff
            {
                _turret.PaletteOverride = Metadata.RedPalette;
            }

            Matrix4 root = EntityBase.GetTransformMatrix(_turret.FacingVector, Vector3.UnitY);
            _turret._baseNodeParent.AnimIgnoreChild = true;
            model.AnimateNodes2(index: 0, false, root, Vector3.One, animInfo);
            _turret._baseNodeParent.AnimIgnoreChild = false;
            _turret._baseNode.BeforeTransform = EntityBase.GetTransformMatrix(_turret._aimVector, Vector3.UnitY, _turret._baseNodeParent.Animation.Row3.Xyz);
            _turret._baseNode.AnimIgnoreParent = true;
            model.AnimateNodes2(_turret._baseNodeParent.ChildIndex, false, Matrix4.Identity, Vector3.One, animInfo);
            _turret._baseNode.AnimIgnoreParent = false;
            _turret._baseNode.BeforeTransform = null;
            root = Matrix4.CreateTranslation(_turret.Position.AddY(-0.45f));
            for (int i = 0; i < model.Nodes.Count; i++)
            {
                Node node = model.Nodes[i];
                node.Animation *= root; // todo?: could do this in the shader
            }

            model.UpdateMatrixStack();
            UpdateMaterials(inst, _turret.Recolor);
            GetDrawItems(inst, 0);
            _turret.PaletteOverride = null;
            if (_turret._freezeTimer > 0)
            {
                _turret._useRoomLights = true;
                float radius = 0.65f;
                var transform = Matrix4.CreateScale(radius);
                transform.Row3.Xyz = _turret.Position;
                UpdateTransforms(_turret._altIceModel, transform, recolor: 0);
                GetDrawItems(_turret._altIceModel, 1);
                _turret._useRoomLights = false;
            }
        }

        protected override int? GetBindingOverride(ModelInstance inst, Material material, int index)
        {
            if (_turret.Owner.DoubleDamage && material.Lighting > 0)
            {
                return _turret.Owner.GetPresentation().DoubleDmgBindingId;
            }

            return base.GetBindingOverride(inst, material, index);
        }

        protected override Vector3 GetEmission(ModelInstance inst, Material material, int index)
        {
            // todo?: it's kinda weird that this doesn't use the team emission color
            if (_turret.Owner.DoubleDamage && material.Lighting > 0)
            {
                return Metadata.EmissionGray;
            }

            return base.GetEmission(inst, material, index);
        }

        // todo: share with player
        protected override Matrix4 GetTexcoordMatrix(ModelInstance inst, Material material, int materialId, Node node, int recolor)
        {
            if (_turret.Owner.DoubleDamage && material.Lighting > 0 && node.BillboardMode == BillboardMode.None)
            {
                Texture texture = _turret.Owner.DoubleDamageModel.Model.Recolors[0].Textures[0];
                // product should start with the upper 3x3 of the node animation result,
                // texgenMatrix should be multiplied with the view matrix if lighting is enabled,
                // and the result should be transposed.
                // these steps are done in the shader so the view matrix can be updated when frame advance is on.
                // strictly speaking, the use_light check in the shader is not the same as what the game does,
                // since the game checks if *any* material in the model uses lighting, but the result is the same.
                Matrix4 texgenMatrix = Matrix4.Identity;
                // in-game, there's only one uniform scale factor for models
                if (inst.Model.Scale.X != 1 || inst.Model.Scale.Y != 1 || inst.Model.Scale.Z != 1)
                {
                    texgenMatrix = Matrix4.CreateScale(inst.Model.Scale) * texgenMatrix;
                }

                Matrix4 product = texgenMatrix;
                product.M12 *= -1;
                product.M13 *= -1;
                product.M22 *= -1;
                product.M23 *= -1;
                product.M32 *= -1;
                product.M33 *= -1;
                ulong frame = _turret._scene.LiveFrames / 2;
                float rotZ = ((int)(16 * ((781874935307L * (53248 * frame) >> 32) + 2048)) >> 20) * (360 / 4096f);
                float rotY = ((int)(16 * ((781874935307L * (26624 * frame) + 0x80000000000) >> 32)) >> 20) * (360 / 4096f);
                var rot = Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(rotZ));
                rot *= Matrix4.CreateRotationY(MathHelper.DegreesToRadians(rotY));
                product = rot * product;
                product *= 1.0f / (texture.Width / 2);
                product = new Matrix4(product.Row0 * 16.0f, product.Row1 * 16.0f, product.Row2 * 16.0f, product.Row3);
                return product;
            }

            return base.GetTexcoordMatrix(inst, material, materialId, node, recolor);
        }
    }
}
