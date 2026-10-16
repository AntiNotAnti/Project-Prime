#if ANDROID && PROJECTPRIME_ANDROID_SHADER_BINARY_CACHE
using System;
using System.IO;
using System.Runtime.InteropServices;
using MphRead.Droid;
using OpenTK.Graphics.OpenGL;

namespace MphRead.Mods.Render;

/// <summary>
/// Android-only disk cache for ES3 linked programs. Every GL call stays on the
/// current EGL owner thread; a failed cache read always compiles from source.
/// </summary>
internal static class AndroidShaderProgramBinaryCache
{
    private const string GLES = "libGLESv3.so";
    private const uint NumBinaryFormats = 0x87FE;
    private const uint ProgramBinaryLength = 0x8741;
    private const uint ProgramBinaryRetrievableHint = 0x8257;
    private const uint Vendor = 0x1F00, Renderer = 0x1F01, Version = 0x1F02, GlslVersion = 0x8B8C;
    private const int FileHeader = 8;
    private const int Magic = 0x50475042; // PGPB

    [DllImport(GLES, EntryPoint = "glGetIntegerv")] private static extern void GetInteger(uint name, out int value);
    [DllImport(GLES, EntryPoint = "glGetProgramiv")] private static extern void GetProgramInt(int program, uint name, out int value);
    [DllImport(GLES, EntryPoint = "glGetProgramBinary")] private static extern void GetBinary(int program, int size, out int length, out int format, [Out] byte[] data);
    [DllImport(GLES, EntryPoint = "glProgramBinary")] private static extern void LoadBinary(int program, int format, byte[] data, int length);
    [DllImport(GLES, EntryPoint = "glProgramParameteri")] private static extern void ProgramParameter(int program, uint name, int value);
    [DllImport(GLES, EntryPoint = "glGetString")] private static extern IntPtr GetString(uint name);

    private static bool Supported()
    {
        GetInteger(NumBinaryFormats, out int count);
        return count > 0;
    }

    private static string Driver()
    {
        string Part(uint name) => Marshal.PtrToStringAnsi(GetString(name)) ?? "";
        return Part(Vendor) + "|" + Part(Renderer) + "|" + Part(Version) + "|" + Part(GlslVersion);
    }

    private static string? PathFor(string vertex, string fragment)
    {
        string? root = MainActivity.Instance?.FilesDir?.AbsolutePath;
        if (string.IsNullOrEmpty(root)) return null;
        return Path.Combine(root, "shader-programs",
            ShaderProgramCacheKey.Compute(vertex, fragment, Driver()) + ".bin");
    }

    internal static void EnableRetrieval(int program)
    {
        try { if (Supported()) ProgramParameter(program, ProgramBinaryRetrievableHint, 1); }
        catch (Exception) { /* optional vendor capability */ }
    }

    internal static bool TryLoad(string vertex, string fragment, out int program)
    {
        program = 0;
        try
        {
            if (!Supported()) return false;
            string? path = PathFor(vertex, fragment);
            if (path == null || !File.Exists(path)) return false;
            FileInfo stat = new(path);
            if (stat.Length <= FileHeader || stat.Length > ShaderProgramCacheKey.MaxBinaryBytes + FileHeader)
                return false;
            byte[] saved = File.ReadAllBytes(path);
            if (saved.Length < FileHeader || BitConverter.ToInt32(saved, 0) != Magic) return false;
            int format = BitConverter.ToInt32(saved, 4);
            int length = saved.Length - FileHeader;
            if (!ShaderProgramCacheKey.AcceptBinaryLength(length)) return false;
            var bytes = new byte[length];
            Buffer.BlockCopy(saved, FileHeader, bytes, 0, length);
            int candidate = GraphicsApi.CreateProgram();
            try
            {
                LoadBinary(candidate, format, bytes, length);
                GraphicsApi.GetProgram(candidate, GetProgramParameterName.LinkStatus, out int linked);
                if (linked == 0) return false;
                program = candidate;
                candidate = 0;
                return true;
            }
            finally
            {
                if (candidate != 0) GraphicsApi.DeleteProgram(candidate);
            }
        }
        catch (Exception)
        {
            // Unsupported extension, corrupt cache, or unmounted storage: compile normally.
            return false;
        }
    }

    internal static void Save(string vertex, string fragment, int program)
    {
        string? temp = null;
        try
        {
            if (!Supported()) return;
            string? path = PathFor(vertex, fragment);
            if (path == null) return;
            GetProgramInt(program, ProgramBinaryLength, out int capacity);
            if (!ShaderProgramCacheKey.AcceptBinaryLength(capacity)) return;
            byte[] bytes = new byte[capacity];
            GetBinary(program, capacity, out int length, out int format, bytes);
            if (!ShaderProgramCacheKey.AcceptBinaryLength(length) || length > capacity) return;
            byte[] serialized = new byte[FileHeader + length];
            BitConverter.GetBytes(Magic).CopyTo(serialized, 0);
            BitConverter.GetBytes(format).CopyTo(serialized, 4);
            Buffer.BlockCopy(bytes, 0, serialized, FileHeader, length);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temp, serialized);
            File.Move(temp, path, overwrite: true);
            temp = null;
        }
        catch (Exception) { /* optional cache must never break gameplay */ }
        finally
        {
            if (temp != null) try { File.Delete(temp); } catch (Exception) { }
        }
    }
}
#endif
