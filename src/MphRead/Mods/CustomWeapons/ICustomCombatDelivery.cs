namespace MphRead.Mods.CustomWeapons;

// Authority-thread owner of pre-effect, bounded result obligations. No packet,
// claimed health value or ordinary native receipt implements this dependency.
internal interface ICustomCombatDelivery
{
    bool HasActionReservation(in CustomWeaponAction action);
    bool HasContactReservation(in CustomWeaponAction action);
    bool Publish(in CustomActionReceipt receipt);
    bool Publish(in CustomContactOutcome outcome);
    bool HasControlWitness(in CustomWeaponAction action);
    void SourceObserved(CustomAuthorityCombatAdapter root,in CustomSourceInput input);
    void AfterSourcePhase(CustomAuthorityCombatAdapter root);
}
