#if ANDROID && PROJECTPRIME_ANDROID_SHADER_BINARY_CACHE
using System;
using System.IO;
using System.Collections.Generic;
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
    private static bool _pruned;

    [DllImport(GLES, EntryPoint = "glGetIntegerv")] private static extern void GetInteger(uint name, out int value);
    [DllImport(GLES, EntryPoint = "glGetProgramiv")] private static extern void GetProgramInt(int program, uint name, out int value);
    [DllImport(GLES, EntryPoint = "glGetProgramBinary")] private static extern void GetBinary(int program, int size, out int length, out int format, [Out] byte[] data);
    [DllImport(GLES, EntryPoint = "glProgramBinary")] private static extern void LoadBinary(int program, int format, byte[] data, int length);
    [DllImport(GLES, EntryPoint = "glProgramParameteri")] private static extern void ProgramParameter(int program, uint name, int value);
    [DllImport(GLES, EntryPoint = "glGetString")] private static extern IntPtr GetString(uint name);
    [DllImport(GLES, EntryPoint = "glGetError")] private static extern uint GetError();

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
            ShaderProgramCacheKey.Compute(EsShaders.PrepareSource(vertex), EsShaders.PrepareSource(fragment), Driver()) + ".bin");
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
            if (!_pruned)
            {
                Prune(Path.GetDirectoryName(path)!);
                _pruned = true;
            }
            using FileStream stream = File.OpenRead(path);
            if (stream.Length <= FileHeader || stream.Length > ShaderProgramCacheKey.MaxBinaryBytes + FileHeader)
                return false;
            byte[] saved = new byte[(int)stream.Length];
            stream.ReadExactly(saved);
            if (saved.Length < FileHeader || BitConverter.ToInt32(saved, 0) != Magic) return false;
            int format = BitConverter.ToInt32(saved, 4);
            int length = saved.Length - FileHeader;
            if (!ShaderProgramCacheKey.AcceptBinaryLength(length)) return false;
            var bytes = new byte[length];
            System.Buffer.BlockCopy(saved, FileHeader, bytes, 0, length);
            int candidate = GraphicsApi.CreateProgram();
            try
            {
                LoadBinary(candidate, format, bytes, length);
                // Invalid/corrupt binary formats set a GL error as well as
                // failing link. Consume that optional operation's error so
                // source fallback does not poison the next allocation check.
                uint binaryError = GetError();
                GraphicsApi.GetProgram(candidate, GetProgramParameterName.LinkStatus, out int linked);
                if (linked == 0 || binaryError != 0) return false;
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

    // Keep the private cache within 64 entries / 64 MiB, including old schemas.
    // Retain only a bounded list while scanning a pre-existing cache directory.
    private static void Prune(string directory)
    {
        var retained = new List<FileInfo>(65);
        long bytes = 0;
        foreach (string file in Directory.EnumerateFiles(directory))
        {
            if (!file.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)
                && !file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
            FileInfo info = new(file);
            retained.Add(info);
            bytes += info.Length;
            retained.Sort((a, b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc));
            while (retained.Count > 64 || bytes > 64L * 1024 * 1024)
            {
                FileInfo oldest = retained[0];
                long length = oldest.Length;
                oldest.Delete();
                retained.RemoveAt(0);
                bytes -= length;
            }
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
            System.Buffer.BlockCopy(bytes, 0, serialized, FileHeader, length);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temp, serialized);
            File.Move(temp, path, overwrite: true);
            temp = null;
            Prune(Path.GetDirectoryName(path)!);
        }
        catch (Exception) { /* optional cache must never break gameplay */ }
        finally
        {
            if (temp != null) try { File.Delete(temp); } catch (Exception) { }
        }
    }
}
#endif
