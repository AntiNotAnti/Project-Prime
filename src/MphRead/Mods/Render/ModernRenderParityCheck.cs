#if !ANDROID && !MPHREAD_SERVER
using System;
using System.IO;
using System.Collections.Generic;
using MphRead.Mods.Input;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Desktop;
using ReFuel.Stb;

namespace MphRead.Mods.Render;

internal static class ModernRenderParityCheck
{
    internal static int Run(string room, string output)
    {
        try
        {
            string directory = Path.GetFullPath(output);
            Directory.CreateDirectory(directory);
            var settings = DesktopGlContext.Settings(background: true);
            settings.ClientSize = new(960, 540);
            using var window = new NativeWindow(settings);
            using var graphics = new DesktopGraphicsSession(window);
            RenderOptions.ApplyGraphicsPreset(GraphicsPreset.Original);
            RenderOptions.ShowFps = false;
            var scene = new Scene(new(960, 540), SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(), _ => { }, () => { });
            try
            {
                scene.AddPlayer(Hunter.Samus);
                scene.AddRoom(room, GameMode.Battle, playerCount: 1);
                scene.OnLoad();
                GameState.MatchTime = -1;
                for (int i = 0; i < 180; i++) scene.OnSimulationFrame();
                var cases = new List<(string Name, Action Configure)>
                {
                    ("world-hud", () => { }),
                    ("cel", () => RenderOptions.CelShading = true),
                    ("filtering", () => { RenderOptions.TextureFiltering = true; RenderOptions.TextureMipmaps = true; RenderOptions.TextureAnisotropy = 16; }),
                    ("shadow-low", () => RenderOptions.Shadows = ShadowQuality.Low),
                    ("shadow-high", () => RenderOptions.Shadows = ShadowQuality.High),
                    ("shadow-ultra", () => RenderOptions.Shadows = ShadowQuality.Ultra),
                    ("pbr", () => { RenderOptions.AdvancedMaterials = true; RenderOptions.DeferredPbr = true; }),
                    ("fxaa", () => RenderOptions.AntiAliasing = AntiAliasingMode.Fxaa),
                    ("fxaa-high", () => RenderOptions.AntiAliasing = AntiAliasingMode.FxaaHigh),
                    ("smaa", () => RenderOptions.AntiAliasing = AntiAliasingMode.Smaa),
                    ("taa", () => RenderOptions.AntiAliasing = AntiAliasingMode.Taa),
                    ("bloom", () => RenderOptions.Bloom = true),
                    ("sharpen", () => RenderOptions.SharpenStrength = 80),
                    ("ssao", () => RenderOptions.AmbientOcclusion = AmbientOcclusionQuality.High),
                    ("contact-shadows", () => RenderOptions.ContactShadows = true),
                    ("reflections", () => RenderOptions.Reflections = true),
                    ("fog", () => RenderOptions.EnhancedFog = true),
                    ("volumetric-fog", () => { RenderOptions.EnhancedFog = true; RenderOptions.VolumetricFog = true; }),
                    ("dynamic-glow", () => RenderOptions.DynamicGlow = true),
                    ("hdr", () => RenderOptions.InternalHdr = true),
                    ("extreme", () => RenderOptions.ApplyGraphicsPreset(GraphicsPreset.Extreme))
                };
                byte[] pixels = new byte[960 * 540 * 3];
                foreach (var test in cases)
                {
                    RenderOptions.ApplyGraphicsPreset(GraphicsPreset.Original);
                    RenderOptions.CelShading = false;
                    test.Configure();
                    scene.OnResize();
                    for (int frame = 0; frame < 12; frame++)
                    {
                        NativeWindow.ProcessWindowEvents(false);
                        DesktopGraphicsSession.Resize(window);
                        scene.OnDrawFrame();
                        if (!scene.OnRenderFrame()) throw new InvalidOperationException("Parity scene stopped.");
                        if (frame == 11)
                        {
                            if (test.Name == "pbr") scene.CapturePbrBuffers(directory);
                            GraphicsApi.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
                            GraphicsApi.ReadPixels(0, 0, 960, 540, PixelFormat.Rgb, PixelType.UnsignedByte, pixels);
                            using var file = File.Create(Path.Combine(directory, test.Name + ".png"));
                            StbImage.FlipVerticallyOnSave = true;
                            StbImage.WritePng<byte>(pixels, 960, 540, StbiImageFormat.Rgb, file);
                        }
                        DesktopGraphicsSession.Present(window);
                        scene.AfterRenderFrame();
                    }
                    Console.WriteLine("RENDERPARITY captured " + test.Name);
                }
            }
            finally { scene.DoCleanup(); scene.UnloadGl(); }
            Console.WriteLine("RENDERPARITY PASS " + directory);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("RENDERPARITY FAIL " + ex); return 1; }
    }
}
#endif
