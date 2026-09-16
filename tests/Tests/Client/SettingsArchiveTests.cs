using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Settings;
using Xunit;

namespace MphRead.Tests.Client;

public sealed class SettingsArchiveTests
{
    [Fact]
    public void ArchiveContainsEveryCanonicalSettingsStore()
    {
        var settings = new MenuSettings { FieldOfView = "91" };
        string expectedControls = String.Join('\n', InputSettings.GetSaveLines()) + "\n";
        string expectedLauncher = String.Join('\n', LauncherPrefs.GetSaveLines()) + "\n";
        using var stream = new MemoryStream();

        SettingsArchive.Write(stream, settings);

        using (var importStream = new MemoryStream(stream.ToArray()))
        {
            SettingsArchive.Content imported = SettingsArchive.Read(importStream);
            Assert.Equal("91", imported.MenuSettings.FieldOfView);
            Assert.Equal(expectedControls, imported.ControlsText);
            Assert.Equal(expectedLauncher, imported.LauncherText);
        }

        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        Assert.Equal(new[]
        {
            SettingRegistry.ControlsFile,
            SettingRegistry.LauncherFile,
            "manifest.json",
            SettingRegistry.MenuSettingsFile
        }, archive.Entries.Select(entry => entry.FullName)
            .Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(expectedControls, Read(archive, SettingRegistry.ControlsFile));
        Assert.Equal(expectedLauncher, Read(archive, SettingRegistry.LauncherFile));

        using JsonDocument settingsDocument = JsonDocument.Parse(
            Read(archive, SettingRegistry.MenuSettingsFile));
        Assert.Equal("91", settingsDocument.RootElement
            .GetProperty("MenuSettings").GetProperty("FieldOfView").GetString());

        using JsonDocument manifest = JsonDocument.Parse(Read(archive, "manifest.json"));
        Assert.Equal(SettingsArchive.FormatVersion,
            manifest.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.Equal(new[]
        {
            SettingRegistry.MenuSettingsFile,
            SettingRegistry.ControlsFile,
            SettingRegistry.LauncherFile
        }, manifest.RootElement.GetProperty("files").EnumerateArray()
            .Select(value => value.GetString()).ToArray());
    }

    [Fact]
    public void ArchiveRejectsUnwritableDestination()
    {
        using var stream = new MemoryStream(Array.Empty<byte>(), writable: false);
        Assert.Throws<ArgumentException>(() =>
            SettingsArchive.Write(stream, new MenuSettings()));
    }

    [Fact]
    public void ArchiveRejectsUnexpectedEntries()
    {
        using var stream = new MemoryStream();
        SettingsArchive.Write(stream, new MenuSettings());
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update,
            leaveOpen: true))
        {
            archive.CreateEntry("not-settings.txt");
        }
        stream.Position = 0;
        Assert.Throws<InvalidDataException>(() => SettingsArchive.Read(stream));
    }

    [Fact]
    public void InstallReplacesAllThreeStoresTogether()
    {
        using var archiveStream = new MemoryStream();
        SettingsArchive.Write(archiveStream,
            new MenuSettings { FieldOfView = "103" });
        archiveStream.Position = 0;
        SettingsArchive.Content content = SettingsArchive.Read(archiveStream);
        string directory = Path.Combine(Path.GetTempPath(),
            $"prime-settings-import-{Guid.NewGuid():N}");
        string settingsPath = Path.Combine(directory, "Savedata", "settings.json");
        string controlsPath = Path.Combine(directory, "controls.txt");
        string launcherPath = Path.Combine(directory, "launcher.txt");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            File.WriteAllText(settingsPath, "old-settings");
            File.WriteAllText(controlsPath, "old-controls");
            File.WriteAllText(launcherPath, "old-launcher");

            SettingsArchive.Install(content, settingsPath, controlsPath,
                launcherPath);

            Assert.Equal(content.SettingsJson, File.ReadAllText(settingsPath));
            Assert.Equal(content.ControlsText, File.ReadAllText(controlsPath));
            Assert.Equal(content.LauncherText, File.ReadAllText(launcherPath));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void InstallRestoresEarlierFileWhenLaterReplacementFails()
    {
        using var archiveStream = new MemoryStream();
        SettingsArchive.Write(archiveStream, new MenuSettings());
        archiveStream.Position = 0;
        SettingsArchive.Content content = SettingsArchive.Read(archiveStream);
        string directory = Path.Combine(Path.GetTempPath(),
            $"prime-settings-rollback-{Guid.NewGuid():N}");
        string settingsPath = Path.Combine(directory, "settings.json");
        string controlsPath = Path.Combine(directory, "controls.txt");
        string launcherPath = Path.Combine(directory, "launcher.txt");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(settingsPath, "old-settings");
            // A directory at the second destination lets staging succeed but
            // forces the second replacement to fail after settings.json moved.
            Directory.CreateDirectory(controlsPath);
            File.WriteAllText(launcherPath, "old-launcher");

            Assert.Throws<IOException>(() => SettingsArchive.Install(content,
                settingsPath, controlsPath, launcherPath));

            Assert.Equal("old-settings", File.ReadAllText(settingsPath));
            Assert.Equal("old-launcher", File.ReadAllText(launcherPath));
            Assert.True(Directory.Exists(controlsPath));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static string Read(ZipArchive archive, string name)
    {
        ZipArchiveEntry entry = Assert.Single(archive.Entries,
            candidate => candidate.FullName == name);
        using StreamReader reader = new(entry.Open());
        return reader.ReadToEnd();
    }
}
