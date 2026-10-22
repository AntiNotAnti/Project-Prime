using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
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

    /// <summary>Experimental four-arena, private-only kinetic cannon variants.
    /// Other two Echoes arenas contain no upstream kinetic-cannon tuning.</summary>
    public static IReadOnlyList<MapDefinition> ReadCannonPlaytests(string isoPath)
    {
        var maps = ReadSelectedArenas(isoPath, EchoesTunedCannons.ArenaKeys, sourceVisuals: true);
        return maps.Select(map =>
        {
            EchoesArenaSources.Arena source = EchoesArenaSources.All.Single(arena =>
                map.Name == "VISUAL ECHOES " + arena.Name.ToUpperInvariant());
            return EchoesTunedCannons.CreateVariant(map, source.Key);
        }).ToArray();
    }

    public static IReadOnlyList<string> InstallCannonPlaytests(string isoPath, string userMapDirectory) =>
        InstallLocalDefinitions(ReadCannonPlaytests(isoPath), userMapDirectory);

    public static IReadOnlyList<MapDefinition> ReadRoutedCannonPlaytests(string isoPath)
    {
        var cannons = ReadCannonPlaytests(isoPath);
        return cannons.Select(map =>
        {
            EchoesArenaSources.Arena arena = EchoesArenaSources.All.Single(a =>
                map.Name == "CANNON ECHOES " + a.Name.ToUpperInvariant());
            return EchoesTunedCannons.CreateRoutedVariant(map, arena.Key);
        }).ToArray();
    }

    public static IReadOnlyList<string> InstallRoutedCannonPlaytests(string isoPath, string userMapDirectory) =>
        InstallLocalDefinitions(ReadRoutedCannonPlaytests(isoPath), userMapDirectory);



    /// <summary>New source-static identity; historical visual/cannon/routed maps stay unchanged.
    /// Exact per-corner primary UVs are retained. Current MapMesh has one UV channel and flat
    /// normals, so this remains an explicitly labelled fallback-material static preview.</summary>
    public static MapDefinition ReadStaticSpiresPlaytest(string isoPath)
    {
        var source = EchoesStaticVisualLayer.ReadVerifiedSpires(isoPath);
        return EchoesStaticVisualLayer.Append(source, source.CreateParent(), (_, _) => 0,
            (model, material, corner) => EchoesStaticVisualLayer.PrimarySourceUv(source, model, material, corner));
    }

    public static string InstallStaticSpiresPlaytest(string isoPath, string userMapDirectory) =>
        InstallLocalDefinition(ReadStaticSpiresPlaytest(isoPath), userMapDirectory);

    public static int RunStaticSpiresCommand(string? isoPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(isoPath))
                throw new ArgumentException("Usage: -importechoesstatics <path-to-owned-GameCube-Echoes.iso-or-gcm-or-rvz>");
            string file = InstallStaticSpiresPlaytest(isoPath, CustomRooms.UserMapDirectory);
            Console.WriteLine("[echoes] Installed private STATIC ECHOES SPIRES: " + file);
            Console.WriteLine("[echoes] Seven source pillar models, unchanged original collision, fallback material and primary source UVs. Offline solo Battle only.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[echoes] Source static import refused: " + ex.Message);
            return 1;
        }
    }

    public static string InstallLocalDefinition(MapDefinition definition, string userMapDirectory) =>
        InstallLocalDefinitions(new[] { definition }, userMapDirectory)[0];

    /// <summary>Validate every map, then publish one immutable local generation
    /// through a single receipt commit. Previous generations remain readable.</summary>
    public static IReadOnlyList<string> InstallLocalDefinitions(
        IEnumerable<MapDefinition> definitions, string userMapDirectory,
        CancellationToken cancellation = default, IProgress<ImportPublishStage>? progress = null)
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
            cancellation.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(map);
            bool staticPreview = map.Name == "STATIC ECHOES SPIRES";
            bool routed = staticPreview || map.Name.StartsWith("ROUTED ECHOES ", StringComparison.Ordinal);
            bool cannonPlaytest = !routed &&
                map.Name.StartsWith("CANNON ECHOES ", StringComparison.Ordinal);
            bool sourceVisuals = !routed && !cannonPlaytest &&
                map.Name.StartsWith("VISUAL ECHOES ", StringComparison.Ordinal);
            string prefix = staticPreview ? "STATIC ECHOES " : routed ? "ROUTED ECHOES " :
                cannonPlaytest ? "CANNON ECHOES " :
                sourceVisuals ? "VISUAL ECHOES " : "LOCAL ECHOES ";
            EchoesArenaSources.Arena arena = EchoesArenaSources.All.FirstOrDefault(x =>
                map.Name == prefix + x.Name.ToUpperInvariant());
            if (cannonPlaytest && (EchoesTunedCannons.CountFor(arena.Key) == 0 ||
                map.Capabilities?.MaxPlayers != 1))
                throw new InvalidDataException("Tuned cannon maps require the original private solo-only arena.");
            if (routed && (map.Capabilities?.MaxPlayers != 1 ||
                map.NavigationLinks.Count != EchoesTunedCannons.CountFor(arena.Key) ||
                map.NavigationLinks.Any(x => x.Kind != MapNavigationLinkKind.JumpPad || x.Bidirectional)))
                throw new InvalidDataException("Routed Echoes map requires its authored one-way routes and solo-only mode.");
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
            if (cannonPlaytest) filename = "cannon-" + filename;
            if (staticPreview) filename = "static-" + filename;
            else if (routed) filename = "routed-" + filename;
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
            if (routed)
            {
                var graph = MapNodePacker.Analyze(compiled.Solid, MapNodePacker.EffectiveLinks(map));
                // Counts alone cannot prove a route survived endpoint snapping.
                // Use the compiler's exact final remap, never a second nearest
                // lookup that could select a different node on a tie.
                if (graph.AuthoredLinks.Count != map.NavigationLinks.Count ||
                    graph.AuthoredLinks.Where((report, index) =>
                        report.Ordinal != index || report.LinkId != map.NavigationLinks[index].Id ||
                        !report.Encoded || report.Refusal != MapNodePacker.NavigationLinkRefusal.None ||
                        report.FromNode == report.ToNode || report.Bidirectional).Any())
                    throw new InvalidDataException(arena.Name +
                        " has an authored cannon route that was not encoded as a directed native edge.");
            }

            byte[] bytes = Encoding.UTF8.GetBytes(map.Serialize());
            if (bytes.LongLength is 0 or > MapPackageReader.MaxProjectBytes)
                throw new InvalidDataException(arena.Name + " exceeds the local project-size limit.");
            string target = Path.Combine(folder, filename);
            if (File.Exists(target) &&
                (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Local map file cannot be a symbolic link.");
            prepared.Add((target, bytes));
        }
        string? source = requests[0].LocalSourceFingerprint;
        if (source is not { Length: 40 } || !source.All(Uri.IsHexDigit)
            || requests.Any(map => map.LocalSourceFingerprint != source))
            throw new InvalidDataException("All bundle components require the same verified local source edition.");
        var parts = prepared.ToDictionary(p => Path.GetFileName(p.File), p => p.Bytes, StringComparer.Ordinal);
        return LocalImportBundle.Publish(root, BundleSpec(source, parts.Keys.ToArray()), parts, cancellation, progress);
    }

    public static ImportBundleSpec CurrentBundleSpec => BundleSpec(
        ExternalSourceRequirements.For(ExternalGameKind.Echoes).Single(x => x.Path == "Metroid6.pak").ExpectedSha1,
        Array.Empty<string>());

    public static ImportBundleSpec BundleSpec(string sourceFingerprint, string[] requiredComponents) =>
        new("echoes", 1, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            "project-prime/echoes-local-generation-v1/c16a907b3658c2fd4079e394a73711596a85caee"))),
            sourceFingerprint, requiredComponents);


    public static int RunSpiresCommand(string? isoPath) =>
        RunCommand(isoPath, single: true);

    public static int RunAllArenasCommand(string? isoPath) =>
        RunCommand(isoPath, single: false);

    public static int RunRoutedCannonPlaytestsCommand(string? isoPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(isoPath))
                throw new ArgumentException("Usage: -importechoesroutes <path-to-owned-GameCube-Echoes.iso-or-gcm-or-rvz>");
            var files = InstallRoutedCannonPlaytests(isoPath, CustomRooms.UserMapDirectory);
            Console.WriteLine("[echoes] Installed " + files.Count +
                " solo-only cannon bot-route playtests. Restart to load them.");
            return 0;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or OverflowException or MapAuthoringException)
        {
            Console.Error.WriteLine("[echoes] Routed cannon import rejected: " + ex.Message);
            return 1;
        }
    }

    public static int RunCannonPlaytestsCommand(string? isoPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(isoPath))
                throw new ArgumentException("Usage: -importechoescannons <path-to-owned-GameCube-Echoes.iso-or-gcm-or-rvz>");
            var installed = InstallCannonPlaytests(isoPath, CustomRooms.UserMapDirectory);
            Console.WriteLine("[echoes] Installed " + installed.Count +
                " experimental solo-only orb-cannon variants; restart to discover them.");
            Console.WriteLine("[echoes] Source geometry stays local; no online match or Community transfer is enabled.");
            return 0;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or OverflowException or MapAuthoringException)
        {
            Console.Error.WriteLine("[echoes] Orb-cannon import refused: " + ex.Message);
            return 1;
        }
    }

    public static int RunVisualPreviewsCommand(string? isoPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(isoPath))
                throw new ArgumentException("Usage: -importechoesvisuals <path-to-owned-GameCube-Echoes.iso-or-gcm-or-rvz>");
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
                    ? "Usage: -importechoesspires <path-to-owned-GameCube-Echoes.iso-or-gcm-or-rvz>"
                    : "Usage: -importechoesarenas <path-to-owned-GameCube-Echoes.iso-or-gcm-or-rvz>");
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
