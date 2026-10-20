using MphRead.Mods.Network;

namespace MphRead.Mods.CustomWeapons;

internal readonly record struct CustomGrantFunding(ulong Sequence,PowerUpSpawnKey Spawn,
    uint ContactWorld,uint ContactSource,uint ExpiresWorld);

// Captured with the first engine source observation, never resolved from an
// owner's damage hint or from current power-up state during delayed admission.
internal readonly record struct CustomSourceModifierEvidence(WeaponOwnerContext Owner,uint SourceFrame,
    uint ObservedWorldFrame,uint Room,TimedGrantKnowledge DoubleKnowledge,bool PrimeAbsent,CustomGrantFunding? Funding)
{
    private static bool Before(uint a,uint b) => unchecked((int)(a-b)) < 0;
    internal static CustomSourceModifierEvidence Capture(WeaponOwnerContext owner,uint source,uint world,
        bool canonicalPrimeAbsent,NetTimedGrants? ledger)
    {
        var result = new CustomSourceModifierEvidence(owner,source,world,ledger?.Room ?? 0,
            TimedGrantKnowledge.Unavailable,canonicalPrimeAbsent,null);
        if (!owner.IsValid || ledger == null || ledger.Room == 0
            || !ledger.TryReadObservation(owner,source,TimedPowerUp.DoubleDamage,out var answer,out uint observed)
            || observed != world) return result;
        if (answer.Knowledge == TimedGrantKnowledge.NotGranted)
            return result with { DoubleKnowledge = TimedGrantKnowledge.NotGranted };
        if (answer.Knowledge != TimedGrantKnowledge.Granted || !ledger.TryRecord(answer.Sequence,out var grant)
            || grant.Sequence != answer.Sequence || grant.Type != TimedPowerUp.DoubleDamage
            || grant.Life.AuthorityEpoch != owner.AuthorityEpoch || grant.Life.MatchId != owner.MatchId
            || grant.Life.ShooterSlot != owner.Slot || grant.Life.Generation != owner.Generation
            || grant.Life.LifeId != owner.Life || grant.Life.ShotId != 0
            || grant.Spawn.Epoch != owner.AuthorityEpoch || grant.Spawn.Match != owner.MatchId
            || grant.Spawn.Room != ledger.Room || grant.Spawn.Generation == 0
            || grant.SourceContactFrame is not uint contact
            || grant.ExpiresAtFrame != unchecked(grant.ContactFrame+NetTimedGrants.Duration)
            || Before(observed,grant.EffectiveFrame) || !Before(observed,grant.ExpiresAtFrame)
            || Before(source,unchecked(contact+1))) return result;
        return result with { DoubleKnowledge = TimedGrantKnowledge.Granted,
            Funding = new(grant.Sequence,grant.Spawn,grant.ContactFrame,contact,grant.ExpiresAtFrame) };
    }
    internal bool Matches(WeaponOwnerContext owner,uint source,uint observed)
        => Owner == owner && SourceFrame == source && ObservedWorldFrame == observed && Room != 0
            && PrimeAbsent && (DoubleKnowledge == TimedGrantKnowledge.NotGranted && Funding == null
                || DoubleKnowledge == TimedGrantKnowledge.Granted && Funding is { Sequence: > 0 });
}
