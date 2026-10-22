using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace MphRead.Mods.MapGen;

/// <summary>
/// A bounded, game-data-free .pk3 to portable .ppmap conversion. Intended to
/// run in an isolated process, not inside the multiplayer/map HTTP listener.
/// No publishing credentials or package server access are needed here.
/// </summary>
public static class Q3WebConversion
{
    public sealed record Result(
        Guid MapId, string RoomName, string SourceLevel, string PackageHash,
        string ContentHash, long PackageBytes, string[] Warnings);

    public static Result Convert(string source, string outputPath, string roomName,
        string? mapName = null, CancellationToken cancellation = default,
        Action<string>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(source) ||
            !Path.GetExtension(source).Equals(".pk3", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Only Quake .pk3 source archives are accepted.");
        MapValidator.RequireRuntimeName(roomName);
        if (string.IsNullOrWhiteSpace(outputPath)
            || !Path.GetExtension(outputPath).Equals(".ppmap", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Output must be a .ppmap file.");
        string fullSource = Path.GetFullPath(source);
        string fullOutput = Path.GetFullPath(outputPath);
        if (Path.GetFullPath(fullSource).Equals(Path.GetFullPath(fullOutput),
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Source and output must not be the same file.");

        cancellation.ThrowIfCancellationRequested();
        // Verify archive entry paths, expansion bounds, symlinks, duplicate
        // entries and non-BSP map names before invoking the expensive converter.
        var levels = Pk3DiscoveryService.Discover(fullSource);
        if (levels.Count == 0)
            throw new InvalidDataException("Archive contains no playable maps/*.bsp levels.");
        if (levels.Count > 64)
            throw new InvalidDataException("Too many BSP levels in a single web conversion.");
        if (mapName != null && !levels.Any(level =>
            level.Name.Equals(mapName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Requested BSP map is absent.");
        string selected = mapName ?? levels[0].Name;

        // The project is generated under an ephemeral private directory. Never
        // use source-supplied archive names to construct filesystem paths.
        string scratch = Path.Combine(Path.GetDirectoryName(fullOutput)
            ?? throw new InvalidDataException("Output directory is missing."),
            ".q3-import-" + Guid.NewGuid().ToString("N"));
        try
        {
            cancellation.ThrowIfCancellationRequested();
            progress?.Invoke("Analyzing and translating BSP geometry");
            var options = new Q3ImportService.Options(
                Source: fullSource, MapName: selected,
                RoomName: roomName, DestinationDirectory: scratch,
                // Do not attach external base-game packs or host-provided data.
                Dependencies: Array.Empty<string>(),
                AllowFallbackTextures: true);
            var imported = Q3ImportService.Import(options, cancellation, progress);
            if (!imported.Succeeded || imported.ProjectPath == null)
                throw new InvalidDataException("Quake import failed: " +
                    string.Join("; ", imported.Diagnostics
                        .Where(d => d.Severity == Q3ImportService.Severity.Error)
                        .Select(d => d.Message).Take(3)));

            cancellation.ThrowIfCancellationRequested();
            var definition = MapDefinition.Load(imported.ProjectPath);
            if (definition.RequiresPrivateSource)
                throw new InvalidDataException("Local-only source cannot be published.");
            progress?.Invoke("Building portable .ppmap package");
            MapPackageBuilder.Build(definition, fullOutput);
            cancellation.ThrowIfCancellationRequested();
            using var check = new MapPackageReader(fullOutput);
            if (check.Manifest == null || check.Manifest.MapId != definition.MapId)
                throw new InvalidDataException("Portable package identity validation failed.");
            var warnings = imported.Diagnostics
                .Where(d => d.Severity == Q3ImportService.Severity.Warning)
                .Select(d => d.Message).Distinct(StringComparer.Ordinal)
                .Take(64).ToArray();
            return new Result(definition.MapId, definition.Name, selected,
                MapBuildFingerprint.HashFile(fullOutput),
                check.Manifest.ContentHash, new FileInfo(fullOutput).Length,
                warnings);
        }
        catch
        {
            if (File.Exists(fullOutput))
                File.Delete(fullOutput);
            throw;
        }
        finally
        {
            if (Directory.Exists(scratch)) Directory.Delete(scratch, true);
        }
    }
}
