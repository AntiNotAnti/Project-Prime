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
        string materialId = captured.Materials.Archive.Binding.CharacterId;
        string path = ContainedPath(_sourceRoot!, (materialId == CharacterGeneratedNormalBallProfile.MaterialId
            ? CharacterGeneratedNormalBallProfile.Stem : materialId == CharacterGeneratedFinalBallProfile.MaterialId
                ? CharacterGeneratedFinalBallProfile.Stem : materialId) + ".materials.bin");
        if (path != captured.Path || CharacterSourceProofIO.HashFile(path, RetroCharacterMaterialArchive.MaxArchiveBytes) != captured.Materials.Archive.Sha256)
            throw new InvalidDataException("Captured source material generation changed.");
    }

    internal void CaptureSourceBallMaterials(CharacterModelAsset asset)
    {
        if (_sourceRoot == null || !HasSourceBall(asset) || !_sourceProofs.TryGetValue(asset,out var proof))
            throw new InvalidDataException("Exact original normal ball owner required for its own material capture.");
        ValidateAtGeneration(asset,proof.ModelSha,_sourceGeneration);
        bool final=asset.PrivateAppearance==new PrivateCharacterAppearance("darksamus","MP2DarkSamusFinal");
        string path=ContainedPath(_sourceRoot,(final?CharacterGeneratedFinalBallProfile.Stem:CharacterGeneratedNormalBallProfile.Stem)+".materials.bin");
        var materials=CharacterSourceGxMaterials.Read(CharacterSourceProofIO.ReadFile(path,RetroCharacterMaterialArchive.MaxArchiveBytes),
            proof.ModelSha,final?CharacterGeneratedFinalBallProfile.MaterialId:CharacterGeneratedNormalBallProfile.MaterialId);
        if(final)
        {
            if(!TryResolvePrivate(Hunter.Samus,asset.PrivateAppearance!.Value,CharacterModelPart.Biped,0,out var body)
                ||!_sourceMaterials.TryGetValue(body,out var bodyMaterials))throw new InvalidDataException("Own original Final body material capture required before composite.");
            materials=materials.ForFinalBall(bodyMaterials.Materials);
        }
        ValidateAtGeneration(asset,proof.ModelSha,_sourceGeneration);
        lock(_sourceProofLock)
        {
            if (_sourceMaterials.TryGetValue(asset,out var old))
            {
                if(old.Path!=path || old.Materials.Archive.Sha256!=materials.Archive.Sha256)
                    throw new InvalidDataException("Captured normal ball materials changed.");
                return;
            }
            _sourceMaterials.Add(asset,new(path,materials));_sourceGeneration++;
        }
    }

    // A qualified generated cannon is a whole-triangle cut of this exact body,
    // not a second CMDL/material edition. Publish both before issuing live leases.
    internal void CaptureGeneratedCannonMaterials(CharacterModelAsset body, CharacterModelAsset cannon)
    {
        if (!_sourceMaterials.TryGetValue(body, out var captured)
            || !_sourceProofs.TryGetValue(body, out var bodyProof)
            || !_sourceProofs.TryGetValue(cannon, out var cannonProof)
            || body.Part != CharacterModelPart.Biped || cannon.Part != CharacterModelPart.ViewModel
            || cannon.PrivateAppearance != body.PrivateAppearance || cannon.ModelPath != body.ModelPath
            || cannon.Hunter != body.Hunter || cannonProof.SourceCharacter != bodyProof.SourceCharacter
            || cannonProof.ModelSha != bodyProof.ModelSha
            || !_sourceCannons.TryGetValue(cannon, out var recipe) || !recipe.IsGenerated)
            throw new InvalidDataException("Same-generation generated body/cannon material authority required.");
        bool qualified = recipe.GeneratedTrooper is { } profile
            ? body.Hunter == Hunter.Weavel && body.PrivateAppearance == new PrivateCharacterAppearance("trooper", profile.Skin)
                && bodyProof.SourceCharacter == profile.Id && bodyProof.ModelSha == profile.ModelSha
                && captured.Materials.Archive.Sha256 == profile.MaterialSha
            : recipe.GeneratedFinal ? body.Hunter == Hunter.Samus && body.PrivateAppearance == new PrivateCharacterAppearance("darksamus","MP2DarkSamusFinal")
                && bodyProof.SourceCharacter == "darksamus-overcharged-echoes" && bodyProof.ModelSha == GeneratedFinalSourceGlbSha256
                && captured.Materials.Archive.Sha256 == "69d27a8e4867b4ec50084260a6fb570936e79edb7b08ad2bbd04a58d5204df8d"
            : body.Hunter == Hunter.Samus && body.PrivateAppearance == CharacterSourcePreviewSelection.Appearance
                && bodyProof.SourceCharacter == "darksamus-echoes" && bodyProof.ModelSha == GeneratedNormalSourceGlbSha256;
        if (!qualified) throw new InvalidDataException("Cannon must use its own qualified source body's material archive.");
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
