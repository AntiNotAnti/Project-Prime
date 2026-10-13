using System.Collections;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead;
using OpenTK.Graphics.OpenGL;

// Read actual uploaded texture bytes/samplers by logical source, rather than
// comparing allocation-local GL handles. Restore all touched binding state.
internal static class ReplayGpuReadbackDiagnostics
{
    internal static void Save(Scene scene, string label, Acceptance.Report report)
    {
        int active = GL.GetInteger(GetPName.ActiveTexture);
        GL.ActiveTexture(TextureUnit.Texture0);
        int binding = GL.GetInteger(GetPName.TextureBinding2D);
        var textures = new SortedDictionary<string, object?>();
        try
        {
            if (Reflection.Get(scene, "_textureSources") is not IDictionary sources)
                throw new InvalidOperationException("Native GPU texture source inventory unavailable.");
            foreach (DictionaryEntry source in sources)
            {
                int id = Convert.ToInt32(source.Key);
                object value = source.Value!;
                object model = Reflection.Get(value, "Item1")!;
                string key = Reflection.Get(model, "Name") + "/tex=" + Reflection.Get(value, "Item2")
                    + "/pal=" + Reflection.Get(value, "Item3") + "/recolor=" + Reflection.Get(value, "Item4");
                GL.BindTexture(TextureTarget.Texture2D, id);
                int Parameter(GetTextureParameter parameter)
                { GL.GetTexParameter(TextureTarget.Texture2D, parameter, out int result); return result; }
                var levels = new List<object>();
                for (int level = 0; level < 12; level++)
                {
                    GL.GetTexLevelParameter(TextureTarget.Texture2D, level, GetTextureParameter.TextureWidth, out int width);
                    GL.GetTexLevelParameter(TextureTarget.Texture2D, level, GetTextureParameter.TextureHeight, out int height);
                    if (width == 0 || height == 0) break;
                    if ((long)width * height > 4 * 1024 * 1024)
                        throw new InvalidOperationException("Native texture readback exceeds diagnostic pixel bound.");
                    var bytes = new byte[checked(width * height * 4)];
                    GL.GetTexImage(TextureTarget.Texture2D, level, PixelFormat.Rgba, PixelType.UnsignedByte, bytes);
                    levels.Add(new { level, width, height, sha256 = Convert.ToHexString(SHA256.HashData(bytes)) });
                }
                textures[key] = new { binding = id,
                    min = Parameter(GetTextureParameter.TextureMinFilter), mag = Parameter(GetTextureParameter.TextureMagFilter),
                    wrapS = Parameter(GetTextureParameter.TextureWrapS), wrapT = Parameter(GetTextureParameter.TextureWrapT), levels };
            }
        }
        finally
        {
            GL.BindTexture(TextureTarget.Texture2D, binding);
            GL.ActiveTexture((TextureUnit)active);
        }
        if (GL.GetError() != ErrorCode.NoError)
            throw new InvalidOperationException("Native diagnostic GPU texture readback reported GL error.");
        string path = Path.Combine(report.Output, label + "-gpu-textures.json");
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(new { label, textures,
            dither = GL.IsEnabled(EnableCap.Dither), multisample = GL.IsEnabled(EnableCap.Multisample),
            program = GL.GetInteger(GetPName.CurrentProgram), framebuffer = GL.GetInteger(GetPName.FramebufferBinding) },
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllBytes(path, json);
        report.Stages.Add(new { stage = "native-private-replay-gpu-textures", label, path,
            sources = textures.Count, sha256 = Convert.ToHexString(SHA256.HashData(json)) });
    }
}
