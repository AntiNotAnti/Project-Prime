using System;
using System.Globalization;
using MphRead.Formats;
using MphRead.Hud;
using MphRead.Mods;
using MphRead.Mods.HeroModes;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Hud;

namespace MphRead.Entities;
public partial class PlayerEntity
{
    private void ModDrawCtzScoreboard()
    {
        var s=_scene.CaptureZone.State;
        var ink=new ColorRgba(239,239,247,255);
        bool ping=ModPingColumnDrawn || CtzScoreboardEvidence.ForcePing;
        float right=EndScreen.Available ? Math.Min(250,254-EndPanelWidth*HudAspectFix-4) : 250;
        float left=6, width=right-left;
        ModDrawCtzScoreCell(CtzPresentation.ModeTitle,left,width-39,4,10,ink,.8f,Align.Left);
        ModDrawCtzScoreCell(CtzPresentation.RoundTime(s),right-38,38,5,9,ink,.7f,Align.Right);
        string round=s.Phase==CtzPhase.Finished
            ? (s.RedWins>s.BlueWins ? "RED TEAM WINS" : "BLUE TEAM WINS")
            : $"ROUND {s.Round} | FIRST TO 2 ROUND WINS";
        ModDrawCtzScoreCell(round,left,width,17,9,ink,.62f,Align.Left);
        var columns=CtzScoreboardLayout.Columns(left,right,ping);
        for(int col=0;col<columns.Length;col++)
        {
            var c=columns[col];
            ModDrawCtzScoreCell(c.Top,c.Left,c.Width,CtzScoreboardLayout.HeaderY,7,ink,.58f,col==0?Align.Left:Align.Center);
            if(c.Bottom.Length>0)
                ModDrawCtzScoreCell(c.Bottom,c.Left,c.Width,CtzScoreboardLayout.SubheaderY,7,ink,.58f,Align.Center);
        }
        float y=CtzScoreboardLayout.FirstTeamY;
        for(int team=0;team<2;team++)
        {
            var color=TeamVisuals.Get(team).Color;
            ModDrawCtzScoreCell(CtzPresentation.TeamScore(s,team),left,width,y,9,color,.68f,Align.Left);
            y+=12;
            for(int i=0;i<_scene.GameState.ActivePlayers;i++)
            {
                int slot=_scene.GameState.ResultSlots[i];
                var player=_scene.Players.Items[slot];
                if(player.TeamIndex!=team)continue;
                var totals=_scene.GameState.CtzStats.Get(slot);
                string name=(player.IsMainPlayer?"> ":"  ")+_scene.GameState.Nicknames[slot];
                var n=columns[0];
                ModDrawCtzScoreCell(name,n.Left,n.Width,y,10,ink,.8f,Align.Left,ellipsis:true);
                int[] values=[_scene.GameState.Kills[slot],_scene.GameState.Deaths[slot],totals.Done,totals.Received,totals.Taken];
                for(int j=0;j<values.Length;j++)
                {
                    var c=columns[j+1];
                    ModDrawCtzScoreCell(values[j].ToString(CultureInfo.InvariantCulture),c.Left,c.Width,y,10,ink,.8f,Align.Right);
                }
                if(ping)
                {
                    int latency=NetSession.SlotPing[slot];
                    var c=columns[^1];
                    ModDrawCtzScoreCell(latency<=0?"--":Math.Min(999,latency).ToString(CultureInfo.InvariantCulture),
                        c.Left,c.Width,y,10,ink,.72f,Align.Right);
                }
                y+=CtzScoreboardLayout.RowHeight;
            }
            y+=3;
        }
    }

    // Cell-bounded typography honors the selected HUD font/color while clamping
    // scale to the column width and row height. Long names ellipsize, never push
    // a statistic into the next column. Large values keep their exact digits.
    private void ModDrawCtzScoreCell(string text,float left,float width,float y,float height,
        ColorRgba color,float preferred,Align align,bool ellipsis=false)
    {
        string? previous=_hudTextOverride;
        _hudTextOverride="all.other";
        try
        {
            var selected=HudProfiles.Runtime.Typography.Get("all.other");
            // Small scoreboard cells need filtered glyphs: downsampling the native
            // nearest-neighbour font discards entire horizontal strokes.
            var style=new HudTextStyle {Enabled=true,Font=selected.Enabled?selected.Font:"font03",
                Size=selected.Enabled?selected.Size:1,Outline=Math.Min(selected.Enabled?selected.Outline:0,height/8),
                Color=selected.Color,OutlineColor=selected.OutlineColor,MatchPalette=selected.MatchPalette,
                PreserveSemanticColors=true,Opacity=selected.Opacity,FitToScreen=false};
            style.Validate();
            float global=HudProfiles.Runtime.Mode==HudMode.Custom?HudProfiles.Runtime.TextScale:1;
            float Measure(string value)
            {
                if(style.Enabled)
                {
                    var atlas=HudFonts.Get(style.Font);float total=0;
                    foreach(char ch in value)total+=atlas.Advances[Math.Clamp(ch-32,0,95)];
                    // Mirrors DrawStyledHudText: size=24 at base scale1, 48px atlas, unit1/3.
                    return total/6*style.Size*HudProfiles.Runtime.TextScale*HudAspectFix;
                }
                return PlayerNameLayout.Measure(value)*global*HudAspectFix;
            }
            float lineHeight=style.Enabled ? (64f/6)*style.Size*HudProfiles.Runtime.TextScale : 12*global;
            // The typography outline is a fixed HUD-space offset, not multiplied
            // by the fitted glyph scale. Reserve it independently inside the cell.
            float outlineY=style.Enabled ? style.Outline/3 : 0, outlineX=outlineY*HudAspectFix;
            float innerWidth=Math.Max(4.01f,width-2*outlineX), innerHeight=Math.Max(.01f,height-2*outlineY);
            float preferredHeight=Math.Min(preferred,innerHeight/Math.Max(.01f,lineHeight));
            if(ellipsis && Measure(text)*preferredHeight>innerWidth-4)
            {
                while(text.Length>0 && Measure(text+"...")*preferredHeight>innerWidth-4)text=text[..^1];
                text+="...";
            }
            float measured=Measure(text);
            float scale=CtzScoreboardLayout.FitScale(preferred,measured,lineHeight,innerWidth,innerHeight);
            float x=align==Align.Left?left+2+outlineX:align==Align.Right?left+width-2-outlineX:left+width/2;
            float drawnWidth=measured*scale;
            float start=align==Align.Left?x:align==Align.Right?x-drawnWidth:x-drawnWidth/2;
            CtzScoreboardEvidence.Capture?.Add(new(text,start-outlineX,y,drawnWidth+2*outlineX,lineHeight*scale+2*outlineY,left,width,height));
            DrawStyledHudText(x,y+outlineY,align,text,scale*24,style,selected.Enabled && !selected.FontOnlyOverride ? null : color,1);
        }
        finally{_hudTextOverride=previous;}
    }
}
