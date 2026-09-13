using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using Avalonia.Platform;
using MphRead.Cosmetics;
using MphRead.Cosmetics.Presentation;
using MphRead.Entities;
using MphRead.Imaging;
using OpenTK.Mathematics;

namespace MphRead;

public partial class ScenePresentation
{
    private readonly CosmeticBudgetRequest[] _armorRequests
        = new CosmeticBudgetRequest[PlayerEntity.SlotCapacity];
    private readonly CosmeticBudgetAllowance[] _armorAllowances
        = new CosmeticBudgetAllowance[PlayerEntity.SlotCapacity];
    private int _armorAllowanceCount;
    private bool _cosmeticAtlasLoadAttempted;
    private CosmeticAtlasCatalog? _cosmeticAtlas;
    private TextureIdentity? _cosmeticAtlasTexture;
    private int _cosmeticAtlasBindingId = -1;
    private readonly Dictionary<string, TextureAssetKey> _cosmeticAtlasTextureKeys
        = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ModelInstance> _cosmeticAttachmentModels
        = new(StringComparer.Ordinal);
    private readonly HashSet<string> _failedCosmeticAttachmentModels
        = new(StringComparer.Ordinal);
    internal CosmeticPrimitiveSubmissionBuffer ArmorPrimitiveSubmissions { get; } = new();

    internal CosmeticPresentationSettings ArmorPresentationSettings { get; private set; }
        = CosmeticPresentationSettings.CreateDefault(OperatingSystem.IsAndroid());

    internal void PrepareArmorEffects()
    {
        // One atomic preference snapshot owns the complete frame. A concurrent
        // settings save cannot tear fields or give players different policy.
        ArmorPresentationSettings = CosmeticPresentationPreferences.Current;
        _armorAllowanceCount = 0;
        ArmorPrimitiveSubmissions.Clear();
        int requestCount = 0;
        foreach (PlayerEntity player in World.GetPlayerEntities())
        {
            if (!player.Initialized || !player.LoadFlags.TestFlag(LoadFlags.Active))
            {
                continue;
            }
            PlayerPresentation presentation = player.GetPresentation();
            if (presentation.PrepareDeathEffect(ArmorPresentationSettings,
                    out DeathEffectFrame deathFrame))
            {
                _armorRequests[requestCount++] = deathFrame.BudgetRequest;
                continue;
            }
            if (presentation.PrepareArmorEffect(ArmorPresentationSettings,
                    out ArmorEffectFrame frame))
            {
                _armorRequests[requestCount++] = frame.BudgetRequest;
            }
        }
        _armorAllowanceCount = CosmeticBudgetArbiter.Admit(
            _armorRequests.AsSpan(0, requestCount), _armorAllowances);
    }

    internal bool TryGetArmorAllowance(ulong stableKey,
        out CosmeticBudgetAllowance allowance)
    {
        for (int i = 0; i < _armorAllowanceCount; i++)
        {
            if (_armorAllowances[i].StableKey == stableKey)
            {
                allowance = _armorAllowances[i];
                return true;
            }
        }
        allowance = default;
        return false;
    }

    internal void FlushArmorEffects()
    {
        IReadOnlyList<CosmeticPrimitiveSubmission> submissions
            = ArmorPrimitiveSubmissions.Seal();
        FlushCosmeticSubmissions(submissions);
    }

    internal void FlushCosmeticSubmissions(
        IReadOnlyList<CosmeticPrimitiveSubmission> submissions)
    {
        ArgumentNullException.ThrowIfNull(submissions);
        for (int i = 0; i < submissions.Count; i++)
        {
            CosmeticPrimitiveSubmission submission = submissions[i];
            switch (submission.Kind)
            {
                case CosmeticPrimitiveKind.Particle:
                    if (!TryAddCosmeticAtlasSprite(submission, submission.Start,
                            scale: .08f + Math.Min(submission.Intensity, 2) * .025f,
                            alpha: Math.Clamp(submission.Intensity / 2, .2f, 1)))
                    {
                        AddSingleParticle(SingleType.Fuzzball, submission.Start,
                            submission.Color, alpha: Math.Clamp(submission.Intensity / 2, .2f, 1),
                            scale: .08f + Math.Min(submission.Intensity, 2) * .025f);
                    }
                    break;
                case CosmeticPrimitiveKind.Ribbon:
                    for (int segment = 0; segment <= submission.SegmentCount; segment++)
                    {
                        float amount = segment / (float)submission.SegmentCount;
                        Vector3 position = Vector3.Lerp(submission.Start, submission.End, amount);
                        if (!TryAddCosmeticAtlasSprite(submission, position,
                                scale: .055f, alpha: .6f))
                        {
                            AddSingleParticle(SingleType.Fuzzball, position,
                                submission.Color, alpha: .6f, scale: .055f);
                        }
                    }
                    break;
                case CosmeticPrimitiveKind.Attachment:
                    TryAddCosmeticAttachmentModel(submission);
                    break;
                case CosmeticPrimitiveKind.LocalLight:
                    TryAddVisualLight(submission.StableKey, submission.Start,
                        new VisualLightProfile(submission.Color, radius: 2.25f,
                            intensity: Math.Clamp(submission.Intensity * .18f, .08f, .5f),
                            priority: submission.PlayerSlot == World.LocalPlayerSlot ? 20 : 10,
                            lifetime: 1f / 30f,
                            falloff: VisualLightProfile.SupportedFalloff));
                    break;
            }
        }
    }

    private bool TryAddCosmeticAttachmentModel(
        in CosmeticPrimitiveSubmission submission)
    {
        if (submission.AssetKey is not string assetKey
            || !IsSupportedCosmeticAttachmentModel(assetKey)
            || _failedCosmeticAttachmentModels.Contains(assetKey))
        {
            return false;
        }
        if (!_cosmeticAttachmentModels.TryGetValue(assetKey,
                out ModelInstance? instance))
        {
            try
            {
                instance = Read.GetModelInstance(assetKey);
                EnsurePortableModelPrepared(instance.Model);
                _cosmeticAttachmentModels.Add(assetKey, instance);
            }
            catch (Exception error) when (error is IOException
                or InvalidDataException or InvalidOperationException
                or KeyNotFoundException)
            {
                _failedCosmeticAttachmentModels.Add(assetKey);
                Mods.DebugLog.Line("cosmetics/armor",
                    $"Attachment model '{assetKey}' unavailable; attachment suppressed: {error.Message}");
                return false;
            }
        }

        Model model = instance.Model;
        Matrix4 transform = submission.LocalTransform;
        transform.Row3.Xyz += submission.Start;
        model.AnimateMaterials(instance.AnimInfo);
        model.AnimateTextures(instance.AnimInfo);
        model.ComputeNodeMatrices(index: 0);
        model.AnimateNodes(index: 0, useNodeTransform: true, transform,
            model.Scale, instance.AnimInfo);
        model.UpdateMatrixStack();
        UpdateMaterials(model, recolorId: 0);

        int polygonId = GetNextPolygonId();
        var tint = new Vector4(submission.Color, 1);
        float attachmentAlpha = Math.Clamp(submission.Intensity / 2, .35f, 1);
        Vector3 attachmentEmission = submission.Color
            * Math.Clamp(submission.Intensity, 0, 2);
        var materialOverride = new CosmeticMaterialOverride(
            EmissionTint: submission.Color,
            EmissionStrength: Math.Clamp(submission.Intensity, 0, 8));
        var light = new LightInfo(World.Light1Vector, World.Light1Color,
            World.Light2Vector, World.Light2Color);
        SubmitNode(0);
        return true;

        void SubmitNode(int nodeIndex)
        {
            Node node = model.Nodes[nodeIndex];
            if (node.Enabled)
            {
                int start = node.MeshId / 2;
                for (int meshIndex = 0; meshIndex < node.MeshCount; meshIndex++)
                {
                    Mesh mesh = model.Meshes[start + meshIndex];
                    if (!mesh.Visible) continue;
                    Material material = model.Materials[mesh.MaterialId];
                    TextureIdentity? texture = GetTextureIdentity(model,
                        material, recolorId: 0);
                    AddRenderItem(material, polygonId, attachmentAlpha,
                        attachmentEmission,
                        light, Matrix4.Identity, node.Animation,
                        GetMeshListId(mesh), mesh.GeometryIdentity,
                        model.NodeMatrixIds.Count, model.MatrixStackValues,
                        overrideColor: null, paletteOverride: tint,
                        SelectionType.None, node.BillboardMode,
                        bindingOverride: null, textureIdentity: texture,
                        textureAssetKey: GetModelTextureAssetKey(model, material, 0),
                        castsDirectionalShadow: false,
                        cosmeticMaterialOverride: materialOverride);
                }
                if (node.ChildIndex != -1) SubmitNode(node.ChildIndex);
            }
            if (node.NextIndex != -1) SubmitNode(node.NextIndex);
        }
    }

    private static bool IsSupportedCosmeticAttachmentModel(string assetKey)
        => String.Equals(assetKey, "octolith_simple", StringComparison.Ordinal);

    private bool TryAddCosmeticAtlasSprite(in CosmeticPrimitiveSubmission submission,
        Vector3 position, float scale, float alpha)
    {
        if (submission.AssetKey == null || !TryEnsureCosmeticAtlas()
            || !_cosmeticAtlas!.TryResolve(submission.AssetKey, out CosmeticAtlasSprite sprite)
            || !_cosmeticAtlasTextureKeys.TryGetValue(submission.AssetKey,
                out TextureAssetKey assetKey))
        {
            return false;
        }

        int inset = _cosmeticAtlas.InsetPixels;
        float u0 = (float)(sprite.X + inset) / _cosmeticAtlas.Width;
        float v0 = (float)(sprite.Y + inset) / _cosmeticAtlas.Height;
        float u1 = (float)(sprite.X + sprite.Width - inset) / _cosmeticAtlas.Width;
        float v1 = (float)(sprite.Y + sprite.Height - inset) / _cosmeticAtlas.Height;
        Vector3[] points = ArrayPool<Vector3>.Shared.Rent(8);
        points[0] = new Vector3(u0, v0, 0);
        points[1] = new Vector3(-scale, scale, 0);
        points[2] = new Vector3(u1, v0, 0);
        points[3] = new Vector3(scale, scale, 0);
        points[4] = new Vector3(u1, v1, 0);
        points[5] = new Vector3(scale, -scale, 0);
        points[6] = new Vector3(u0, v1, 0);
        points[7] = new Vector3(-scale, -scale, 0);
        AddRenderItem(RenderPrimitive.Particle, alpha, GetNextPolygonId(),
            submission.Color, RepeatMode.Clamp, RepeatMode.Clamp, 1, 1,
            Matrix4.CreateTranslation(position), points, _cosmeticAtlasTexture,
            _cosmeticAtlasBindingId, BillboardMode.Sphere,
            bloomStrength: Math.Clamp(submission.Intensity * .2f, 0, 1),
            textureAssetKey: assetKey);
        return true;
    }

    private bool TryEnsureCosmeticAtlas()
    {
        if (_cosmeticAtlasLoadAttempted)
            return _cosmeticAtlas != null && _cosmeticAtlasTexture != null;
        _cosmeticAtlasLoadAttempted = true;
        try
        {
            using Stream manifestStream = AssetLoader.Open(
                new Uri(CosmeticAtlasCatalog.OfficialManifestUri));
            CosmeticAtlasCatalog atlas = CosmeticAtlasCatalog.Load(manifestStream);
            using Stream imageStream = AssetLoader.Open(
                new Uri(CosmeticAtlasCatalog.OfficialAssetUri));
            const int maximumImageBytes = 32 * 1024 * 1024;
            using var encoded = new MemoryStream(capacity: 1024 * 1024);
            Span<byte> readBuffer = stackalloc byte[16 * 1024];
            int total = 0;
            while (true)
            {
                int read = imageStream.Read(readBuffer);
                if (read == 0) break;
                total = checked(total + read);
                if (total > maximumImageBytes)
                    throw new InvalidDataException("Cosmetic atlas image exceeds its size limit.");
                encoded.Write(readBuffer[..read]);
            }
            RgbaImage decoded = StbImageDecoder.DecodeRgba(encoded.ToArray());
            if (decoded.Width != atlas.Width || decoded.Height != atlas.Height)
                throw new InvalidDataException("Cosmetic atlas image dimensions do not match its manifest.");
            ReadOnlySpan<byte> rgba = decoded.Pixels.Span;
            var pixels = new ColorRgba[checked(decoded.Width * decoded.Height)];
            for (int i = 0; i < pixels.Length; i++)
            {
                int offset = i * 4;
                pixels[i] = new ColorRgba(rgba[offset], rgba[offset + 1],
                    rgba[offset + 2], rgba[offset + 3]);
            }
            int bindingId = BindGetTexture(pixels, decoded.Width, decoded.Height);
            if (!TryGetTextureIdentityForBinding(bindingId, out TextureIdentity identity))
                throw new InvalidOperationException("Cosmetic atlas texture identity was not registered.");
            foreach (ArmorEffectRecipe recipe in ArmorEffectRecipeCatalog.All)
            {
                if (recipe.ParticleSprites != null)
                {
                    foreach (string spriteKey in recipe.ParticleSprites)
                        _cosmeticAtlasTextureKeys.TryAdd(spriteKey,
                            TextureAssetKey.ForCustom("cosmetics", "vfx-atlas", spriteKey));
                }
            }
            _cosmeticAtlas = atlas;
            _cosmeticAtlasTexture = identity;
            _cosmeticAtlasBindingId = bindingId;
            Mods.DebugLog.Line("cosmetics/catalog",
                $"Loaded official VFX atlas '{atlas.Image}' with {atlas.Count} sprites.");
            return true;
        }
        catch (Exception error) when (error is IOException or InvalidDataException
            or InvalidOperationException or UnauthorizedAccessException)
        {
            Mods.DebugLog.Line("cosmetics/armor",
                $"VFX atlas unavailable; using built-in fallback particles: {error.Message}");
            _cosmeticAtlasTextureKeys.Clear();
            return false;
        }
    }
}
