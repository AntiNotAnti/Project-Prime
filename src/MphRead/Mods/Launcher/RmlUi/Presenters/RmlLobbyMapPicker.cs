#if MPHREAD_RMLUI_POC
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Theatre;
using MphRead.Mods.Network;
using MphRead.Mods.MapGen;
using MphRead.Mods.Multiplayer;

namespace MphRead.Mods.Launcher.RmlUi.Presenters;

internal sealed class RmlLobbyMapPicker : IDisposable
{
    private readonly RmlUiHost _host;
    private readonly LobbySessionController _lobby;
    private readonly Guid _lifetime;
    private readonly string[] _rooms;
    private readonly TheatreImageCache _images;
    private readonly Dictionary<string, (MatchDefinition Match, int Players, bool Valid)> _compatibility = new(StringComparer.OrdinalIgnoreCase);
    private RmlUiDocumentToken _document;
    private int _page;
    private string _status = "Select a map to update the lobby.";
    public bool Active => _document != default && _host.IsAlive(_document);
    public RmlLobbyMapPicker(RmlUiHost host, LobbySessionController lobby, IReadOnlyList<string> rooms, TheatreImageCache? images = null)
    {
        _images=images ?? new(); _host=host; _lobby=lobby; _lifetime=lobby.Snapshot().Lifetime;
        _rooms=rooms.Concat(CustomRooms.Definitions.Select(d=>d.Name)).Where(r=>!string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(r=>r).ToArray();
    }
    public void Open() { _document=_host.OpenDocument("pages/lobby/maps.rml",RmlUiDocumentLayer.Modal); Update(); _host.FocusDocument(_document,"map_close"); }
    private bool Compatible(string room, LobbySnapshot state, out MatchDefinition match, bool refresh = false)
    {
        match=default;
        if(state.Match is not {} current)return false;
        try
        {
            match=current with {RoomKey=room,MapIdentity=NetworkMapIdentity.ForRoom(room)};
            int players=LobbyRules.ExactTeams(match)?LobbyRules.ResolveTeamLayout(match).TotalPlayers:state.MaxPlayers;
            if (!refresh && _compatibility.TryGetValue(room, out var cached)
                && cached.Match == match && cached.Players == state.MaxPlayers) return cached.Valid;
            bool valid = LobbyRules.ValidateDefinition(match,out _)==LobbyResultCode.Ok
                && MapModeCapabilities.Supports(room,match.Mode,LobbyRules.ResolveWorldProfile(match,state.MaxPlayers),out _,players);
            _compatibility[room] = (match, state.MaxPlayers, valid);
            return valid;
        }
        catch(Exception){return false;}
    }
    public void Update()
    {
        if(!Active)return;
        var state=_lobby.Snapshot();
        if(state.Lifetime!=_lifetime||state.Closed||!state.Active||state.Phase!=SessionPhase.Lobby){Dispose();return;}
        _page=Math.Clamp(_page,0,Math.Max(0,(_rooms.Length-1)/6));
        _host.SetText(_document,"map_status",state.CommandError.Length>0?state.CommandError:!state.CanEdit?"Only the host can change the map.":_status);
        _host.SetText(_document,"map_page",$"{_page+1} / {Math.Max(1,(_rooms.Length+5)/6)}");
        _host.SetBool(_document,"disabled:map_previous",_page==0);
        _host.SetBool(_document,"disabled:map_next",(_page+1)*6>=_rooms.Length);
        for(int i=0;i<6;i++)
        {
            int index=_page*6+i;bool shown=index<_rooms.Length;
            _host.SetBool(_document,"visible:map_choice"+i,shown);if(!shown)continue;
            string room=_rooms[index]; bool valid=Compatible(room,state,out _);
            _host.SetText(_document,"map_name"+i,room);
            _host.SetText(_document,"map_hint"+i,valid?state.Match?.RoomKey==room?"CURRENT MAP":"SELECT MAP":"Unavailable for these rules");
            _host.SetBool(_document,"disabled:map_choice"+i,!valid||!state.CanEdit||state.CommandPending||state.RulesPending);
            string path=ThumbnailGenerator.PathFor(room),image="";
            if(File.Exists(path))try{image=_images.Load(path,default);}catch(Exception){}
            _host.SetText(_document,"image:map_image"+i,image);_host.SetBool(_document,"visible:map_image"+i,image.Length>0);
        }
    }
    public bool Handle(RmlUiIntent intent)
    {
        if(!Active||intent.Document!=_document)return false;
        if(intent.Kind==RmlUiIntentKind.LobbyMapClose){Dispose();return true;}
        if(intent.Kind==RmlUiIntentKind.LobbyMapPrevious)_page--;
        else if(intent.Kind==RmlUiIntentKind.LobbyMapNext)_page++;
        else if(intent.Kind==RmlUiIntentKind.LobbyMapSelect)
        {
            int index=_page*6+intent.Argument;var state=_lobby.Snapshot();
            if((uint)index<(uint)_rooms.Length && state.CanEdit && Compatible(_rooms[index],state,out var match, refresh:true))
            {
                var result=_lobby.Dispatch(_lobby.Intent(LobbyIntentKind.UpdateRules) with {Match=match,RuleFlags=state.RuleFlags, DraftVersion=1});
                _status=result.Message;if(result.Accepted){Dispose();return true;}
            }
        }
        else return false;
        Update();return true;
    }
    public void Dispose(){if(Active)_host.CloseDocument(_document);_document=default;}
}
#endif
