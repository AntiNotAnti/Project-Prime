using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;

// Pure cosmetic state. Results supplied here are explicit trusted fixtures;
// production's bounded result queue owns exact-once delivery/deduplication.
// These checks do not establish source admission, resource payment or audio/GL.
internal static class CustomFeedbackChecks
{
    private static readonly WeaponOwnerContext Owner=new(7,0xfedcba9876543210UL,2,9,12);
    private static readonly WorldBootstrapIdentity Bootstrap=new(new(7,Owner.AuthorityEpoch,3),4,9,100,5,6);
    private static readonly WeaponDigest Manifest=WeaponDigest.FromHex(new string('a',64));
    private static CustomInputProjection Identity()=>new(Owner,true,1,0,0,0,Bootstrap,Manifest);
    private static CustomPresentationResult Result(uint sequence,WeaponActionResult result=WeaponActionResult.Accepted,
        CustomWeaponActionKind kind=CustomWeaponActionKind.Fire)
        =>new(Bootstrap,Manifest,Owner,sequence,kind,result,result==WeaponActionResult.Accepted);
    internal static void Run(Action<bool,string> check)
    { Prediction(check);FixedClock(check);Corrections(check);EmptyAndRefusal(check);CosmeticCancellation(check);IdentityBoundaries(check);Allocation(check); }

    private static void Prediction(Action<bool,string> check)
    {
        var feedback=new CustomWeaponFeedback();
        feedback.Submitted(CustomWeaponActionKind.Fire,1);
        check(feedback.View==default,"feedback cannot predict a fire without current presentation identity");
        var canonical=new WeaponRuntimeState(RiftlineM7.Create(),Owner,100);
        canonical.SetEquipped(100,true);var resources=canonical.Snapshot();
        feedback.Synchronize(Identity());feedback.Submitted(CustomWeaponActionKind.Fire,1);
        check(feedback.View is {FirePulse:>0,Empty:0,Denied:0},"submitted fire creates cosmetic pulse without a receipt or ammunition prediction");
        feedback.Step();var before=feedback.View;
        check(!feedback.Apply(Result(1))&&feedback.View==before,
            "accepted fire result preserves elapsed prediction rather than replaying or extending it");
        foreach(var kind in new[]{CustomWeaponActionKind.Optic,CustomWeaponActionKind.Reload,CustomWeaponActionKind.Equip,CustomWeaponActionKind.Unequip})
            feedback.Submitted(kind,2);
        check(canonical.Snapshot()==resources,"feedback submission, ticking and accepted result leave canonical magazine/reserve/timing untouched");
        feedback.Synchronize(Identity() with{Optic=2,SettledSequence=2});
        check(feedback.View==before,"same identity resource/optic settlement does not replay or clear cosmetic feedback");
        feedback.Clear();check(feedback.View==default,"explicit clear removes pulse and status timers");
    }
    private static void FixedClock(Action<bool,string> check)
    {
        var feedback=new CustomWeaponFeedback();feedback.Synchronize(Identity());feedback.Submitted(CustomWeaponActionKind.Fire,1);
        var initial=feedback.View;bool stable=true;
        for(int i=0;i<10000;i++)stable&=feedback.View==initial;
        check(stable,"repeated render-time feedback reads never advance fixed-step pulse");
        feedback.Step();check(feedback.View.FirePulse==initial.FirePulse-1,"one simulation step advances the pulse exactly once");
        for(int i=0;i<100;i++)feedback.Step();
        check(feedback.View==default,"fixed-step feedback timers retire at zero without underflow");
    }
    private static void Corrections(Action<bool,string> check)
    {
        var feedback=new CustomWeaponFeedback();feedback.Synchronize(Identity());
        feedback.Submitted(CustomWeaponActionKind.Fire,1);feedback.Submitted(CustomWeaponActionKind.Fire,2);
        var newer=feedback.View;
        check(!feedback.Apply(Result(1,WeaponActionResult.Cooldown))&&feedback.View==newer,
            "late denial for older fire cannot cancel a newer physical fire pulse or replace its status");
        check(!feedback.Apply(Result(1,WeaponActionResult.InvalidLife,CustomWeaponActionKind.Reload))&&feedback.View==newer,
            "late nonfire denial cannot replace newer fire presentation");
        check(!feedback.Apply(Result(2,WeaponActionResult.Cooldown))&&feedback.View is {FirePulse:0,Empty:0,Denied:>0},
            "matching denied fire cancels predicted pulse and shows refusal without dry-fire cue");
        feedback.Submitted(CustomWeaponActionKind.Fire,3);var pulse=feedback.View.FirePulse;
        feedback.Submitted(CustomWeaponActionKind.Reload,4);
        check(!feedback.Apply(Result(4,WeaponActionResult.Full,CustomWeaponActionKind.Reload))&&feedback.View.FirePulse==pulse,
            "denied reload cannot cancel a separate fire pulse");
        feedback.Clear();feedback.Submitted(CustomWeaponActionKind.Fire,5);feedback.Submitted(CustomWeaponActionKind.Optic,6);
        check(!feedback.Apply(Result(5,WeaponActionResult.InvalidLife))&&feedback.View==default,
            "older matching fire denial still cancels its own pulse without replacing newer optic action feedback");
    }
    private static void EmptyAndRefusal(Action<bool,string> check)
    {
        var feedback=new CustomWeaponFeedback();feedback.Synchronize(Identity());feedback.Submitted(CustomWeaponActionKind.Fire,1);
        // Deliver once: CustomClientCombat's presentation queue deduplicates
        // results before this model, which intentionally has no replay ledger.
        check(feedback.Apply(Result(1,WeaponActionResult.Empty))&&feedback.View is {FirePulse:0,Empty:>0,Denied:0},
            "one exact current Empty delivery requests one dry-fire cue and visible empty status");
        var empty=feedback.View;bool stable=true;
        for(int i=0;i<1000;i++)stable&=feedback.View==empty;
        check(stable,"drawing visible Empty status does not replay cue or restart its timer");
        feedback.Step();check(feedback.View.Empty==empty.Empty-1,"empty feedback duration advances only on fixed steps");
        feedback.Submitted(CustomWeaponActionKind.Fire,2);
        check(feedback.View.Empty==0&&feedback.View.Denied==0&&feedback.View.FirePulse>0,
            "new physical action clears prior empty/refusal status before predicting new fire");
        var current=feedback.View;
        check(!feedback.Apply(Result(1,WeaponActionResult.Empty))&&feedback.View==current,
            "late older Empty cannot replay a dry cue or overwrite newer action feedback");
        foreach(var reason in new[]{WeaponActionResult.Cooldown,WeaponActionResult.CapacityUnavailable,WeaponActionResult.Reloading})
        {
            feedback.Submitted(CustomWeaponActionKind.Fire,3);
            check(!feedback.Apply(Result(3,reason))&&feedback.View is {Empty:0,Denied:>0},
                "nonempty refusal stays distinct from empty ammunition: "+reason);
        }
        feedback.Clear();feedback.Submitted(CustomWeaponActionKind.Reload,4);
        check(!feedback.Apply(Result(4,WeaponActionResult.Empty,CustomWeaponActionKind.Reload))&&feedback.View is {Empty:0,Denied:>0},
            "Empty enum on a nonfire result cannot produce a dry-fire cue");
        for(int i=0;i<100;i++)feedback.Step();check(feedback.View==default,"refusal feedback expires without render-time mutation");
    }
    private static void CosmeticCancellation(Action<bool,string> check)
    {
        var feedback=new CustomWeaponFeedback();feedback.Synchronize(Identity());
        feedback.Submitted(CustomWeaponActionKind.Fire,1);feedback.Submitted(CustomWeaponActionKind.Fire,2);
        feedback.CancelCosmetics();
        check(feedback.View==default&&!feedback.Apply(Result(1,WeaponActionResult.Empty))&&feedback.View==default,
            "overflow cosmetic cancellation clears visuals while retaining newer-action fence against old dry results");
        feedback.Submitted(CustomWeaponActionKind.Fire,3);
        check(feedback.View.FirePulse>0,"cosmetic cancellation preserves current identity for a fresh physical action");
    }
    private static void IdentityBoundaries(Action<bool,string> check)
    {
        var original=Identity();
        var replacements=new[]{original with{Owner=new(7,Owner.AuthorityEpoch,2,9,13)},
            original with{Owner=new(7,Owner.AuthorityEpoch,2,10,12)},
            original with{Bootstrap=Bootstrap with{Revision=2}},
            original with{Manifest=WeaponDigest.FromHex(new string('b',64))}};
        foreach(var replacement in replacements)
        {
            var feedback=new CustomWeaponFeedback();feedback.Synchronize(original);feedback.Submitted(CustomWeaponActionKind.Fire,1);
            feedback.Apply(Result(1,WeaponActionResult.Empty));feedback.Submitted(CustomWeaponActionKind.Fire,2);
            feedback.Synchronize(replacement);
            check(feedback.View==default&&!feedback.Apply(Result(2,WeaponActionResult.Empty))&&feedback.View==default,
                "owner/life, generation, bootstrap or manifest replacement retires old pulse and rejects old dry result");
        }
        var lost=new CustomWeaponFeedback();lost.Synchronize(original);lost.Submitted(CustomWeaponActionKind.Fire,1);lost.Synchronize(null);
        lost.Submitted(CustomWeaponActionKind.Fire,2);
        check(lost.View==default&&!lost.Apply(Result(1,WeaponActionResult.Empty)),"readiness loss clears feedback and refuses prediction/result until identity recovers");
        lost.Synchronize(original);lost.Submitted(CustomWeaponActionKind.Fire,3);
        check(lost.View.FirePulse>0,"readiness recovery permits fresh current-identity presentation");
    }
    private static void Allocation(Action<bool,string> check)
    {
        var feedback=new CustomWeaponFeedback();feedback.Synchronize(Identity());feedback.Submitted(CustomWeaponActionKind.Fire,1);
        int total=0;for(int i=0;i<1000;i++)total+=feedback.View.FirePulse;
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<100000;i++){var view=feedback.View;total+=view.FirePulse+view.Empty+view.Denied;}
        long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        check(total>0&&allocated==0,"100000 warmed read-only cosmetic view samples allocate zero bytes and retain live pulse");
    }
}
