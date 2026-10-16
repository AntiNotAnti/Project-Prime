using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using MphRead.Mods.Import.Retro;
using MphRead.Mods.Platform;

namespace MphRead.Mods.Import;

/// <summary>
/// Source-local, geometry-only preview conversion for Dark Samus (Echoes)
/// and four Prime 1 Trooper looks. Source model/skeleton data is generated
/// only in the user's own application data; this is not a character gameplay
/// install, network pack, or native animated skin.
/// </summary>
public static class RetroCharacterPreviewExporter
{
    public const int MaxVertices = 2_000_000;
    public const int MaxTriangles = 500_000;
    public const long MaxOutputBytes = 256L * 1024 * 1024;
    public sealed record SourceCharacter(
        string Id, ExternalGameKind Game, uint Ancs, string NativeName);
    public static IReadOnlyList<SourceCharacter> Sources { get; } = Array.AsReadOnly(new[]
    {
        new SourceCharacter("darksamus-echoes", ExternalGameKind.Echoes, 0x39900a11, "Dark_Samus"),
        new SourceCharacter("trooper-power", ExternalGameKind.MetroidPrime, 0x8169653a, "power_trooper"),
        new SourceCharacter("trooper-ice", ExternalGameKind.MetroidPrime, 0x8169653a, "ice_trooper"),
        new SourceCharacter("trooper-wave", ExternalGameKind.MetroidPrime, 0x8169653a, "wave_trooper"),
        new SourceCharacter("trooper-plasma", ExternalGameKind.MetroidPrime, 0x8169653a, "plasma_trooper"),
    });

    public static string DefaultRoot => Path.Combine(AppPaths.UserDataDirectory,
        "source-character-previews");

    public static int RunCommand(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            Console.Error.WriteLine("[retro-character] -retrocharacterexport requires a locally owned Prime or Echoes .iso/.gcm path.");
            return 2;
        }
        try
        {
            IReadOnlyList<string> files = ExportFromOwnedGame(sourcePath);
            Console.WriteLine("[retro-character] Wrote " + files.Count +
                " private OBJ, CINF skeleton, skin-weight and sparse motion sidecars:");
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
        string? destinationRoot = null)
    {
        using GameCubeDiscReader disc = GameCubeDiscReader.Open(sourcePath);
        string root = Path.GetFullPath(destinationRoot ?? DefaultRoot);
        if (Directory.Exists(root) &&
            (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Source model output directory must not be a symbolic link.");

        // A preview must be sourced from the exact pinned media edition.
        // SHA1 here is an upstream recipe identity, not a security signature.
        var required = ExternalSourceRequirements.For(disc.Game);
        foreach (var archive in required)
        {
            if (ExternalSourceRequirements.Verify(disc, archive).Status != ExternalArchiveStatus.Verified)
                throw new InvalidDataException("User-owned disc source does not match the pinned archive recipe: " +
                    archive.Path);
        }

        var paks = new List<RetroPakReader>();
        string staging = Path.Combine(root, ".staging-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (ExternalArchiveRequirement archive in required)
                paks.Add(new RetroPakReader(disc.OpenRead(archive.Path), leaveOpen: false));

            byte[] Resource(uint id)
            {
                foreach (RetroPakReader pak in paks)
                    if (pak.Contains(id)) return pak.ReadResource(id);
                throw new InvalidDataException("Missing required local character resource " +
                    id.ToString("x8"));
            }

            Directory.CreateDirectory(staging);
            var staged = new List<(string Temp, string Final)>();
            foreach (SourceCharacter spec in Sources.Where(x => x.Game == disc.Game))
            {
                byte[] ancsBytes = Resource(spec.Ancs);
                RetroCharacterRefs refs = RetroCharacterSource.FindCharacter(
                    ancsBytes, spec.NativeName);
                if (refs.Cmdl == 0 || refs.Cskr == 0 || refs.Cinf == 0)
                    throw new InvalidDataException("Incomplete Retro character resource references: " + spec.Id);
                RetroNativeSkeleton skeleton =
                    RetroCharacterSource.ReadSkeleton(Resource(refs.Cinf));
                Cmdl model = Cmdl.Load(Resource, refs.Cmdl, refs.Cskr);
                if (model.PositionCount < 3 || model.TriangleCount < 1 ||
                    model.PositionCount > MaxVertices || model.TriangleCount > MaxTriangles ||
                    model.Skin?.Count != model.PositionCount)
                    throw new InvalidDataException("Incomplete or oversized skinned Retro body: " + spec.Id);

                string obj = spec.Id + ".obj";
                string json = spec.Id + ".skeleton.json";
                string stagedObj = Path.Combine(staging, obj);
                using (var writer = new StreamWriter(stagedObj, false,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 65536))
                    WriteObj(model, writer);
                if (new FileInfo(stagedObj).Length > MaxOutputBytes)
                    throw new InvalidDataException("OBJ source preview is too large: " + spec.Id);
                string stagedSkeleton = Path.Combine(staging, json);
                File.WriteAllText(stagedSkeleton, JsonSerializer.Serialize(new
                {
                    spec.Id, SourceGameId = disc.GameId, Refs = refs,
                    SourceCharacter = spec.NativeName,
                    BoneCount = skeleton.Bones.Count,
                    Bones = skeleton.Bones.Values.OrderBy(b => b.Id)
                        .Select(b => new { b.Id, b.Parent, b.Name, Position = new[] { b.X, b.Y, b.Z } })
                        .ToArray(),
                    Notes = "Local preview: needs rig retargeting, animation, materials, gun and alt form before gameplay."
                }));
                // Optional sparse pose samples are authored from the same
                // validated local disc. Blends stay explicitly unresolved;
                // no unsupported ANIM is substituted with a guessed pose.
                RetroMotionPreview.Inventory motion = RetroMotionPreview.FromLocalSource(
                    ancsBytes, Resource, disc.Game == ExternalGameKind.Echoes);
                string motionName = spec.Id + ".motions.json";
                string stagedMotion = Path.Combine(staging, motionName);
                File.WriteAllText(stagedMotion, JsonSerializer.Serialize(motion));
                if (new FileInfo(stagedMotion).Length > RetroMotionPreview.MaxAnimationResourceBytes)
                    throw new InvalidDataException("Motion preview exceeds bounded sidecar output.");
                staged.Add((stagedObj, Path.Combine(root, obj)));
                staged.Add((stagedSkeleton, Path.Combine(root, json)));
                staged.Add((stagedMotion, Path.Combine(root, motionName)));
                string weightsName = spec.Id + ".weights.json";
                string stagedWeights = Path.Combine(staging, weightsName);
                using (FileStream weightsFile = new(stagedWeights, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 65536))
                    RetroCharacterSkinWeights.Write(model, weightsFile);
                if (new FileInfo(stagedWeights).Length > MaxOutputBytes)
                    throw new InvalidDataException("Source skin weights exceed the private sidecar budget.");
                staged.Add((stagedWeights, Path.Combine(root, weightsName)));
            }
            if (staged.Count == 0)
                throw new InvalidDataException("No supported Retro characters in the supplied source.");

            // Every body/skeleton is parsed before the first file is published.
            // Individual final moves are atomic, and a failed source never
            // emits an incomplete OBJ. No assets are pushed to network/CDN.
            foreach ((string from, string to) in staged)
                File.Move(from, to, overwrite: true);
            return staged.Where(x => x.Final.EndsWith(".obj", StringComparison.Ordinal))
                .Select(x => x.Final).ToArray();
        }
        finally
        {
            foreach (RetroPakReader pak in paks) pak.Dispose();
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
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
