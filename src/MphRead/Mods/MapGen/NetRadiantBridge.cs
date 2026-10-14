using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.MapGen;

/// <summary>
/// Optional external GPL editor bridge. Neither NetRadiant nor q3map2 is
/// redistributed, linked or installed by Project Prime. A caller opts into
/// configured, separately obtained executables and retained editable sources.
/// </summary>
public static class NetRadiantBridge
{
    private const int MaximumOutputChars = 256 * 1024;

    public static Process LaunchEditor(string executable, string mapSource)
    {
        if (OperatingSystem.IsAndroid())
            throw new PlatformNotSupportedException(
                "NetRadiant authoring runs in external desktop Project Prime Studio.");
        string binary = ValidateTool(executable);
        string source = ValidateSource(mapSource);
        var args = new ProcessStartInfo(binary)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(source)!
        };
        // Never use a shell, string concatenation or editor-provided commands.
        args.ArgumentList.Add(source);
        return Process.Start(args)
            ?? throw new IOException("NetRadiant did not start.");
    }

    public sealed record CompileResult(
        bool Succeeded, string? ProjectPath, string[] Diagnostics,
        string SourceSha256, string? BspSha256);

    /// <summary>
    /// Externally compiles standard Quake .map to BSP in private staging, then
    /// delegates transactional conversion to the *existing* Q3 import service.
    /// The original .map remains the editable master; .ppmap remains the exact
    /// gameplay package after normal Build/validation.
    /// </summary>
    public static async Task<CompileResult> CompileImportAsync(
        string q3map2Executable, string mapSource,
        string roomName, string destination, CancellationToken cancellation)
    {
        if (OperatingSystem.IsAndroid())
            throw new PlatformNotSupportedException("External q3map2 is desktop only.");
        string tool = ValidateTool(q3map2Executable);
        string source = ValidateSource(mapSource);
        MapValidator.RequireRuntimeName(roomName);

        string digest = MapBuildFingerprint.HashFile(source);
        string parent = Path.GetDirectoryName(Path.GetFullPath(destination))
            ?? throw new IOException("Map destination requires a parent directory.");
        Directory.CreateDirectory(parent);
        string temp = Path.Combine(parent,
            ".radiant-compile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            string stagedSource = Path.Combine(temp, Path.GetFileName(source));
            File.Copy(source, stagedSource);
            string expectedBsp = Path.ChangeExtension(stagedSource, ".bsp");

            var start = new ProcessStartInfo(tool)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                WorkingDirectory = temp,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("-game");
            start.ArgumentList.Add("quake3");
            start.ArgumentList.Add("-meta");
            start.ArgumentList.Add("-fs_basepath");
            start.ArgumentList.Add(temp);
            start.ArgumentList.Add(stagedSource);

            using var process = Process.Start(start)
                ?? throw new IOException("q3map2 did not start.");
            Task<string> stdout = DrainAsync(process.StandardOutput, cancellation);
            Task<string> stderr = DrainAsync(process.StandardError, cancellation);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(TimeSpan.FromMinutes(5));
            try { await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                throw;
            }
            string output = await stdout.ConfigureAwait(false);
            string errors = await stderr.ConfigureAwait(false);
            if (process.ExitCode != 0)
                return new CompileResult(false, null,
                    new[] { $"q3map2 exit code {process.ExitCode}", output, errors },
                    digest, null);
            if (!File.Exists(expectedBsp))
                return new CompileResult(false, null,
                    new[] { "q3map2 did not produce the expected BSP.", output, errors },
                    digest, null);
            if (MapBuildFingerprint.HashFile(source) != digest)
                throw new IOException("NetRadiant source changed during compilation.");

            string bspHash = MapBuildFingerprint.HashFile(expectedBsp);
            Q3ImportService.Result result = Q3ImportService.Import(
                new Q3ImportService.Options(expectedBsp, null,
                    roomName, destination,
                    UnitsPerUnit: 16, AllowFallbackTextures: true),
                cancellation);
            return new CompileResult(result.Succeeded, result.ProjectPath,
                result.Diagnostics.Select(d => $"{d.Severity}: {d.Message}")
                    .Prepend(output).Prepend(errors).ToArray(),
                digest, bspHash);
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); }
            catch (IOException) { }
        }
    }

    private static async Task<string> DrainAsync(
        StreamReader reader, CancellationToken cancellation)
    {
        var result = new System.Text.StringBuilder();
        char[] chars = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(chars.AsMemory(), cancellation)
            .ConfigureAwait(false)) > 0)
        {
            int remaining = MaximumOutputChars - result.Length;
            if (remaining > 0) result.Append(chars, 0, Math.Min(count, remaining));
            // Continue draining discarded text to avoid blocking q3map2.
        }
        return result.ToString();
    }

    private static string ValidateTool(string executable)
    {
        if (!Path.IsPathFullyQualified(executable))
            throw new ArgumentException("Configure a fully qualified external executable path.");
        string path = Path.GetFullPath(executable);
        if (!File.Exists(path)
            || File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            throw new FileNotFoundException("External editor tool is missing or a symlink.", path);
        return path;
    }

    private static string ValidateSource(string mapSource)
    {
        string path = Path.GetFullPath(mapSource);
        if (Path.GetExtension(path).ToLowerInvariant() != ".map"
            || !File.Exists(path)
            || File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)
            || new FileInfo(path).Length is <= 0 or > 32L * 1024 * 1024)
            throw new InvalidDataException("Expected a regular Quake .map source within 32 MiB.");
        return path;
    }
}
