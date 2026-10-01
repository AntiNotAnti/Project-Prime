#if !ANDROID && !MPHREAD_SERVER
using System;
using System.IO;
using System.Text.Json;
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
            var requestedBackend = GraphicsBackendPolicy.Requested;
            bool explicitRequest = GraphicsBackendPolicy.Configured;
            var settings = DesktopGlContext.Settings(background: true);
            settings.ClientSize = new(960, 540);
            using var window = new NativeWindow(settings);
            using var graphics = new DesktopGraphicsSession(window);
            int width = window.FramebufferSize.X, height = window.FramebufferSize.Y;
            bool modern = ModernGraphicsCompat.Active;
            var identity = modern ? ModernGraphicsCompat.DeviceIdentity
                : (GraphicsBackend.OpenGL, GraphicsApi.GetString(StringName.Renderer), GraphicsApi.GetString(StringName.Version));
            int initialGeneration = modern ? ModernGraphicsCompat.DeviceGeneration : 0;
            var captures = new List<object>();
            int frames = 0;
            RenderOptions.ApplyGraphicsPreset(GraphicsPreset.Original);
            RenderOptions.ShowFps = false;
            var scene = new Scene(new(width, height), SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(), _ => { }, () => { });
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
                            byte[] pixels = FinalCompositeCapture.Read(width, height);
                            using var file = File.Create(Path.Combine(directory, test.Name + ".png"));
                            StbImage.FlipVerticallyOnSave = true;
                            StbImage.WritePng<byte>(pixels, width, height, StbiImageFormat.Rgb, file);
                        }
                        frames++;
                        DesktopGraphicsSession.Present(window);
                        scene.AfterRenderFrame();
                    }
                    captures.Add(new { image = test.Name + ".png", preset = test.Name == "extreme" ? "Extreme" : "Original",
                        configuration = test.Name, width, height,
                        renderScale = RenderOptions.ResolutionScale / 100.0,
                        scope = "Scene final backbuffer after world/postprocess/HUD/visor/fade; excludes shell UI, launcher hunter and Shell.AfterDraw" });
                    Console.WriteLine("RENDERPARITY captured " + test.Name);
                }
            }
            finally { scene.DoCleanup(); scene.UnloadGl(); }
            File.WriteAllText(Path.Combine(directory, "evidence.json"), JsonSerializer.Serialize(new
            {
                format = 1, requestedBackend = requestedBackend.ToString(), explicitRequest,
                actualBackend = identity.Item1.ToString(), adapter = identity.Item2, driver = identity.Item3,
                platform = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                room, frames, width, height,
                deviceLost = (int?)null,
                deviceReconstructionCount = modern ? ModernGraphicsCompat.DeviceGeneration - initialGeneration : 0,
                notes = "Device-loss callbacks are not counted by this harness. Captures are scene-level evidence, not full application composite acceptance. pbr-albedo/normal/material.png are intermediate G-buffer diagnostics.",
                captures
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("RENDERPARITY PASS " + directory);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("RENDERPARITY FAIL " + ex); return 1; }
    }
}
#endif
