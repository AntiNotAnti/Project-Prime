#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using Silk.NET.WebGPU;

namespace MphRead.Mods.Render;

internal sealed unsafe partial class ModernGraphicsCompat
{
    private sealed record StudioSurfaceState(uint Width,uint Height,Silk.NET.WebGPU.TextureFormat Format,
        CompositeAlphaMode AlphaMode,PresentMode Mode,PresentMode[] Modes,bool Configured,bool NeedsRecreation);
    private readonly Dictionary<nint,StudioSurfaceState> _studioSurfaceStates=new();
    internal static void BeginStudioSurface(ModernGraphicsDevice device, Surface* surface, int width, int height)
    {
        if (_current == null)
        {
            device.BorrowStudioSurface(surface);
            _deviceRecoveryAttempts = 0; _deviceRecoveryFailure = null; DeviceGeneration++;
            _current = new ModernGraphicsCompat(device,width,height,ownsDevice:false);
            return;
        }
        var current = _current;
        if (current._ownsDevice || !ReferenceEquals(current._device,device))
            throw new InvalidOperationException("A different graphics host already owns the compatibility renderer.");
        if (device.Surface != surface)
        {
            current.FlushCommands(); current.ReleaseSurfaceTexture();
            if(device.Surface!=null)current._studioSurfaceStates[(nint)device.Surface]=new(current._width,current._height,
                current._surfaceFormat,current._alphaMode,current._presentMode,System.Linq.Enumerable.ToArray(current._presentModes),
                current._surfaceConfigured,current._surfaceNeedsRecreation);
            if(current._width!=(uint)width || current._height!=(uint)height)current.ReleaseSurfaceDepth();
            device.BorrowStudioSurface(surface);
            if(current._studioSurfaceStates.TryGetValue((nint)surface,out var state))
            {
                current._surfaceFormat=state.Format;current._alphaMode=state.AlphaMode;
                current._presentModes.Clear();foreach(var mode in state.Modes)current._presentModes.Add(mode);
                current.SelectPresentMode();
                current._surfaceConfigured=state.Configured && state.Width==(uint)width && state.Height==(uint)height && state.Mode==current._presentMode;
                current._surfaceNeedsRecreation=state.NeedsRecreation;
            }
            else
            {
                current._surfaceConfigured=current._surfaceNeedsRecreation=false;current.QuerySurfaceFormat();
            }
            current._width=(uint)width;current._height=(uint)height;
            current._viewportX=current._viewportY=current._scissorX=current._scissorY=0;
            current._viewportWidth=current._scissorWidth=width;current._viewportHeight=current._scissorHeight=height;
            // Configuring a WSI surface drains pending device work. Four views
            // retain their own configuration instead of draining on every switch.
            if(!current._surfaceConfigured && !current._surfaceNeedsRecreation)current.ConfigureSurface();
            return;
        }
        current.ResizeCore(width,height);
    }
    internal static void DetachStudioSurface(ModernGraphicsDevice device, Surface* surface)
    {
        var current = _current;
        if (current == null || current._ownsDevice || !ReferenceEquals(current._device,device)) return;
        current._studioSurfaceStates.Remove((nint)surface);
        if(device.Surface!=surface)return;
        current.DiscardCommands(); current.ReleaseSurfaceTexture();
        current._surfaceConfigured = false;
        device.ClearBorrowedStudioSurface();
    }
    internal static void ReplaceStudioDevice(ModernGraphicsDevice device)
    {
        var previous = _current;
        if (previous == null || previous._ownsDevice) return;
        try
        {
            _deviceRecoveryFailure = null; _deviceRecoveryAttempts = 0;
            _current = new ModernGraphicsCompat(device,(int)previous._width,(int)previous._height,previous,ownsDevice:false);
            DeviceGeneration++;
        }
        finally { previous.Dispose(); }
    }
    internal static void ShutdownStudio()
    {
        if (_current is { _ownsDevice: false }) Shutdown();
    }
}
#endif
