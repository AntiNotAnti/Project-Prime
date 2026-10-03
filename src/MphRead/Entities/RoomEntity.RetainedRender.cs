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

            internal RetainedRoomMeshTemplate(Mesh mesh, Material material)
            {
                Mesh = mesh;
                Material = material;
                MaterialId = mesh.MaterialId;
                ListId = mesh.ListId;
                Item = new RenderItem();
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
