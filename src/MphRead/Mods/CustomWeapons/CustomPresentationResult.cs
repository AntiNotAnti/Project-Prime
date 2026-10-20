using MphRead.Mods.Network;

namespace MphRead.Mods.CustomWeapons;

// Trusted current-life control feedback only. This is never a damage,
// ammunition, selection authority or replay fact.
internal readonly record struct CustomPresentationResult(WorldBootstrapIdentity Bootstrap,WeaponDigest Manifest,
    WeaponOwnerContext Owner,uint Sequence,CustomWeaponActionKind Kind,WeaponActionResult Result,bool Accepted);
