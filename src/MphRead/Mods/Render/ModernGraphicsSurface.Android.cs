#if ANDROID && !MPHREAD_SERVER
using System;
using Silk.NET.WebGPU;
namespace MphRead.Mods.Render
{
    internal static unsafe class ModernGraphicsAndroidSurface
    {
        // The render-loop owner holds an ANativeWindow reference for exactly
        // this surface lifetime and releases it only after detaching WebGPU.
        internal static Surface* Create(WebGPU api, Instance* instance, nint window)
        {
            if (window == 0) throw new ArgumentException("ANativeWindow must be valid.", nameof(window));
            var android = new SurfaceDescriptorFromAndroidNativeWindow
            {
                Chain = new ChainedStruct { SType = SType.SurfaceDescriptorFromAndroidNativeWindow },
                Window = (void*)window
            };
            var descriptor = new SurfaceDescriptor { NextInChain = (ChainedStruct*)&android };
            var surface = api.InstanceCreateSurface(instance, &descriptor);
            if (surface == null) throw new InvalidOperationException("Could not create Android Vulkan surface.");
            return surface;
        }
    }
}
#endif
