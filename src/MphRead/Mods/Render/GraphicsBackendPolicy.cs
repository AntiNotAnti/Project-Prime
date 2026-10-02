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
        public static bool Configured { get; private set; }

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

        public static GraphicsBackend[] RendererChoices()
        {
            var modern = ModernBackendsFor(CurrentPlatform);
            var choices = new GraphicsBackend[modern.Count + 2];
            choices[0] = GraphicsBackend.Auto;
            for (int i = 0; i < modern.Count; i++) choices[i + 1] = modern[i];
            choices[^1] = GraphicsBackend.OpenGL;
            return choices;
        }

        private static bool _preferenceRead;
        private static bool _preferenceDriven;
        private static string? _startupFallbackReason;
        private static string StartupGuardPath => System.IO.Path.Combine("Savedata", "renderer-startup.pending");

        internal static bool StartupFallbackActive => _startupFallbackReason != null;
        internal static string? StartupFallbackReason => _startupFallbackReason;

        public static void LoadPreference()
        {
            if (Configured || _preferenceRead) return;
            _preferenceRead = true;
            try
            {
                string path = System.IO.Path.Combine("Savedata", "settings.json");
                if (!System.IO.File.Exists(path)) return;
                using var document = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(path));
                if (document.RootElement.TryGetProperty("MenuSettings", out var menu)
                    && menu.TryGetProperty("Renderer", out var renderer)
                    && renderer.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    Configure(renderer.GetString());
                    _preferenceDriven = true;
                    ApplyPendingStartupGuard();
                }
            }
            catch (Exception ex) when (ex is System.IO.IOException or System.Text.Json.JsonException
                or ArgumentException or PlatformNotSupportedException)
            {
                Console.Error.WriteLine($"[render] Ignoring renderer preference: {ex.Message}");
                Configure("opengl");
            }
        }

        public static void UseCompatibilityFallback(string reason)
        {
            Console.Error.WriteLine($"[render] {DisplayName(Resolved)} failed; using OpenGL compatibility: {reason}");
            _startupFallbackReason = reason;
            Requested = GraphicsBackend.OpenGL;
            Configured = true;
        }

        /// <summary>
        /// Arm before the first native modern-renderer window/device call. If the
        /// process dies in a driver/native frame, the file survives and the next
        /// preference-driven launch uses OpenGL instead of crash-looping.
        /// Explicit diagnostic/command-line renderer selections never arm it.
        /// </summary>
        internal static void BeginStartupAttempt(GraphicsBackend backend)
        {
            if (!_preferenceDriven || !IsModern(backend)) return;
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(StartupGuardPath)!);
                System.IO.File.WriteAllText(StartupGuardPath, backend.ToString());
                Mods.DebugLog.Checkpoint("render", $"armed {DisplayName(backend)} startup recovery");
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine("[render] Could not arm renderer startup recovery: " + ex.Message);
            }
        }

        internal static void CompleteStartupAttempt(GraphicsBackend backend)
        {
            if (!_preferenceDriven || !IsModern(backend)) return;
            try
            {
                if (System.IO.File.Exists(StartupGuardPath)) System.IO.File.Delete(StartupGuardPath);
                Mods.DebugLog.Checkpoint("render", $"{DisplayName(backend)} startup recovery cleared");
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine("[render] Could not clear renderer startup recovery: " + ex.Message);
            }
        }

        internal static void ClearStartupGuardForRendererChange()
        {
            _startupFallbackReason = null;
            try
            {
                if (System.IO.File.Exists(StartupGuardPath)) System.IO.File.Delete(StartupGuardPath);
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine("[render] Could not clear previous renderer failure: " + ex.Message);
            }
        }

        private static void ApplyPendingStartupGuard()
        {
            if (!IsModern(Resolved) || !System.IO.File.Exists(StartupGuardPath)) return;
            try
            {
                string value = System.IO.File.ReadAllText(StartupGuardPath).Trim();
                if (!TryParse(value, out GraphicsBackend failed) || !IsModern(failed))
                {
                    UseCompatibilityFallback("the previous modern renderer startup did not complete");
                    return;
                }
                if (failed == Resolved)
                {
                    UseCompatibilityFallback(
                        $"the previous {DisplayName(failed)} startup did not complete; choose it again in Settings to retry");
                    return;
                }
                // The user changed renderer after the failure. The old fence no
                // longer applies to this selection.
                System.IO.File.Delete(StartupGuardPath);
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                UseCompatibilityFallback("renderer startup recovery could not be read: " + ex.Message);
            }
        }

        internal static bool StartupGuardMatches(string value, GraphicsBackend backend)
            => TryParse(value, out GraphicsBackend failed) && IsModern(failed) && failed == backend;

        public static GraphicsBackend Resolved => Resolve(CurrentPlatform, Requested);

        /// <summary>
        /// True only when the user/diagnostic explicitly selected a modern backend.
        /// Until the compatibility renderer reaches full scene parity, an omitted
        /// renderer option deliberately keeps the proven OpenGL path.
        /// </summary>
        public static bool ModernGameplayRequested => Configured && IsModern(Resolved);

        public static void Configure(string? value)
        {
            // An explicit selection (command line/diagnostic) is independent
            // from a crash fence created by the persisted launcher preference.
            _preferenceDriven = false;
            _startupFallbackReason = null;
            Configured = true;
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
