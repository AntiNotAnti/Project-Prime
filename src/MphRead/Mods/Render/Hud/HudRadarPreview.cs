using System;
using System.Numerics;
namespace MphRead.Mods.Render.Hud;

public static class HudRadarPreview
{
    private static readonly RadarContact[] Samples =
    {
        new(RadarContactKind.Hunter,new(-8,0,9),.5f,1,0,RadarContactRelation.Friendly,new(.3f,.6f,1,1)),
        new(RadarContactKind.Hunter,new(7,0,13),2,2,1,RadarContactRelation.Enemy,new(1,.3f,.2f,1)),
        new(RadarContactKind.Hunter,new(-13,5,1),1,3,1,RadarContactRelation.Enemy,new(.35f,.95f,.35f,1)),
        new(RadarContactKind.Hunter,new(2,-5,-12),3,4,1,RadarContactRelation.Enemy,new(.35f,.95f,.35f,1)),
        new(RadarContactKind.Hunter,new(-40,0,55),2,5,1,RadarContactRelation.Enemy,new(.35f,.95f,.35f,1)),
        new(RadarContactKind.Objective,new(3,0,8),0,12,-1,RadarContactRelation.Neutral,Vector4.One),
        new(RadarContactKind.ObjectiveBase,new(-7,0,-10),0,13,-1,RadarContactRelation.Friendly,new(.3f,.6f,1,1)),
        new(RadarContactKind.Node,new(15,0,2),0,14,-1,RadarContactRelation.Neutral,Vector4.One),
        new(RadarContactKind.PrimeHunter,new(-3,0,17),0,15,-1,RadarContactRelation.Enemy,new(1,.3f,.2f,1)),
        new(RadarContactKind.Weapon,new(-12,0,-5),0,10,-1,RadarContactRelation.Neutral,new(1,.65f,.2f,1)),
        new(RadarContactKind.Powerup,new(9,0,-6),0,11,-1,RadarContactRelation.Neutral,new(1,.4f,.8f,1))
    };
    public static ReadOnlySpan<RadarContact> Contacts => Samples;
}
