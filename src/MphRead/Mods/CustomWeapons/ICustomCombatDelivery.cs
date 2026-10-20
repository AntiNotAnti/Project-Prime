namespace MphRead.Mods.CustomWeapons;

// Authority-thread owner of pre-effect, bounded result obligations. No packet,
// claimed health value or ordinary native receipt implements this dependency.
internal interface ICustomCombatDelivery
{
    bool HasActionReservation(in CustomWeaponAction action);
    bool TryKnowledge(in CustomWeaponAction action,out CustomResourceKnowledge knowledge)
    {knowledge=default;return HasActionReservation(action);}
    // A reserved action still needs a current audience. A closed execution
    // barrier produces a terminal source denial, without advancing resources.
    bool MayExecute(in CustomWeaponAction action) => true;
    bool HasContactReservation(in CustomWeaponAction action);
    bool Publish(in CustomActionReceipt receipt);
    bool Publish(in CustomActionReceipt receipt,CustomResourceKnowledge knowledge)=>knowledge.IsEmpty&&Publish(receipt);
    bool Publish(in CustomContactOutcome outcome);
    bool HasControlWitness(in CustomWeaponAction action);
    bool TryPrepareAudience(in CustomWeaponAction action);
    bool HasPreparedAudience(in CustomWeaponAction action);
    bool PublishLaunch(in CustomReplicaLaunch launch);
    void SourceObserved(CustomAuthorityCombatAdapter root,in CustomSourceInput input);
    void AfterSourcePhase(CustomAuthorityCombatAdapter root);
    void SourcePhaseSealed(CustomAuthorityCombatAdapter root) { }
}
