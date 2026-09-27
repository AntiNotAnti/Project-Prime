using System;
using System.Collections.Generic;
using System.Linq;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.MapEditor
{
    public enum MapTemplateAction
    {
        Create,
        ImportQ3,
        CloneNative
    }

    public sealed record MapTemplateInfo(
        string Id,
        string Name,
        string Description,
        string RecommendedPlayers,
        IReadOnlyList<string> SupportedModes,
        MapTemplateAction Action = MapTemplateAction.Create);

    public static class MapTemplates
    {
        public static IReadOnlyList<MapTemplateInfo> Catalog { get; } = new[]
        {
            new MapTemplateInfo("basic-ffa","Basic FFA",
                "Playable free-for-all shell with eight radial spawns, perimeter cover and a simple central pickup loop.",
                "4–8",new[]{"Battle","Survival"}),
            new MapTemplateInfo("duel-1v1","1v1 Duel",
                "Compact duel layout with opposing starts, mirrored cover, a raised center lane and deliberately sparse resources.",
                "2",new[]{"Battle","Survival"}),
            new MapTemplateInfo("team-symmetric","Team Symmetric",
                "Mirrored red/blue bases with team spawns, side lanes, central cover and symmetric resource placement.",
                "4–8",new[]{"BattleTeams","SurvivalTeams"}),
            new MapTemplateInfo("vertical-arena","Vertical Arena",
                "Three-height arena with elevated platforms, lower routes and jump pads for vertical combat.",
                "4–8",new[]{"Battle","Survival"}),
            new MapTemplateInfo("jump-pad-playground","Jump Pad Playground",
                "Movement-test arena with four launch pads, elevated landing platforms and open sightlines.",
                "2–8",new[]{"Battle","Survival"}),
            new MapTemplateInfo("large-outdoor","Large Outdoor",
                "Large open starter with long sightlines, perimeter cover, distributed spawns and generous runtime bounds.",
                "6–12",new[]{"Battle","Survival"}),
            new MapTemplateInfo("import-review","Import Review Workspace",
                "Starts the Quake 3 BSP/PK3 import flow so the result opens directly in the collision, material and health review workflow.",
                "Any",new[]{"Import / Review"},MapTemplateAction.ImportQ3),
            new MapTemplateInfo("native-remix","Native Remix Starter",
                "Starts the built-in room clone workflow and creates a safe remix project without overwriting cartridge content.",
                "Varies",new[]{"Native Remix"},MapTemplateAction.CloneNative)
        };

        public static MapTemplateInfo Get(string id)
            => Catalog.FirstOrDefault(t=>t.Id.Equals(id,StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"Unknown map template {id}.",nameof(id));

        // Compatibility for older tests/callers.
        public static MapProject Create(string name, bool enclosed = false, bool teams = false)
            => Create(name, teams ? "team-symmetric" : enclosed ? "basic-ffa" : "basic-ffa");

        public static MapProject Create(string name, string templateId)
        {
            MapValidator.RequireRuntimeName(name);
            MapTemplateInfo info=Get(templateId);
            if(info.Action!=MapTemplateAction.Create)
                throw new InvalidOperationException($"{info.Name} launches a source workflow instead of creating a blank project.");

            var d=Base(name,info);
            switch(info.Id)
            {
                case "basic-ffa": BasicFfa(d); break;
                case "duel-1v1": Duel(d); break;
                case "team-symmetric": TeamSymmetric(d); break;
                case "vertical-arena": VerticalArena(d); break;
                case "jump-pad-playground": JumpPadPlayground(d); break;
                case "large-outdoor": LargeOutdoor(d); break;
                default: throw new InvalidOperationException($"Template {info.Id} has no builder.");
            }
            return new(d);
        }

        public static MapDefinition Preview(string templateId)
        {
            MapTemplateInfo info=Get(templateId);
            if(info.Action!=MapTemplateAction.Create)
            {
                var d=Base("Preview",info);
                d.Geometry.Add(new MapBox{Label=info.Action==MapTemplateAction.ImportQ3?"BSP / PK3":"Native room",
                    Transform=new(){Position=new[]{0f,.5f,0},Scale=new[]{26f,1,18f}},Solid=false});
                return d;
            }
            return Create("Preview",templateId).Definition;
        }

        private static MapDefinition Base(string name,MapTemplateInfo info)
        {
            var d=new MapDefinition
            {
                FormatVersion=2,MapId=Guid.NewGuid(),Name=name.ToUpperInvariant(),InGameName=name,
                Version="1.0.0",FogEnabled=false,KillHeight=-20,FarClip=350,
                Preview=new(){Position=new[]{25f,22,30},Target=new[]{0f,2,0}},
                Capabilities=new(){SupportedModes=info.SupportedModes
                    .Where(m=>m is not "Import / Review" and not "Native Remix").ToList()}
            };
            if(d.Capabilities.SupportedModes.Count==0)
                d.Capabilities.SupportedModes=new(){"Battle","Survival"};
            d.Materials.Add(new(){Id=Guid.NewGuid(),Name="Arena",SourceMaterial=2,TexScale=16});
            d.Materials.Add(new(){Id=Guid.NewGuid(),Name="Accent",SourceMaterial=1,TexScale=16});
            return d;
        }

        private static void Floor(MapDefinition d,float width,float depth,float y=-.5f)
            => d.Geometry.Add(new MapBox{Label="Floor",Layer="Architecture",
                Transform=new(){Position=new[]{0f,y,0},Scale=new[]{width,1f,depth}}});

        private static void Walls(MapDefinition d,float width,float depth,float height=10)
        {
            float x=width/2,z=depth/2;
            d.Geometry.Add(new MapBox{Label="West wall",Layer="Architecture",Transform=new(){Position=new[]{-x,height/2,0},Scale=new[]{1f,height,depth}}});
            d.Geometry.Add(new MapBox{Label="East wall",Layer="Architecture",Transform=new(){Position=new[]{x,height/2,0},Scale=new[]{1f,height,depth}}});
            d.Geometry.Add(new MapBox{Label="North wall",Layer="Architecture",Transform=new(){Position=new[]{0,height/2,-z},Scale=new[]{width,height,1f}}});
            d.Geometry.Add(new MapBox{Label="South wall",Layer="Architecture",Transform=new(){Position=new[]{0,height/2,z},Scale=new[]{width,height,1f}}});
        }

        private static void RadialSpawns(MapDefinition d,int count,float radius,bool teams=false,float y=.1f)
        {
            for(int i=0;i<count;i++)
            {
                float angle=i*MathF.Tau/count;
                d.Spawns.Add(new(){Id=Guid.NewGuid(),Label=$"Spawn {i+1}",
                    Position=new[]{MathF.Sin(angle)*radius,y,MathF.Cos(angle)*radius},
                    Yaw=angle*180/MathF.PI+180,Team=teams?i%2:-1});
            }
        }

        private static void BasicFfa(MapDefinition d)
        {
            Floor(d,40,40);Walls(d,40,40,9);RadialSpawns(d,8,13);
            for(int i=0;i<4;i++)
            {
                float a=i*MathF.PI/2;
                d.Geometry.Add(new MapBox{Label=$"Cover {i+1}",Layer="Gameplay",Material=1,
                    Transform=new(){Position=new[]{MathF.Sin(a)*7,1.25f,MathF.Cos(a)*7},Scale=new[]{3f,2.5f,6f}}});
            }
            d.Items.Add(new(){Id=Guid.NewGuid(),Label="Center health",Type="HealthMedium",Position=new[]{0f,.1f,0}});
            d.Items.Add(new(){Id=Guid.NewGuid(),Type="UABig",Position=new[]{10f,.1f,0}});
            d.Items.Add(new(){Id=Guid.NewGuid(),Type="MissileBig",Position=new[]{-10f,.1f,0}});
        }

        private static void Duel(MapDefinition d)
        {
            Floor(d,32,24);Walls(d,32,24,8);
            d.Spawns.Add(new(){Id=Guid.NewGuid(),Label="Duel A",Position=new[]{-11f,.1f,0},Yaw=90});
            d.Spawns.Add(new(){Id=Guid.NewGuid(),Label="Duel B",Position=new[]{11f,.1f,0},Yaw=270});
            d.Geometry.Add(new MapBox{Label="Center dais",Layer="Gameplay",Material=1,
                Transform=new(){Position=new[]{0,.75f,0},Scale=new[]{6f,1.5f,6f}}});
            foreach(float z in new[]{-6f,6f})
            {
                d.Geometry.Add(new MapBox{Label="Duel cover",Layer="Gameplay",
                    Transform=new(){Position=new[]{-5f,1.25f,z},Scale=new[]{2f,2.5f,4f}}});
                d.Geometry.Add(new MapBox{Label="Duel cover",Layer="Gameplay",
                    Transform=new(){Position=new[]{5f,1.25f,-z},Scale=new[]{2f,2.5f,4f}}});
            }
            d.Items.Add(new(){Id=Guid.NewGuid(),Type="HealthMedium",Position=new[]{0f,1.6f,0}});
            d.Items.Add(new(){Id=Guid.NewGuid(),Type="UASmall",Position=new[]{0f,.1f,-8f}});
            d.Items.Add(new(){Id=Guid.NewGuid(),Type="MissileSmall",Position=new[]{0f,.1f,8f}});
        }

        private static void TeamSymmetric(MapDefinition d)
        {
            Floor(d,52,34);Walls(d,52,34,10);
            for(int team=0;team<2;team++)
            {
                float sign=team==0?-1:1;
                for(int i=0;i<4;i++)
                {
                    float z=(i-1.5f)*4;
                    d.Spawns.Add(new(){Id=Guid.NewGuid(),Label=$"Team {(team==0?"A":"B")} {i+1}",
                        Position=new[]{sign*19,.1f,z},Yaw=team==0?90:270,Team=team});
                }
                d.Geometry.Add(new MapBox{Label=$"Team {(team==0?"A":"B")} base",Layer="Team Bases",Material=1,
                    Transform=new(){Position=new[]{sign*19,1.25f,0},Scale=new[]{6f,2.5f,12f}}});
                d.Items.Add(new(){Id=Guid.NewGuid(),Type="HealthMedium",Position=new[]{sign*15,.1f,0}});
                d.Items.Add(new(){Id=Guid.NewGuid(),Type="UABig",Position=new[]{sign*11,.1f,7f}});
            }
            d.Geometry.Add(new MapBox{Label="Center cover",Layer="Gameplay",
                Transform=new(){Position=new[]{0,1.5f,0},Scale=new[]{3f,3f,12f}}});
        }

        private static void VerticalArena(MapDefinition d)
        {
            Floor(d,34,34);Walls(d,34,34,16);RadialSpawns(d,8,11);
            d.Geometry.Add(new MapBox{Label="Upper platform",Layer="Vertical",Material=1,
                Transform=new(){Position=new[]{0,8,0},Scale=new[]{10f,1f,10f}}});
            d.Geometry.Add(new MapBox{Label="West platform",Layer="Vertical",
                Transform=new(){Position=new[]{-10,4,-7},Scale=new[]{9f,1f,7f}}});
            d.Geometry.Add(new MapBox{Label="East platform",Layer="Vertical",
                Transform=new(){Position=new[]{10,4,7},Scale=new[]{9f,1f,7f}}});
            d.JumpPads.Add(new(){Id=Guid.NewGuid(),Label="Lower to upper",Position=new[]{0f,.1f,-9f},Target=new[]{0f,8.7f,0}});
            d.JumpPads.Add(new(){Id=Guid.NewGuid(),Label="West lift",Position=new[]{-10f,.1f,6f},Target=new[]{-10f,4.7f,-7f}});
            d.JumpPads.Add(new(){Id=Guid.NewGuid(),Label="East lift",Position=new[]{10f,.1f,-6f},Target=new[]{10f,4.7f,7f}});
            d.Items.Add(new(){Id=Guid.NewGuid(),Type="HealthBig",Position=new[]{0f,8.7f,0}});
        }

        private static void JumpPadPlayground(MapDefinition d)
        {
            Floor(d,40,40);Walls(d,40,40,14);RadialSpawns(d,6,13);
            Vector3[] targets={
                new(-11,6,-11),new(11,6,-11),new(11,6,11),new(-11,6,11)
            };
            for(int i=0;i<targets.Length;i++)
            {
                var t=targets[i];
                d.Geometry.Add(new MapBox{Label=$"Landing {i+1}",Layer="Jump Pads",Material=1,
                    Transform=new(){Position=new[]{t.X,t.Y,t.Z},Scale=new[]{7f,1f,7f}}});
                float sx=MathF.Sign(t.X)*5,sz=MathF.Sign(t.Z)*5;
                d.JumpPads.Add(new(){Id=Guid.NewGuid(),Label=$"Pad {i+1}",
                    Position=new[]{sx,.1f,sz},Target=new[]{t.X,t.Y+.7f,t.Z}});
            }
            d.Items.Add(new(){Id=Guid.NewGuid(),Type="HealthBig",Position=new[]{0f,.1f,0}});
        }

        private static void LargeOutdoor(MapDefinition d)
        {
            d.KillHeight=-35;d.FarClip=600;d.Preview=new(){Position=new[]{70f,55,70},Target=new[]{0f,2,0}};
            Floor(d,96,96);Walls(d,96,96,6);RadialSpawns(d,12,35);
            for(int i=0;i<12;i++)
            {
                float angle=i*MathF.Tau/12;
                float radius=i%2==0?18:28;
                d.Geometry.Add(new MapBox{Label=$"Outdoor cover {i+1}",Layer="Terrain",Material=i%3==0?1:0,
                    Transform=new(){Position=new[]{MathF.Sin(angle)*radius,1.5f,MathF.Cos(angle)*radius},
                        Scale=new[]{5f+(i%3)*2,3f+(i%2)*2,5f+(i%4)}}});
            }
            foreach(float x in new[]{-24f,0f,24f})
                d.Items.Add(new(){Id=Guid.NewGuid(),Type=x==0?"HealthBig":"UABig",Position=new[]{x,.1f,0}});
        }
    }
}
