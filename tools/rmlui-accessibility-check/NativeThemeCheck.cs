using MphRead.Mods.Launcher.RmlUi.Components;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Render;
using System.Runtime.InteropServices;

internal static class NativeThemeCheck
{
    internal static int Run(string root)
    {
        int checks=0;void Check(bool value,string name){if(!value)throw new InvalidOperationException(name);checks++;Console.WriteLine("PASS "+name);}
        foreach(var viewport in new[]{(640,320,1f),(1280,640,2f),(1280,720,1f),(1024,768,1f),(1440,900,1f),(2560,1080,1f),(320,640,1f)})
        {
            using var host=new RmlUiHost();Check(host.Initialize(viewport.Item1,viewport.Item2,viewport.Item3,root,RmlUiRenderBackend.DrawList),"theme real native initialization");
            var doc=host.OpenDocument("pages/home/home.rml",RmlUiDocumentLayer.Page);
            host.SetText(doc,"text_activity_group_1","MULTIPLAYER");host.SetText(doc,"text_activity_title_1","ONLINE MULTIPLAYER");
            host.SetText(doc,"text_activity_description_1","Find live matches, browse servers, or create a private room.");host.SetText(doc,"text_activity_hint_1","CHANGE ACTIVITY");
            host.SetText(doc,"text_activity_action_1","PLAY ONLINE");host.SetText(doc,"text_activity_group_2","MULTIPLAYER ACTIVITY");
            host.SetBool(doc,"visible:footer_actions",true);host.SetBool(doc,"class:activity_drawer:open",false);host.SetBool(doc,"class:activity_compact:hidden",false);
            host.SetBool(doc,"visible:session_panel",false);host.Update();
            RmlUiVisualPolicy.Apply(host,doc,new(true,true,false,false));host.Update();
            (float X,float Y,float W,float H) Bounds(string id){Check(host.TryGetElementBounds(doc,id,out float x,out float y,out float w,out float h),"theme positive bounds "+id);return(x,y,w,h);}
            var top=Bounds("topbar");var footer=Bounds("footerbar");var panel=Bounds("activity_panel");var selector=Bounds("activity_selector");
            Check(selector.Y>=top.Y+top.H-1,"Home primary activity does not overlap header");
            Check(panel.Y>=top.Y+top.H-1&&panel.Y+panel.H<=footer.Y+1,"Home activity viewport stays between chrome");
            foreach(int language in Enumerable.Range(0,6)) {
                RmlUiChromeLocalization.Apply(host,doc,language);host.Update();
                foreach(string id in new[]{"nav_hunters","profile","header_settings","footer_quit"}) {
                    var b=Bounds(id);Check(b.X>=-1&&b.X+b.W<=viewport.Item1+1,"localized chrome within viewport "+id);
                }
            }
            var reader=new RmlUiDrawListReader();
            foreach(bool contrast in new[]{false,true})
            foreach(string route in new[]{"home","hunters","community","studio","settings"}) {
                RmlUiVisualPolicy.Apply(host,doc,new(true,contrast,false,false));
                RmlUiChromeRoutePolicy.Apply(host,doc,route);host.Update();host.Render(viewport.Item1,viewport.Item2);
                var frame=reader.Capture();string selected=RmlUiChromeRoutePolicy.ActiveNavigation(route);
                bool correct=true;
                foreach(string id in new[]{"nav_play","nav_hunters","nav_community","nav_studio"}) {
                    if(!host.TryGetElementBounds(doc,id,out float x,out float y,out float w,out float h)) {correct=false;break;}
                    bool painted=PaintedHighlight(frame,contrast?0xff66d8ffu:0xffffc772u,x,y,w,h,viewport.Item3);
                    correct&=painted==(id==selected);
                    if(painted!=(id==selected)) {
                        Console.WriteLine($"NAV DEBUG route={route} id={id} expected={id==selected} painted={painted} bounds={x},{y},{w},{h}");
                        Console.WriteLine("NAV DEBUG vertices="+string.Join(";",frame.Commands.Where(c=>c.Kind==RmlUiDrawCommandKind.Geometry&&c.Texture==0)
                            .SelectMany(c=>frame.Geometry[c.Geometry].Vertices.Select(v=>$"{v.Color:x8}@{v.X+c.TranslationX},{v.Y+c.TranslationY}"))
                            .Where(s=>s.StartsWith(contrast?"ff66d8ff":"ffffc772")).Take(24)));
                    }
                }
                Check(correct,"actual native primary navigation highlight follows "+route+" contrast="+contrast);
            }
            var social=Bounds("header_settings");host.Input.PointerMoved(social.X+social.W/2,social.Y+social.H/2);host.Input.PointerButton(0,social.X+social.W/2,social.Y+social.H/2,true);host.Input.PointerButton(0,social.X+social.W/2,social.Y+social.H/2,false);host.Update();
            Check(host.TryTakeIntent(out var action)&&RmlUiIntentRegistry.ToLegacy(action)=="route:settings","real compact Settings header pointer action");
            var hunter=Bounds("nav_hunters");host.Input.PointerMoved(hunter.X+hunter.W/2,hunter.Y+hunter.H/2);host.Input.PointerButton(0,hunter.X+hunter.W/2,hunter.Y+hunter.H/2,true);host.Input.PointerButton(0,hunter.X+hunter.W/2,hunter.Y+hunter.H/2,false);host.Update();
            Check(host.TryTakeIntent(out action)&&action.Kind==RmlUiIntentKind.HunterOpen,"shared HUNTERS opens actual Hunter selection");
            if(viewport.Item2/viewport.Item3<=480) {
                var before=Bounds("deploy");host.Input.PointerMoved(panel.X+panel.W/2,panel.Y+panel.H/2);host.Input.PointerWheel(-7);
                for(int frame=0;frame<25;frame++){Thread.Sleep(16);host.Update();}
                var after=Bounds("deploy");Check(after.Y+after.H<=footer.Y+1,"short Home wheel reaches deploy within stage");
                Check(after.Y<=before.Y,"short Home scroll preserves ordered activity flow");
            }
            RmlUiVisualPolicy.Apply(host,doc,new(true,true,true,true));host.Update();
            Check(Bounds("header_settings").H>=48*viewport.Item3,"touch policy supplies48dp Settings target");
            Check(Bounds("nav_hunters").H>=48*viewport.Item3,"touch policy supplies48dp Hunter target");
            host.Render(viewport.Item1,viewport.Item2);
            Check(DrawFeatures()==0,"shared theme renders without unsupported layer/filter/shader features");
        }
        return checks;
    }
    [DllImport("ProjectPrime.RmlUi.Native",EntryPoint="pp_rmlui_draw_features",CallingConvention=CallingConvention.Cdecl)]
    private static extern uint DrawFeatures();

    private static bool PaintedHighlight(RmlUiDrawListFrame frame,uint color,float x,float y,float w,float h,float density)
    {
        foreach(var command in frame.Commands) {
            if(command.Kind!=RmlUiDrawCommandKind.Geometry||command.Texture!=0)continue;
            var geometry=frame.Geometry[command.Geometry];
            for(int i=0;i<geometry.Indices.Length;i+=3) {
                var a=geometry.Vertices[geometry.Indices[i]];var b=geometry.Vertices[geometry.Indices[i+1]];var c=geometry.Vertices[geometry.Indices[i+2]];
                if(a.Color!=color||b.Color!=color||c.Color!=color)continue;
                float centerX=(a.X+b.X+c.X)/3+command.TranslationX;
                float centerY=(a.Y+b.Y+c.Y)/3+command.TranslationY;
                if(centerX>x&&centerX<x+w&&centerY>=y+h-8*density&&centerY<=y+h+2*density)return true;
            }
        }
        return false;
    }
}
