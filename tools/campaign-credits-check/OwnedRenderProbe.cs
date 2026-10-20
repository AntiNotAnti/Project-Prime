using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using MphRead;
using OpenTK.Mathematics;
using System.Text;
using System.Text.Json;
using ModelSource = MphRead.Mods.Campaign.CampaignCreditsModelSource;
using Binding = MphRead.Mods.Campaign.CampaignCreditsAnimationBinding;
using Duration = MphRead.Mods.Campaign.CampaignCreditsAnimationDuration;

// Opt-in AMHE1 CPU decode/evaluation with metadata-only output.
// No GL, source payload export, UI, audio or save owner.
internal static class OwnedRenderProbe
{
    private sealed record ModelPin(string FileId, string Sha256, int Bytes, int Nodes,
        int Materials, int Meshes, int Textures, int Matrices, int Pixels, int Vertices,
        int Triangles, int Depth);
    private sealed record AnimationPin(string FileId, string Sha256, int Bytes, int Frames,
        int NodeRecords, int MaterialRecords);
    private static readonly ModelPin[] ModelPins =
    {
        new("logo_screen/MAYA/flare2_Model.bin", "c9cc6ec7c38d97c0b56fa2724fc6e6e070022497e346c06344351e3b58df9c6c", 2564, 3, 2, 2, 1, 2, 1024, 24, 16, 2),
        new("logo_screen/MAYA/flare3_Model.bin", "efeff3196f27de722818892607f5955894b1538d174a6c53ade478f7f28e4a55", 2564, 3, 2, 2, 1, 2, 1024, 24, 16, 2),
        new("logo_screen/MAYA/logo2_Model.bin", "7682bffcfe9fec8300bf4a44d5ebcd16f5431383ebddc4d6ac0cdf381ec897d6", 42640, 11, 9, 10, 9, 10, 73920, 48, 28, 2),
        new("logo_screen/MAYA/logo3_Model.bin", "2393ea89063137312d71f86edc9f5cd6cbae526ad320863bda7636718f871acd", 38632, 11, 9, 10, 9, 10, 49344, 52, 32, 2),
        new("main menu/toplogoR_Model.bin", "d5133b37d5d9dedb1104a13c150cf94a160675ee28f025c4a6833f4e9b7e45ce", 900, 1, 1, 1, 1, 1, 256, 4, 2, 1),
        new("tostart/logofinal_Model.bin", "a0ce0bcb402b2952e51fbaf586583c6c321d7e00dc480a1a97f8af1aecebfabc", 54100, 6, 7, 7, 5, 5, 66688, 66, 42, 2),
        new("gameoptions/ok_Model.bin", "2724cb0c747292f8cd55a9abbb4644e382e706a0367dcc1b187fdb0915609a99", 1684, 1, 1, 1, 1, 1, 1024, 4, 2, 1),
        new("models/hudfont_Model.bin", "3f89b432c47b96e52d0837fdd1fa2eb1165df9309665640a0bea02208300e7ef", 33412, 1, 1, 1, 1, 1, 32768, 4, 2, 1),
    };
    private static readonly AnimationPin[] AnimationPins =
    {
        new("gameoptions/ok_Hidden_Idle_Anim.bin", "193971d218b7342e45052fcabd0910d0d66095fb34d9b4a995a758f7ce114b9e", 304, 7, 1, 1),
        new("gameoptions/ok_Idle_Anim.bin", "998d81b6885fac0cf8e553677db59d5f3814202ce2ae6de0f91a026e738ddd0c", 296, 1, 1, 1),
        new("gameoptions/ok_Idle_Hidden_Anim.bin", "175bb330503edbaa513a99cbb37007b705dd93b73efa0f5deed6b4ede3caea29", 300, 5, 1, 1),
        new("gameoptions/ok_Selected_Anim.bin", "998d81b6885fac0cf8e553677db59d5f3814202ce2ae6de0f91a026e738ddd0c", 296, 1, 1, 1),
        new("gameoptions/ok_Selected_Hidden_Anim.bin", "a0dabc9b0e7152f4aada1ba5bb3dc3f92b3170b7c1f115812bbbef222b01a443", 320, 24, 1, 1),
        new("logo_screen/MAYA/flare2_Hidden_Idle_Anim.bin", "a1c9af11acafd6ed560f05938cf927cba21f5d388335d8885db70f53872d3d8a", 1352, 191, 3, 1),
        new("logo_screen/MAYA/flare2_Idle_Hidden_Anim.bin", "9fd3cbdb70b524ea11c70f4443e11e51a7fa65715ab86ba42cebbf6f3b52cc3d", 400, 1, 3, 1),
        new("logo_screen/MAYA/flare3_Hidden_Idle_Anim.bin", "f3dca70097f7fe550734df84e3153337c9b99b090fc4762d47a20de7700f6ca7", 1352, 191, 3, 1),
        new("logo_screen/MAYA/flare3_Idle_Hidden_Anim.bin", "728d246aa884c3403dd3e2dea60206bd34c1db5197a17b6c108737be73a3e361", 400, 1, 3, 1),
        new("logo_screen/MAYA/logo2_Hidden_Idle_Anim.bin", "1d87b65e1bd1cf5a111a7e26bbb6254f391cdde44d7eb6b1ac45f1bc581d7e10", 2400, 191, 11, 7),
        new("logo_screen/MAYA/logo2_Idle_Hidden_Anim.bin", "f34f6366534cbc119a418902e221d99819b9a9e49c910ce79b74d06dc8369376", 1636, 1, 11, 7),
        new("logo_screen/MAYA/logo3_Hidden_Idle_Anim.bin", "eddd200c2f864ebfe1fc751986e023b9940f61f7ac8dd6cb82533b63e55927f4", 2400, 191, 11, 7),
        new("logo_screen/MAYA/logo3_Idle_Hidden_Anim.bin", "0c33ea91069518c193563deca2d44dd88c4e2d632d09eb3377620f2077d83af8", 1636, 1, 11, 7),
        new("main menu/toplogoR_Hidden_Idle_Anim.bin", "fd3ae105e15af2b57e672664454f79b4d35c3b3785a34b43559cedf1f1f6e21e", 352, 55, 1, 1),
        new("main menu/toplogoR_Idle_Anim.bin", "99f3e05d430d4526dd4f2e37ab10db0b6d076629233028de2e5c6ab865c1af49", 296, 1, 1, 1),
        new("main menu/toplogoR_Idle_Hidden_Anim.bin", "09c4d9edfdd3c33f4feaba369ed2433b097a7b3be6ee33129eff4430cab192bc", 344, 47, 1, 1),
        new("tostart/logofinal_Hidden_Idle_Anim.bin", "58b44907bf0731b67d7299b8227f215140745e72517a2ff853aa91510e30b06b", 4692, 246, 6, 6),
        new("tostart/logofinal_Idle_Anim.bin", "922ba68c9412dd4a56289aeed390c6644274743f5cbc76ebb96b693baf85b72f", 1256, 1, 6, 6),
    };
    private sealed class InputProof
    {
        internal required string Path;
        internal required string FileId;
        internal required string Role;
        internal required string Before;
        internal string? After;
        internal int Maximum;
        internal long Bytes;
    }

    internal static int Run(string ownedRoot)
    {
        int checks = 0;
        string stage = "Initialize";
        var inputs = new List<InputProof>();
        var models = new Dictionary<string, ModelSource.Snapshot>(StringComparer.Ordinal);
        var modelMetadata = new List<object>();
        var animationMetadata = new List<object>();
        var mutableOwners = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var animationInfos = new Dictionary<Model, AnimationInfo>(ReferenceEqualityComparer.Instance);
        Dictionary<string, object[]>? cachesBefore = null, cachesAfter = null;
        int decodedModels = 0, sampledAnimationFrames = 0, sampledStillFrames = 0;
        int nodeMatrixSamples = 0, materialSamples = 0, matrixStackFloatSamples = 0;
        int decodedTexturePairs = 0;
        long decodedPixelSamples = 0;
        bool cachesUnchanged = false;
        string assemblyPath = typeof(ModelSource).Assembly.Location;
        string? assemblyBefore = null, assemblyAfter = null;
        string? errorType = null, errorMessageSha256 = null;
        try
        {
            assemblyBefore = HashFile(assemblyPath, 256 * 1024 * 1024);
            cachesBefore = SnapshotCaches();
            string root = Path.GetFullPath(ownedRoot);
            Check(ModelPins.Length == 8 && AnimationPins.Length == 18, "Pinned inventory differs.");
            stage = "ReadModels";
            foreach (ModelPin pin in ModelPins)
            {
                byte[] bytes = ReadTracked(root, pin.FileId, "Model", ModelSource.MaxInputBytes);
                Check(bytes.Length == pin.Bytes && Hash(bytes) == pin.Sha256, "Pinned model identity differs.");
                ModelSource.Snapshot model = ModelSource.Read(bytes);
                Check(model.SourceSha256 == pin.Sha256 && model.ByteLength == pin.Bytes
                    && model.NodeCount == pin.Nodes && model.MaterialCount == pin.Materials
                    && model.MeshCount == pin.Meshes && model.TextureCount == pin.Textures
                    && model.MatrixCount == pin.Matrices && model.PixelCount == pin.Pixels
                    && model.VertexCount == pin.Vertices && model.TriangleCount == pin.Triangles
                    && model.MaximumNodeDepth == pin.Depth, "Pinned admitted model metadata differs.");
                models.Add(pin.FileId, model);
                Model decoded = DecodePrivate(model, null);
                var pixels = VerifyPixels(decoded);
                Evaluate(decoded, 0);
                sampledStillFrames++;
                modelMetadata.Add(new { pin.FileId, model.SourceSha256, model.ByteLength,
                    model.NodeCount, model.MaterialCount, model.MeshCount, model.TextureCount,
                    model.MatrixCount, model.PixelCount, model.VertexCount, model.TriangleCount,
                    model.MaximumNodeDepth, CpuDecoded = true, StillFramesSampled = 1,
                    DecodedTexturePairs = pixels.Pairs, DecodedPixelSamples = pixels.Pixels });
            }
            stage = "ReadBoundAnimations";
            foreach (AnimationPin pin in AnimationPins)
            {
                // The authored model stem can itself contain underscores. Match
                // the longest complete stem plus '_' rather than splitting names.
                ModelPin[] candidates = ModelPins.Where(m => m.FileId.EndsWith("_Model.bin", StringComparison.Ordinal)
                    && pin.FileId.StartsWith(m.FileId[..^"_Model.bin".Length] + "_", StringComparison.Ordinal))
                    .OrderByDescending(m => m.FileId.Length).ToArray();
                Check(candidates.Length > 0 && (candidates.Length == 1
                    || candidates[0].FileId.Length > candidates[1].FileId.Length), "Animation model prefix is missing or ambiguous.");
                ModelPin modelPin = candidates[0];
                ModelSource.Snapshot model = models[modelPin.FileId];
                byte[] bytes = ReadTracked(root, pin.FileId, "Animation", Duration.MaxInputBytes);
                Check(bytes.Length == pin.Bytes && Hash(bytes) == pin.Sha256, "Pinned animation identity differs.");
                Duration.Snapshot duration = Duration.Read(bytes);
                Check(duration.Sha256 == pin.Sha256 && duration.ByteLength == pin.Bytes
                    && duration.SlotCount == 1 && duration.NodeFrames == pin.Frames
                    && duration.MaterialFrames == pin.Frames && duration.WidgetFrames == pin.Frames
                    && duration.TexcoordFrames == 0 && duration.TextureFrames == 0,
                    "Pinned animation duration metadata differs.");
                Binding.Snapshot binding = Binding.Read(model, duration, bytes);
                Check(binding.SourceSha256 == pin.Sha256 && binding.ModelSha256 == modelPin.Sha256
                    && binding.FrameCount == pin.Frames && binding.NodeAnimationCount == pin.NodeRecords
                    && binding.NodeAnimationCount == model.NodeCount && binding.MaterialAnimationCount == pin.MaterialRecords,
                    "Pinned exact-byte model/animation binding differs.");
                Model decoded = DecodePrivate(model, binding);
                var pixels = VerifyPixels(decoded);
                Check(decoded.AnimationGroups.Node.Count == 1 && decoded.AnimationGroups.Material.Count == 1
                    && decoded.AnimationGroups.Texcoord.Count == 1 && decoded.AnimationGroups.Texcoord[0].Count == 0
                    && decoded.AnimationGroups.Texture.Count == 1 && decoded.AnimationGroups.Texture[0].Count == 0,
                    "Decoded animation group inventory differs.");
                for (int frame = 0; frame < binding.FrameCount; frame++)
                {
                    Evaluate(decoded, frame);
                    sampledAnimationFrames++;
                }
                animationMetadata.Add(new { pin.FileId, ModelFileId = modelPin.FileId,
                    binding.SourceSha256, binding.ModelSha256, ByteLength = bytes.Length,
                    duration.SlotCount, duration.NodeFrames, duration.MaterialFrames,
                    duration.TexcoordFrames, duration.TextureFrames, binding.FrameCount,
                    binding.NodeAnimationCount, binding.MaterialAnimationCount,
                    FramesSampled = binding.FrameCount, CpuDecoded = true,
                    DecodedTexturePairs = pixels.Pairs, DecodedPixelSamples = pixels.Pixels });
            }
            stage = "VerifyAggregateMetadata";
            Check(models.Count == 8 && animationMetadata.Count == 18 && inputs.Count == 26,
                "Owned admission inventory differs.");
            Check(inputs.Where(i => i.Role == "Model").Sum(i => i.Bytes) == 176496
                && inputs.Where(i => i.Role == "Animation").Sum(i => i.Bytes) == 20032
                && models.Values.Sum(m => m.PixelCount) == 226048
                && models.Values.Sum(m => m.VertexCount) == 226
                && models.Values.Sum(m => m.TriangleCount) == 140
                && models.Where(m => m.Key != "models/hudfont_Model.bin").Sum(m => m.Value.TriangleCount) == 138,
                "Pinned aggregate model/animation metadata differs.");
            Check(decodedModels == 26 && sampledStillFrames == 8
                && sampledAnimationFrames == 1156 && sampledAnimationFrames == AnimationPins.Sum(p => p.Frames)
                && nodeMatrixSamples == 14072 && materialSamples == 6126,
                "CPU decode/frame sampling inventory differs.");
        }
        catch (Exception error)
        {
            errorType = error.GetType().Name;
            errorMessageSha256 = Hash(Encoding.UTF8.GetBytes(error.Message));
        }
        finally
        {
            foreach (InputProof input in inputs)
            {
                try { input.After = HashFile(input.Path, input.Maximum); }
                catch (Exception error)
                {
                    errorType ??= error.GetType().Name;
                    errorMessageSha256 ??= Hash(Encoding.UTF8.GetBytes(error.Message));
                }
            }
            try
            {
                cachesAfter = SnapshotCaches();
                cachesUnchanged = cachesBefore != null && cachesBefore.Count == cachesAfter.Count
                    && cachesBefore.All(p => cachesAfter.TryGetValue(p.Key, out object[]? values)
                        && p.Value.SequenceEqual(values));
            }
            catch (Exception error)
            {
                errorType ??= error.GetType().Name;
                errorMessageSha256 ??= Hash(Encoding.UTF8.GetBytes(error.Message));
            }
            try { assemblyAfter = HashFile(assemblyPath, 256 * 1024 * 1024); }
            catch (Exception error)
            {
                errorType ??= error.GetType().Name;
                errorMessageSha256 ??= Hash(Encoding.UTF8.GetBytes(error.Message));
            }
        }
        bool unchanged = inputs.All(i => i.Before == i.After);
        bool assemblyUnchanged = assemblyBefore != null && assemblyBefore == assemblyAfter;
        bool passed = errorType == null && unchanged && assemblyUnchanged && cachesUnchanged
            && inputs.Count == 26 && models.Count == 8 && animationMetadata.Count == 18 && decodedModels == 26;
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Schema = 1, Edition = "AMHE1", Passed = passed, Stage = stage, Checks = checks,
            ErrorType = errorType, ErrorMessageSha256 = errorMessageSha256,
            MetadataOutputOnly = true, ModelsDecoded = decodedModels > 0,
            AnimationsEvaluated = sampledAnimationFrames > 0,
            DecodedModelInstances = decodedModels, PrivateMutableObjects = mutableOwners.Count,
            SampledAnimationFrames = sampledAnimationFrames, SampledStillFrames = sampledStillFrames,
            NodeMatrixSamples = nodeMatrixSamples, MaterialSamples = materialSamples,
            MatrixStackFloatSamples = matrixStackFloatSamples, DecodedTexturePairs = decodedTexturePairs,
            DecodedPixelSamples = decodedPixelSamples, CachesUnchanged = cachesUnchanged,
            CacheCountsBefore = cachesBefore?.ToDictionary(p => p.Key, p => p.Value.Length),
            CacheCountsAfter = cachesAfter?.ToDictionary(p => p.Key, p => p.Value.Length),
            RenderingActivated = false, GLActivated = false, SceneCreated = false,
            UiAudioOrSaveTouched = false, SourcePayloadsWritten = false, GameTextExported = false,
            SourceUnchanged = unchanged, AssemblySha256Before = assemblyBefore,
            AssemblySha256After = assemblyAfter, AssemblyUnchanged = assemblyUnchanged,
            ModelCount = models.Count, AnimationCount = animationMetadata.Count,
            ModelBytes = inputs.Where(i => i.Role == "Model").Sum(i => i.Bytes),
            AnimationBytes = inputs.Where(i => i.Role == "Animation").Sum(i => i.Bytes),
            TexturePixels = models.Values.Sum(m => m.PixelCount),
            PotentialDecodedRgbaBytes = models.Values.Sum(m => (long)m.PixelCount * 4),
            EmittedVertices = models.Values.Sum(m => m.VertexCount),
            ExpandedTriangles = models.Values.Sum(m => m.TriangleCount),
            SourceInputs = inputs.Select(i => new { i.FileId, i.Role, i.Bytes,
                Sha256Before = i.Before, Sha256After = i.After }).ToArray(),
            Models = modelMetadata, Animations = animationMetadata
        }, new JsonSerializerOptions { WriteIndented = true }));
        return passed ? 0 : 1;

        void Check(bool condition, string message)
        {
            checks++;
            if (!condition) throw new InvalidDataException(message);
        }
        Model DecodePrivate(ModelSource.Snapshot source, Binding.Snapshot? animation)
        {
            Model decoded = Read.ReadCreditsModel(source, animation);
            // Every still and every animation state gets a distinct mutable
            // model, nodes, materials, meshes and matrix-stack storage.
            Check(mutableOwners.Add(decoded) && mutableOwners.Add(decoded.MatrixStackValues)
                && decoded.Nodes.All(n => mutableOwners.Add(n))
                && decoded.Materials.All(m => mutableOwners.Add(m))
                && decoded.Meshes.All(m => mutableOwners.Add(m)),
                "CPU decoder shared mutable state across admitted models/states.");
            Check(decoded.Nodes.Count == source.NodeCount && decoded.Materials.Count == source.MaterialCount
                && decoded.Meshes.Count == source.MeshCount && decoded.NodeMatrixIds.Count == source.MatrixCount
                && decoded.Recolors.Count == 1 && decoded.Recolors[0].Textures.Count == source.TextureCount
                && decoded.Meshes.All(m => m.ListId == 0) && decoded.Materials.All(m => m.TextureBindingId == 0),
                "Decoded CPU model metadata or untouched driver handles differ.");
            Check(float.IsFinite(decoded.Scale.X) && decoded.Scale.X > 0
                && float.IsFinite(decoded.Scale.Y) && decoded.Scale.Y > 0
                && float.IsFinite(decoded.Scale.Z) && decoded.Scale.Z > 0,
                "Decoded model scale is not finite and positive.");
            animationInfos.Add(decoded, new AnimationInfo());
            decodedModels++;
            return decoded;
        }
        (int Pairs, long Pixels) VerifyPixels(Model decoded)
        {
            var recolor = decoded.Recolors[0];
            Check(decoded.Materials.All(m => (m.TextureId == -1 && m.PaletteId == -1)
                || (m.TextureId >= 0 && m.PaletteId >= 0)), "Decoded static material has a mixed or invalid untextured binding.");
            var pairs = decoded.Materials.Where(m => m.TextureId != -1)
                .Select(m => (m.TextureId, m.PaletteId)).Distinct().ToArray();
            Check(pairs.Select(p => p.TextureId).Distinct().Count() == recolor.Textures.Count,
                "Decoded model texture lacks an admitted material/palette binding.");
            long sampled = 0;
            foreach (var pair in pairs)
            {
                Check(pair.TextureId >= 0 && pair.TextureId < recolor.Textures.Count
                    && pair.PaletteId >= 0 && pair.PaletteId < recolor.Palettes.Count,
                    "Decoded texture/palette binding is invalid.");
                var texture = recolor.Textures[pair.TextureId];
                var pixels = decoded.GetPixels(pair.TextureId, pair.PaletteId, recolorId: 0);
                Check(pixels.Count == texture.Width * texture.Height
                    && pixels.Count <= ModelSource.MaxPixels, "Decoded bound texture pixel extent differs.");
                bool valid = true;
                foreach (var pixel in pixels)
                {
                    // Byte channels already establish finite [0,255] range.
                    // Also verify the native decoder's five-bit RGB expansion
                    // and admitted palette/three-bit-alpha encoding domains.
                    valid &= ExpandedChannel(pixel.Red, 31) && ExpandedChannel(pixel.Green, 31)
                        && ExpandedChannel(pixel.Blue, 31)
                        && (texture.Format == TextureFormat.PaletteA3I5
                            ? ExpandedChannel(pixel.Alpha, 7) : pixel.Alpha is 0 or 255);
                }
                Check(valid, "Decoded pixel color/alpha escaped its admitted encoding domain.");
                sampled += pixels.Count;
            }
            decodedTexturePairs += pairs.Length; decodedPixelSamples += sampled;
            return (pairs.Length, sampled);
        }
        void Evaluate(Model decoded, int frame)
        {
            var groups = decoded.AnimationGroups;
            var info = animationInfos[decoded];
            info.Node.Slot = info.Material.Slot = info.Texture.Slot = info.Texcoord.Slot = 0;
            info.Node.Group = groups.Node.Count == 0 ? null : groups.Node[0];
            info.Material.Group = groups.Material.Count == 0 ? null : groups.Material[0];
            info.Texture.Group = groups.Texture.Count == 0 ? null : groups.Texture[0];
            info.Texcoord.Group = groups.Texcoord.Count == 0 ? null : groups.Texcoord[0];
            // Exact pinned MenuWidgets.Evaluate order; no Scene or renderer.
            info.Frame[0] = GroupFrame(frame, info.Material.Group?.FrameCount ?? 0);
            decoded.AnimateMaterials(info);
            info.Frame[0] = GroupFrame(frame, info.Texture.Group?.FrameCount ?? 0);
            decoded.AnimateTextures(info);
            decoded.ComputeNodeMatrices(index: 0);
            info.Frame[0] = GroupFrame(frame, info.Node.Group?.FrameCount ?? 0);
            decoded.AnimateNodes(index: 0, useNodeTransform: true, Matrix4.Identity, decoded.Scale, info);
            decoded.UpdateMatrixStack();
            Check(decoded.Nodes.All(n => Finite(n.Transform) && Finite(n.Animation))
                && decoded.MatrixStackValues.All(float.IsFinite), "Evaluated node/stack matrix is nonfinite.");
            Check(decoded.Materials.All(m => Unit(m.CurrentDiffuse) && Unit(m.CurrentAmbient)
                && Unit(m.CurrentSpecular) && Unit(m.CurrentAlpha)
                && ((m.CurrentTextureId == -1 && m.CurrentPaletteId == -1)
                    || (m.CurrentTextureId >= 0 && m.CurrentTextureId < decoded.Recolors[0].Textures.Count
                        && m.CurrentPaletteId >= 0 && m.CurrentPaletteId < decoded.Recolors[0].Palettes.Count))),
                "Evaluated material color/alpha or binding is invalid.");
            nodeMatrixSamples += decoded.Nodes.Count * 2;
            materialSamples += decoded.Materials.Count;
            matrixStackFloatSamples += decoded.MatrixStackValues.Count;
        }
        byte[] ReadTracked(string root, string fileId, string role, int maximum)
        {
            string path = Resolve(root, fileId);
            string before = HashFile(path, maximum);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            long length = stream.Length;
            if (length < 1 || length > maximum) throw new InvalidDataException("Owned input exceeds its bounded extent.");
            inputs.Add(new() { Path = path, FileId = fileId, Role = role, Before = before,
                Maximum = maximum, Bytes = length });
            byte[] bytes = new byte[(int)length];
            stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1 || stream.Length != length || Hash(bytes) != before)
                throw new InvalidDataException("Owned input changed during bounded admission.");
            return bytes;
        }
    }

    private static int GroupFrame(int frame, int count) => count <= 1 ? 0 : Math.Min(frame, count - 1);
    private static bool Unit(float value) => float.IsFinite(value) && value is >= 0 and <= 1;
    private static bool Unit(Vector3 value) => Unit(value.X) && Unit(value.Y) && Unit(value.Z);
    private static bool Finite(Matrix4 matrix) => Finite(matrix.Row0) && Finite(matrix.Row1)
        && Finite(matrix.Row2) && Finite(matrix.Row3);
    private static bool Finite(Vector4 value) => float.IsFinite(value.X) && float.IsFinite(value.Y)
        && float.IsFinite(value.Z) && float.IsFinite(value.W);
    private static bool ExpandedChannel(byte value, int maximum)
    {
        int source = (int)MathF.Round(value / 255f * maximum);
        return value == (byte)MathF.Round(source / (float)maximum * 255f);
    }
    private static Dictionary<string, object[]> SnapshotCaches()
    {
        var result = new Dictionary<string, object[]>(StringComparer.Ordinal);
        foreach (string name in new[] { "_modelCache", "_fhModelCache", "_retiredRoomModels", "_effects", "_particleDefs" })
        {
            var field = typeof(Read).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidDataException("CPU decoder cache witness is unavailable.");
            var values = field.GetValue(null) as IEnumerable
                ?? throw new InvalidDataException("CPU decoder cache witness has an unsupported shape.");
            result.Add(name, values.Cast<object>().ToArray());
        }
        return result;
    }
    private static string Resolve(string root, string relative)
    {
        string full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, comparison))
            throw new InvalidDataException("Owned source path escaped its supplied root.");
        return full;
    }
    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string HashFile(string path, int maximum)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length < 1 || stream.Length > maximum)
            throw new InvalidDataException("Owned hash input exceeds its bounded extent.");
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
