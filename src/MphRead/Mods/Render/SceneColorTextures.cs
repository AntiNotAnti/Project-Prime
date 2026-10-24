using System;
using System.Collections.Generic;
using MphRead.Mods.Render;
using MphRead.Formats;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MphRead;

public partial class Scene
{
    // Freeze the color contract with the scene's uploads. A saved setting takes
    // effect on scene creation, so an in-flight decode cannot change domains.
    internal readonly bool SceneColorRequested = Mods.RenderOptions.LinearColor && !OpenGlOptimizationPolicy.ForceLegacy;
    internal bool SceneColorTexturesFailed { get; private set; }
    private readonly Dictionary<int, int> _sceneColorTextures = new();
    private readonly Dictionary<int, long> _sceneColorTextureBytes = new();
    private readonly Dictionary<int, TextureAlphaPolicy> _sceneTextureAlpha = new();
    private readonly Dictionary<int, float> _sceneTextureAlphaFactor = new();
    private readonly Dictionary<int, TextureWrapPolicy> _sceneTextureWrap = new();
    private long _sceneColorResidentBytes;

    private int ResolveSceneColorTexture(int original)
        => SceneLinearColorActive && _sceneColorTextures.TryGetValue(original, out int scene)
            ? scene : original;

    private void ReleaseSceneColorTexture(int original)
    {
        _sceneTextureAlpha.Remove(original);
        _sceneTextureAlphaFactor.Remove(original);
        _sceneTextureWrap.Remove(original);
        if (!_sceneColorTextures.Remove(original, out int scene)) return;
        if (_sceneColorTextureBytes.Remove(original, out long bytes)) _sceneColorResidentBytes -= bytes;
        ReleaseTexture(scene);
    }

    private void RegisterSceneColorAsset(int original, PreparedTextureAsset asset)
    {
        ReleaseSceneColorTexture(original);
        if (!SceneColorRequested || original == 0) return;
        _sceneTextureAlpha[original] = asset.Semantics.Alpha;
        _sceneTextureAlphaFactor[original] = asset.Semantics.AlphaFactor;
        if (asset.Semantics.Wrap is { } wrap) _sceneTextureWrap[original] = wrap;
        if (SceneColorTexturesFailed) return;
        int previous = GL.GetInteger(GetPName.TextureBinding2D);
        int scene = 0;
        try
        {
            PreparedTextureAsset filtered = ColorTexturePolicy.PrepareForLinearFiltering(asset);
            bool native = asset.Key.StartsWith("native:", StringComparison.Ordinal);
            TextureSamplerDescriptor sampling = native ? TextureSamplingPolicy.ResolveNativeWorld()
                : TextureSamplingPolicy.ResolveModern(asset.AssetClass, asset.Channel);
            sampling = filtered.EffectiveSampling(sampling);
            long bytes = filtered.EstimateGpuBytes(sampling.Mipmaps);
            if (bytes > TextureAssetManager.WorldMaterialMemoryBudgetBytes() - _sceneColorResidentBytes
                - _worldMaterialResidentBytes)
                throw new InvalidOperationException("Linear material texture budget exceeded.");
            scene = AllocateTexture();
            GL.BindTexture(TextureTarget.Texture2D, scene);
            TextureAssetManager.UploadPreparedBound(filtered, repeat: true, sampling, linearColor: true);
            if (GL.GetError() != ErrorCode.NoError) throw new InvalidOperationException("Linear material upload failed.");
            _sceneColorTextures.Add(original, scene);
            _sceneColorTextureBytes.Add(original, bytes);
            _sceneColorResidentBytes += bytes;
            _textureUploadSampling[scene] = sampling;
            if (sampling.Mipmaps) _mipmappedTextures.Add(scene);
            if (!native) RegisterModernTexture(scene, asset.AssetClass, asset.Channel, filtered);
            byte[]? rgba = asset is ModernTextureAsset image ? image.Pixels
                : asset is RgbaMipTextureAsset levels ? levels.Mips[0].Data : null;
            if (rgba != null)
            {
                Vector3 mean = Vector3.Zero;
                for (int i = 0; i < rgba.Length; i += 4)
                {
                    Vector3 color = new(rgba[i] / 255f, rgba[i + 1] / 255f, rgba[i + 2] / 255f);
                    mean += asset.Semantics.Transfer == TextureTransferFunction.Srgb
                        ? new Vector3(DecodeColor(color.X), DecodeColor(color.Y), DecodeColor(color.Z)) : color;
                }
                mean /= asset.Width * asset.Height;
                _flatColors[scene] = new(EncodeColor(mean.X), EncodeColor(mean.Y), EncodeColor(mean.Z));
            }
            else if (_flatColors.TryGetValue(original, out Vector3 flat)) _flatColors[scene] = flat;
            scene = 0;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            SceneColorTexturesFailed = true;
            Mods.DebugLog.Line("render", "Linear world materials refused for this scene: " + ex.Message);
        }
        finally
        {
            if (scene != 0) ReleaseTexture(scene);
            GL.BindTexture(TextureTarget.Texture2D, previous);
        }
    }

    private static float DecodeColor(float value) => value <= .04045f ? value / 12.92f
        : MathF.Pow((value + .055f) / 1.055f, 2.4f);
    private static float EncodeColor(float value) => value <= .0031308f ? value * 12.92f
        : 1.055f * MathF.Pow(value, 1f / 2.4f) - .055f;

    private TextureAlphaPolicy EffectiveSceneAlphaPolicy(RenderItem item)
        => item.AlphaPolicy.Mode != TextureAlphaMode.Legacy ? item.AlphaPolicy
            : _sceneTextureAlpha.TryGetValue(item.TextureBindingId, out var policy) ? policy : default;

    private void ApplySceneAlphaPolicy(RenderItem item)
    {
        if (!SceneColorRequested || _collectingPreview) return;
        TextureAlphaPolicy policy = EffectiveSceneAlphaPolicy(item);
        if (_sceneTextureWrap.TryGetValue(item.TextureBindingId, out var wrap))
        { item.XRepeat = wrap.S; item.YRepeat = wrap.T; }
        if (item.AlphaPolicy.Mode == TextureAlphaMode.Legacy
            && _sceneTextureAlphaFactor.TryGetValue(item.TextureBindingId, out float factor))
            item.AuthoredAlphaFactor = factor;
        item.AlphaPolicy = policy;
        if (policy.Mode == TextureAlphaMode.Blend) item.RenderMode = RenderMode.Translucent;
        else if (policy.Mode is TextureAlphaMode.Opaque or TextureAlphaMode.Mask
            && item.RenderMode == RenderMode.Translucent) item.RenderMode = RenderMode.Normal;
    }

    private bool SceneAlphaCastsShadow(RenderItem item)
        => !SceneColorRequested || EffectiveSceneAlphaPolicy(item).Mode != TextureAlphaMode.Blend;

    private void SetAuthoredAlphaUniforms(RenderItem item)
    {
        if (!SceneColorRequested) return;
        bool enabled = true;
        TextureAlphaPolicy policy = enabled ? EffectiveSceneAlphaPolicy(item) : default;
        GL.Uniform1(_shaderLocations.AuthoredAlphaEnabled, enabled ? 1 : 0);
        GL.Uniform1(_shaderLocations.AuthoredAlphaMode, (int)policy.Mode);
        GL.Uniform1(_shaderLocations.AuthoredAlphaCutoff, policy.Cutoff);
        float factor = item.AlphaPolicy.Mode != TextureAlphaMode.Legacy ? item.AuthoredAlphaFactor
            : _sceneTextureAlphaFactor.TryGetValue(item.TextureBindingId, out float textureFactor) ? textureFactor : 1f;
        GL.Uniform1(_shaderLocations.AuthoredAlphaFactor, factor);
    }

    private void RegisterSceneColorPixels(int original, uint[] pixels, int width, int height)
    {
        if (!SceneColorRequested) return;
        int count = checked(width * height);
        byte[] bytes = new byte[checked(count * 4)];
        for (int i = 0; i < count; i++)
        {
            uint pixel = pixels[i];
            int offset = i * 4;
            bytes[offset] = (byte)pixel; bytes[offset + 1] = (byte)(pixel >> 8);
            bytes[offset + 2] = (byte)(pixel >> 16); bytes[offset + 3] = (byte)(pixel >> 24);
        }
        TextureAssetSemantics semantics = TextureAssetSemantics.ForChannel(TextureAssetChannel.Albedo,
            alphaPolicy: _sceneTextureAlpha.TryGetValue(original, out var alpha) ? alpha : TextureAlphaPolicy.Legacy,
            wrap: _sceneTextureWrap.TryGetValue(original, out var wrap) ? wrap : null,
            alphaFactor: _sceneTextureAlphaFactor.TryGetValue(original, out float factor) ? factor : 1f);
        RegisterSceneColorAsset(original, ModernTextureAsset.FromRgba("native:" + original,
            TextureAssetClass.World, TextureAssetChannel.Albedo, width, height, bytes, semantics));
    }

    private void RegisterSceneColorPixels(int original, IReadOnlyList<ColorRgba> pixels,
        int width, int height)
    {
        if (!SceneColorRequested) return;
        uint[] packed = new uint[checked(width * height)];
        for (int i = 0; i < packed.Length; i++) packed[i] = pixels[i].ToUint();
        RegisterSceneColorPixels(original, packed, width, height);
    }
}
