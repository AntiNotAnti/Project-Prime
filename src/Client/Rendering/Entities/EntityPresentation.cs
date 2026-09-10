using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using MphRead.Formats.Collision;
using MphRead.Formats.Culling;
using MphRead.Mods.Network;
using MphRead.Sound;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public partial class EntityPresentation
    {
        protected EntityBase Entity { get; }
        protected ScenePresentation Presentation { get; }
        protected Scene _scene => Presentation.World;

        public EntityPresentation(EntityBase entity, ScenePresentation presentation)
        {
            Entity = entity;
            Presentation = presentation;
        }

        protected virtual int? GetBindingOverride(ModelInstance inst, Material material, int index)
        {
            return null;
        }

        protected virtual TextureIdentity? GetTextureIdentity(ModelInstance inst, Material material, int index, int recolor)
        {
            return Presentation.GetTextureIdentity(inst.Model, material, recolor);
        }

        protected virtual EnhancedForceFieldDrawState? GetEnhancedForceFieldDrawState(
            ModelInstance inst, int modelIndex, int nodeIndex, int meshIndex)
            => null;

        protected void GetDrawItems(ModelInstance inst, int i, LightInfo? lightInfo = null)
        {
            int polygonId = Presentation.GetNextPolygonId();
            bool interpolateNodes = Presentation.ResolveNodeSubmission(Entity, inst, out Matrix4[] nodePoses, out float[] nodeStack);
            int recolor = Entity.GetModelRecolor(inst, i);
            GetItems(inst, i, 0, polygonId);
            void GetItems(ModelInstance inst, int index, int nodeIndex, int polygonId)
            {
                Model model = inst.Model;
                Node node = model.Nodes[nodeIndex];
                if (node.Enabled)
                {
                    int start = node.MeshId / 2;
                    for (int k = 0; k < node.MeshCount; k++)
                    {
                        Mesh mesh = model.Meshes[start + k];
                        if (!mesh.Visible)
                        {
                            continue;
                        }

                        Material material = model.Materials[mesh.MaterialId];
                        Vector3 emission = GetEmission(inst, material, mesh.MaterialId);
                        Matrix4 texcoordMatrix = GetTexcoordMatrix(inst, material, mesh.MaterialId, node);
                        Vector4? color = inst.IsPlaceholder ? Entity.GetOverrideColor(inst, index) : null;
                        SelectionType selectionType = Selection.CheckSelection(Entity, inst, node, mesh);
                        int? bindingOverride = GetBindingOverride(inst, material, mesh.MaterialId);
                        TextureIdentity? textureIdentity = GetTextureIdentity(inst, material, mesh.MaterialId, recolor);
                        TextureAssetKey? textureAssetKey
                            = ScenePresentation.GetModelTextureAssetKey(model, material, recolor);
                        Presentation.AddRenderItem(material, polygonId, Entity.Alpha, emission, lightInfo ?? GetLightInfo(), texcoordMatrix, interpolateNodes ? nodePoses[nodeIndex] : node.Animation, Presentation.GetMeshListId(mesh), mesh.GeometryIdentity, model.NodeMatrixIds.Count, interpolateNodes ? nodeStack : model.MatrixStackValues, color, Entity.PaletteOverride, selectionType, node.BillboardMode, Entity._drawScale, bindingOverride, textureIdentity, textureAssetKey,
                            GetEnhancedForceFieldDrawState(inst, index, nodeIndex,
                                start + k));
                    }

                    if (node.ChildIndex != -1)
                    {
                        GetItems(inst, index, node.ChildIndex, polygonId);
                    }
                }

                if (node.NextIndex != -1)
                {
                    GetItems(inst, index, node.NextIndex, polygonId);
                }
            }
        }

        public virtual bool ScanVisible()
        {
            return IsVisible(Entity.NodeRef);
        }

        public virtual void GetDrawInfo()
        {
            for (int i = 0; i < Entity._models.Count; i++)
            {
                ModelInstance inst = Entity._models[i];
                if ((!inst.Active && !Presentation.ShowAllEntities) || (inst.IsPlaceholder && !Presentation.ShowInvisibleEntities && !Presentation.ShowAllEntities))
                {
                    continue;
                }

                UpdateTransforms(inst, i);
                if (!Entity.Hidden)
                {
                    // todo: hide attached effects
                    GetDrawItems(inst, i);
                }
            }

            if (Presentation.ShowCollision && (Presentation.ColEntDisplay == EntityType.All || Presentation.ColEntDisplay == Entity.Type))
            {
                GetCollisionDrawInfo();
            }
        }

        protected virtual void GetCollisionDrawInfo()
        {
            for (int i = 0; i < 2; i++)
            {
                EntityCollision? entCol = Entity.EntityCollision[i];
                if (entCol?.Collision != null && entCol.Collision.Active)
                {
                    entCol.Collision.Info.GetDrawInfo(entCol.DrawPoints, Vector3.Zero, Entity.Type, Presentation);
                }
            }
        }

        protected virtual Vector3 GetEmission(ModelInstance inst, Material material, int index)
        {
            return Vector3.Zero;
        }

        protected virtual Matrix4 GetTexcoordMatrix(ModelInstance inst, Material material, int materialId, Node node, int recolor = -1)
        {
            Model model = inst.Model;
            Matrix4 texcoordMatrix = Matrix4.Identity;
            TexcoordAnimationGroup? group = inst.AnimInfo.Texcoord.Group;
            TexcoordAnimation? animation = null;
            if (group != null && group.Animations.TryGetValue(material.Name, out TexcoordAnimation result))
            {
                animation = result;
            }

            if (group != null && animation != null && (!inst.Model.FirstHunt || material.TexgenMode != TexgenMode.None))
            {
                // MPH overwrites a material's None texgen with Texcoord when parsing a texcoord animation; FH does not
                texcoordMatrix = model.AnimateTexcoords(group, animation.Value, inst.AnimInfo.TexcoordFrame);
            }

            if (material.TexgenMode != TexgenMode.None)
            {
                Matrix4 materialMatrix;
                // in-game, this is a list of precomputed matrices that we compute on the fly in the next block;
                // however, we only use it for the one hard-coded matrix in AlimbicCapsule
                if (model.TextureMatrices.Count > 0)
                {
                    materialMatrix = model.TextureMatrices[material.MatrixId];
                }
                else
                {
                    materialMatrix = Matrix4.CreateTranslation(material.ScaleS * material.TranslateS, material.ScaleT * material.TranslateT, 0.0f);
                    materialMatrix = Matrix4.CreateScale(material.ScaleS, material.ScaleT, 1.0f) * materialMatrix;
                    materialMatrix = Matrix4.CreateRotationZ(material.RotateZ) * materialMatrix;
                }

                // for texcoord texgen, the animation result is used if any, otherwise the material matrix is used.
                // for normal texgen, two matrices are multiplied. the first is always the material matrix.
                // the second is the animation result if any, otherwise it's the material matrix again.
                if (group == null || animation == null)
                {
                    texcoordMatrix = materialMatrix;
                }

                if (material.TexgenMode == TexgenMode.Normal)
                {
                    Texture texture = model.Recolors[recolor == -1 ? Entity.Recolor : recolor].Textures[material.TextureId];
                    // product should start with the upper 3x3 of the node animation result,
                    // texgenMatrix should be multiplied with the view matrix if lighting is enabled,
                    // and the result should be transposed.
                    // these steps are done in the shader so the view matrix can be updated when frame advance is on.
                    // strictly speaking, the use_light check in the shader is not the same as what the game does,
                    // since the game checks if *any* material in the model uses lighting, but the result is the same.
                    Matrix4 texgenMatrix = Matrix4.Identity;
                    // in-game, there's only one uniform scale factor for models
                    if (model.Scale.X != 1 || model.Scale.Y != 1 || model.Scale.Z != 1)
                    {
                        texgenMatrix = Matrix4.CreateScale(model.Scale);
                    }

                    Matrix4 product = texgenMatrix;
                    product.M12 *= -1;
                    product.M13 *= -1;
                    product.M22 *= -1;
                    product.M23 *= -1;
                    product.M32 *= -1;
                    product.M33 *= -1;
                    product *= materialMatrix;
                    product *= texcoordMatrix;
                    product *= 1.0f / (texture.Width / 2);
                    texcoordMatrix = new Matrix4(product.Row0 * 16.0f, product.Row1 * 16.0f, product.Row2 * 16.0f, product.Row3);
                }
            }

            return texcoordMatrix;
        }

        protected void AddDotItem(Vector3 position, Vector3 color)
        {
            AddVolumeItem(new CollisionVolume(position, 0.1f), color, 1);
        }

        protected void AddVolumeItem(CollisionVolume volume, Vector3 color, float alpha = 0.5f)
        {
            if (!Selection.CheckVolume(Entity))
            {
                return;
            }

            Vector3[] verts = Array.Empty<Vector3>();
            if (volume.Type == VolumeType.Box)
            {
                verts = ArrayPool<Vector3>.Shared.Rent(8);
                Vector3 point0 = volume.BoxPosition;
                Vector3 sideX = volume.BoxVector1 * volume.BoxDot1;
                Vector3 sideY = volume.BoxVector2 * volume.BoxDot2;
                Vector3 sideZ = volume.BoxVector3 * volume.BoxDot3;
                verts[0] = point0;
                verts[1] = point0 + sideZ;
                verts[2] = point0 + sideX;
                verts[3] = point0 + sideX + sideZ;
                verts[4] = point0 + sideY;
                verts[5] = point0 + sideY + sideZ;
                verts[6] = point0 + sideX + sideY;
                verts[7] = point0 + sideX + sideY + sideZ;
            }
            else if (volume.Type == VolumeType.Cylinder)
            {
                verts = ArrayPool<Vector3>.Shared.Rent(34);
                Vector3 vector = volume.CylinderVector.Normalized();
                float radius = volume.CylinderRadius;
                Matrix3 rotation = Matrix.RotateAlign(Vector3.UnitY, vector);
                Vector3 start;
                Vector3 end;
                // cylinder volumes are always axis-aligned, so we can use this hack to avoid normal issues
                if (vector == Vector3.UnitX || vector == Vector3.UnitY || vector == Vector3.UnitZ)
                {
                    start = volume.CylinderPosition;
                    end = volume.CylinderPosition + vector * volume.CylinderDot;
                }
                else
                {
                    start = volume.CylinderPosition + vector * volume.CylinderDot;
                    end = volume.CylinderPosition;
                }

                for (int i = 0; i < 16; i++)
                {
                    verts[i] = GetDiscVertices(radius, i) * rotation + start;
                }

                for (int i = 0; i < 16; i++)
                {
                    verts[i + 16] = GetDiscVertices(radius, i) * rotation + end;
                }

                verts[32] = start;
                verts[33] = end;
            }
            else if (volume.Type == VolumeType.Sphere)
            {
                int stackCount = ScenePresentation.DisplaySphereStacks;
                int sectorCount = ScenePresentation.DisplaySphereSectors;
                verts = ArrayPool<Vector3>.Shared.Rent((stackCount + 1) * (sectorCount + 1));
                float radius = volume.SphereRadius;
                float sectorStep = 2 * MathF.PI / sectorCount;
                float stackStep = MathF.PI / stackCount;
                float sectorAngle, stackAngle, x, y, z, xy;
                for (int i = 0; i <= stackCount; i++)
                {
                    stackAngle = MathF.PI / 2 - i * stackStep;
                    xy = radius * MathF.Cos(stackAngle);
                    z = radius * MathF.Sin(stackAngle);
                    for (int j = 0; j <= sectorCount; j++)
                    {
                        sectorAngle = j * sectorStep;
                        x = xy * MathF.Cos(sectorAngle);
                        y = xy * MathF.Sin(sectorAngle);
                        verts[i * (sectorCount + 1) + j] = new Vector3(x, z, y) + volume.SpherePosition;
                    }
                }
            }

            CullingMode cullingMode = volume.TestPoint(Presentation.CameraPosition) ? CullingMode.Front : CullingMode.Back;
            Presentation.AddRenderItem(cullingMode, Presentation.GetNextPolygonId(), new Vector4(color, alpha), (RenderPrimitive)(volume.Type + 1), verts);
        }

        private Vector3 GetDiscVertices(float radius, int index)
        {
            return new Vector3(radius * MathF.Cos(2f * MathF.PI * index / 16f), 0.0f, radius * MathF.Sin(2f * MathF.PI * index / 16f));
        }

        protected void AddVectorItem(Vector3 point, Vector3 vector, Vector3 color)
        {
            CollisionVolume volume;
            if (vector == Vector3.Zero)
            {
                volume = new CollisionVolume(point, 0.1f);
            }
            else
            {
                volume = new CollisionVolume(vector.Normalized(), point, 0.05f, vector.Length);
            }

            AddVolumeItem(volume, color);
        }

        public virtual void GetDisplayVolumes()
        {
        }
    }
}
