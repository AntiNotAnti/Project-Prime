using System;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.Mods.Render
{
    internal static class DesktopGlContext
    {
        private static readonly GLFWCallbacks.ErrorCallback _errorCallback = (code, description) =>
        {
            // Never throw across GLFW's native callback frames. In particular,
            // a NoAPI window can report NoWindowContext or platform errors that
            // must not bypass renderer fallback by terminating the process.
            try
            {
                Mods.DebugLog.Checkpoint("window", $"GLFW {code}: {description}");
                Console.Error.WriteLine($"[window] GLFW {code}: {description}");
            }
            catch { /* Logging failures must not unwind into GLFW either. */ }
        };

        internal static void InstallErrorCallback() => GLFW.SetErrorCallback(_errorCallback);

        public static void PreserveWorkingDirectory()
        {
            // GLFW otherwise changes a bundled Mac app to Contents/Resources,
            // separating launcher validation/settings from the extraction child.
            if (OperatingSystem.IsMacOS())
                GLFW.InitHint(InitHintBool.CocoaChdirResources, false);
        }

        // OpenTK 4.9.4 queries this handle with glfwGetVideoMode before it
        // calls glfwCreateWindow, including windowed and NoAPI windows. A null
        // primary monitor must be rejected in managed code before construction.
        private static NativeWindowSettings RequirePrimaryMonitor(NativeWindowSettings settings)
        {
            if (settings.CurrentMonitor.Pointer == IntPtr.Zero)
                throw new InvalidOperationException(
                    "GLFW did not report a primary monitor. A game window cannot be created while the display is unavailable.");
            return settings;
        }

        public static NativeWindowSettings Settings(bool background = false)
        {
            GraphicsBackendPolicy.LoadPreference();
            // Install before NativeWindowSettings initializes GLFW/monitors.
            // OpenTK's default handler throws across native frames and aborts
            // on macOS; NativeWindow checks a failed CreateWindow itself and
            // throws safely after returning to managed code.
            GLFWProvider.SetErrorCallback(_errorCallback);
            PreserveWorkingDirectory();
            if (background && OperatingSystem.IsMacOS())
                GLFW.InitHint(InitHintBool.CocoaMenubar, false);
            Mods.DebugLog.Checkpoint("render", $"initializing GLFW for {GraphicsBackendPolicy.Resolved}");
            GLFWProvider.EnsureInitialized();
            // GLFW hints survive window destruction. In particular a previous
            // NoAPI/core window must not leave ForwardCompat set on GL 2.1.
            GLFW.DefaultWindowHints();
            Mods.DebugLog.Checkpoint("render", $"creating GLFW window for {GraphicsBackendPolicy.Resolved}");

            if (GraphicsBackendPolicy.ModernGameplayRequested)
            {
                // Vulkan surfaces cannot be created for a GLFW window that
                // already owns an OpenGL client API. Metal/DX12 follow the same
                // path so one window contract covers every modern backend.
                return RequirePrimaryMonitor(new NativeWindowSettings
                {
                    ClientSize = new Vector2i(1280, 768),
                    Title = Branding.Name,
                    API = ContextAPI.NoAPI,
                    Flags = ContextFlags.Default,
                    AutoLoadBindings = false,
                    StartVisible = false
                });
            }

            return RequirePrimaryMonitor(new NativeWindowSettings
            {
                ClientSize = new Vector2i(1280, 768),
                Title = Branding.Name,
                // Legacy immediate mode/GLSL 1.20 need the 2.1 context on macOS;
                // Apple's 3.2+ contexts are core-only.
                Profile = OperatingSystem.IsMacOS() ? ContextProfile.Any : ContextProfile.Compatability,
                Flags = ContextFlags.Default,
                APIVersion = OperatingSystem.IsMacOS() ? new Version(2, 1) : new Version(3, 2),
                StartVisible = false
            });
        }
    }
}
