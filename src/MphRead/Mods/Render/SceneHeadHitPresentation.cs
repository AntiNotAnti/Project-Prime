using System;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Cosmetics;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Hud;
using OpenTK.Mathematics;

namespace MphRead;

public partial class Scene
{
    private sealed class HeadHitSlot
    {
        internal readonly HeadHitTrack Track;
        internal HeadHitSlot(HeadHitTrack? track=null) => Track=track??new();
        internal Vector3 Position;
        internal uint NotBefore;
        internal bool Terminated;
    }
    private HeadHitSlot?[]? _headHits;
    private ulong _localHeadHitSerial;
    private readonly record struct PendingHeadHit(HeadHitFact Fact,uint QueuedStep,bool Eligible);
    private PendingHeadHit[]? _pendingHeadHits;
    private bool HeadHitVisuals=>!Headless.Active&&!ThumbnailMode.Active
        &&RenderOptions.ShowCustomCosmetics&&RenderOptions.CosmeticQuality!=CosmeticEffectQuality.Off
        &&!HudProfiles.Runtime.ReduceMotion;
    private bool HeadHitEligible(PlayerEntity player)=>HeadHitVisuals
        &&(Services.IsReplica||!NetSession.IsClient||NetSession.SnapshotAge<=HeadHitTrack.MaximumAge)&&player.Health>0
        &&(uint)player.Hunter<7&&!player.ModFrozen&&!player.IsAltForm&&!player.IsMorphing&&!player.IsUnmorphing
        &&!player.Flags2.TestFlag(PlayerFlags2.Spectating)
        &&(player.LoadFlags&(LoadFlags.Active|LoadFlags.Spawned))==(LoadFlags.Active|LoadFlags.Spawned);
    private uint HeadHitWorld=>Services is ReplaySceneServices replay?replay.Session.RecordingFrame
        :NetSession.Active?NetSession.LastSnapshotFrame:(uint)FrameCount;
    private uint HeadHitStep=>Services is ReplaySceneServices replay?replay.Session.RecordingFrame:(uint)FrameCount;
    private HeadHitVictim HeadHitContext(PlayerEntity player)
    {
        ushort match=0,generation=0;ulong epoch=0;uint life=(uint)Math.Max(0,GameState.SpawnOrdinals[player.SlotIndex]);
        long timeline=FrameTiming.PresentationGeneration;
        if(Services is ReplaySceneServices replay)
        {
            timeline=replay.Session.Transport.SeekGeneration;
            if(replay.State.Match is {} state&&replay.State.TryGetPlayer(player.SlotIndex,out var actor))
            {match=state.MatchId;epoch=state.AuthorityEpoch;generation=actor.SlotGeneration;life=actor.LifeId;}
            else life=0;
        }
        else if(NetSession.Active)
        {match=NetSession.CurrentMatchId;epoch=NetSession.AuthorityEpoch;generation=NetPlayerLifecycle.Generation(player.SlotIndex);life=NetPlayerLifecycle.Get(player.SlotIndex);}
        return new(LifetimeGeneration,player,(byte)player.SlotIndex,match,epoch,generation,life,timeline);
    }
    private HeadHitSlot? HeadHitOwner(PlayerEntity player)
    {
        if(Headless.Active||ThumbnailMode.Active||player.OwningScene!=this||(uint)player.SlotIndex>=8)return null;
        _headHits??=new HeadHitSlot[8];var slot=_headHits[player.SlotIndex]??=new();
        var context=HeadHitContext(player);uint now=HeadHitWorld;
        if(!slot.Track.Victim.Matches(context))
        {slot.Track.Bind(context);slot.Terminated=false;slot.NotBefore=Services.IsReplica&&now>HeadHitTrack.Duration?now-HeadHitTrack.Duration:Services.IsReplica?0:now;slot.Position=player.Position;}
        else if((slot.Position-player.Position).LengthSquared>16)
        {CancelHeadHit(slot);slot.NotBefore=now;}
        slot.Position=player.Position;
        if(!HeadHitEligible(player)){CancelHeadHit(slot);slot.NotBefore=now;}
        return slot;
    }
    private void CancelHeadHit(HeadHitSlot slot)
    {
        slot.Track.Cancel();
        if(_pendingHeadHits!=null)for(int i=0;i<_pendingHeadHits.Length;i++)
            if(_pendingHeadHits[i].Fact.Victim.Matches(slot.Track.Victim))
                _pendingHeadHits[i]=_pendingHeadHits[i] with{Eligible=false};
    }
    private void OfferHeadHit(PlayerEntity player,HeadHitSlot slot,in HeadHitFact fact)
    {
        if(unchecked((int)(fact.ResolveWorld-slot.NotBefore))<0)return;
        bool eligible=HeadHitEligible(player)&&!slot.Terminated;
        int ahead=unchecked((int)(fact.ResolveWorld-HeadHitWorld));
        if(ahead>0)
        {
            // Reliable accepted results can beat the next world snapshot. Keep
            // only normalized immutable cosmetic values, never advance health
            // or retag an old resolve tick as the arrival time.
            if(ahead>HeadHitTrack.MaximumAge)return;
            _pendingHeadHits??=new PendingHeadHit[64];int free=-1;
            for(int i=0;i<_pendingHeadHits.Length;i++)
            {
                ref var pending=ref _pendingHeadHits[i];
                if(pending.Fact.Event==fact.Event&&pending.Fact.Victim.Matches(fact.Victim))return;
                if(!pending.Fact.Event.IsValid)free=i;
            }
            if(free>=0)_pendingHeadHits[free]=new(fact,HeadHitStep,eligible);
            return;
        }
        if(slot.Track.Offer(fact,HeadHitWorld,eligible,HeadHitStep))player.ModPrewarmHeadHitPose();
    }
    internal void ModAcceptLocalHeadHit(PlayerEntity player,Vector3 direction)
    {
        if(NetSession.Active||Services.IsReplica||DemoPlayback.IsActive||HeadHitOwner(player) is not {} slot)return;
        if(++_localHeadHitSerial==0)return;
        var key=new HeadHitEvent(HeadHitDomain.Local,default,default,default,_localHeadHitSerial,0);
        OfferHeadHit(player,slot,new(slot.Track.Victim,key,(uint)FrameCount,direction));
    }
    internal void ModAcceptNativeHeadHit(in ReplayShotFact fact,uint? recordingWorld=null)
    {
        if(fact.VictimSlot>=Players.Items.Count||Players.Items[fact.VictimSlot] is not {} player
            ||HeadHitOwner(player) is not {} slot)return;
        var victim=slot.Track.Victim;
        if(victim.Match!=fact.MatchId||victim.Epoch!=fact.AuthorityEpoch||victim.Generation!=fact.VictimGeneration
            ||victim.Life!=fact.VictimLifeId)return;
        if(fact.Lethal&&fact.Damage>0)
        {CancelHeadHit(slot);slot.Terminated=true;AcceptNativeHeadDeath(player,fact,recordingWorld);return;}
        if(!HeadHitFact.TryNative(victim,fact,out var hit))return;
        // Only the passive replica may map an accepted authority tick onto its
        // existing recorded clock. Live ingress always uses ResolveTick.
        if(recordingWorld is {} at)
        {if(Services is not ReplaySceneServices)return;hit=hit with{ResolveWorld=at};}
        OfferHeadHit(player,slot,hit);
    }
    private void ApplyCustomHeadHit(in CustomReplicaEvent fact)
    {
        if(fact.Kind!=CustomReplicaKind.Contact||!NetSession.CustomWeaponReplicaReady||Services.IsReplica)return;
        var message=fact.Contact;var hit=message.Outcome;var key=hit.Contact.Key;
        if(message.Bootstrap.Start!=NetSession.ConfirmedWorldBootstrap?.Start
            ||key.Action.Manifest!=NetSession.CustomWeaponPresentationManifest||key.Victim.Slot>=Players.Items.Count
            ||Players.Items[key.Victim.Slot] is not {} player||HeadHitOwner(player) is not {} slot)return;
        if(slot.Track.Victim.Generation!=key.Victim.Generation||slot.Track.Victim.Life!=key.Victim.Life)return;
        Vector3 direction=default;
        if(_customPublicFacts!=null)foreach(var recent in _customPublicFacts)
            if(recent.Fact.Kind==CustomReplicaKind.Launch&&recent.Fact.Launch.Source.Action.Bootstrap==message.Bootstrap
                &&recent.Fact.Launch.Identity==key.Action){direction=recent.Fact.Launch.Direction;break;}
        if(hit.Disposition==CustomContactDisposition.Canonical&&hit.Lethal)
        {CancelHeadHit(slot);slot.Terminated=true;AcceptCustomHeadDeath(player,message.Bootstrap,hit,direction);return;}
        if(HeadHitFact.TryCustom(slot.Track.Victim,message.Bootstrap,hit,direction,out var normalized))
            OfferHeadHit(player,slot,normalized);
    }
    internal HeadHitPose ModHeadHitPose(PlayerEntity player)
    {
        // Sampling cannot bind a track, load an asset, consume a fact or step.
        if(!HeadHitEligible(player)||_headHits==null||(uint)player.SlotIndex>=8
            ||_headHits[player.SlotIndex] is not {} slot||slot.Terminated||!slot.Track.Victim.Matches(HeadHitContext(player)))return default;
        return slot.Track.Sample(FrameTiming.Active&&FrameTiming.HighRefreshPresentation?(float)FrameTiming.PresentationAlpha:0);
    }
    private void StepHeadHitPresentation()
    {
        if(Headless.Active||ThumbnailMode.Active)return;
        foreach(var player in Players.Items)
            if(player!=null&&HeadHitOwner(player) is {} slot)slot.Track.Step(HeadHitStep,HeadHitEligible(player)&&!slot.Terminated);
        if(_pendingHeadHits!=null)for(int i=0;i<_pendingHeadHits.Length;i++)
        {
            var pending=_pendingHeadHits[i];var hit=pending.Fact;
            if(!hit.Event.IsValid)continue;
            int slotIndex=hit.Victim.Slot;
            if(unchecked((uint)(HeadHitStep-pending.QueuedStep))>HeadHitTrack.MaximumAge
                ||slotIndex>=Players.Items.Count||_headHits?[slotIndex] is not {} slot
                ||!slot.Track.Victim.Matches(hit.Victim)||unchecked((int)(hit.ResolveWorld-slot.NotBefore))<0)
            {_pendingHeadHits[i]=default;continue;}
            if(unchecked((int)(HeadHitWorld-hit.ResolveWorld))<0)continue;
            _pendingHeadHits[i]=default;
            var player=Players.Items[slotIndex];
            if(slot.Track.Offer(hit,HeadHitWorld,pending.Eligible&&HeadHitEligible(player)&&!slot.Terminated,HeadHitStep))
                player.ModPrewarmHeadHitPose();
        }
        if(Services is ReplaySceneServices replay&&ReplayPoses is {} poses)
        {
            uint frame=replay.Session.RecordingFrame;
            // Reconstruct only this bounded short overlay from existing accepted
            // replay history. Seek-generation contexts reset old dedupe/pose;
            // repeated pictures cannot re-offer events or accumulate matrices.
            uint first=frame>HeadHitTrack.Duration?frame-HeadHitTrack.Duration:0;
            for(uint at=first;;at++)
            {
                foreach(var fact in poses.ResolvedShotFactsAt(at))ModAcceptNativeHeadHit(fact,at);
                if(at==frame)break;
            }
        }
    }
    private void ReleaseHeadHitPresentation(){_headHits=null;_localHeadHitSerial=0;_pendingHeadHits=null;}
}
