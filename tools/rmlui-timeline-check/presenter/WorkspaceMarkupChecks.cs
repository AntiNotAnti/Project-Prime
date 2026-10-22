using System.Xml.Linq;
internal static class WorkspaceMarkupChecks
{
    internal static void Run(string directory)
    {
        int checks=0;void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;}
        var document=XDocument.Load(Path.Combine(directory,"studio-workspace.rml"));var ids=document.Descendants().Attributes("id").Select(a=>a.Value).ToArray();
        Check(ids.Length==ids.Distinct().Count(),"persistent workspace has no duplicate control identities");
        XElement Element(string id)=>document.Descendants().Single(e=>e.Attribute("id")?.Value==id);
        bool Under(string child,string parent)=>Element(child).Ancestors().Contains(Element(parent));
        foreach(string id in new[]{"replay_pause","replay_mark_in","replay_mark_out","studio_settings","replay_back","studio_open","studio_save_as"})
            Check(Under(id,"studio_transport"),"persistent transport exposes "+id);
        // Every component is embedded intact: future presenter fields and
        // dynamic control families must not disappear during workspace composition.
        foreach(string component in new[]{"camera.rml","camera-tools.rml","bookmarks.rml","ranges.rml","export.rml","editor.rml","spatial.rml","studio-tools.rml","timeline.rml"})
        {
            var componentIds=XDocument.Load(Path.Combine(directory,component)).Descendants().Attributes("id").Select(attribute=>attribute.Value);
            foreach(string id in componentIds)Check(ids.Contains(id),"embedded native component binding survives composition: "+component+" // "+id);
        }
        foreach(string id in new[]{"replay_state","replay_time","replay_camera","replay_rate","replay_range_summary","replay_error","replay_inactive","replay_position","replay_previous_player","replay_next_player","replay_fullscreen"})
            Check(ids.Contains(id),"shared playback presentation binding exists in workspace: "+id);
        Check(!Under("replay_viewport","studio_inspector_scroll")&&!Under("replay_timeline_panel","studio_inspector_scroll"),"viewport and timeline remain outside scrolling inspector");
        foreach(string tab in new[]{"camera","markers","combat","analysis","audio","export"})
            Check(Under("studio_tab_"+tab,"studio_tabs")&&Under("studio_inspector_"+tab,"studio_inspector_scroll"),"persistent named inspector tab "+tab);
        foreach(string lane in new[]{"camera","events","bookmarks","ranges","reel","kills","damage","shots","spawns","objectives","audio","cuts","exportrange"})
            Check(Under("replay_timeline_"+lane,"studio_timeline")&&Under("studio_track_"+lane,"studio_inspector_markers"),"individually controlled persistent timeline track "+lane);
        foreach(string lane in new[]{"events","bookmarks","ranges","reel","kills","damage","shots","spawns","objectives","audio","cuts","exportrange"})
            Check(Under("replay_timeline_"+lane+"_count","replay_timeline_"+lane),"live timeline count is the visible compact track label: "+lane);
        Check(Under("replay_camera_curve_speed_bounds","studio_inspector_camera"),"readonly speed graph has a native caption binding");
        foreach(string field in new[]{"x","y","z","yaw","pitch","roll","fov"})Check(Under("studio_camera_"+field,"studio_inspector_camera"),"direct numeric camera field "+field);
        Check(Under("studio_combat_shooter_pov","studio_inspector_combat")&&Under("studio_combat_authority","studio_inspector_combat"),"explicit authoritative combat POV controls");
        foreach(string action in document.Descendants().Attributes("data-action").Select(a=>a.Value).Distinct())
            Check(MphRead.Mods.Launcher.RmlUi.Host.RmlUiIntentRegistry.TryParseLegacy(action,new MphRead.Mods.Launcher.RmlUi.Host.RmlUiDocumentToken(1,1),1,out _),"finite workspace action registered: "+action);
        Console.WriteLine($"PASS {checks} persistent workspace authored structure/control checks; no native geometry or interaction claim");
    }
}
