using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Ktx;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

/// <summary>Checks the opt-in scene-color upload contract in a real GL context.</summary>
internal static unsafe class LinearMaterialGpuChecks
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private const int Srgb8Alpha8 = 0x8C43;
    private const int Rgba8 = 0x8058;

    internal static void Run(Scene originalScene)
    {
        bool previousLinearColor = RenderOptions.LinearColor;
        int previousActiveTexture = GL.GetInteger(GetPName.ActiveTexture);
        GL.ActiveTexture(TextureUnit.Texture0);
        int previousBinding = GL.GetInteger(GetPName.TextureBinding2D);
        Scene? scene = null;
        TextureAssetManager? preparedAssets = null;
        var manualOriginals = new List<int>();
        int normalTexture = 0;
        try
        {
            byte[] opaqueKtx = MakeSrgbMipKtx();
            var opaquePolicy = new TextureAlphaPolicy(TextureAlphaMode.Opaque);
            Check(!originalScene.SceneColorRequested, "baseline Scene retains the native encoded-color request");

            RenderOptions.LinearColor = true;
            scene = Scene.CreateEditorRenderer(new Vector2i(32, 32));
            Check(scene.SceneColorRequested, "independent Scene freezes the enabled linear-color request");
            preparedAssets = new TextureAssetManager(
                () => AllocateSceneTexture(scene), binding => ReleaseSceneTexture(scene, binding),
                (binding, asset) => RegisterPrepared(scene, binding, asset), linearFiltering: true);
            var opaqueSemantics = TextureAssetSemantics.ForChannel(TextureAssetChannel.Albedo,
                alphaPolicy: opaquePolicy, wrap: TextureWrapPolicy.Clamp, alphaFactor: 0.25f);
            int originalKtx = preparedAssets.Upload("linear-opaque-fixture",
                () => new MemoryStream(opaqueKtx, writable: false), TextureAssetClass.World,
                TextureAssetChannel.Albedo, repeat: false, out _, out _, opaqueSemantics);
            Check(originalKtx != 0 && GL.IsTexture(originalKtx),
                "generic texture manager admits the prepared OPAQUE KTX2 payload");
            var sceneTextures = Field<Dictionary<int, int>>(scene, "_sceneColorTextures");
            var sceneAlpha = Field<Dictionary<int, TextureAlphaPolicy>>(scene, "_sceneTextureAlpha");
            var sceneFactors = Field<Dictionary<int, float>>(scene, "_sceneTextureAlphaFactor");
            var sceneWraps = Field<Dictionary<int, TextureWrapPolicy>>(scene, "_sceneTextureWrap");
            Check(sceneTextures.TryGetValue(originalKtx, out int ktxTwin) && GL.IsTexture(ktxTwin),
                "prepared KTX upload promotes its stable binding to a resident scene-color twin");
            Check(sceneAlpha.TryGetValue(originalKtx, out TextureAlphaPolicy storedOpaque)
                && storedOpaque == opaquePolicy && sceneFactors.TryGetValue(originalKtx, out float storedFactor)
                && Math.Abs(storedFactor - 0.25f) < 0.0001f,
                "OPAQUE alpha policy and authored factor survive prepared upload promotion");
            Check(InternalFormat(originalKtx) == Rgba8 && InternalFormat(ktxTwin) == Srgb8Alpha8,
                "Original-compatible storage remains RGBA8 while the authored sRGB twin uses sRGB8-alpha8");
            byte[] expectedBase = { 210, 90, 30, 17, 30, 160, 220, 17, 15, 45, 180, 17, 240, 200, 10, 17 };
            byte[] expectedMip = { 101, 111, 71, 17 };
            Check(ReadLevel(originalKtx, 0, 2, 2).SequenceEqual(expectedBase)
                && ReadLevel(ktxTwin, 0, 2, 2).SequenceEqual(expectedBase)
                && ReadLevel(ktxTwin, 1, 1, 1).SequenceEqual(expectedMip),
                "OPAQUE KTX keeps every authored RGBA mip byte, including source alpha");
            int repeatedKtx = preparedAssets.Upload("linear-opaque-fixture",
                () => new MemoryStream(opaqueKtx, writable: false), TextureAssetClass.World,
                TextureAssetChannel.Albedo, repeat: false, out _, out _, opaqueSemantics);
            Check(repeatedKtx == originalKtx && sceneTextures[repeatedKtx] == ktxTwin,
                "repeated prepared request keeps its original and promoted GL bindings stable");

            byte[] transparentEdgePng = MakeTransparentEdgePng();
            var blendPolicy = new TextureAlphaPolicy(TextureAlphaMode.Blend);
            var repeatedSemantics = TextureAssetSemantics.ForChannel(TextureAssetChannel.Albedo,
                alphaPolicy: blendPolicy, wrap: new TextureWrapPolicy(RepeatMode.Repeat, RepeatMode.Clamp));
            var clampedSemantics = repeatedSemantics.WithWrap(TextureWrapPolicy.Clamp);
            int repeatedEdgeOriginal = preparedAssets.Upload("linear-wrap-edge",
                () => new MemoryStream(transparentEdgePng, writable: false), TextureAssetClass.World,
                TextureAssetChannel.Albedo, repeat: false, out _, out _, repeatedSemantics);
            int clampedEdgeOriginal = preparedAssets.Upload("linear-wrap-edge",
                () => new MemoryStream(transparentEdgePng, writable: false), TextureAssetClass.World,
                TextureAssetChannel.Albedo, repeat: false, out _, out _, clampedSemantics);
            int repeatedEdgeTwin = sceneTextures[repeatedEdgeOriginal];
            int clampedEdgeTwin = sceneTextures[clampedEdgeOriginal];
            byte[] repeatedEdgePixels = ReadLevel(repeatedEdgeTwin, 0, 5, 1);
            byte[] clampedEdgePixels = ReadLevel(clampedEdgeTwin, 0, 5, 1);
            Check(repeatedEdgeOriginal != clampedEdgeOriginal && repeatedEdgeTwin != clampedEdgeTwin
                && repeatedEdgePixels[0] > 200 && clampedEdgePixels[0] == 0
                && repeatedEdgePixels[3] == 0 && clampedEdgePixels[3] == 0,
                "generic sampler wraps participate in prepared identity and transparent-edge dilation only");
            Check(TextureWrap(repeatedEdgeTwin, GetTextureParameter.TextureWrapS) == (int)TextureWrapMode.Repeat
                && TextureWrap(clampedEdgeTwin, GetTextureParameter.TextureWrapS) == (int)TextureWrapMode.ClampToEdge
                && TextureWrap(repeatedEdgeTwin, GetTextureParameter.TextureWrapT) == (int)TextureWrapMode.ClampToEdge,
                "promoted generic texture twins retain their actual per-axis sampler modes");

            // Native uploads may receive a pooled array larger than the image.
            // The scene callback must only copy width * height pixels.
            ColorRgba[] pooled =
            {
                new(33, 66, 99, 132), new(211, 177, 143, 109),
                new(7, 8, 9, 10), new(250, 249, 248, 247)
            };
            int nativeOriginal = scene.BindGetTexture(pooled, 1, 1);
            int nativeTwin = sceneTextures[nativeOriginal];
            Check(ReadWidth(nativeTwin, 0) == 1
                && ReadLevel(nativeTwin, 0, 1, 1).SequenceEqual(new byte[] { 33, 66, 99, 132 }),
                "native scene-color callback copies only the declared image from a larger pooled pixel array");
            Check(sceneAlpha.TryGetValue(nativeOriginal, out TextureAlphaPolicy nativeAlpha)
                && nativeAlpha.Mode == TextureAlphaMode.Legacy && sceneFactors[nativeOriginal] == 1f,
                "native pixel promotion uses legacy alpha defaults and factor one");

            var maskPolicy = new TextureAlphaPolicy(TextureAlphaMode.Mask, 0.37f);
            TextureAssetSemantics maskSemantics = TextureAssetSemantics.ForChannel(
                TextureAssetChannel.Albedo, alphaPolicy: maskPolicy,
                wrap: TextureWrapPolicy.Clamp, alphaFactor: 0.72f);
            byte[] modernPixels =
            {
                240, 40, 20, 255, 10, 210, 45, 128,
                15, 60, 230, 0, 200, 180, 30, 64
            };
            int modernOriginal = scene.BindGetTexture(new[]
                { new ColorRgba(100, 120, 140, 255), new ColorRgba(80, 90, 100, 255),
                  new ColorRgba(60, 70, 80, 255), new ColorRgba(40, 50, 60, 255) }, 2, 2,
                sceneColor: false);
            manualOriginals.Add(modernOriginal);
            var modern = ModernTextureAsset.FromRgba("linear-modern-semantics",
                TextureAssetClass.World, TextureAssetChannel.Albedo, 2, 2, modernPixels, maskSemantics);
            RegisterPrepared(scene, modernOriginal, modern);
            int modernTwin = sceneTextures[modernOriginal];
            Check(InternalFormat(modernTwin) == Srgb8Alpha8
                && sceneAlpha[modernOriginal] == maskPolicy
                && Math.Abs(sceneFactors[modernOriginal] - 0.72f) < 0.0001f,
                "modern RGBA semantics retain sRGB transfer, MASK cutoff and alpha factor on the promoted binding");
            Check(ReadWidth(modernTwin, 1) == 1,
                "modern RGBA promotion creates a filtered mip chain for the opt-in upload");

            // An already-authored RGBA chain must go to the sRGB storage format
            // unchanged rather than being regenerated by the semantic filter.
            byte[] authoredBase =
            {
                10, 20, 30, 40, 50, 60, 70, 80,
                90, 100, 110, 120, 130, 140, 150, 160
            };
            byte[] authoredLower = { 201, 111, 21, 131 };
            int authoredOriginal = scene.BindGetTexture(new[] { new ColorRgba(1, 2, 3, 255) }, 1, 1,
                sceneColor: false);
            manualOriginals.Add(authoredOriginal);
            var authoredSemantics = TextureAssetSemantics.ForChannel(TextureAssetChannel.Albedo,
                alphaPolicy: maskPolicy, transferOverride: TextureTransferFunction.Srgb,
                wrap: TextureWrapPolicy.Clamp, alphaFactor: 0.61f);
            var authored = new RgbaMipTextureAsset("linear-authored-rgba-mips",
                TextureAssetClass.World, TextureAssetChannel.Albedo,
                new[] { new CompressedTextureMip(2, 2, authoredBase),
                    new CompressedTextureMip(1, 1, authoredLower) }, authoredSemantics);
            RegisterPrepared(scene, authoredOriginal, authored);
            int authoredTwin = sceneTextures[authoredOriginal];
            Check(InternalFormat(authoredTwin) == Srgb8Alpha8
                && ReadLevel(authoredTwin, 0, 2, 2).SequenceEqual(authoredBase)
                && ReadLevel(authoredTwin, 1, 1, 1).SequenceEqual(authoredLower),
                "prepared authored RGBA mips upload as sRGB and retain exact artist-authored levels");
            Check(sceneAlpha[authoredOriginal] == maskPolicy
                && Math.Abs(sceneFactors[authoredOriginal] - 0.61f) < 0.0001f,
                "authored RGBA mip metadata remains attached to its stable base binding");

            // Numeric normal texels still use linear UNORM storage in the same
            // upload helper even though the caller opts into color uploads.
            normalTexture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, normalTexture);
            byte[] normals =
            {
                128, 128, 255, 255, 128, 128, 255, 255,
                128, 128, 255, 255, 128, 128, 255, 255
            };
            TextureAssetSemantics normalSemantics = TextureAssetSemantics.ForChannel(TextureAssetChannel.Normal);
            PreparedTextureAsset normal = ColorTexturePolicy.PrepareForLinearFiltering(
                ModernTextureAsset.FromRgba("linear-normal-unorm", TextureAssetClass.World,
                    TextureAssetChannel.Normal, 2, 2, normals, normalSemantics));
            TextureAssetManager.UploadPreparedBound(normal, repeat: true,
                TextureSamplingPolicy.ResolveModern(TextureAssetClass.World, TextureAssetChannel.Normal),
                linearColor: true);
            Check(InternalFormat(normalTexture) == Rgba8,
                "normal data uses linear RGBA8 UNORM storage when color uploads are enabled");

            Check(GL.GetError() == ErrorCode.NoError, "linear material asset integration leaves GL error state clean");

            preparedAssets.Dispose();
            preparedAssets = null;
            Check(!GL.IsTexture(originalKtx) && !GL.IsTexture(ktxTwin)
                && !GL.IsTexture(repeatedEdgeOriginal) && !GL.IsTexture(repeatedEdgeTwin)
                && !GL.IsTexture(clampedEdgeOriginal) && !GL.IsTexture(clampedEdgeTwin)
                && !sceneTextures.ContainsKey(originalKtx)
                && !sceneTextures.ContainsKey(repeatedEdgeOriginal) && !sceneTextures.ContainsKey(clampedEdgeOriginal)
                && !sceneAlpha.ContainsKey(originalKtx) && !sceneAlpha.ContainsKey(repeatedEdgeOriginal)
                && !sceneAlpha.ContainsKey(clampedEdgeOriginal) && !sceneFactors.ContainsKey(originalKtx)
                && !sceneFactors.ContainsKey(repeatedEdgeOriginal) && !sceneFactors.ContainsKey(clampedEdgeOriginal)
                && !sceneWraps.ContainsKey(originalKtx) && !sceneWraps.ContainsKey(repeatedEdgeOriginal)
                && !sceneWraps.ContainsKey(clampedEdgeOriginal)
                && sceneTextures.TryGetValue(nativeOriginal, out int survivingNativeTwin)
                && survivingNativeTwin == nativeTwin && GL.IsTexture(nativeOriginal) && GL.IsTexture(nativeTwin)
                && sceneTextures.TryGetValue(modernOriginal, out int survivingModernTwin)
                && survivingModernTwin == modernTwin && GL.IsTexture(modernOriginal) && GL.IsTexture(modernTwin)
                && sceneTextures.TryGetValue(authoredOriginal, out int survivingAuthoredTwin)
                && survivingAuthoredTwin == authoredTwin && GL.IsTexture(authoredOriginal) && GL.IsTexture(authoredTwin)
                && sceneAlpha[nativeOriginal].Mode == TextureAlphaMode.Legacy && sceneFactors[nativeOriginal] == 1f
                && sceneAlpha[modernOriginal] == maskPolicy && Math.Abs(sceneFactors[modernOriginal] - 0.72f) < 0.0001f
                && sceneAlpha[authoredOriginal] == maskPolicy && Math.Abs(sceneFactors[authoredOriginal] - 0.61f) < 0.0001f
                && sceneWraps[modernOriginal] == TextureWrapPolicy.Clamp
                && sceneWraps[authoredOriginal] == TextureWrapPolicy.Clamp,
                "texture manager teardown retires its bindings and metadata while unrelated scene-color residents remain intact");
            ReleaseSceneTexture(scene, nativeOriginal);
            Check(!GL.IsTexture(nativeOriginal) && !GL.IsTexture(nativeTwin)
                && !sceneTextures.ContainsKey(nativeOriginal) && !sceneAlpha.ContainsKey(nativeOriginal)
                && !sceneFactors.ContainsKey(nativeOriginal) && !sceneWraps.ContainsKey(nativeOriginal),
                "native stable-binding release retires its resident scene-color twin");
            foreach (int original in manualOriginals)
                ReleaseSceneTexture(scene, original);
            Check(manualOriginals.All(binding => !GL.IsTexture(binding) && !sceneTextures.ContainsKey(binding))
                && !GL.IsTexture(modernTwin) && !GL.IsTexture(authoredTwin)
                && sceneTextures.Count == 0 && sceneAlpha.Count == 0 && sceneFactors.Count == 0
                && sceneWraps.Count == 0,
                "prepared modern and authored-mip twins retire with their stable base bindings");
            GL.DeleteTexture(normalTexture);
            normalTexture = 0;
            Check(GL.GetError() == ErrorCode.NoError, "linear material resident retirement leaves GL error state clean");
            Console.WriteLine("LINEAR MATERIAL GPU PASS: generic prepared KTX residency, sRGB RGBA/mips, alpha metadata, pooled native upload, normal UNORM, stable promotion and twin retirement");
        }
        finally
        {
            if (normalTexture != 0 && GL.IsTexture(normalTexture)) GL.DeleteTexture(normalTexture);
            if (scene != null)
            {
                preparedAssets?.Dispose();
                scene.UnloadGl();
            }
            GL.BindTexture(TextureTarget.Texture2D, previousBinding);
            GL.ActiveTexture((TextureUnit)previousActiveTexture);
            RenderOptions.LinearColor = previousLinearColor;
        }
    }

    private static int AllocateSceneTexture(Scene scene)
    {
        MethodInfo allocate = typeof(Scene).GetMethod("AllocateTexture", Hidden)
            ?? throw new MissingMethodException(typeof(Scene).FullName, "AllocateTexture");
        return (int)allocate.Invoke(scene, null)!;
    }

    private static void RegisterPrepared(Scene scene, int original, PreparedTextureAsset asset)
    {
        MethodInfo register = typeof(Scene).GetMethod("RegisterSceneColorAsset", Hidden)
            ?? throw new MissingMethodException(typeof(Scene).FullName, "RegisterSceneColorAsset");
        register.Invoke(scene, new object[] { original, asset });
    }

    private static void ReleaseSceneTexture(Scene scene, int original)
    {
        MethodInfo release = typeof(Scene).GetMethod("ReleaseTexture", Hidden)
            ?? throw new MissingMethodException(typeof(Scene).FullName, "ReleaseTexture");
        release.Invoke(scene, new object[] { original });
    }

    private static T Field<T>(Scene scene, string name)
        => (T)(typeof(Scene).GetField(name, Hidden)?.GetValue(scene)
            ?? throw new MissingFieldException(typeof(Scene).FullName, name));

    private static int InternalFormat(int binding, int level = 0)
    {
        GL.BindTexture(TextureTarget.Texture2D, binding);
        GL.GetTexLevelParameter(TextureTarget.Texture2D, level,
            GetTextureParameter.TextureInternalFormat, out int format);
        return format;
    }

    private static int ReadWidth(int binding, int level)
    {
        GL.BindTexture(TextureTarget.Texture2D, binding);
        GL.GetTexLevelParameter(TextureTarget.Texture2D, level,
            GetTextureParameter.TextureWidth, out int width);
        return width;
    }

    private static int TextureWrap(int binding, GetTextureParameter parameter)
    {
        GL.BindTexture(TextureTarget.Texture2D, binding);
        GL.GetTexParameter(TextureTarget.Texture2D, parameter, out int wrap);
        return wrap;
    }

    private static byte[] ReadLevel(int binding, int level, int width, int height)
    {
        GL.BindTexture(TextureTarget.Texture2D, binding);
        byte[] pixels = new byte[checked(width * height * 4)];
        GL.GetTexImage(TextureTarget.Texture2D, level, PixelFormat.Rgba,
            PixelType.UnsignedByte, pixels);
        return pixels;
    }

    private static byte[] MakeSrgbMipKtx()
    {
        string path = Path.Combine(Path.GetTempPath(), "prime-linear-material-" + Guid.NewGuid().ToString("N") + ".ktx2");
        Ktx2.Texture* texture = null;
        try
        {
            var info = new Ktx2.TextureCreateInfo
            {
                VkFormat = Ktx2.VkFormat.R8G8B8A8Srgb,
                BaseWidth = 2, BaseHeight = 2, BaseDepth = 1,
                NumDimensions = 2, NumLevels = 2, NumLayers = 1, NumFaces = 1
            };
            Success(Ktx2.Create(in info, Ktx2.TextureCreateStorage.AllocStorage, out texture));
            byte[] basePixels = { 210, 90, 30, 17, 30, 160, 220, 17, 15, 45, 180, 17, 240, 200, 10, 17 };
            byte[] mipPixels = { 101, 111, 71, 17 };
            Success(Ktx2.SetImageFromMemory(texture, 0, 0, 0, in basePixels[0], (nuint)basePixels.Length));
            Success(Ktx2.SetImageFromMemory(texture, 1, 0, 0, in mipPixels[0], (nuint)mipPixels.Length));
            Success(Ktx2.WriteToNamedFile(texture, path));
            return File.ReadAllBytes(path);
        }
        finally
        {
            if (texture != null) Ktx2.Destroy(texture);
            File.Delete(path);
        }
    }

    private static byte[] MakeTransparentEdgePng()
    {
        byte[] pixels =
        {
            0,0,0,0, 0,0,0,0, 0,0,0,255, 0,0,0,255, 255,0,0,255
        };
        using var stream = new MemoryStream();
        MphRead.Mods.PngEncoder.Write<byte>(pixels, 5, 1,
            ReFuel.Stb.StbiImageFormat.Rgba, stream);
        return stream.ToArray();
    }

    private static void Success(Ktx2.ErrorCode result)
    {
        if (result != Ktx2.ErrorCode.Success)
            throw new Exception("Linear material KTX fixture: " + result);
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }
}
