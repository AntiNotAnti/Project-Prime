#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Immutable;
using System.Linq;
using MphRead.Entities;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;
namespace MphRead.Mods.Launcher.RmlUi.Render;
internal static partial class RmlUiOpenGlSmoke
{
    private sealed partial class CurrentReplayEditor : ITheatreWorkspaceCombatBackend
    {
        private sealed record CombatEvidence(ImmutableArray<ReplayCombatDiagnostic> Shots);
        private bool _workspaceShooterView=true;
        private sealed record RetainedCombat(TheatreEditorContext Owner, ReplayCombatDiagnostic Shot, long Seek);
        private RetainedCombat? _workspaceRetainedCombat;
        public TheatreCombatReview ReadCombat(TheatreEditorContext context)
        {
            lock(ReplayAnnotationWitness.Gate)
            {
                Validate(context,true);
                var scene=DemoPlayback.PresentationScene;
                if(scene==null||!DemoPlayback.Owns(scene)||scene.ReplayPoses==null)
                    throw new InvalidOperationException("Authoritative combat evidence is unavailable in this replay picture.");
                var shots=scene.ReplayPoses.CombatDiagnosticsAt(context.Frame).Take(16).ToImmutableArray();
                if(_published is{} published&&ReplayCombatDiagnostics.Selected==published
                    &&scene.ReplayPoses.TryCombatDiagnostic(published,out var inspected)&&!shots.Contains(inspected))
                    shots=shots.Take(15).Prepend(inspected).ToImmutableArray();
                if(RetainedCombatCurrent(context)&&_workspaceRetainedCombat is{} retained&&!shots.Contains(retained.Shot))
                    shots=shots.Take(15).Prepend(retained.Shot).ToImmutableArray();
                int selected=-1;
                for(int i=0;i<shots.Length;i++)if(_published is{} key&&key.Matches(shots[i].Fact)&&ReplayCombatDiagnostics.Selected==key)selected=i;
                var choices=shots.Select(s=>new TheatreCombatChoice($"SHOT #{s.Fact.ShotId} // P{s.Fact.ShooterSlot+1} → P{s.Fact.VictimSlot+1}",
                    ReplayCombatDiagnostics.Describe(s),s.HasFire&&s.Fact.ShooterSlot<PlayerEntity.SlotCapacity)).ToImmutableArray();
                return new(context,choices,selected,_workspaceShooterView,new CombatEvidence(shots));
            }
        }
        public void SelectCombat(TheatreCombatReview review,int index)
        {
            lock(ReplayAnnotationWitness.Gate)
            {
                var shot=RequireCombat(review,index);Retire();ReplayCombatDiagnostics.Select(shot);_published=ReplayCombatDiagnostics.Selected;
                _workspaceRetainedCombat=new(review.Context,shot,((ReplayTransport)review.Context.Transport).SeekGeneration);
                _diagnostic=ReplayCombatDiagnostics.Describe(shot);
            }
        }
        public void ShowCombat(TheatreCombatReview review,bool shooterView)
        {
            lock(ReplayAnnotationWitness.Gate)
            {
                var shot=RequireCombat(review,review.Selected);
                if(_published is not{} key||!key.Matches(shot.Fact)||ReplayCombatDiagnostics.Selected!=key)
                    throw new TheatreEditorStaleException("Select the exact authoritative shot again before changing its view.");
                if(shooterView&&(!shot.HasFire||shot.Fact.ShooterSlot>=PlayerEntity.SlotCapacity))
                    throw new InvalidOperationException("This shot has no recorded shooter fire frame.");
                uint frame=shooterView?shot.FireRecordingFrame:shot.RecordingFrame;
                if(frame>review.Context.Duration)throw new InvalidOperationException("The recorded shot frame lies beyond this replay.");
                var scene=DemoPlayback.PresentationScene!;var transport=(ReplayTransport)review.Context.Transport;
                ReplayCamera.Director=false;ReplayCamera.PlayTrack=false;ReplayCamera.LookAtSlot=-1;
                if(shooterView){SpectatorMode.Watch(shot.Fact.ShooterSlot);ReplayCamera.SetMode(ReplayCameraMode.FirstPerson);}
                else scene.UseNativeReplayFreeCamera(new(frame,shot.Fact.ImpactPoint+new Vector3(0,3,8),Quaternion.Identity,
                    MathHelper.DegreesToRadians(ReplayCamera.FieldOfView),-1,0));
                _workspaceShooterView=shooterView;Retire();transport.Seek(frame,resume:false);
                _workspaceRetainedCombat=new(review.Context,shot,transport.SeekGeneration);
                ReplayCombatDiagnostics.Select(shot);_published=ReplayCombatDiagnostics.Selected;
                _diagnostic=shooterView?"Showing recorded shooter fire frame. Refresh once seeking settles.":"Showing authority resolution frame at accepted impact. Refresh once seeking settles.";
            }
        }
        private bool RetainedCombatCurrent(TheatreEditorContext context)
        {
            var retained=_workspaceRetainedCombat;
            return retained!=null&&ReferenceEquals(retained.Owner.Owner,context.Owner)
                &&ReferenceEquals(retained.Owner.Transport,context.Transport)&&retained.Owner.SceneGeneration==context.SceneGeneration
                &&retained.Owner.LogicalPath==context.LogicalPath&&retained.Owner.SourceRevision==context.SourceRevision
                &&((ReplayTransport)context.Transport).SeekGeneration==retained.Seek;
        }
        private ReplayCombatDiagnostic RequireCombat(TheatreCombatReview review,int index)
        {
            Validate(review.Context,true);
            if(review.Evidence is not CombatEvidence evidence||(uint)index>=(uint)evidence.Shots.Length)
                throw new ArgumentException("Choose one of the reviewed authoritative shots.");
            var scene=DemoPlayback.PresentationScene;
            if(scene==null||!DemoPlayback.Owns(scene)||scene.ReplayPoses==null)
                throw new TheatreEditorStaleException("The combat presentation owner changed.");
            var shot=evidence.Shots[index];var identity=new ReplayCombatDiagnostics.Selection(shot.Fact.MatchId,shot.Fact.AuthorityEpoch,
                shot.Fact.ShooterSlot,shot.Fact.ShooterGeneration,shot.Fact.ShooterLifeId,shot.Fact.VictimSlot,
                shot.Fact.VictimGeneration,shot.Fact.VictimLifeId,shot.Fact.ShotId,shot.Fact.DamageEventId);
            if((!scene.ReplayPoses.TryCombatDiagnostic(identity,out var current)||current!=shot)
                &&!(RetainedCombatCurrent(review.Context)&&_workspaceRetainedCombat!.Shot==shot))
                throw new TheatreEditorStaleException("The retained authoritative shot changed. Refresh combat evidence.");
            return shot;
        }
    }
}
#endif
