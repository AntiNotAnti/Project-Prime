#if !ANDROID && !MPHREAD_SERVER
using System;
using MphRead.Mods.MapEditor;
using MphRead.Mods.Render;
using Silk.NET.WebGPU;
using GpuTexture = Silk.NET.WebGPU.Texture;
using GpuTextureFormat = Silk.NET.WebGPU.TextureFormat;

namespace MphRead.Mods.StudioRendering;

/// <summary>An Avalonia-owned child handle and a borrowed device, never a game window.</summary>
public sealed unsafe class StudioNativeSurface : IDisposable
{
    internal readonly StudioRenderDevice Owner;
    private readonly nint _handle, _display;
    private readonly string _kind;
    internal Surface* Surface;
    internal GpuTextureFormat Format;
    private uint _width, _height;
    private bool _configured;
    private SurfaceTexture _acquired;
    private TextureView* _view;
    private bool _disposed;
    internal StudioNativeSurface(StudioRenderDevice owner, nint handle, string kind, nint display)
    { Owner = owner; _handle = handle; _kind = kind; _display = display; Create(); }
    private void Create()
    {
        Surface = ModernGraphicsSurface.CreateNativeHost(_handle, _kind, _display, Owner.Api, Owner.Device.Instance, Owner.Device.Backend);
        if (Surface == null) throw new InvalidOperationException("Studio native surface creation failed.");
        SurfaceCapabilities caps = default;
        Owner.Api.SurfaceGetCapabilities(Surface, Owner.Device.Adapter, &caps);
        try
        {
            if (caps.FormatCount == 0) throw new InvalidOperationException("Studio native surface has no supported presentation format.");
            Format = caps.Formats[0];
            for (nuint i = 0; i < caps.FormatCount; i++)
                if (caps.Formats[i] == GpuTextureFormat.Rgba8Unorm) { Format = GpuTextureFormat.Rgba8Unorm; break; }
            if (Format != GpuTextureFormat.Rgba8Unorm)
                for (nuint i = 0; i < caps.FormatCount; i++)
                    if (caps.Formats[i] == GpuTextureFormat.Bgra8Unorm) { Format = GpuTextureFormat.Bgra8Unorm; break; }
        }
        finally { Owner.Api.SurfaceCapabilitiesFreeMembers(caps); }
    }
    internal TextureView* Acquire(MapViewportLayout layout)
    {
        Owner.RequireOwner();
        if (_view != null) throw new InvalidOperationException("Studio surface is already acquired.");
        if (!_configured || _width != layout.PixelWidth || _height != layout.PixelHeight)
        {
            _width = (uint)layout.PixelWidth; _height = (uint)layout.PixelHeight;
            _configured = Owner.Device.ObserveNativeResult(ModernGraphicsNativeBridge.Configure(Surface, new SurfaceConfiguration {
                Device = Owner.Device.Device, Format = Format, Usage = TextureUsage.RenderAttachment,
                Width = _width, Height = _height, PresentMode = PresentMode.Fifo, AlphaMode = CompositeAlphaMode.Auto }), "Studio surface configure");
            if (!_configured) return null;
        }
        var result = ModernGraphicsNativeBridge.Acquire(Surface, out _acquired);
        if (!Owner.Device.ObserveNativeResult(result, "Studio surface acquire"))
        { _configured = false; if (result.Outcome == NativeGraphicsOutcome.SurfaceLost) Recreate(); return null; }
        _view = Owner.Api.TextureCreateView(_acquired.Texture,null); return _view;
    }
    internal void Present()
    {
        try { Owner.Device.ObserveNativeResult(ModernGraphicsNativeBridge.Present(Surface), "Studio present"); }
        finally { ReleaseFrame(); }
    }
    private void ReleaseFrame()
    {
        if (_view != null) Owner.Api.TextureViewRelease(_view);
        if (_acquired.Texture != null) Owner.Api.TextureRelease(_acquired.Texture);
        _view = null; _acquired = default;
    }
    internal void ReleaseNative()
    {
        ReleaseFrame();
        if (Surface != null) { Owner.Api.SurfaceUnconfigure(Surface); Owner.Api.SurfaceRelease(Surface); Surface = null; }
        _configured = false;
    }
    internal void Recreate() { ReleaseNative(); Create(); }
    public void Dispose()
    { if (_disposed) return; Owner.RequireOwner(); Owner.Forget(this); ReleaseNative(); _disposed = true; }
}
#endif
