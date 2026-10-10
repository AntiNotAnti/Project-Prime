using System;
using System.Collections.Generic;
using System.IO;

namespace MphRead.Mods.Render
{
    // The retired values stay temporarily so old diagnostics and persisted
    // preferences can be recognized while the WebGPU implementation is removed.
    // They are not supported or launchable renderers.
    public enum GraphicsBackend
    {
        Auto,
        OpenGL,
        DirectX12,
        Vulkan,
        Metal
    }

    public enum GraphicsPlatform
    {
        Windows,
        MacOS,
        Linux,
        Android,
        Other
    }

    /// <summary>
    /// OpenGL-only runtime policy. Desktop runs compatibility OpenGL; Android
    /// uses the shared renderer through OpenGL ES 3.0.
    ///
    /// Deprecated WebGPU types remain in this migration slice for compilation
    /// only. No preference, command-line value, or Auto alias can select them.
    /// </summary>
    public static class GraphicsBackendPolicy
    {
        private static readonly GraphicsBackend[] _choices = { GraphicsBackend.OpenGL };
        private static readonly GraphicsBackend[] _noModernBackends = Array.Empty<GraphicsBackend>();
        private static readonly string StartupGuardPath = Path.Combine("Savedata", "renderer-startup.pending");
        private static bool _preferenceRead;

        public static GraphicsBackend Requested { get; private set; } = GraphicsBackend.OpenGL;
        public static bool Configured { get; private set; }
        internal static bool StartupFallbackActive => false;
        internal static string? StartupFallbackReason => null;

        public static GraphicsPlatform CurrentPlatform
        {
            get
            {
                if (OperatingSystem.IsAndroid()) return GraphicsPlatform.Android;
                if (OperatingSystem.IsWindows()) return GraphicsPlatform.Windows;
                if (OperatingSystem.IsMacOS()) return GraphicsPlatform.MacOS;
                if (OperatingSystem.IsLinux()) return GraphicsPlatform.Linux;
                return GraphicsPlatform.Other;
            }
        }

        public static GraphicsBackend[] RendererChoices() => (GraphicsBackend[])_choices.Clone();

        /// <summary>
        /// Ignore persisted modern renderer selections before creating a window.
        /// SettingsMigration rewrites the stored value to OpenGL on normal load.
        /// </summary>
        public static void LoadPreference()
        {
            if (Configured || _preferenceRead) return;
            _preferenceRead = true;
            Configure("opengl");
            ClearStartupGuardForRendererChange();
        }

        public static void UseCompatibilityFallback(string reason)
        {
            Console.Error.WriteLine($"[render] OpenGL compatibility: {reason}");
            Requested = GraphicsBackend.OpenGL;
            Configured = true;
        }

        // Migration shims. These methods are referenced by the yet-to-be-deleted
        // modern renderer code. A retired backend cannot arm a startup guard.
        internal static void BeginStartupAttempt(GraphicsBackend backend) { }
        internal static void CompleteStartupAttempt(GraphicsBackend backend) { }
        internal static bool StartupGuardMatches(string value, GraphicsBackend backend) => false;

        internal static void ClearStartupGuardForRendererChange()
        {
            try
            {
                if (File.Exists(StartupGuardPath)) File.Delete(StartupGuardPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine("[render] Could not clear retired renderer guard: " + ex.Message);
            }
        }

        public static GraphicsBackend Resolved => GraphicsBackend.OpenGL;
        public static bool ModernGameplayRequested => false;

        public static void Configure(string? value)
        {
            if (!TryParse(value ?? "opengl", out GraphicsBackend backend))
                throw new ArgumentException(
                    $"Unknown renderer '{value}'. Project Prime supports only OpenGL.");

            if (!IsSupported(CurrentPlatform, backend))
                throw new PlatformNotSupportedException(
                    $"{DisplayName(backend)} has been retired. Project Prime now supports only {Describe(CurrentPlatform)}.");

            // Auto remains a compatible spelling, but never enables a modern
            // renderer. The stored and reported runtime choice is always OpenGL.
            Requested = GraphicsBackend.OpenGL;
            Configured = true;
        }

        public static GraphicsBackend Resolve(GraphicsPlatform platform, GraphicsBackend requested)
        {
            if (requested is GraphicsBackend.Auto or GraphicsBackend.OpenGL)
                return GraphicsBackend.OpenGL;
            throw new PlatformNotSupportedException(
                $"{DisplayName(requested)} has been retired. Project Prime now supports only {Describe(platform)}.");
        }

        public static GraphicsBackend DefaultFor(GraphicsPlatform platform) => GraphicsBackend.OpenGL;

        // Preserved for callers in the WebGPU code until the follow-up deletion.
        public static IReadOnlyList<GraphicsBackend> ModernBackendsFor(GraphicsPlatform platform)
            => _noModernBackends;

        public static bool IsSupported(GraphicsPlatform platform, GraphicsBackend backend)
            => backend is GraphicsBackend.Auto or GraphicsBackend.OpenGL;

        public static bool IsModern(GraphicsBackend backend)
            => backend is GraphicsBackend.DirectX12 or GraphicsBackend.Vulkan or GraphicsBackend.Metal;

        // Recognize retired spellings to produce a useful error rather than
        // silently treating a modern renderer request as OpenGL.
        public static bool TryParse(string value, out GraphicsBackend backend)
        {
            switch (value.Trim().ToLowerInvariant())
            {
                case "":
                case "auto":
                case "default":
                    backend = GraphicsBackend.Auto;
                    return true;
                case "gl":
                case "opengl":
                case "legacy":
                case "gles":
                    backend = GraphicsBackend.OpenGL;
                    return true;
                case "dx12":
                case "d3d12":
                case "directx12":
                case "direct3d12":
                    backend = GraphicsBackend.DirectX12;
                    return true;
                case "vk":
                case "vulkan":
                case "moltenvk":
                    backend = GraphicsBackend.Vulkan;
                    return true;
                case "metal":
                    backend = GraphicsBackend.Metal;
                    return true;
                default:
                    backend = GraphicsBackend.Auto;
                    return false;
            }
        }

        public static string DisplayName(GraphicsBackend backend) => backend switch
        {
            GraphicsBackend.Auto => "Auto",
            GraphicsBackend.OpenGL => CurrentPlatform == GraphicsPlatform.Android ? "OpenGL ES" : "OpenGL",
            GraphicsBackend.DirectX12 => "DirectX 12",
            GraphicsBackend.Vulkan => "Vulkan",
            GraphicsBackend.Metal => "Metal",
            _ => backend.ToString()
        };

        public static string Describe(GraphicsPlatform platform)
            => platform == GraphicsPlatform.Android ? "OpenGL ES" : "OpenGL";
    }
}
