using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Threading;
using MphRead.Mods.Import.Retro;
using MphRead.Mods.Platform;

namespace MphRead.Mods.Import;

/// <summary>
/// Source-local, geometry-only preview conversion for Dark Samus (Echoes)
/// and four Prime 1 Trooper looks. Source model/skeleton data is generated
/// only in the user's own application data; this is not a character gameplay
/// install, network pack, or native animated skin.
/// </summary>
public static partial class RetroCharacterPreviewExporter
{
    public const int MaxVertices = 2_000_000;
    public const int MaxTriangles = 500_000;
    public const long MaxOutputBytes = 256L * 1024 * 1024;
    public sealed record SourceCharacter(
        string Id, ExternalGameKind Game, uint Ancs, string NativeName)
    {
        public SourcePart? AlternateForm { get; init; }
        public string DisplayName => Id switch
        {
            "darksamus-echoes"=>"Dark Samus (Echoes)",
            "darksamus-overcharged-echoes"=>"Overcharged Dark Samus (Echoes)",
            "trooper-power"=>"Power Trooper (Prime)","trooper-ice"=>"Ice Trooper (Prime)",
            "trooper-wave"=>"Wave Trooper (Prime)","trooper-plasma"=>"Plasma Trooper (Prime)",
            _=>Id
        };
    }
    public static IReadOnlyList<SourceCharacter> Sources { get; } = Array.AsReadOnly(new[]
    {
        new SourceCharacter("darksamus-echoes", ExternalGameKind.Echoes, 0x39900a11, "Dark_Samus")
            { AlternateForm = new("ball", 0x654a3f97, "Dark_Samus_MorphBall", 1) },
        new SourceCharacter("trooper-power", ExternalGameKind.MetroidPrime, 0x8169653a, "power_trooper"),
        new SourceCharacter("trooper-ice", ExternalGameKind.MetroidPrime, 0x8169653a, "ice_trooper"),
        new SourceCharacter("trooper-wave", ExternalGameKind.MetroidPrime, 0x8169653a, "wave_trooper"),
        new SourceCharacter("trooper-plasma", ExternalGameKind.MetroidPrime, 0x8169653a, "plasma_trooper"),
        // A distinct source body in the same verified ANCS, never an alias of
        // Dark_Samus or a playable/native-Hunter registration.
        new SourceCharacter("darksamus-overcharged-echoes", ExternalGameKind.Echoes, 0x39900a11, "Battle_3")
            { AlternateForm = new("ball", 0x654a3f97, "Dark_Samus_MorphBall", 1) },
    });

    public static string DefaultRoot => Path.Combine(AppPaths.UserDataDirectory,
        "source-character-previews");

    public static int RunCommand(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            Console.Error.WriteLine("[retro-character] -retrocharacterexport requires a locally owned GameCube Prime or Echoes .iso/.gcm/.rvz path (RVZ: None or Zstandard).");
            return 2;
        }
        try
        {
            IReadOnlyList<string> files = ExportFromOwnedGame(sourcePath);
            Console.WriteLine("[retro-character] Published " + files.Count +
                " private character preview bundles. Selected OBJ paths:");
            foreach (string file in files) Console.WriteLine("[retro-character] " + file);
            Console.WriteLine("[retro-character] Not installed as a playable Hunter; no files were uploaded.");
            return 0;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException
            or UnauthorizedAccessException or ArgumentException or NotSupportedException
            or OverflowException or KeyNotFoundException)
        {
            Console.Error.WriteLine("[retro-character] Source preview not installed: " + ex.Message);
            return 1;
        }
    }

    public static IReadOnlyList<string> ExportFromOwnedGame(string sourcePath,
        string? destinationRoot = null, CancellationToken cancellation = default,
        IProgress<ImportPublishStage>? progress = null, IReadOnlyCollection<string>? selectedIds = null)
    {
        cancellation.ThrowIfCancellationRequested();
        progress?.Report(ImportPublishStage.Checking);
        using GameCubeDiscReader disc = GameCubeDiscReader.Open(sourcePath);
        string root = Path.GetFullPath(destinationRoot ?? DefaultRoot);
        if (Directory.Exists(root) &&
            (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Source model output directory must not be a symbolic link.");

        // A preview must be sourced from the exact pinned media edition.
        // SHA1 here is an upstream recipe identity, not a security signature.
        var selected = Sources.Where(x => x.Game == disc.Game && (selectedIds == null || selectedIds.Contains(x.Id, StringComparer.Ordinal))).ToArray();
        if (selected.Length == 0 || selectedIds != null && (selectedIds.Count != selected.Length || selectedIds.Distinct(StringComparer.Ordinal).Count() != selectedIds.Count))
            throw new InvalidDataException("Choose supported character previews from this source edition.");
        var required = ExternalSourceRequirements.For(disc.Game);
        foreach (var archive in required)
        {
            cancellation.ThrowIfCancellationRequested();
            if (ExternalSourceRequirements.Verify(disc, archive).Status != ExternalArchiveStatus.Verified)
                throw new InvalidDataException("User-owned disc source does not match the pinned archive recipe: " +
                    archive.Path);
        }

        var paks = new List<RetroPakReader>();
        string staging = Path.Combine(Path.GetTempPath(), "prime-character-stage-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (ExternalArchiveRequirement archive in required)
                paks.Add(new RetroPakReader(disc.OpenRead(archive.Path), leaveOpen: false));

            byte[] Resource(uint id)
            {
                cancellation.ThrowIfCancellationRequested();
                foreach (RetroPakReader pak in paks)
                    if (pak.Contains(id)) return pak.ReadResource(id);
                throw new InvalidDataException("Missing required local character resource " +
                    id.ToString("x8"));
            }

            Directory.CreateDirectory(staging);
            var staged = new List<(string Temp, string Final)>();
            foreach (SourceCharacter spec in selected)
            {
                cancellation.ThrowIfCancellationRequested();
                var parts = new List<AuthoringPart>();
                foreach (SourcePart part in PartsFor(spec.Id))
                {
                    cancellation.ThrowIfCancellationRequested();
                    parts.Add(WritePart(spec, part, disc.GameId, staging, root, Resource, staged, cancellation));
                }
                if (spec.AlternateForm != null)
                {
                    string name = spec.Id + ".parts.json";
                    string path = Path.Combine(staging, name);
                    var index = new AuthoringPartsIndex(1, spec.Id, disc.GameId, 3, false,
                        parts.ToArray(), new[] { "source-materials", "first-person", "runtime-animation-and-morph" });
                    File.WriteAllText(path, JsonSerializer.Serialize(index));
                    staged.Add((path, Path.Combine(root, name)));
                }
            }
            if (staged.Count == 0)
                throw new InvalidDataException("No supported Retro characters in the supplied source.");

            // Each appearance is a complete immutable generation. Parse all
            // selected appearances before publishing any; Update all commits
            // one independent appearance at a time, never half a model/rig.
            IEnumerable<ImportBundleUpdate> PrepareUpdates()
            {
                foreach (SourceCharacter spec in selected)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var parts = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                    long total = 0;
                    foreach (var file in staged.Where(x => Path.GetFileName(x.Temp).StartsWith(spec.Id + ".", StringComparison.Ordinal)))
                    {
                        cancellation.ThrowIfCancellationRequested();
                        long length = new FileInfo(file.Temp).Length;
                        if (length is <= 0 or > LocalImportBundle.MaxComponentBytes || (total += length) > LocalImportBundle.MaxBundleBytes)
                            throw new InvalidDataException("Source character bundle exceeds its private component budget.");
                        parts.Add(Path.GetFileName(file.Temp), File.ReadAllBytes(file.Temp));
                    }
                    yield return new(BundleSpec(spec.Id),parts);
                }
            }
            return LocalImportBundle.PublishSequential(root,PrepareUpdates(),cancellation,progress)
                .Where(path=>selected.Any(spec=>Path.GetFileName(path)==spec.Id+".obj")).ToArray();
        }
        finally
        {
            foreach (RetroPakReader pak in paks) pak.Dispose();
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>OBJ vertex, normal, UV and indexed-triangle handoff for
    /// local artists. No material textures or animation assets are embedded.</summary>
    public static void WriteObj(Cmdl model, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(writer);
        if (model.Positions == null || model.Positions.Length % 3 != 0 ||
            model.Normals == null || model.Normals.Length % 3 != 0 ||
            model.Uv0 == null || model.Uv0.Length % 2 != 0 ||
            model.TriangleMaterial == null || model.Triangles == null ||
            model.Triangles.Length != model.TriangleMaterial.Length * 3 ||
            model.Materials == null || model.Materials.Length == 0 ||
            model.PositionCount is < 3 or > MaxVertices ||
            model.TriangleCount is < 1 or > MaxTriangles)
            throw new InvalidDataException("Invalid or oversized Retro character model geometry.");

        int normCount = model.Normals.Length / 3;
        int texCount = model.Uv0.Length / 2;
        int shortCount = model.ShortUvs?.Length / 2 ?? 0;
        if (model.ShortUvs != null && model.ShortUvs.Length % 2 != 0)
            throw new InvalidDataException("Retro short UV buffer is truncated.");

        writer.WriteLine("# Local Retro body geometry from user-owned source; do not redistribute");
        writer.WriteLine("o RetroLocalCharacter");
        for (int i = 0; i < model.Positions.Length; i += 3)
        {
            float x = model.Positions[i], y = model.Positions[i + 1], z = model.Positions[i + 2];
            if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
                throw new InvalidDataException("Nonfinite Retro source position.");
            writer.WriteLine(FormattableString.Invariant($"v {x:R} {y:R} {z:R}"));
        }
        for (int i = 0; i < model.Uv0.Length; i += 2)
            writer.WriteLine(FormattableString.Invariant($"vt {model.Uv0[i]:R} {model.Uv0[i+1]:R}"));
        if (model.ShortUvs != null)
            for (int i = 0; i < model.ShortUvs.Length; i += 2)
                writer.WriteLine(FormattableString.Invariant($"vt {model.ShortUvs[i]:R} {model.ShortUvs[i+1]:R}"));
        for (int i = 0; i < model.Normals.Length; i += 3)
            writer.WriteLine(FormattableString.Invariant(
                $"vn {model.Normals[i]:R} {model.Normals[i+1]:R} {model.Normals[i+2]:R}"));

        int previousMaterial = -1;
        for (int i = 0; i < model.TriangleCount; i++)
        {
            int material = model.TriangleMaterial[i];
            if ((uint)material >= model.Materials.Length)
                throw new InvalidDataException("Retro source triangle has invalid material.");
            if (material != previousMaterial)
            {
                writer.WriteLine("g material_" + material.ToString(CultureInfo.InvariantCulture));
                previousMaterial = material;
            }

            string Vertex(Cmdl.Corner corner)
            {
                if ((uint)corner.P >= model.PositionCount)
                    throw new InvalidDataException("Retro model triangle index exceeds positions.");
                int vtIndex = corner.T0;
                if (vtIndex >= 0 && (model.Materials[material].Flags & Cmdl.ShortUv) != 0)
                    vtIndex += texCount;
                bool useUv = vtIndex >= 0 && vtIndex < texCount + shortCount;
                bool useNormal = corner.N >= 0 && corner.N < normCount;
                string p = (corner.P + 1).ToString(CultureInfo.InvariantCulture);
                if (!useUv && !useNormal) return p;
                string uv = useUv ? (vtIndex + 1).ToString(CultureInfo.InvariantCulture) : "";
                string n = useNormal ? (corner.N + 1).ToString(CultureInfo.InvariantCulture) : "";
                return p + "/" + uv + (useNormal ? "/" + n : "");
            }
            int at = i * 3;
            writer.WriteLine("f " + Vertex(model.Triangles[at]) + " " +
                Vertex(model.Triangles[at + 1]) + " " +
                Vertex(model.Triangles[at + 2]));
        }
    }
}
