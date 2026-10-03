using System;
using System.Runtime.CompilerServices;

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

            internal RetainedRoomMeshTemplate(Mesh mesh, Material material)
            {
                Mesh = mesh;
                Material = material;
                MaterialId = mesh.MaterialId;
                ListId = mesh.ListId;
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

        // Weak keys matter across room rotation: RoomEntity can survive a
        // transition, but an old Model/Node graph must not be kept alive just
        // because it was once visible.
        private readonly ConditionalWeakTable<Node, RetainedRoomNodeTemplate>
            _retainedRoomNodeTemplates = new();

        private long _retainedRoomTemplateBuilds;
        private long _retainedRoomTemplateHits;

        internal long RetainedRoomTemplateBuilds => _retainedRoomTemplateBuilds;
        internal long RetainedRoomTemplateHits => _retainedRoomTemplateHits;

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
    }
}
