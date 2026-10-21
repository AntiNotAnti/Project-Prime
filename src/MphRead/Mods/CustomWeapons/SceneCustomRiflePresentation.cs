using System;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using MphRead.Sound;
using OpenTK.Mathematics;

namespace MphRead;

public partial class Scene
{
    private CustomRifleTrack?[]? _customRifleTracks;
    private CustomRifleAudio? _customRifleAudio;
    private object? _customRifleAudioFailedDevice;
    private float[][]? _customRifleAudioWaves;
    internal ReadOnlySpan<float> CustomRifleWave(int cue)
        =>_customRifleAudioWaves!=null&&(uint)cue<4?_customRifleAudioWaves[cue]:ReadOnlySpan<float>.Empty;
    internal CustomRiflePose CustomRiflePose(PlayerEntity player)
        =>_customRifleTracks!=null&&(uint)player.SlotIndex<8&&_customRifleTracks[player.SlotIndex] is {} track
            ?track.Sample(FrameTiming.Active&&FrameTiming.HighRefreshPresentation?(float)FrameTiming.PresentationAlpha:0):default;
    private void PrewarmCustomRifleAudio()
    {
        if(Headless.Active||Services.IsReplica||ThumbnailMode.Active)return;
        if(_customRifleAudio?.MatchesDevice==true)return;
        _customRifleAudio?.Dispose();_customRifleAudio=null;
        var device=MusicPlayer.PlaybackDevice;
        if(device==null||ReferenceEquals(device,_customRifleAudioFailedDevice))return;
        // Package waves were decoded at activation. A device change only rebuilds
        // callback voices, never reads package bytes or decodes on a game tick.
        if(LocalWeaponPackage!=null&&_customRifleAudioWaves==null)return;
        try{_customRifleAudio=CustomRifleAudio.Prewarm(_customRifleAudioWaves);}
        catch(Exception error) when(error is not OutOfMemoryException and not StackOverflowException)
        {_customRifleAudioFailedDevice=device;DebugLog.Line("custom-weapons","Original M7 audio unavailable; native fallback: "+error.GetType().Name);}
    }
    internal void StopCustomRifleAudio()=>_customRifleAudio?.StopAll();
    private void ReleaseCustomRifleAudio()
    {_customRifleAudio?.Dispose();_customRifleAudio=null;_customRifleAudioFailedDevice=null;_customRifleTracks=null;_customRifleAudioWaves=null;}
    internal void PlayCustomRifleCue(PlayerEntity player,CustomRifleCue cue,Vector3? origin=null)
    {
        if(Headless.Active||Services.IsReplica||DemoPlayback.IsActive||ThumbnailMode.Active
            ||Mods.Replay.ReplayVideoExporter.Rendering||!Mods.Replay.ReplayAudioOwner.MayPlay(this)||player.Health<=0
            ||player.IsAltForm||player.IsMorphing||player.IsUnmorphing)return;
        if(_customRifleAudio?.Play(player.SlotIndex,cue,origin??player.Position,Audio.GetListenerPosition())!=true)
            player.ModPlayCustomRifleFallback(cue);
    }
    private void PlayCustomRifleCues(PlayerEntity player,CustomRifleCueMask cues,Vector3? origin=null)
    {
        if((cues&CustomRifleCueMask.Shot)!=0)PlayCustomRifleCue(player,CustomRifleCue.Shot,origin);
        if((cues&CustomRifleCueMask.Bolt)!=0)PlayCustomRifleCue(player,CustomRifleCue.Bolt,origin);
        if((cues&CustomRifleCueMask.Reload)!=0)PlayCustomRifleCue(player,CustomRifleCue.Reload,origin);
    }
    private void SynchronizeCustomRifleTrack(PlayerEntity player,in CustomWeaponHudState hud,WeaponMechanics? mechanics,bool online)
    {
        if(Headless.Active)return;
        int slot=player.SlotIndex;if((uint)slot>=8)return;
        if(!hud.Ready||player.Health<=0||player.IsAltForm||player.IsMorphing||player.IsUnmorphing)
        {_customRifleTracks?[slot]?.Reset();_customRifleAudio?.Stop(slot);return;}
        _customRifleTracks??=new CustomRifleTrack[8];var track=_customRifleTracks[slot]??=new();
        var context=new CustomRifleContext(hud.State.Owner,online?NetSession.ConfirmedWorldBootstrap??default:default,
            online?NetSession.CustomWeaponPresentationManifest:default,hud.State.Weapon,!online);
        if(track.Context!=context||track.Selected&&!hud.Visible)_customRifleAudio?.Stop(slot);
        var cues=track.Synchronize(context,hud.State,hud.Visible,mechanics?.ReloadTicks??138,!online);
        PlayCustomRifleCues(player,cues);
    }
    private void StepCustomRifleTracks()
    {
        if(Headless.Active)return;
        if(!Mods.Replay.ReplayAudioOwner.MayPlay(this))StopCustomRifleAudio();
        if(_customRifleTracks==null)return;
        for(int slot=0;slot<_customRifleTracks.Length;slot++)
            if(_customRifleTracks[slot] is {} track&&slot<Players.Items.Count&&Players.Items[slot] is {} player)
                PlayCustomRifleCues(player,track.Step());
    }
    private void SubmitCustomRiflePrediction(PlayerEntity player,CustomWeaponActionKind kind,uint sequence)
    {
        if(Headless.Active)return;
        int slot=player.SlotIndex;
        if(kind!=CustomWeaponActionKind.Fire||_customRifleTracks==null||(uint)slot>=8||_customRifleTracks[slot] is not {} track)return;
        var hud=CustomWeaponHud(player);
        if(track.Submitted(track.Context,sequence,hud.Ready&&hud.Visible&&hud.State.Magazine>0&&hud.State.Phase==CustomWeaponPhase.Ready))
            PlayCustomRifleCue(player,CustomRifleCue.Shot);
    }
    private void ApplyCustomRifleResult(in CustomPresentationResult result)
    {
        int slot=result.Owner.Slot;if(_customRifleTracks==null||slot>=8||_customRifleTracks[slot] is not {} track)return;
        if(track.Apply(result))_customRifleAudio?.StopShot(slot);
    }
    private void ApplyCustomRifleLaunch(in CustomReplicaEvent fact)
    {
        if(fact.Kind!=CustomReplicaKind.Launch)return;
        int slot=fact.Launch.Source.Action.Slot;
        if(_customRifleTracks==null||slot>=8||slot>=Players.Items.Count||_customRifleTracks[slot] is not {} track
            ||Players.Items[slot] is not {} player)return;
        if(track.AcceptedLaunch(fact.Launch))PlayCustomRifleCue(player,CustomRifleCue.Shot,fact.Launch.Origin);
    }
    private void DrawCustomRifleWorldEffects()
    {
        if(Headless.Active||Services.IsReplica||DemoPlayback.IsActive)return;
        float alpha=FrameTiming.Active&&FrameTiming.HighRefreshPresentation?(float)FrameTiming.PresentationAlpha:0;
        if(!NetSession.Active&&OfflineWeapons?.LastImpact is {} hit)
        {
            uint age=unchecked((uint)FrameCount-hit.Tick);
            if(age<8)CustomRiflePresentation.Spark(this,hit.Point,1-(age+alpha)/8);
        }
        if(!NetSession.CustomWeaponReplicaReady||_customPublicFacts==null)return;
        foreach(var recent in _customPublicFacts)
        {
            float age=unchecked((uint)FrameCount-recent.Tick)+alpha;
            if(recent.Fact.Kind==CustomReplicaKind.Contact&&age<8
                &&recent.Fact.Contact.Outcome is {Disposition:CustomContactDisposition.Canonical} contact
                &&contact.Health.BodyRemoved+contact.Health.TurretRemoved>0
                &&recent.Fact.Contact.Bootstrap.Start==NetSession.ConfirmedWorldBootstrap?.Start
                &&recent.Fact.Contact.Outcome.Contact.Key.Action.Manifest==NetSession.CustomWeaponPresentationManifest)
                CustomRiflePresentation.Spark(this,contact.Contact.Point,1-age/8);
            else if(recent.Fact.Kind==CustomReplicaKind.Launch&&age<6)
            {
                var launch=recent.Fact.Launch;
                if(launch.Source.Action.Bootstrap.Start!=NetSession.ConfirmedWorldBootstrap?.Start
                    ||launch.Source.Action.Manifest!=NetSession.CustomWeaponPresentationManifest)continue;
                float speed=(NetSession.CustomWeaponPresentationMechanics?.ProjectileSpeed4096??819200)/4096f;
                var from=launch.Origin;
                // Receipt age cannot prove flight freshness. Only a current
                // local bootstrap with a recent authority world can join the
                // displayed barrel; delayed/foreign launches keep their origin.
                var local=Players.Main;
                if(launch.Source.Action.Bootstrap==NetSession.ConfirmedWorldBootstrap
                    &&unchecked(NetSession.LastSnapshotFrame-launch.World)<=6
                    &&local.ModTryCustomRifleTrailStart(launch.Source.Action.Owner,CustomWeaponHud(local).State.Weapon,out var visualStart))
                    from=visualStart;
                var end=launch.Origin+launch.Direction*MathF.Min(20,(age+1)*(speed/60));
                foreach(var other in _customPublicFacts)
                    if(other.Fact.Kind==CustomReplicaKind.Contact&&other.Fact.Contact.Bootstrap==launch.Source.Action.Bootstrap
                        &&other.Fact.Contact.Outcome.Contact.Key.Action==launch.Identity
                        &&(other.Fact.Contact.Outcome.Contact.Point-launch.Origin).LengthSquared<(end-launch.Origin).LengthSquared)
                        end=other.Fact.Contact.Outcome.Contact.Point;
                CustomRiflePresentation.Trail(this,from,end,1-age/6);
            }
        }
    }

}
