using System;
using System.Buffers;
using System.Diagnostics;
using MphRead.Effects;
using MphRead.Formats;
using MphRead.Formats.Culling;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public sealed class BombEntityPresentation : EntityPresentation
    {
        private readonly BombEntity _entity;
        public BombEntityPresentation(BombEntity entity, ScenePresentation presentation) : base(entity, presentation)
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
            if (_entity.BombType == BombType.Lockjaw)
            {
                if (_entity.BombIndex == 1)
                {
                    DrawLockjawTrail(Entity.Position, _entity.Owner.SyluxBombs[0]!.Position, Fixed.ToFloat(614), 10);
                }
                else if (_entity.BombIndex == 2)
                {
                    DrawLockjawTrail(Entity.Position, _entity.Owner.SyluxBombs[1]!.Position, Fixed.ToFloat(614), 10);
                    DrawLockjawTrail(Entity.Position, _entity.Owner.SyluxBombs[0]!.Position, Fixed.ToFloat(614), 10);
                }
            }

            base.GetDrawInfo();
        }

        /// <summary>
        /// Bombs do not carry a material colour, so use their existing bomb
        /// kind as the stable presentation colour for a small render-only
        /// light.  No collision, target, countdown, or simulation state is
        /// changed by this optional visual record.
        /// </summary>
        private void AddVisualLight()
        {
            if (!_entity.ShouldDraw || !_entity.Active || _entity.Hidden
                || _entity.Flags.TestFlag(BombFlags.Exploded))
            {
                return;
            }

            Vector3 color = _entity.BombType switch
            {
                BombType.Stinglarva => new Vector3(1f, 0.25f, 0.05f),
                BombType.Lockjaw => new Vector3(0.15f, 0.7f, 1f),
                _ => new Vector3(0.3f, 0.5f, 1f)
            };
            Presentation.TryAddVisualLight(_entity.Position, color,
                radius: 1.5f, intensity: 0.4f, priority: 24);
        }

        private void DrawLockjawTrail(Vector3 point1, Vector3 point2, float height, int segments)
        {
            Debug.Assert(_entity._trailModel != null);
            if (segments < 2)
            {
                return;
            }

            int count = 4 * segments;
            int recolor = Entity.Recolor;
            if (Entity.Recolor > 0)
            {
                recolor--;
            }

            Vector3 vec = point2 - point1;
            Texture texture = _entity._trailModel.Model.Recolors[recolor].Textures[0];
            float uvT = (texture.Height - (1 / 16f)) / texture.Height;
            Vector3[] uvsAndVerts = ArrayPool<Vector3>.Shared.Rent(count);
            for (int i = 0; i < segments; i++)
            {
                float uvS = 0;
                if (i > 0)
                {
                    uvS = (texture.Width / (float)(segments - 1) * i - (1 / 16f)) / texture.Width;
                }

                float pct = i * (1f / (segments - 1));
                float x = vec.X * pct;
                float y = vec.Y * pct;
                float z = vec.Z * pct;
                if (i > 0 && i < segments - 1)
                {
                    x += _entity._scene.Random.GetRandomInt1(0x800) / 4096f - 0.25f;
                    y += _entity._scene.Random.GetRandomInt1(0x800) / 4096f - 0.25f;
                    z += _entity._scene.Random.GetRandomInt1(0x800) / 4096f - 0.25f;
                }

                uvsAndVerts[4 * i] = new Vector3(uvS, 0, 0);
                uvsAndVerts[4 * i + 1] = new Vector3(x, y - height, z);
                uvsAndVerts[4 * i + 2] = new Vector3(uvS, uvT, 0);
                uvsAndVerts[4 * i + 3] = new Vector3(x, y + height, z);
            }

            Material material = _entity._trailModel.Model.Materials[0];
            int resolvedRecolor = Math.Max(Entity.Recolor - 1, 0);
            Presentation.AddRenderItem(RenderPrimitive.TrailMulti, alpha: 1, Presentation.GetNextPolygonId(), Vector3.One, material.XRepeat, material.YRepeat, material.ScaleS, material.ScaleT, Matrix4.CreateTranslation(point1), uvsAndVerts, GetTrailIdentity(material, resolvedRecolor), GetTrailBinding(material, resolvedRecolor), trailCount: count, bloomStrength: RenderMaterial.TrailBloomStrength);
        }
    }
}
