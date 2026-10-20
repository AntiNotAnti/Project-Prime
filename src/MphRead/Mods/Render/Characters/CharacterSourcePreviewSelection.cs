using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Text.Json;
using MphRead.Mods.Import;

namespace MphRead.Mods.Render.Characters;

internal sealed record CharacterSourcePreviewClipChoice(int Ordinal, string Name, double Duration);
internal sealed record CharacterSourceStageRequest(object Owner, CharacterModelPack Pack,
    PrivateCharacterAppearance Appearance, int ClipOrdinal, double Seconds);

// CPU-only capture at one explicit Preview boundary. A retained generation is
// never switched underneath a playing clip; reopening rechecks discovery.
internal sealed class CharacterSourcePreviewSelection
{
    internal CharacterModelPack Pack { get; }
    internal string Generation { get; }
    internal IReadOnlyList<CharacterSourcePreviewClipChoice> Clips { get; }
    internal static readonly PrivateCharacterAppearance Appearance = new("darksamus", "MP2DarkSamus");
    private CharacterSourcePreviewSelection(CharacterModelPack pack, string generation, CharacterSourcePreviewClipChoice[] clips)
        => (Pack, Generation, Clips) = (pack, generation, Array.AsReadOnly(clips));

    internal static CharacterSourcePreviewSelection Load(string id, CancellationToken cancellation, string? root = null)
    {
        if (id != "darksamus-echoes") throw new NotSupportedException("Animated diagnostic preview currently supports normal Echoes Dark Samus. Other source bundles remain available for authoring.");
        cancellation.ThrowIfCancellationRequested();
        string importRoot = root ?? RetroCharacterPreviewExporter.DefaultRoot;
        var state = LocalImportBundle.Inspect(importRoot, id, RetroCharacterPreviewExporter.BundleSpec(id));
        if (state.State != ImportBundleState.Ready || state.Receipt == null)
            throw new InvalidDataException("Update the selected source bundle before previewing: " + state.State + ". " + state.Message);
        string manifest = state.Files.Single(f => Path.GetFileName(f) == "characters.json");
        string folder = Path.GetDirectoryName(manifest)!;
        if (Path.GetFileName(folder) != state.Receipt.Generation || state.Files.Any(f => Path.GetDirectoryName(f) != folder))
            throw new InvalidDataException("Source preview components must share one committed generation.");
        foreach (string file in state.Files) LocalImportBundle.CheckPath(file, importRoot);
        cancellation.ThrowIfCancellationRequested();
        var pack = CharacterModelPack.Load(folder);
        if (!pack.TryResolvePrivate(Hunter.Samus, Appearance, CharacterModelPart.Biped, 0, out var asset)
            || !pack.TryCreateSourcePreviewLease(asset, out var lease) || lease.SourceAnimations == null)
            throw new InvalidDataException("Generated source preview owner is unavailable.");
        // This product viewer admits the generated importer profile only.
        // Legacy manual/private packs retain their separate acceptance route.
        using (var proof = JsonDocument.Parse(lease.ReadProvenanceSnapshot()))
            if (!proof.RootElement.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.Number
                || !format.TryGetInt32(out int version) || version != 2
                || !String.Equals(lease.ModelSha256, CharacterModelPack.GeneratedNormalSourceGlbSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Animated import preview requires the qualified generated source geometry profile.");
        var geometry = CharacterWeightedModelLoader.LoadSourcePreview(asset, lease);
        var clips = new List<CharacterSourcePreviewClipChoice>();
        foreach (var rootClip in lease.SourceAnimations.Data.Roots.Where(r => r.CharacterDeclared && r.StrictPlay))
        {
            cancellation.ThrowIfCancellationRequested();
            var temporal = lease.SourceAnimations.Data.OpenStrictPrimitive(rootClip.Ordinal);
            if (!temporal.RigidScale) continue;
            // Exact generated hierarchy must bind the real source motion before
            // a worker result can be published to the render owner.
            var clip = CharacterSourceClip.Load(lease, asset, geometry.SourceRig!, rootClip.Ordinal);
            _ = clip.Sample(0);
            clips.Add(new(rootClip.Ordinal, rootClip.Name, temporal.Duration));
        }
        if (clips.Count == 0) throw new InvalidDataException("No qualified strict source clips are available.");
        lease.Require(asset); cancellation.ThrowIfCancellationRequested();
        return new(pack, state.Receipt.Generation, clips.ToArray());
    }
}
