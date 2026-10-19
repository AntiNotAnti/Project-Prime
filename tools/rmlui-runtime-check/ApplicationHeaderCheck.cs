using System.Xml.Linq;
using MphRead.Mods.Launcher.RmlUi.Host;

internal static class ApplicationHeaderCheck
{
    internal static void Run(Action<bool,string> require)
    {
        var game=RmlUiApplicationHeader.Bindings(false);var studio=RmlUiApplicationHeader.Bindings(true);
        require(ReferenceEquals(game,RmlUiApplicationHeader.Bindings(false))&&ReferenceEquals(studio,RmlUiApplicationHeader.Bindings(true)),"Header role bindings reuse immutable projections");
        foreach(string id in new[]{"nav_play","nav_hunters","nav_community","nav_studio","profile"})
            require(game["visible:"+id].Boolean&&!studio["visible:"+id].Boolean,"Studio hides game-only header control "+id);
        require(!game["visible:nav_studio_home"].Boolean&&studio["visible:nav_studio_home"].Boolean&&studio["visible:nav_theatre"].Boolean,
            "Studio header exposes Home and Replay Studio");
        require(studio["brand_title"].Text=="PROJECT PRIME STUDIO"&&studio["nav_theatre"].Text=="REPLAY STUDIO"&&game["nav_theatre"].Text=="THEATRE",
            "Application role labels distinguish Studio editor from game Theatre");
        var document=XDocument.Load(Path.Combine(AppContext.BaseDirectory,"header-app-shell.rml"));
        XElement Element(string id)=>document.Descendants().Single(element=>(string?)element.Attribute("id")==id);
        foreach(var pair in new Dictionary<string,string>{{"nav_play","route:home"},{"nav_hunters","route:hunter"},{"nav_community","route:forge"},{"nav_theatre","route:theatre"},{"nav_studio","studio:open"},{"nav_studio_home","studio:open"}})
            require((string?)Element(pair.Key).Attribute("data-action")==pair.Value,"Header typed destination remains exact: "+pair.Key);
        require(Element("nav_studio").Value=="PRIME STUDIO"&&Element("nav_theatre").Value=="THEATRE","Main header names Theatre and Prime Studio separately");
        foreach(string key in studio.Keys)
        {
            string[] parts=key.Split(':');string id=parts.Length>1?parts[1]:key;
            require(document.Descendants().Any(element=>(string?)element.Attribute("id")==id),"Role binding addresses authored element "+key);
        }
    }
}
