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
    PrivateCharacterAppearance Appearance, int ClipOrdinal, double Seconds, CharacterModelPart Part = CharacterModelPart.Biped, double MaterialSeconds = 0);

// CPU-only capture at one explicit Preview boundary. A retained generation is
// never switched underneath a playing clip; reopening rechecks discovery.
internal sealed class CharacterSourcePreviewSelection
{
    internal CharacterModelPack Pack { get; }
    internal string Generation { get; }
    internal IReadOnlyList<CharacterSourcePreviewClipChoice> Clips { get; }
    internal PrivateCharacterAppearance SelectedAppearance { get; }
    internal bool HasCannon { get; }
    internal static readonly PrivateCharacterAppearance Appearance = new("darksamus", "MP2DarkSamus");
    private CharacterSourcePreviewSelection(CharacterModelPack pack, string generation, CharacterSourcePreviewClipChoice[] clips, PrivateCharacterAppearance appearance, bool hasCannon)
        => (Pack, Generation, Clips, SelectedAppearance, HasCannon) = (pack, generation, Array.AsReadOnly(clips), appearance, hasCannon);

    internal static CharacterSourcePreviewSelection Load(string id, CancellationToken cancellation, string? root = null)
    {
        bool final = id == "darksamus-overcharged-echoes";
        if (!final && id != "darksamus-echoes") throw new NotSupportedException("Body source programs currently support normal and Overcharged Echoes. Other bundles remain available for authoring.");
        var appearance = final ? new PrivateCharacterAppearance("darksamus", "MP2DarkSamusFinal") : Appearance;
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
        if (!pack.TryResolvePrivate(Hunter.Samus, appearance, CharacterModelPart.Biped, 0, out var asset))
            throw new InvalidDataException("Generated source preview asset is unavailable.");
        pack.CaptureSourceMaterials(asset, id);
        if (!pack.TryCreateSourcePreviewLease(asset, out var lease) || lease.SourceAnimations == null)
            throw new InvalidDataException("Generated source preview owner is unavailable.");
        // This product viewer admits the generated importer profile only.
        // Legacy manual/private packs retain their separate acceptance route.
        using (var proof = JsonDocument.Parse(lease.ReadProvenanceSnapshot()))
            if (!proof.RootElement.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.Number
                || !format.TryGetInt32(out int version) || version != 2
                || !String.Equals(lease.ModelSha256, final ? CharacterModelPack.GeneratedFinalSourceGlbSha256 : CharacterModelPack.GeneratedNormalSourceGlbSha256, StringComparison.OrdinalIgnoreCase))
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
        // Normal keeps its separate fixed diagnostic cannon. Final has no
        // cannon capability and never borrows normal's cut or source binding.
        if (!final)
        {
            if (!pack.TryResolvePrivate(Hunter.Samus, appearance, CharacterModelPart.ViewModel, 0, out var cannon)
                || !pack.TryCreateSourcePreviewLease(cannon, out var cannonLease) || cannonLease.SourceCannon?.IsGenerated != true)
                throw new InvalidDataException("Update the import to include its qualified generated cannon recipe.");
            var cannonGeometry = CharacterWeightedModelLoader.LoadSourcePreview(cannon, cannonLease);
            _ = CharacterSourceClip.Load(cannonLease, cannon, cannonGeometry.SourceRig!, 32).Sample(0);
        }
        if (clips.Count == 0) throw new InvalidDataException("No qualified strict source clips are available.");
        lease.Require(asset); cancellation.ThrowIfCancellationRequested();
        return new(pack, state.Receipt.Generation, clips.ToArray(), appearance, !final);
    }
}
