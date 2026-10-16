using System;
using System.Linq;
using MphRead.Mods.MapGen;

namespace MphRead.NetTest;

internal static class Q3ShaderStagesTests
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool ok, string label)
        {
            checks++;
            if (!ok) throw new InvalidOperationException("Q3 SHADER STAGES " + label);
        }

        string script = """
        // Shader scripts are read from user-owned archives.
        textures/test/panel
        {
            qer_editorimage textures/test/preview.tga
            {
                map textures/test/panel.jpg
                tcMod scroll 0.05 -0.01
                tcMod rotate 45
                tcMod scale 2 1
            }
        }
        /*
          bogus shader
          {
            { tcMod scroll NaN 900 }
          }
        */
        textures/test/fog
        {
            fogparms ( 0.25 0.50 0.75 ) 512
            {
                map textures/test/fog.tga
                blendFunc blend
                rgbGen wave sin 0.5 0.5 0 0.2
            }
        }
        textures/test/multipass
        {
            { map textures/test/a.jpg }
            {
                map textures/test/b.jpg
                blendfunc add
            }
        }
        """;
        var result = Q3ShaderStageParser.Parse(script);
        Check(result.Count == 3, "nested brace/comment parser keeps only source shaders");
        var panel = result["textures/test/panel"];
        Check(panel.Stages.Count == 1 && panel.Stages[0].Texture == "textures/test/panel.jpg",
            "first image-bearing stage is preserved");
        Check(MathF.Abs(panel.Stages[0].ScrollS - .05f) < .00001f
            && MathF.Abs(panel.Stages[0].ScrollT + .01f) < .00001f,
            "scroll preserves signed Quake velocities");
        Check(panel.TryNativeUv(out var animation) && animation.LoopFrames >= 30
            && animation.LoopFrames <= MapUvAnimation.MaxLoopFrames,
            "bounded native UV cycle available without network format changes");
        Check(animation.UvScale[0] == 2f && animation.UvScale[1] == 1f
            && animation.UvRotationDegreesPerSecond == 45f,
            "native scale and rotate rates preserve authored values");
        var fog = result["textures/test/fog"];
        Check(fog.HasFog && MathF.Abs(fog.FogColor.Y - .5f) < .00001f
            && MathF.Abs(fog.FogDistance - 512) < .00001f,
            "fog color and distance are parsed as finite source metadata");
        Check(fog.RequiresFullShader && !fog.TryNativeUv(out _),
            "fog + alpha wave cannot be misrepresented as a single native pass");
        Check(result["textures/test/multipass"].RequiresFullShader
            && !result["textures/test/multipass"].TryNativeUv(out _),
            "additive multipass stages are not silently flattened to one texture");
        Check(Q3ShaderStageParser.Parse("x { { map image tcMod scroll NaN 0 } }")
            ["x"].TryNativeUv(out _) == false,
            "nonfinite scroll cannot become active native UV animation");
        Check(Q3ShaderStageParser.Parse("x { { map image tcMod turb 1 2 3 4 } }")
            ["x"].RequiresFullShader, "unsupported turbulence fails closed");
        Check(Q3ShaderStageParser.Parse(new string('x', Q3ShaderStageParser.MaxScriptChars + 1)).Count == 0,
            "oversized shader scripts rejected");

        var authored = new MapMaterial[]
        {
            new MapMaterial { Name = "custom-native", Animation = new MapMaterialAnimation
                { UvScroll = new[] { 0.1f, 0f }, LoopFrames = 300 } }
        };
        var imported = Q3ShaderNativePlan.Build(authored,
            new[] { "textures/test/panel", "textures/test/fog",
                "textures/test/multipass" }, result);
        Check(imported.Count == 4 && imported[0].Animation != null,
            "single-stage UV curve preserves shader material ordering");
        Check(imported[1].Animation == null && imported[2].Animation == null,
            "fog and multipass remain safe baked-image fallbacks");
        Check(ReferenceEquals(imported[3], authored[0]),
            "native authored material remains completely unchanged");
        Check(MapUvAnimation.TryGetGroupFrameCount(
            imported.Where(MapUvAnimation.IsAnimated).ToArray(), out int nativeCycle)
                && nativeCycle <= MapUvAnimation.MaxLoopFrames,
            "imported plus authored UV cycles remain within native frame budget");
        var duplicate = Q3ShaderNativePlan.Build(Array.Empty<MapMaterial>(),
            new[] { "textures/test/panel", "textures/test/panel" }, result);
        Check(duplicate.Count == 2 && duplicate[0].Animation != null
            && duplicate[1].Animation == null,
            "duplicate source shader material name does not generate ambiguous tracks");
        Console.WriteLine("Q3 SHADER STAGES PASS " + checks + " checks");
        return 0;
    }
}
