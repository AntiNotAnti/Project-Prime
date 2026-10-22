#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Globalization;
using System.IO;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
namespace MphRead.Mods.Launcher.RmlUi.Pages.Theatre;
public sealed partial class TheatrePlaybackPagePresenter
{
    private TheatreCameraContext? _workspaceCameraReview;
    private RmlUiDocumentToken _workspaceCameraDocument;
    private string _workspaceCameraStatus="",_workspaceCameraProblem="";
    // Call from the owning workspace's accepted replay:studio intent after checking its document.
    private bool HandleWorkspaceCameraControl(string id)
    {
        if(HandleWorkspaceCameraChoice(id))return true;
        if(id is not("studio_camera_apply" or "studio_camera_overview" or "studio_preview_hud" or "studio_preview_overlay" or "studio_preview_path"))return false;
        if(!IsOpen||_cameraBackend is not ITheatreWorkspaceCameraBackend backend||_workspaceCameraDocument==default
            ||_pages.Top!=_workspaceCameraDocument||_host.CurrentInputDocument!=_workspaceCameraDocument)return false;
        _workspaceCameraProblem="";
        try
        {
            var context=_workspaceCameraReview??throw new InvalidOperationException("Wait for a reviewed camera picture.");
            if(!_cameraBackend.IsCurrent(context))throw new TheatreCameraStaleException("The reviewed camera changed. Refresh the camera values before applying.");
            if(id=="studio_camera_overview")
            {
                if(_cameraBackend is not ITheatreWorkspaceOverviewBackend overview)throw new InvalidOperationException("Overview is unavailable for this camera owner.");
                overview.SetOverview(context);
            }
            else if(id=="studio_camera_apply")backend.ApplyDirectCamera(context,new(Read("x"),Read("y"),Read("z"),Read("yaw"),Read("pitch"),Read("roll"),Read("fov")));
            else
            {
                var preview=backend.ReadPreview(context);
                backend.SetPreview(context,id switch {
                    "studio_preview_hud"=>preview with{GameHud=!preview.GameHud},
                    "studio_preview_overlay"=>preview with{ReplayOverlay=!preview.ReplayOverlay},
                    _=>preview with{CameraPath=!preview.CameraPath}});
            }
            _workspaceCameraAwaitingPicture=context.PresentationRevision;_workspaceCameraReview=null;_workspaceCameraStatus=id=="studio_camera_apply"?"Preview camera applied. Add or update a camera key to save this pose.":"Preview display updated.";
        }
        catch(Exception error)when(error is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
        {_workspaceCameraStatus=error.Message;}
        return true;
        float Read(string field)
        {
            if(!float.TryParse(_host.ReadField(_workspaceCameraDocument,"studio_camera_"+field),NumberStyles.Float,CultureInfo.InvariantCulture,out float value)||!float.IsFinite(value))
                throw new ArgumentException("Enter a finite number for camera "+field+".");
            return value;
        }
    }
    private void PresentWorkspaceCameraControls(RmlUiDocumentToken document)
    {
        if(!IsOpen||document==default||!_host.IsAlive(document)||_cameraBackend is not ITheatreWorkspaceCameraBackend backend)return;
        if(_workspaceCameraDocument!=document){_workspaceCameraReview=null;_workspaceCameraAwaitingPicture=null;_workspaceCameraDocument=document;}
        bool editing=_host.FocusedElement().StartsWith("studio_camera_",StringComparison.Ordinal)&&_host.FocusedElement()!="studio_camera_apply";
        try
        {
            // Never overwrite a numeric draft or silently rebind it to another playhead/owner.
            if(_workspaceCameraReview==null||!editing&&_host.FocusedElement()!="studio_camera_apply")
            {
                var context=_cameraBackend.Capture();
                if(_workspaceCameraAwaitingPicture is long previous&&context.PresentationRevision<=previous)throw new InvalidOperationException("Wait for a newer successful camera picture before editing again.");
                _workspaceCameraAwaitingPicture=null;var v=backend.ReadDirectCamera(context);_workspaceCameraReview=context;
                Set("x",v.X);Set("y",v.Y);Set("z",v.Z);Set("yaw",v.Yaw);Set("pitch",v.Pitch);Set("roll",v.Roll);Set("fov",v.Fov);
            }
            var review=_workspaceCameraReview;bool current=review!=null&&_cameraBackend.IsCurrent(review);
            foreach(string id in new[]{"studio_camera_apply","studio_camera_overview","studio_preview_hud","studio_preview_overlay","studio_preview_path"})_host.SetBool(document,"disabled:"+id,!current);
            if(current&&review!=null)
            {
                var preview=backend.ReadPreview(review);
                _host.SetText(document,"studio_preview_hud","GAME HUD: "+(preview.GameHud?"ON":"OFF"));
                _host.SetText(document,"studio_preview_overlay","REPLAY OVERLAY: "+(preview.ReplayOverlay?"ON":"OFF"));
                _host.SetText(document,"studio_preview_path","CAMERA PATH: "+(preview.CameraPath?"ON":"OFF"));
                _workspaceCameraProblem="";
            }
        }
        catch(Exception error)when(error is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
        {_workspaceCameraProblem=error.Message;_host.SetBool(document,"disabled:studio_camera_apply",true);}
        PresentWorkspaceCameraChoices();
        _host.SetText(document,"studio_camera_status",_workspaceCameraProblem.Length>0?_workspaceCameraProblem:_workspaceCameraStatus);
        void Set(string id,float value)=>_host.SetField(document,"studio_camera_"+id,value.ToString("0.###",CultureInfo.InvariantCulture));
    }
}
#endif
