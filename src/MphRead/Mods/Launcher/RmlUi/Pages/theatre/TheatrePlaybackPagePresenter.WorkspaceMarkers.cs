#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Theatre;

public sealed partial class TheatrePlaybackPagePresenter
{
    private RmlUiDocumentToken _workspaceMarkerDocument;
    private string _workspaceSearchDraft = "", _workspaceTagsDraft = "";

    // The workspace router accepts and sequences these two authored controls before calling here.
    private bool HandleWorkspaceMarkerControl(string id)
    {
        if(id is not("studio_event_search_apply" or "studio_replay_tags_save"))return false;
        if(!WorkspaceMode||!IsOpen||_editor==null||_workspaceTab!="markers"||_editorConfirmation!=default
            ||_pages.Top!=_document||_host.CurrentInputDocument!=_document)return false;
        if(id=="studio_event_search_apply")
            _editor.SetEventSearch(_host.ReadField(_document,"studio_event_search"));
        else if(_editor.SetTags(_host.ReadField(_document,"studio_replay_tags")))
            _editor.SaveTags();
        PresentEditor();
        PresentWorkspaceMarkerControls(_document);
        return true;
    }

    private void PresentWorkspaceMarkerControls(RmlUiDocumentToken document)
    {
        if(!WorkspaceMode||!IsOpen||_editor==null||document!=_document||_workspaceTab!="markers"
            ||_pages.Top!=document||_host.CurrentInputDocument!=document)return;
        var state=_editor.Snapshot();
        if(_workspaceMarkerDocument!=document)
        {
            _workspaceMarkerDocument=document;
            _workspaceSearchDraft=state.EventSearch;_workspaceTagsDraft=state.Tags;
            _host.SetField(document,"studio_event_search",_workspaceSearchDraft);
            _host.SetField(document,"studio_replay_tags",_workspaceTagsDraft);
        }
        string focus=_host.FocusedElement();
        // Match the previous Studio's live type-name filter without scanning the recording or reading files.
        if(focus=="studio_event_search")
        {
            string draft=_host.ReadField(document,"studio_event_search");
            if(draft!=_workspaceSearchDraft)
            {if(_editor.SetEventSearch(draft))_workspaceSearchDraft=draft;state=_editor.Snapshot();}
        }
        else if(_workspaceSearchDraft!=state.EventSearch)
        {_workspaceSearchDraft=state.EventSearch;_host.SetField(document,"studio_event_search",_workspaceSearchDraft);}
        if(focus=="studio_replay_tags")
        {
            string draft=_host.ReadField(document,"studio_replay_tags");
            if(draft!=_workspaceTagsDraft)
            {if(_editor.SetTags(draft))_workspaceTagsDraft=draft;state=_editor.Snapshot();}
        }
        else if(_workspaceTagsDraft!=state.Tags)
        {_workspaceTagsDraft=state.Tags;_host.SetField(document,"studio_replay_tags",_workspaceTagsDraft);}
        bool free=state.Pending==null;
        _host.SetBool(document,"disabled:studio_event_search_apply",!free);
        _host.SetBool(document,"disabled:studio_replay_tags_save",!free||_editorBackend is not ITheatreEditorMetadataBackend
            ||!state.Live.Current||state.Live.ExportRendering||!state.Context.PublicationVerified||!state.Context.SettledPaused);
        _host.SetText(document,"studio_marker_metadata_status",state.Error.Length>0?state.Error
            :state.TagsDirty?"UNSAVED TAGS // Save Tags writes only this replay's tag list."
            :state.Status);
    }
}
#endif
