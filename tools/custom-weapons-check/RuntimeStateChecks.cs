using MphRead.Mods.CustomWeapons;

internal static class RuntimeStateChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var definition = WeaponDefinition.Create(Guid.Parse("9a776851-f39f-4a76-8c13-c5d88875ef61"),"M7",MechanicsChecks.Rules());
        var owner = new WeaponOwnerContext(1,1,0,1,1);
        WeaponRuntimeState Fresh(uint tick = 0, WeaponDefinition? weapon = null)
        { var s = new WeaponRuntimeState(weapon ?? definition,owner,tick); s.SetEquipped(tick,true); return s; }
        var state = Fresh();
        check(state.TryFire(0,1,true) == WeaponActionResult.Accepted && state.Snapshot().Magazine == 3,"one accepted fire spends one round");
        check(state.TryFire(0,1,true) == WeaponActionResult.Duplicate && state.Snapshot().Magazine == 3,"duplicate fire never spends");
        check(state.TryFire(74,2,true) == WeaponActionResult.Cooldown,"75-tick bolt denies tick74");
        check(state.TryFire(75,2,true) == WeaponActionResult.Duplicate,"denied shot cannot become delayed fire");
        check(state.TryFire(75,3,true) == WeaponActionResult.Accepted,"75-tick exact boundary accepts fresh input");
        check(state.TryFire(74,4,true) == WeaponActionResult.StaleFrame,"reordered simulation time refused");
        check(state.TrySetOptic(75,1,2) == WeaponActionResult.Accepted && state.Snapshot().FireReadyTick == 150,"optic cannot reset bolt");
        check(state.TrySetOptic(75,2,3) == WeaponActionResult.InvalidOptic,"unsupported optic refused");
        state.SetEquipped(76,false); state.SetEquipped(77,true);
        check(state.TryFire(78,5,true) == WeaponActionResult.Cooldown,"swap cannot bypass bolt");
        check(state.Snapshot().Optic == 0,"swap cancels scope");
        var capacity = Fresh();
        check(capacity.TryFire(0,1,false) == WeaponActionResult.CapacityUnavailable && capacity.Snapshot().Magazine == 4
            && capacity.Snapshot().LastAcceptedShotId == 0,"pool exhaustion cannot debit or author accepted shot");
        check(capacity.TryFire(100,1,true) == WeaponActionResult.Duplicate,"capacity denial cannot fire later under same key");
        check(capacity.TryFire(100,2,true) == WeaponActionResult.Accepted,"new valid input may fire after denied capacity");
        var stale = Fresh(); stale.Advance(100);
        check(stale.TryFire(99,1,true) == WeaponActionResult.StaleFrame
            && stale.TryFire(100,1,true) == WeaponActionResult.Duplicate,"stale denied shot cannot revive later");
        check(stale.TryReload(99,1) == WeaponActionResult.StaleFrame
            && stale.TryReload(100,1) == WeaponActionResult.Duplicate,"stale reload ID stays denied");
        check(stale.TrySetOptic(99,1,1) == WeaponActionResult.StaleFrame
            && stale.TrySetOptic(100,1,1) == WeaponActionResult.Duplicate,"stale optic ID stays denied");
        var rollback = Fresh(); var checkpoint = rollback.Snapshot(); rollback.TryFire(0,1,true);
        rollback.CancelUnemittedFire(1,checkpoint);
        check(rollback.Snapshot().Magazine == 4 && rollback.Snapshot().LastAcceptedShotId == 0
            && rollback.TryFire(0,1,true) == WeaponActionResult.Duplicate,"unemitted rollback restores cost without reviving denied request");
        var reload = Fresh(); reload.TryFire(0,1,true);
        check(reload.TryReload(0,1) == WeaponActionResult.Accepted,"partial reload accepted");
        check(reload.Snapshot().Magazine == 3 && reload.Snapshot().Reserve == 16,"reload start grants nothing");
        check(reload.TryFire(10,2,true) == WeaponActionResult.Reloading,"no fire during reload");
        check(reload.TryReload(20,1) == WeaponActionResult.Duplicate && reload.Snapshot().ReloadCompleteTick == 138,"duplicate does not restart reload");
        reload.Advance(137); check(reload.Snapshot().Magazine == 3 && reload.Snapshot().Reserve == 16,"no premature reserve transfer");
        reload.Advance(138); check(reload.Snapshot().Magazine == 4 && reload.Snapshot().Reserve == 15,"138-tick transfer exactly once");
        reload.Advance(139); check(reload.Snapshot().Reserve == 15,"reload cannot double-complete");
        check(reload.TryFire(139,2,true) == WeaponActionResult.Duplicate,"reload-denied fire stays denied after completion");
        var preview = Fresh(); preview.TryFire(0,1,true); preview.TryReload(0,1);
        preview.TryFire(10,2,true); preview.TryReload(10,2); preview.TrySetOptic(10,1,1);
        var committed = preview.Snapshot();
        check(preview.Preview(137,out var beforeCompletion) == WeaponActionResult.Accepted
            && beforeCompletion.Tick == 137 && beforeCompletion.Phase == CustomWeaponPhase.Reloading
            && beforeCompletion.Magazine == 3 && beforeCompletion.Reserve == 16,"preview before reload deadline transfers nothing");
        check(preview.Preview(138,out var atCompletion) == WeaponActionResult.Accepted
            && atCompletion.Tick == 138 && atCompletion.Phase == CustomWeaponPhase.Ready
            && atCompletion.Magazine == 4 && atCompletion.Reserve == 15
            && atCompletion.ReloadCompleteTick == 138 && atCompletion.FireReadyTick == 75
            && atCompletion.LastAcceptedShotId == 1 && atCompletion.LastAcceptedShotTick == 0,
            "preview projects exact reload completion and preserves acceptance/deadlines");
        check(preview.Preview(1000,out var laterCompletion) == WeaponActionResult.Accepted
            && laterCompletion.Magazine == 4 && laterCompletion.Reserve == 15
            && preview.Snapshot() == committed,"future previews do not advance committed tick or resources");
        check(preview.TryFire(20,2,true) == WeaponActionResult.Duplicate
            && preview.TryReload(20,2) == WeaponActionResult.Duplicate
            && preview.TrySetOptic(20,1,1) == WeaponActionResult.Duplicate,
            "preview cannot revive denied fire/reload/optic IDs");
        check(preview.TryFire(20,3,true) == WeaponActionResult.Reloading
            && preview.Snapshot().Tick == 20 && preview.Snapshot().Magazine == 3
            && preview.Snapshot().Reserve == 16,"delayed source fire remains in reload after future preview");
        check(preview.Preview(19,out var stalePreview) == WeaponActionResult.StaleFrame
            && stalePreview == preview.Snapshot(),"stale preview returns actual committed snapshot");
        check(preview.Advance(138) == WeaponActionResult.Accepted && preview.Snapshot() == atCompletion,
            "real reload completion matches earlier projection exactly");
        preview.Advance(1000);
        check(preview.Snapshot() == laterCompletion,"real advancement after preview transfers reserve only once");
        var previewBolt = Fresh(); previewBolt.TryFire(0,1,true);
        check(previewBolt.Preview(74,out var cyclingPreview) == WeaponActionResult.Accepted
            && cyclingPreview.Phase == CustomWeaponPhase.BoltCycling
            && previewBolt.Preview(75,out var readyPreview) == WeaponActionResult.Accepted
            && readyPreview.Phase == CustomWeaponPhase.Ready && previewBolt.Snapshot().Tick == 0,
            "preview projects bolt boundary without advancing committed tick");
        var cancel = Fresh(); cancel.TryFire(0,1,true); cancel.TryReload(0,1);
        cancel.SetEquipped(137,false); cancel.Advance(1000); cancel.SetEquipped(1000,true);
        check(cancel.Snapshot().Magazine == 3 && cancel.Snapshot().Reserve == 16,"swap/form cancellation gives no rounds");
        cancel.TryReload(1000,2); cancel.Advance(1137); check(cancel.Snapshot().Magazine == 3,"new reload pays full duration");
        cancel.Advance(1138); check(cancel.Snapshot().Magazine == 4 && cancel.Snapshot().Reserve == 15,"cancelled reload can restart cleanly");
        foreach (var reason in Enum.GetValues<WeaponCancellation>())
        {
            var invalid = Fresh(); invalid.TryFire(0,1,true); invalid.TryReload(1,1); invalid.Invalidate(reason);
            check(invalid.Advance(1000) == WeaponActionResult.InvalidLife
                && invalid.TryFire(1000,2,true) == WeaponActionResult.InvalidLife
                && invalid.TryReload(1000,2) == WeaponActionResult.InvalidLife
                && invalid.Snapshot().Phase == CustomWeaponPhase.Invalid
                && invalid.Snapshot().Magazine == 3 && invalid.Snapshot().Reserve == 16,"lifecycle invalidation " + reason);
            check(invalid.Preview(1000,out var invalidPreview) == WeaponActionResult.InvalidLife
                && invalidPreview == invalid.Snapshot(),"invalid-life preview returns actual snapshot " + reason);
        }
        var empty = Fresh();
        for (uint i=0; i<4; i++) check(empty.TryFire(i*75,i+1,true) == WeaponActionResult.Accepted,"four bounded shots");
        check(empty.TryFire(300,5,true) == WeaponActionResult.Empty && empty.Snapshot().Magazine == 0,"empty cannot fire");
        empty.TryReload(300,1); empty.Advance(438);
        check(empty.Snapshot().Magazine == 4 && empty.Snapshot().Reserve == 12,"empty reload transfers four");
        var partialDefinition = WeaponDefinition.Create(definition.Identity.ContentId,"Partial",
            MechanicsChecks.Rules(MechanicsChecks.Json.Replace("\"initialReserve\":16","\"initialReserve\":2")));
        var partial = Fresh(0,partialDefinition);
        for (uint i=0; i<4; i++) partial.TryFire(i*75,i+1,true);
        partial.TryReload(300,1); partial.Advance(438);
        check(partial.Snapshot().Magazine == 2 && partial.Snapshot().Reserve == 0,"partial reserve cannot create rounds");
        partial.TryFire(438,5,true);
        check(partial.TryReload(439,2) == WeaponActionResult.NoReserve,"no infinite reserve");
        uint start = uint.MaxValue-10;
        var wrap = Fresh(start); wrap.TryFire(start,uint.MaxValue,true);
        check(wrap.TryFire(unchecked(start+74),1,true) == WeaponActionResult.Cooldown,"bolt frame wrap before boundary");
        check(wrap.TryFire(unchecked(start+75),2,true) == WeaponActionResult.Accepted,"frame/sequence wrap bounded correctly");
        var wrapReload = Fresh(start); wrapReload.TryFire(start,1,true); wrapReload.TryReload(start,1);
        var wrapCommitted = wrapReload.Snapshot();
        var wrapBeforeResult = wrapReload.Preview(unchecked(start+137),out var wrapBefore);
        var wrapCompleteResult = wrapReload.Preview(unchecked(start+138),out var wrapComplete);
        check(wrapBeforeResult == WeaponActionResult.Accepted
            && wrapBefore.Phase == CustomWeaponPhase.Reloading && wrapBefore.Magazine == 3
            && wrapCompleteResult == WeaponActionResult.Accepted
            && wrapComplete.Magazine == 4 && wrapComplete.Reserve == 15
            && wrapComplete.ReloadCompleteTick == unchecked(start+138)
            && wrapReload.Snapshot() == wrapCommitted,"reload preview crosses uint wrap without committing transfer");
        check(wrapReload.Preview(unchecked(start-1),out var wrapStale) == WeaponActionResult.StaleFrame
            && wrapStale == wrapCommitted,"preview rejects stale tick across uint wrap");
        check(wrapReload.Preview(unchecked(start+0x80000000u),out var ambiguousPreview) == WeaponActionResult.StaleFrame
            && ambiguousPreview == wrapCommitted,"preview rejects ambiguous half-range tick");
        wrapReload.Advance(unchecked(start+138)); check(wrapReload.Snapshot().Reserve == 15,"reload completes across uint wrap");
        check(wrapReload.Snapshot() == wrapComplete,"committed wrap completion matches preview");
        using var registry = new WeaponRegistry(true,new[] { definition });
        var inventory = registry.Inventory(0);
        check(!inventory.Grant(definition.Identity,0),"no grant before owner binding");
        check(inventory.BindLife(owner,0) && inventory.Grant(definition.Identity,0),"bound grant");
        inventory.TryGetState(definition.Identity,out var old); old!.SetEquipped(0,true); old.TryFire(0,1,true); old.TryReload(0,1);
        check(!inventory.BindLife(owner,1) && !inventory.Grant(definition.Identity,0),"same-life rebind/grant cannot refill");
        check(inventory.BindLife(new(1,1,0,1,2),100) && inventory.Grant(definition.Identity,100),"new life gets independent grant");
        check(old.Advance(200) == WeaponActionResult.InvalidLife && old.Snapshot().Reserve == 16,"old-life reload cannot complete for new life");
        check(!inventory.BindLife(owner,201) && !inventory.BindLife(new(2,1,0,1,3),201)
            && !inventory.BindLife(new(1,1,1,1,3),201),"old life, match change and wrong slot require proper ownership");
        inventory.TryGetState(definition.Identity,out var current); inventory.Clear();
        check(current!.Snapshot().Phase == CustomWeaponPhase.Invalid && !inventory.Grant(definition.Identity,0)
            && !inventory.BindLife(new(1,1,0,1,2),202),"clear cannot erase life high-water and refill");
        using var grants = new WeaponRegistry(true,new[] { definition, partialDefinition });
        var late = grants.Inventory(0); late.BindLife(owner,0); late.Grant(definition.Identity,0);
        late.TryGetState(definition.Identity,out var first); first!.Advance(500);
        check(!late.Grant(partialDefinition.Identity,100),"shared inventory high-water rejects backdated grant");
        check(late.Grant(partialDefinition.Identity,500),"late grant records acquisition tick");
        late.TryGetState(partialDefinition.Identity,out var acquired);
        check(acquired!.SetEquipped(0,true) == WeaponActionResult.StaleFrame
            && acquired.TryFire(75,1,true) == WeaponActionResult.StaleFrame,"late acquisition cannot fire before grant");
        check(!late.BindLife(new(1,1,0,1,2),499),"new life cannot backdate shared inventory time");
        WeaponStateSnapshot Schedule(int hz)
        {
            var s = Fresh(); int phase = 0; uint tick = 0;
            for (int render=0; render<hz*10; render++)
            {
                phase += 60;
                while (phase >= hz)
                {
                    phase -= hz; s.Advance(tick);
                    if (tick == 0) s.TryFire(tick,1,true);
                    if (tick == 1) s.TryFire(tick,2,true);
                    if (tick == 75) s.TryFire(tick,3,true);
                    if (tick == 100) s.TryReload(tick,1);
                    if (tick == 238) s.TryFire(tick,4,true);
                    tick++;
                }
            }
            return s.Snapshot();
        }
        check(Schedule(60) == Schedule(120) && Schedule(60) == Schedule(240),"same accepted inputs at 60/120/240 Hz are deterministic");
        var hot = Fresh();
        for (uint tick=0; tick<20000; tick++) { hot.Advance(tick); hot.TrySetOptic(tick,tick+1,(int)(tick%3)); _=hot.Snapshot(); }
        long before = GC.GetAllocatedBytesForCurrentThread(); long sum = 0;
        for (uint tick=20000; tick<120000; tick++) { hot.Advance(tick); hot.TrySetOptic(tick,tick+1,(int)(tick%3)); sum+=hot.Snapshot().Magazine; }
        long allocated = GC.GetAllocatedBytesForCurrentThread()-before;
        check(allocated == 0 && sum == 400000,"100000 warmed routine ticks/optics/snapshots allocate zero bytes");
        var previewHot = Fresh(); previewHot.TryFire(0,1,true); previewHot.TryReload(0,1);
        var previewHotCommitted = previewHot.Snapshot();
        for (uint tick=0; tick<20000; tick++) previewHot.Preview(tick,out _);
        before = GC.GetAllocatedBytesForCurrentThread(); sum = 0;
        for (uint tick=20000; tick<120000; tick++)
        { previewHot.Preview(tick,out var projected); sum += projected.Magazine + projected.Reserve; }
        allocated = GC.GetAllocatedBytesForCurrentThread()-before;
        check(allocated == 0 && sum == 1900000 && previewHot.Snapshot() == previewHotCommitted,
            "100000 warmed previews allocate zero bytes and leave admission state unchanged");
    }
}
