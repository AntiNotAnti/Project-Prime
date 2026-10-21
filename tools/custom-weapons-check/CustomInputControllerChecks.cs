using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;

// Pure fixed-sample presentation/intent model. Sink acceptance and projections
// are explicit fixtures: no authority resource, input-device, network or HP proof.
internal static class CustomInputControllerChecks
{
    private static readonly WeaponOwnerContext Owner=new(7,0xfedcba9876543210UL,3,9,12);
    private readonly record struct Attempt(CustomWeaponActionKind Kind,byte Optic,uint Sequence,bool Accepted);
    private sealed class Sink : ICustomWeaponInputSink
    {
        internal readonly Attempt[] Trace=new Attempt[64];
        internal int Count,Accepted;
        internal uint Sequence;
        internal bool FailNext,Capture=true;
        public bool Submit(CustomWeaponActionKind kind,byte optic,out uint sequence)
        {
            bool accepted=!FailNext&&Sequence!=uint.MaxValue;FailNext=false;
            sequence=accepted?++Sequence:0;
            if(accepted)Accepted++;
            if(Capture)Trace[Count]=new(kind,optic,sequence,accepted);
            Count++;return accepted;
        }
    }
    private static CustomInputProjection Projection(bool selected=false,byte optic=0,uint settled=0,
        uint selectionDenied=0,uint opticDenied=0,WeaponOwnerContext? owner=null,WorldBootstrapIdentity bootstrap=default,WeaponDigest manifest=default)
        =>new(owner??Owner,selected,optic,settled,selectionDenied,opticDenied,bootstrap,manifest);
    private static CustomInputSample Sample(bool custom=false,bool native=false,bool shoot=false,bool reload=false,
        bool zoom=false,bool down=false,bool cycle=false,bool hold=false,bool allowed=true)
        =>new(allowed,custom,native,shoot,reload,zoom,down,cycle,hold);
    internal static void Run(Action<bool,string> check)
    {
        Selection(check);UncertainEquipFence(check);Denials(check);QueueRefusal(check);Scope(check);HeldScope(check);HeldRefusal(check);HoldReconciliation(check);
        OpticCountMatrix(check);Context(check);BootstrapBoundary(check);ManifestBoundary(check);ResourceBoundary(check);RenderSchedules(check);Allocation(check);
    }
    private static void OpticCountMatrix(Action<bool,string> check)
    {
        for(int count=1;count<=4;count++)
        {
            var controller=new CustomWeaponInputController();var sink=new Sink();controller.Synchronize(Projection(true,1),count);
            for(int step=1;step<=count*2;step++)
            {
                int before=sink.Count;byte expected=(byte)(step%count+1);controller.Sample(Sample(cycle:true),sink);
                check(controller.PresentationOptic==expected&&sink.Count==before+(count==1?0:1)
                    &&(count==1||sink.Trace[before].Kind==CustomWeaponActionKind.Optic&&sink.Trace[before].Optic==expected),"actual input sink cycles exactly selected optic count "+count+" step "+step);
                controller.Synchronize(Projection(true,expected,settled:sink.Sequence),count);
            }
            controller.Sample(Sample(zoom:true),sink);check(controller.PresentationOptic==0,"selected count scope toggle exits without changing ammo "+count);
            controller.Synchronize(Projection(true,0,settled:sink.Sequence),count);int unscoped=sink.Count;controller.Sample(Sample(cycle:true),sink);
            check(sink.Count==unscoped&&controller.PresentationOptic==0,"unscoped cycle changes preference without authority request "+count);
            controller.Sample(Sample(zoom:true),sink);check(controller.PresentationOptic==(count==1?1:2)&&sink.Trace[sink.Count-1].Optic==controller.PresentationOptic,"next scope submits exact bounded preferred optic to actual sink "+count);
        }
    }
    private static void Selection(Action<bool,string> check)
    {
        var controller=new CustomWeaponInputController();var sink=new Sink();
        controller.Sample(Sample(custom:true,shoot:true),sink);
        check(!controller.Available&&!controller.Selected&&!controller.SuppressNative&&sink.Count==0,
            "input controller cannot publish without canonical availability");
        controller.Synchronize(Projection());controller.Sample(Sample(custom:true),sink);
        check(controller.Available&&controller.Selected&&controller.SuppressNative&&controller.PresentationOptic==0
            &&sink.Trace[0]==new Attempt(CustomWeaponActionKind.Equip,0,1,true),
            "accepted equip predicts reversible custom selection and immediately suppresses native emission");
        controller.Sample(Sample(custom:true),sink);
        check(sink.Count==1,"selecting already predicted custom weapon does not duplicate equip requests");
        controller.Sample(Sample(native:true,shoot:true,reload:true,zoom:true),sink);
        check(!controller.Selected&&controller.SuppressNative&&sink.Count==2
            &&sink.Trace[1]==new Attempt(CustomWeaponActionKind.Unequip,0,2,true),
            "native selection predicts unequip but holds native suppression and cannot also fire/reload/scope");
        controller.Synchronize(Projection(true,settled:1));
        check(!controller.Selected&&controller.SuppressNative,
            "earlier equip settlement cannot erase newer pending unequip prediction");
        controller.Synchronize(Projection(false,settled:2));
        check(!controller.Selected&&!controller.SuppressNative&&controller.PresentationOptic==0,
            "only canonical unequip settlement releases ordinary native fallback");
        var simultaneous=new CustomWeaponInputController();var both=new Sink();simultaneous.Synchronize(Projection());
        simultaneous.Sample(Sample(custom:true,native:true,shoot:true),both);
        check(both.Count==0&&!simultaneous.Selected&&!simultaneous.SuppressNative,
            "native selection wins simultaneous native/custom input while already native");
        simultaneous.Synchronize(Projection(true,1));simultaneous.Sample(Sample(custom:true,native:true,shoot:true),both);
        check(both.Count==1&&both.Trace[0].Kind==CustomWeaponActionKind.Unequip&&!simultaneous.Selected
            &&simultaneous.SuppressNative,"explicit native fallback requests unequip even when fallback beam itself is unchanged");
    }
    private static void UncertainEquipFence(Action<bool,string> check)
    {
        var c=new CustomWeaponInputController();var s=new Sink();c.Synchronize(Projection());
        c.Sample(Sample(custom:true),s);c.Sample(Sample(native:true),s);
        c.Synchronize(Projection(selectionDenied:2));
        check(!c.Selected&&c.SuppressNative,
            "denied unequip with stale native state cannot erase unresolved earlier equip permission fence");
        c.Synchronize(null);c.Synchronize(Projection(selectionDenied:2));
        check(!c.Selected&&c.SuppressNative,
            "readiness recovery and repeated out-of-order unequip denial preserve unresolved equip suppression");
        c.Sample(Sample(custom:true),s);c.Synchronize(Projection(selectionDenied:3));
        check(!c.Selected&&c.SuppressNative,
            "denial of a newer equip cannot authorize native fire while an older equip outcome remains missing");
        c.Synchronize(Projection(true,settled:3));
        check(c.Selected&&c.SuppressNative,"fresh selected canonical state resolves missing outcomes while continuing native suppression");
        c.Sample(Sample(native:true),s);c.Synchronize(Projection(false,settled:4));
        check(!c.Selected&&!c.SuppressNative,"fresh canonical unequip releases suppression after all earlier equip outcomes settle");
        var denied=new CustomWeaponInputController();var exact=new Sink();denied.Synchronize(Projection());
        denied.Sample(Sample(custom:true),exact);denied.Sample(Sample(native:true),exact);
        denied.Synchronize(Projection(selectionDenied:2));denied.Synchronize(Projection(selectionDenied:1));
        check(!denied.Selected&&!denied.SuppressNative,
            "exact original equip denial can clear its fence after newer unequip denial without inventing acceptance");
        var optic=new CustomWeaponInputController();var scoped=new Sink();optic.Synchronize(Projection(true));
        optic.Sample(Sample(zoom:true),scoped);optic.Sample(Sample(native:true),scoped);
        optic.Synchronize(Projection(true,selectionDenied:2));
        check(optic.Selected&&optic.PresentationOptic==0,
            "native intent clears pending optic prediction so denied unequip cannot revive an older unconfirmed scope");
    }
    private static void Denials(Action<bool,string> check)
    {
        var c=new CustomWeaponInputController();var s=new Sink();c.Synchronize(Projection());c.Sample(Sample(custom:true),s);
        c.Synchronize(Projection(selectionDenied:1));
        check(!c.Selected&&!c.SuppressNative&&c.PresentationOptic==0,
            "exact equip denial reverses custom prediction without needing an advanced settled sequence");
        c.Sample(Sample(custom:true),s);c.Synchronize(Projection(true,settled:2));c.Sample(Sample(native:true),s);
        c.Synchronize(Projection(true,1,settled:2,selectionDenied:3));
        check(c.Selected&&c.SuppressNative&&c.PresentationOptic==1,
            "exact unequip denial restores canonical selected custom weapon and its optic");
        c.Sample(Sample(native:true),s);c.Synchronize(Projection(true,1,settled:2,selectionDenied:3));
        check(!c.Selected&&c.SuppressNative,"stale denial cannot cancel a later pending selection request");
        c.Synchronize(Projection(false,settled:4));
        check(!c.Selected&&!c.SuppressNative,"later successful unequip recovers after denied earlier selection");
        c.Synchronize(Projection(true,1,settled:4));c.Sample(Sample(zoom:true),s);
        check(c.PresentationOptic==0,"accepted optic toggle predicts unscoped presentation");
        c.Synchronize(Projection(true,1,settled:4,opticDenied:5));
        check(c.PresentationOptic==1&&c.Selected,"exact optic denial restores canonical optic without changing selection");
        c.Sample(Sample(reload:true),s);
        check(c.PresentationOptic==0&&s.Trace[s.Count-1].Kind==CustomWeaponActionKind.Reload,
            "queued reload predicts scope cancellation without changing resource state");
        c.Synchronize(Projection(true,1,settled:6));
        check(c.PresentationOptic==1,"canonical denied reload settlement restores the actual unchanged optic");
    }
    private static void QueueRefusal(Action<bool,string> check)
    {
        var c=new CustomWeaponInputController();var s=new Sink{FailNext=true};c.Synchronize(Projection());
        c.Sample(Sample(custom:true,shoot:true),s);
        check(!c.Selected&&!c.SuppressNative&&s.Count==1&&!s.Trace[0].Accepted&&s.Sequence==0,
            "refused equip does not predict custom selection or publish a following fire");
        c.Sample(Sample(custom:true),s);c.Synchronize(Projection(true,1,settled:1));s.FailNext=true;
        c.Sample(Sample(native:true,shoot:true),s);
        check(c.Selected&&c.SuppressNative&&c.PresentationOptic==1&&s.Count==3&&!s.Trace[2].Accepted,
            "refused unequip preserves canonical custom selection and never falls through to fire");
        s.FailNext=true;c.Sample(Sample(zoom:true,shoot:true),s);
        check(c.PresentationOptic==1&&s.Trace[3]==new Attempt(CustomWeaponActionKind.Optic,0,0,false)
            &&s.Trace[4]==new Attempt(CustomWeaponActionKind.Fire,1,2,true),
            "refused optic request keeps fire paired with the still-presented canonical optic");
        s.FailNext=true;c.Sample(Sample(reload:true),s);
        check(c.PresentationOptic==1&&!s.Trace[5].Accepted,
            "refused reload cannot predict scope cancellation");
    }
    private static void Scope(Action<bool,string> check)
    {
        var c=new CustomWeaponInputController();var s=new Sink();c.Synchronize(Projection(true));
        c.Sample(Sample(zoom:true,shoot:true),s);
        check(s.Count==2&&s.Trace[0]==new Attempt(CustomWeaponActionKind.Optic,1,1,true)
            &&s.Trace[1]==new Attempt(CustomWeaponActionKind.Fire,1,2,true)&&c.PresentationOptic==1,
            "physical scope press publishes optic before same-sample fire with matching predicted scope");
        c.Sample(Sample(cycle:true,shoot:true),s);
        check(s.Trace[2]==new Attempt(CustomWeaponActionKind.Optic,2,3,true)
            &&s.Trace[3]==new Attempt(CustomWeaponActionKind.Fire,2,4,true)&&c.PresentationOptic==2,
            "optic cycle publishes changed magnification before same-sample fire");
        c.Sample(Sample(zoom:true),s);
        check(c.PresentationOptic==0&&s.Trace[4].Optic==0,"toggle scope press leaves scoped presentation");
        c.Sample(Sample(zoom:true),s);
        check(c.PresentationOptic==2&&s.Trace[5].Optic==2,"scope toggle remembers the last selected magnification");
        c.Synchronize(Projection(true,2,settled:6));c.Sample(Sample(reload:true,shoot:true),s);
        check(s.Trace[6]==new Attempt(CustomWeaponActionKind.Reload,0,7,true)
            &&s.Trace[7]==new Attempt(CustomWeaponActionKind.Fire,0,8,true)&&c.PresentationOptic==0,
            "same-sample reload precedes fire and predicts optic cancellation while authority owns permission");
        var equip=new CustomWeaponInputController();var first=new Sink();equip.Synchronize(Projection());
        equip.Sample(Sample(custom:true,zoom:true,shoot:true),first);
        check(first.Count==3&&first.Trace[0].Kind==CustomWeaponActionKind.Equip
            &&first.Trace[1]==new Attempt(CustomWeaponActionKind.Optic,1,2,true)
            &&first.Trace[2]==new Attempt(CustomWeaponActionKind.Fire,1,3,true),
            "same-sample custom selection, scope and fire preserve exact equip-optic-fire publication order");
        var hip=new CustomWeaponInputController();var preference=new Sink();hip.Synchronize(Projection(true));
        hip.Sample(Sample(cycle:true),preference);
        check(preference.Count==0&&hip.PresentationOptic==0,"cycling while unscoped changes only a local preferred optic");
        hip.Sample(Sample(zoom:true),preference);
        check(preference.Count==1&&preference.Trace[0].Optic==2,"next scope press uses unscoped local magnification preference");
    }
    private static void HeldScope(Action<bool,string> check)
    {
        var c=new CustomWeaponInputController();var s=new Sink();c.Synchronize(Projection(true));
        c.Sample(Sample(down:true,hold:true,shoot:true),s);
        check(s.Trace[0]==new Attempt(CustomWeaponActionKind.Optic,1,1,true)
            &&s.Trace[1]==new Attempt(CustomWeaponActionKind.Fire,1,2,true),
            "held-scope entry publishes optic before a simultaneous physical fire edge");
        for(int i=0;i<30;i++)c.Sample(Sample(down:true,hold:true),s);
        check(s.Count==2&&c.PresentationOptic==1,"physical held scope and held shooting without a new press do not repeat requests");
        c.Sample(Sample(down:true,hold:true,cycle:true,shoot:true),s);
        check(s.Trace[2]==new Attempt(CustomWeaponActionKind.Optic,2,3,true)
            &&s.Trace[3]==new Attempt(CustomWeaponActionKind.Fire,2,4,true)&&c.PresentationOptic==2,
            "held-scope magnification cycle changes optic before same-sample fire");
        c.Sample(Sample(hold:true),s);
        check(c.PresentationOptic==0&&s.Trace[4]==new Attempt(CustomWeaponActionKind.Optic,0,5,true),
            "releasing held scope requests exact optic zero once");
        c.Sample(Sample(down:true,hold:true),s);
        check(c.PresentationOptic==2&&s.Trace[5].Optic==2,"holding scope again preserves selected magnification");
    }
    private static void HeldRefusal(Action<bool,string> check)
    {
        var c=new CustomWeaponInputController();var s=new Sink();c.Synchronize(Projection(true));
        c.Sample(Sample(down:true,hold:true),s);c.Synchronize(Projection(true,settled:1,opticDenied:1));
        for(int i=0;i<30;i++)c.Sample(Sample(down:true,hold:true),s);
        check(s.Count==1&&c.PresentationOptic==0,
            "denied held-scope request stays denied while physical button remains held");
        c.Sample(Sample(hold:true),s);
        check(s.Count==1,"releasing denied unscoped hold publishes no redundant optic zero");
        c.Sample(Sample(down:true,hold:true),s);
        check(s.Count==2&&s.Trace[1]==new Attempt(CustomWeaponActionKind.Optic,1,2,true),
            "fresh scope press after release can retry a previously denied held-scope intent");
        var failed=new CustomWeaponInputController();var queue=new Sink{FailNext=true};failed.Synchronize(Projection(true));
        failed.Sample(Sample(down:true,hold:true),queue);
        for(int i=0;i<30;i++)failed.Sample(Sample(down:true,hold:true),queue);
        check(queue.Count==1&&!queue.Trace[0].Accepted&&failed.PresentationOptic==0,
            "failed held-scope enqueue cannot resubmit every simulation frame");
        failed.Sample(Sample(hold:true),queue);failed.Sample(Sample(down:true,hold:true),queue);
        check(queue.Count==2&&queue.Trace[1].Sequence==1&&failed.PresentationOptic==1,
            "physical repress retries failed hold without a controller-owned sequence burn");
        var recovered=new CustomWeaponInputController();var unavailable=new Sink();recovered.Synchronize(Projection(true));
        recovered.Synchronize(null);recovered.Sample(Sample(down:true,hold:true),unavailable);
        recovered.Synchronize(Projection(true));recovered.Sample(Sample(down:true,hold:true),unavailable);
        check(unavailable.Count==0&&recovered.PresentationOptic==0,
            "hold pressed while context unavailable cannot synthesize a new edge on same-owner recovery");
        recovered.Sample(Sample(hold:true),unavailable);recovered.Sample(Sample(down:true,hold:true,allowed:false),unavailable);
        recovered.Sample(Sample(down:true,hold:true),unavailable);
        check(unavailable.Count==0,"engine-disallowed hold edge is not deferred until a later favorable sample");
    }
    private static void HoldReconciliation(Action<bool,string> check)
    {
        var preheld=new CustomWeaponInputController();var equip=new Sink();preheld.Synchronize(Projection());
        preheld.Sample(Sample(down:true,hold:true),equip);
        check(equip.Count==0,"holding Zoom while native cannot publish custom scope");
        preheld.Sample(Sample(custom:true,down:true,hold:true,shoot:true),equip);
        check(equip.Count==3&&equip.Trace[0].Kind==CustomWeaponActionKind.Equip
            &&equip.Trace[1]==new Attempt(CustomWeaponActionKind.Optic,1,2,true)
            &&equip.Trace[2]==new Attempt(CustomWeaponActionKind.Fire,1,3,true),
            "successful Equip with preheld Zoom reconciles scope once before physical fire");
        preheld.Synchronize(Projection(true,settled:3,opticDenied:2));
        for(int i=0;i<30;i++)preheld.Sample(Sample(down:true,hold:true),equip);
        check(equip.Count==3&&preheld.PresentationOptic==0,
            "denied preheld Equip scope cannot retry or refire without a new epoch");

        foreach(bool unavailable in new[]{false,true})
        {
            var c=new CustomWeaponInputController();var sink=new Sink();c.Synchronize(Projection(true));
            c.Sample(Sample(down:true,hold:true),sink);c.Synchronize(Projection(true,1,settled:1));
            if(unavailable)c.Synchronize(null);
            c.Sample(Sample(hold:true,allowed:false),sink);
            c.Sample(Sample(hold:true,allowed:false),sink);
            check(sink.Count==1&&c.PresentationOptic==1,
                "blocked held-scope release records intent without publishing: unavailable="+unavailable);
            if(unavailable)c.Synchronize(Projection(true,1,settled:1));
            c.Sample(Sample(hold:true),sink);
            check(sink.Count==2&&sink.Trace[1]==new Attempt(CustomWeaponActionKind.Optic,0,2,true),
                "first allowed sample reconciles remembered scope release exactly once: unavailable="+unavailable);
            c.Synchronize(Projection(true,1,settled:1,opticDenied:2));
            c.Synchronize(null);c.Sample(Sample(hold:true),sink);c.Synchronize(Projection(true,1,settled:1,opticDenied:2));
            for(int i=0;i<30;i++)c.Sample(Sample(hold:true),sink);
            check(sink.Count==2&&c.PresentationOptic==1,
                "denied release and readiness recovery do not rearm an unchanged hold epoch: unavailable="+unavailable);
        }

        var mode=new CustomWeaponInputController();var changes=new Sink();mode.Synchronize(Projection(true,1));
        mode.Sample(Sample(),changes);mode.Sample(Sample(hold:true),changes);
        check(changes.Count==1&&changes.Trace[0]==new Attempt(CustomWeaponActionKind.Optic,0,1,true),
            "toggle-to-hold reconciles an already scoped released button once");
        mode.Synchronize(Projection(true,1,opticDenied:1));mode.Sample(Sample(hold:true),changes);
        check(changes.Count==1&&mode.PresentationOptic==1,"denied hold-mode reconciliation remains disarmed");
        mode.Sample(Sample(),changes);
        check(changes.Count==1&&mode.PresentationOptic==1,"hold-to-toggle preserves optic without inventing a toggle press");
        mode.Sample(Sample(hold:true),changes);
        check(changes.Count==2&&changes.Trace[1].Optic==0,"a distinct hold-mode entry starts one new reconciliation epoch");
        mode.Synchronize(Projection(true,settled:2));mode.Sample(Sample(down:true),changes);
        mode.Sample(Sample(down:true,hold:true),changes);
        check(changes.Count==3&&changes.Trace[2].Optic==1,
            "toggle-to-hold with a preheld button scopes without requiring a fabricated press");
        mode.Sample(Sample(down:true,hold:true,zoom:true),changes);
        check(changes.Count==3,"held mode does not interpret a redundant ZoomPressed hint as toggle");

        var failed=new CustomWeaponInputController();var full=new Sink();failed.Synchronize(Projection(true,1));
        failed.Sample(Sample(down:true,hold:true),full);full.FailNext=true;
        failed.Sample(Sample(hold:true,allowed:false),full);failed.Sample(Sample(hold:true),full);
        for(int i=0;i<30;i++)failed.Sample(Sample(hold:true),full);
        check(full.Count==1&&!full.Trace[0].Accepted&&failed.PresentationOptic==1,
            "refused deferred release is attempted once rather than retrying on every allowed sample");
        failed.Sample(Sample(down:true,hold:true),full);failed.Sample(Sample(hold:true),full);
        check(full.Count==2&&full.Trace[1]==new Attempt(CustomWeaponActionKind.Optic,0,1,true),
            "actual repress and release can retry a previously refused release without burning nonce");

        var native=new CustomWeaponInputController();var winner=new Sink();native.Synchronize(Projection(true,1));
        native.Sample(Sample(native:true,custom:true,shoot:true,zoom:true,down:true,cycle:true,hold:true),winner);
        check(winner.Count==1&&winner.Trace[0].Kind==CustomWeaponActionKind.Unequip&&!native.Selected,
            "explicit native selection wins hold reconciliation, custom selection, optic cycle and fire");
    }
    private static void Context(Action<bool,string> check)
    {
        var c=new CustomWeaponInputController();var s=new Sink();c.Synchronize(Projection());
        c.Sample(Sample(custom:true,allowed:false),s);
        check(s.Count==0&&!c.Selected,"engine-disallowed sample cannot publish selection or combat controls");
        c.Sample(Sample(custom:true),s);c.Synchronize(null);
        check(!c.Available&&c.SuppressNative,"lost context retains fail-closed suppression for pending custom selection");
        c.Sample(Sample(native:true,shoot:true,reload:true,zoom:true),s);
        check(s.Count==1,"context loss cannot publish corrective input with an unavailable owner");
        c.Synchronize(Projection(true,1,settled:1));
        check(c.Available&&c.Selected&&c.SuppressNative&&c.PresentationOptic==1,
            "same-owner canonical recovery restores authoritative selected state");
        c.Synchronize(null);c.Synchronize(null);
        check(!c.Available&&c.Selected&&c.SuppressNative,"readiness loss remains fail closed after canonical custom selection");
        var fresh=new WeaponOwnerContext(Owner.MatchId,Owner.AuthorityEpoch,Owner.Slot,Owner.Generation,13);
        c.Synchronize(Projection(owner:fresh));
        check(c.Available&&!c.Selected&&!c.SuppressNative&&c.PresentationOptic==0,
            "new-life canonical projection clears old predictions and suppression");
        c.Sample(Sample(custom:true),s);c.Reset();
        check(!c.Available&&!c.Selected&&!c.SuppressNative&&c.PresentationOptic==0,"explicit scene reset clears all local presentation intent");
        c.Synchronize(Projection(true));c.Sample(Sample(zoom:true),s);
        check(c.PresentationOptic==1,"new context/reset restores default optic preference without old-life leakage");
    }
    private static void BootstrapBoundary(Action<bool,string> check)
    {
        var first=new WorldBootstrapIdentity(new(Owner.MatchId,Owner.AuthorityEpoch,1),1,Owner.Generation,1000,1,1);
        foreach(var replacement in new[]{first with{Revision=2},first with{AuthorityFrame=1001},
            first with{SlowRevision=2},first with{WorldRevision=2},first with{Start=first.Start with{StartGeneration=2}},
            default(WorldBootstrapIdentity)})
        {
            var c=new CustomWeaponInputController();var sink=new Sink();c.Synchronize(Projection(bootstrap:first));
            c.Sample(Sample(custom:true,zoom:true),sink);c.Sample(Sample(cycle:true),sink);
            c.Synchronize(Projection(bootstrap:first));
            check(c.Selected&&c.SuppressNative&&c.PresentationOptic==2&&sink.Count==3,
                "periodic same-owner/same-bootstrap state preserves unsettled selection, optic and native fence");
            c.Synchronize(Projection(bootstrap:replacement));
            check(c.Available&&!c.Selected&&!c.SuppressNative&&c.PresentationOptic==0,
                "same owner under changed exact bootstrap clears old pending selection, optic and native suppression");
            c.Sample(Sample(custom:true,zoom:true),sink);
            check(c.Selected&&c.PresentationOptic==1&&sink.Trace[sink.Count-1].Kind==CustomWeaponActionKind.Optic
                &&sink.Trace[sink.Count-1].Optic==1,
                "replacement bootstrap resets previous magnification preference rather than carrying presentation intent");
        }
        var ambiguous=new CustomWeaponInputController();var requests=new Sink();ambiguous.Synchronize(Projection(bootstrap:first));
        ambiguous.Sample(Sample(custom:true),requests);ambiguous.Sample(Sample(native:true),requests);
        ambiguous.Synchronize(Projection(selectionDenied:2,bootstrap:first));
        ambiguous.Sample(Sample(custom:true),requests);ambiguous.Synchronize(Projection(selectionDenied:3,bootstrap:first));
        check(!ambiguous.Selected&&ambiguous.SuppressNative,"same-stream missing equip outcomes keep ambiguous suppression before bootstrap replacement");
        ambiguous.Synchronize(Projection(bootstrap:first with{Revision=2}));
        check(!ambiguous.Selected&&!ambiguous.SuppressNative&&ambiguous.Available,
            "fresh exact bootstrap clears old ambiguous equip fence even when lifecycle owner values remain identical");
    }
    private static void ManifestBoundary(Action<bool,string> check)
    {
        var bootstrap=new WorldBootstrapIdentity(new(Owner.MatchId,Owner.AuthorityEpoch,1),1,Owner.Generation,1000,1,1);
        var first=WeaponDigest.FromHex(new string('a',64));var replacement=WeaponDigest.FromHex(new string('b',64));
        var c=new CustomWeaponInputController();var sink=new Sink();c.Synchronize(Projection(bootstrap:bootstrap,manifest:first));
        c.Sample(Sample(custom:true,zoom:true),sink);c.Sample(Sample(cycle:true),sink);
        c.Synchronize(Projection(bootstrap:bootstrap,manifest:first));
        check(first.IsValid&&replacement.IsValid&&first!=replacement&&c.Selected&&c.SuppressNative&&c.PresentationOptic==2,
            "same manifest under exact owner/bootstrap preserves pending prediction with known nonzero digest fixtures");
        c.Synchronize(Projection(bootstrap:bootstrap,manifest:replacement));
        check(c.Available&&!c.Selected&&!c.SuppressNative&&c.PresentationOptic==0,
            "new manifest under same owner/bootstrap cannot adopt old pending equip, optic or native permission fence");
        c.Sample(Sample(custom:true,zoom:true),sink);
        check(c.Selected&&c.PresentationOptic==1&&sink.Trace[sink.Count-1].Optic==1,
            "new manifest starts a fresh local magnification preference rather than old-content presentation state");
        c.Sample(Sample(native:true),sink);c.Synchronize(Projection(selectionDenied:sink.Sequence,bootstrap:bootstrap,manifest:replacement));
        check(!c.Selected&&c.SuppressNative,"new manifest can independently retain its own unresolved equip suppression");
        c.Synchronize(Projection(bootstrap:bootstrap));
        check(!c.Selected&&!c.SuppressNative&&c.PresentationOptic==0,
            "explicit offline default manifest is a separate context and cannot inherit online pending control state");
    }
    private static void ResourceBoundary(Action<bool,string> check)
    {
        var definition=RiftlineM7.Create();var runtime=new WeaponRuntimeState(definition,Owner,1000);
        runtime.SetEquipped(1000,true);var before=runtime.Snapshot();
        var c=new CustomWeaponInputController();var s=new Sink();c.Synchronize(Projection(true));
        c.Sample(Sample(zoom:true,shoot:true),s);c.Sample(Sample(reload:true),s);c.Sample(Sample(native:true),s);
        check(s.Count==4&&runtime.Snapshot()==before,
            "published selection/optic/fire/reload intents never mutate an independent authoritative magazine, reserve, cooldown or reload state");
    }
    private static Attempt[] RenderTrace(int hz)
    {
        var c=new CustomWeaponInputController();var sink=new Sink();c.Synchronize(Projection());
        int fixedSamples=0;
        for(int render=0;render<hz*2;render++)
        {
            int target=(render+1)*60/hz;
            while(fixedSamples<target)
            {
                int tick=fixedSamples++;
                c.Sample(Sample(custom:tick==0,native:tick==90,shoot:tick is 2 or 40,
                    zoom:tick is 1 or 60,cycle:tick==30,reload:tick==70),sink);
                if(tick==10)c.Synchronize(Projection(true,1,settled:3));
                if(tick==100)c.Synchronize(Projection(false,settled:sink.Sequence));
            }
            // Rendering reads presentation only; no simulation input sample.
            _=c.Selected;_=c.SuppressNative;_=c.PresentationOptic;
        }
        if(fixedSamples!=120)throw new InvalidOperationException("render fixture did not execute120 fixed samples");
        return sink.Trace.AsSpan(0,sink.Count).ToArray();
    }
    private static void RenderSchedules(Action<bool,string> check)
    {
        var sixty=RenderTrace(60);var oneTwenty=RenderTrace(120);var twoForty=RenderTrace(240);
        check(sixty.AsSpan().SequenceEqual(oneTwenty)&&sixty.AsSpan().SequenceEqual(twoForty),
            "identical120 fixed60Hz input samples publish identical requests under60/120/240Hz presentation schedules");
        check(sixty.Count(x=>x.Kind==CustomWeaponActionKind.Fire)==2,
            "render schedule cannot create additional fire requests beyond two physical fixed-sample edges");
    }
    private static void Allocation(Action<bool,string> check)
    {
        var c=new CustomWeaponInputController();var sink=new Sink{Capture=false};c.Synchronize(Projection(true));
        void Cycle()
        {
            c.Sample(Sample(zoom:true,shoot:true),sink);
            c.Synchronize(Projection(true,c.PresentationOptic,settled:sink.Sequence));
            c.Sample(Sample(),sink);_=c.Selected;_=c.SuppressNative;_=c.PresentationOptic;
        }
        for(int i=0;i<1000;i++)Cycle();
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<100000;i++)Cycle();
        long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        check(allocated==0&&sink.Accepted==202000&&c.Available&&c.Selected,
            "100000 warmed fixed input/synchronize/presentation cycles allocate zero bytes");
        Console.WriteLine($"CUSTOM INPUT CONTROLLER ALLOCATION:100000 warmed fixed-sample cycles={allocated}B");
    }
}
