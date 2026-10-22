using System.Reflection;
using T = MphRead.Mods.CustomWeapons.PirateBurstTimeline;

// Pure source-state acceptance only. Literal source timeline and an external
// simulated UA ledger exercise the subordinate helper, without native actors,
// ammo mutation, allocation, source/admission validation or flight lifetimes.
// Complete outcomes below are test inputs, never real native Spawn evidence.
// Reflection writes only explicit synthetic uint exhaustion setup.
internal static class PirateBurstTimelineChecks
{
    internal static void Run(Action<bool,string> check) => new Cases(check).Run();

    private sealed class Cases(Action<bool,string> check)
    {
        private void Check(bool condition, string label) => check(condition, "Pirate burst source state: " + label);
        private T.Ticket Plan(T t, bool held=true, int ua=60, int slots=3)
        { Check(t.TryPlan(held,ua,slots,out var ticket), "expected production proposal"); return ticket; }
        private void Success(T t, T.Ticket ticket)
        { Check(t.Complete(ticket,true), "reported-success outcome accepted once"); }
        private void Empty(T t, bool held=true, int ua=60, int slots=3)
        { Check(!t.TryPlan(held,ua,slots,out var ticket) && ticket == default, "refusal produces no ticket"); }
        private void SetCounter(T t, string field, uint value)
        {
            // Explicit terminal-provenance setup only; not gameplay/actor/ammo state.
            typeof(T).GetField(field, BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(t,value);
        }
        private void LiteralTimeline()
        {
            var t=new T(); var observed=new List<ulong>(); var ordinals=new List<int>(); var costs=new List<int>();
            for(ulong frame=0;frame<=40;frame++)
            {
                Check(t.BeginStep(frame,true),"new sequential step admitted");
                bool due=frame is 0 or 6 or 12 or 40;
                if(due)
                {
                    var p=Plan(t); observed.Add(p.Frame); ordinals.Add(p.Ordinal); costs.Add(p.AmmoCost); Success(t,p);
                    Check(!t.Complete(p,true),"duplicate complete cannot settle twice"); Empty(t);
                }
                else Empty(t);
                Check(t.Cooldown==(frame<40 ? 40-(int)frame : 40),"literal40 wait from successful first only");
            }
            Check(observed.SequenceEqual(new ulong[]{0,6,12,40}),"independent literal0/6/12/40 oracle");
            Check(ordinals.SequenceEqual(new[]{0,1,2,0}) && costs.SequenceEqual(new[]{5,0,0,5}),"first-only external UA ticket costs");
        }
        private void ExternalSixtyUa()
        {
            var t=new T(); int ua=60, bursts=0, bolts=0, spent=0; var keys=new HashSet<(uint,uint)>();
            var starts=new List<ulong>();
            for(ulong frame=0;frame<520;frame++)
            {
                Check(t.BeginStep(frame,true),"budget step admitted");
                if(!t.TryPlan(true,ua,3,out var p))continue;
                // External test ledger is deliberately outside helper. It commits
                // only successful first Spawn outcomes; this is not native ammo proof.
                Check(p.AmmoCost==(p.Ordinal==0?5:0),"ticket payment only first");
                Check(keys.Add((p.Burst,p.Projectile)),"unique burst/projectile provenance");
                if(p.Ordinal==0){bursts++;starts.Add(frame);ua-=p.AmmoCost;spent+=p.AmmoCost;}
                bolts++;Success(t,p);
            }
            Check(bursts==12 && bolts==36 && ua==0 && spent==60,"literal60 UA ->12 bursts/36 bolts/external UA5 first-only");
            Check(starts.SequenceEqual(new ulong[]{0,40,80,120,160,200,240,280,320,360,400,440}),"independent twelve literal burst starts");
        }
        private void ReleasedTailsAndRefusals()
        {
            var t=new T(); Check(t.BeginStep(0,true),"initial step");
            var before=t.Snapshot;Empty(t,ua:4);Empty(t,slots:2);Empty(t,held:false);
            Check(t.Snapshot==before && t.Cooldown==0,"UA4/free2/released first refusals consume no identity/time");
            var first=Plan(t,ua:5,slots:3);Success(t,first);
            Check(first.Burst==1 && first.Projectile==1 && first.Ordinal==0 && first.AmmoCost==5,"exact first source key/cost");
            var got=new List<ulong>();
            for(ulong frame=1;frame<=12;frame++)
            {
                Check(t.BeginStep(frame,true),"released sequential step");
                if(frame is 6 or 12){var p=Plan(t,held:false,ua:0,slots:1);got.Add(p.Frame);Check(p.AmmoCost==0,"paid tails remain free");Success(t,p);}
                else Empty(t,held:false,ua:0,slots:1);
            }
            Check(got.SequenceEqual(new ulong[]{6,12}) && !t.HasPaidTails,"release never erases already-paid two tails");
        }
        private void FailedFirstNoWaitAndGaps()
        {
            var t=new T();Check(t.BeginStep(100,true),"first fail step");var failed=Plan(t);
            Check(t.Complete(failed,false) && !t.HasPaidTails && t.Cooldown==0 && t.Snapshot.ReadyAt==0,"first NoSpawn receives no wait/tails/payment");
            Check(!t.Complete(failed,true),"failed first cannot be converted into success later");Empty(t);
            Check(t.BeginStep(101,true),"next step after first NoSpawn");var next=Plan(t);Success(t,next);
            Check(next.Burst==2 && next.Projectile==2 && next.Frame==101 && next.AmmoCost==5,"failed attempt leaves unique provenance gap, new first next tick");
            Check(t.Cooldown==40 && t.Snapshot.ReadyAt==141,"new success owns own40 wait");
            Check(!t.Complete(failed,true),"old failed key cannot settle newer first");
        }
        private void TailFailureAndCapacity()
        {
            var t=new T();Check(t.BeginStep(0,true),"tail failure initial");var first=Plan(t);Success(t,first);
            for(ulong frame=1;frame<=6;frame++)Check(t.BeginStep(frame,true),"tail failure sequence");
            var tail=Plan(t,held:false,ua:0,slots:1);Check(t.Complete(tail,false),"reported tail NoSpawn settles");
            Check(t.Cooldown==34 && t.Snapshot.ReadyAt==40,"tail failure cannot refund or remove paid wait");
            // Timeline consumes the failed second opportunity but preserves the
            // final paid third; explicit observed policy, no invented native allocation.
            for(ulong frame=7;frame<=12;frame++)Check(t.BeginStep(frame,true),"last paid opportunity sequence");
            var last=Plan(t,held:false,ua:0,slots:1);Check(last.Ordinal==2 && last.AmmoCost==0,"tail Spawn failure advances without refund; remaining paid third stays eligible");Success(t,last);
            var q=new T();Check(q.BeginStep(0,true),"capacity arm first");Success(q,Plan(q));
            for(ulong frame=1;frame<=6;frame++)Check(q.BeginStep(frame,true),"capacity arm sequence");
            var provenance=q.Snapshot;Empty(q,held:false,ua:0,slots:0);
            Check(!q.HasPaidTails && q.Snapshot.Projectile==provenance.Projectile && q.Snapshot.ReadyAt==40,"zero-free tail refuses with no new allocation/eviction ticket or refund");
            for(ulong frame=7;frame<=12;frame++)Check(q.BeginStep(frame,true),"capacity arm third canceled");Empty(q,held:false,ua:0,slots:1);
        }
        private void CancelAndOpenTransactions()
        {
            foreach(bool emitted in new[]{false,true})
            {
                var t=new T();Check(t.BeginStep(9,true),"open first");var p=Plan(t);t.CancelTails();
                Empty(t);Check(t.Complete(p,emitted),"open canceled first still resolves reported Spawn outcome");
                Check(!t.HasPaidTails && t.Cooldown==(emitted?40:0),"canceled first true keeps40/no tails, false gains no wait");
                Check(t.BeginStep(10,true),"canceled first following frame");
                if(emitted){Empty(t);Check(t.Cooldown==39,"canceled emitted first wait decrements normally");}
                else {var next=Plan(t);Check(next.Burst>p.Burst && next.Projectile>p.Projectile,"canceled failed first never reuses sequence");Success(t,next);}
            }
            var q=new T();Check(q.BeginStep(0,true),"morph simulation first");Success(q,Plan(q));
            Check(q.BeginStep(1,true),"morph step");q.CancelTails();Check(q.Cooldown==39 && !q.HasPaidTails,"explicit cancel/morph retains remaining40 wait");
            for(ulong frame=2;frame<40;frame++){Check(q.BeginStep(frame,true),"post cancel sequential");Empty(q);}
            Check(q.BeginStep(40,true),"postcancel ready boundary");Success(q,Plan(q));
        }
        private void DuplicatesStaleAbandonedAndGaps()
        {
            var t=new T();Check(t.BeginStep(0,true),"first validstep");var p=Plan(t);Success(t,p);
            Check(!t.BeginStep(0,true),"duplicate step rejected");Empty(t);
            Check(t.BeginStep(1,true) && !t.BeginStep(0,true),"stale step rejected");Empty(t);
            Check(!t.Complete(p,true),"previous source ticket cannot complete current step");
            var a=new T();Check(a.BeginStep(100,true),"abandoned firststep");var abandoned=Plan(a);
            Check(a.BeginStep(101,true) && a.Snapshot.Exhausted,"abandoned proposal retires helper on next source step");Empty(a);
            Check(!a.Complete(abandoned,true),"abandoned source ticket no longer settles");a.ResetForLife();Check(a.BeginStep(0,true),"newlife after abandonment");Empty(a);Check(a.Snapshot.Exhausted,"life reset cannot revive retired provenance");
            var gap=new T();Check(gap.BeginStep(0,true),"gap first");Success(gap,Plan(gap));Check(gap.BeginStep(6,true),"nonconsecutive source observed");Empty(gap,held:false,ua:0,slots:1);
            Check(!gap.HasPaidTails && gap.Snapshot.ReadyAt==40,"missing native source steps cancel paid tails without erasing wait");
            var unavailable=new T();Check(unavailable.BeginStep(0,true),"unusable first");Success(unavailable,Plan(unavailable));
            for(ulong frame=1;frame<=6;frame++)Check(unavailable.BeginStep(frame,frame!=6),"explicit unusable actual source");
            Empty(unavailable);Check(!unavailable.HasPaidTails && unavailable.Cooldown==34,"unusable tail step cancels without refund");
        }
        private void ExhaustionAndLife()
        {
            var time=new T();Check(time.BeginStep(ulong.MaxValue-39,true),"near terminal time step");Empty(time);Check(time.Snapshot.Exhausted,"time insufficient for full40 wait fails closed");
            time.ResetForLife();Check(time.BeginStep(0,true),"terminal time life reset");Empty(time);
            var boundary=new T();ulong start=ulong.MaxValue-40;
            for(ulong offset=0;offset<=40;offset++)
            {
                Check(boundary.BeginStep(start+offset,true),"last safe timeline source");
                if(offset is 0 or 6 or 12)Success(boundary,Plan(boundary));else Empty(boundary);
            }
            Check(boundary.Snapshot.Exhausted && !boundary.BeginStep(0,true),"ulong time exhaustion never wraps source frame");
            var bursts=new T();SetCounter(bursts,"_burst",uint.MaxValue);Check(bursts.BeginStep(0,true),"terminal burst sequence setup");Empty(bursts);Check(bursts.Snapshot.Exhausted,"uint burst exhaustion fails closed");
            var flights=new T();SetCounter(flights,"_projectile",uint.MaxValue);Check(flights.BeginStep(0,true),"terminal projectile sequence setup");Empty(flights);Check(flights.Snapshot.Exhausted,"uint projectile exhaustion fails closed");
            // Synthetic boundary setup: each first requires three identities,
            // so one/two remaining IDs must be rejected before a payable ticket.
            foreach(uint initial in new[]{uint.MaxValue-1,uint.MaxValue-2})
            {
                var shortBurst=new T();SetCounter(shortBurst,"_projectile",initial);
                Check(shortBurst.BeginStep(0,true),"insufficient whole-burst identity setup");
                var before=shortBurst.Snapshot;int externalUa=60;
                bool planned=shortBurst.TryPlan(true,externalUa,3,out var ticket);
                if(planned && shortBurst.Complete(ticket,true))externalUa-=ticket.AmmoCost;
                Check(!planned && ticket==default,"one/two remaining identities refuse the first payable ticket");
                Check(shortBurst.Snapshot.Exhausted && shortBurst.Snapshot.Projectile==before.Projectile
                    && shortBurst.Snapshot.Burst==before.Burst,"whole-burst refusal exhausts without jumping either sequence");
                Check(externalUa==60,"no first ticket means no simulated external owner UA debit");
                Check(shortBurst.Cooldown==0 && !shortBurst.HasPaidTails,"identity-short first refusal creates no wait or tails");
                Check(!shortBurst.Complete(ticket,true),"identity-short refusal cannot settle a default ticket into emission");
            }
            var life=new T();Check(life.BeginStep(99,true),"old life source");var old=Plan(life);Success(life,old);life.ResetForLife();
            Check(!life.Snapshot.HasStep && life.Cooldown==0 && !life.HasPaidTails,"life reset clears schedule only");
            Check(life.BeginStep(99,true),"same frame number in new life");var fresh=Plan(life);
            Check(fresh.Burst==old.Burst+1 && fresh.Projectile==old.Projectile+1 && !life.Complete(old,true),"ResetLife preserves unique provenance and rejects stale same-frame key");Success(life,fresh);
            bursts.ResetForLife();Check(bursts.BeginStep(0,true),"exhausted sequence newlife");Empty(bursts);Check(bursts.Snapshot.Exhausted,"uint exhaustion terminal across life reset");
        }
        private void WholeBurstIdentityBoundary()
        {
            var t=new T();SetCounter(t,"_projectile",uint.MaxValue-3);
            int externalUa=60;var frames=new List<ulong>();var ids=new List<uint>();var costs=new List<int>();
            for(ulong frame=0;frame<=40;frame++)
            {
                Check(t.BeginStep(frame,true),"last complete three-ID burst advances actual source steps");
                if(frame is 0 or 6 or 12)
                {
                    var ticket=Plan(t,ua:externalUa);
                    Check(ticket.Burst==1 && ticket.Frame==frame && ticket.Ordinal==frames.Count,
                        "last whole burst preserves exact burst/frame/ordinal provenance");
                    frames.Add(ticket.Frame);ids.Add(ticket.Projectile);costs.Add(ticket.AmmoCost);
                    Success(t,ticket);externalUa-=ticket.AmmoCost;
                }
                else
                {
                    var before=t.Snapshot;Empty(t,ua:externalUa);
                    if(frame==40)Check(t.Snapshot.Exhausted && t.Snapshot.Projectile==before.Projectile
                        && t.Snapshot.Burst==before.Burst && !t.HasPaidTails,
                        "after last three identities the next burst terminally refuses without sequence reuse");
                }
            }
            Check(frames.SequenceEqual(new ulong[]{0,6,12})
                && ids.SequenceEqual(new uint[]{uint.MaxValue-2,uint.MaxValue-1,uint.MaxValue}),
                "literal final three-ID burst emits only0/6/12 with Max-2/Max-1/Max keys");
            Check(costs.SequenceEqual(new[]{5,0,0}) && externalUa==55,
                "last complete burst debits simulated external UA5 once, including next-first refusal");
        }
        internal void Run()
        {
            LiteralTimeline();
            ExternalSixtyUa();
            ReleasedTailsAndRefusals();
            FailedFirstNoWaitAndGaps();
            TailFailureAndCapacity();
            CancelAndOpenTransactions();
            DuplicatesStaleAbandonedAndGaps();
            ExhaustionAndLife();
            WholeBurstIdentityBoundary();
        }
    }
}
