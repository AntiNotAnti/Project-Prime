#if MPHREAD_STUDIO
using System;
using System.Linq;
using MphRead.Entities;
using MphRead.Hud;
using MphRead.Mods.Chat;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;
namespace MphRead.Mods.StudioReplay;

/// <summary>Replay annotation/analytics layer, independently composited above Game HUD.</summary>
internal sealed class StudioReplayOverlay
{
    private readonly ReplayPlaybackSession _session;
    private HudObjectInstance? _font;
    private Scene? _scene;
    private readonly ReplayAnalyticsSnapshot _analytics;
    internal StudioReplayOverlay(ReplayPlaybackSession session){_session=session;_analytics=ReplayStudio.Analytics(session.Events,session.LastFrame);}
    internal void Draw(Scene scene)
    {
        if(!ReferenceEquals(_scene,scene))
        {
            _scene=scene;_font=new HudObjectInstance(ChatFont.Cell,ChatFont.Cell);
            _font.SetPaletteData(new[]{new ColorRgba(),new ColorRgba(255,255,255,255)},scene);
            _font.SetCharacterData(ChatFont.Pixels,scene);_font.Enabled=true;
        }
        bool compact=scene.Size.Y<360;
        float top=compact?174:3;
        // A split view can be taller than it is wide. Reserve the native HUD's
        // height-scaled left counters and right radar before placing annotations.
        float aspect=scene.Size.Y/(float)Math.Max(1,scene.Size.X);
        float left=Math.Max(72,256*aspect*.27f),right=Math.Min(207,256-256*aspect*.25f);
        if(right-left<40){top=174;compact=true;left=72;right=207;}
        float textLeft=left+3,textRight=right-3;
        scene.DrawHudFlatBox(left,top,right,compact?190:27,new Vector4(0,0,0,0.72f));
        uint frame=(uint)Math.Max(0,scene.ReplayPresentationFrame);
        Text(scene,textLeft,top+3,$"Replay {ReplayHud.Time(frame)} / {ReplayHud.Time(_session.LastFrame)}   P{scene.Players.MainPlayerIndex+1}",textRight);
        var row=_analytics.Players.FirstOrDefault(p=>p.Slot==scene.Players.MainPlayerIndex);
        if(!compact)Text(scene,textLeft,13,$"K {row.Kills}   D {row.Deaths}   DMG {row.Damage}   OBJ {row.ObjectiveEvents}",textRight);
        float progress=_session.LastFrame==0?0:Math.Clamp(frame/(float)_session.LastFrame,0,1);
        float bar=compact?187:22;
        scene.DrawHudFlatBox(textLeft,bar,textRight,bar+2,new Vector4(.3f,.3f,.3f,1));scene.DrawHudFlatBox(textLeft,bar,textLeft+(textRight-textLeft)*progress,bar+2,new Vector4(.3f,1,.65f,1));
        foreach(var marker in _session.Events)
            if(marker.Type is ReplayEventType.Kill or ReplayEventType.Headshot or ReplayEventType.Objective)
            {float x=textLeft+(textRight-textLeft)*marker.Frame/Math.Max(1f,_session.LastFrame);scene.DrawHudFlatBox(x,bar-1,x+1,bar+3,new Vector4(1,.7f,.3f,1));}
    }
    private void Text(Scene scene,float x,float y,string text,float maxX)
    {
        var font=_font!;font.Alpha=1;
        foreach(char ch in text)
        {int index=ChatFont.Index(ch);if(index<0)continue;font.PositionX=x/256;font.PositionY=y/192;font.SetData(index,new ColorRgba(210,255,225,255),scene);scene.DrawHudObject(font,mode:1,scale:.55f,isText:true);x+=ChatFont.Widths[index]*.55f*scene.Size.Y/192f*256f/Math.Max(1,scene.Size.X);if(x>maxX)break;}
    }
}

#endif
