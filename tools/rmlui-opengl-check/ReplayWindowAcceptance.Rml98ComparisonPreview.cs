using System;
using MphRead;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;

internal sealed partial class ReplayWindowAcceptance
{
    // Wire after the existing successful same-map world comparison, while the
    // isolated existing clone _replay and original paused owner remain alive.
    private void PlanRml98ComparisonPreviewQualification()
    {
        Scene? compared=null;int subject=-1;
        object? previewOwner=null;string recordingHash="";uint playedFrame=0;
        Do("RML98 open persistent comparison pictures",()=>{Activate("studio_tab_analysis");Activate("studio_refresh");});
        Wait("RML98 exact paused comparison context is presented",()=>Read<object>(Page,"_workspaceComparisonReview")!=null);
        Do("RML98 start private B through the actual native action",()=>
        {subject=DemoPlayback.PresentationScene!.Players.MainPlayerIndex;Host!.SetField(Page!.Document,"studio_comparison_path",_replay);Activate("studio_comparison_preview");});
        Wait("RML98 private B reaches the exact reviewed paused frame",()=>
        {compared=Page?.CurrentComparisonPreviewScene() as Scene;return compared!=null;},45000);
        WaitFrames(4,"RML98 two real pictures retain foreground canonical clock and subjects",()=>
        {
            Rml97SameWorld();var foreground=DemoPlayback.PresentationScene!;
            Require(compared!=null&&!ReferenceEquals(compared,foreground)&&!DemoPlayback.Owns(compared),"B is a private unpublished replay world");
            Require(foreground.Players.MainPlayerIndex==subject,"A selected player restored after every comparison draw");
            Require(ReplayStateHash.Compute(compared!,ReplayController.CurrentFrame)==ReplayStateHash.Compute(foreground,ReplayController.CurrentFrame),"same recording actual B canonical world matches A");
            Capture("rml98-private-comparison-preview");RequireStudioCompositeVariation("paused-private-B");
        });
        Do("RML98 play actual A while retaining the same private B owner",()=>
        {
            previewOwner=Read<object>(Read<object>(Page,"_editorBackend"),"_comparisonPreview");recordingHash=Hash(_rml97Path);
            Require(previewOwner!=null&&_rml97Duration>=60,"continuous comparison fixture has a private owner and sufficient recording frames");
            Activate("replay_pause");
        });
        Wait("RML98 actual A progresses at least thirty frames with B following",()=>
        {
            Require(ReferenceEquals(DemoPlayback.PlaybackOwner,_rml97Owner),"continuous B never replaces the foreground player");
            return StudioWorkspaceForeground()&&!ReplayController.IsSeeking&&ReplayController.CurrentFrame>=30;
        },15000);
        Do("RML98 pause actual moving A through native transport",()=>
        {Require(!ReplayController.IsPaused,"actual A was playing before native pause");Activate("replay_pause");});
        Wait("RML98 private B settles at the actual played A frame",()=>SameCurrentComparison("after-play"),45000);
        Do("RML98 record bounded actual continuous comparison picture",()=>
        {playedFrame=ReplayController.CurrentFrame;Capture("rml98-following-B-after-play");RequireStudioCompositeVariation("following-B-after-play");});
        SeekFollowing(0,"backward");
        SeekFollowing(60,"forward");
        SeekFollowing(0,"restore-original");
        Do("RML98 explicitly refresh reviews after restoring original A frame",()=>Activate("studio_refresh"));
        Wait("RML98 restored original frame has explicitly current camera tools and editor reviews",()=>
            StudioWorkspaceForeground()&&!ReplayController.IsSeeking&&ReplayController.IsPaused&&ReplayController.CurrentFrame==0
            &&Camera?.Snapshot().Current==true&&TryTools()?.Snapshot().Current==true&&Editor?.Snapshot().Live.Current==true,12000);
        Do("RML98 retain adopted seek transport only after exact original-world restoration",()=>
        {
            Require(ReferenceEquals(DemoPlayback.PlaybackOwner,_rml97Owner)&&Hash(_rml97Path)==recordingHash&&Rml97WorldHash()==_rml97Baseline,
                "continuous playback and seeks restore the original canonical frame without changing recording/player");
            _rml97Transport=DemoPlayback.Session.Transport;Rml97SameWorld();
            _rml97StudioChecks.Add(new{Feature="actual-following-private-B",PlayedFrame=playedFrame,BackwardFrame=0,ForwardFrame=60,
                RestoredFrame=0,SamePreviewOwner=true,SameRecording=true,CanonicalEquality=true});
        });
        Do("RML98 stop preview with the actual native action",()=>Activate("studio_comparison_stop"));
        Wait("RML98 private B is disposed through its real owner",()=>Page?.CurrentComparisonPreviewScene()==null
            &&Read<object>(Read<object>(Page,"_editorBackend"),"_comparisonPreview")==null);
        Do("RML98 comparison teardown retains foreground source and canonical world",()=>
        {Rml97SameWorld();_rml97StudioChecks.Add(new{Feature="paused-private-comparison-preview",Swaps=4,Owner=Id(_rml97Owner)});});
        void SeekFollowing(uint frame,string stage)
        {
            Do("RML98 native Go seeks A with existing B: "+stage,()=>
            {Host!.SetField(Page!.Document,"studio_frame",frame.ToString(System.Globalization.CultureInfo.InvariantCulture));Activate("studio_seek");});
            Wait("RML98 existing B follows exact native seek: "+stage,()=>
                ReplayController.CurrentFrame==frame&&SameCurrentComparison(stage),45000);
        }
        bool SameCurrentComparison(string stage)
        {
            if(!StudioWorkspaceForeground()||ReplayController.IsSeeking||!ReplayController.IsPaused)return false;
            var currentPreview=Read<object>(Read<object>(Page,"_editorBackend"),"_comparisonPreview");
            Require(ReferenceEquals(currentPreview,previewOwner),"Play and seek retain the original private B preview owner: "+stage);
            Require(ReferenceEquals(DemoPlayback.PlaybackOwner,_rml97Owner)&&DemoPlayback.LogicalPath==_rml97Path&&Hash(_rml97Path)==recordingHash,
                "following B preserves exact original recording and foreground player: "+stage);
            compared=Page?.CurrentComparisonPreviewScene() as Scene;if(compared==null)return false;
            var player=Read<PassiveReplayPlayer>(currentPreview,"Player");
            Require(player!=null&&player.Ready&&player.Transport.CurrentFrame==ReplayController.CurrentFrame,
                "published following B is ready at exact current A frame: "+stage);
            var foreground=DemoPlayback.PresentationScene!;
            Require(!ReferenceEquals(compared,foreground)&&!DemoPlayback.Owns(compared)&&foreground.Players.MainPlayerIndex==subject,
                "following B stays private and restores selected A subject: "+stage);
            Require(ReplayStateHash.Compute(compared,ReplayController.CurrentFrame)==ReplayStateHash.Compute(foreground,ReplayController.CurrentFrame),
                "same-source moving/seeked B canonical world equals current A: "+stage);
            return true;
        }
    }
}
