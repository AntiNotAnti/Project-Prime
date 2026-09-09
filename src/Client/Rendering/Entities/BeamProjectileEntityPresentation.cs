using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using MphRead.Effects;
using MphRead.Formats;
using MphRead.Formats.Collision;
using MphRead.Formats.Culling;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public sealed class BeamProjectileEntityPresentation : EntityPresentation
    {
        private readonly BeamProjectileEntity _entity;
        public BeamProjectileEntityPresentation(BeamProjectileEntity entity, ScenePresentation presentation) : base(entity, presentation)
        {
            _entity = entity;
        }

        private Model? _boundTrailModel;
        private int _boundTrailRecolor;
        private int _trailBinding;
        private int GetTrailBinding(Material material, int recolor)
        {
            Model model = _entity._trailModel!.Model;
            if (!ReferenceEquals(_boundTrailModel, model) || _boundTrailRecolor != recolor)
            {
                _trailBinding = Presentation.BindGetTexture(model, material.TextureId, material.PaletteId, recolor);
                _boundTrailModel = model;
                _boundTrailRecolor = recolor;
            }

            return _trailBinding;
        }

        private TextureIdentity? GetTrailIdentity(Material material, int recolor)
            => Presentation.GetTextureIdentity(_entity._trailModel!.Model, material, recolor);

        public override void GetDrawInfo()
        {
            AddVisualLight();
            if (_entity.DrawFuncId == 0)
            {
                Draw00();
            }
            else if (_entity.DrawFuncId == 1)
            {
                Draw01();
            }
            else if (_entity.DrawFuncId == 2)
            {
                Draw02();
            }
            else if (_entity.DrawFuncId == 3)
            {
                Draw03();
            }
            else if (_entity.DrawFuncId == 6 || _entity.DrawFuncId == 12)
            {
                Draw06();
            }
            else if (_entity.DrawFuncId == 7)
            {
                Draw07();
            }
            else if (_entity.DrawFuncId == 9)
            {
                Draw09();
            }
            else if (_entity.DrawFuncId == 10)
            {
                Draw10();
            }
            else if (_entity.DrawFuncId == 17)
            {
                Draw17();
            }
        }

        /// <summary>
        /// Capture the beam's already-resolved presentation colour as a small
        /// render-only light.  This is intentionally independent of beam
        /// collision, damage, lifetime, and RNG state; the normal draw path
        /// remains the authority for whether the projectile is visible.
        /// </summary>
        private void AddVisualLight()
        {
            Vector3 color = _entity.Color;
            Vector3 position = _entity.Position;
            if (!_entity.ShouldDraw || !_entity.Active || _entity.Hidden
                || !IsFinite(position) || !IsFinite(color)
                || color.LengthSquared <= 0.0001f)
            {
                return;
            }

            Presentation.TryAddVisualLight(position, color,
                radius: 1.25f, intensity: 0.35f, priority: 20);
        }

        private static bool IsFinite(Vector3 value)
            => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

        // Power Beam
        private void Draw00()
        {
            if (!_entity.Flags.TestFlag(BeamFlags.Collided))
            {
                Presentation.AddSingleParticle(SingleType.Fuzzball, Entity.Position, _entity.Color, alpha: 1, scale: 1 / 4f);
            }

            DrawTrail1(Fixed.ToFloat(122));
        }

        // uncharged Volt Driver
        private void Draw01()
        {
            DrawTrail1(Fixed.ToFloat(614));
        }

        // charged Volt Driver
        private void Draw02()
        {
            DrawTrail2(Fixed.ToFloat(1024), 5);
        }

        // non-affinity Judicator
        private void Draw03()
        {
            if (!_entity.Flags.TestFlag(BeamFlags.Collided))
            {
                base.GetDrawInfo();
            }

            DrawTrail2(Fixed.ToFloat(204), 5);
        }

        // fieldLock tear/Judicator
        private void Draw06()
        {
            if (!_entity.Flags.TestFlag(BeamFlags.Collided))
            {
                Presentation.AddSingleParticle(SingleType.Fuzzball, Entity.Position, Vector3.One, alpha: 1, scale: 1 / 4f);
            }

            DrawTrail3(Fixed.ToFloat(204));
        }

        // Missile
        private void Draw07()
        {
            if (!_entity.Flags.TestFlag(BeamFlags.Collided))
            {
                Presentation.AddSingleParticle(SingleType.Fuzzball, Entity.Position, Vector3.One, alpha: 1, scale: 1 / 4f);
            }

            DrawTrail2(Fixed.ToFloat(204), 5);
        }

        // Shock Coil
        private void Draw09()
        {
            if (!_entity.Flags.TestFlag(BeamFlags.Collided))
            {
                if (_entity.Target != null)
                {
                    DrawTrail4(height: 0.15f, range: 0.5f, segments: 10);
                }
                else if (_entity.Owner == _scene.LocalPlayer!)
                {
                    DrawTrail4(height: 0.025f, range: 0.35f, segments: 5);
                }
            }
        }

        // Battlehammer
        private void Draw10()
        {
            DrawTrail2(Fixed.ToFloat(81), 2);
        }

        // green energy beam
        private void Draw17()
        {
            if (!_entity.Flags.TestFlag(BeamFlags.Collided))
            {
                base.GetDrawInfo();
            }
        }

        private void DrawTrail1(float height)
        {
            Debug.Assert(_entity._trailModel != null);
            Texture texture = _entity._trailModel.Model.Recolors[0].Textures[0];
            float uvS = (texture.Width - (1 / 16f)) / texture.Width;
            float uvT = (texture.Height - (1 / 16f)) / texture.Height;
            Vector3[] uvsAndVerts = ArrayPool<Vector3>.Shared.Rent(8);
            uvsAndVerts[0] = Vector3.Zero;
            uvsAndVerts[1] = new Vector3(Entity.Position.X - _entity.BackPosition.X, Entity.Position.Y - _entity.BackPosition.Y - height, Entity.Position.Z - _entity.BackPosition.Z);
            uvsAndVerts[2] = new Vector3(0, uvT, 0);
            uvsAndVerts[3] = new Vector3(Entity.Position.X - _entity.BackPosition.X, height + Entity.Position.Y - _entity.BackPosition.Y, Entity.Position.Z - _entity.BackPosition.Z);
            uvsAndVerts[4] = new Vector3(uvS, 0, 0);
            uvsAndVerts[5] = new Vector3(0, -height, 0);
            uvsAndVerts[6] = new Vector3(uvS, uvT, 0);
            uvsAndVerts[7] = new Vector3(0, height, 0);
            Material material = _entity._trailModel.Model.Materials[0];
            float alpha = Math.Clamp(_entity.Lifespan * 30 * 8, 0, 31) / 31;
            Presentation.AddRenderItem(RenderPrimitive.TrailSingle, alpha, Presentation.GetNextPolygonId(), _entity.Color, material.XRepeat, material.YRepeat, material.ScaleS, material.ScaleT, Matrix4.CreateTranslation(_entity.BackPosition), uvsAndVerts, GetTrailIdentity(material, 0), GetTrailBinding(material, 0), bloomStrength: RenderMaterial.TrailBloomStrength);
        }

        private void DrawTrail2(float height, int segments)
        {
            Debug.Assert(_entity._trailModel != null);
            if (segments < 2)
            {
                return;
            }

            if (segments > _entity.PastPositions.Length / 2)
            {
                segments = _entity.PastPositions.Length / 2;
            }

            int count = 4 * segments;
            Texture texture = _entity._trailModel.Model.Recolors[0].Textures[0];
            float uvT = (texture.Height - (1 / 16f)) / texture.Height;
            Vector3[] uvsAndVerts = ArrayPool<Vector3>.Shared.Rent(count);
            for (int i = 0; i < segments; i++)
            {
                float uvS = 0;
                if (i > 0)
                {
                    uvS = (texture.Width / (float)(segments - 1) * i - (1 / 16f)) / texture.Width;
                }

                Vector3 vec = _entity.PastPositions[i * 2] - _entity.PastPositions[0];
                uvsAndVerts[4 * i] = new Vector3(uvS, 0, 0);
                uvsAndVerts[4 * i + 1] = new Vector3(vec.X, vec.Y - height, vec.Z);
                uvsAndVerts[4 * i + 2] = new Vector3(uvS, uvT, 0);
                uvsAndVerts[4 * i + 3] = new Vector3(vec.X, vec.Y + height, vec.Z);
            }

            Material material = _entity._trailModel.Model.Materials[0];
            float alpha = Math.Clamp(_entity.Lifespan * 30 * 8, 0, 31) / 31;
            Presentation.AddRenderItem(RenderPrimitive.TrailMulti, alpha, Presentation.GetNextPolygonId(), _entity.Color, material.XRepeat, material.YRepeat, material.ScaleS, material.ScaleT, Matrix4.CreateTranslation(_entity.PastPositions[0]), uvsAndVerts, GetTrailIdentity(material, 0), GetTrailBinding(material, 0), trailCount: count, bloomStrength: RenderMaterial.TrailBloomStrength);
        }

        private void DrawTrail3(float height)
        {
            Debug.Assert(_entity._trailModel != null);
            Texture texture = _entity._trailModel.Model.Recolors[0].Textures[0];
            float uvS2 = (texture.Width - (1 / 16f)) / texture.Width;
            float uvT2 = (texture.Height / 4f - (1 / 16f)) / texture.Height;
            Vector3[] uvsAndVerts = ArrayPool<Vector3>.Shared.Rent(8);
            uvsAndVerts[0] = Vector3.Zero;
            uvsAndVerts[1] = new Vector3(0, -height, 0);
            uvsAndVerts[2] = new Vector3(0, uvT2, 0);
            uvsAndVerts[3] = new Vector3(0, height, 0);
            uvsAndVerts[4] = new Vector3(uvS2, 0, 0);
            uvsAndVerts[5] = new Vector3(_entity.PastPositions[8].X - _entity.PastPositions[0].X, _entity.PastPositions[8].Y - _entity.PastPositions[0].Y - height, _entity.PastPositions[8].Z - _entity.PastPositions[0].Z);
            uvsAndVerts[6] = new Vector3(uvS2, uvT2, 0);
            uvsAndVerts[7] = new Vector3(_entity.PastPositions[8].X - _entity.PastPositions[0].X, _entity.PastPositions[8].Y - _entity.PastPositions[0].Y + height, _entity.PastPositions[8].Z - _entity.PastPositions[0].Z);
            Material material = _entity._trailModel.Model.Materials[0];
            float alpha = Math.Clamp(_entity.Lifespan * 30 * 8, 0, 31) / 31;
            Presentation.AddRenderItem(RenderPrimitive.TrailSingle, alpha, Presentation.GetNextPolygonId(), _entity.Color, material.XRepeat, material.YRepeat, material.ScaleS, material.ScaleT, Matrix4.CreateTranslation(_entity.PastPositions[0]), uvsAndVerts, GetTrailIdentity(material, 0), GetTrailBinding(material, 0), bloomStrength: RenderMaterial.TrailBloomStrength);
        }

        private void DrawTrail4(float height, float range, int segments)
        {
            Debug.Assert(_entity._trailModel != null);
            if (segments < 2)
            {
                return;
            }

            int count = 4 * segments;
            int frames = (int)_scene.LiveFrames / 2;
            uint rng = (uint)(frames + (int)(Entity.Position.X * 4096));
            int index = frames & 15;
            float halfRange = range / 2;
            Vector3 vec = Entity.Position - _entity.PastPositions[8];
            Texture texture = _entity._trailModel.Model.Recolors[0].Textures[0];
            float uvT = (texture.Height - (1 / 16f)) / texture.Height;
            Vector3[] uvsAndVerts = ArrayPool<Vector3>.Shared.Rent(count);
            for (int i = 0; i < segments; i++)
            {
                float uvS = 0;
                int factor = index + i;
                if (factor > 0)
                {
                    uvS = (2 * texture.Width / (float)(segments - 1) * factor - (1 / 16f)) / texture.Width;
                }

                float pct = (float)i / (segments - 1);
                // todo?: not sure if dividing by 4 is strictly correct here
                float x = vec.X * pct + _entity.Velocity.X / 4 * pct * (1 - pct);
                float y = vec.Y * pct + _entity.Velocity.Y / 4 * pct * (1 - pct);
                float z = vec.Z * pct + _entity.Velocity.Z / 4 * pct * (1 - pct);
                if (i > 0 && i < segments - 1)
                {
                    x += Rng.CallRng(ref rng, (uint)Fixed.ToInt(range)) / 4096f - halfRange;
                    y += Rng.CallRng(ref rng, (uint)Fixed.ToInt(range)) / 4096f - halfRange;
                    z += Rng.CallRng(ref rng, (uint)Fixed.ToInt(range)) / 4096f - halfRange;
                }

                uvsAndVerts[4 * i] = new Vector3(uvS, 0, 0);
                uvsAndVerts[4 * i + 1] = new Vector3(x, y - height, z);
                uvsAndVerts[4 * i + 2] = new Vector3(uvS, uvT, 0);
                uvsAndVerts[4 * i + 3] = new Vector3(x, y + height, z);
            }

            Material material = _entity._trailModel.Model.Materials[0];
            Presentation.AddRenderItem(RenderPrimitive.TrailMulti, alpha: 1, Presentation.GetNextPolygonId(), _entity.Color, material.XRepeat, material.YRepeat, material.ScaleS, material.ScaleT, Matrix4.CreateTranslation(_entity.PastPositions[8]), uvsAndVerts, GetTrailIdentity(material, 0), GetTrailBinding(material, 0), trailCount: count, bloomStrength: RenderMaterial.TrailBloomStrength);
        }
    }
}
