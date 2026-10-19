#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Theatre;

public sealed partial class TheatrePlaybackPagePresenter
{
    private readonly ITheatreEditorBackend? _editorBackend;
    private TheatreEditorController? _editor;
    private RmlUiDocumentToken _editorModal,_editorConfirmation;
    private string _editorProblem="";
    private bool HandleEditorIntent(in RmlUiIntent intent)
    {
        if(!IsOpen||_editorBackend==null||!Enum.IsDefined((TheatrePlaybackAction)intent.Argument))return false;
        var action=(TheatrePlaybackAction)intent.Argument;
        bool opening=action is TheatrePlaybackAction.EditorOpen or TheatrePlaybackAction.Studio
            &&_editorModal==default&&intent.Document==_document;
        bool confirmation=_editorConfirmation!=default&&intent.Document==_editorConfirmation
            &&action is TheatrePlaybackAction.EditorConfirm or TheatrePlaybackAction.EditorCancel;
        bool editing=_editorConfirmation==default&&_editorModal!=default&&intent.Document==_editorModal
            &&intent.Argument>=(int)TheatrePlaybackAction.EditorRefresh&&intent.Argument<=(int)TheatrePlaybackAction.EditorToggleShotRays
            &&action is not(TheatrePlaybackAction.EditorConfirm or TheatrePlaybackAction.EditorCancel);
        if((!opening&&!confirmation&&!editing)||!_pages.Accept(intent))return false;
        try
        {
            _editorProblem="";
            if(opening) TryOpenEditor();
            else if(_editor!=null)switch(action)
            {
                case TheatrePlaybackAction.EditorRefresh:_editor.Refresh();break;
                case TheatrePlaybackAction.EditorTab:_editor.NextTab();break;
                case TheatrePlaybackAction.EditorPlayer:_editor.NextPlayer();break;
                case TheatrePlaybackAction.EditorEventFilter:_editor.NextFilter();break;
                case TheatrePlaybackAction.EditorPrevious:_editor.Move(-1);break;
                case TheatrePlaybackAction.EditorNext:_editor.Move(1);break;
                case TheatrePlaybackAction.EditorSeek:_editor.Seek(false);break;
                case TheatrePlaybackAction.EditorSelectRange:_editor.Seek(true);break;
                case TheatrePlaybackAction.EditorAddSelectionToReel:StageEditor(TheatreEditorEditKind.AddSelection);break;
                case TheatrePlaybackAction.EditorAddHighlightToReel:StageEditor(TheatreEditorEditKind.AddHighlight);break;
                case TheatrePlaybackAction.EditorAddTopHighlights:StageEditor(TheatreEditorEditKind.AddTopHighlights);break;
                case TheatrePlaybackAction.EditorMoveUp:StageEditor(TheatreEditorEditKind.MoveUp);break;
                case TheatrePlaybackAction.EditorMoveDown:StageEditor(TheatreEditorEditKind.MoveDown);break;
                case TheatrePlaybackAction.EditorTrim:StageEditor(TheatreEditorEditKind.Trim);break;
                case TheatrePlaybackAction.EditorCycleCamera:StageEditor(TheatreEditorEditKind.CycleCamera);break;
                case TheatrePlaybackAction.EditorRemove:StageEditor(TheatreEditorEditKind.Remove);break;
                case TheatrePlaybackAction.EditorClearReel:StageEditor(TheatreEditorEditKind.ClearReel);break;
                case TheatrePlaybackAction.EditorRemoveNamedHighlight:StageEditor(TheatreEditorEditKind.RemoveNamedHighlight);break;
                case TheatrePlaybackAction.EditorConfirm:if(_editor.Confirm()&&_pages.CloseModal())_editorConfirmation=default;break;
                case TheatrePlaybackAction.EditorCancel:BackEditor();break;
                case TheatrePlaybackAction.EditorClose:CloseEditor();break;
                case TheatrePlaybackAction.EditorPreviousShot:_editor.InspectShot(-1);break;
                case TheatrePlaybackAction.EditorShotAtPlayhead:_editor.InspectShot(0);break;
                case TheatrePlaybackAction.EditorNextShot:_editor.InspectShot(1);break;
                case TheatrePlaybackAction.EditorToggleAnalyticsHud:_editor.ToggleHud(TheatreEditorHud.Analytics);break;
                case TheatrePlaybackAction.EditorToggleNetworkHud:_editor.ToggleHud(TheatreEditorHud.Network);break;
                case TheatrePlaybackAction.EditorToggleCombatHud:_editor.ToggleHud(TheatreEditorHud.Combat);break;
                case TheatrePlaybackAction.EditorToggleShotRays:_editor.ToggleHud(TheatreEditorHud.ShotRays);break;
            }
        }
        catch(Exception error)when(error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {_editorProblem=error.Message;}
        PresentEditor();return true;
    }
    /// <summary>Open after the existing replay launch receipt owns the foreground scene and native page.</summary>
    public bool TryOpenEditor()
    {
        if(!IsOpen||_editorBackend==null)return false;
        if(WorkspaceMode){EnsureWorkspaceOwners();SelectWorkspaceTab("markers");return _editor!=null;}
        if(_editorModal!=default)return _host.IsAlive(_editorModal)&&_pages.Top==_editorModal;
        if(_pages.Top!=_document)return false;
        try
        {
            _editorProblem="";Viewport.Release();_editor?.Dispose();_editor=new(_editorBackend);
            _editorModal=_pages.OpenModal(new("replay-editor","pages/theatre/editor.rml","replay_editor_refresh"));
            PresentEditor();return true;
        }
        catch(Exception error)when(error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {_editorProblem=error.Message;_editor?.Dispose();_editor=null;return false;}
    }
    private void StageEditor(TheatreEditorEditKind kind)
    {
        if(_editor==null)return;
        _editor.SetName(_host.ReadField(_editorModal,"replay_editor_name"));
        if(_editor.Stage(kind))_editorConfirmation=_pages.OpenModal(new("replay-editor-confirm","pages/theatre/editor-confirm.rml","replay_editor_confirm_cancel"));
    }
    private void PresentEditor()
    {
        if(_editorConfirmation!=default&&!_host.IsAlive(_editorConfirmation)){_editorConfirmation=default;_editor?.Cancel();}
        if(_editorModal!=default&&!_host.IsAlive(_editorModal)){_editorModal=default;_editor?.Dispose();_editor=null;}
        if(_editorModal==default||_editor==null)return;
        var state=_editor.Snapshot();var row=state.Selected;bool available=state.Live.Current&&!state.Live.ExportRendering;
        bool writable=available&&state.Context.PublicationVerified&&state.Context.SettledPaused;
        bool marked=writable&&state.Context.MarkIn is uint start&&state.Context.MarkOut is uint end&&start<end&&end<=state.Context.Duration;
        _host.SetText(_editorModal,"replay_editor_replay",Path.GetFileName(state.Context.LogicalPath));
        _host.SetText(_editorModal,"replay_editor_tab","VIEW: "+state.Tab.ToString().ToUpperInvariant());
        _host.SetText(_editorModal,"replay_editor_player","PLAYER: "+(state.Player<0?"ALL":state.Context.Players.FirstOrDefault(p=>p.Slot==state.Player)?.Name??state.Player.ToString(CultureInfo.InvariantCulture)));
        _host.SetText(_editorModal,"replay_editor_filter","EVENTS: "+state.Filter.ToString().ToUpperInvariant());
        _host.SetText(_editorModal,"replay_editor_count",state.Entries.IsEmpty?"NO MATCHING ENTRIES":$"ENTRY {state.SelectedIndex+1} / {state.Entries.Length}");
        _host.SetText(_editorModal,"replay_editor_selected",row==null?"No entry selected.":DescribeEditorEntry(row));
        // A bounded window supplies neighbouring event/reel context without rebuilding
        // native controls or giving unreviewed rows their own mutation authority.
        int first=Math.Max(0,Math.Min(state.SelectedIndex-3,state.Entries.Length-7));
        _host.SetText(_editorModal,"replay_editor_entries",string.Join("\n",state.Entries.Skip(first).Take(7)
            .Select((entry,index)=>(first+index==state.SelectedIndex?"> ":"  ")+(first+index+1).ToString(CultureInfo.InvariantCulture)
                +". "+entry.Name+" // "+entry.Frame.ToString(CultureInfo.InvariantCulture)
                +(entry.EndFrame>entry.Frame?"–"+entry.EndFrame.ToString(CultureInfo.InvariantCulture):""))));
        _host.SetText(_editorModal,"replay_editor_review",$"REVIEWED FRAME {state.Context.Frame} // IN {state.Context.MarkIn?.ToString()??"NOT SET"} // OUT {state.Context.MarkOut?.ToString()??"NOT SET"} // {state.Context.Reel.Length} REEL SEGMENTS");
        _host.SetText(_editorModal,"replay_editor_diagnostic",state.Live.Diagnostic);
        _host.SetText(_editorModal,"replay_editor_status",state.Status);
        _host.SetText(_editorModal,"replay_editor_error",_editorProblem.Length>0?_editorProblem:state.Error.Length>0?state.Error:!state.Context.PublicationVerified?"Metadata publication is unverified. Inspect the recovery notice and refresh before editing.":!state.Live.Current?"Original replay changed. Refresh or close this editor.":state.Live.ExportRendering?"Video export owns playback. Cancel it before editing.":"");
        _host.SetText(_editorModal,"replay_editor_analytics_hud","ANALYTICS HUD: "+(state.Live.AnalyticsHud?"ON":"OFF"));
        _host.SetText(_editorModal,"replay_editor_network_hud","NETWORK HUD: "+(state.Live.NetworkHud?"ON":"OFF"));
        _host.SetText(_editorModal,"replay_editor_combat_hud","COMBAT HUD: "+(state.Live.CombatHud?"ON":"OFF"));
        _host.SetText(_editorModal,"replay_editor_shot_rays","SHOT RAYS: "+(state.Live.ShotRays?"ON":"OFF"));
        foreach(string id in new[]{"seek","select_range","add_selection","add_highlight","add_top","move_up","move_down","trim","camera","remove","clear","remove_named","previous_shot","current_shot","next_shot","analytics_hud","network_hud","combat_hud","shot_rays"})
            _host.SetBool(_editorModal,"disabled:replay_editor_"+id,!available);
        _host.SetBool(_editorModal,"disabled:replay_editor_seek",!available||row==null);
        _host.SetBool(_editorModal,"disabled:replay_editor_select_range",!available||row==null||row.Frame>=row.EndFrame);
        _host.SetBool(_editorModal,"disabled:replay_editor_previous",state.Entries.IsEmpty);
        _host.SetBool(_editorModal,"disabled:replay_editor_next",state.Entries.IsEmpty);
        _host.SetBool(_editorModal,"disabled:replay_editor_add_selection",!marked);
        _host.SetBool(_editorModal,"disabled:replay_editor_add_highlight",!writable||state.Tab!=TheatreEditorTab.AutomaticHighlights||row==null);
        _host.SetBool(_editorModal,"disabled:replay_editor_add_top",!writable||state.Context.Highlights.IsEmpty);
        _host.SetBool(_editorModal,"disabled:replay_editor_remove_named",!writable||state.Tab!=TheatreEditorTab.NamedHighlights||row==null);
        foreach(string id in new[]{"move_up","move_down","trim","camera","remove"})
            _host.SetBool(_editorModal,"disabled:replay_editor_"+id,!writable||state.Tab!=TheatreEditorTab.Reel||row==null);
        _host.SetBool(_editorModal,"disabled:replay_editor_trim",!marked||state.Tab!=TheatreEditorTab.Reel||row==null);
        _host.SetBool(_editorModal,"disabled:replay_editor_move_up",!writable||state.Tab!=TheatreEditorTab.Reel||row==null||state.SelectedIndex==0);
        _host.SetBool(_editorModal,"disabled:replay_editor_move_down",!writable||state.Tab!=TheatreEditorTab.Reel||row==null||state.SelectedIndex>=state.Entries.Length-1);
        _host.SetBool(_editorModal,"disabled:replay_editor_studio_tools",!available||_editorBackend is not ITheatreStudioToolsBackend);
        _host.SetBool(_editorModal,"disabled:replay_editor_spatial",!available||_editorBackend is not ITheatreSpatialBackend);
        _host.SetBool(_editorModal,"disabled:replay_editor_clear",!writable||state.Context.Reel.IsEmpty);
        if(_editorConfirmation==default)return;
        var pending=state.Pending;
        _host.SetText(_editorConfirmation,"replay_editor_confirm_title",pending?.Kind switch
        {
            TheatreEditorEditKind.AddSelection => "ADD THIS MARKED RANGE TO THE REEL?",
            TheatreEditorEditKind.AddHighlight => "ADD THIS HIGHLIGHT TO THE REEL?",
            TheatreEditorEditKind.AddTopHighlights => "ADD THESE TOP HIGHLIGHTS TO THE REEL?",
            TheatreEditorEditKind.MoveUp => "MOVE THIS SEGMENT EARLIER?",
            TheatreEditorEditKind.MoveDown => "MOVE THIS SEGMENT LATER?",
            TheatreEditorEditKind.Trim => "TRIM THIS SEGMENT TO THE REVIEWED MARKS?",
            TheatreEditorEditKind.CycleCamera => "CHANGE THIS SEGMENT'S CAMERA?",
            TheatreEditorEditKind.Remove => "REMOVE THIS REEL SEGMENT?",
            TheatreEditorEditKind.ClearReel => "CLEAR THESE REEL SEGMENTS?",
            TheatreEditorEditKind.RemoveNamedHighlight => "REMOVE THIS NAMED HIGHLIGHT?",
            _ => "SAVE THIS REVIEWED EDIT?"
        });
        _host.SetText(_editorConfirmation,"replay_editor_confirm_before",pending==null?"No edit is pending.":string.Join("\n\n",pending.Before.Select(DescribeEditorEntry)));
        _host.SetText(_editorConfirmation,"replay_editor_confirm_after",pending==null?"":string.Join("\n\n",pending.After.Select(DescribeEditorEntry)));
        _host.SetText(_editorConfirmation,"replay_editor_confirm_error",_editorProblem.Length>0?_editorProblem:state.Error);
        _host.SetBool(_editorConfirmation,"disabled:replay_editor_confirm_apply",!writable||pending==null);
    }
    private static string DescribeEditorEntry(TheatreEditorEntry row)=>row.Name+" // "+row.Frame+"–"+row.EndFrame
        +" // CAMERA "+new[]{"CURRENT","FIRST PERSON","CHASE","ORBIT","DIRECTOR"}[Math.Clamp(row.Camera,0,4)]+"\n"+row.Detail;
    private bool BackEditor()
    {if(WorkspaceMode&&!_workspaceClosing&&_editorConfirmation==default)return false;
        if(_editorConfirmation!=default){if(!_pages.CloseModal())return true;_editorConfirmation=default;_editor?.Cancel();PresentEditor();return true;}
        if(_editorModal==default)return false;CloseEditor();return true;
    }
    private bool CloseEditor()
    {
        if(WorkspaceMode&&!_workspaceClosing){SelectWorkspaceTab("camera");return true;}
        if(_editorConfirmation!=default){if(_host.IsAlive(_editorConfirmation)&&!_pages.CloseModal())return false;_editorConfirmation=default;}
        if(_editorModal!=default){if(_editorModal!=_document&&_host.IsAlive(_editorModal)&&!_pages.CloseModal())return false;_editorModal=default;}
        _editor?.Dispose();_editor=null;_editorProblem="";return true;
    }
}
#endif
