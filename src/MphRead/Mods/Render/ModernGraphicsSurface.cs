#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Runtime.InteropServices;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Silk.NET.WebGPU;

namespace MphRead.Mods.Render
{
    /// <summary>
    /// Creates a WebGPU presentation surface from the GLFW window Project Prime
    /// already owns. The window must have been created with ContextAPI.NoAPI
    /// when this is used for gameplay.
    /// </summary>
    internal static unsafe class ModernGraphicsSurface
    {
        private static nint _quartzCore;
        internal static Surface* Create(NativeWindow window, WebGPU api, Instance* instance)
        {
            var descriptor = new SurfaceDescriptor();
            OpenTK.Windowing.GraphicsLibraryFramework.Platform platform = GLFW.GetPlatform();

            switch (platform)
            {
            case OpenTK.Windowing.GraphicsLibraryFramework.Platform.Win32:
                {
                    var native = new SurfaceDescriptorFromWindowsHWND
                    {
                        Chain = new ChainedStruct
                        {
                            Next = null,
                            SType = SType.SurfaceDescriptorFromWindowsHwnd
                        },
                        Hwnd = (void*)GLFW.GetWin32Window(window.WindowPtr),
                        Hinstance = (void*)GetModuleHandle(null)
                    };
                    descriptor.NextInChain = (ChainedStruct*)&native;
                    return Require(api.InstanceCreateSurface(instance, descriptor), platform);
                }

            case OpenTK.Windowing.GraphicsLibraryFramework.Platform.X11:
                {
                    var native = new SurfaceDescriptorFromXlibWindow
                    {
                        Chain = new ChainedStruct
                        {
                            Next = null,
                            SType = SType.SurfaceDescriptorFromXlibWindow
                        },
                        Display = (void*)GLFW.GetX11Display(),
                        Window = (uint)GLFW.GetX11Window(window.WindowPtr)
                    };
                    descriptor.NextInChain = (ChainedStruct*)&native;
                    return Require(api.InstanceCreateSurface(instance, descriptor), platform);
                }

            case OpenTK.Windowing.GraphicsLibraryFramework.Platform.Wayland:
                {
                    var native = new SurfaceDescriptorFromWaylandSurface
                    {
                        Chain = new ChainedStruct
                        {
                            Next = null,
                            SType = SType.SurfaceDescriptorFromWaylandSurface
                        },
                        Display = (void*)GLFW.GetWaylandDisplay(),
                        Surface = (void*)GLFW.GetWaylandWindow(window.WindowPtr)
                    };
                    descriptor.NextInChain = (ChainedStruct*)&native;
                    return Require(api.InstanceCreateSurface(instance, descriptor), platform);
                }

            case OpenTK.Windowing.GraphicsLibraryFramework.Platform.Cocoa:
                {
                    IntPtr layer = AttachMetalLayer(window);
                    var native = new SurfaceDescriptorFromMetalLayer
                    {
                        Chain = new ChainedStruct
                        {
                            Next = null,
                            SType = SType.SurfaceDescriptorFromMetalLayer
                        },
                        Layer = (void*)layer
                    };
                    descriptor.NextInChain = (ChainedStruct*)&native;
                    return Require(api.InstanceCreateSurface(instance, descriptor), platform);
                }

            default:
                throw new PlatformNotSupportedException(
                    $"WebGPU presentation is not wired for GLFW platform {platform}.");
            }
        }

        private static IntPtr AttachMetalLayer(NativeWindow window)
        {
            // Silk.NET's equivalent helper is internal. Reach the two Cocoa
            // properties directly instead of taking a dependency on its
            // implementation detail. GLFW exposes the existing NSView.
            if (_quartzCore == 0)
            {
                _quartzCore = NativeLibrary.Load(
                    "/System/Library/Frameworks/QuartzCore.framework/QuartzCore");
            }

            IntPtr view = GLFW.GetCocoaView(window.WindowPtr);
            if (view == IntPtr.Zero)
            {
                throw new InvalidOperationException("GLFW returned no Cocoa content view.");
            }
            IntPtr layerClass = objc_getClass("CAMetalLayer");
            if (layerClass == IntPtr.Zero)
            {
                throw new InvalidOperationException("CAMetalLayer is unavailable.");
            }
            IntPtr layer = objc_msgSend(layerClass, sel_registerName("layer"));
            if (layer == IntPtr.Zero)
            {
                throw new InvalidOperationException("Could not create CAMetalLayer.");
            }
            objc_msgSend_bool(view, sel_registerName("setWantsLayer:"), 1);
            objc_msgSend_ptr(view, sel_registerName("setLayer:"), layer);
            return layer;
        }

                private static Surface* Require(Surface* surface,
            OpenTK.Windowing.GraphicsLibraryFramework.Platform platform)
        {
            if (surface == null)
            {
                throw new InvalidOperationException(
                    $"wgpu-native could not create a presentation surface for GLFW {platform}.");
            }
            return surface;
        }

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = false)]
        private static extern IntPtr GetModuleHandle(string? moduleName);

        [DllImport("/usr/lib/libobjc.A.dylib", CharSet = CharSet.Ansi)]
        private static extern IntPtr objc_getClass(string name);

        [DllImport("/usr/lib/libobjc.A.dylib", CharSet = CharSet.Ansi)]
        private static extern IntPtr sel_registerName(string name);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_bool(IntPtr receiver, IntPtr selector, byte value);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_ptr(IntPtr receiver, IntPtr selector, IntPtr value);
    }
}
#endif
