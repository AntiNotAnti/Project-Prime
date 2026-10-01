using System;
using System.IO;
using System.Text.Json;
using OpenTK.Graphics.OpenGL;

namespace MphRead.Mods.Render;

/// <summary>
/// Read the completed application backbuffer on its render owner, after all scene,
/// HUD, tone-map and shell passes, before Present. Never substitutes a scene target.
/// Callers capturing before shell drawing must label that narrower scope explicitly.
/// RGB rows are bottom-up, matching the platform PNG writer contract.
/// </summary>
internal static class FinalCompositeCapture
{
    internal static byte[] Read(int width, int height)
    {
        if (width <= 0 || height <= 0 || width > 8192 || height > 8192
            || (long)width * height * 3 > 64 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(width), "Composite readback exceeds its 64 MiB budget.");
        int framebuffer = GL.GetInteger(GetPName.ReadFramebufferBinding);
        int alignment = GL.GetInteger(GetPName.PackAlignment);
#if !MPHREAD_SERVER
        bool modern = ModernGraphicsCompat.Active;
#else
        bool modern = false;
#endif
        int readBuffer = modern ? 0 : GL.GetInteger(GetPName.ReadBuffer);
        try
        {
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
            GL.ReadBuffer(ReadBufferMode.Back);
            GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
            byte[] pixels = new byte[checked(width * height * 3)];
            GL.ReadPixels(0, 0, width, height, PixelFormat.Rgb, PixelType.UnsignedByte, pixels);
            return pixels;
        }
        finally
        {
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, framebuffer);
            if (!modern) GL.ReadBuffer((ReadBufferMode)readBuffer);
            GL.PixelStore(PixelStoreParameter.PackAlignment, alignment);
        }
    }

    internal static void WriteEvidence(string imagePath, int width, int height, string scope)
    {
#if !MPHREAD_SERVER
        bool modern = ModernGraphicsCompat.Active;
        var identity = modern ? ModernGraphicsCompat.DeviceIdentity
            : (GraphicsBackend.OpenGL, GL.GetString(StringName.Renderer), GL.GetString(StringName.Version));
        File.WriteAllText(imagePath + ".evidence.json", JsonSerializer.Serialize(new
        {
            format = 1, contract = nameof(FinalCompositeCapture), scope,
            requestedBackend = GraphicsBackendPolicy.Requested.ToString(),
            actualBackend = identity.Item1.ToString(), adapter = identity.Item2, driver = identity.Item3,
            platform = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            width, height, renderScale = RenderOptions.ResolutionScale / 100.0,
            deviceLost = (int?)null, deviceGeneration = modern ? ModernGraphicsCompat.DeviceGeneration : 0,
            resources = modern ? (object)ModernGraphicsCompat.LiveResources : null,
            note = "Device loss and lifetime stability are not inferred from a successful capture."
        }, new JsonSerializerOptions { WriteIndented = true }));
#endif
    }
}
