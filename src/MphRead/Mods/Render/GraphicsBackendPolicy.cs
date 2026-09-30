using System;
using System.Collections.Generic;

namespace MphRead.Mods.Render
{
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
    /// One source of truth for renderer/backend availability. This is kept
    /// independent of the native API so it can be regression-tested on any
    /// runner without pretending that the runner is another operating system.
    /// </summary>
    public static class GraphicsBackendPolicy
    {
        private static readonly GraphicsBackend[] _windows = { GraphicsBackend.DirectX12, GraphicsBackend.Vulkan };
        private static readonly GraphicsBackend[] _macOS = { GraphicsBackend.Metal, GraphicsBackend.Vulkan };
        private static readonly GraphicsBackend[] _linux = { GraphicsBackend.Vulkan };
        private static readonly GraphicsBackend[] _android = { GraphicsBackend.Vulkan };
        private static readonly GraphicsBackend[] _none = Array.Empty<GraphicsBackend>();

        public static GraphicsBackend Requested { get; private set; } = GraphicsBackend.Auto;

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

        public static GraphicsBackend Resolved => Resolve(CurrentPlatform, Requested);

        public static void Configure(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                Requested = GraphicsBackend.Auto;
                return;
            }
            if (!TryParse(value, out GraphicsBackend backend))
            {
                throw new ArgumentException(
                    $"Unknown renderer '{value}'. Expected auto, opengl, dx12, vulkan, or metal.");
            }
            if (backend != GraphicsBackend.Auto && !IsSupported(CurrentPlatform, backend))
            {
                throw new PlatformNotSupportedException(
                    $"{DisplayName(backend)} is not a supported Project Prime renderer on {CurrentPlatform}. "
                    + $"Available: {Describe(CurrentPlatform)}.");
            }
            Requested = backend;
        }

        public static GraphicsBackend Resolve(GraphicsPlatform platform, GraphicsBackend requested)
        {
            if (requested == GraphicsBackend.Auto)
            {
                return DefaultFor(platform);
            }
            if (!IsSupported(platform, requested))
            {
                throw new PlatformNotSupportedException(
                    $"{DisplayName(requested)} is not supported on {platform}. Available: {Describe(platform)}.");
            }
            return requested;
        }

        public static GraphicsBackend DefaultFor(GraphicsPlatform platform)
        {
            return platform switch
            {
                GraphicsPlatform.Windows => GraphicsBackend.DirectX12,
                GraphicsPlatform.MacOS => GraphicsBackend.Metal,
                GraphicsPlatform.Linux => GraphicsBackend.Vulkan,
                GraphicsPlatform.Android => GraphicsBackend.Vulkan,
                _ => GraphicsBackend.OpenGL
            };
        }

        public static IReadOnlyList<GraphicsBackend> ModernBackendsFor(GraphicsPlatform platform)
        {
            return platform switch
            {
                GraphicsPlatform.Windows => _windows,
                GraphicsPlatform.MacOS => _macOS,
                GraphicsPlatform.Linux => _linux,
                GraphicsPlatform.Android => _android,
                _ => _none
            };
        }

        public static bool IsSupported(GraphicsPlatform platform, GraphicsBackend backend)
        {
            if (backend == GraphicsBackend.Auto || backend == GraphicsBackend.OpenGL)
            {
                return true;
            }
            foreach (GraphicsBackend candidate in ModernBackendsFor(platform))
            {
                if (candidate == backend) return true;
            }
            return false;
        }

        public static bool IsModern(GraphicsBackend backend)
        {
            return backend == GraphicsBackend.DirectX12
                || backend == GraphicsBackend.Vulkan
                || backend == GraphicsBackend.Metal;
        }

        public static bool TryParse(string value, out GraphicsBackend backend)
        {
            switch (value.Trim().ToLowerInvariant())
            {
                case "auto":
                case "default":
                    backend = GraphicsBackend.Auto;
                    return true;
                case "gl":
                case "opengl":
                case "legacy":
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

        public static string DisplayName(GraphicsBackend backend)
        {
            return backend switch
            {
                GraphicsBackend.Auto => "Auto",
                GraphicsBackend.OpenGL => CurrentPlatform == GraphicsPlatform.Android ? "OpenGL ES" : "OpenGL",
                GraphicsBackend.DirectX12 => "DirectX 12",
                GraphicsBackend.Vulkan when CurrentPlatform == GraphicsPlatform.MacOS => "Vulkan (MoltenVK)",
                GraphicsBackend.Vulkan => "Vulkan",
                GraphicsBackend.Metal => "Metal",
                _ => backend.ToString()
            };
        }

        public static string Describe(GraphicsPlatform platform)
        {
            IReadOnlyList<GraphicsBackend> modern = ModernBackendsFor(platform);
            if (modern.Count == 0) return "OpenGL";
            string[] names = new string[modern.Count + 1];
            for (int i = 0; i < modern.Count; i++)
            {
                GraphicsBackend backend = modern[i];
                names[i] = backend == GraphicsBackend.Vulkan && platform == GraphicsPlatform.MacOS
                    ? "Vulkan (MoltenVK)"
                    : backend == GraphicsBackend.DirectX12 ? "DirectX 12" : backend.ToString();
            }
            names[^1] = platform == GraphicsPlatform.Android ? "OpenGL ES (fallback)" : "OpenGL (fallback)";
            return string.Join(", ", names);
        }
    }
}
