using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using MphRead;
using OpenTK.Graphics.OpenGL;

sealed partial class CheckWindow
{
    // Explicit -shots readback only. The shadow composite has no world depth;
    // sample the original scene target and restore the read binding afterwards.
    private object CaptureWorldDepth(int index, bool repeat = false)
    {
        int framebuffer = (int)typeof(Scene).GetField("_frameBuffer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Scene)!;
        int width = Scene.RenderSize.X, height = Scene.RenderSize.Y;
        float[] depth = new float[checked(width * height)];
        int previous = GL.GetInteger(GetPName.ReadFramebufferBinding);
        try
        {
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, framebuffer);
            GL.ReadPixels(0, 0, width, height, PixelFormat.DepthComponent, PixelType.Float, depth);
        }
        finally { GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, previous); }
        if (depth.Any(d => !float.IsFinite(d) || d < 0 || d > 1) || !depth.Any(d => d < 1))
            throw new InvalidDataException("World depth witness is invalid or has no covered geometry.");
        byte[] bytes = MemoryMarshal.AsBytes(depth.AsSpan()).ToArray();
        string path = Path.Combine(_shots!, $"frame-{index}-world-depth{(repeat ? "-repeat" : "")}-float32.bin");
        if (File.Exists(path)) throw new IOException("Refusing world depth evidence overwrite: " + path);
        File.WriteAllBytes(path, bytes);
        if (repeat && !File.ReadAllBytes(Path.Combine(_shots!, $"frame-{index}-world-depth-float32.bin")).SequenceEqual(bytes))
            throw new InvalidDataException("Repeated world draw changed exact depth.");
        return new { width, height, format = "native-endian-float32", byteLength = bytes.Length,
            sha256 = Convert.ToHexString(SHA256.HashData(bytes)), minimum = depth.Min(), maximum = depth.Max(),
            coveredPixels = depth.Count(d => d < 1) };
    }
}
