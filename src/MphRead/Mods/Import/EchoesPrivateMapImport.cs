using System;
using System.IO;
using System.Linq;
using System.Text;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Import;

/// <summary>
/// An explicit opt-in conversion of a user's own Echoes GameCube source to a
/// private Project Prime map recipe. Uses the normal local custom-map catalog
/// on NEXT launch; never builds a .ppmap or transfers source assets.
/// </summary>
public static class EchoesPrivateMapImport
{
    public const string RelativeSpiresFile = "local-echoes/spires.json";

    public static MapDefinition ReadSpires(string isoPath)
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
        const uint SpiresMrea = 0x09749b2f;
        if (!pak.Contains(SpiresMrea) || pak.Entry(SpiresMrea).Type != "MREA")
            throw new InvalidDataException("Spires MREA is missing from this Echoes disc.");
        EchoesMreaSections area = EchoesMreaSections.Parse(pak.ReadResource(SpiresMrea));
        MapDefinition definition = EchoesLocalMapDraft.BuildSpires(
            EchoesCollisionSource.Read(area), EchoesScriptSource.Read(area),
            verified.ActualSha1!);
        return definition;
    }

    /// <summary>
    /// Source files and staged outputs are user local. Never move a partial
    /// recipe into the runtime catalog. The consumer should restart Project
    /// Prime to register the newly installed room, not hot swap an active
    /// multiplayer/scene snapshot.
    /// </summary>
    public static string InstallSpires(string isoPath, string userMapDirectory)
        => InstallLocalDefinition(ReadSpires(isoPath), userMapDirectory);

    public static string InstallLocalDefinition(MapDefinition definition, string userMapDirectory)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(userMapDirectory);
        if (!definition.LocalSourceOnly || definition.Name != "LOCAL ECHOES SPIRES"
            || definition.FormatVersion != 3)
            throw new InvalidDataException("Only the local Echoes Spires source draft can be installed here.");
        // Validate the detached definition before any filesystem mutation.
        // Required native MPH assets/texture sources are checked when the
        // next launch loads and compiles the map normally.
        MapValidationResult validation = MapValidator.Validate(definition, checkSources: false);
        if (!validation.IsValid)
            throw new InvalidDataException("Spires local project validation failed: " +
                string.Join("; ", validation.Diagnostics.Where(x =>
                    x.Severity == MapDiagnosticSeverity.Error).Select(x => x.Message).Take(5)));

        var compiled = MapBuilder.Build(definition);
        if (compiled.Faces.Count == 0 || compiled.Solid.Count == 0 || compiled.Entities.Count < 2)
            throw new InvalidDataException("Spires local map has no viable geometry or spawns.");

        byte[] bytes = Encoding.UTF8.GetBytes(definition.Serialize());
        if (bytes.Length == 0 || bytes.LongLength > MapPackageReader.MaxProjectBytes)
            throw new InvalidDataException("Spires exceeds the local map project-size limit.");

        string root = Path.GetFullPath(userMapDirectory);
        string folder = Path.Combine(root, "local-echoes");
        string target = Path.Combine(folder, "spires.json");
        // This path must remain in the user's own map directory. Do not
        // follow an existing symlink out into another storage namespace.
        if (Directory.Exists(folder) &&
            (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Local map folder cannot be a symbolic link.");
        if (File.Exists(target) &&
            (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Local map file cannot be a symbolic link.");

        AtomicFile.Write(target, bytes);
        return target;
    }

    public static int RunSpiresCommand(string? isoPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(isoPath))
                throw new ArgumentException("Usage: -importechoesspires <path-to-owned-Echoes.iso-or-gcm>");
            string project = InstallSpires(isoPath, CustomRooms.UserMapDirectory);
            Console.WriteLine("[echoes] Installed local-only Spires map: " + project);
            Console.WriteLine("[echoes] Restart the game before opening the new map. Do not distribute extracted map recipes or game assets.");
            return 0;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or OverflowException or MapAuthoringException)
        {
            Console.Error.WriteLine("[echoes] Import failed without changing active game content: " + ex.Message);
            return 1;
        }
    }
}
