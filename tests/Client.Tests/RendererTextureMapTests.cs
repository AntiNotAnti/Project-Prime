using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MphRead.Formats;
using MphRead.Mods.Render;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

public sealed class RendererTextureMapTests
{
    [Fact]
    public void UpdateMaterialsLazilyPreparesMissingAnimatedTextureCombo()
    {
        Model model = CreateTwoTextureModel();
        Material material = model.Materials[0];
        material.CurrentTextureId = 1;
        material.CurrentPaletteId = 0;
        ScenePresentation presentation = CreateUninitializedPresentation();

        presentation.UpdateMaterials(model, recolorId: 0);

        FieldInfo mapField = typeof(ScenePresentation).GetField("_texPalMap",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var maps = (Dictionary<int, TextureMap>)mapField.GetValue(presentation)!;
        Assert.True(maps[model.Id].TryGet(1, 0, 0, out _));
        TextureIdentity identity = Assert.IsType<TextureIdentity>(
            presentation.GetTextureIdentity(model, material, recolorId: 0));
        Assert.Equal(1, identity.TextureId);
        Assert.Equal(0, identity.PaletteId);
        Assert.Equal(0, identity.RecolorId);
        Assert.True(presentation.TryGetTexture(identity, out _));
    }

    [Fact]
    public void TextureMapTryGetKeepsMissingCombosObservable()
    {
        var map = new TextureMap();

        Assert.False(map.TryGet(72, 0, 0, out _));
        map.Add(72, 0, 0, bindingId: 9, onlyOpaque: true);

        Assert.True(map.TryGet(72, 0, 0,
            out (int BindingId, bool OnlyOpaque) value));
        Assert.Equal(9, value.BindingId);
        Assert.True(value.OnlyOpaque);
    }

    [Fact]
    public void RendererUnloadDoesNotEvictTheSharedParsedModelCache()
    {
        const string name = "renderer-cache-ownership-probe";
        var model = new Model(name, firstHunt: false, default,
            Array.Empty<RawNode>(), Array.Empty<RawMesh>(),
            Array.Empty<RawMaterial>(), Array.Empty<DisplayList>(),
            Array.Empty<IReadOnlyList<RenderInstruction>>(),
            new AnimationResults(), Array.Empty<Matrix4>(),
            Array.Empty<Recolor>(), Array.Empty<int>(),
            Array.Empty<Vector3Fx>(), Array.Empty<Vector3Fx>(),
            Array.Empty<int>(), Array.Empty<Fixed>());
        FieldInfo cacheField = typeof(Read).GetField("_modelCache",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        var cache = (Dictionary<string, Model>)cacheField.GetValue(null)!;
        string key = $"{MetaDir.Models}:{name}";
        ScenePresentation presentation = CreateUninitializedPresentation();

        lock (ContentEnvironment.SyncRoot)
        {
            cache.Add(key, model);
            try
            {
                presentation.UnloadModel(model);

                Assert.Same(model, cache[key]);
            }
            finally
            {
                cache.Remove(key);
            }
        }
    }

    private static ScenePresentation CreateUninitializedPresentation()
    {
        var presentation = (ScenePresentation)RuntimeHelpers.GetUninitializedObject(
            typeof(ScenePresentation));
        SetField(presentation, "_texPalMap", new Dictionary<int, TextureMap>());
        SetField(presentation, "_textureResources",
            new Dictionary<TextureIdentity, RenderTexturePixels>());
        SetField(presentation, "_bindingTextureIdentities",
            new Dictionary<int, TextureIdentity>());

        FieldInfo bindings = typeof(ScenePresentation).GetField("_materialBindings",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        Type valueType = bindings.FieldType.GetGenericArguments()[1];
        Type tableType = typeof(ConditionalWeakTable<,>).MakeGenericType(
            typeof(Material), valueType);
        bindings.SetValue(presentation, Activator.CreateInstance(tableType));
        return presentation;
    }

    private static void SetField(object target, string name, object value)
        => typeof(ScenePresentation).GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private static Model CreateTwoTextureModel()
    {
        byte[] materialBytes = new byte[Marshal.SizeOf<RawMaterial>()];
        BitConverter.GetBytes((short)0).CopyTo(materialBytes,
            Marshal.OffsetOf<RawMaterial>(nameof(RawMaterial.TextureId)).ToInt32());
        BitConverter.GetBytes((short)0).CopyTo(materialBytes,
            Marshal.OffsetOf<RawMaterial>(nameof(RawMaterial.PaletteId)).ToInt32());
        RawMaterial material = Read.ReadStruct<RawMaterial>(materialBytes);

        var recolor = new Recolor("base",
            new[]
            {
                new Texture(TextureFormat.DirectRgb, 1, 1),
                new Texture(TextureFormat.DirectRgb, 1, 1)
            },
            Array.Empty<Palette>(),
            new IReadOnlyList<TextureData>[]
            {
                new[] { new TextureData(0x00001Fu, 255) },
                new[] { new TextureData(0x0003E0u, 255) }
            },
            Array.Empty<IReadOnlyList<PaletteData>>());

        var animations = new AnimationResults();
        animations.NodeAnimationGroups.Add(NodeAnimationGroup.Empty());
        animations.MaterialAnimationGroups.Add(MaterialAnimationGroup.Empty());
        animations.TexcoordAnimationGroups.Add(TexcoordAnimationGroup.Empty());
        animations.TextureAnimationGroups.Add(TextureAnimationGroup.Empty());

        return new Model("texture-map-lazy", firstHunt: false, default,
            new[] { default(RawNode) }, new[] { default(RawMesh) },
            new[] { material }, Array.Empty<DisplayList>(),
            Array.Empty<IReadOnlyList<RenderInstruction>>(), animations,
            Array.Empty<Matrix4>(), new[] { recolor }, Array.Empty<int>(),
            Array.Empty<Vector3Fx>(), Array.Empty<Vector3Fx>(),
            Array.Empty<int>(), Array.Empty<Fixed>());
    }
}
