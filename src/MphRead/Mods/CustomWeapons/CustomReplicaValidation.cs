using System;
using MphRead.Mods.Network;

namespace MphRead.Mods.CustomWeapons;

// Value-only projection validation. No recipient, readiness packet or transport
// identity is constructed when historical authority facts use these primitives.
internal readonly struct CustomReplicaValidation
{
    internal MatchStartIdentity Start { get; }
    internal WeaponDefinition Definition { get; }
    internal CustomWeaponManifest Manifest { get; }

    internal CustomReplicaValidation(MatchStartIdentity start, WeaponDefinition definition, CustomWeaponManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(manifest);
        if (start.MatchId == 0 || start.AuthorityEpoch == 0 || start.StartGeneration == 0
            || manifest.Rows.Count != 1 || manifest.Rows[0].RuntimeId.Value != 10
            || !MatchesDefinition(manifest.Rows[0], definition) || !manifest.Digest.IsValid)
            throw new ArgumentException("An exact single-definition manifest and complete match start are required.");
        Start = start; Definition = definition; Manifest = manifest;
    }

    private static bool MatchesDefinition(CustomManifestRow row, WeaponDefinition definition)
    {
        if (row.ContentId != definition.Identity.ContentId || definition.Identity.DefinitionHash is not { Length: 64 } hash)
            return false;
        Span<byte> digest = stackalloc byte[32]; row.Mechanics.Write(digest);
        const string hex = "0123456789abcdef";
        for (int i = 0; i < digest.Length; i++)
            if (hash[i * 2] != hex[digest[i] >> 4] || hash[i * 2 + 1] != hex[digest[i] & 15]) return false;
        return true;
    }
}
