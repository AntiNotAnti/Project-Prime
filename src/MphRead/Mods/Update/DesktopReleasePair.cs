using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MphRead.Mods.Update;

/// <summary>The two desktop applications are released and updated as one compatible unit.</summary>
public sealed record DesktopReleasePair(int Version, string GameVersion, string StudioVersion, int IpcVersion)
{
    public const string FileName = ".project-prime-desktop.json";
    public static DesktopReleasePair? Read(string directory)
    {
        string path = ReleaseInstallation.OwnedPath(directory, FileName);
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 4096) throw new InvalidDataException("The desktop release metadata is too large.");
        DesktopReleasePair? pair;
        try { pair = JsonSerializer.Deserialize<DesktopReleasePair>(File.ReadAllBytes(path)); }
        catch (JsonException ex) { throw new InvalidDataException("Invalid desktop release metadata.", ex); }
        if (pair is not { Version: 1, IpcVersion: > 0 } || !ValidVersion(pair.GameVersion)
            || !ValidVersion(pair.StudioVersion) || !String.Equals(pair.GameVersion, pair.StudioVersion, StringComparison.Ordinal))
            throw new InvalidDataException("Project Prime and Studio must have matching desktop release versions.");
        return pair;
    }

    internal static void ValidateUpdate(string source, string target, ReleaseInstallation.Manifest next,
        ReleaseInstallation.Manifest? previous)
    {
        bool incomingStudio = next.Files.Any(IsStudio);
        bool installedStudio = previous?.Files.Any(IsStudio) == true
            || File.Exists(Path.Combine(target, "ProjectPrimeStudio"))
            || File.Exists(Path.Combine(target, "ProjectPrimeStudio.exe"));
        var pair = Read(source);
        if (incomingStudio && (pair == null || !next.Files.Contains(FileName, StringComparer.Ordinal)))
            throw new InvalidDataException("The paired desktop release is missing its compatibility metadata.");
        if (pair != null && (!incomingStudio || !next.Files.Any(IsGame)))
            throw new InvalidDataException("The paired desktop release must include both application executables.");
        if (installedStudio && !incomingStudio)
            throw new InvalidDataException("The selected release does not contain Project Prime Studio and cannot replace this paired installation. For a legacy downgrade, extract that release into a separate folder instead.");
    }

    private static bool IsStudio(string path) => Path.GetFileName(path) is "ProjectPrimeStudio" or "ProjectPrimeStudio.exe";
    private static bool IsGame(string path) => Path.GetFileName(path) is "ProjectPrime" or "ProjectPrime.exe";
    private static bool ValidVersion(string? value) => !String.IsNullOrWhiteSpace(value) && value.Length <= 128
        && value.All(c => Char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '+' or '_');
}
