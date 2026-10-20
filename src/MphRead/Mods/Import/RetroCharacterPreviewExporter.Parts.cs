using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MphRead.Mods.Import.Retro;

namespace MphRead.Mods.Import;

public static partial class RetroCharacterPreviewExporter
{
    public sealed record SourcePart(string Id, uint Ancs, string NativeName, int Version);
    public sealed record AuthoringComponent(string Role, int Version, string File);
    public sealed record AuthoringPart(string Id, int Version, uint Ancs, string NativeName,
        RetroCharacterRefs Refs, AuthoringComponent[] RequiredComponents, string[] OptionalComponents);
    public sealed record AuthoringPartsIndex(int Schema, string CharacterId, string SourceGameId,
        int BundleVersion, bool RuntimeInstall, AuthoringPart[] Parts, string[] UnqualifiedParts);
    public sealed record AuthoringComponentStatus(string Role, int Version, ImportBundleState State);
    public sealed record AuthoringPartStatus(string Id, int Version, ImportBundleState State,
        IReadOnlyList<AuthoringComponentStatus> Components);

    public static IReadOnlyList<AuthoringPartStatus> InspectParts(string id, ImportBundleInspection inspection)
    {
        return Array.AsReadOnly(PartsFor(id).Select(part =>
        {
            var components = RequiredPartComponents(id, part.Id).Select(component =>
                new AuthoringComponentStatus(component.Role, component.Version,
                    inspection.ComponentStates?.SingleOrDefault(c => c.Id == component.File)?.State
                    ?? (inspection.State == ImportBundleState.Ready ? ImportBundleState.Incomplete : inspection.State))).ToArray();
            ImportBundleState state = components.Any(c => c.State == ImportBundleState.Failed)
                ? ImportBundleState.Failed : components.FirstOrDefault(c => c.State != ImportBundleState.Ready)?.State
                    ?? ImportBundleState.Ready;
            return new AuthoringPartStatus(part.Id, part.Version, state, Array.AsReadOnly(components));
        }).ToArray());
    }

    public static string DescribeInspection(string id, ImportBundleInspection inspection)
    {
        string parts = string.Join("; ", InspectParts(id, inspection).Select(part =>
            part.Id + " v" + part.Version + ": " + part.State
            + (part.State == ImportBundleState.Ready || inspection.ComponentStates == null ? "" : " (" + string.Join(", ", part.Components
                .Where(c => c.State != ImportBundleState.Ready).Select(c => c.Role + " v" + c.Version + " " + c.State)) + ")")));
        return id + ": " + inspection.State + ". " + parts + ". " + inspection.Message
            + ". Source authoring bundle; normal Echoes supports an animated diagnostic preview. Materials, first-person and playable animation/morph remain unqualified.";
    }

    private static SourceCharacter Source(string id) => Sources.SingleOrDefault(s => s.Id == id)
        ?? throw new InvalidDataException("Unknown canonical source character preview.");

    /// <summary>Logical parts share their existing appearance/card identity.</summary>
    public static IReadOnlyList<SourcePart> PartsFor(string id)
    {
        SourceCharacter source = Source(id);
        var body = new SourcePart("body", source.Ancs, source.NativeName, source.Game == ExternalGameKind.Echoes ? 3 : 1);
        return Array.AsReadOnly(source.AlternateForm is { } ball ? new[] { body, ball with { Version = 3 } } : new[] { body });
    }

    public static string PartStem(string id, string part)
    {
        if (!PartsFor(id).Any(p => p.Id == part))
            throw new InvalidDataException("Unknown or unsupported source character part.");
        return part == "body" ? id : id + ".ball";
    }

    private static AuthoringComponent[] RequiredPartComponents(string id, string part)
    {
        string stem = PartStem(id, part);
        var legacy = new[]
        {
            new AuthoringComponent("geometry", 1, stem + ".obj"),
            new AuthoringComponent("rig", 1, stem + ".skeleton.json"),
            new AuthoringComponent("weights", 1, stem + ".weights.json"),
            new AuthoringComponent("motion-samples", 1, stem + ".motions.json")
        };
        return Source(id).Game == ExternalGameKind.Echoes ? legacy.Concat(new[]
        {
            new AuthoringComponent("skeleton-source", 1, stem + ".cinf.bin"),
            new AuthoringComponent("animation-resources", 1, stem + ".animations.bin"),
            new AuthoringComponent("source-rig-geometry", 1, stem + ".source.glb"),
            new AuthoringComponent("generated-provenance", 1, stem + ".source.json")
        }).Concat(id == "darksamus-echoes" && part == "body"
            ? new[] { new AuthoringComponent("source-preview-manifest", 1, "characters.json") }
            : Array.Empty<AuthoringComponent>()).ToArray() : legacy;
    }

    /// <summary>Receipt describes complete authoring parts, never gameplay readiness.</summary>
    public static ImportBundleSpec BundleSpec(string sourceCharacterId)
    {
        SourceCharacter spec = Source(sourceCharacterId);
        string source = string.Join("\n", ExternalSourceRequirements.For(spec.Game)
            .OrderBy(a => a.Path, StringComparer.Ordinal).Select(a => a.Path + ":" + a.ExpectedSha1));
        string fingerprint = Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(source)));
        if (spec.AlternateForm == null)
        {
            // Preserve the original four Prime body recipes byte-for-byte.
            string legacy = $"retro-character-preview-v1:{spec.Id}:{spec.Game}:{spec.Ancs:x8}:{spec.NativeName}:obj-skeleton-weights-motions-optional-graph";
            return new(spec.Id, 1, HashRecipe(legacy), fingerprint,
                RequiredPartComponents(spec.Id, "body").Select(c => c.File).ToArray());
        }
        string recipe = $"retro-character-preview-v4:{spec.Id}:{spec.Game}:parts-index-v1:"
            + string.Join(":", PartsFor(spec.Id).Select(p =>
                $"{p.Id}-v{p.Version}:{p.Ancs:x8}:{p.NativeName}:geometry-v1-rig-v1-weights-v1-motion-samples-v1-cinf-v1-full-anim-bank-v1-generated-glb-v1-diagnostic-colors-v1-generated-provenance-v2-optional-part-graph"));
        return new(spec.Id, 4, HashRecipe(recipe), fingerprint,
            PartsFor(spec.Id).SelectMany(p => RequiredPartComponents(spec.Id, p.Id)).Select(c => c.File)
                .Append(spec.Id + ".parts.json").ToArray());
    }

    private static string HashRecipe(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static AuthoringPart WritePart(SourceCharacter spec, SourcePart part, string gameId,
        string staging, string root, Func<uint, byte[]> resource, List<(string Temp, string Final)> staged, System.Threading.CancellationToken cancellation)
    {
        string stem = PartStem(spec.Id, part.Id);
        byte[] ancsBytes = resource(part.Ancs);
        RetroCharacterRefs refs = RetroCharacterSource.FindCharacter(ancsBytes, part.NativeName);
        if (refs.Cmdl == 0 || refs.Cskr == 0 || refs.Cinf == 0)
            throw new InvalidDataException("Incomplete Retro source part references: " + stem);
        byte[] cinfBytes = resource(refs.Cinf);
        RetroNativeSkeleton skeleton = RetroCharacterSource.ReadSkeleton(cinfBytes);
        byte[] cmdlBytes = resource(refs.Cmdl), cskrBytes = resource(refs.Cskr);
        Cmdl model = Cmdl.Load(id => id == refs.Cmdl ? cmdlBytes : id == refs.Cskr ? cskrBytes : resource(id), refs.Cmdl, refs.Cskr);
        if (model.PositionCount < 3 || model.TriangleCount < 1 || model.PositionCount > MaxVertices
            || model.TriangleCount > MaxTriangles || model.Skin?.Count != model.PositionCount)
            throw new InvalidDataException("Incomplete or oversized skinned Retro source part: " + stem);

        var files = new List<string>();
        void Stage(string name)
        {
            string path = Path.Combine(staging, name);
            if (new FileInfo(path).Length is <= 0 or > MaxOutputBytes)
                throw new InvalidDataException("Source authoring part exceeds its output budget: " + stem);
            staged.Add((path, Path.Combine(root, name)));
            files.Add(name);
        }

        string obj = stem + ".obj";
        using (var writer = new StreamWriter(Path.Combine(staging, obj), false,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 65536))
            WriteObj(model, writer);
        Stage(obj);

        var bones = skeleton.Bones.Values.OrderBy(b => b.Id)
            .Select(b => new { b.Id, b.Parent, b.Name, Position = new[] { b.X, b.Y, b.Z } }).ToArray();
        string skeletonName = stem + ".skeleton.json";
        object description = part.Id == "body" ? new
        {
            spec.Id, SourceGameId = gameId, Refs = refs, SourceCharacter = part.NativeName,
            BoneCount = skeleton.Bones.Count, Bones = bones,
            Notes = "Local preview: needs rig retargeting, animation, materials, gun and alt form before gameplay."
        } : new
        {
            spec.Id, SourceGameId = gameId, Refs = refs, SourceCharacter = part.NativeName,
            BoneCount = skeleton.Bones.Count, Bones = bones,
            Notes = "Local alternate-form authoring preview; source materials, runtime animation, morph transitions and gameplay are unqualified.",
            SourcePart = part.Id
        };
        File.WriteAllText(Path.Combine(staging, skeletonName), JsonSerializer.Serialize(description));
        Stage(skeletonName);

        // Sparse samples and raw graph proof are independently bound to this
        // part's exact ANCS. They do not imply full animation acceptance.
        RetroMotionPreview.Inventory motion = RetroMotionPreview.FromLocalSource(
            ancsBytes, resource, spec.Game == ExternalGameKind.Echoes);
        var graphResult = RetroAnimationGraphProof.CaptureOptional(ancsBytes, gameId, part.Ancs,
            spec.Id, part.NativeName, refs, sourcePart: part.Id == "body" ? null : part.Id);
        if (graphResult.Graph is { } graph)
        {
            string graphName = stem + ".ancs-graph.json", rawName = graph.Metadata.SourceSnapshotFile;
            File.WriteAllBytes(Path.Combine(staging, graphName), graph.Descriptor);
            File.WriteAllBytes(Path.Combine(staging, rawName), graph.SourceSnapshot);
            Stage(graphName); Stage(rawName);
            if (spec.Game == ExternalGameKind.Echoes)
            {
                string cinfName = stem + ".cinf.bin", animationName = stem + ".animations.bin";
                File.WriteAllBytes(Path.Combine(staging, cinfName), cinfBytes); Stage(cinfName);
                byte[] archive = RetroCharacterAnimationArchive.Build(graph, cinfBytes, echoes: true, resource, cancellation);
                File.WriteAllBytes(Path.Combine(staging, animationName), archive); Stage(animationName);
                string geometryName = stem + ".source.glb", provenanceName = stem + ".source.json";
                byte[] geometry = RetroCharacterSourceGlbWriter.Write(model, skeleton, cancellation);
                File.WriteAllBytes(Path.Combine(staging, geometryName), geometry); Stage(geometryName);
                byte[] provenance = GeneratedSourceProvenance(spec, part, refs, gameId, cmdlBytes, cskrBytes, cinfBytes,
                    ancsBytes, archive, geometry, model, skeleton);
                File.WriteAllBytes(Path.Combine(staging, provenanceName), provenance); Stage(provenanceName);
                if (spec.Id == "darksamus-echoes" && part.Id == "body")
                {
                    File.WriteAllBytes(Path.Combine(staging, "characters.json"), JsonSerializer.SerializeToUtf8Bytes(new
                    {
                        format = 1, id = "generated-darksamus-echoes-diagnostic-v1", models = new[] { new
                        {
                            hunter = "Samus", part = "Biped", lod = 0, skinning = "Weighted4", poseSource = "SourceRigPreview",
                            model = geometryName, characterId = "darksamus", skinSource = "MP2DarkSamus",
                            provenance = provenanceName, provenanceSha256 = RetroAnimationGraphProof.Sha(provenance),
                            sourceAnimationArchive = animationName, sourceAnimationArchiveSha256 = RetroAnimationGraphProof.Sha(archive),
                            boneMap = new Dictionary<string, string>()
                        } }
                    }));
                    Stage("characters.json");
                }
            }
            motion = motion with { SourceGraphProof = new(1, graphName,
                RetroAnimationGraphProof.Sha(graph.Descriptor), rawName, part.Ancs,
                graph.Metadata.SourceAncsSha256, graph.Metadata.SourceAncsBytes) };
        }
        else
        {
            if (spec.Game == ExternalGameKind.Echoes) throw new InvalidDataException("Required Echoes animation source set is absent.");
            motion = motion with { SourceGraphAbsence = "animation-set-absent" };
        }
        string motionName = stem + ".motions.json";
        File.WriteAllText(Path.Combine(staging, motionName), JsonSerializer.Serialize(motion));
        if (new FileInfo(Path.Combine(staging, motionName)).Length > RetroMotionPreview.MaxAnimationResourceBytes)
            throw new InvalidDataException("Motion preview exceeds bounded sidecar output.");
        Stage(motionName);

        string weightsName = stem + ".weights.json";
        using (FileStream output = new(Path.Combine(staging, weightsName), FileMode.CreateNew,
            FileAccess.Write, FileShare.None, 65536))
            RetroCharacterSkinWeights.Write(model, output);
        Stage(weightsName);
        AuthoringComponent[] required = RequiredPartComponents(spec.Id, part.Id);
        return new(part.Id, part.Version, part.Ancs, part.NativeName, refs, required,
            files.Except(required.Select(c => c.File), StringComparer.Ordinal).ToArray());
    }
    private static byte[] GeneratedSourceProvenance(SourceCharacter spec, SourcePart part, RetroCharacterRefs refs,
        string gameId, byte[] cmdl, byte[] cskr, byte[] cinf, byte[] ancs, byte[] archive, byte[] geometry,
        Cmdl model, RetroNativeSkeleton skeleton)
    {
        string Sha(byte[] value) => Convert.ToHexStringLower(SHA256.HashData(value));
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            format = 2, kind = "generated-retro-source-rig-preview-v1", writerVersion = RetroCharacterSourceGlbWriter.Version,
            materialPolicy = RetroCharacterSourceGlbWriter.MaterialPolicy, sourceBasis = "(-x,z,y)",
            sourceCharacter = spec.Id, sourceNativeName = part.NativeName, sourceGameId = gameId, sourceCharacterId = spec.Id, sourcePart = part.Id,
            characterId = "darksamus", skinSource = spec.Id == "darksamus-echoes" ? "MP2DarkSamus" : "MP2DarkSamusFinal",
            nativeHunter = "Samus", presentationPart = part.Id == "body" ? "biped" : "alternateForm", visualOnly = true,
            approvedForGameplay = false, multiplayerEnabled = false, playerSelectionEnabled = false,
            firstPersonVerified = false, alternateFormVerified = false, sourceMotionRetargetVerified = false, devicePerformanceVerified = false,
            sourceAncs = spec.AlternateForm != null && part.Id == "ball" ? part.Ancs : spec.Ancs,
            sourceCmdl = refs.Cmdl, sourceCskr = refs.Cskr, sourceCinf = refs.Cinf,
            sourceAncsSha256 = Sha(ancs), sourceCmdlSha256 = Sha(cmdl), sourceCskrSha256 = Sha(cskr), sourceCinfSha256 = Sha(cinf),
            sourceAnimationArchiveSha256 = Sha(archive), sourceGlbSha256 = Sha(geometry),
            sourcePositionCount = model.PositionCount, sourceTriangleCount = model.TriangleCount,
            sourceMaterialCount = model.Materials.Length, sourceBoneCount = skeleton.Bones.Count,
            sourceMaterialTriangleCounts = Enumerable.Range(0, model.Materials.Length).Select(m => model.TriangleMaterial.Count(v => v == m)).ToArray()
        });
    }

}
