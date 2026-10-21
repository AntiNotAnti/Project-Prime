using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MphRead.Mods.Import.Retro;

namespace MphRead.Mods.Import;

public static partial class RetroCharacterPreviewExporter
{
    private static bool IsDrone(SourceCharacter source)
        => source.Id is "trooper-drone-alpha" or "trooper-drone-gamma";

    private static readonly IReadOnlyList<ExternalArchiveRequirement> DroneArchives = Array.AsReadOnly(new[]
    {
        new ExternalArchiveRequirement("Metroid3.pak", "30953a16e8820f7d1a5425084e04541a0439617a"),
        new ExternalArchiveRequirement("AudioGrp.pak", "157e0841155cc7b699176dd7afc746c7048dbc28")
    });

    public static IReadOnlyList<SourceCharacter> SelectSourcesForEdition(ExternalGameKind game, ExternalSourceEdition edition,
        IReadOnlyCollection<string>? selectedIds = null, bool sameSourceGroup = false)
    {
        _ = ExternalSourceRequirements.For(game, edition);
        var group = Sources.Where(s => s.Game == game).ToArray();
        var selected = group.Where(s => selectedIds == null || selectedIds.Contains(s.Id, StringComparer.Ordinal)).ToArray();
        if (selected.Length == 0 || selectedIds != null && (selectedIds.Count != selected.Length
            || selectedIds.Distinct(StringComparer.Ordinal).Count() != selectedIds.Count)
            || sameSourceGroup && (selectedIds == null || selected.Length != group.Length))
            throw new InvalidDataException("Choose supported character previews from this source edition.");
        // A group means every supported appearance on the chosen disc. An
        // explicitly requested unsupported drone still refuses, never silently
        // becoming a different bundle or an empty successful import.
        if (selectedIds == null || sameSourceGroup)
            selected = selected.Where(s => !IsDrone(s) || edition == ExternalSourceEdition.PrimeUsaRevision2).ToArray();
        foreach (var source in selected) _ = RequiredSourceArchives(source.Id, edition);
        return Array.AsReadOnly(selected);
    }

    // Requirements belong to an independent bundle. Extending the game's
    // global recipe would change every previously qualified Trooper identity.
    public static IReadOnlyList<ExternalArchiveRequirement> RequiredSourceArchives(string id, ExternalSourceEdition edition)
    {
        SourceCharacter source = Source(id);
        if (!IsDrone(source)) return ExternalSourceRequirements.For(source.Game, edition);
        if (edition != ExternalSourceEdition.PrimeUsaRevision2)
            throw new NotSupportedException("Scout Drone authoring requires the pinned Prime USA revision2 source.");
        return DroneArchives;
    }

    private static byte[] DroneAuthoringProvenance(SourceCharacter source, RetroCharacterRefs refs,
        string gameId, byte[] cmdl, byte[] cskr, byte[] cinf, byte[] ancs, byte[] graph, byte[] archive,
        byte[] geometry, Cmdl model, RetroNativeSkeleton skeleton)
    {
        string Sha(byte[] bytes) => RetroAnimationGraphProof.Sha(bytes);
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            format = 1, kind = "generated-retro-drone-authoring-v1", writerVersion = RetroCharacterSourceGlbWriter.Version,
            materialPolicy = RetroCharacterSourceGlbWriter.MaterialPolicy, sourceBasis = "(-x,z,y)",
            sourceCharacterId = source.Id, sourceNativeName = source.NativeName, sourceGameId = gameId, sourcePart = "drone",
            sourceEdition = "prime-usa-rev2", sourceFingerprint = BundleSpec(source.Id).SourceFingerprint,
            sourceRootContract = "prime-drone-root3-parent2-v1", sourceRootBoneId = 3, sourceExternalParentId = 2,
            runtimeInstall = false, visualOnly = true,
            sourceAncs = source.Ancs, sourceCmdl = refs.Cmdl, sourceCskr = refs.Cskr, sourceCinf = refs.Cinf,
            sourceAncsSha256 = Sha(ancs), sourceCmdlSha256 = Sha(cmdl), sourceCskrSha256 = Sha(cskr), sourceCinfSha256 = Sha(cinf),
            sourceGraphSha256 = Sha(graph), sourceAnimationArchiveSha256 = Sha(archive), sourceGlbSha256 = Sha(geometry),
            sourcePositionCount = model.PositionCount, sourceTriangleCount = model.TriangleCount,
            sourceMaterialCount = model.Materials.Length, sourceBoneCount = skeleton.Bones.Count,
            sourceMotionPolicy = "original-strict-bank-root-pinning-required-at-runtime",
            unqualifiedParts = new[] { "source-material-rendering", "runtime-animation-and-gameplay", "selection-network-replay", "first-person", "drone-abilities" }
        });
    }

    private static byte[] DroneManifest(SourceCharacter source, string geometry, string provenance, string animation,
        byte[] provenanceBytes, byte[] animationBytes)
        => JsonSerializer.SerializeToUtf8Bytes(new
        {
            format = 1, id = "generated-" + source.Id + "-source-v1", models = new[]
            {
                new
                {
                    hunter = "Weavel", part = "AlternateForm", lod = 0, skinning = "Weighted4", poseSource = "SourceRigPreview",
                    model = geometry, characterId = "trooper", skinSource = source.Id == "trooper-drone-alpha" ? "MP1AlphaDrone" : "MP1GammaDrone",
                    provenance, provenanceSha256 = RetroAnimationGraphProof.Sha(provenanceBytes),
                    sourceAnimationArchive = animation, sourceAnimationArchiveSha256 = RetroAnimationGraphProof.Sha(animationBytes),
                    boneMap = new Dictionary<string, string>()
                }
            }
        });
}
