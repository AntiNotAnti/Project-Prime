using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render;

internal static class DepthReplayPolicyCheck
{
    internal static int Run()
    {
        int failures = 0;
        void Check(bool condition, string name)
        {
            Console.WriteLine($"[depthreplaycheck] {(condition ? "PASS" : "FAIL")}: {name}");
            if (!condition) failures++;
        }
        var item = new RenderItem { Type = RenderItemType.Mesh, Alpha = 1f };
        bool Reject(bool textures = true, bool outline = false) => DepthReplayPolicy.RejectsTranslucentAlpha(item, textures, outline);
        Check(Reject(), "untextured unit alpha cannot alter mark-pass depth");
        item.HasTexture = true;
        Check(!Reject() && Reject(false), "textured modulate needs alpha proof unless textures hidden");
        item.PolygonMode = PolygonMode.Decal;
        Check(Reject(), "decal alpha ignores sampled texture alpha");
        item.PolygonMode = PolygonMode.Toon;
        Check(!Reject(), "textured toon retains sampled alpha fallback");
        item.HasTexture = false;
        foreach (float alpha in new[] { 0f, .999f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            item.Alpha = alpha; Check(!Reject(), "unsafe/nonfinite alpha falls back: " + alpha);
        }
        item.Alpha = 1f;
        Check(!Reject(outline: true), "outline draw cannot claim ordinary alpha proof");
        item.OverrideColor = Vector4.One; Check(!Reject(), "override is a proof barrier"); item.OverrideColor = null;
        item.PaletteOverride = Vector4.One; Check(!Reject(), "palette override conservatively falls back"); item.PaletteOverride = null;
        item.PlayerOutlineColor = Vector4.One; Check(!Reject(), "per-item outline is a proof barrier"); item.PlayerOutlineColor = null;
        item.TexturedPlayerSkin = true; Check(!Reject(), "player skin is a proof barrier"); item.TexturedPlayerSkin = false;
        foreach (var cosmetic in new[] { default(Mods.Cosmetics.CosmeticSurface) with { Skin = 1 },
            default(Mods.Cosmetics.CosmeticSurface) with { Effect = 1 }, default(Mods.Cosmetics.CosmeticSurface) with { Dissolve = .1f },
            default(Mods.Cosmetics.CosmeticSurface) with { Dissolve = float.NaN } })
        {
            item.Cosmetics = cosmetic; Check(!Reject(), "cosmetic or nonfinite dissolve cannot claim coverage");
        }
        item.Cosmetics = default;
        item.CosmeticMaterial = new(1, 0, 0, 0); Check(!Reject(), "cosmetic material conservatively falls back"); item.CosmeticMaterial = default;
        foreach (var type in new[] { RenderItemType.Particle, RenderItemType.TrailSingle, RenderItemType.TrailMulti, RenderItemType.TrailStack })
        {
            item.Type = type; Check(Reject(), type + " has canonical alpha proof independent of current points/pose");
        }
        item.Type = RenderItemType.Mesh;
        var packet = new RetainedDrawPacket(item, 0, new RetainedMeshDescriptor(RetainedMeshDescriptor.KeyOf(item)));
        var packets = new[] { packet };
        Check(DepthReplayPolicy.CanKeepOpaqueDepth(0, packets, true, false)
            && !DepthReplayPolicy.CanKeepOpaqueDepth(1, packets, true, false)
            && !DepthReplayPolicy.CanKeepOpaqueDepth(0, packets, true, false, true), "nonempty alpha-rejected list respects decal and legacy barriers");
        item.Alpha = .5f;
        Check(!DepthReplayPolicy.CanKeepOpaqueDepth(0, packets, true, false), "current-frame alpha mutation invalidates prior packet proof");
        Check(DepthReplayPolicy.CanKeepOpaqueDepth(0, Array.Empty<RetainedDrawPacket>(), true, false), "empty intervening lists preserve opaque depth");
        return failures;
    }
}
