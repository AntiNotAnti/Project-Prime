using System;
using MphRead.Mods.Cosmetics;

namespace MphRead.Mods.Render;

internal static class WorldShaderVariantCheck
{
    internal static int Run()
    {
        int failures = 0;
        void Check(bool condition, string name)
        {
            Console.WriteLine($"[worldshadercheck] {(condition ? "PASS" : "FAIL")}: {name}");
            if (!condition) failures++;
        }
        void CheckSource(string full, string native, string backend, bool cosmetics = false)
        {
            string signature = cosmetics ? "void apply_cosmetics(inout vec4 col)" : "void apply_material_lighting(inout vec4 col)";
            int start = full.IndexOf(signature, StringComparison.Ordinal);
            int open = full.IndexOf('{', start);
            int level = 1, close = open + 1;
            while (close < full.Length && level > 0)
            {
                if (full[close] == '{') level++;
                if (full[close] == '}') level--;
                close++;
            }
            string prefix = full.Substring(0, open + 1);
            string suffix = full.Substring(close - 1);
            Check(native.StartsWith(prefix, StringComparison.Ordinal)
                && native.EndsWith(suffix, StringComparison.Ordinal), backend + " preserves every expression outside the removed stage");
            Check(native.Length == prefix.Length + 1 + suffix.Length,
                backend + " removes the complete nested stage body only");
            string guard = cosmetics ? "if (cosmetic_skin == 0 && cosmetic_effect == 0 && cosmetic_dissolve <= 0.0) return;" : "if (!advanced_materials) return;";
            Check(full.Contains(guard, StringComparison.Ordinal)
                && !native.Contains(guard, StringComparison.Ordinal),
                backend + " canonical fallback retains the original stage");
            Check(native.Contains("cosmetic_dissolve", StringComparison.Ordinal)
                && native.Contains("depth_only && !use_override", StringComparison.Ordinal)
                && native.Contains("mat_mode == 1", StringComparison.Ordinal),
                backend + " retains cosmetic and alpha coverage paths");
        }
        CheckSource(Shaders.FragmentShader, Shaders.NativeFragmentShader, "desktop");
        CheckSource(Shaders.NativeFragmentShader, Shaders.NativeNoCosmeticsFragmentShader, "desktop plain", cosmetics: true);
        void CheckUnlitVertex(string full, string unlit, string backend)
        {
            Check(unlit.Replace("const bool use_light = false;", "uniform bool use_light;") == full,
                backend + " specializes lighting value without changing skinning, UV, billboard or alpha expressions");
            Check(unlit.Contains("use_light ? view_mtx : mat4(1.0)", StringComparison.Ordinal),
                backend + " normal texgen retains constant-false camera transform choice");
        }
        CheckUnlitVertex(Shaders.VertexShader, Shaders.UnlitVertexShader, "desktop depth vertex");
        CheckUnlitVertex(Shaders.LegacyVertexShader, Shaders.UnlitLegacyVertexShader, "desktop legacy vertex");
        Check(Scene.PlainWorldShaderEligible(Array.Empty<RetainedDrawPacket>()), "empty world is eligible");
        var neutralItem = new RenderItem { Type = RenderItemType.Mesh };
        var testedItem = new RenderItem { Type = RenderItemType.Mesh };
        var packets = new[] {
            new RetainedDrawPacket(neutralItem, 0, new RetainedMeshDescriptor(RetainedMeshDescriptor.KeyOf(neutralItem))),
            new RetainedDrawPacket(testedItem, 1, new RetainedMeshDescriptor(RetainedMeshDescriptor.KeyOf(testedItem)))
        };
        Check(Scene.PlainWorldShaderEligible(packets), "neutral items eligible");
        foreach (CosmeticSurface surface in new[] {
            default(CosmeticSurface) with { Skin = 1 }, default(CosmeticSurface) with { Effect = 1 },
            default(CosmeticSurface) with { Dissolve = .01f }, default(CosmeticSurface) with { Dissolve = float.NaN },
            default(CosmeticSurface) with { Dissolve = float.PositiveInfinity }
        })
        {
            testedItem.Cosmetics = surface;
            Check(!Scene.PlainWorldShaderEligible(packets), "later active/nonfinite cosmetic item rejects specialization");
        }
        testedItem.Cosmetics = default(CosmeticSurface) with { Dissolve = -.1f, Time = 4, Intensity = 1 };
        Check(Scene.PlainWorldShaderEligible(packets), "inactive cosmetic parameters follow canonical early return");
        var overlays = new[] { neutralItem, testedItem };
        Check(Scene.PlainWorldItemsEligible(overlays), "neutral separately collected preview items eligible");
        testedItem.Cosmetics = default(CosmeticSurface) with { Effect = 2 };
        Check(!Scene.PlainWorldItemsEligible(overlays), "active preview item rejects whole-frame plain program");
        Check(!Scene.PlainCosmeticEligible(testedItem.Cosmetics), "outlined item outside retained packets rejects whole-frame plain program");
        testedItem.Lighting = neutralItem.Lighting = false;
        Check(Scene.UnlitWorldShaderEligible(packets) && Scene.UnlitWorldItemsEligible(overlays),
            "all-unlit world and preview items permit constant vertex lighting");
        testedItem.Lighting = true;
        Check(!Scene.UnlitWorldShaderEligible(packets) && !Scene.UnlitWorldItemsEligible(overlays),
            "later lit world/preview item rejects constant vertex lighting");
        bool rejectsMissingStage = false;
        try { Shaders.NativeMaterialSource("void main() {}\n"); }
        catch (InvalidOperationException) { rejectsMissingStage = true; }
        Check(rejectsMissingStage, "missing material stage fails explicitly");
#if ANDROID
        EsShaders.CheckInSync();
        CheckSource(EsShaders.FragmentShader, EsShaders.NativeFragmentShader, "ES");
        CheckSource(EsShaders.NativeFragmentShader, EsShaders.NativeNoCosmeticsFragmentShader, "ES plain", cosmetics: true);
        CheckUnlitVertex(EsShaders.VertexShader, EsShaders.UnlitVertexShader, "ES depth vertex");
        CheckUnlitVertex(EsShaders.LegacyVertexShader, EsShaders.UnlitLegacyVertexShader, "ES legacy vertex");
        Check(ReferenceEquals(EsShaders.Translate(Shaders.UnlitVertexShader), EsShaders.UnlitVertexShader)
            && ReferenceEquals(EsShaders.Translate(Shaders.UnlitLegacyVertexShader), EsShaders.UnlitLegacyVertexShader),
            "ES unlit source variants translate before compilation");
        Check(ReferenceEquals(EsShaders.Translate(Shaders.NativeNoCosmeticsFragmentShader), EsShaders.NativeNoCosmeticsFragmentShader),
            "ES plain source translates before compilation");
        Check(ReferenceEquals(EsShaders.Translate(Shaders.NativeFragmentShader), EsShaders.NativeFragmentShader),
            "ES native source translates before compilation");
#endif
        return failures == 0 ? 0 : 1;
    }
}
