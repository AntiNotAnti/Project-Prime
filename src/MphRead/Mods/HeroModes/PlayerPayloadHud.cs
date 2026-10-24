using MphRead.Mods.Render.Hud;
using System;
using MphRead.Formats;
using MphRead.Hud;
using MphRead.Mods.HeroModes;
using MphRead.Mods.Multiplayer;
using ProjectPrime.PayloadEscort;
namespace MphRead.Entities;
public partial class PlayerEntity
{
    private static string PayloadTime(int ticks)
    {int seconds=(Math.Max(0,ticks)+59)/60;return $"{seconds/60}:{seconds%60:00}";}
    private void ModDrawPayloadClock()
    {
        if(ShowScoreboard || _scene.Payload.Frame is not {} f)return;
        using var layout=UseHudLayout(6,128,10);
        string title=f.Finished?"MATCH COMPLETE":f.Phase==0?"PREPARE":f.Phase==2?"SWAPPING SIDES":f.Overtime?"OVERTIME":"TIME REMAINING";
        DrawText2D(128,10,Align.Center,0,title,scale:.55f);
        DrawText2D(128,20,Align.Center,0,PayloadTime(f.Phase==0?f.Setup:f.Overtime?f.Fuse:f.Clock),scale:.65f);
    }
    private void ModDrawPayloadHud()
    {
        if(ShowScoreboard || _scene.GameState.MenuPause || _scene.GameState.MatchState!=MatchState.InProgress || _scene.Payload.Frame is not {} f)return;
        using(UseHudLayout(10,0,0))
        {
            string status=f.Phase==0?"PREPARING":f.Phase==2?"ROUND COMPLETE":f.Contested?"CONTESTED":f.Hold>0?"CHECKPOINT SECURED":f.Escorts>0?"MOVING":"STOPPED";
            DrawText2D(128,39,Align.Center,0,$"{100.0*f.Progress/f.Length:0.0}% | {status} | {f.Checkpoints}/3",scale:.45f);
            if(f.Target>=0)DrawText2D(128,47,Align.Center,0,$"TARGET {f.Target/(double)EscortRules.DistanceScale:0.00} U",scale:.4f);
        }
        if(f.Phase==1 && f.Tick-RulesPreparation<180)
            DrawText2D(128,92,Align.Center,0,TeamIndex==f.Attacker?"ESCORT THE PAYLOAD":"STOP THE PAYLOAD",scale:.75f);
        if(Health==0 && _respawnTimer>0)
            using(UseHudLayout(Array.IndexOf(HudProfileDefaults.ElementIds, "match.respawn"),128,150))DrawText2D(128,150,Align.Center,0,$"RESPAWN IN {(_respawnTimer+59)/60}",scale:.6f);
        if(Hunter==Hunter.Noxus)
            using(UseHudLayout(Array.IndexOf(HudProfileDefaults.ElementIds, "combat.abilityCooldown"),128,176))DrawText2D(128,176,Align.Center,0,ModCtzFreezeCooldown>0?$"FREEZE READY IN {(ModCtzFreezeCooldown+59)/60}s":"FREEZE READY",scale:.45f);
    }
    private static int RulesPreparation=>PayloadRuntime.Rules.PreparationTicks;
    private void ModDrawPayloadScoreboard()
    {
        if(_scene.Payload.Frame is not {} s)return;
        var ink=new ColorRgba(239,239,247,255);
        DrawText2D(128,3,Align.Center,0,"HERO SHOOTER / PUSH THE PAYLOAD",ink,scale:.6f);
        string title=s.Finished?(s.Winner<0?"DRAW":$"{TeamVisuals.Get(s.Winner).Label} WINS"):$"ROUND {s.Round+1} | {TeamVisuals.Get(s.Attacker).Label} ATTACKING | {PayloadTime(s.Clock)}";
        DrawText2D(128,15,Align.Center,0,title,ink,scale:.5f);
        float nameX=ModScoreNameColumn-18;
        DrawText2D(nameX,29,Align.Left,0,"HUNTERS",ink,scale:.65f);
        DrawText2D(ModScoreColumn1,29,Align.Center,0,"KILLS",ink,scale:.6f);
        DrawText2D(ModScoreColumn2,29,Align.Center,0,"DEATHS",ink,scale:.6f);ModDrawPingHeader(29);
        float y=42;
        for(int team=0;team<2;team++)
        {
            var visual=TeamVisuals.Get(team);long best=team==0?s.AlphaBest:s.BravoBest;int points=team==0?s.AlphaPoints:s.BravoPoints,bank=team==0?s.AlphaBank:s.BravoBank;
            DrawText2D(nameX,y,Align.Left,0,$"{visual.Label}: {points} PT | {best/(double)EscortRules.DistanceScale:0.00} U | BANK {PayloadTime(bank)}",visual.Color,scale:.48f);y+=12;
            for(int i=0;i<_scene.GameState.ActivePlayers;i++)
            {
                int slot=_scene.GameState.ResultSlots[i];var p=_scene.Players.Items[slot];if(p.TeamIndex!=team)continue;
                DrawPlayerName(nameX,y,Align.Left,0,(p.IsMainPlayer?"> ":"  ")+_scene.GameState.Nicknames[slot],ink,scale:.8f,maxWidth:(ModScoreColumn1-ModScoreNameColumn+8)/HudAspectFix);
                DrawText2D(ModScoreColumn1,y,Align.Center,0,_scene.GameState.Kills[slot].ToString(),ink);
                DrawText2D(ModScoreColumn2,y,Align.Center,0,_scene.GameState.Deaths[slot].ToString(),ink);ModDrawPingRow(y,ink,slot);y+=13;
            }
        }
    }
}
