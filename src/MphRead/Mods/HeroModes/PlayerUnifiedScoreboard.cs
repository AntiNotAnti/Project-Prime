using System;
using System.Linq;
using System.Globalization;
using MphRead.Formats;
using MphRead.Hud;
using MphRead.Mods.Render.Hud;
using MphRead.Mods.HeroModes;
using MphRead.Mods.Network;
using MphRead.Mods.Multiplayer;

namespace MphRead.Entities;
public partial class PlayerEntity
{
    private string UnifiedMatchDetail()
    {
        if(HeroRules.IsZone(_scene.GameState.Mode)) {
            var s=_scene.CaptureZone.State;
            return $"ROUND {s.Round} | RED {s.RedWins} - {s.BlueWins} BLUE | {CtzPresentation.RoundTime(s)}";
        }
        if(HeroRules.IsPayload(_scene.GameState.Mode) && _scene.Payload.Frame is {} p)
            return $"ROUND {p.Round+1} | {TeamVisuals.Get(p.Attacker).Label} ATTACK | {PayloadTime(p.Clock)} | {p.Checkpoints}/3";
        if(_scene.GameState.Mode==GameMode.Oddball && _scene.GameState.Oddball is {} ball)
            return $"ROUND {ball.RoundNumber} | {ball.Score(0)} - {ball.Score(1)} | ROUNDS {ball.RoundsWon[0]} - {ball.RoundsWon[1]} | {PayloadTime(ball.RemainingTicks)}";
        return $"{_scene.GameState.TeamPoints[0]} - {_scene.GameState.TeamPoints[1]} | {FormatTime(TimeSpan.FromSeconds(Math.Max(0,_scene.GameState.MatchTime)))}";
    }
    private string UnifiedPlayerDetail(int slot)
    {
        var stat=_scene.GameState.CtzStats.Get(slot);
        string line=HeroRules.Active(_scene.GameState.Mode)?$"HEAL DONE {stat.Done}   RECEIVED {stat.Received}   ":"";
        if(HeroRules.IsPayload(_scene.GameState.Mode)) line+=$"ESCORT {stat.Objective/60}s   CONTEST {stat.Secondary/60}s";
        else if(HeroRules.IsZone(_scene.GameState.Mode)) line+=$"ZONE {stat.Objective/60}s   CONTEST {stat.Secondary/60}s";
        else if(_scene.GameState.Mode==GameMode.CaptureTheFlag) line+=$"CAPTURES {_scene.GameState.OctolithScores[slot]}   RETURNS {stat.Objective}   PICKUPS {stat.Secondary}   CARRY {(int)_scene.GameState.ObjectiveSeconds[slot]}s";
        else if(_scene.GameState.Mode==GameMode.Oddball) line+=$"BALL TIME {(_scene.GameState.Oddball?.HoldTicks[slot]??0)/60}s";
        return line;
    }
    private void ModDrawUnifiedScoreboard()
    {
        var color=HudProfiles.Runtime.AccentInk;
        var ink=new ColorRgba((byte)(color.X*255),(byte)(color.Y*255),(byte)(color.Z*255),255);
        _scene.DrawHudFlatBox(3,1,253,190,new OpenTK.Mathematics.Vector4(.015f,.025f,.045f,.96f));
        float left=5,right=251;
        ModDrawCtzScoreCell(MatchTypeCatalog.BaseLabel(_scene.GameState.Mode),left,right-left,3,10,ink,.72f,Align.Left);
        ModDrawCtzScoreCell(UnifiedMatchDetail(),left,right-left,16,9,ink,.58f,Align.Left);
        string[] headers=["PLAYER","K","A","D","DMG DONE","DMG TAKEN","PING"];
        float[] weights=[86,16,16,16,43,43,26];
        float x=left;
        var columns=new CtzScoreColumn[headers.Length];
        for(int i=0;i<headers.Length;i++) {
            columns[i]=new(headers[i],"",x,weights[i]);
            ModDrawCtzScoreCell(headers[i],x,weights[i],29,7,ink,.46f,i==0?Align.Left:Align.Right);
            x+=weights[i];
        }
        float y=40;
        int teams=_scene.GameState.Teams?Math.Max(2,_scene.GameState.TeamCount):1;
        for(int team=0;team<teams;team++) {
            var players=Enumerable.Range(0,_scene.GameState.ActivePlayers).Select(i=>_scene.GameState.ResultSlots[i])
                .Where(slot=>!_scene.GameState.Teams||_scene.Players.Items[slot].TeamIndex==team).ToArray();
            if(players.Length==0)continue;
            if(_scene.GameState.Teams) {
                string label=TeamVisuals.Get(team).Label;
                if(HeroRules.IsPayload(_scene.GameState.Mode) && _scene.Payload.Frame is {} p)
                    label+=$" | {(team==0?p.AlphaPoints:p.BravoPoints)} PT | {(team==0?p.AlphaBest:p.BravoBest)/(double)ProjectPrime.PayloadEscort.EscortRules.DistanceScale:0.00} U | BANK {PayloadTime(team==0?p.AlphaBank:p.BravoBank)}";
                ModDrawCtzScoreCell(label,left,right-left,y,8,TeamVisuals.Get(team).Color,.5f,Align.Left);y+=9;
            }
            foreach(int slot in players) {
                var stat=_scene.GameState.CtzStats.Get(slot);
                string[] values=[(_scene.Players.Items[slot].IsMainPlayer?"> ":"")+_scene.GameState.Nicknames[slot],
                    _scene.GameState.Kills[slot].ToString(),stat.Assists.ToString(),_scene.GameState.Deaths[slot].ToString(),
                    stat.Damage.ToString(CultureInfo.InvariantCulture),stat.Taken.ToString(CultureInfo.InvariantCulture),
                    NetSession.SlotPing[slot]>0?NetSession.SlotPing[slot].ToString():"--"];
                for(int i=0;i<values.Length;i++) {
                    var c=columns[i];ModDrawCtzScoreCell(values[i],c.Left,c.Width,y,8,ink,.62f,i==0?Align.Left:Align.Right,ellipsis:i==0);
                }
                ModDrawCtzScoreCell(UnifiedPlayerDetail(slot),left+4,right-left-4,y+8,6,ink,.45f,Align.Left);
                y+=teams>2?14:15;
            }
        }
    }
}
