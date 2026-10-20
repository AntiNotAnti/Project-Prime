using System;
using System.Buffers.Binary;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.Import.Retro;
using MphRead.Mods.MapGen;
using MphRead.Mods.Render.Materials;
using OpenTK.Graphics.OpenGL;

// Runs inside GpuCheck's existing context and Scene. All bank/TXTR bytes are
// generated here; the archive/area identifiers are format metadata only.
internal static class SourceBankGpuCheck
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly MethodInfo Refresh = typeof(Scene).GetMethod("RefreshTextureQuality", Hidden)!;
    private static readonly MethodInfo Pump = typeof(Scene).GetMethod("PumpModernTextureStreaming", Hidden)!;
    private static readonly FieldInfo Queue = typeof(Scene).GetField("_streamingTextureQueue", Hidden)!;
    private static readonly FieldInfo Decodes = typeof(Scene).GetField("_streamingTextureDecodes", Hidden)!;

    internal static void WaitForIdle(Scene scene)
    {
        var watch = Stopwatch.StartNew();
        do
        {
            Pump.Invoke(scene, null);
            if (Count(Queue, scene) == 0 && Count(Decodes, scene) == 0) return;
            Thread.Sleep(10);
        } while (watch.Elapsed < TimeSpan.FromSeconds(10));
        throw new Exception("Texture streaming did not drain within ten seconds.");
    }

    internal static void Run(string root, string packRoot, Scene scene, Model model,
        Guid mapId, Guid firstId, Guid secondId)
    {
        bool oldReplacements = RenderOptions.TextureReplacements;
        TextureUpscaleMode oldUpscale = RenderOptions.TextureUpscale;
        TextureAssetQuality oldQuality = RenderOptions.TextureQuality;
        TextureSamplingMode oldSampling = RenderOptions.TextureSampling;
        byte[] redGreen = { 255, 0, 0, 255, 0, 255, 0, 255 };
        byte[] blueYellow = { 0, 0, 255, 255, 255, 255, 0, 255 };
        byte[] firstTxtr = Texture(0xfc00, 0x83e0);
        byte[] secondTxtr = Texture(0x801f, 0xffe0);
        byte[] oldBytes = Bank(firstTxtr, secondTxtr);
        byte[] newBytes = Bank(secondTxtr, firstTxtr);
        string folder = Path.Combine(root, "source-bank-gpu");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "old.bin"), oldBytes);
        File.WriteAllBytes(Path.Combine(folder, "new.bin"), newBytes);
        var definition = new MapDefinition
        {
            MapId = mapId, BaseDirectory = folder,
            Assets = new() { new() { Path = "old.bin", Kind = "source-texture-bank" } },
            Materials = new()
            {
                new() { Id = firstId, Name = "first", ProceduralWhite = true,
                    AlbedoBank = Reference("old.bin", oldBytes, 101, redGreen) },
                new() { Id = secondId, Name = "second", ProceduralWhite = true,
                    AlbedoBank = Reference("old.bin", oldBytes, 202, blueYellow) }
            }
        };
        Type snapshotType = typeof(Scene).Assembly.GetType("MphRead.Mods.Render.Materials.SourceBankMaterialSnapshot", true)!;
        object snapshot = snapshotType.GetMethod("Capture", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { definition })!;
        typeof(Model).GetProperty("SourceBankMaterials", Hidden)!.SetValue(model, snapshot);
        Type uploader = typeof(Scene).Assembly.GetType("MphRead.Mods.Render.TextureReplacementPack", true)!;
        void Reload() => uploader.GetMethod("Reload")!.Invoke(null, null);
        void RefreshAndUpdate()
        {
            Refresh.Invoke(scene, null);
            scene.UpdateMaterials(model, 0);
        }
        int[] Bindings() => model.Materials.Select(m => m.TextureBindingId).ToArray();
        void CheckImages(string phase)
        {
            int[] bindings = Bindings();
            if (bindings[0] == bindings[1] || bindings.Any(id => id == 0 || !GL.IsTexture(id)))
                throw new Exception(phase + ": source banks merged independent material assignments.");
            CheckTexture(bindings[0], 2, 1, redGreen, phase + " first");
            CheckTexture(bindings[1], 2, 1, blueYellow, phase + " second");
            var flatColors = (IDictionary)typeof(Scene).GetField("_flatColors", Hidden)!.GetValue(scene)!;
            for (int i = 0; i < bindings.Length; i++)
            {
                var expected = i == 0 ? new OpenTK.Mathematics.Vector3(.5f, .5f, 0)
                    : new OpenTK.Mathematics.Vector3(.5f);
                if (flatColors[bindings[i]] is not OpenTK.Mathematics.Vector3 actual
                    || !float.IsFinite(actual.X) || !float.IsFinite(actual.Y) || !float.IsFinite(actual.Z)
                    || (actual - expected).LengthSquared > 1e-8f)
                    throw new Exception(phase + ": source promotion retained the native flat-color average.");
            }
        }
        try
        {
            // An empty local manifest leaves captured source banks authoritative.
            MaterialInventory.Write(Path.Combine(packRoot, "materials.json"), new MaterialPackManifest());
            Reload();
            RenderOptions.TextureReplacements = true;
            RenderOptions.TextureUpscale = TextureUpscaleMode.Off;
            RenderOptions.TextureSampling = TextureSamplingMode.Legacy;
            typeof(Scene).GetMethod("InitTextures", Hidden)!.Invoke(scene, new object[] { model });
            RefreshAndUpdate();
            var requests = ((IEnumerable)Queue.GetValue(scene)!).Cast<object>().ToArray();
            string capturedPrefix = "source-bank/" + Hash(oldBytes) + "/";
            if (requests.Count(request => ((MaterialImage)request.GetType().GetProperty("Image")!
                    .GetValue(request)!).Path.StartsWith(capturedPrefix, StringComparison.Ordinal)) != 2)
                throw new Exception("Source bank albedos did not enter the existing background decode queue.");

            // Change every mutable reference before either worker starts. The
            // captured model must keep reopening its immutable old generation.
            Mutate(definition.Materials[0].AlbedoBank!, Reference("new.bin", newBytes, 101, blueYellow));
            Mutate(definition.Materials[1].AlbedoBank!, Reference("new.bin", newBytes, 202, redGreen));
            definition.Assets[0].Path = "new.bin";
            definition.BaseDirectory = Path.Combine(folder, "edited-root-does-not-exist");
            Pump.Invoke(scene, null);
            if (Count(Decodes, scene) == 0)
                throw new Exception("Source bank fixture did not start an asynchronous decode.");
            // Supersede active requests through the actual material-revision
            // fence; retry still uses the captured model rather than edited DTOs.
            Reload();
            RefreshAndUpdate();
            WaitForIdle(scene);
            CheckImages("captured generation after supersession");
            int[] stable = Bindings();

            RenderOptions.TextureQuality = oldQuality == TextureAssetQuality.Low
                ? TextureAssetQuality.High : TextureAssetQuality.Low;
            RefreshAndUpdate();
            WaitForIdle(scene);
            CheckImages("quality refresh");
            if (!Bindings().SequenceEqual(stable)) throw new Exception("Quality refresh replaced stable source texture handles.");

            RenderOptions.TextureReplacements = false;
            RefreshAndUpdate();
            WaitForIdle(scene);
            // Once created, authored handles remain stable because existing
            // materials/render items may hold them. Disabling reuploads the
            // native fallback into each handle rather than merging identities.
            if (!Bindings().SequenceEqual(stable)) throw new Exception("Disabled bank replacements changed stable authored handles.");
            foreach (int binding in Bindings())
                CheckTexture(binding, 1, 1, new byte[] { 255, 255, 255, 255 }, "disabled fallback");
            RenderOptions.TextureReplacements = true;
            Reload();
            RefreshAndUpdate();
            WaitForIdle(scene);
            CheckImages("re-enabled captured generation");
            if (!Bindings().SequenceEqual(stable)) throw new Exception("Source-bank reload lost stable authored handles.");
            if (GL.GetError() != ErrorCode.NoError) throw new Exception("Source bank GPU fixture left an OpenGL error.");
            Console.WriteLine("Source bank GPU checks PASS: two independent exact RGBA images, queued decode, supersession, captured generation, quality refresh and replacement toggles");
        }
        finally
        {
            GL.BindTexture(TextureTarget.Texture2D, 0);
            RenderOptions.TextureReplacements = oldReplacements;
            RenderOptions.TextureUpscale = oldUpscale;
            RenderOptions.TextureQuality = oldQuality;
            RenderOptions.TextureSampling = oldSampling;
        }
        // GpuCheck's existing UnloadGl and owned-handle checks include these
        // same scene textures. Keep the old bank files until that teardown.
    }

    private static int Count(FieldInfo field, Scene scene)
    {
        object value = field.GetValue(scene)!;
        return (int)value.GetType().GetProperty("Count")!.GetValue(value)!;
    }

    private static void CheckTexture(int binding, int width, int height, byte[] expected, string phase)
    {
        GL.BindTexture(TextureTarget.Texture2D, binding);
        GL.GetTexLevelParameter(TextureTarget.Texture2D, 0, GetTextureParameter.TextureWidth, out int actualWidth);
        GL.GetTexLevelParameter(TextureTarget.Texture2D, 0, GetTextureParameter.TextureHeight, out int actualHeight);
        if (actualWidth != width || actualHeight != height) throw new Exception(phase + ": wrong GPU image dimensions.");
        var pixels = new byte[checked(width * height * 4)];
        GL.GetTexImage(TextureTarget.Texture2D, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
        if (!pixels.SequenceEqual(expected)) throw new Exception(phase + ": GPU RGBA bytes differ from independent constants.");
        GL.BindTexture(TextureTarget.Texture2D, 0);
    }

    private static byte[] Texture(ushort first, ushort second)
    {
        byte[] bytes = new byte[44];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, 8);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6), 1);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), 1);
        for (int i = 0; i < 16; i++) BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(12 + i * 2), (i & 1) == 0 ? first : second);
        return bytes;
    }

    private static byte[] Bank(byte[] first, byte[] second)
        => EchoesSourceTextureBank.Encode(new(0x02690de1, Hash(Encoding.ASCII.GetBytes("content-free MREA fixture"))),
            new EchoesSourceTextureBank.Input[]
            {
                new(101, Hash(first), RetroTextureSource.DecodeTop(first)),
                new(202, Hash(second), RetroTextureSource.DecodeTop(second))
            });

    private static MapSourceBankImage Reference(string path, byte[] bank, uint resource, byte[] rgba)
        => new() { Bank = path, BankSha256 = Hash(bank), ResourceId = resource, RgbaSha256 = Hash(rgba), Width = 2, Height = 1 };

    private static void Mutate(MapSourceBankImage target, MapSourceBankImage edited)
    {
        target.Bank = edited.Bank;
        target.BankSha256 = edited.BankSha256;
        target.ResourceId = edited.ResourceId;
        target.RgbaSha256 = edited.RgbaSha256;
        target.Width = edited.Width;
        target.Height = edited.Height;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
