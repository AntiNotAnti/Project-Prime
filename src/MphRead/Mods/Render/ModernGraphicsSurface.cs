#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Runtime.InteropServices;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Silk.NET.WebGPU;
using Silk.NET.WebGPU.Platforms.MacOS;

namespace MphRead.Mods.Render
{
    /// <summary>
    /// Creates a WebGPU presentation surface from the GLFW window Project Prime
    /// already owns. The window must have been created with ContextAPI.NoAPI
    /// when this is used for gameplay.
    /// </summary>
    internal static unsafe class ModernGraphicsSurface
    {
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
                    // Same arrangement used by Silk's windowing helper: attach
                    // a CAMetalLayer to GLFW's existing NSWindow content view.
                    CAMetalLayer metalLayer = CAMetalLayer.New();
                    NSWindow nsWindow = new(GLFW.GetCocoaWindow(window.WindowPtr));
                    var contentView = nsWindow.contentView;
                    contentView.wantsLayer = true;
                    contentView.layer = metalLayer.NativePtr;

                    var native = new SurfaceDescriptorFromMetalLayer
                    {
                        Chain = new ChainedStruct
                        {
                            Next = null,
                            SType = SType.SurfaceDescriptorFromMetalLayer
                        },
                        Layer = (void*)metalLayer.NativePtr
                    };
                    descriptor.NextInChain = (ChainedStruct*)&native;
                    return Require(api.InstanceCreateSurface(instance, descriptor), platform);
                }

            default:
                throw new PlatformNotSupportedException(
                    $"WebGPU presentation is not wired for GLFW platform {platform}.");
            }
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
    }
}
#endif
