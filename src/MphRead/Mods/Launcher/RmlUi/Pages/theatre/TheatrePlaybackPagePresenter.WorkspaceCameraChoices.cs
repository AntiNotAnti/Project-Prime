#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using MphRead.Mods.Launcher.Core;
namespace MphRead.Mods.Launcher.RmlUi.Pages.Theatre;
public sealed partial class TheatrePlaybackPagePresenter
{
    private long? _workspaceCameraAwaitingPicture;
    private bool HandleWorkspaceCameraChoice(string id)
    {
        const string prefix="studio_camera_";
        if(!id.StartsWith(prefix,StringComparison.Ordinal))return false;
        string command=id[prefix.Length..];bool saved=command.StartsWith("key_",StringComparison.Ordinal);if(saved)command=command[4..];
        string kind=command.StartsWith("interpolation_",StringComparison.Ordinal)?"interpolation":command.StartsWith("ease_",StringComparison.Ordinal)?"ease":command.StartsWith("lookat_",StringComparison.Ordinal)?"lookat":command.StartsWith("slot_",StringComparison.Ordinal)?"slot":"";
        if(kind.Length==0)return false;
        if(!IsOpen||_workspaceCameraDocument==default||_pages.Top!=_workspaceCameraDocument||_host.CurrentInputDocument!=_workspaceCameraDocument)return false;
        _workspaceCameraProblem="";
        try
        {
            string text=command[(kind.Length+1)..];int value=text=="none"?-1:int.Parse(text,NumberStyles.None,CultureInfo.InvariantCulture);
            if(saved)
            {
                if(_cameraTools==null||kind=="slot")throw new InvalidOperationException("Select reviewed saved camera keys first.");
                var change=kind=="interpolation"?TheatreCameraToolsChange.Interpolation:kind=="ease"?TheatreCameraToolsChange.Ease:TheatreCameraToolsChange.LookAt;
                if(_cameraTools.StageChoice(change,value))_cameraToolsConfirmation=_pages.OpenModal(new("replay-camera-tools-confirm","pages/theatre/camera-tools-confirm.rml","replay_camera_tools_confirm_cancel"));
            }
            else
            {
                var review=_workspaceCameraReview??throw new InvalidOperationException("Wait for a reviewed camera picture.");
                if(_cameraBackend is not ITheatreWorkspaceCameraChoicesBackend backend||!_cameraBackend.IsCurrent(review))throw new TheatreCameraStaleException("Refresh the reviewed camera picture before choosing camera settings.");
                var current=backend.ReadChoices(review);
                backend.ApplyChoices(review,kind=="interpolation"?value:current.Interpolation,kind=="ease"?value:current.Ease,kind=="lookat"?value:current.LookAt,kind=="slot"?value:current.PlayerSlot);
                _workspaceCameraAwaitingPicture=review.PresentationRevision;_workspaceCameraReview=null;
                _workspaceCameraStatus="Presentation camera choice applied. Wait for the new picture; add or update a key to save the pose.";
            }
        }
        catch(Exception error)when(error is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or FormatException or OverflowException)
        {_workspaceCameraStatus=error.Message;}
        return true;
    }
    private void PresentWorkspaceCameraChoices()
    {
        if(_workspaceCameraDocument==default||_cameraBackend is not ITheatreWorkspaceCameraChoicesBackend backend)return;
        try
        {
            var review=_workspaceCameraReview;bool current=review!=null&&_workspaceCameraAwaitingPicture==null&&_cameraBackend.IsCurrent(review);
            var choices=current?backend.ReadChoices(review!):null;
            void Button(string id,bool available,bool selected)
            {_host.SetBool(_workspaceCameraDocument,"disabled:"+id,!current||!available);_host.SetBool(_workspaceCameraDocument,"class:"+id+":selected",selected);}
            for(int i=0;i<5;i++)Button("studio_camera_interpolation_"+i,true,choices?.Interpolation==i);
            for(int i=0;i<4;i++)Button("studio_camera_ease_"+i,true,choices?.Ease==i);
            Button("studio_camera_lookat_none",true,choices?.LookAt==-1);
            for(int i=0;i<8;i++)
            {bool available=choices?.PlayerSlots.Contains(i)==true;Button("studio_camera_lookat_"+i,available,choices?.LookAt==i);Button("studio_camera_slot_"+i,available,choices?.PlayerSlot==i);}
            var tools=_cameraTools?.Snapshot();bool editable=tools?.Current==true&&tools.Selection.Length>0&&tools.Pending==null;
            void Key(string id,bool available){_host.SetBool(_workspaceCameraDocument,"disabled:"+id,!editable||!available);}
            for(int i=0;i<5;i++)Key("studio_camera_key_interpolation_"+i,true);
            for(int i=0;i<4;i++)Key("studio_camera_key_ease_"+i,true);
            Key("studio_camera_key_lookat_none",true);
            for(int i=0;i<8;i++)Key("studio_camera_key_lookat_"+i,tools?.Context.LookAtSlots.Contains(i)==true);
        }
        catch(Exception error)when(error is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {_workspaceCameraProblem=error.Message;}
    }
}
#endif
