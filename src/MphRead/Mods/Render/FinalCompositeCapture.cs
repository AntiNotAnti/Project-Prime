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
        return CaptureReadback.Read(new CaptureSource(0, width, height, "final-output"));
    }

    internal static void WriteEvidence(string imagePath, int width, int height, string scope)
    {
#if !MPHREAD_SERVER
        var identity = (GraphicsBackend.OpenGL,
            GL.GetString(StringName.Renderer), GL.GetString(StringName.Version));
        File.WriteAllText(imagePath + ".evidence.json", JsonSerializer.Serialize(new
        {
            format = 1, contract = nameof(FinalCompositeCapture), scope,
            requestedBackend = GraphicsBackendPolicy.Requested.ToString(),
            actualBackend = identity.Item1.ToString(), adapter = identity.Item2, driver = identity.Item3,
            platform = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            width, height, renderScale = RenderOptions.ResolutionScale / 100.0,
            deviceLost = (int?)null, deviceGeneration = 0,
            resources = (object?)null,
            note = "Device loss and lifetime stability are not inferred from a successful capture."
        }, new JsonSerializerOptions { WriteIndented = true }));
#endif
    }
}
