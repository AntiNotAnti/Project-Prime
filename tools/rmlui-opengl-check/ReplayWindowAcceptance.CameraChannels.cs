using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Pages.Theatre;
using MphRead.Mods.Replay;

internal sealed partial class ReplayWindowAcceptance
{
    private void PlanReplayIndependentCameraChannels()
    {
        string cameraHash="",archiveHash="",revision="";
        byte[] authored=Array.Empty<byte>();
        ReplayCameraTrackIdentity identity=default;
        int transactions=0,initialUndo=0;
        Do("independent curves retain exact saved baseline and frozen world",()=>
        {
            Rml97SameWorld();cameraHash=Hash(_rml97Path+".camera");archiveHash=Hash(_rml97Path);
            revision=Tools.Snapshot().Context.Camera.CameraRevision;
            identity=ReplayCameraTrackIdentity.Create(_rml97Path);
            Require(Tools.MemoryEditing&&!Tools.Snapshot().Dirty,"native channel fixture starts with a saved memory document");
            initialUndo=Tools.Snapshot().UndoCount;
            Require(initialUndo<=80,"native channel fixture has room for its bounded transaction history");
            Activate("replay_camera_tools_all");
        });
        foreach(var (id,count) in new[]{("orientation_curve",2),("orientation_ease",4),("fov_curve",3),
            ("fov_ease",3),("roll_curve",2),("roll_ease",2)})
        {
            for(int i=0;i<count;i++)
            {
                int expected=++transactions;
                Do("actual independent camera control "+id+" cycle "+i,()=>Activate("replay_camera_tools_"+id));
                Wait("independent camera transaction "+expected+" commits in memory",()=>
                {
                    var state=Tools.Snapshot();if(state.UndoCount<initialUndo+expected)return false;
                    Rml97SameWorld();
                    Require(state.Dirty&&state.Pending==null&&Host!.CurrentInputDocument==Page!.Document
                        &&Hash(_rml97Path+".camera")==cameraHash&&Hash(_rml97Path)==archiveHash,
                        "native independent curve control retains source bytes and uses no confirmation modal");
                    return true;
                });
            }
        }
        var curves=new TheatreCameraChannelCurves(2,4,3,3,2,2);
        string layoutDiagnostic="";int layoutSamples=0,layoutScrolls=0;long nextLayoutScroll=0;
        Do("reveal all authored independent camera buttons for visual qualification",()=>Focus("replay_camera_tools_orientation_curve"));
        Wait("six native independent camera buttons have visible separated rows",()=>
        {
            TimelineBox Bounds(string id)
            {
                Require(Host!.TryGetElementBounds(Page!.Document,id,out float x,out float y,out float width,out float height),"native camera control has actual bounds: "+id);
                return new(x,y,width,height);
            }
            var inspector=Bounds("studio_inspector_scroll");
            var buttons=new[]{"orientation_curve","orientation_ease","fov_curve","fov_ease","roll_curve","roll_ease"}
                .Select(id=>new{Id=id,Box=Bounds("replay_camera_tools_"+id)}).ToArray();
            string diagnostic=System.Text.Json.JsonSerializer.Serialize(new{Inspector=inspector,Buttons=buttons,Focus=Host!.FocusedElement()});
            if(diagnostic!=layoutDiagnostic&&layoutSamples<12)
            {
                layoutDiagnostic=diagnostic;layoutSamples++;
                File.AppendAllText(Path.Combine(_output,"camera-channel-layout.jsonl"),diagnostic+"\n");
            }
            if(buttons.Any(button=>!button.Box.Inside(inspector)))
            {
                // Nearest focus reveal need only expose the focused control.
                // Scroll the real inspector a bounded amount to show the group.
                bool bottom=buttons[^1].Box.Y+buttons[^1].Box.Height>inspector.Y+inspector.Height;
                bool top=buttons[0].Box.Y<inspector.Y;
                bool room=bottom?buttons[0].Box.Y>inspector.Y+48:
                    top&&buttons[^1].Box.Y+buttons[^1].Box.Height<inspector.Y+inspector.Height-48;
                if(room&&layoutScrolls<4&&_elapsed.ElapsedMilliseconds>=nextLayoutScroll)
                {
                    Host.Input.PointerMoved(inspector.X+inspector.Width/2,inspector.Y+inspector.Height/2);
                    Host.Input.PointerWheel(bottom?-1:1);layoutScrolls++;nextLayoutScroll=_elapsed.ElapsedMilliseconds+300;
                }
                return false;
            }
            for(int index=0;index<buttons.Length;index++)
            {
                Require(buttons[index].Box.Height>=28&&buttons[index].Box.Width>=inspector.Width*.75,
                    "independent channel has a visible full-row button target");
                if(index>0)Require(buttons[index].Box.Y>=buttons[index-1].Box.Y+buttons[index-1].Box.Height+2,
                    "independent channel buttons have separated non-overlapping rows");
            }
            _rml97StudioChecks.Add(new{Feature="independent-camera-controls-visible-layout",Inspector=inspector,Buttons=buttons});
            return true;
        });
        Do("native six-channel controls author the exact curve document",()=>
        {
            Require(Tools.Snapshot().Context.Camera.Keys.All(k=>k.Pose.Channels==curves),"six native controls reach exact selected-key curve values");
            authored=ReplayCamera.Track.ExportState();Capture("independent-camera-channels");
            Activate("replay_camera_tools_undo");
        });
        Wait("native independent curve undo restores preceding transaction",()=>
        {
            var state=Tools.Snapshot();if(state.UndoCount!=initialUndo+transactions-1)return false;
            Require(state.Context.Camera.Keys.All(k=>k.Pose.Channels==curves with{RollEase=1}),"native undo preserves all other independent channels");
            Rml97SameWorld();return true;
        });
        Do("redo native independent curve transaction",()=>Activate("replay_camera_tools_redo"));
        Wait("native redo restores exact authored portable state",()=>
            Tools.Snapshot().UndoCount==initialUndo+transactions&&ReplayCamera.Track.ExportState().SequenceEqual(authored));
        Wait("latest recovery draft preserves independent channels",()=>
        {
            if(!ReplayCameraDraftStore.PendingWrites(_rml97Path).IsCompleted)return false;
            Require(ReplayCameraDraftStore.TryRecover(_rml97Path,identity,revision,out var recovered)
                &&recovered!.SequenceEqual(authored),"source-bound recovery stores the same independent curve document");
            return true;
        });
        Do("save native independent camera document",()=>Activate("replay_camera_tools_save"));
        Wait("independent channel save reloads exact native keys",()=>
        {
            if(Tools.Snapshot().Dirty)return false;
            var reloaded=new ReplayCameraTrack();
            Require(reloaded.Load(_rml97Path)&&reloaded.Keys.SequenceEqual(ReplayCamera.Track.Keys)
                &&Tools.Snapshot().Context.Camera.Keys.All(k=>k.Pose.Channels==curves),
                "native saved v7 camera reload retains all six independent fields");
            var export=ReplayExportPresentation.Capture();
            Require(export.Keys.SequenceEqual(reloaded.Keys)&&Hash(_rml97Path)==archiveHash,
                "preview and accepted export capture the same curve metadata without changing the replay archive");
            Rml97SameWorld();
            _rml97StudioChecks.Add(new{Feature="independent-camera-channels-native-memory-undo-redo-save-recovery",
                Transactions=transactions,Curves=curves,CameraSha256=Hash(_rml97Path+".camera"),
                StateSha256=Convert.ToHexString(SHA256.HashData(authored)),ArchiveSha256=archiveHash});
            return true;
        });
    }
}
