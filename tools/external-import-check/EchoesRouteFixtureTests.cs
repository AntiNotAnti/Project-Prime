using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MphRead.Mods.Import;
using MphRead.Mods.MapGen;

internal static class EchoesRouteFixtureTests
{
    public static void Run()
    {
        Check(EchoesTunedCannons.ArenaKeys.Sum(x=>EchoesTunedCannons.Landings(x).Count)==12,
            "all twelve pinned source arrival points");
        var collision = new MapMesh
        {
            Id=Guid.NewGuid(), Solid=true, CollisionOnly=true,
            Vertices=new List<float[]> {
                new[]{0f,0f,0f},new[]{0f,0f,6f},new[]{6f,0f,0f},
                new[]{12f,0f,0f},new[]{12f,0f,6f},new[]{18f,0f,0f}
            },
            Faces=new List<int[]> {new[]{0,1,2},new[]{3,4,5}}
        };
        Check(EchoesTunedCannons.TryCreateLandingLink(new[]{collision},
            new[]{2f,1f,2f},new[]{14f,1f,2f},out var link) &&
            link!=null && !link.Bidirectional &&
            link.Kind==MapNavigationLinkKind.JumpPad &&
            link.ToNodeKind==MapNavigationAnchorKind.Aerial &&
            link.From[1]==0 && link.To[1]==0,
            "grounded directed link with special landing node");
        var render = new MapMesh { CollisionOnly=false, Solid=false,
            Vertices=collision.Vertices, Faces=collision.Faces };
        Check(!EchoesTunedCannons.TryCreateLandingLink(new[]{render},
            new[]{2f,1f,2f},new[]{14f,1f,2f},out _),
            "source visuals must not fabricate traversable ground");
        Check(!EchoesTunedCannons.TryCreateLandingLink(new[]{collision},
            new[]{2f,1f,2f},new[]{2f,1f,2f},out _),
            "refuse self-loop landing");
        Check(!EchoesTunedCannons.TryCreateLandingLink(new[]{collision},
            new[]{2f,1f,2f},new[]{100f,1f,100f},out _),
            "refuse missing landing floor");
    }
    private static void Check(bool okay,string why)
    {
        if(!okay)throw new InvalidOperationException("Echoes route fixture: "+why);
    }
}
