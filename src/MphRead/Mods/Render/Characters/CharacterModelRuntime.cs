using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using OpenTK.Graphics.OpenGL;

namespace MphRead.Mods.Render.Characters
{
    internal sealed record CharacterRigidRenderSegment(
        int NativeNodeIndex,
        int NativeMaterialIndex,
        int ListId);

    internal sealed class CharacterRigidRenderModel
    {
        public CharacterModelAsset Asset { get; }
        public IReadOnlyList<CharacterRigidRenderSegment> Segments { get; }
        public int VertexCount { get; }
        public int IndexCount { get; }

        public CharacterRigidRenderModel(CharacterModelAsset asset,
            IReadOnlyList<CharacterRigidRenderSegment> segments, int vertexCount, int indexCount)
        {
            Asset = asset;
            Segments = segments;
            VertexCount = vertexCount;
            IndexCount = indexCount;
        }

        public void Release()
        {
            foreach (CharacterRigidRenderSegment segment in Segments)
                if (segment.ListId != 0) GraphicsApi.DeleteLists(segment.ListId, 1);
        }
    }

    /// <summary>
    /// Scene-owned GPU bridge for local HD character geometry. The pack and CPU
    /// file contract are process-local, while display-list/native-buffer handles
    /// are owned by the scene/context that created them.
    /// </summary>
    internal static class CharacterModelRuntime
    {
        private sealed class SceneResources
        {
            public readonly Dictionary<(string Path, int NativeModelId), CharacterRigidRenderModel> Models = new();
            public readonly HashSet<(string Path, int NativeModelId)> Failed = new();

            public void Release()
            {
                foreach (CharacterRigidRenderModel model in Models.Values) model.Release();
                Models.Clear();
                Failed.Clear();
            }
        }

        private static readonly ConditionalWeakTable<Scene, SceneResources> _scenes = new();
        private static readonly object _packLock = new();
        private static CharacterModelPack? _pack;
        private static bool _packLoaded;
        private static string? _packIssue;
        private static bool _packIssueLogged;

        public static bool TryGetRigid(Scene scene, Hunter hunter, CharacterModelPart part,
            Model nativeModel, out CharacterRigidRenderModel model)
        {
            model = null!;
            if (Mods.Headless.Active || !RenderOptions.CharacterModelReplacements) return false;

            CharacterModelPack pack = GetPack();
            if (_packIssue != null && !_packIssueLogged)
            {
                _packIssueLogged = true;
                DebugLog.Line("render", "HD character model pack disabled: " + _packIssue);
            }
            if (!pack.TryResolve(hunter, part, out CharacterModelAsset asset)
                || asset.Skinning != CharacterSkinningMode.RigidNodes)
                return false;

            var key = (asset.ModelPath, nativeModel.Id);
            SceneResources resources = _scenes.GetValue(scene, _ => new SceneResources());
            if (resources.Models.TryGetValue(key, out model!)) return true;
            if (resources.Failed.Contains(key)) return false;

            try
            {
                if (!CharacterModelPack.ValidateNativeRig(asset, nativeModel, out string? rigIssue))
                    throw new InvalidDataException(rigIssue);
                model = Compile(asset, nativeModel);
                resources.Models.Add(key, model);
                DebugLog.Line("render",
                    $"HD character ready: {hunter}/{part}, {model.Segments.Count} segments, "
                    + $"{model.VertexCount} vertices, {model.IndexCount / 3} triangles");
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or InvalidDataException or ArgumentException or InvalidOperationException)
            {
                resources.Failed.Add(key);
                DebugLog.Line("render", $"HD character fallback for {hunter}/{part}: {ex.Message}");
                return false;
            }
        }

        public static void Release(Scene scene)
        {
            if (_scenes.TryGetValue(scene, out SceneResources? resources))
            {
                resources.Release();
                _scenes.Remove(scene);
            }
        }

        internal static void ResetPackForCheck()
        {
            lock (_packLock)
            {
                _pack = null;
                _packLoaded = false;
                _packIssue = null;
                _packIssueLogged = false;
            }
        }

        private static CharacterModelPack GetPack()
        {
            lock (_packLock)
            {
                if (!_packLoaded)
                {
                    _pack = CharacterModelPack.LoadDefault(out _packIssue);
                    _packLoaded = true;
                }
                return _pack!;
            }
        }

        private static CharacterRigidRenderModel Compile(CharacterModelAsset asset, Model nativeModel)
        {
            CharacterRigidModelData geometry = CharacterRigidModelLoader.Load(asset);
            var nodeIndices = nativeModel.Nodes.Select((node, index) => (node.Name, index))
                .ToDictionary(value => value.Name, value => value.index, StringComparer.Ordinal);
            var materialIndices = nativeModel.Materials.Select((material, index) => (material.Name, index))
                .GroupBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().index, StringComparer.OrdinalIgnoreCase);

            var compiled = new List<CharacterRigidRenderSegment>(geometry.Primitives.Count);
            try
            {
                foreach (CharacterRigidPrimitive primitive in geometry.Primitives)
                {
                    if (!nodeIndices.TryGetValue(primitive.TargetNode, out int nodeIndex))
                        throw new InvalidDataException($"Native model no longer contains mapped node '{primitive.TargetNode}'.");
                    int materialIndex = ResolveMaterial(nativeModel, nodeIndex, primitive.MaterialName, materialIndices);
                    Material material = nativeModel.Materials[materialIndex];
                    if (material.TexgenMode is TexgenMode.Normal or TexgenMode.Vertex)
                        throw new InvalidDataException(
                            $"HD primitive material '{material.Name}' uses native generated coordinates; rigid GLB UVs require None/Texcoord.");

                    int list = CompileList(primitive);
                    compiled.Add(new(nodeIndex, materialIndex, list));
                }
                return new(asset, compiled.ToArray(), geometry.VertexCount, geometry.IndexCount);
            }
            catch
            {
                foreach (CharacterRigidRenderSegment segment in compiled)
                    if (segment.ListId != 0) GraphicsApi.DeleteLists(segment.ListId, 1);
                throw;
            }
        }

        private static int ResolveMaterial(Model nativeModel, int nodeIndex, string? materialName,
            IReadOnlyDictionary<string, int> materialIndices)
        {
            if (!String.IsNullOrWhiteSpace(materialName))
            {
                if (!materialIndices.TryGetValue(materialName, out int mapped))
                    throw new InvalidDataException(
                        $"HD material '{materialName}' does not match a native material in {nativeModel.Name}.");
                return mapped;
            }

            Node node = nativeModel.Nodes[nodeIndex];
            int start = node.MeshId / 2;
            if (node.MeshCount <= 0 || (uint)start >= nativeModel.Meshes.Count)
                throw new InvalidDataException(
                    $"HD node '{node.Name}' has no native mesh from which to inherit a material.");
            return nativeModel.Meshes[start].MaterialId;
        }

        private static int CompileList(CharacterRigidPrimitive primitive)
        {
            int list = GraphicsApi.GenLists(1);
            if (list == 0) throw new InvalidOperationException("Renderer could not allocate HD character geometry.");
            try
            {
                GraphicsApi.NewList(list, ListMode.Compile);
                GraphicsApi.Color4(1, 1, 1, 1);
                GraphicsApi.TexCoord3(0, 0, 0);
                GraphicsApi.Normal3(0, 1, 0);
                GraphicsApi.Begin(PrimitiveType.Triangles);
                foreach (uint rawIndex in primitive.Indices)
                {
                    CharacterRigidVertex vertex = primitive.Vertices[(int)rawIndex];
                    GraphicsApi.Normal3(vertex.Normal.X, vertex.Normal.Y, vertex.Normal.Z);
                    GraphicsApi.TexCoord2(vertex.Texcoord.X, vertex.Texcoord.Y);
                    GraphicsApi.Vertex3(vertex.Position.X, vertex.Position.Y, vertex.Position.Z);
                }
                GraphicsApi.End();
                GraphicsApi.EndList();
                return list;
            }
            catch
            {
                try { GraphicsApi.End(); } catch { }
                try { GraphicsApi.EndList(); } catch { }
                GraphicsApi.DeleteLists(list, 1);
                throw;
            }
        }
    }
}
