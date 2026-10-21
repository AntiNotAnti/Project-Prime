using System;
using System.Collections.Generic;
using System.IO;
using MphRead.Mods.Import;

namespace MphRead.Mods.Render.Characters;

internal sealed partial class CharacterModelPack
{
    private sealed record CapturedMaterials(string Path, CharacterSourceGxMaterials Materials);
    private readonly Dictionary<CharacterModelAsset, CapturedMaterials> _sourceMaterials = new(ReferenceEqualityComparer.Instance);

    // Explicit worker/load boundary. Fixed importer names plus whole-file pins
    // avoid adding caller-controlled shader/material references to manifests.
    internal void CaptureSourceMaterials(CharacterModelAsset asset, string sourceCharacter)
    {
        if (_sourceRoot == null || !_sourceProofs.TryGetValue(asset, out var proof)
            || asset.Part != CharacterModelPart.Biped || asset.PoseSource != CharacterPoseSource.SourceRigPreview
            || proof.SourceCharacter != sourceCharacter)
            throw new InvalidDataException("Exact generated body owner required for source materials.");
        ValidateAtGeneration(asset, proof.ModelSha, _sourceGeneration);
        string path = ContainedPath(_sourceRoot, sourceCharacter + ".materials.bin");
        var materials = CharacterSourceGxMaterials.Read(CharacterSourceProofIO.ReadFile(path, RetroCharacterMaterialArchive.MaxArchiveBytes), proof.ModelSha, sourceCharacter);
        ValidateAtGeneration(asset, proof.ModelSha, _sourceGeneration);
        if (_sourceMaterials.TryGetValue(asset, out var previous))
        {
            if (previous.Path != path || previous.Materials.Archive.Sha256 != materials.Archive.Sha256)
                throw new InvalidDataException("Source material capture identity changed.");
            return;
        }
        lock (_sourceProofLock)
        {
            _sourceMaterials.Add(asset, new(path, materials));
            // Adding source-program authority retires any earlier diagnostic
            // lease/cache entry belonging to this pack. No live owner silently
            // changes its material edition underneath an existing draw.
            _sourceGeneration++;
        }
    }
    private void RevalidateSourceMaterials(CharacterModelAsset asset)
    {
        if (!_sourceMaterials.TryGetValue(asset, out var captured)) return;
        string path = ContainedPath(_sourceRoot!, captured.Materials.Archive.Binding.CharacterId + ".materials.bin");
        if (path != captured.Path || CharacterSourceProofIO.HashFile(path, RetroCharacterMaterialArchive.MaxArchiveBytes) != captured.Materials.Archive.Sha256)
            throw new InvalidDataException("Captured source material generation changed.");
    }

    // The generated normal cannon is a whole-triangle cut of this exact body,
    // not a second CMDL/material edition. Publish both before issuing live leases.
    internal void CaptureGeneratedNormalCannonMaterials(CharacterModelAsset body, CharacterModelAsset cannon)
    {
        if (!_sourceMaterials.TryGetValue(body, out var captured)
            || !_sourceProofs.TryGetValue(body, out var bodyProof)
            || !_sourceProofs.TryGetValue(cannon, out var cannonProof)
            || body.Part != CharacterModelPart.Biped || cannon.Part != CharacterModelPart.ViewModel
            || body.PrivateAppearance != CharacterSourcePreviewSelection.Appearance
            || cannon.PrivateAppearance != body.PrivateAppearance || cannon.ModelPath != body.ModelPath
            || bodyProof.SourceCharacter != "darksamus-echoes" || cannonProof.SourceCharacter != bodyProof.SourceCharacter
            || bodyProof.ModelSha != GeneratedNormalSourceGlbSha256 || cannonProof.ModelSha != bodyProof.ModelSha
            || !_sourceCannons.TryGetValue(cannon, out var recipe) || !recipe.IsGenerated)
            throw new InvalidDataException("Same-generation generated normal body/cannon material authority required.");
        ValidateAtGeneration(body, bodyProof.ModelSha, _sourceGeneration);
        ValidateAtGeneration(cannon, cannonProof.ModelSha, _sourceGeneration);
        lock (_sourceProofLock)
        {
            if (_sourceMaterials.TryGetValue(cannon, out var previous))
            {
                if (!ReferenceEquals(previous, captured)) throw new InvalidDataException("Cannon material identity changed.");
                return;
            }
            _sourceMaterials.Add(cannon, captured);
            _sourceGeneration++;
        }
    }
}
