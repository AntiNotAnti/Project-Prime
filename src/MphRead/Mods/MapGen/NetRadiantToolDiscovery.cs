using System;
using System.Collections.Generic;
using System.IO;

namespace MphRead.Mods.MapGen;

public enum NetRadiantPlatform { Windows, Linux, MacOS, Unsupported }
public sealed record NetRadiantTools(string? EditorExecutable, string? CompilerExecutable,
    string? ResourceDirectory, string? EditorError, string? CompilerError);

/// <summary>Only operator overrides and fixed locations beside this installation are trusted.
/// Never searches PATH, the current directory, map sources, or downloaded content.</summary>
public static class NetRadiantToolDiscovery
{
    public static NetRadiantPlatform CurrentPlatform => OperatingSystem.IsAndroid() ? NetRadiantPlatform.Unsupported
        : OperatingSystem.IsWindows() ? NetRadiantPlatform.Windows
        : OperatingSystem.IsMacOS() ? NetRadiantPlatform.MacOS
        : OperatingSystem.IsLinux() ? NetRadiantPlatform.Linux : NetRadiantPlatform.Unsupported;

    public static NetRadiantTools Resolve(string installationDirectory, NetRadiantPlatform platform,
        string? editorOverride = null, string? compilerOverride = null)
    {
        if (platform == NetRadiantPlatform.Unsupported) return new(null, null, null, null, null);
        string installation = Path.GetFullPath(installationDirectory);
        var candidates = new List<string>();
        if (platform == NetRadiantPlatform.MacOS)
        {
            var macos = new DirectoryInfo(installation);
            if (macos.Name == "MacOS" && macos.Parent?.Name == "Contents"
                && macos.Parent.Parent is { } app
                && app.Name is "Project Prime Studio.app" or "Project Prime.app")
                candidates.Add(Path.Combine(app.Parent!.FullName, "Project Prime Radiant.app", "Contents", "MacOS"));
        }
        else
        {
            candidates.Add(Path.Combine(installation, "Project Prime Radiant"));
            var root = new DirectoryInfo(installation);
            if (root.Name is "Project Prime Studio" or "Project Prime" && root.Parent != null)
                candidates.Add(Path.Combine(root.Parent.FullName, "Project Prime Radiant"));
        }
        string suffix = platform == NetRadiantPlatform.Windows ? ".exe" : "";
        string? Locate(string name)
        {
            foreach (string candidate in candidates)
            {
                string path = Path.Combine(candidate, name + suffix);
                // A linked product directory must not redirect discovery to user content.
                string product = platform == NetRadiantPlatform.MacOS
                    ? Path.GetFullPath(Path.Combine(candidate, "..", "..")) : candidate;
                if (RegularDirectory(product) && RegularDirectory(candidate)
                    && (platform != NetRadiantPlatform.MacOS || RegularDirectory(Path.Combine(product, "Contents")))
                    && RegularFile(path)) return path;
            }
            return null;
        }
        string? ResolveTool(string? value, string name, out string? error)
        {
            error = null;
            if (value == null) return Locate(name);
            if (!string.IsNullOrWhiteSpace(value) && Path.IsPathFullyQualified(value))
            {
                try
                {
                    string path = Path.GetFullPath(value);
                    if (RegularFile(path)) return path;
                }
                catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { }
            }
            error = "The configured " + name + " override must be an existing absolute executable file, not a symlink. Packaged discovery is disabled until the override is corrected or removed.";
            return null;
        }
        string? editor = ResolveTool(editorOverride, "radiant", out string? editorError);
        string? compiler = ResolveTool(compilerOverride, "q3map2", out string? compilerError);
        return new(editor, compiler, editor == null ? null : ResourceDirectoryForExecutable(editor), editorError, compilerError);
    }

    public static string ResourceDirectoryForExecutable(string executable)
    {
        var binaryDirectory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(executable))!);
        return binaryDirectory.Name == "MacOS" && binaryDirectory.Parent?.Name == "Contents"
            && binaryDirectory.Parent.Parent?.Name.EndsWith(".app", StringComparison.Ordinal) == true
            ? Path.Combine(binaryDirectory.Parent.FullName, "Resources", "netradiant") : binaryDirectory.FullName;
    }

    internal static bool RegularFile(string path) => SafeAttributes(path, false);
    internal static bool RegularDirectory(string path) => SafeAttributes(path, true);
    private static bool SafeAttributes(string path, bool directory)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            return !attributes.HasFlag(FileAttributes.ReparsePoint)
                && attributes.HasFlag(FileAttributes.Directory) == directory;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { return false; }
    }
}
