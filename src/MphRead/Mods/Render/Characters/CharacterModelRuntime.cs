using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MphRead.Mods;
using MphRead.Formats;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render.Characters
{
    /// <summary>Scene-owned lazy native-suit albedos. Companions are shared across suits.</summary>
    internal sealed class CharacterAlbedoPalette
    {
        private readonly CharacterEmbeddedAlbedo _albedo;
        private readonly TextureAssetClass _assetClass;
        private readonly int _baseBinding;

        internal CharacterAlbedoPalette(CharacterEmbeddedAlbedo albedo, TextureAssetClass assetClass, int baseBinding)
        { _albedo = albedo; _assetClass = assetClass; _baseBinding = baseBinding; }

        internal int GetBinding(Scene scene, int recolor)
        {
            if (_albedo.Recolors == null || !_albedo.Recolors.TryGetValue(recolor, out var image)) return _baseBinding;
            int binding = scene.GetCharacterModelVariantTexture(image.Image, _assetClass, image.Opaque);
            // Scene leases resolve again after eviction; stale texture IDs cannot survive in a palette.
            return binding == 0 ? _baseBinding : binding;
        }
    }

    internal sealed record CharacterRigidRenderSegment(
        int NativeNodeIndex,
        int NativeMaterialIndex,
        int ListId,
        int? AlbedoBinding = null,
        RepeatMode WrapS = RepeatMode.Repeat,
        RepeatMode WrapT = RepeatMode.Repeat,
        MaterialMapBindings MaterialMaps = default,
        CharacterAlbedoPalette? AlbedoPalette = null, bool DoubleSided = false, bool Transparent = false)
    {
        internal int? GetAlbedo(Scene scene, int recolor) => AlbedoPalette?.GetBinding(scene, recolor) ?? AlbedoBinding;
    }

    internal sealed record CharacterWeightedRenderSegment(
        int NativeMaterialIndex,
        int ListId,
        int? AlbedoBinding = null,
        RepeatMode WrapS = RepeatMode.Repeat,
        RepeatMode WrapT = RepeatMode.Repeat,
        MaterialMapBindings MaterialMaps = default,
        CharacterAlbedoPalette? AlbedoPalette = null, bool DoubleSided = false, bool Transparent = false)
    {
        internal int? GetAlbedo(Scene scene, int recolor) => AlbedoPalette?.GetBinding(scene, recolor) ?? AlbedoBinding;
    }

    internal sealed record CharacterWeightedRenderJoint(
        int NativeNodeIndex,
        Matrix4 InverseBind);

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
#if !MPHREAD_SERVER
            foreach (CharacterRigidRenderSegment segment in Segments)
                if (segment.ListId != 0) GraphicsApi.DeleteLists(segment.ListId, 1);
#endif
        }
    }

    internal sealed class CharacterWeightedRenderModel
    {
        public CharacterModelAsset Asset { get; }
        public IReadOnlyList<CharacterWeightedRenderSegment> Segments { get; }
        public IReadOnlyList<CharacterWeightedRenderJoint> Joints { get; }
        public float[] MatrixPalette { get; }
        public int VertexCount { get; }
        public int IndexCount { get; }

        public CharacterWeightedRenderModel(CharacterModelAsset asset,
            IReadOnlyList<CharacterWeightedRenderSegment> segments,
            IReadOnlyList<CharacterWeightedRenderJoint> joints,
            int vertexCount, int indexCount)
        {
            Asset = asset;
            Segments = segments;
            Joints = joints;
            MatrixPalette = new float[16 * joints.Count];
            VertexCount = vertexCount;
            IndexCount = indexCount;
        }

        public void UpdatePalette(Model nativeModel)
        {
            for (int i = 0; i < Joints.Count; i++)
            {
                CharacterWeightedRenderJoint joint = Joints[i];
                Matrix4 matrix = joint.InverseBind * nativeModel.Nodes[joint.NativeNodeIndex].Animation;
                WriteMatrix(MatrixPalette, i * 16, matrix);
            }
        }

        public void Release()
        {
#if !MPHREAD_SERVER
            foreach (CharacterWeightedRenderSegment segment in Segments)
                if (segment.ListId != 0) GraphicsApi.DeleteLists(segment.ListId, 1);
#endif
        }

        private static void WriteMatrix(float[] target, int offset, Matrix4 matrix)
        {
            target[offset + 0] = matrix.M11; target[offset + 1] = matrix.M12;
            target[offset + 2] = matrix.M13; target[offset + 3] = matrix.M14;
            target[offset + 4] = matrix.M21; target[offset + 5] = matrix.M22;
            target[offset + 6] = matrix.M23; target[offset + 7] = matrix.M24;
            target[offset + 8] = matrix.M31; target[offset + 9] = matrix.M32;
            target[offset + 10] = matrix.M33; target[offset + 11] = matrix.M34;
            target[offset + 12] = matrix.M41; target[offset + 13] = matrix.M42;
            target[offset + 14] = matrix.M43; target[offset + 15] = matrix.M44;
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
            public readonly TextureAssetQuality TextureQuality = RenderOptions.TextureQuality;
            public readonly string SamplingKey = TextureSamplingPolicy.RuntimeKey;
            public readonly Dictionary<(string Path, int NativeModelId), CharacterRigidRenderModel> RigidModels = new();
            public readonly Dictionary<(string Path, int NativeModelId), CharacterWeightedRenderModel> WeightedModels = new();
            public readonly HashSet<(string Path, int NativeModelId)> Failed = new();

            public void Release()
            {
                foreach (CharacterRigidRenderModel model in RigidModels.Values) model.Release();
                foreach (CharacterWeightedRenderModel model in WeightedModels.Values) model.Release();
                RigidModels.Clear();
                WeightedModels.Clear();
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
            Model nativeModel, out CharacterRigidRenderModel model, int lod = 0)
        {
            model = null!;
            if (Headless.Active || !RenderOptions.CharacterModelReplacements) return false;

            CharacterModelPack pack = GetPack();
            if (_packIssue != null && !_packIssueLogged)
            {
                _packIssueLogged = true;
                DebugLog.Line("render", "HD character model pack disabled: " + _packIssue);
            }
            if (!pack.TryResolve(hunter, part, lod, out CharacterModelAsset asset)
                || asset.Skinning != CharacterSkinningMode.RigidNodes)
                return false;

            var key = (asset.ModelPath, nativeModel.Id);
            SceneResources resources = _scenes.GetValue(scene, _ => new SceneResources());
            if (resources.TextureQuality != RenderOptions.TextureQuality
                || resources.SamplingKey != TextureSamplingPolicy.RuntimeKey)
            {
                Release(scene);
                resources = _scenes.GetValue(scene, _ => new SceneResources());
            }
            if (resources.RigidModels.TryGetValue(key, out model!)) return true;
            if (resources.Failed.Contains(key)) return false;

            try
            {
                if (!CharacterModelPack.ValidateNativeRig(asset, nativeModel, out string? rigIssue))
                    throw new InvalidDataException(rigIssue);
                model = Compile(scene, asset, nativeModel);
                resources.RigidModels.Add(key, model);
                DebugLog.Line("render",
                    $"HD character ready: {hunter}/{part}/lod{asset.Lod}, {model.Segments.Count} segments, "
                    + $"{model.VertexCount} vertices, {model.IndexCount / 3} triangles");
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or InvalidDataException or ArgumentException or InvalidOperationException
                or JsonException or FormatException or OverflowException or KeyNotFoundException)
            {
                resources.Failed.Add(key);
                DebugLog.Line("render",
                    $"HD character fallback for {hunter}/{part}/lod{asset.Lod}: {ex.Message}");
                return false;
            }
        }

        public static bool TryGetWeighted(Scene scene, Hunter hunter, CharacterModelPart part,
            Model nativeModel, out CharacterWeightedRenderModel model, int lod = 0)
        {
            model = null!;
            if (Headless.Active || !RenderOptions.CharacterModelReplacements) return false;

            CharacterModelPack pack = GetPack();
            if (!pack.TryResolve(hunter, part, lod, out CharacterModelAsset asset)
                || asset.Skinning != CharacterSkinningMode.Weighted4)
                return false;

            var key = (asset.ModelPath, nativeModel.Id);
            SceneResources resources = _scenes.GetValue(scene, _ => new SceneResources());
            if (resources.TextureQuality != RenderOptions.TextureQuality
                || resources.SamplingKey != TextureSamplingPolicy.RuntimeKey)
            {
                Release(scene);
                resources = _scenes.GetValue(scene, _ => new SceneResources());
            }
            if (resources.WeightedModels.TryGetValue(key, out model!)) return true;
            if (resources.Failed.Contains(key)) return false;

            try
            {
                if (!CharacterModelPack.ValidateNativeRig(asset, nativeModel, out string? rigIssue))
                    throw new InvalidDataException(rigIssue);
                model = CompileWeighted(scene, asset, nativeModel);
                resources.WeightedModels.Add(key, model);
                DebugLog.Line("render",
                    $"HD weighted character ready: {hunter}/{part}/lod{asset.Lod}, "
                    + $"{model.Joints.Count} joints, {model.Segments.Count} segments, "
                    + $"{model.VertexCount} vertices, {model.IndexCount / 3} triangles");
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or InvalidDataException or ArgumentException or InvalidOperationException
                or JsonException or FormatException or OverflowException or KeyNotFoundException)
            {
                resources.Failed.Add(key);
                DebugLog.Line("render",
                    $"HD weighted character fallback for {hunter}/{part}/lod{asset.Lod}: {ex.Message}");
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
            scene.ClearCharacterModelTextures();
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

        private static CharacterRigidRenderModel Compile(Scene scene, CharacterModelAsset asset, Model nativeModel)
        {
#if MPHREAD_SERVER
            throw new InvalidOperationException("HD character geometry is unavailable in dedicated-server builds.");
#else
            CharacterRigidModelData geometry = CharacterRigidModelLoader.Load(asset);
            var nodeIndices = nativeModel.Nodes.Select((node, index) => (Name: node.Name, Index: index))
                .ToDictionary(value => value.Name, value => value.Index, StringComparer.Ordinal);
            var materialIndices = nativeModel.Materials.Select((material, index) => (Name: material.Name, Index: index))
                .GroupBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Index, StringComparer.OrdinalIgnoreCase);

            var compiled = new List<CharacterRigidRenderSegment>(geometry.Primitives.Count);
            var albedos = new Dictionary<CharacterEmbeddedAlbedo, int>();
            var materialMaps = new Dictionary<CharacterEmbeddedMaterialMaps, MaterialMapBindings>();
            TextureAssetClass assetClass = asset.Part == CharacterModelPart.ViewModel
                ? TextureAssetClass.Weapon : TextureAssetClass.Hunter;
            using var uploads = scene.BeginCharacterTextureUpload();
            try
            {
                // Reserve all base images before optional maps. Shared material
                // instances across rigid segments upload only once per scene.
                foreach (var primitive in geometry.Primitives)
                {
                    if (primitive.Albedo == null || albedos.ContainsKey(primitive.Albedo)) continue;
                    int binding = scene.GetCharacterModelTexture(asset.ModelPath + "/rigid-albedo/" + albedos.Count,
                        primitive.Albedo.Image, assetClass, primitive.Albedo.Opaque);
                    if (binding == 0) throw new CharacterTextureAdmissionException();
                    albedos.Add(primitive.Albedo, binding);
                }
                foreach (CharacterRigidPrimitive primitive in geometry.Primitives)
                {
                    if (!nodeIndices.TryGetValue(primitive.TargetNode, out int nodeIndex))
                        throw new InvalidDataException($"Native model no longer contains mapped node '{primitive.TargetNode}'.");
                    int materialIndex = ResolveMaterial(nativeModel, nodeIndex, primitive.MaterialName, materialIndices);
                    Material material = nativeModel.Materials[materialIndex];
                    if (asset.NativeSupplementMaterials.Contains(material.Name))
                        throw new InvalidDataException($"HD material '{material.Name}' duplicates its explicit native supplement.");
                    if (!CanUseAuthoredTexcoords(material.TexgenMode, primitive.Albedo != null))
                        throw new InvalidDataException(
                            $"HD primitive material '{material.Name}' uses native generated coordinates; rigid GLB UVs require None/Texcoord.");

                    MaterialMapBindings maps = default;
                    if (primitive.MaterialMaps != null && !materialMaps.TryGetValue(primitive.MaterialMaps, out maps))
                    {
                        maps = scene.GetCharacterModelMaterialMaps(asset.ModelPath + "/rigid-maps/" + materialMaps.Count,
                            primitive.MaterialMaps, assetClass);
                        materialMaps.Add(primitive.MaterialMaps, maps);
                    }
                    int list = CompileList(primitive);
                    compiled.Add(new(nodeIndex, materialIndex, list,
                        primitive.Albedo == null ? null : albedos[primitive.Albedo],
                        primitive.Albedo?.WrapS ?? RepeatMode.Repeat,
                        primitive.Albedo?.WrapT ?? RepeatMode.Repeat, maps,
                        primitive.Albedo?.Recolors?.Count > 0
                            ? new CharacterAlbedoPalette(primitive.Albedo, assetClass, albedos[primitive.Albedo]) : null, primitive.DoubleSided, primitive.Albedo?.Opaque == false));
                }
                var result = new CharacterRigidRenderModel(asset, compiled.ToArray(), geometry.VertexCount, geometry.IndexCount);
                uploads.Commit();
                return result;
            }
            catch
            {
                foreach (CharacterRigidRenderSegment segment in compiled)
                    if (segment.ListId != 0) GraphicsApi.DeleteLists(segment.ListId, 1);
                throw;
            }
#endif
        }

        // Embedded albedo submission explicitly selects authored Texcoord UVs.
        // Native-bound rigid assets still cannot replace generated-coordinate meshes.
        internal static bool CanUseAuthoredTexcoords(TexgenMode mode, bool embeddedAlbedo)
            => embeddedAlbedo || mode is TexgenMode.None or TexgenMode.Texcoord;

        private static CharacterWeightedRenderModel CompileWeighted(
            Scene scene, CharacterModelAsset asset, Model nativeModel)
        {
#if MPHREAD_SERVER
            throw new InvalidOperationException("HD character geometry is unavailable in dedicated-server builds.");
#else
            CharacterWeightedModelData geometry = CharacterWeightedModelLoader.Load(asset);
            var nodeIndices = nativeModel.Nodes.Select((node, index) => (Name: node.Name, Index: index))
                .ToDictionary(value => value.Name, value => value.Index, StringComparer.Ordinal);
            var materialIndices = nativeModel.Materials.Select((material, index) => (Name: material.Name, Index: index))
                .GroupBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Index, StringComparer.OrdinalIgnoreCase);

            var joints = new List<CharacterWeightedRenderJoint>(geometry.Joints.Count);
            foreach (CharacterWeightedJoint joint in geometry.Joints)
            {
                if (!nodeIndices.TryGetValue(joint.TargetNode, out int nativeNodeIndex))
                    throw new InvalidDataException(
                        $"Native model no longer contains weighted target node '{joint.TargetNode}'.");
                joints.Add(new(nativeNodeIndex, joint.InverseBind));
            }

            var compiled = new List<CharacterWeightedRenderSegment>(geometry.Primitives.Count);
            using var uploads = scene.BeginCharacterTextureUpload();
            try
            {
                foreach (CharacterWeightedPrimitive primitive in geometry.Primitives)
                {
                    if (String.IsNullOrWhiteSpace(primitive.MaterialName)
                        || !materialIndices.TryGetValue(primitive.MaterialName, out int materialIndex))
                        throw new InvalidDataException(
                            $"Weighted HD material '{primitive.MaterialName ?? "(unnamed)"}' "
                            + $"does not match a native material in {nativeModel.Name}.");
                    Material material = nativeModel.Materials[materialIndex];
                    if (asset.NativeSupplementMaterials.Contains(material.Name))
                        throw new InvalidDataException($"HD material '{material.Name}' duplicates its explicit native supplement.");
                    if (!CanUseAuthoredTexcoords(material.TexgenMode, primitive.Albedo != null))
                        throw new InvalidDataException(
                            $"Weighted HD material '{material.Name}' uses generated native coordinates; "
                            + "weighted GLB UVs require None/Texcoord.");

                    int? albedo = null;
                    if (primitive.Albedo != null)
                    {
                        int binding = scene.GetCharacterModelTexture(
                            asset.ModelPath + "/" + compiled.Count, primitive.Albedo.Image,
                            asset.Part == CharacterModelPart.ViewModel
                                ? TextureAssetClass.Weapon : TextureAssetClass.Hunter,
                            primitive.Albedo.Opaque);
                        if (binding == 0)
                            throw new CharacterTextureAdmissionException();
                        albedo = binding;
                    }
                    MaterialMapBindings maps = primitive.MaterialMaps == null ? default
                        : scene.GetCharacterModelMaterialMaps(asset.ModelPath + "/" + compiled.Count,
                            primitive.MaterialMaps, asset.Part == CharacterModelPart.ViewModel
                                ? TextureAssetClass.Weapon : TextureAssetClass.Hunter);
                    int list = CompileWeightedList(primitive);
                    compiled.Add(new(materialIndex, list, albedo,
                        primitive.Albedo?.WrapS ?? RepeatMode.Repeat,
                        primitive.Albedo?.WrapT ?? RepeatMode.Repeat, maps,
                        primitive.Albedo?.Recolors?.Count > 0 && albedo.HasValue
                            ? new CharacterAlbedoPalette(primitive.Albedo, asset.Part == CharacterModelPart.ViewModel
                                ? TextureAssetClass.Weapon : TextureAssetClass.Hunter, albedo.Value) : null, primitive.DoubleSided, primitive.Albedo?.Opaque == false));
                }
                var result = new CharacterWeightedRenderModel(asset, compiled.ToArray(), joints.ToArray(),
                    geometry.VertexCount, geometry.IndexCount);
                uploads.Commit();
                return result;
            }
            catch
            {
                foreach (CharacterWeightedRenderSegment segment in compiled)
                    if (segment.ListId != 0) GraphicsApi.DeleteLists(segment.ListId, 1);
                throw;
            }
#endif
        }

        private static int CompileWeightedList(CharacterWeightedPrimitive primitive)
        {
#if MPHREAD_SERVER
            throw new InvalidOperationException("HD character geometry is unavailable in dedicated-server builds.");
#else
            int list = GraphicsApi.GenLists(1);
            if (list == 0)
                throw new InvalidOperationException("Renderer could not allocate weighted HD character geometry.");
            try
            {
                GraphicsApi.NewList(list, ListMode.Compile);
                GraphicsApi.TexCoord3(0, 0, 0);
                GraphicsApi.Normal3(0, 1, 0);
                GraphicsApi.Begin(PrimitiveType.Triangles);
                foreach (uint rawIndex in primitive.Indices)
                {
                    CharacterWeightedVertex vertex = primitive.Vertices[(int)rawIndex];
                    GraphicsApi.Color4(vertex.Weights.X, vertex.Weights.Y,
                        vertex.Weights.Z, vertex.Weights.W);
                    GraphicsApi.Normal3(vertex.Normal.X, vertex.Normal.Y, vertex.Normal.Z);
                    GraphicsApi.TexCoord3(vertex.Texcoord.X, vertex.Texcoord.Y, vertex.PackedJoints);
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
#endif
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
            if (node.MeshCount <= 0 || start < 0 || start >= nativeModel.Meshes.Count)
                throw new InvalidDataException(
                    $"HD node '{node.Name}' has no native mesh from which to inherit a material.");
            return nativeModel.Meshes[start].MaterialId;
        }

        private static int CompileList(CharacterRigidPrimitive primitive)
        {
#if MPHREAD_SERVER
            throw new InvalidOperationException("HD character geometry is unavailable in dedicated-server builds.");
#else
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
                    GraphicsApi.Color4(vertex.Color.X, vertex.Color.Y, vertex.Color.Z, 1);
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
#endif
        }
    }
}
