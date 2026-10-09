using System;
using MphRead.Entities;
using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

internal static class ImpactPresentationRules
{
    // Source spawn ordinal, deliberately separate from the authority-only witness serial.
    // A child whose ancestry cannot be encoded is unknown, never nearest-matched.
    internal static uint Component(uint parent, int ordinal, bool child)
        // Child emission can occur in repeated spawn calls with the same ordinal.
        // Until the fire stream carries that lineage, only primary ordinals correlate across views.
        => child || ordinal is < 0 or >= 15 ? 0 : (uint)(ordinal + 1);
    internal static Vector3 Point(in LiveCombatImpact impact, Vector3 victimPosition)
        => impact.Presentation.HasBodyOffset ? victimPosition + impact.Presentation.BodyOffset : impact.Fact.ImpactPoint;
    internal static bool CanCorrelate(Vector3 position, Vector3 velocity, Vector3 point)
    {
        Vector3 delta = point - position;
        if (!float.IsFinite(delta.LengthSquared) || delta.LengthSquared > 16) return false;
        if (delta.LengthSquared <= .0625f) return true;
        return velocity.LengthSquared > .000001f
            && Vector3.Dot(delta.Normalized(), velocity.Normalized()) >= .5f;
    }
    internal static bool SameBlast(in LiveCombatImpact a, in LiveCombatImpact b)
        => (a.Presentation.Kind == CombatImpactKind.Splash || b.Presentation.Kind == CombatImpactKind.Splash)
        && a.Presentation.Kind is CombatImpactKind.Direct or CombatImpactKind.Splash
        && b.Presentation.Kind is CombatImpactKind.Direct or CombatImpactKind.Splash
        && a.Identity.Shot == b.Identity.Shot && a.Presentation.Component == b.Presentation.Component
        && a.Presentation.Component != 0 && a.Fact.Weapon == b.Fact.Weapon
        && unchecked(a.Fact.ResolveTick-b.Fact.ResolveTick+2) <= 4
        && (a.Fact.ImpactPoint-b.Fact.ImpactPoint).LengthSquared <= .0625f;
}

/// <summary>Bounded scene-local lookup refreshed after packet decode; it never owns a beam's simulation.</summary>
internal sealed class NetLiveProjectileIndex
{
    internal const int Capacity = 256;
    private readonly BeamProjectileEntity?[] _beams = new BeamProjectileEntity?[Capacity];
    private int _count;
    internal void Refresh(Scene scene)
    {
        Reset();
        foreach (var beam in scene.GetBeamProjectileEntities())
        {
            if (_count == Capacity) break;
            if (beam.ModShotId != 0 && beam.ModPresentationComponent != 0) _beams[_count++] = beam;
        }
    }
    internal BeamProjectileEntity? Find(in LiveCombatImpact impact, out bool ambiguous)
    {
        ambiguous = false; BeamProjectileEntity? match = null;
        if (impact.Presentation.Component == 0) return null;
        for (int i=0;i<_count;i++)
        {
            var beam=_beams[i]!;
            if (beam.ModPresentationComponent != impact.Presentation.Component
                || !BeamProjectileEntity.ModReplayIdentityMatches(beam.ModLaunchKey,beam.ModShotId,beam.Beam,impact.Fact)) continue;
            if (match != null) { ambiguous=true; return null; }
            match=beam;
        }
        return match;
    }
    internal void Reset() { Array.Clear(_beams); _count=0; }
}

/// <summary>Only submits transient draw particles. Never calls TakeDamage or changes an entity transform.</summary>
internal static class NetLiveImpactPresenter
{
    internal const uint HoldFrames = 3, CueFrames = 6;
    private struct Pending { internal LiveCombatImpact Impact; internal uint Arrived, Presented; internal bool Live, Ready, Suppressed; }
    private readonly record struct Observed(ShotKey Shot, uint Component, byte Weapon, Vector3 Point, uint Frame);
    private static readonly Pending[] PendingEvents = new Pending[128];
    private static readonly Observed[] ObservedImpacts = new Observed[128];
    private static readonly NetLiveProjectileIndex Index = new();
    private static int _observedCursor;
    private static Scene? _scene;
    internal static void NoteNativeImpact(BeamProjectileEntity beam, Vector3 point)
    {
        if (!NetCombatFactPublisher.LiveEnabled || beam.OwningScene.Services.IsReplica
            || !beam.OwningScene.Services.AllowsPresentationSideEffects
            || NetSession.Role != NetRole.Client || beam.ModShotId == 0) return;
        if (_scene != beam.OwningScene) { Reset(); _scene = beam.OwningScene; }
        ObservedImpacts[_observedCursor] = new(beam.ModLaunchKey,beam.ModPresentationComponent,(byte)beam.Beam,point,NetSession.NetFrame);
        _observedCursor=(_observedCursor+1)%ObservedImpacts.Length;
    }
    private static bool AlreadyVisible(in LiveCombatImpact impact)
    {
        foreach (var observed in ObservedImpacts)
            if (observed.Component!=0 && observed.Component==impact.Presentation.Component
                && observed.Shot==impact.Identity.Shot && observed.Weapon==impact.Fact.Weapon
                && NetSession.NetFrame-observed.Frame<=NetLiveImpactInbox.MaxAgeFrames
                && (observed.Point-impact.Fact.ImpactPoint).LengthSquared<=.0625f) return true;
        return false;
    }
    internal static void Draw(Scene scene)
    {
        if (!NetCombatFactPublisher.LiveEnabled || NetSession.Role!=NetRole.Client || scene.Services.IsReplica
            || !scene.Services.AllowsPresentationSideEffects || DemoPlayback.IsActive) return;
        if (_scene != scene) { Reset(); _scene=scene; }
        Index.Refresh(scene);
        while(NetLiveImpactInbox.TryTake(out var impact,out uint arrived))
        {
            for(int i=0;i<PendingEvents.Length;i++) if(!PendingEvents[i].Live)
            { PendingEvents[i]=new(){Impact=impact,Arrived=arrived,Live=true}; break; }
        }
        uint now=NetSession.NetFrame;
        foreach(ref var pending in PendingEvents.AsSpan())
        {
            if(!pending.Live) continue;
            var impact=pending.Impact;
            if(!NetLiveImpactInbox.Current(impact) || now-pending.Arrived>NetLiveImpactInbox.MaxAgeFrames
                || pending.Ready && now-pending.Presented>=CueFrames)
            { pending=default; continue; }
            if(!pending.Ready)
            {
                var beam=Index.Find(impact,out bool ambiguous);
                if(beam==null && now-pending.Arrived<HoldFrames) continue;
                pending.Ready=true;pending.Presented=now;
                pending.Suppressed=AlreadyVisible(impact);
                // A single native blast reaching multiple victims needs one cue.
                foreach(var other in PendingEvents)
                    if(other.Live && other.Ready && !other.Suppressed && other.Impact.Identity!=impact.Identity
                        && ImpactPresentationRules.SameBlast(other.Impact,impact)) pending.Suppressed=true;
                bool correlated=beam!=null && ImpactPresentationRules.CanCorrelate(beam.Position,beam.Velocity,impact.Fact.ImpactPoint);
                // There is no corrected tracer: a discrepant or blocked path gets only its truthful endpoint cue.
                if(correlated)
                {
                    CollisionResult collision=default;
                    correlated=!CollisionDetection.CheckBetweenPoints(beam!.Position,impact.Fact.ImpactPoint,TestFlags.Beams,scene,ref collision);
                }
                NetImpactDiagnostics.Record(impact.Fact,pending.Suppressed?ImpactStage.AlreadyVisible
                    :ambiguous?ImpactStage.Ambiguous:correlated?ImpactStage.Matched:ImpactStage.Synthesized,impact.Presentation.Component);
            }
            if(pending.Suppressed) continue;
            Vector3 victimPosition=scene.Players.Items[impact.Fact.VictimSlot].Position;
            if(NetSmoothing.SamplePresentation(impact.Fact.VictimSlot,out Vector3 smooth,out _)) victimPosition=smooth;
            Vector3 point=ImpactPresentationRules.Point(impact,victimPosition);
            CombatImpactDrawing.Draw(scene,impact.Fact,point,1-(now-pending.Presented)/(float)CueFrames);
        }
    }
    internal static void Reset()
    { Array.Clear(PendingEvents);Array.Clear(ObservedImpacts);Index.Reset();_observedCursor=0;_scene=null; }
}

internal static class CombatImpactDrawing
{
    internal static void Draw(Scene scene,in ReplayShotFact fact,Vector3 point,float alpha=1)
        => scene.AddSingleParticle(SingleType.Fuzzball,point,BeamProjectileEntity.ModReplayImpactColor(scene,fact),
            alpha,fact.Headshot?.46f:.32f);
}
