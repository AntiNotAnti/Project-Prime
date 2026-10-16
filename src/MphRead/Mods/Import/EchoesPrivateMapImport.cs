using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Import;

/// <summary>
/// Opt-in conversion of owned Echoes media into private Project Prime
/// collision-proxy maps. The runtime catalog sees them after restart, but
/// derived proprietary content is never packaged/uploaded/transferred.
/// </summary>
public static class EchoesPrivateMapImport
{
    public const string RelativeSpiresFile = "local-echoes/spires.json";

    public static MapDefinition ReadSpires(string isoPath) =>
        ReadSelectedArenas(isoPath, new[] { "M02_Spires" }).Single();

    public static IReadOnlyList<MapDefinition> ReadAllArenas(string isoPath, bool sourceVisuals = false) =>
        ReadSelectedArenas(isoPath, EchoesArenaSources.All.Select(x => x.Key).ToArray(), sourceVisuals);

    private static IReadOnlyList<MapDefinition> ReadSelectedArenas(
        string isoPath, IReadOnlyCollection<string> keys, bool sourceVisuals = false)
    {
        using var disc = GameCubeDiscReader.Open(isoPath);
        if (disc.Game != ExternalGameKind.Echoes)
            throw new InvalidDataException("The Echoes GameCube source is required.");
        ExternalArchiveRequirement archive = ExternalSourceRequirements.For(disc.Game)
            .Single(x => x.Path == "Metroid6.pak");
        ExternalArchiveCheck verified = ExternalSourceRequirements.Verify(disc, archive);
        if (verified.Status != ExternalArchiveStatus.Verified)
            throw new InvalidDataException("This Echoes edition does not match the verified arena recipe.");

        using Stream stream = disc.OpenRead(archive.Path);
        using var pak = new RetroPakReader(stream);
        var maps = new List<MapDefinition>(keys.Count);
        foreach (EchoesArenaSources.Arena arena in EchoesArenaSources.All.Where(a => keys.Contains(a.Key)))
        {
            if (!pak.Contains(arena.ResourceId) || pak.Entry(arena.ResourceId).Type != "MREA")
                throw new InvalidDataException(arena.Name + " MREA is missing from the supplied Echoes source.");
            EchoesMreaSections area = EchoesMreaSections.Parse(pak.ReadResource(arena.ResourceId));
            MapDefinition draft = EchoesLocalMapDraft.BuildArena(
                arena, EchoesCollisionSource.Read(area), EchoesScriptSource.Read(area),
                verified.ActualSha1!);
            // Source world meshes are an independent, explicitly opt-in
            // visual channel. The collision remains the original MREA
            // physics geometry and never derives from the render model.
            if (sourceVisuals)
                draft = EchoesVisualLayer.CreatePreview(draft, EchoesWorldGeometrySource.Read(area));
            maps.Add(draft);
        }
        if (maps.Count != keys.Count)
            throw new InvalidDataException("The requested Echoes arena inventory is incomplete.");
        return maps;
    }

    public static string InstallSpires(string isoPath, string userMapDirectory) =>
        InstallLocalDefinition(ReadSpires(isoPath), userMapDirectory);

    public static IReadOnlyList<string> InstallAllArenas(string isoPath, string userMapDirectory) =>
        InstallLocalDefinitions(ReadAllArenas(isoPath), userMapDirectory);

    public static IReadOnlyList<string> InstallVisualPreviews(string isoPath, string userMapDirectory) =>
        InstallLocalDefinitions(ReadAllArenas(isoPath, sourceVisuals: true), userMapDirectory);

    public static string InstallLocalDefinition(MapDefinition definition, string userMapDirectory) =>
        InstallLocalDefinitions(new[] { definition }, userMapDirectory)[0];

    /// <summary>
    /// Validate/compile/serialize every requested map before creating or
    /// replacing ANY local recipe. Individual file commits are atomic; a
    /// filesystem outage during publication may still leave a partial set
    /// of fully valid private maps, never half-written JSON or shared assets.
    /// </summary>
    public static IReadOnlyList<string> InstallLocalDefinitions(
        IEnumerable<MapDefinition> definitions, string userMapDirectory)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentException.ThrowIfNullOrWhiteSpace(userMapDirectory);
        string root = Path.GetFullPath(userMapDirectory);
        string folder = Path.Combine(root, "local-echoes");
        if (Directory.Exists(folder) &&
            (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Local map folder cannot be a symbolic link.");

        var requests = definitions.ToArray();
        if (requests.Length is < 1 or > 6)
            throw new InvalidDataException("Import requires between one and six known Echoes arenas.");
        var prepared = new List<(string File, byte[] Bytes)>(requests.Length);
        var ids = new HashSet<Guid>();
        var filenames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (MapDefinition map in requests)
        {
            ArgumentNullException.ThrowIfNull(map);
            bool sourceVisuals = map.Name.StartsWith("VISUAL ECHOES ", StringComparison.Ordinal);
            EchoesArenaSources.Arena arena = EchoesArenaSources.All.FirstOrDefault(x =>
                map.Name == (sourceVisuals ? "VISUAL ECHOES " : "LOCAL ECHOES ") + x.Name.ToUpperInvariant());
            if (string.IsNullOrEmpty(arena.Key) || !map.LocalSourceOnly ||
                map.FormatVersion != 3 || map.MapId == Guid.Empty || !ids.Add(map.MapId))
                throw new InvalidDataException("Only distinct local Echoes source maps can be installed.");
            string filename = arena.Key switch
            {
                "M01_SidehopperStation" => "sidehopper-station.json",
                "M02_Spires" => "spires.json",
                "M03_CrossfireChaos" => "crossfire-chaos.json",
                "M04_Pipeline" => "pipeline.json",
                "M05_SpiderComplex" => "spider-complex.json",
                "M06_ShootingGallery" => "shooting-gallery.json",
                _ => throw new InvalidDataException("Unsupported local Echoes arena.")
            };
            if (sourceVisuals) filename = "visual-" + filename;
            if (!filenames.Add(filename))
                throw new InvalidDataException("Duplicate local Echoes filename.");

            MapValidationResult validation = MapValidator.Validate(map, checkSources: false);
            if (!validation.IsValid)
                throw new InvalidDataException(arena.Name + " map validation failed: " +
                    string.Join("; ", validation.Diagnostics.Where(x =>
                        x.Severity == MapDiagnosticSeverity.Error).Select(x => x.Message).Take(5)));
            var compiled = MapBuilder.Build(map);
            if (compiled.Faces.Count == 0 || compiled.Solid.Count == 0 || compiled.Entities.Count < 2)
                throw new InvalidDataException(arena.Name + " has no viable geometry or spawns.");

            byte[] bytes = Encoding.UTF8.GetBytes(map.Serialize());
            if (bytes.LongLength is 0 or > MapPackageReader.MaxProjectBytes)
                throw new InvalidDataException(arena.Name + " exceeds the local project-size limit.");
            string target = Path.Combine(folder, filename);
            if (File.Exists(target) &&
                (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Local map file cannot be a symbolic link.");
            prepared.Add((target, bytes));
        }
        var installed = new List<string>(prepared.Count);
        foreach ((string path, byte[] bytes) in prepared)
        {
            AtomicFile.Write(path, bytes);
            installed.Add(path);
        }
        return installed;
    }

    public static int RunSpiresCommand(string? isoPath) =>
        RunCommand(isoPath, single: true);

    public static int RunAllArenasCommand(string? isoPath) =>
        RunCommand(isoPath, single: false);

    public static int RunVisualPreviewsCommand(string? isoPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(isoPath))
                throw new ArgumentException("Usage: -importechoesvisuals <path-to-owned-Echoes.iso-or-gcm>");
            var installed = InstallVisualPreviews(isoPath, CustomRooms.UserMapDirectory);
            Console.WriteLine("[echoes] Installed " + installed.Count +
                " local-only world-model preview maps. Restart the game to discover them.");
            Console.WriteLine("[echoes] Original source geometry remains private; online matches and Community transfers are disabled.");
            return 0;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or OverflowException or MapAuthoringException)
        {
            Console.Error.WriteLine("[echoes] Visual preview import failed: " + ex.Message);
            return 1;
        }
    }

    private static int RunCommand(string? isoPath, bool single)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(isoPath))
                throw new ArgumentException(single
                    ? "Usage: -importechoesspires <path-to-owned-Echoes.iso-or-gcm>"
                    : "Usage: -importechoesarenas <path-to-owned-Echoes.iso-or-gcm>");
            IReadOnlyList<string> paths = single
                ? new[] { InstallSpires(isoPath, CustomRooms.UserMapDirectory) }
                : InstallAllArenas(isoPath, CustomRooms.UserMapDirectory);
            Console.WriteLine("[echoes] Installed " + paths.Count +
                " local-only Echoes collision previews. Restart the game to discover them.");
            foreach (string path in paths) Console.WriteLine("[echoes] " + path);
            Console.WriteLine("[echoes] These are private source-derived maps, not distributable Community packages.");
            return 0;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or OverflowException or MapAuthoringException)
        {
            Console.Error.WriteLine("[echoes] Import failed: " + ex.Message);
            return 1;
        }
    }
}
