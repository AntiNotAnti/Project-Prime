using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using MphRead.Mods.Input;

namespace MphRead.Mods.Launcher.Settings;

/// <summary>
/// Portable backup of every canonical player-settings store. The archive is
/// intentionally made from the same serializers as normal persistence so it
/// remains inspectable and does not create a second settings schema.
/// </summary>
internal static class SettingsArchive
{
    internal const int FormatVersion = 1;
    internal const string SuggestedFileName = "ProjectPrime-settings.zip";
    private const int ManifestLimit = 64 * 1024;
    private const int SettingsLimit = 2 * 1024 * 1024;
    private const int ControlsLimit = 2 * 1024 * 1024;
    private const int LauncherLimit = 256 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions ManifestJson = new(
        JsonSerializerDefaults.Web) { WriteIndented = true };

    internal sealed record Content(string SettingsJson, string ControlsText,
        string LauncherText, MenuSettings MenuSettings);

    internal static void Write(Stream destination, MenuSettings menuSettings)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(menuSettings);
        if (!destination.CanWrite)
        {
            throw new ArgumentException("The settings export destination is not writable.",
                nameof(destination));
        }

        using var archive = new ZipArchive(destination, ZipArchiveMode.Create,
            leaveOpen: true);
        WriteText(archive, "manifest.json", JsonSerializer.Serialize(new
        {
            formatVersion = FormatVersion,
            product = Branding.Name,
            files = new[]
            {
                SettingRegistry.MenuSettingsFile,
                SettingRegistry.ControlsFile,
                SettingRegistry.LauncherFile
            }
        }, ManifestJson));
        WriteText(archive, SettingRegistry.MenuSettingsFile,
            ClientSettings.SerializeSettings(menuSettings));
        WriteLines(archive, SettingRegistry.ControlsFile,
            InputSettings.GetSaveLines());
        WriteLines(archive, SettingRegistry.LauncherFile,
            LauncherPrefs.GetSaveLines());
    }

    internal static Content Read(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
        {
            throw new ArgumentException("The settings archive source is not readable.",
                nameof(source));
        }

        using var archive = new ZipArchive(source, ZipArchiveMode.Read,
            leaveOpen: true);
        string[] allowed =
        {
            "manifest.json",
            SettingRegistry.MenuSettingsFile,
            SettingRegistry.ControlsFile,
            SettingRegistry.LauncherFile
        };
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (!allowed.Contains(entry.FullName, StringComparer.Ordinal)
                || !entries.TryAdd(entry.FullName, entry))
            {
                throw new InvalidDataException(
                    $"Unexpected or duplicate settings archive entry '{entry.FullName}'.");
            }
        }
        if (entries.Count != allowed.Length)
        {
            throw new InvalidDataException(
                "The settings archive is missing one or more required files.");
        }

        string manifestJson = ReadText(entries["manifest.json"], ManifestLimit);
        ValidateManifest(manifestJson);
        string settingsJson = ReadText(entries[SettingRegistry.MenuSettingsFile],
            SettingsLimit);
        MenuSettings settings = ClientSettings.DeserializeSettings(settingsJson);
        string controls = ReadText(entries[SettingRegistry.ControlsFile], ControlsLimit);
        string launcher = ReadText(entries[SettingRegistry.LauncherFile], LauncherLimit);
        ValidateLineFile(controls, SettingRegistry.ControlsFile);
        ValidateLineFile(launcher, SettingRegistry.LauncherFile);
        return new Content(settingsJson, controls, launcher, settings);
    }

    internal static void Install(Content content)
    {
        ArgumentNullException.ThrowIfNull(content);
        Install(content, ClientSettings.SettingsPath,
            Path.Combine(LauncherPrefs.Directory, SettingRegistry.ControlsFile),
            Path.Combine(LauncherPrefs.Directory, SettingRegistry.LauncherFile));
    }

    internal static void Install(Content content, string settingsPath,
        string controlsPath, string launcherPath)
    {
        ArgumentNullException.ThrowIfNull(content);
        string[] paths = new[] { settingsPath, controlsPath, launcherPath }
            .Select(Path.GetFullPath).ToArray();
        if (paths.Distinct(StringComparer.Ordinal).Count() != paths.Length)
        {
            throw new InvalidOperationException(
                "Settings import destinations must be distinct files.");
        }
        string[] values =
        {
            content.SettingsJson,
            content.ControlsText,
            content.LauncherText
        };
        byte[]?[] originals = paths.Select(path => File.Exists(path)
            ? File.ReadAllBytes(path) : null).ToArray();
        string[] temporary = paths.Select(path => path
            + $".import-{Guid.NewGuid():N}.tmp").ToArray();
        int installed = 0;
        try
        {
            for (int i = 0; i < paths.Length; i++)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(paths[i])!);
                File.WriteAllText(temporary[i], values[i], new UTF8Encoding(false));
            }
            for (; installed < paths.Length; installed++)
            {
                File.Move(temporary[installed], paths[installed], overwrite: true);
            }
        }
        catch (Exception installError)
        {
            Exception? rollbackError = null;
            for (int i = installed - 1; i >= 0; i--)
            {
                try
                {
                    if (originals[i] == null)
                    {
                        File.Delete(paths[i]);
                    }
                    else
                    {
                        string restore = paths[i]
                            + $".restore-{Guid.NewGuid():N}.tmp";
                        File.WriteAllBytes(restore, originals[i]!);
                        File.Move(restore, paths[i], overwrite: true);
                    }
                }
                catch (Exception ex)
                {
                    rollbackError ??= ex;
                }
            }
            throw rollbackError == null
                ? new IOException("Settings import failed; existing settings were restored.",
                    installError)
                : new AggregateException(
                    "Settings import failed and could not completely restore the previous files.",
                    installError, rollbackError);
        }
        finally
        {
            foreach (string path in temporary)
            {
                try { File.Delete(path); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static void ValidateManifest(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("formatVersion", out JsonElement version)
            || version.GetInt32() != FormatVersion)
        {
            throw new InvalidDataException(
                "This settings archive version is not supported.");
        }
        string[] expected =
        {
            SettingRegistry.MenuSettingsFile,
            SettingRegistry.ControlsFile,
            SettingRegistry.LauncherFile
        };
        if (!root.TryGetProperty("files", out JsonElement files)
            || files.ValueKind != JsonValueKind.Array
            || !files.EnumerateArray().Select(value => value.GetString())
                .SequenceEqual(expected))
        {
            throw new InvalidDataException(
                "The settings archive manifest does not match its required files.");
        }
    }

    private static void ValidateLineFile(string text, string name)
    {
        if (text.IndexOf('\0') >= 0)
        {
            throw new InvalidDataException($"{name} contains invalid text data.");
        }
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            int split = line.IndexOf('=');
            if (split <= 0 || split > 128)
            {
                throw new InvalidDataException($"{name} contains an invalid setting line.");
            }
        }
    }

    private static string ReadText(ZipArchiveEntry entry, int limit)
    {
        if (entry.Length < 0 || entry.Length > limit)
        {
            throw new InvalidDataException(
                $"Settings archive entry '{entry.FullName}' is too large.");
        }
        using Stream source = entry.Open();
        using var buffer = new MemoryStream((int)entry.Length);
        source.CopyTo(buffer);
        if (buffer.Length > limit)
        {
            throw new InvalidDataException(
                $"Settings archive entry '{entry.FullName}' is too large.");
        }
        try
        {
            return StrictUtf8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidDataException(
                $"Settings archive entry '{entry.FullName}' is not valid UTF-8.", ex);
        }
    }

    private static void WriteLines(ZipArchive archive, string name,
        System.Collections.Generic.IEnumerable<string> lines)
        => WriteText(archive, name, String.Join('\n', lines) + "\n");

    private static void WriteText(ZipArchive archive, string name, string text)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name,
            CompressionLevel.Optimal);
        using Stream stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(text);
    }
}
