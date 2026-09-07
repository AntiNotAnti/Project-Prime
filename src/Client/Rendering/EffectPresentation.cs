using System;
using System.Buffers;
using System.Diagnostics;
using MphRead.Effects;
using MphRead.Formats;
using OpenTK.Mathematics;
namespace MphRead
{
    public static class EffectPresentation
    {
        public static void AddRenderItem(this SingleParticle particle, ScenePresentation scene)
        {
            Vector3[] uvsAndVerts = ArrayPool<Vector3>.Shared.Rent(8);
            uvsAndVerts[0] = new Vector3(particle.Texcoord0);
            uvsAndVerts[1] = particle.Vertex0;
            uvsAndVerts[2] = new Vector3(particle.Texcoord1);
            uvsAndVerts[3] = particle.Vertex1;
            uvsAndVerts[4] = new Vector3(particle.Texcoord2);
            uvsAndVerts[5] = particle.Vertex2;
            uvsAndVerts[6] = new Vector3(particle.Texcoord3);
            uvsAndVerts[7] = particle.Vertex3;
            Material material = particle.ParticleDefinition.Model.Materials[particle.ParticleDefinition.MaterialId];
            // should already be bound
            int bindingId = scene.BindGetTexture(particle.ParticleDefinition.Model, material.TextureId, material.PaletteId, 0);
            RepeatMode xRepeat = material.XRepeat;
            RepeatMode yRepeat = material.YRepeat;
            float scaleS = 1;
            float scaleT = 1;
            if (xRepeat == RepeatMode.Mirror)
            {
                scaleS = material.ScaleS;
            }
            if (yRepeat == RepeatMode.Mirror)
            {
                scaleT = material.ScaleT;
            }
            var transform = Matrix4.CreateTranslation(particle.Position);
            scene.AddRenderItem(RenderItemType.Particle, particle.Alpha, scene.GetNextPolygonId(), particle.Color, xRepeat, yRepeat,
                scaleS, scaleT, transform, uvsAndVerts, bindingId, BillboardMode.Sphere);
        }
        public static void AddRenderItem(this EffectParticle particle, ScenePresentation scene)
        {
            if (particle.DrawNode)
            {
                Model model = particle.Owner.Model;
                Node node = particle.Owner.Nodes[particle.ParticleId];
                Mesh mesh = particle.Owner.Model.Meshes[node.MeshId / 2];
                Material material = particle.Owner.Model.Materials[particle.MaterialId];
                Matrix4 transform = particle.NodeTransform;
                Matrix4 texcoordMtx = Matrix4.Identity;
                if (material.TexgenMode == TexgenMode.Texcoord)
                {
                    texcoordMtx = Matrix4.CreateTranslation(material.ScaleS * material.TranslateS,
                        material.ScaleT * material.TranslateT, 0.0f);
                    texcoordMtx = Matrix4.CreateScale(material.ScaleS, material.ScaleT, 1.0f) * texcoordMtx;
                    texcoordMtx = Matrix4.CreateRotationZ(material.RotateZ) * texcoordMtx;
                }
                material.CurrentDiffuse = particle.Color;
                material.CurrentAlpha = particle.Alpha;
                Debug.Assert(model.NodeMatrixIds.Count == 0);
                scene.UpdateMaterials(model, 0); // probably not necessary unless the model has texture animation
                scene.AddRenderItem(material, scene.GetNextPolygonId(), 1, Vector3.Zero, LightInfo.Zero, texcoordMtx,
                    transform, scene.GetMeshListId(mesh), 0, Array.Empty<float>(), null, null, SelectionType.None, particle.BillboardMode);
            }
            else
            {
                if (particle.MaterialId >= particle.Owner.Model.Materials.Count)
                {
                    // todo: investigate this
                    return;
                }
                Vector3[] uvsAndVerts = ArrayPool<Vector3>.Shared.Rent(8);
                uvsAndVerts[0] = new Vector3(particle.Texcoord0);
                uvsAndVerts[1] = particle.Vertex0;
                uvsAndVerts[2] = new Vector3(particle.Texcoord1);
                uvsAndVerts[3] = particle.Vertex1;
                uvsAndVerts[4] = new Vector3(particle.Texcoord2);
                uvsAndVerts[5] = particle.Vertex2;
                uvsAndVerts[6] = new Vector3(particle.Texcoord3);
                uvsAndVerts[7] = particle.Vertex3;
                Material material = particle.Owner.Model.Materials[particle.MaterialId];
                int bindingId = scene.GetEffectTextureBindings(particle.Owner)[particle.ParticleId];
                RepeatMode xRepeat = material.XRepeat;
                RepeatMode yRepeat = material.YRepeat;
                float scaleS = 1;
                float scaleT = 1;
                if (xRepeat == RepeatMode.Mirror)
                {
                    scaleS = material.ScaleS;
                }
                if (yRepeat == RepeatMode.Mirror)
                {
                    scaleT = material.ScaleT;
                }
                Matrix4 transform;
                if (particle.Owner.Flags.TestFlag(EffElemFlags.UseTransform))
                {
                    if (particle.BillboardMode != BillboardMode.None)
                    {
                        Vector3 position = Matrix.Vec3MultMtx4(particle.Position, particle.Owner.Transform.ClearTranslation());
                        transform = Matrix4.CreateTranslation(position + particle.Owner.Transform.Row3.Xyz);
                    }
                    else
                    {
                        transform = Matrix4.CreateTranslation(particle.Position) * particle.Owner.Transform;
                    }
                }
                else
                {
                    transform = Matrix4.CreateTranslation(particle.Position);
                }
                scene.AddRenderItem(RenderItemType.Particle, particle.Alpha, scene.GetNextPolygonId(), particle.Color, xRepeat, yRepeat,
                    scaleS, scaleT, transform, uvsAndVerts, bindingId, particle.BillboardMode);
            }
        }
    }
}
