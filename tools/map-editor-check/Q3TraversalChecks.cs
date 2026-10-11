using System;
using System.IO;
using System.Linq;
using MphRead.Mods.MapGen;
using OpenTK.Mathematics;

static class Q3TraversalChecks
{
    public static void Run(Action<bool, string> check, string root)
    {
        var push = new MapQ3Trigger { Kind = "push", SourceEntity = 1,
            Min = new[] {-1f, 0f, -1f}, Max = new[] {1f, 1f, 1f},
            Destination = new[] {10f, 10.5f, 0f}, Gravity = 20 };
        check(Q3Traversal.TryLaunch(push, out var velocity), "Q3 apex launch solves");
        check(MathF.Abs(velocity.Y - 20f / 30) < 0.000001f
            && MathF.Abs(velocity.X - 10f / 30) < 0.000001f, "Q3 apex solver measures native speed units");
        check(Q3Traversal.Contact(push, new Vector3(-3, .5f, 0), new Vector3(3, .5f, 0)),
            "Q3 feet sweep catches moving player crossing a thin trigger");
        check(!Q3Traversal.Contact(push, new Vector3(-3, 2, 0), new Vector3(3, 2, 0)),
            "Q3 feet sweep rejects crossing above trigger");
        // The 60 Hz fixed step is independent of how many presentation frames surround it.
        foreach (int visualHz in new[] {60, 120, 240})
        {
            Vector3 position = new(0, .5f, 0), speed = velocity;
            for (int frame = 0; frame < visualHz; frame++)
            {
                if (frame % (visualHz / 60) != 0) continue;
                speed.Y -= push.Gravity / 1800;
                position += speed / 2;
            }
            check(MathF.Abs(position.X - 10) < .0001f && MathF.Abs(position.Y - (10.5f - 1f / 6)) < .0001f,
                $"Q3 measured 60-step semi-implicit route at {visualHz} Hz");
        }
        foreach (var invalid in new[] {
            new MapQ3Trigger { SourceEntity = 32768 },
            new MapQ3Trigger { Gravity = float.NaN },
            new MapQ3Trigger { Min = new[] {1f}, Gravity = 20 },
            new MapQ3Trigger { Kind = "unknown", Gravity = 20 } })
            check(!Q3Traversal.Valid(invalid), "Q3 malformed trigger rejected");
        var flat = new MapQ3Trigger { SourceEntity=2, Min=push.Min, Max=push.Max,
            Destination=new[]{0f,.5f,0f}, Gravity=20 };
        check(!Q3Traversal.TryLaunch(flat, out _), "Q3 nonpositive apex does not emit invalid velocity");
        string source = Path.Combine(root, "q3-traversal-targets.bsp");
        Q3ImportChecks.WriteBsp(source,
            "{\"classname\" \"target_position\" \"targetname\" \"Pad\" \"origin\" \"0 0 160\"}\n"
            + "{\"classname\" \"target_position\" \"targetname\" \"Pad\" \"origin\" \"64 0 320\"}\n"
            + "{\"classname\" \"trigger_push\" \"target\" \"Pad\" \"model\" \"*1\"}\n"
            + "{\"classname\" \"trigger_push\" \"target\" \"pad\" \"model\" \"*1\"}\n"
            + "{\"classname\" \"misc_teleporter_dest\" \"targetname\" \"Exit\" \"origin\" \"64 32 24\" \"angles\" \"0 90 0\"}\n"
            + "{\"classname\" \"trigger_teleport\" \"target\" \"Exit\" \"model\" \"*1\"}\n");
        var extracted = Q3Traversal.Extract(Q3Bsp.Load(source, null), 16);
        check(extracted.Count == 2 && extracted[0].Destination.SequenceEqual(new[] {0f,10f,0f}),
            "Q3 first source target pairing is stable and case sensitive");
        check(extracted[1].Kind == "teleport" && extracted[1].Destination.SequenceEqual(new[] {4f,0f,-2f})
            && extracted[1].Yaw == 180, "Q3 teleport converts feet origin and orientation");
        var map = new MapDefinition { FormatVersion=3, MapId=Guid.NewGuid(), Name="Q3_TRAVERSAL_SCHEMA",
            Q3Triggers=new(){push} };
        string project=Path.Combine(root,"q3-traversal-schema.json"); map.Save(project);
        var loaded=MapDefinition.Load(project);
        check(loaded.FormatVersion==3 && loaded.Q3Triggers!.Single().Destination.SequenceEqual(push.Destination),
            "Q3 typed metadata round trips in version3 project");
        map.FormatVersion=2; map.Q3Triggers=null; map.Save(project);
        check(MapDefinition.Load(project).Q3Triggers==null, "Legacy project preserves native traversal fallback");
    }
}
