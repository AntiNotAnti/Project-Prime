using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace MphRead.Mods.Settings;

/// <summary>Explicit one-time local import. Supplied HUD/controls remain authoritative.
/// The old installation is read-only; credentials never enter reports or archives.</summary>
internal static class ClientMigration
{
    internal static void Run(string source, string destination)
    {
        source = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar);
        destination = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar);
        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Choose your previous client folder, not the new client folder.");
        if (!File.Exists(Path.Combine(source, "ProjectPrime.exe")) && !File.Exists(Path.Combine(source, "ProjectPrime.dll")))
            throw new IOException("The selected folder does not contain ProjectPrime.exe.");
        string session = Path.Combine(source, "hunter-license.session.json");
        if (!File.Exists(session))
            throw new IOException("The selected client has no Hunter License session. Choose the installation with your existing profile.");
        using (var identity = JsonDocument.Parse(ReadBounded(session, 1024 * 1024)))
            if (!identity.RootElement.TryGetProperty("refresh_token", out var token)
                || token.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(token.GetString()))
                throw new InvalidDataException("The existing Hunter License session is invalid.");

        Directory.CreateDirectory(destination);
        string backup = Path.Combine(destination, "migration-backups", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backup);
        var changes = new List<(string Path, string? Backup)>();
        try
        {
            foreach (string name in new[] { "launcher.txt", "controller-profiles.json", "gamecontrollerdb.txt", "hud-color-favorites.json", "paths.txt", "hunter-license.session.json" })
            {
                string from = Path.Combine(source, name);
                if (!File.Exists(from)) continue;
                byte[] bytes = ReadBounded(from, 4 * 1024 * 1024);
                if (name == "controller-profiles.json") Input.GamepadProfiles.ParseLibrary(System.Text.Encoding.UTF8.GetString(bytes));
                if (name == "paths.txt")
                {
                    var lines = System.Text.Encoding.UTF8.GetString(bytes).Split('\n');
                    for (int i = 1; i < lines.Length; i++)
                    {
                        int split = lines[i].IndexOf('=');
                        if (split < 0) continue;
                        string value = lines[i][(split + 1)..].Trim();
                        if (value.Length > 0 && !Path.IsPathRooted(value))
                            lines[i] = lines[i][..(split + 1)] + Path.GetFullPath(Path.Combine(source, value));
                    }
                    bytes = System.Text.Encoding.UTF8.GetBytes(string.Join('\n', lines));
                }
                string to = Path.Combine(destination, name), previous = Path.Combine(backup, name);
                if (File.Exists(to)) File.Copy(to, previous, overwrite: false);
                changes.Add((to, File.Exists(previous) ? previous : null));
                string temporary = to + ".migration-" + Guid.NewGuid().ToString("N");
                try
                {
                    File.WriteAllBytes(temporary, bytes);
                    if (!SHA256.HashData(File.ReadAllBytes(temporary)).SequenceEqual(SHA256.HashData(bytes)))
                        throw new IOException("Migration verification failed.");
                    File.Move(temporary, to, overwrite: true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            File.WriteAllText(Path.Combine(destination, "migration-complete.txt"),
                "Local preferences and existing Hunter License session imported. Supplied controls and HUD/settings retained. Statistics must be verified in Hunter License.\n");
            File.WriteAllText(Path.Combine(destination, "migration-status.txt"), "SUCCESS\n");
        }
        catch
        {
            foreach (var change in changes.AsEnumerable().Reverse())
                if (change.Backup != null) File.Copy(change.Backup, change.Path, overwrite: true);
                else if (File.Exists(change.Path)) File.Delete(change.Path);
            throw;
        }
    }
    private static byte[] ReadBounded(string path, long maximum)
    {
        if (new FileInfo(path).Length > maximum) throw new InvalidDataException("Migration input exceeds the supported size.");
        return File.ReadAllBytes(path);
    }
}
