#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Globalization;
using System.IO;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
namespace MphRead.Mods.Launcher.RmlUi.Pages.Theatre;
public sealed partial class TheatrePlaybackPagePresenter
{
    private TheatreCombatReview? _workspaceCombatReview;
    private RmlUiDocumentToken _workspaceCombatDocument;
    private string _workspaceCombatStatus="";
    // The workspace dispatcher has already accepted the current-document replay:studio intent.
    private bool HandleWorkspaceCombatControl(string id)
    {
        bool choice=id.StartsWith("studio_combat_choice_",StringComparison.Ordinal);
        if(!choice&&id is not("studio_combat_shooter_view" or "studio_combat_shooter_pov" or "studio_combat_authority"))return false;
        if(!IsOpen||_editorBackend is not ITheatreWorkspaceCombatBackend backend||_workspaceCombatDocument==default
            ||_pages.Top!=_workspaceCombatDocument||_host.CurrentInputDocument!=_workspaceCombatDocument)return false;
        try
        {
            var review=_workspaceCombatReview??throw new InvalidOperationException("Pause and refresh to review authoritative shot evidence.");
            if(choice)
            {
                if(!int.TryParse(id[21..],NumberStyles.None,CultureInfo.InvariantCulture,out int index)||index is <0 or >=16)
                    throw new ArgumentException("Choose a displayed authoritative shot.");
                backend.SelectCombat(review,index);_workspaceCombatStatus="Authoritative shot selected. Choose shooter POV or authority resolution.";
            }
            else
            {
                bool shooter=id=="studio_combat_shooter_pov"||id=="studio_combat_shooter_view"&&!review.ShooterView;
                backend.ShowCombat(review,shooter);_workspaceCombatStatus="Presentation seek requested. Refresh after it settles to inspect more shots.";
            }
            _workspaceCombatReview=null;
        }
        catch(Exception error)when(error is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
        {_workspaceCombatStatus=error.Message;_workspaceCombatReview=null;}
        return true;
    }
    private void PresentWorkspaceCombatControls(RmlUiDocumentToken document)
    {
        if(!IsOpen||document==default||!_host.IsAlive(document)||_editorBackend is not ITheatreWorkspaceCombatBackend backend||_editor==null)return;
        _workspaceCombatDocument=document;
        try
        {
            var review=backend.ReadCombat(_editor.Snapshot().Context);_workspaceCombatReview=review;
            bool selected=review.Selected>=0&&review.Selected<review.Shots.Length;
            for(int i=0;i<16;i++)
            {
                string id="studio_combat_choice_"+i;bool visible=i<review.Shots.Length;
                _host.SetBool(document,"visible:"+id,visible);_host.SetBool(document,"disabled:"+id,!visible);
                if(visible)_host.SetText(document,id,review.Shots[i].Label);
                _host.SetBool(document,"class:"+id+":selected",selected&&i==review.Selected);
            }
            _host.SetText(document,"studio_combat_shooter_view","WHAT SHOOTER SAW: "+(review.ShooterView?"ON":"OFF"));
            _host.SetBool(document,"disabled:studio_combat_shooter_pov",!selected||!review.Shots[review.Selected].HasFire);
            _host.SetBool(document,"disabled:studio_combat_authority",!selected);
            _host.SetBool(document,"disabled:studio_combat_shooter_view",!selected||!review.ShooterView&&!review.Shots[review.Selected].HasFire);
            _host.SetText(document,"studio_combat_evidence",selected?review.Shots[review.Selected].Detail:
                review.Shots.IsEmpty?"No authoritative resolved shot facts at this frame. Inspect a recorded shot, settle the seek, then refresh.":"Choose an authoritative shot resolved at this reviewed frame (up to 16 shown).");
        }
        catch(Exception error)when(error is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            _workspaceCombatReview=null;_workspaceCombatStatus=error.Message;
            for(int i=0;i<16;i++)_host.SetBool(document,"visible:studio_combat_choice_"+i,false);
            foreach(string id in new[]{"studio_combat_shooter_view","studio_combat_shooter_pov","studio_combat_authority"})_host.SetBool(document,"disabled:"+id,true);
            _host.SetText(document,"studio_combat_evidence","Pause, settle the replay seek, and refresh authoritative shot evidence.");
        }
        _host.SetText(document,"studio_combat_status",_workspaceCombatStatus);
    }
}
#endif
