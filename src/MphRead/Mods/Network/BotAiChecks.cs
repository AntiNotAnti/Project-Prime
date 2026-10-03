using System;
using System.Linq;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.MapGen;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

/// <summary>
/// Pure/headless regression checks for bot decision policy and generated navigation.
/// Asset-backed combat is still covered by NetBotCheck; these checks make tactical
/// regressions cheap enough to run on every relevant change.
/// </summary>
public static class BotAiChecks
{
    public static int Run()
    {
        int checks=0;
        void Check(bool condition,string name)
        {
            if(!condition)throw new InvalidOperationException("[bot-ai] FAIL "+name);
            checks++;Console.WriteLine("[bot-ai] PASS "+name);
        }

        Check(PlayerEntity.PlayerAiData.TacticalViewportContainsForTest(new(.5f,.5f)),
            "viewport accepts center");
        Check(!PlayerEntity.PlayerAiData.TacticalViewportContainsForTest(new(-.01f,.5f))
            && !PlayerEntity.PlayerAiData.TacticalViewportContainsForTest(new(.5f,1.01f)),
            "viewport rejects negative and overflow coordinates");

        float closeShock=PlayerEntity.PlayerAiData.TacticalWeaponUtilityForTest(
            BeamType.ShockCoil,6,false,false,false,false,1,true);
        float closeImp=PlayerEntity.PlayerAiData.TacticalWeaponUtilityForTest(
            BeamType.Imperialist,6,false,false,false,false,1,true);
        Check(closeShock>closeImp,"close range favors Shock Coil over Imperialist");

        float longShock=PlayerEntity.PlayerAiData.TacticalWeaponUtilityForTest(
            BeamType.ShockCoil,28,false,false,false,false,1,true);
        float longImp=PlayerEntity.PlayerAiData.TacticalWeaponUtilityForTest(
            BeamType.Imperialist,28,false,false,false,false,1,true);
        Check(longImp>longShock,"long range favors Imperialist over Shock Coil");
        Check(PlayerEntity.PlayerAiData.TacticalHunterWeaponBiasForTest(
            Hunter.Trace,BeamType.Imperialist,26,false)
            >PlayerEntity.PlayerAiData.TacticalHunterWeaponBiasForTest(
                Hunter.Trace,BeamType.Battlehammer,26,false),
            "Trace strongly prefers long-range Imperialist play");
        Check(PlayerEntity.PlayerAiData.TacticalHunterWeaponBiasForTest(
            Hunter.Noxus,BeamType.Imperialist,10,true)>0,
            "Noxus recognizes frozen-target finish opportunities");

        float ordinary=PlayerEntity.PlayerAiData.TacticalTargetUtilityForTest(
            14,true,false,false,1,0,false,2);
        float objective=PlayerEntity.PlayerAiData.TacticalTargetUtilityForTest(
            14,true,false,true,1,0,false,2);
        float threat=PlayerEntity.PlayerAiData.TacticalTargetUtilityForTest(
            14,true,false,false,1,0,true,2);
        Check(objective>ordinary&&threat>ordinary,"objective and recent threat raise target priority");
        Check(PlayerEntity.PlayerAiData.TacticalObjectivePriorityForTest(
                GameMode.Headhunter,5,false,false,false)
            >PlayerEntity.PlayerAiData.TacticalObjectivePriorityForTest(
                GameMode.Headhunter,1,false,false,false),
            "Headhunter prioritizes enemies carrying larger token banks");
        Check(PlayerEntity.PlayerAiData.TacticalObjectivePriorityForTest(
                GameMode.Hardpoint,0,true,false,false)>0
            &&PlayerEntity.PlayerAiData.TacticalObjectivePriorityForTest(
                GameMode.Hardpoint,0,false,false,false)==0,
            "Hardpoint prioritizes enemies occupying the active objective");

        Check(MapNodePacker.NavigationAnchorTypeForTest(MapNavigationLinkKind.Jump,
            MapNavigationAnchorKind.Auto,false,4)==NodeType.Aerial,
            "jump destination infers aerial node");
        Check(MapNodePacker.NavigationAnchorTypeForTest(MapNavigationLinkKind.Teleporter,
            MapNavigationAnchorKind.Auto,true,0)==NodeType.Special,
            "teleporter endpoint infers special node");
        Check(MapNodePacker.NavigationAnchorTypeForTest(MapNavigationLinkKind.Manual,
            MapNavigationAnchorKind.AltForm,true,0)==NodeType.AltForm,
            "authored alt-form node overrides automatic semantics");

        var jumpDefinition=new MapDefinition();
        jumpDefinition.JumpPads.Add(new MapJumpPad{Position=new[]{0f,0f,0f},Target=new[]{4f,3f,0f}});
        var effectiveLinks=MapNodePacker.EffectiveLinks(jumpDefinition);
        Check(effectiveLinks.Count==1&&effectiveLinks[0].Kind==MapNavigationLinkKind.JumpPad
            &&effectiveLinks[0].ToNodeKind==MapNavigationAnchorKind.Aerial,
            "target-based jump pad contributes an aerial navigation link");

        var hazard=MapNodePacker.Analyze(new[]{Floor(-8,8,-8,8,0,Terrain.Lava)});
        Check(hazard.Types.Length>0&&hazard.Types.All(t=>t==NodeType.Hazard),
            "damaging terrain generates hazard nodes");

        var tiers=MapNodePacker.Analyze(new[]{
            Floor(-8,0,-8,8,0,Terrain.Metal),
            Floor(0,8,-8,8,4,Terrain.Metal)
        });
        Check(tiers.Types.Contains(NodeType.Vantage),"elevated ledge generates vantage nodes");
        bool hasOneWayDrop=false;
        for(int i=0;i<tiers.Positions.Length;i++)
        {
            foreach(int j in tiers.Neighbours[i])
            {
                if(tiers.Positions[i].Y-tiers.Positions[j].Y<2)continue;
                bool reverse=tiers.Neighbours[j].Contains(i);
                if(!reverse){hasOneWayDrop=true;break;}
            }
            if(hasOneWayDrop)break;
        }
        Check(hasOneWayDrop,"generated drop does not become an impossible reverse climb");

        Console.WriteLine($"[bot-ai] {checks} checks passed");
        return 0;
    }

    private static BuiltFace Floor(float minX,float maxX,float minZ,float maxZ,float y,Terrain terrain)
    {
        var face=new BuiltFace(
        [
            new(minX,y,minZ),new(maxX,y,minZ),new(maxX,y,maxZ),new(minX,y,maxZ)
        ],
        [
            Vector2.Zero,Vector2.UnitX,Vector2.One,Vector2.UnitY
        ],Vector3.UnitY,0,1)
        {
            Terrain=terrain,
            Damaging=(int)terrain>=(int)Terrain.Lava
        };
        return face;
    }
}
