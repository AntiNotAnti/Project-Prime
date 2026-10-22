using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MphRead.Mods.CustomWeapons.Packages;

/// <summary>Offline CPU-only package authoring and store operations; never activates gameplay.</summary>
public static class WeaponPackageCli
{
    public static int Run(string[] args)
    {
        try
        {
            if (args.Length == 0) return Usage();
            switch (args[0].ToLowerInvariant())
            {
                case "export" when args.Length == 2:
                    Export(args[1]); Console.WriteLine("Exported editable Riftline M7 project."); break;
                case "build" when args.Length == 3:
                    string root = SafePath(args[1]);
                    var project = WeaponProject.Parse(Read(Path.Combine(root, "weapon.json"), WeaponPackageLimits.MaximumJsonBytes));
                    var resources = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                    long total = 0;
                    foreach (var resource in project.Required)
                    {
                        int cap = ResourceLimit(resource.Value);
                        byte[] bytes = Read(Path.Combine(root, resource.Key.Replace('/', Path.DirectorySeparatorChar)), cap);
                        total += bytes.Length;
                        if (total > WeaponPackageLimits.MaximumExpandedBytes) throw new InvalidDataException("Source byte budget.");
                        resources.Add(resource.Key, bytes);
                    }
                    byte[] archive = WeaponPackageBuilder.Build(project, resources);
                    WriteNew(args[2], archive); Print(WeaponPackage.Parse(archive)); break;
                case "validate" when args.Length == 2:
                    Print(WeaponPackage.Parse(Read(args[1], WeaponPackageLimits.MaximumArchiveBytes))); break;
                case "install" when args.Length == 3:
                    byte[] input = Read(args[1], WeaponPackageLimits.MaximumArchiveBytes);
                    Print(new WeaponPackageStore(SafePath(args[2])).Install(input)); break;
                case "list" when args.Length == 2:
                    foreach (var entry in new WeaponPackageStore(SafePath(args[1])).List())
                        Console.WriteLine($"{entry.PackageHash} {entry.ContentId} {SafeText(entry.Name)}");
                    break;
                case "uninstall" when args.Length == 3:
                    if (!new WeaponPackageStore(SafePath(args[2])).Uninstall(args[1]))
                    { Console.Error.WriteLine("Package was not installed."); return 1; }
                    Console.WriteLine("Uninstalled package."); break;
                case "prune" when args.Length == 2:
                    Console.WriteLine($"Pruned {new WeaponPackageStore(SafePath(args[1])).Prune()} packages."); break;
                default: return Usage();
            }
            return 0;
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException
            or InvalidOperationException or NotSupportedException or System.Security.SecurityException)
        { Console.Error.WriteLine("Weapon command failed: " + SafeText(e.Message)); return 1; }
    }

    private static int Usage()
    {
        Console.Error.WriteLine("-weapon export <new-project-dir> | build <project-dir> <new.ppweapon> | validate <file> | install <file> <store-dir> | list <store-dir> | uninstall <package-sha256> <store-dir> | prune <store-dir>");
        return 2;
    }

    private static string SafeText(string text) => new(text.Take(512).Select(c => c is >= ' ' and <= '~' ? c : '?').ToArray());
    private static void Print(WeaponPackage package)
    {
        Console.WriteLine($"{package.PackageHash} {package.Project.ContentId} {SafeText(package.Project.Name)}");
        Console.WriteLine($"content {package.ContentHash}\ndependencies {package.DependencyHash}\nmechanics {package.Definition.Mechanics.DefinitionHash}");
    }

    private static int ResourceLimit(string media) => media switch
    {
        "audio/wav" => 44 + WeaponPackageLimits.MaximumWaveFrames * 2,
        "text/plain" => WeaponPackageLimits.MaximumTextBytes,
        "application/vnd.projectprime.weapon-mechanics+json" => WeaponMechanics.MaxJsonBytes,
        "application/vnd.projectprime.rigid-boxes+json" => CustomRifleModel.MaximumBytes,
        _ => WeaponPackageLimits.MaximumJsonBytes
    };

    // Check every existing ancestor, including dangling links. Authoring directories
    // must be privately owned: managed path checks cannot fence a concurrent rename.
    private static string SafePath(string input)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length > 4096 || input.Any(char.IsControl))
            throw new IOException("Invalid disk path.");
        foreach (string part in input.Split('/', '\\'))
            if (part is not ("." or "..") && (part.EndsWith(' ') || part.EndsWith('.')))
                throw new IOException("Ambiguous disk path.");
        string full = Path.GetFullPath(input);
        if (full.StartsWith(@"\\", StringComparison.Ordinal)) throw new IOException("Network/device paths are prohibited.");
        string volume = Path.GetPathRoot(full)!;
        foreach (string part in full[volume.Length..].Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (part.Contains(':') || part.EndsWith(' ') || part.EndsWith('.')) throw new IOException("Unsafe disk path.");
            string stem = part.Split('.')[0].ToLowerInvariant();
            if (stem is "con" or "prn" or "aux" or "nul"
                || stem.Length == 4 && (stem.StartsWith("com", StringComparison.Ordinal) || stem.StartsWith("lpt", StringComparison.Ordinal))
                    && stem[3] is >= '0' and <= '9') throw new IOException("Device paths are prohibited.");
        }
        string? current = full;
        while (current != null)
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Symlink/reparse paths are prohibited.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            current = Path.GetDirectoryName(current);
        }
        return full;
    }

    private static byte[] Read(string path, int maximum)
    {
        path = SafePath(path);
        if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.Device)) != 0)
            throw new IOException("Input must be a regular file.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is <= 0 || stream.Length > maximum) throw new InvalidDataException("Input byte budget.");
        byte[] bytes = new byte[(int)stream.Length]; stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new InvalidDataException("Input changed during read.");
        return bytes;
    }

    private static void WriteNew(string destination, byte[] bytes)
    {
        destination = SafePath(destination);
        string parent = Path.GetDirectoryName(destination)!;
        if (!Directory.Exists(parent)) throw new IOException("Output parent directory must exist.");
        string temporary = Path.Combine(parent, ".weapon-" + Guid.NewGuid().ToString("N") + ".tmp");
        bool owned = false;
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { owned = true; stream.Write(bytes); stream.Flush(true); }
            SafePath(destination); File.Move(temporary, destination, overwrite: false);
        }
        finally { if (owned && File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void Export(string destination)
    {
        var package = WeaponPackage.Parse(WeaponPackageReference.BuildRiftlineM7());
        destination = SafePath(destination);
        if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("Export destination already exists.");
        string parent = Path.GetDirectoryName(destination)!;
        if (!Directory.Exists(parent)) throw new IOException("Export parent directory must exist.");
        string staging = Path.Combine(parent, ".weapon-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            foreach (string resource in package.ResourcePaths)
            {
                string output = Path.Combine(staging, resource.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                WriteNew(output, package.CopyResource(resource));
            }
            SafePath(destination); Directory.Move(staging, destination);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                // Resolve/check the exact private sibling before recursive cleanup.
                string cleanup = SafePath(staging);
                if (Path.GetDirectoryName(cleanup) != parent) throw new IOException("Unsafe staging cleanup.");
                Directory.Delete(cleanup, recursive: true);
            }
        }
    }
}
