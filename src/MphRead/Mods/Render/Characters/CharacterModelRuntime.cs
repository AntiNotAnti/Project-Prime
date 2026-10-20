using CharacterSourcePreviewLease = MphRead.Mods.Render.Characters.CharacterModelPack.SourcePreviewLease;
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
        CharacterAlbedoPalette? AlbedoPalette = null, bool DoubleSided = false, bool Transparent = false, IReadOnlyList<int>? SourceJointPalette = null, float[]? SourceMatrixPalette = null, string? SourceMaterialName = null, int SourcePrimitiveOrdinal = -1, CharacterSourceGxBinding? SourceGx = null)
    {
        // GPU Weighted4 consumes only its matrix stack. Keep raw source skin matrices
        // intact and reuse a separate bounded world-composed buffer for submission.
        internal float[]? SourcePresentationPalette { get; } = SourceMatrixPalette == null
            ? null : new float[SourceMatrixPalette.Length];
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
        private readonly CharacterWeightedModelData? _sourceGeometry;
        internal bool IsSourcePreview => _sourceGeometry?.SourceRig != null;
        internal Matrix4 DiagnosticPreviewFit { get; }
        internal CharacterSourceRig? SourceRig => _sourceGeometry?.SourceRig;
        internal CharacterSourceCannonRecipe? SourceCannon => _sourceGeometry?.SourceCannon;
        internal void UpdateSourcePreviewPalette(IReadOnlyList<Matrix4>? localPose = null)
        {
            if (_sourceGeometry?.SourceRig is not { } rig) throw new InvalidOperationException("Model is not a source preview.");
            // Capture immutable draw-only input; no gameplay clock or native pose writes.
            if(SourceCannon!=null && localPose!=null)throw new InvalidDataException("Fixed source cannon pose cannot be overridden.");
            var captured = (SourceCannon==null?localPose:_sourceGeometry.SourceAuthoringPose)?.ToArray();
            var full = rig.Evaluate(captured, _sourceGeometry.Joints);
            var palettes = _sourceGeometry.Primitives.Select(p=>CharacterSourceRig.GatherSegmentPalette(p,full)).ToArray();
            for(int i=0;i<Segments.Count;i++)
            {
                if(Segments[i].SourceMatrixPalette is not { } buffer || buffer.Length!=palettes[i].Length)
                    throw new InvalidDataException("Source segment palette contract changed.");
                Array.Copy(palettes[i],buffer,palettes[i].Length);
            }
        }
        internal void PrepareSourcePreviewPresentation(Matrix4 presentationPose)
        {
            if (!IsSourcePreview) throw new InvalidOperationException("Native palettes already contain their owner pose.");
            // The existing preview owner supplies positive uniform zoom and rotation.
            // Preserve the shader's normalized mat3 normal convention; no new
            // nonuniform-scale, mirrored, perspective or world-clock contract.
            for (int row = 0; row < 4; row++)
                for (int column = 0; column < 4; column++)
                    if (!float.IsFinite(presentationPose[row, column])
                        || MathF.Abs(presentationPose[row, column]) > 100000)
                        throw new InvalidDataException("Invalid source presentation transform.");
            Vector3 x = presentationPose.Row0.Xyz, y = presentationPose.Row1.Xyz, z = presentationPose.Row2.Xyz;
            float scaleSquared = x.LengthSquared;
            float tolerance = scaleSquared * .00001f;
            if (scaleSquared < .00000001f || MathF.Abs(y.LengthSquared - scaleSquared) > tolerance
                || MathF.Abs(z.LengthSquared - scaleSquared) > tolerance
                || MathF.Abs(Vector3.Dot(x, y)) > tolerance || MathF.Abs(Vector3.Dot(x, z)) > tolerance
                || MathF.Abs(Vector3.Dot(y, z)) > tolerance || Vector3.Dot(Vector3.Cross(x, y), z) <= 0
                || presentationPose.M14 != 0 || presentationPose.M24 != 0 || presentationPose.M34 != 0
                || presentationPose.M44 != 1)
                throw new InvalidDataException("Source presentation requires positive uniform scale and an affine rotation.");
            Matrix4 sourcePresentation=SourceCannon==null?presentationPose:SourceCannon.ToCannon*presentationPose;
            for (int segmentIndex = 0; segmentIndex < Segments.Count; segmentIndex++)
            {
                CharacterWeightedRenderSegment segment = Segments[segmentIndex];
                if (segment.SourceMatrixPalette is not { Length: > 0 and <= 512 } raw
                    || raw.Length % 16 != 0 || segment.SourceJointPalette?.Count * 16 != raw.Length
                    || segment.SourcePresentationPalette is not { } target || target.Length != raw.Length
                    || ReferenceEquals(raw, target))
                    throw new InvalidDataException("Source presentation palette exceeds the 32-slot contract.");
                for (int offset = 0; offset < raw.Length; offset += 16)
                {
                    var skin = new Matrix4(raw[offset], raw[offset+1], raw[offset+2], raw[offset+3],
                        raw[offset+4], raw[offset+5], raw[offset+6], raw[offset+7],
                        raw[offset+8], raw[offset+9], raw[offset+10], raw[offset+11],
                        raw[offset+12], raw[offset+13], raw[offset+14], raw[offset+15]);
                    // OpenTK row-vector order; GL's transpose=false interpretation
                    // supplies world * skin to the existing column-vector shader.
                    WriteMatrix(target, offset, skin * sourcePresentation);
                }
            }
        }
        public int VertexCount { get; }
        public int IndexCount { get; }

        public CharacterWeightedRenderModel(CharacterModelAsset asset,
            IReadOnlyList<CharacterWeightedRenderSegment> segments,
            IReadOnlyList<CharacterWeightedRenderJoint> joints,
            int vertexCount, int indexCount, CharacterWeightedModelData? sourceGeometry = null)
        {
            _sourceGeometry = sourceGeometry;
            DiagnosticPreviewFit = Matrix4.Identity;
            if (sourceGeometry?.SourceRig != null)
            {
                // A cannon cut preserves complete source vertex arrays. Its
                // diagnostic bounds must use only retained referenced corners,
                // after the fixed skin and the single source-to-cannon basis.
                var positions = sourceGeometry.SourceCannon == null
                    ? sourceGeometry.Primitives.SelectMany(p => p.Vertices).Select(v => v.Position).ToArray()
                    : CannonReferencedPositions(sourceGeometry).ToArray();
                var min = positions.Aggregate(System.Numerics.Vector3.Min); var max = positions.Aggregate(System.Numerics.Vector3.Max);
                float height = sourceGeometry.SourceCannon == null ? max.Y - min.Y : (max - min).Length();
                if (float.IsFinite(height) && height >= .0001f)
                {
                    var center = (min + max) * .5f;
                    float scale = Math.Clamp(2.2f / height, .001f, 1000f);
                    DiagnosticPreviewFit = sourceGeometry.SourceCannon == null
                        ? Matrix4.CreateTranslation(-center.X, -min.Y, -center.Z) * Matrix4.CreateScale(scale)
                        : Matrix4.CreateTranslation(-center.X, -center.Y, -center.Z) * Matrix4.CreateScale(scale)
                            * Matrix4.CreateTranslation(0, 1.05f, 0);
                }
            }
            Asset = asset;
            Segments = segments;
            Joints = joints;
            MatrixPalette = new float[16 * joints.Count];
            VertexCount = vertexCount;
            IndexCount = indexCount;
        }

        private static IEnumerable<System.Numerics.Vector3> CannonReferencedPositions(CharacterWeightedModelData source)
        {
            var skin = source.SourceRig!.Evaluate(source.SourceAuthoringPose, source.Joints);
            foreach (var p in source.Primitives)
                foreach (uint index in p.Indices)
                {
                    var vertex = p.Vertices[index]; var result = Vector3.Zero;
                    for (int slot = 0; slot < 4; slot++)
                    {
                        float weight = vertex.Weights[slot]; if (weight == 0) continue;
                        int joint = p.JointPalette![vertex.PackedJoints >> (slot * 5) & 31];
                        var position = new Vector3(vertex.Position.X, vertex.Position.Y, vertex.Position.Z);
                        result += Vector3.TransformPosition(position, skin[joint] * source.SourceCannon!.ToCannon) * weight;
                    }
                    yield return new System.Numerics.Vector3(result.X, result.Y, result.Z);
                }
        }

        public void UpdatePalette(Model nativeModel)
        {
            if(IsSourcePreview) throw new InvalidOperationException("Source preview requires its complete source pose.");
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
    internal sealed class CharacterSourcePreviewInstance
    {
        internal readonly string? MaterialAnchorName;
        internal readonly CharacterModelPart Part;
        internal readonly object? StageOwner;
        internal object? PublishedContext;
        internal CharacterSourcePreviewInstance(string? materialAnchorName = null,CharacterModelPart part=CharacterModelPart.Biped,object? stageOwner=null)
            =>(MaterialAnchorName,Part,StageOwner)=(materialAnchorName,part,stageOwner);
    }

    internal static partial class CharacterModelRuntime
    {
        private sealed record SourceEntry(CharacterWeightedRenderModel Model,CharacterSourcePreviewLease Lease,Model NativeModel,
            SourceMaterialAttachment? Attachment);
        // Minted only from the actual decoded source and current scene resource owner.
        // The native material is a draw/sampler anchor, never source material identity.
        private sealed class SourceMaterialAttachment
        {
            internal readonly Scene Scene;
            internal readonly SceneResources Resources;
            internal readonly CharacterSourcePreviewLease Lease;
            internal readonly CharacterModelAsset Asset;
            internal readonly Model NativeModel;
            internal readonly CharacterWeightedModelData Geometry;
            internal readonly int AnchorIndex;
            private readonly Material _anchor;
            private readonly string _anchorName;
            internal SourceMaterialAttachment(Scene scene,SceneResources resources,CharacterSourcePreviewLease lease,
                CharacterModelAsset asset,Model nativeModel,CharacterWeightedModelData geometry,string anchorName)
            {
                if(string.IsNullOrWhiteSpace(anchorName)||anchorName.Length>128||geometry.SourceRig==null)
                    throw new InvalidDataException("Explicit source material attachment required.");
                var anchors=nativeModel.Materials.Select((m,i)=>(m,i)).Where(x=>x.m.Name==anchorName).ToArray();
                if(anchors.Length!=1||!CanUseAuthoredTexcoords(anchors[0].m.TexgenMode,true)
                    ||geometry.Primitives.Any(p=>string.IsNullOrWhiteSpace(p.MaterialName)||p.Albedo==null))
                    throw new InvalidDataException("Unique existing native UV anchor and each source albedo required.");
                (Scene,Resources,Lease,Asset,NativeModel,Geometry,AnchorIndex,_anchor,_anchorName)=
                    (scene,resources,lease,asset,nativeModel,geometry,anchors[0].i,anchors[0].m,anchorName);
                RequireCurrent(scene,resources,lease,asset,nativeModel,geometry.SourceRig);
            }
            internal void RequireCurrent(Scene scene,SceneResources resources,CharacterSourcePreviewLease lease,
                CharacterModelAsset asset,Model nativeModel,CharacterSourceRig? rig)
            {
                lease.RequireCurrent(asset);
                if(!ReferenceEquals(Scene,scene)||!ReferenceEquals(Resources,resources)||!ReferenceEquals(Lease,lease)
                    ||!ReferenceEquals(Asset,asset)||!ReferenceEquals(NativeModel,nativeModel)
                    ||!ReferenceEquals(Geometry.SourceRig,rig)||AnchorIndex>=nativeModel.Materials.Count
                    ||!ReferenceEquals(nativeModel.Materials[AnchorIndex],_anchor)||_anchor.Name!=_anchorName
                    ||!CanUseAuthoredTexcoords(_anchor.TexgenMode,true))
                    throw new InvalidDataException("Source attachment identity retired or mismatched.");
            }
        }
        private sealed class SceneResources
        {
            public readonly TextureAssetQuality TextureQuality = RenderOptions.TextureQuality;
            public readonly string SamplingKey = TextureSamplingPolicy.RuntimeKey;
            public readonly Dictionary<(string Path, int NativeModelId, PrivateCharacterAppearance? Appearance), CharacterRigidRenderModel> RigidModels = new();
            public readonly Dictionary<(string Path, int NativeModelId, PrivateCharacterAppearance? Appearance), CharacterWeightedRenderModel> WeightedModels = new();
            public readonly Dictionary<(CharacterSourceCacheKey Source,int NativeModelId,CharacterSourcePreviewInstance Instance),SourceEntry> SourceModels = new();
            public readonly HashSet<(string Path, int NativeModelId, PrivateCharacterAppearance? Appearance)> Failed = new();

            public void Release()
            {
                foreach (CharacterRigidRenderModel model in RigidModels.Values) model.Release();
                foreach (CharacterWeightedRenderModel model in WeightedModels.Values) model.Release();
                foreach (var entry in SourceModels.Values) entry.Model.Release();
                SourceModels.Clear();
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

        /// <summary>
        /// An explicit private acceptance input is resolved per seat. It is
        /// never stored as process-global character selection and never turns
        /// a Hunter-only replacement into an extra character automatically.
        /// </summary>
        internal static bool TryResolvePrivateAppearance(CharacterModelPack pack, Hunter hunter,
            CharacterModelPart part, int lod, PrivateCharacterAppearance? appearance,
            bool allowPrivatePreview, out CharacterModelAsset asset)
        {
            asset = null!;
            if (appearance == null) return pack.TryResolve(hunter, part, lod, out asset);
            if (!allowPrivatePreview || Mods.Network.NetSession.Active || Mods.Network.DemoPlayback.IsActive)
                return false;
            return pack.TryResolvePrivate(hunter, appearance.Value, part, lod, out asset);
        }

        internal static (string Path, int NativeModelId, PrivateCharacterAppearance? Appearance)
            ModelCacheKey(CharacterModelAsset asset, int nativeModelId)
            => (asset.ModelPath, nativeModelId, asset.PrivateAppearance);

        public static bool TryGetRigid(Scene scene, Hunter hunter, CharacterModelPart part,
            Model nativeModel, out CharacterRigidRenderModel model, int lod = 0,
            CharacterModelPack? privatePack = null, PrivateCharacterAppearance? privateAppearance = null,
            bool allowPrivatePreview = false)
        {
            model = null!;
            if (Headless.Active || !RenderOptions.CharacterModelReplacements) return false;
            bool privateRequest = privatePack != null || privateAppearance != null || allowPrivatePreview;
            if (privateRequest && (!allowPrivatePreview || privatePack == null || scene.Services.IsReplica
                || Mods.Network.NetSession.Active || Mods.Network.DemoPlayback.IsActive)) return false;

            CharacterModelPack pack = privatePack ?? GetPack();
            if (privatePack == null && _packIssue != null && !_packIssueLogged)
            {
                _packIssueLogged = true;
                DebugLog.Line("render", "HD character model pack disabled: " + _packIssue);
            }
            if (!TryResolvePrivateAppearance(pack, hunter, part, lod, privateAppearance,
                    allowPrivatePreview, out CharacterModelAsset asset)
                || asset.Skinning != CharacterSkinningMode.RigidNodes)
                return false;

            var key = ModelCacheKey(asset, nativeModel.Id);
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
            Model nativeModel, out CharacterWeightedRenderModel model, int lod = 0,
            CharacterModelPack? privatePack = null, PrivateCharacterAppearance? privateAppearance = null,
            bool allowPrivatePreview = false)
        {
            model = null!;
            if (Headless.Active || !RenderOptions.CharacterModelReplacements) return false;
            bool privateRequest = privatePack != null || privateAppearance != null || allowPrivatePreview;
            if (privateRequest && (!allowPrivatePreview || privatePack == null || scene.Services.IsReplica
                || Mods.Network.NetSession.Active || Mods.Network.DemoPlayback.IsActive)) return false;

            CharacterModelPack pack = privatePack ?? GetPack();
            if (!TryResolvePrivateAppearance(pack, hunter, part, lod, privateAppearance,
                    allowPrivatePreview, out CharacterModelAsset asset)
                || asset.Skinning != CharacterSkinningMode.Weighted4 || asset.PoseSource != CharacterPoseSource.Native)
                return false;

            var key = ModelCacheKey(asset, nativeModel.Id);
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

        internal static bool TryGetSourcePreview(Scene scene,Hunter hunter,CharacterModelPart part,Model nativeModel,
            CharacterModelPack privatePack,PrivateCharacterAppearance appearance,CharacterSourcePreviewInstance instance,bool allowSourcePreview,
            out CharacterWeightedRenderModel model)
        {
            model=null!;
            if(!CharacterSourceRootWitness.QualifiedForCurrentProcess)
            {if(instance!=null)ReleaseSourcePreview(scene,instance);DebugLog.Line("render",CharacterSourceRootWitness.UnsupportedPlatformMessage);return false;}
            bool diagnosticStage = false;
#if !ANDROID && !MPHREAD_SERVER
            diagnosticStage = instance?.StageOwner is { } stageOwner && LauncherHunter.OwnsSourcePreview(scene,stageOwner);
#endif
            if(instance==null||Headless.Active||(!RenderOptions.CharacterModelReplacements&&!diagnosticStage)||!allowSourcePreview
                ||part!=instance.Part||part is not (CharacterModelPart.Biped or CharacterModelPart.ViewModel)||scene.Services.IsReplica||Mods.Network.NetSession.Active||Mods.Network.DemoPlayback.IsActive
                ||!privatePack.TryResolvePrivate(hunter,appearance,part,0,out var asset)||asset.PoseSource!=CharacterPoseSource.SourceRigPreview
                ||part==CharacterModelPart.ViewModel&&!privatePack.HasSourceCannon(asset))
            {if(instance!=null)ReleaseSourcePreview(scene,instance);return false;}
            var resources=_scenes.GetValue(scene,_=>new SceneResources());
            if(resources.TextureQuality!=RenderOptions.TextureQuality||resources.SamplingKey!=TextureSamplingPolicy.RuntimeKey)
            {Release(scene);resources=_scenes.GetValue(scene,_=>new SceneResources());}
            if(instance.MaterialAnchorName!=null && instance.PublishedContext!=null
                &&!ReferenceEquals(instance.PublishedContext,resources))
            {ReleaseSourcePreview(scene,instance);return false;}
            // Reuse immutable GPU/CPU data without filesystem IO. Exact pack,
            // asset, lease generation, native model and current context owner
            // are checked here. Explicit refresh retires old generations.
            foreach(var previous in resources.SourceModels.Keys.Where(k=>ReferenceEquals(k.Instance,instance)).ToArray())
            {
                var entry=resources.SourceModels[previous];
                if(ReferenceEquals(entry.Lease.CacheIdentity,privatePack)&&ReferenceEquals(entry.Model.Asset,asset)
                    &&ReferenceEquals(entry.NativeModel,nativeModel))
                {
                    try{entry.Lease.RequireCurrent(asset);
                        entry.Attachment?.RequireCurrent(scene,resources,entry.Lease,asset,nativeModel,entry.Model.SourceRig);
                        model=entry.Model;return true;}
                    catch(InvalidDataException){}
                }
                resources.SourceModels.Remove(previous);entry.Model.Release();
            }
            // An explicitly attached instance is never resurrected after retirement.
            if(instance.MaterialAnchorName!=null&&instance.PublishedContext!=null)return false;
            if(resources.SourceModels.Count>=CharacterModelPack.MaximumModels
                ||!privatePack.TryCreateSourcePreviewLease(asset,out var lease))return false;
            var key=(lease.CacheKey,nativeModel.Id,instance);
            try
            {
                var candidate=CompileWeighted(scene,asset,nativeModel,lease,instance.MaterialAnchorName,resources,out var attachment);
                try
                {
                    lease.Require(asset); // Strong bounded proof before publish.
                    if(!_scenes.TryGetValue(scene,out var current)||!ReferenceEquals(current,resources))
                        throw new InvalidDataException("Source compilation context owner retired.");
                    attachment?.RequireCurrent(scene,resources,lease,asset,nativeModel,candidate.SourceRig);
                    candidate.UpdateSourcePreviewPalette();
                    resources.SourceModels.Add(key,new(candidate,lease,nativeModel,attachment));
                    if(attachment!=null)instance.PublishedContext=resources;
                    model=candidate;return true;
                }
                catch{candidate.Release();throw;}
            }
            catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException
                or InvalidOperationException or JsonException or FormatException or OverflowException or KeyNotFoundException or ProgramException)
            {model=null!;DebugLog.Line("render","Private source preview rejected: "+ex.Message);return false;}
        }

        internal static bool TryLoadSourceClip(Scene scene,CharacterSourcePreviewInstance instance,
            CharacterWeightedRenderModel model,int ordinal,out CharacterSourceClip clip)
        {
            clip=null!;
            if(model.SourceCannon!=null)return false;
            if(!_scenes.TryGetValue(scene,out var resources)||!ReferenceEquals(instance.PublishedContext,resources))return false;
            var entries=resources.SourceModels.Where(e=>ReferenceEquals(e.Key.Instance,instance)&&ReferenceEquals(e.Value.Model,model)).ToArray();
            if(entries.Length!=1||entries[0].Value.Attachment is not { } attachment)return false;
            var entry=entries[0].Value;
            try
            {
                attachment.RequireCurrent(scene,resources,entry.Lease,model.Asset,entry.NativeModel,model.SourceRig);
                clip=CharacterSourceClip.Load(entry.Lease,model.Asset,model.SourceRig!,ordinal);
                attachment.RequireCurrent(scene,resources,entry.Lease,model.Asset,entry.NativeModel,model.SourceRig);return true;
            }
            catch(Exception e)when(e is IOException or InvalidDataException or InvalidOperationException or ArgumentException
                or NotSupportedException or JsonException or FormatException or OverflowException or KeyNotFoundException){clip=null!;return false;}
        }
        internal static bool IsSourceClipCurrent(Scene scene,CharacterSourcePreviewInstance instance,
            CharacterWeightedRenderModel model,CharacterSourceClip clip)
        {
            if(!_scenes.TryGetValue(scene,out var resources)||!ReferenceEquals(instance.PublishedContext,resources))return false;
            foreach(var entry in resources.SourceModels.Where(e=>ReferenceEquals(e.Key.Instance,instance)&&ReferenceEquals(e.Value.Model,model)).Select(e=>e.Value))
                if(entry.Attachment is { } attachment && clip.Matches(entry.Lease,model.Asset,model.SourceRig!))
                {try{attachment.RequireCurrent(scene,resources,entry.Lease,model.Asset,entry.NativeModel,model.SourceRig);return true;}catch(InvalidDataException){return false;}}
            return false;
        }

        // Existing explicit editor/acceptance owner calls this on user refresh,
        // never from a frame or watcher. It retires this scene's matching GPU
        // entries immediately; other scenes reject the old generation on reuse.
        internal static bool RefreshSourcePreview(Scene scene,CharacterModelPack pack)
        {
            bool valid=pack.RefreshSourcePreviewProof();
            if(_scenes.TryGetValue(scene,out var resources))
                foreach(var key in resources.SourceModels.Keys.Where(k=>ReferenceEquals(k.Source.Pack,pack)).ToArray())
                    if(resources.SourceModels.Remove(key,out var entry))entry.Model.Release();
            return valid;
        }
        internal static void ReleaseSourcePreview(Scene scene,CharacterSourcePreviewInstance instance)
        {
            if(!_scenes.TryGetValue(scene,out var resources))return;
            foreach(var key in resources.SourceModels.Keys.Where(k=>ReferenceEquals(k.Instance,instance)).ToArray())
                if(resources.SourceModels.Remove(key,out var entry))entry.Model.Release();
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
            try
            {
                // Reserve all base images before optional maps. Shared material
                // instances across rigid segments upload only once per scene.
                foreach (var primitive in geometry.Primitives)
                {
                    if (primitive.Albedo == null || albedos.ContainsKey(primitive.Albedo)) continue;
                    int binding = scene.GetCharacterModelTexture(asset.ModelPath + "/rigid-albedo/" + albedos.Count,
                        primitive.Albedo.Image, assetClass, primitive.Albedo.Opaque);
                    if (binding == 0) throw new InvalidDataException("Embedded rigid character albedo could not be uploaded.");
                    albedos.Add(primitive.Albedo, binding);
                }
                foreach (CharacterRigidPrimitive primitive in geometry.Primitives)
                {
                    if (!nodeIndices.TryGetValue(primitive.TargetNode, out int nodeIndex))
                        throw new InvalidDataException($"Native model no longer contains mapped node '{primitive.TargetNode}'.");
                    int materialIndex = ResolveMaterial(nativeModel, nodeIndex, primitive.MaterialName, materialIndices);
                    Material material = nativeModel.Materials[materialIndex];
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
                return new(asset, compiled.ToArray(), geometry.VertexCount, geometry.IndexCount);
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
            Scene scene, CharacterModelAsset asset, Model nativeModel, CharacterSourcePreviewLease? sourceLease = null)
            => CompileWeighted(scene,asset,nativeModel,sourceLease,null,null,out _);

        private static CharacterWeightedRenderModel CompileWeighted(Scene scene,CharacterModelAsset asset,Model nativeModel,
            CharacterSourcePreviewLease? sourceLease,string? sourceAnchor,SceneResources? resources,out SourceMaterialAttachment? attachment)
        {
            attachment=null;
#if MPHREAD_SERVER
            throw new InvalidOperationException("HD character geometry is unavailable in dedicated-server builds.");
#else
            CharacterWeightedModelData geometry = sourceLease==null ? CharacterWeightedModelLoader.Load(asset)
                : CharacterWeightedModelLoader.LoadSourcePreview(asset,sourceLease);
            if(sourceAnchor!=null)
            {
                if(sourceLease==null||resources==null)throw new InvalidDataException("Source attachment owner absent.");
                attachment=new(scene,resources,sourceLease,asset,nativeModel,geometry,sourceAnchor);
            }
            IReadOnlyList<CharacterSourceGxBinding>? sourcePrograms = null;
            if (sourceLease?.SourceMaterials is { } sourceMaterials && sourceLease.SourceCannon == null)
            {
                if (attachment == null || asset.Part != CharacterModelPart.Biped
                    || geometry.Primitives.Any(p => p.SourceMaterialOrdinal < 0 || p.SourceMaterialOrdinal >= sourceMaterials.Programs.Count)
                    || geometry.Primitives.Select(p => p.SourceMaterialOrdinal).Distinct().Count() != sourceMaterials.Programs.Count)
                    throw new InvalidDataException("Complete original body material ordinals required.");
                sourcePrograms = scene.PrepareSourceGxMaterials(sourceMaterials);
            }
            var nodeIndices = nativeModel.Nodes.Select((node, index) => (Name: node.Name, Index: index))
                .ToDictionary(value => value.Name, value => value.Index, StringComparer.Ordinal);
            var materialIndices = nativeModel.Materials.Select((material, index) => (Name: material.Name, Index: index))
                .GroupBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Index, StringComparer.OrdinalIgnoreCase);

            var joints = new List<CharacterWeightedRenderJoint>(geometry.Joints.Count);
            foreach (CharacterWeightedJoint joint in geometry.Joints)
            {
                int nativeNodeIndex=-1;
                if (sourceLease==null && !nodeIndices.TryGetValue(joint.TargetNode, out nativeNodeIndex))
                    throw new InvalidDataException(
                        $"Native model no longer contains weighted target node '{joint.TargetNode}'.");
                joints.Add(new(nativeNodeIndex, joint.InverseBind));
            }

            string compiledAssetKey=sourceLease?.SourceCannon is { } cannon?asset.ModelPath+"/viewModel/"+cannon.ReceiptSha256:asset.ModelPath;
            var compiled = new List<CharacterWeightedRenderSegment>(geometry.Primitives.Count);
            try
            {
                foreach (CharacterWeightedPrimitive primitive in geometry.Primitives)
                {
                    int materialIndex=attachment?.AnchorIndex ?? -1;
                    if (attachment==null && (String.IsNullOrWhiteSpace(primitive.MaterialName)
                        || !materialIndices.TryGetValue(primitive.MaterialName, out materialIndex)))
                        throw new InvalidDataException(
                            $"Weighted HD material '{primitive.MaterialName ?? "(unnamed)"}' "
                            + $"does not match a native material in {nativeModel.Name}.");
                    Material material = nativeModel.Materials[materialIndex];
                    if (!CanUseAuthoredTexcoords(material.TexgenMode, primitive.Albedo != null))
                        throw new InvalidDataException(
                            $"Weighted HD material '{material.Name}' uses generated native coordinates; "
                            + "weighted GLB UVs require None/Texcoord.");

                    int? albedo = null;
                    if (sourcePrograms == null && primitive.Albedo != null)
                    {
                        int binding = scene.GetCharacterModelTexture(
                            compiledAssetKey + "/" + compiled.Count, primitive.Albedo.Image,
                            asset.Part == CharacterModelPart.ViewModel
                                ? TextureAssetClass.Weapon : TextureAssetClass.Hunter,
                            primitive.Albedo.Opaque);
                        if (binding == 0)
                            throw new InvalidDataException("Embedded character albedo could not be uploaded.");
                        albedo = binding;
                    }
                    MaterialMapBindings maps = primitive.MaterialMaps == null ? default
                        : scene.GetCharacterModelMaterialMaps(compiledAssetKey + "/" + compiled.Count,
                            primitive.MaterialMaps, asset.Part == CharacterModelPart.ViewModel
                                ? TextureAssetClass.Weapon : TextureAssetClass.Hunter);
                    int list = CompileWeightedList(primitive);
                    compiled.Add(new(materialIndex, list, albedo,
                        primitive.Albedo?.WrapS ?? RepeatMode.Repeat,
                        primitive.Albedo?.WrapT ?? RepeatMode.Repeat, maps,
                        primitive.Albedo?.Recolors?.Count > 0 && albedo.HasValue
                            ? new CharacterAlbedoPalette(primitive.Albedo, asset.Part == CharacterModelPart.ViewModel
                                ? TextureAssetClass.Weapon : TextureAssetClass.Hunter, albedo.Value) : null, primitive.DoubleSided, primitive.Albedo?.Opaque == false,
                        primitive.JointPalette==null?null:Array.AsReadOnly(primitive.JointPalette.ToArray()),
                        primitive.JointPalette==null?null:new float[16*primitive.JointPalette.Length],
                        sourceLease==null?null:primitive.MaterialName,sourceLease==null?-1:primitive.SourcePrimitiveOrdinal>=0?primitive.SourcePrimitiveOrdinal:compiled.Count,
                        sourcePrograms?[primitive.SourceMaterialOrdinal]));
                }
                return new(asset, compiled.ToArray(), joints.ToArray(),
                    geometry.VertexCount, geometry.IndexCount, sourceLease==null?null:geometry);
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
