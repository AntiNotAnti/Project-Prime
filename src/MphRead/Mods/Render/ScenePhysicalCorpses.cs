using System;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.Cosmetics;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
#if !MPHREAD_SERVER
using MphRead.CorpsePhysics;
#endif

namespace MphRead.Mods.Render
{
    // Explicit experimental startup opt-in. Existing cosmetic quality is the
    // live disable/budget control; no inventory or weapon acquisition setting.
    internal static class PhysicalCorpseOptions
    {
        internal static bool Enabled {get;set;}=Environment.GetEnvironmentVariable("PROJECT_PRIME_RAGDOLL_PHYSICS")=="1";
    }
    internal readonly record struct PhysicalCorpseDiagnostics(int Queued,int Active,int Bodies,int Contacts,long PoolBytes,
        ulong Created,ulong Retired,ulong Refused,ulong Faults,ulong Draws,ulong FixedSteps);
}

namespace MphRead
{
public partial class Scene
{
#if MPHREAD_SERVER
    internal PhysicalCorpseDiagnostics ModPhysicalCorpseDiagnostics=>default;
    internal IDisposable? ModBeginPhysicalBody(PlayerEntity player,Model model,HeadDeathPose pose)=>null;
    private void StepPhysicalCorpses(){}
    private void ReleasePhysicalCorpses(){}
    internal void ModReleasePhysicalCorpseWorlds(){}
#else
    private sealed class PhysicalCorpseSlot
    {
        internal readonly HeadHitVictim Victim;
        internal readonly HeadHitEvent Event;
        internal readonly NativeCorpseCapture Data;
        internal readonly CorpseStaticSnapshot Static;
        internal readonly CorpsePose[] Bodies=new CorpsePose[15];
        internal readonly uint CapturedAt;
        internal BepuCorpseBackend? Backend;
        internal CorpseHandle Handle;
        internal uint LastTick;
        internal PhysicalCorpseSlot(HeadHitVictim victim,HeadHitEvent cause,NativeCorpseCapture data,CorpseStaticSnapshot snapshot,uint at)
        {Victim=victim;Event=cause;Data=data;Static=snapshot;CapturedAt=LastTick=at;data.Primary.InitialBodies.CopyTo(Bodies);}
    }
    private PhysicalCorpseSlot?[]? _physicalCorpses;
    private int _physicalOwnerThread;
    private bool _physicalCaptureThisStep;
    private uint _physicalCaptureStep;
    private bool _physicalHasStep;
    private uint _physicalPresentationStep;
    private ulong _physicalCreated,_physicalRetired,_physicalRefused,_physicalFaults,_physicalDraws,_physicalFixedSteps;
    private bool PhysicalSceneAllowed=>!Headless.Active&&!ThumbnailMode.Active
        &&ForegroundLease.ThreadId==Environment.CurrentManagedThreadId&&PhysicalCorpseOptions.Enabled&&HeadHitVisuals
        &&!NetUnlagged.ShotScopeActive&&!DemoPlayback.IsActive&&!KillCam.Active
        &&!RenderOptions.CharacterModelReplacements&&CorpseStaticSnapshot.SceneAllowed(this);
    private bool PhysicalVictimAllowed(PlayerEntity player,HeadDeathSlot death)=>PhysicalSceneAllowed
        &&ReferenceEquals(player.OwningScene,this)&&(uint)player.SlotIndex<Players.Items.Count
        &&ReferenceEquals(Players.Items[player.SlotIndex],player)&&player.Health==0&&HeadDeathEligible(player)
        &&player.ModNativePhysicalCorpseCandidate&&player.ModCorpseDeathDefinition.WireId==0
        &&death.Track.BeganDead&&death.Track.Event.IsValid&&death.Track.Victim.Matches(HeadHitContext(player))
        &&death.Track.Sample(0,player.ModCorpseDeathDefinition).Active;
    internal PhysicalCorpseDiagnostics ModPhysicalCorpseDiagnostics
    {
        get
        {
            if(_physicalCorpses!=null&&_physicalOwnerThread!=Environment.CurrentManagedThreadId)return default;
            int queued=0,active=0,bodies=0,contacts=0;long pool=0;
            if(_physicalCorpses!=null)foreach(var slot in _physicalCorpses)if(slot!=null)
            {
                if(slot.Backend is {} backend)
                {var s=backend.Statistics;active+=s.ActiveCorpses;bodies+=s.DynamicBodies;pool+=s.ReservedPoolBytes;contacts+=Math.Max(0,backend.SolverConstraints-42);}
                else queued++;
            }
            return new(queued,active,bodies,contacts,pool,_physicalCreated,_physicalRetired,_physicalRefused,_physicalFaults,_physicalDraws,_physicalFixedSteps);
        }
    }
    private void RetirePhysicalCorpse(int index)
    {
        if(_physicalCorpses?[index] is not {} slot)return;
        _physicalCorpses[index]=null;
        try{slot.Data.Dispose();}catch{_physicalFaults++;}
        finally
        {
            try{slot.Backend?.Dispose();}catch{_physicalFaults++;}
            _physicalRetired++;
        }
    }
    private void ReleasePhysicalCorpses()
    {
        if(_physicalCorpses==null)return;
        if(_physicalOwnerThread!=Environment.CurrentManagedThreadId)return;
        for(int i=0;i<_physicalCorpses.Length;i++)RetirePhysicalCorpse(i);
        _physicalCorpses=null;
    }
    // CPU-only teardown also works after EGL context loss. Scene cleanup must
    // execute it on the original owner before abandoning the render loop.
    internal void ModReleasePhysicalCorpseWorlds()=>ReleasePhysicalCorpses();
    private bool PhysicalBudgetFits()
    {
        var stats=ModPhysicalCorpseDiagnostics;
        return stats.Queued+stats.Active<=CorpsePresentationBudget.Capacity(RenderOptions.CosmeticQuality)
            &&stats.Bodies<=120&&stats.Contacts<=512&&stats.PoolBytes<=32L*1024*1024;
    }
    private void ConsumePhysicalCauses()
    {
        // A presentation fence consumes this death's opportunity. Changing
        // settings or leaving a fenced view must not restart an aged corpse.
        if(_headDeaths!=null)
            foreach(var death in _headDeaths)if(death is {Track.HasCause:true})death.PhysicalAttempted=true;
    }
    private void StepPhysicalCorpses()
    {
        if(ForegroundLease.ThreadId!=Environment.CurrentManagedThreadId)return;
        _physicalHasStep=true;_physicalPresentationStep=HeadHitStep;
        if(!PhysicalSceneAllowed)
        {
            ConsumePhysicalCauses();
            ReleasePhysicalCorpses();return;
        }
        if(_physicalCorpses==null)return;
        uint now=HeadHitStep;
        for(int i=0;i<_physicalCorpses.Length;i++)
        {
            if(_physicalCorpses[i] is not {} slot)continue;
            if(i>=Players.Items.Count||Players.Items[i] is not {} player||_headDeaths?[i] is not {} death
                ||!PhysicalVictimAllowed(player,death)||!slot.Victim.Matches(death.Track.Victim)||slot.Event!=death.Track.Event
                ||!player.ModPhysicalCorpseModelsMatch(slot.Data)||!slot.Static.Matches(this))
            {RetirePhysicalCorpse(i);continue;}
            try
            {
                if(slot.Backend==null)
                {
                    // Capture belongs to the preceding picture. No world is
                    // created or stepped by drawing, even at high refresh.
                    if(now==slot.CapturedAt)continue; // Paused/repeated fixed picture.
                    if(unchecked(now-slot.CapturedAt)!=1){RetirePhysicalCorpse(i);continue;}
                    var v=slot.Victim;var key=new CorpseKey(new((ulong)v.Scene,v.Match,v.Epoch,v.Slot,v.Generation,v.Life,v.Timeline),
                        new(_physicalCreated+1,(ulong)(uint)i+1,(ulong)now+1,1));
                    if(_physicalCreated==ulong.MaxValue){RetirePhysicalCorpse(i);continue;}
                    slot.Backend=new BepuCorpseBackend(slot.Static.World,CorpseQuality.Low,true);
                    var pose=death.Track.Sample(0,player.ModCorpseDeathDefinition);
                    var spawn=new CorpseSpawnDescriptor(key,slot.Data.Primary.Rig,slot.Data.Primary.Origin,
                        CorpseMatrixConversion.Copy(pose.Direction),default,null,pose.Side<0?0u:1u,now,HeadDeathTrack.Duration);
                    if(!slot.Backend.TrySpawn(spawn,out slot.Handle)){_physicalRefused++;RetirePhysicalCorpse(i);continue;}
                    _physicalCreated++;slot.LastTick=now;
                }
                else if(now!=slot.LastTick)
                {
                    if(unchecked(now-slot.LastTick)!=1||!slot.Backend.Step(now))
                    {RetirePhysicalCorpse(i);continue;}
                    slot.LastTick=now;_physicalFixedSteps++;
                }
                if(!slot.Backend.Sample(slot.Handle,1,slot.Bodies)||!slot.Static.Contains(slot.Bodies,slot.Data.Primary.Rig))
                    RetirePhysicalCorpse(i);
            }
            catch{_physicalFaults++;RetirePhysicalCorpse(i);}
        }
        if(!PhysicalBudgetFits())ReleasePhysicalCorpses();
    }
    internal IDisposable? ModBeginPhysicalBody(PlayerEntity player,Model model,HeadDeathPose pose)
    {
        if(ForegroundLease.ThreadId!=Environment.CurrentManagedThreadId)return null;
        if(!PhysicalSceneAllowed){ConsumePhysicalCauses();ReleasePhysicalCorpses();return null;}
        if(!_physicalHasStep)return null;
        int index=player.SlotIndex;
        if(!pose.Active||(uint)index>=8||_headDeaths?[index] is not {} death||!PhysicalVictimAllowed(player,death))return null;
        if(_physicalCorpses?[index] is {} slot)
        {
            if(!slot.Victim.Matches(death.Track.Victim)||slot.Event!=death.Track.Event||!player.ModPhysicalCorpseModelsMatch(slot.Data)
                ||!slot.Static.Matches(this)||!PhysicalBudgetFits())
            {RetirePhysicalCorpse(index);return null;}
            if(slot.Backend==null)return null;
            try
            {
                float alpha=FrameTiming.Active&&FrameTiming.HighRefreshPresentation?(float)FrameTiming.PresentationAlpha:1;
                if(!slot.Backend.Sample(slot.Handle,alpha,slot.Bodies)||!slot.Static.Contains(slot.Bodies,slot.Data.Primary.Rig))
                {RetirePhysicalCorpse(index);return null;}
                foreach(var palette in slot.Data.Palettes)if(ReferenceEquals(palette.Model,model))
                {var lease=palette.Begin(slot.Bodies);if(lease!=null)_physicalDraws++;return lease;}
                RetirePhysicalCorpse(index);return null;
            }
            catch{_physicalFaults++;RetirePhysicalCorpse(index);return null;}
        }
        if(death.PhysicalAttempted)return null;
        if(_physicalCaptureThisStep&&_physicalCaptureStep==_physicalPresentationStep)return null;
        _physicalCaptureThisStep=true;_physicalCaptureStep=_physicalPresentationStep;death.PhysicalAttempted=true;
        NativeCorpseCapture? capture=null;
        try
        {
            if(!player.ModCapturePhysicalCorpse(model,pose,out capture)||capture==null){_physicalRefused++;return null;}
            // Origin is already Numerics, no second root/scale conversion.
            CorpseStaticSnapshot? snapshot=null;
            foreach(float extent in new[]{4f,3f,2f})
                if(CorpseStaticSnapshot.TryCapture(this,capture!.Primary.Origin.Position,extent,out var candidate)
                    &&candidate!.Contains(capture.Primary.InitialBodies,capture.Primary.Rig))
                {snapshot=candidate;break;}
            if(snapshot==null){_physicalRefused++;capture.Dispose();return null;}
            if(_physicalCorpses==null)
            {_physicalOwnerThread=Environment.CurrentManagedThreadId;_physicalCorpses=new PhysicalCorpseSlot[8];}
            // FrameCount increments after StepHeadDeathPresentation. Keep the
            // completed presentation step, not the subsequent draw counter.
            _physicalCorpses[index]=new(death.Track.Victim,death.Track.Event,capture,snapshot,_physicalPresentationStep);
            if(!PhysicalBudgetFits())RetirePhysicalCorpse(index);
        }
        catch{_physicalFaults++;try{capture?.Dispose();}catch{_physicalFaults++;}RetirePhysicalCorpse(index);}
        return null;
    }
#endif
}
}
