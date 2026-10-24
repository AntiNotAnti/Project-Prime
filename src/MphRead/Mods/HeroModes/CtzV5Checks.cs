using System;
using System.IO;
using System.Linq;
using System.Reflection;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
namespace MphRead.Mods.HeroModes;

internal static class CtzV5Checks
{
    internal static void Run(Scene scene, Action<bool,string> check)
    {
        var stats=scene.GameState.CtzStats;
        var ally=scene.Players.Items[1];var spire=scene.Players.Items[3];var enemy=scene.Players.Items[4];
        var frame=typeof(Scene).GetField("_frameCount",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var invuln=typeof(PlayerEntity).GetField("_spawnInvulnTimer",BindingFlags.Instance|BindingFlags.NonPublic)!;
        void Ready(PlayerEntity p,Vector3 pos)
        {
            p.Spawn(pos,Vector3.UnitZ,Vector3.UnitY,p.NodeRef,false);
            p.ModSetSpectating(false);invuln.SetValue(p,(ushort)0);p.ModReleaseSpawnProtection();p.IsBot=false;
        }
        void Pulse(int ticks)
        {for(int i=0;i<ticks;i++){frame.SetValue(scene,scene.FrameCount+1);ally.ModTickCtzSupport();}}
        Ready(ally,new(0,1,0));Ready(spire,new(8,1,0));Ready(enemy,new(20,1,0));
        stats.Reset();ally.Health=100;
        ally.ModStartCtzHealingAura(spire);spire.Health=0;Pulse(60);
        check(stats.Get(3).Done==4 && stats.Get(1).Received==4,"Aura credits the original caster after that caster dies");
        ushort generation=NetPlayerLifecycle.Generation(3);
        NetPlayerLifecycle.SetOccupant(3,(ushort)(generation+1));Pulse(60);
        check(stats.Get(3).Done==0 && stats.Get(1).Received==8,"Continuing aura does not credit a new occupant of the caster slot");
        NetPlayerLifecycle.SetOccupant(3,generation);ally.ModResetCtzLifeSupport();Ready(spire,new(8,1,0));
        stats.Reset();ally.Health=100;
        ally.TakeDamage(20,DamageFlags.IgnoreInvuln|DamageFlags.NoDmgInvuln,null,enemy);
        check(stats.Get(4).Damage==20,"Accepted enemy hit credits actual Damage Done");
        check(ally.Health==80 && stats.Get(1).Taken==20,"Native accepted enemy damage records20 actual HP lost");
        ally.TakeDamage(7,DamageFlags.IgnoreInvuln|DamageFlags.NoDmgInvuln,null,ally);
        check(ally.Health==73 && stats.Get(1).Taken==27,"Native self damage contributes actual damage taken without healing credit");
        ally.TakeDamage(5,DamageFlags.IgnoreInvuln|DamageFlags.NoDmgInvuln,null,null);
        check(ally.Health==68 && stats.Get(1).Taken==32,"Native environmental damage contributes actual damage taken");
        ally.ModPlaceAt(new(-43,1,14));int before=ally.Health;
        ally.TakeDamage(100,DamageFlags.IgnoreInvuln|DamageFlags.NoDmgInvuln,null,enemy);
        check(ally.Health==before && stats.Get(1).Taken==32,"Spawn-door protected damage does not inflate damage taken");
        ally.ModPlaceAt(new(0,1,0));ally.Health=5;
        ally.TakeDamage(500,DamageFlags.IgnoreInvuln|DamageFlags.NoDmgInvuln,null,enemy);
        check(ally.Health==0 && stats.Get(1).Taken==37,"Lethal overkill records only remaining5HP, not500");
        check(stats.Get(4).Damage==25,"Overkill, self/environment and protected hits cannot inflate attacker Damage Done");
        var beforeRespawn=stats.Get(1);Ready(ally,new(0,1,0));
        check(stats.Get(1)==beforeRespawn,"Death and respawn preserve the same player's match totals");
        scene.CaptureZone.Apply(scene.CaptureZone.State with{Round=2});
        ally.ModZoneRoundRespawn();
        check(stats.Get(1)==beforeRespawn,"CTZ side swap and new round preserve match totals");
        scene.CaptureZone.Apply(scene.CaptureZone.State with{Round=1});Ready(ally,new(0,1,0));
        var tank=scene.Players.Items[5];Ready(tank,new(10,1,10));stats.Reset();tank.Health=160;
        tank.TakeDamage(20,DamageFlags.IgnoreInvuln|DamageFlags.NoDmgInvuln,null,ally);
        check(stats.Get(5).Taken==160-tank.Health && tank.Health==142,"Damage taken is measured after Kanden's damage reduction");
        stats.Reset();ally.Health=100;ally.ModGainCtzHealth(9,spire);
        var saved=stats.Capture();
        var world=ReplayAuthorityWorld.Capture(scene,1,1,123);
        byte[] encoded=world.Encode();var decoded=ReplayAuthorityWorld.Decode(encoded);
        check(encoded[0]==10 && decoded.Combat.Length==8 && decoded.Combat.SequenceEqual(saved),"Production authority payload roundtrips all eight support/damage rows");
        stats.Reset();stats.Apply(decoded.Combat);stats.Apply(decoded.Combat);
        check(stats.Get(3).Done==9 && stats.Get(1).Received==9,"Reapplying the same full snapshot replaces, never doubles, totals");
        var scratch=new ReplayAuthorityCaptureScratch();
        var scratchWorld=scratch.Capture(scene,1,1,124,_=>1);
        check(scratchWorld.Combat.SequenceEqual(saved),"Reusable real-server capture buffers include current combat totals");
        bool malformed=false;
        try{ReplayAuthorityWorld.Decode(encoded[..^1]);}catch(InvalidDataException){malformed=true;}
        check(malformed,"Truncated combat-total payload refused");
        malformed=false;var duplicate=ReplayAuthorityWorld.Decode(encoded);duplicate.Combat=[saved[0],saved[0]];
        try{duplicate.Encode();}catch(InvalidDataException){malformed=true;}
        check(malformed,"Duplicate statistic slots refused before transmission");
        malformed=false;
        try{using var stream=new MemoryStream();new CtzCombatRow(1,1,-1,0,0).Write(new BinaryWriter(stream));}catch(InvalidDataException){malformed=true;}
        check(malformed,"Negative healing totals rejected");
        ushort oldGeneration=NetPlayerLifecycle.Generation(1);NetPlayerLifecycle.SetOccupant(1,(ushort)(oldGeneration+1));
        stats.Apply(saved);
        check(stats.Get(1).Received==0 && stats.Get(1).Taken==0,"Stale-generation authority rows cannot follow a new occupant");
        NetPlayerLifecycle.SetOccupant(1,oldGeneration);
        stats.Apply([new CtzCombatRow(1,oldGeneration,int.MaxValue,int.MaxValue,int.MaxValue)]);
        stats.HealingDone(1,oldGeneration,500);stats.HealingReceived(1,500);stats.DamageTaken(1,500);
        check(stats.Get(1).Done==int.MaxValue && stats.Get(1).Received==int.MaxValue && stats.Get(1).Taken==int.MaxValue,"Large match totals saturate safely without wrapping negative");
        stats.ForgetSlot(1);check(stats.Get(1).Received==0,"Explicit network slot removal clears departing player's totals");
        var originalMode=scene.GameState.Mode;scene.GameState.Mode=GameMode.BattleTeams;
        check(CtzCombatStats.CanRecord(scene) && ReplayAuthorityWorld.Capture(scene,1,1,125).Encode()[0]==10,"Ordinary game modes record the shared authoritative combat totals");
        stats.Reset();Ready(ally,new(0,1,0));Ready(enemy,new(20,1,0));ally.Health=7;
        ally.TakeDamage(500,DamageFlags.IgnoreInvuln|DamageFlags.NoDmgInvuln,null,enemy);
        check(stats.Get(4).Damage==7 && stats.Get(1).Taken==7,"Battle uses actual HP deltas and excludes lethal overkill");
        scene.GameState.Mode=originalMode;
        foreach(bool ping in new[]{false,true})foreach(float width in new[]{244f,196f,150f})
        {
            var columns=CtzScoreboardLayout.Columns(6,6+width,ping);
            check(Math.Abs(columns[^1].Right-(6+width))<.01f,"Scoreboard columns fit available width "+width+" ping="+ping);
            for(int i=1;i<columns.Length;i++)check(columns[i].Left>=columns[i-1].Right-.01f,"Nonoverlapping column boundaries "+i+" width="+width+" ping="+ping);
            foreach(var col in columns)
            {
                float scale=CtzScoreboardLayout.FitScale(.8f,1000,120,col.Width,10);
                check(1000*scale<=col.Width-4+.01f && 120*scale<=10+.01f,"Huge text scale constrained to its cell "+col.Top+col.Bottom);
            }
        }
        stats.Reset();check(stats.Capture().All(r=>r.Done==0 && r.Received==0 && r.Taken==0),"New-match reset clears every player's support and damage totals");
    }
}
