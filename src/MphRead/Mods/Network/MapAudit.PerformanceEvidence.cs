using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using MphRead.Mods.Diagnostics;
using MphRead.Mods.MapGen;
using OpenTK.Graphics.OpenGL;

namespace MphRead.Mods.Network;

public sealed partial class MapAudit
{
    private sealed record PerformanceEvidence(string ApplicationImageSha256,
        string ApplicationImageKind, object Hardware, object Workload);
    private PerformanceEvidence? _performanceEvidence;

    // Capture once after source/GL initialization, before the measured loop.
    // Digests identify bytes, never publish source payloads or local paths.
    private PerformanceEvidence CapturePerformanceEvidence()
    {
        var image = RuntimeApplicationImage.Capture(typeof(MapAudit).Assembly);
        var (metadata, _) = Metadata.GetRoomByName(_room);
        if (metadata == null) throw new InvalidDataException("Cannot identify benchmark room sources.");
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        string root = metadata.FirstHunt ? Paths.FhFileSystem : Paths.FileSystem;
        void Source(string role, string? relative)
        {
            if (String.IsNullOrEmpty(relative)) return;
            string path = Paths.Combine(root, relative);
            if (File.Exists(path)) files.Add(role, MapBuildFingerprint.HashFile(path));
        }
        Source("Model", metadata.ModelPath);
        Source("Collision", metadata.CollisionPath);
        Source("Animation", metadata.AnimationPath);
        Source("Texture", metadata.TexturePath);
        Source("Entities", metadata.EntityPath);
        Source("Nodes", metadata.NodePath);
        if (!files.ContainsKey("Model") || !files.ContainsKey("Collision"))
            throw new InvalidDataException("Benchmark room model/collision source identity is unavailable.");
        var hardware = new
        {
            MachineSha256 = MapBuildFingerprint.HashText(Environment.MachineName),
            OperatingSystem = RuntimeInformation.OSDescription,
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            LogicalProcessors = Environment.ProcessorCount,
            GlVendor = GL.GetString(StringName.Vendor),
            GlRenderer = GL.GetString(StringName.Renderer),
            GlVersion = GL.GetString(StringName.Version)
        };
        var workload = new
        {
            Schema = 1,
            Route = "map-audit-v1",
            Bots = _bots,
            MainHunter = MainHunter?.ToString() ?? "Samus",
            GameRules = new
            {
                Scene.GameState.Mode, Scene.GameState.FriendlyFire,
                Scene.GameState.AffinityWeapons, Scene.GameState.EnhancedHunters,
                Scene.GameState.SpawnProtection, Scene.GameState.Fiesta,
                Scene.GameState.OneInTheChamber, Scene.GameState.InstaGib,
                Scene.GameState.LowTier, Scene.GameState.NoImperialist,
                Scene.GameState.BalancedMode, Scene.GameState.ThirdPersonCamera,
                Scene.GameState.MovementModel
            },
            SourceIdentity = new
            {
                Scope = "room-files-v1",
                GameKey = metadata.FirstHunt ? Paths.FhKey : Paths.MphKey,
                Files = files
            },
            Settings = new
            {
                RenderOptions.ResolutionScale, RenderOptions.FieldOfView,
                RenderOptions.Lighting, RenderOptions.BrightSkins, RenderOptions.BrightSkinStyle,
                RenderOptions.PlayerOutline, RenderOptions.PlayerOutlineWidth,
                RenderOptions.CelShading, RenderOptions.ShowFps, RenderOptions.SmoothNativeHud,
                RenderOptions.CelBands, RenderOptions.CelEdge, RenderOptions.Fog,
                RenderOptions.TextureFiltering, RenderOptions.TextureMipmaps, RenderOptions.TextureAnisotropy,
                RenderOptions.TextureSampling, RenderOptions.TextureUpscale, RenderOptions.Preset,
                RenderOptions.AntiAliasing, RenderOptions.SharpenStrength,
                RenderOptions.Bloom, RenderOptions.BloomIntensity, RenderOptions.ColorGrade,
                RenderOptions.Gamma, RenderOptions.Contrast, RenderOptions.Saturation,
                RenderOptions.EnhancedLighting, RenderOptions.AdvancedMaterials, RenderOptions.DeferredPbr,
                RenderOptions.Shadows, RenderOptions.AmbientOcclusion, RenderOptions.ContactShadows,
                RenderOptions.EnhancedFog, RenderOptions.VolumetricFog, RenderOptions.InternalHdr,
                RenderOptions.Reflections, RenderOptions.DynamicGlow,
                RenderOptions.TextureReplacements, RenderOptions.CharacterModelReplacements,
                RenderOptions.TextureQuality, RenderOptions.ShowCustomCosmetics, RenderOptions.CosmeticQuality
            }
        };
        return new(image.Sha256, image.Kind, hardware, workload);
    }
}
