using System;
using MphRead.Mods.Network;

namespace MphRead.Mods.CustomWeapons;

// Owner-only envelope. Canonical Actions also appear in public replica facts
// and must never acquire this private nonce or change their reviewed bytes.
internal readonly record struct CustomPrivateAction(CustomWeaponAction Action,CustomResourceKnowledge Knowledge)
{
    internal const int Size=CustomWeaponAction.Size+CustomResourceKnowledge.Size;
    internal bool IsValid=>Action.IsValid&&Knowledge.IsValid;
    internal void Write(Span<byte> bytes)
    {
        if(!IsValid||bytes.Length<Size)throw new ArgumentException("Valid private action and complete buffer required.");
        Action.Write(bytes);Knowledge.Write(bytes[CustomWeaponAction.Size..]);
    }
    internal static bool TryRead(ReadOnlySpan<byte> bytes,out CustomPrivateAction envelope)
    {
        envelope=default;
        if(bytes.Length!=Size||!CustomWeaponAction.TryRead(bytes[..CustomWeaponAction.Size],out var action)
            ||!CustomResourceKnowledge.TryRead(bytes[CustomWeaponAction.Size..],out var knowledge))return false;
        envelope=new(action,knowledge);return true;
    }
}

// Latched beside original native intent creation. Its scope prevents a later
// lifecycle or readiness transition from attaching old knowledge to a new input.
internal readonly record struct CustomCapturedKnowledge(WeaponOwnerContext Owner,WorldBootstrapIdentity Bootstrap,
    WeaponDigest Manifest,CustomResourceKnowledge Knowledge,uint SourceFrame)
{
    internal bool IsEmpty=>this==default;
    internal bool IsValid=>IsEmpty||Owner.IsValid&&CustomWeaponProtocol.ValidBootstrap(Bootstrap)&&Manifest.IsValid
        &&Knowledge.IsPresent&&Owner.MatchId==Bootstrap.Start.MatchId&&Owner.AuthorityEpoch==Bootstrap.Start.AuthorityEpoch
        &&Owner.Generation==Bootstrap.SlotGeneration;
}
