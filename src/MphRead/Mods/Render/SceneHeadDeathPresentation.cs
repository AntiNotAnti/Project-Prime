using System;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using MphRead.Mods.Cosmetics.Death;
using OpenTK.Mathematics;

namespace MphRead;

public partial class Scene
{
    private sealed class HeadDeathSlot
    {
        internal readonly HeadDeathTrack Track;
        internal HeadDeathSlot(HeadDeathTrack? track=null) => Track=track??new();
        internal HeadHitFact Pending;
        internal uint PendingAt;
        internal bool PendingHead,PendingEligible,AliveSeen,WasFrozen,WasAlt,Anchored,DeadBlocked,PhysicalAttempted;
        internal Vector3 LastPosition,Position,Facing;
        internal Hunter Hunter;
    }
    private HeadDeathSlot?[]? _headDeaths;
    private bool HeadDeathEligible(PlayerEntity player)=>HeadHitVisuals
        &&(Services.IsReplica||!NetSession.IsClient||NetSession.SnapshotAge<=HeadHitTrack.MaximumAge)
        &&(uint)player.Hunter<7&&!player.ModFrozen&&!player.Flags2.TestFlag(PlayerFlags2.Spectating)
        &&(player.LoadFlags&(LoadFlags.Active|LoadFlags.Spawned))==(LoadFlags.Active|LoadFlags.Spawned)
        &&!player.IsAltForm&&!player.IsMorphing&&!player.IsUnmorphing;
    private HeadDeathSlot? HeadDeathOwner(PlayerEntity player)
    {
        if(Headless.Active||ThumbnailMode.Active||player.OwningScene!=this||(uint)player.SlotIndex>=8)return null;
        _headDeaths??=new HeadDeathSlot[8];var slot=_headDeaths[player.SlotIndex]??=new();
        var context=HeadHitContext(player);
        if(!slot.Track.Victim.Matches(context)||slot.Hunter!=player.Hunter)
        {
            slot.Track.Bind(default);slot.Track.Bind(context);slot.Pending=default;
            slot.AliveSeen=slot.WasFrozen=slot.WasAlt=slot.Anchored=slot.DeadBlocked=slot.PhysicalAttempted=false;
            slot.LastPosition=player.Position;slot.Hunter=player.Hunter;
        }
        else if((slot.LastPosition-player.Position).LengthSquared>16)
        {slot.Track.Cancel();slot.PendingEligible=false;if(player.Health==0)slot.DeadBlocked=true;}
        slot.LastPosition=player.Position;
        if(player.Health>0)
        {slot.AliveSeen=true;slot.WasFrozen=player.ModFrozen;slot.WasAlt=player.IsAltForm||player.IsMorphing||player.IsUnmorphing;}
        if(!HeadDeathEligible(player)){slot.Track.Cancel();slot.PendingEligible=false;}
        return slot;
    }
    private void OfferHeadDeath(PlayerEntity player,HeadDeathSlot slot,in HeadHitFact fact,bool headshot,bool wasFrozen=false)
    {
        if(slot.Track.HasCause||slot.Pending.Event.IsValid)return;
        bool eligible=HeadDeathEligible(player)&&!slot.DeadBlocked&&!slot.WasFrozen&&!slot.WasAlt&&!wasFrozen
            &&(slot.AliveSeen||Services.IsReplica);
        int ahead=unchecked((int)(fact.ResolveWorld-HeadHitWorld));
        if(ahead>0)
        {
            if(ahead>HeadDeathTrack.PendingLifetime)return;
            slot.Pending=fact;slot.PendingAt=HeadHitStep;slot.PendingHead=headshot;slot.PendingEligible=eligible;
            return;
        }
        if(slot.Track.Accept(fact,HeadHitWorld,HeadHitStep,headshot,eligible,Services.IsReplica))
            player.ModPrewarmHeadDeathPose();
        AdvanceHeadDeath(player,slot);
    }
    private void AdvanceHeadDeath(PlayerEntity player,HeadDeathSlot slot)
    {
        bool dead=player.Health==0;
        slot.Track.Step(HeadHitStep,dead,HeadDeathEligible(player));
        if(dead&&slot.Track.BeganDead&&!slot.Anchored)
        {
            var cosmetic=player.CosmeticDeathState;
            slot.Position=cosmetic.Active?cosmetic.Position:player.Position;
            slot.Facing=cosmetic.Active?cosmetic.Facing:player.FacingVector;slot.Anchored=true;
        }
    }
    internal void ModAcceptLocalDeath(PlayerEntity player,bool headshot,Vector3 direction,bool wasFrozen)
    {
        if(NetSession.Active||Services.IsReplica||DemoPlayback.IsActive||player.Health!=0||HeadDeathOwner(player) is not {} slot)return;
        if(++_localHeadHitSerial==0)return;
        var key=new HeadHitEvent(HeadHitDomain.Local,default,default,default,_localHeadHitSerial,0);
        OfferHeadDeath(player,slot,new(slot.Track.Victim,key,(uint)FrameCount,direction),headshot,wasFrozen);
    }
    private void AcceptNativeHeadDeath(PlayerEntity player,in ReplayShotFact fact,uint? recordingWorld)
    {
        if(!fact.Lethal||fact.HealthAfter!=0||fact.Damage==0||fact.Weapon>(byte)BeamType.OmegaCannon
            ||!float.IsFinite(fact.ImpactPoint.X)||!float.IsFinite(fact.ImpactPoint.Y)||!float.IsFinite(fact.ImpactPoint.Z)
            ||HeadDeathOwner(player) is not {} slot)return;
        if(recordingWorld.HasValue&&Services is not ReplaySceneServices)return;
        var context=slot.Track.Victim;
        if(context.Match!=fact.MatchId||context.Epoch!=fact.AuthorityEpoch||context.Generation!=fact.VictimGeneration
            ||context.Slot!=fact.VictimSlot||context.Life!=fact.VictimLifeId)return;
        var id=new HeadHitEvent(HeadHitDomain.Native,new(fact.AuthorityEpoch,fact.MatchId,fact.ShooterSlot,
            fact.ShooterGeneration,fact.ShooterLifeId,fact.ShotId),default,default,0,fact.DamageEventId);
        if(!id.IsValid)return;
        bool head=HeadHitFact.TryNativeDeath(context,fact,out var normalized);
        var cause=new HeadHitFact(context,id,recordingWorld??fact.ResolveTick,head?normalized.Direction:default);
        OfferHeadDeath(player,slot,cause,head);
    }
    private void AcceptCustomHeadDeath(PlayerEntity player,WorldBootstrapIdentity bootstrap,in CustomContactOutcome hit,Vector3 direction,uint? recordingWorld=null)
    {
        if(recordingWorld.HasValue&&Services is not ReplaySceneServices)return;
        if(hit.Disposition!=CustomContactDisposition.Canonical||!hit.Contact.IsValid||!hit.Health.IsValid||!hit.Lethal
            ||HeadDeathOwner(player) is not {} slot)return;
        var victim=hit.Contact.Key.Victim;var owner=hit.Contact.Key.Action.Owner;var context=slot.Track.Victim;
        if(context.Slot!=victim.Slot||context.Generation!=victim.Generation||context.Life!=victim.Life
            ||context.Match!=owner.MatchId||context.Epoch!=owner.AuthorityEpoch)return;
        var id=new HeadHitEvent(HeadHitDomain.Custom,default,hit.Contact.Key,bootstrap,0,hit.Health.DamageEventId);
        if(!id.IsValid)return;
        bool head=HeadHitFact.TryCustomDeath(slot.Track.Victim,bootstrap,hit,direction,out var normalized);
        OfferHeadDeath(player,slot,new(slot.Track.Victim,id,recordingWorld??hit.AuthorityFrame,head?normalized.Direction:default),head);
    }
    internal HeadDeathPose ModHeadDeathPose(PlayerEntity player,DeathPresentationDefinition definition)
    {
        if(!HeadDeathEligible(player)||player.Health!=0||_headDeaths==null||(uint)player.SlotIndex>=8
            ||_headDeaths[player.SlotIndex] is not {} slot||!slot.Track.Victim.Matches(HeadHitContext(player)))return default;
        return slot.Track.Sample(FrameTiming.Active&&FrameTiming.HighRefreshPresentation?(float)FrameTiming.PresentationAlpha:0,definition);
    }
    internal bool ModHeadDeathAnchor(PlayerEntity player,out Vector3 position,out Vector3 facing)
    {
        position=facing=default;
        if(_headDeaths==null||(uint)player.SlotIndex>=8||_headDeaths[player.SlotIndex] is not {Anchored:true} slot
            ||!slot.Track.Victim.Matches(HeadHitContext(player)))return false;
        position=slot.Position;facing=slot.Facing;return true;
    }
    private void StepHeadDeathPresentation()
    {
        if(Headless.Active||ThumbnailMode.Active){ReleasePhysicalCorpses();return;}
        foreach(var player in Players.Items)if(player!=null&&HeadDeathOwner(player) is {} slot)
        {
            if(slot.Pending.Event.IsValid)
            {
                var pending=slot.Pending;
                if(!slot.Track.Victim.Matches(pending.Victim)||unchecked(HeadHitStep-slot.PendingAt)>HeadDeathTrack.PendingLifetime)
                {slot.Track.Suppress(pending);slot.Pending=default;}
                else if(unchecked((int)(HeadHitWorld-pending.ResolveWorld))>=0)
                {
                    slot.Pending=default;
                    if(slot.Track.Accept(pending,HeadHitWorld,HeadHitStep,slot.PendingHead,slot.PendingEligible&&HeadDeathEligible(player),Services.IsReplica))
                        player.ModPrewarmHeadDeathPose();
                }
            }
            AdvanceHeadDeath(player,slot);
        }
        if(Services is ReplaySceneServices replay&&ReplayPoses is {} poses)
        {
            uint frame=replay.Session.RecordingFrame,first=frame>HeadDeathTrack.MaximumDuration?frame-HeadDeathTrack.MaximumDuration:0;
            for(uint at=first;;at++)
            {
                foreach(var fact in poses.ResolvedShotFactsAt(at))
                    if(fact.VictimSlot<Players.Items.Count&&Players.Items[fact.VictimSlot] is {} player)
                        AcceptNativeHeadDeath(player,fact,at);
                if(at==frame)break;
            }
        }
        ApplyHeadDeathBudget();
        StepPhysicalCorpses();
    }
    private void ApplyHeadDeathBudget()
    {
        if(_headDeaths==null)return;
        Span<int> candidates=stackalloc int[8];Span<float> distances=stackalloc float[8];int count=0;
        var quality=RenderOptions.CosmeticQuality;int capacity=CorpsePresentationBudget.Capacity(quality);
        Vector3 camera=Players.Main.CameraInfo.Position;
        for(int i=0;i<Players.Items.Count&&i<8;i++)
        {
            if(Players.Items[i] is not {} player||_headDeaths[i] is not {} slot
                ||!slot.Track.Sample(0,player.ModCorpseDeathDefinition).Active)continue;
            float distance=((slot.Anchored?slot.Position:player.Position)-camera).LengthSquared;
            if(!CorpsePresentationBudget.Within(quality,distance)){slot.Track.Cancel();continue;}
            int at=count++;
            while(at>0&&(distance<distances[at-1]||distance==distances[at-1]&&i<candidates[at-1]))
            {candidates[at]=candidates[at-1];distances[at]=distances[at-1];at--;}
            candidates[at]=i;distances[at]=distance;
        }
        for(int i=capacity;i<count;i++)_headDeaths[candidates[i]]!.Track.Cancel();
    }
    private void ReleaseHeadDeathPresentation(){ReleasePhysicalCorpses();_headDeaths=null;}
}
