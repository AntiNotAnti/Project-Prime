using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MphRead.Formats.Culling;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    /// <summary>
    /// Retained room-submission metadata. Portal/frustum visibility remains
    /// dynamic; this cache removes repeated mesh-range/material resolution once
    /// a visible node reaches submission.
    /// </summary>
    public partial class RoomEntity
    {
        private readonly struct RetainedRoomMeshTemplate
        {
            internal Mesh Mesh { get; }
            internal Material Material { get; }
            internal int MaterialId { get; }
            internal int ListId { get; }
            internal RenderItem Item { get; }
            // Independent from the camera's RenderItem: preparing a caster
            // must never overwrite an already-captured world packet.
            internal RenderItem ShadowItem { get; }

            internal RetainedRoomMeshTemplate(Mesh mesh, Material material)
            {
                Mesh = mesh;
                Material = material;
                MaterialId = mesh.MaterialId;
                ListId = mesh.ListId;
                Item = new RenderItem();
                ShadowItem = new RenderItem();
            }
        }

        private sealed class RetainedRoomNodeTemplate
        {
            internal RetainedRoomMeshTemplate[] Meshes { get; }

            internal RetainedRoomNodeTemplate(RetainedRoomMeshTemplate[] meshes)
            {
                Meshes = meshes;
            }
        }

        private sealed class RetainedRoomVisibilityCluster
        {
            internal Node[] Nodes { get; }
            internal float[] Bounds { get; }

            internal RetainedRoomVisibilityCluster(Node[] nodes, float[] bounds)
            {
                Nodes = nodes;
                Bounds = bounds;
            }
        }

        private sealed class RetainedRoomPartTemplate
        {
            internal RetainedRoomVisibilityCluster[] Clusters { get; }

            internal RetainedRoomPartTemplate(RetainedRoomVisibilityCluster[] clusters)
            {
                Clusters = clusters;
            }
        }

        // Weak keys matter across room rotation: RoomEntity can survive a
        // transition, but an old Model/Node graph must not be kept alive just
        // because it was once visible.
        private readonly ConditionalWeakTable<Node, RetainedRoomNodeTemplate>
            _retainedRoomNodeTemplates = new();
        private readonly ConditionalWeakTable<Node, RetainedRoomPartTemplate>
            _retainedRoomPartTemplates = new();

        private long _retainedRoomTemplateBuilds;
        private long _retainedRoomTemplateHits;
        private long _retainedRoomPacketSubmissions;
        private long _retainedRoomClusterBuilds;
        private long _retainedRoomClusterTests;
        private long _retainedRoomClusterRejects;
        private long _retainedRoomNodeVisibilityTests;

        internal long RetainedRoomTemplateBuilds => _retainedRoomTemplateBuilds;
        internal long RetainedRoomTemplateHits => _retainedRoomTemplateHits;
        internal long RetainedRoomPacketSubmissions => _retainedRoomPacketSubmissions;
        internal long RetainedRoomClusterBuilds => _retainedRoomClusterBuilds;
        internal long RetainedRoomClusterTests => _retainedRoomClusterTests;
        internal long RetainedRoomClusterRejects => _retainedRoomClusterRejects;
        internal long RetainedRoomNodeVisibilityTests => _retainedRoomNodeVisibilityTests;

        private ReadOnlySpan<RetainedRoomMeshTemplate> RetainedMeshes(Model model, Node node)
        {
            if (_retainedRoomNodeTemplates.TryGetValue(node, out RetainedRoomNodeTemplate? found))
            {
                _retainedRoomTemplateHits++;
                return found.Meshes;
            }

            int start = node.MeshId / 2;
            var meshes = new RetainedRoomMeshTemplate[node.MeshCount];
            for (int i = 0; i < meshes.Length; i++)
            {
                Mesh mesh = model.Meshes[start + i];
                meshes[i] = new RetainedRoomMeshTemplate(
                    mesh, model.Materials[mesh.MaterialId]);
            }

            var built = new RetainedRoomNodeTemplate(meshes);
            _retainedRoomNodeTemplates.Add(node, built);
            _retainedRoomTemplateBuilds++;
            return meshes;
        }

        private ReadOnlySpan<RetainedRoomVisibilityCluster> RetainedVisibilityClusters(
            Model model, int startNodeIndex)
        {
            Node first = model.Nodes[startNodeIndex];
            if (_retainedRoomPartTemplates.TryGetValue(
                first, out RetainedRoomPartTemplate? found))
            {
                return found.Clusters;
            }

            const int nodesPerCluster = 8;
            var clusters = new List<RetainedRoomVisibilityCluster>();
            var nodes = new List<Node>(nodesPerCluster);
            Vector3 min = new(float.PositiveInfinity);
            Vector3 max = new(float.NegativeInfinity);
            int nodeIndex = startNodeIndex;
            while (nodeIndex != -1)
            {
                Node node = model.Nodes[nodeIndex];
                nodes.Add(node);
                min = Vector3.ComponentMin(min, node.MinBounds);
                max = Vector3.ComponentMax(max, node.MaxBounds);
                nodeIndex = node.NextIndex;

                if (nodes.Count == nodesPerCluster || nodeIndex == -1)
                {
                    clusters.Add(new RetainedRoomVisibilityCluster(
                        nodes.ToArray(),
                        new[]
                        {
                            min.X, min.Y, min.Z,
                            max.X, max.Y, max.Z
                        }));
                    nodes.Clear();
                    min = new Vector3(float.PositiveInfinity);
                    max = new Vector3(float.NegativeInfinity);
                }
            }

            var built = new RetainedRoomPartTemplate(clusters.ToArray());
            _retainedRoomPartTemplates.Add(first, built);
            _retainedRoomClusterBuilds++;
            return built.Clusters;
        }


        // Shadow selection is light-space rather than portal/camera visible.
        // Check only light X/Y to conservatively retain casters beyond the
        // camera frustum or shadow depth slab. A little margin covers slopes.
        internal static bool ShadowCasterOverlapsLightXY(
            Vector3 min, Vector3 max, Matrix4 lightViewProjection)
        {
            if (!float.IsFinite(min.X) || !float.IsFinite(min.Y)
                || !float.IsFinite(min.Z) || !float.IsFinite(max.X)
                || !float.IsFinite(max.Y) || !float.IsFinite(max.Z)
                || min.X > max.X || min.Y > max.Y || min.Z > max.Z)
                return true; // Unknown bounds must never lose casters.

            min -= new Vector3(2f);
            max += new Vector3(2f);
            bool left = true, right = true, below = true, above = true;
            for (int i = 0; i < 8; i++)
            {
                float x = (i & 1) == 0 ? min.X : max.X;
                float y = (i & 2) == 0 ? min.Y : max.Y;
                float z = (i & 4) == 0 ? min.Z : max.Z;
                float cx = x * lightViewProjection.M11 + y * lightViewProjection.M21
                    + z * lightViewProjection.M31 + lightViewProjection.M41;
                float cy = x * lightViewProjection.M12 + y * lightViewProjection.M22
                    + z * lightViewProjection.M32 + lightViewProjection.M42;
                float cw = x * lightViewProjection.M14 + y * lightViewProjection.M24
                    + z * lightViewProjection.M34 + lightViewProjection.M44;
                if (!float.IsFinite(cx) || !float.IsFinite(cy)
                    || !float.IsFinite(cw) || cw <= 0)
                    return true;
                left &= cx < -cw;
                right &= cx > cw;
                below &= cy < -cw;
                above &= cy > cw;
            }
            return !(left || right || below || above);
        }

        /// <summary>
        /// Rebuild the static-room shadow caster set independently of the
        /// camera portal list. Scene.OnDrawFrame has already refreshed model
        /// animation/material state. ShadowItems belong to each room mesh
        /// template and never enter the ordinary frame draw/packet pool.
        /// </summary>
        internal bool CollectLightSpaceShadowCasters(Matrix4 lightView,
            Matrix4 lightProjection, List<RenderItem> output)
        {
            output.Clear();
            if (Hidden || _models.Count == 0 || _scene.GameState.InRoomTransition)
                return false;

            Matrix4 lightViewProjection = lightView * lightProjection;
            ModelInstance main = _models[0];
            if (main.Active)
                CollectModelShadowCasters(main, Vector3.Zero,
                    Matrix4.Identity, lightViewProjection, cull: true, output);
            for (int index = 0; index < _connectorModels.Count; index++)
            {
                ModelInstance connector = _connectorModels[index];
                if (!connector.Active || index + 1 >= _roomCollision.Count)
                    continue;
                Vector3 offset = _roomCollision[index + 1].Translation;
                Matrix4 transform = Matrix4.CreateScale(connector.Model.Scale);
                transform.Row3.Xyz = offset;
                // Connector bounds may be authored in a different model scale.
                // Include conservatively until all connector packs are checked.
                CollectModelShadowCasters(connector, offset, transform,
                    lightViewProjection, cull: false, output);
            }
            return true;
        }

        private void CollectModelShadowCasters(ModelInstance instance,
            Vector3 offset, Matrix4 connectorTransform,
            Matrix4 lightViewProjection, bool cull, List<RenderItem> output)
        {
            Model model = instance.Model;
            LightInfo lightInfo = GetLightInfo();
            int matrixStackCount = model.NodeMatrixIds.Count;
            IReadOnlyList<float> matrixStack = model.MatrixStackValues;
            for (int i = 0; i < model.Nodes.Count; i++)
            {
                Node node = model.Nodes[i];
                if (!node.Enabled || node.MeshCount <= 0)
                    continue;
                if (cull && !ShadowCasterOverlapsLightXY(
                        node.MinBounds, node.MaxBounds, lightViewProjection))
                    continue;
                Matrix4 transform = cull ? node.Animation : connectorTransform;
                ReadOnlySpan<RetainedRoomMeshTemplate> templates =
                    RetainedMeshes(model, node);
                for (int k = 0; k < templates.Length; k++)
                {
                    ref readonly RetainedRoomMeshTemplate template = ref templates[k];
                    Material material = template.Material;
                    if (!template.Mesh.Visible
                        || material.RenderMode == RenderMode.Decal
                        || material.RenderMode == RenderMode.Translucent
                        || material.CurrentAlpha < .999f)
                        continue;
                    Matrix4 texcoordMatrix = GetTexcoordMatrix(instance,
                        material, template.MaterialId, node);
                    _scene.AddRetainedRoomRenderItem(
                        template.ShadowItem, material, polygonId: 0,
                        alphaScale: 1f, emission: Vector3.Zero, lightInfo,
                        texcoordMatrix, transform, template.ListId,
                        matrixStackCount, matrixStack, SelectionType.None,
                        node.BillboardMode, retainedGpuVisibilityEligible: false,
                        node.MinBounds + offset, node.MaxBounds + offset,
                        submit: false);
                    output.Add(template.ShadowItem);
                }
            }
        }

        private bool RetainedClusterVisible(
            FrustumInfo frustumInfo, RetainedRoomVisibilityCluster cluster,
            Vector3 offset)
        {
            _retainedRoomClusterTests++;
            float[] bounds = cluster.Bounds;
            for (int i = 0; i < frustumInfo.Count; i++)
            {
                FrustumPlane frustumPlane = frustumInfo.Planes[i];
                Vector4 plane = frustumPlane.Plane;
                if (plane.X * (bounds[frustumPlane.XIndex2] + offset.X)
                    + plane.Y * (bounds[frustumPlane.YIndex2] + offset.Y)
                    + plane.Z * (bounds[frustumPlane.ZIndex2] + offset.Z)
                    - plane.W < 0)
                {
                    _retainedRoomClusterRejects++;
                    return false;
                }
            }
            return true;
        }
    }
}
