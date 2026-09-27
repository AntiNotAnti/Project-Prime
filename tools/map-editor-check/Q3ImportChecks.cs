using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MphRead.Mods.MapGen;

static class Q3ImportChecks
{
    public static void Run(Action<bool, string> check, string root)
    {
        string source = Path.Combine(root, "prime-fixture.bsp");
        string starts = "{\"classname\" \"info_player_deathmatch\" \"origin\" \"-64 -64 24\" \"angle\" \"0\"}\n"
            + "{\"classname\" \"info_player_start\" \"origin\" \"64 64 24\" \"angle\" \"90\"}\n";
        string targets = "{\"classname\" \"target_position\" \"targetname\" \"pad-target\" \"origin\" \"0 0 96\"}\n"
            + "{\"classname\" \"info_player_intermission\" \"origin\" \"100 100 128\"}\n"
            + "{\"classname\" \"trigger_push\" \"target\" \"pad-target\" \"model\" \"*1\"}\n";
        WriteBsp(source, starts + targets + "{\"classname\" \"trigger_teleport\"}\n{\"classname\" \"func_door\"}\n");
        var analysis = Q3ImportService.Analyze(source, scale:16);
        check(analysis.Spawns == 2, "Q3 preflight counts only real starts");
        check(analysis.GameplayWarnings.Any(d => d.Message.Contains("trigger_teleport"))
            && analysis.GameplayWarnings.Any(d => d.Message.Contains("func_door")), "Q3 preflight identifies unsupported traversal");
        foreach (float invalid in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.Epsilon })
        {
            bool rejected = false;
            try { Q3ImportService.Analyze(source, scale: invalid); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            check(rejected, "Q3 preflight rejects invalid scale " + invalid);
        }
        string destination = Path.Combine(root, "q3-imported");
        var imported = Q3ImportService.Import(new(source, null, "Q3_RELIABLE", destination,
            UnitsPerUnit:16, AutoHealCollision:false));
        check(imported.Succeeded, "synthetic Q3 map imports transactionally");
        var map = MapDefinition.Load(imported.ProjectPath!);
        check(map.Spawns.Count == 2 && !map.Import!.KeepSpawns, "Q3 conversion persists real editable spawns without target/camera fallbacks");
        check(map.Spawns.Select(s => s.Yaw).SequenceEqual(new[] { 90f, 180f }), "Q3 conversion preserves source facing");
        check(map.Spawns.All(s => s.Position[1] == 0), "Q3 starts use the same feet offset in conversion and runtime");
        check(map.ScaleFactor == 1, "Q3 positive fixed-point boundary selects the next scale factor");
        check(Q3ImportManifest.Load(destination)!.GameplayWarnings.Any(d => d.Message.Contains("trigger_teleport")),
            "Q3 gameplay review persists across reopening");
        var compiled = MapCompiler.Compile(map);
        check(compiled.Map != null && compiled.Validation.IsValid,
            "synthetic Q3 floor, spawns and targeted jump pad compile for Prime: " + string.Join("; ",compiled.Validation.Diagnostics));
        check(compiled.Map!.Definition.Spawns.Count == 2 && compiled.Map.Definition.JumpPads.Count == 1,
            "Q3 compile creates one jump pad without duplicating starts");
        check(compiled.Validation.Diagnostics.Any(d => d.Code == "FP-MAP-023" && d.Message.Contains("trigger_teleport")),
            "Q3 unsupported traversal appears in build diagnostics");
        var legacy = MapProjectSerializer.Clone(map); legacy.Spawns.Clear(); legacy.Import!.KeepSpawns = true;
        var legacyMap = Q3Import.Build(legacy, false);
        check(legacyMap.Definition.Spawns.Select(s => s.Yaw).SequenceEqual(map.Spawns.Select(s => s.Yaw)),
            "Q3 legacy dynamic starts match editable converted starts");
        var repeated = MapCompiler.Compile(map);
        check(repeated.Map!.Definition.Spawns.Count == 2 && repeated.Map.Definition.JumpPads.Count == 1
            && map.JumpPads.Count == 0, "Q3 repeated builds do not mutate source gameplay data");

        // Leave a valid pack whose shader ID no longer matches the source BSP.
        string texture = map.Import!.ResolveTextures()!;
        using (var writer = new BinaryWriter(File.OpenWrite(texture))) { writer.BaseStream.Position = 8; writer.Write((ushort)99); }
        var missingTexture = MapCompiler.Compile(map);
        check(missingTexture.Validation.IsValid && missingTexture.Map!.Faces.Count == compiled.Map.Faces.Count
            && missingTexture.Map.Solid.Count == compiled.Map.Solid.Count
            && missingTexture.Validation.Diagnostics.Any(d => d.Code == "FP-MAP-006" && d.Severity == MapDiagnosticSeverity.Warning),
            "Q3 incomplete texture pack retains geometry and collision with a reported placeholder");

        string ctf = Path.Combine(root, "ctf-fixture.bsp");
        WriteBsp(ctf, "{\"classname\" \"team_CTF_redplayer\" \"origin\" \"-64 -64 24\"}\n"
            + "{\"classname\" \"team_CTF_redspawn\" \"origin\" \"-64 -64 24\"}\n"
            + "{\"classname\" \"team_CTF_bluespawn\" \"origin\" \"64 64 24\" \"angles\" \"0 90 0\"}\n"
            + "{\"classname\" \"info_player_start\" \"origin\" \"NaN 0 0\"}\n" + targets);
        var ctfBsp = Q3Bsp.Load(ctf, null);
        var ctfSpawns = Q3Gameplay.Spawns(ctfBsp, 16);
        check(ctfSpawns.Count == 2 && ctfSpawns.All(s => s.Team == -1), "Q3 CTF starts become neutral Prime starts with exact duplicates removed");
        check(ctfSpawns[1].Yaw == 180, "Q3 vector angles preserve yaw");
        check(Q3Gameplay.Inspect(ctfBsp,16).Any(d => d.Message.Contains("invalid coordinates")), "Q3 malformed start is reported, not placed at world origin");
        WriteBsp(ctf, targets + "{\"classname\" \"trigger_push\" \"target\" \"missing\" \"model\" \"*0\"}\n");
        ctfBsp = Q3Bsp.Load(ctf, null);
        check(Q3Gameplay.Spawns(ctfBsp,16).Count == 0 && Q3Gameplay.Inspect(ctfBsp,16).Any(d => d.Message.Contains("cannot be imported")),
            "Q3 missing starts and broken pads remain explicit repair tasks");
        var noStarts = MapProjectSerializer.Clone(map);
        noStarts.Spawns.Clear(); noStarts.Import!.Source = ctf; noStarts.Import.KeepSpawns = true;
        var noStartResult = MapCompiler.Compile(noStarts);
        check(!noStartResult.Validation.IsValid && noStartResult.Validation.Diagnostics.Any(d => d.Code == "FP-MAP-014"),
            "Q3 legacy map without any real starts cannot pass runtime validation");
        var noMaterials = MapProjectSerializer.Clone(map); noMaterials.Import!.Textures = null;
        var noMaterialResult = MapCompiler.Compile(noMaterials);
        check(!noMaterialResult.Validation.IsValid && noMaterialResult.Validation.Diagnostics.Any(d => d.Message.Contains("no materials")),
            "Q3 missing all materials reports an error without accessing unavailable game files");
    }

    // Pure synthetic IBSP 46: one solid floor, its drawn top, and one push-trigger model.
    private static void WriteBsp(string path, string entities)
    {
        byte[][] lumps = Enumerable.Range(0,17).Select(_ => Array.Empty<byte>()).ToArray();
        void Lump(int index, Action<BinaryWriter> write)
        { using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream); write(writer); lumps[index] = stream.ToArray(); }
        lumps[0] = Encoding.ASCII.GetBytes("{\"classname\" \"worldspawn\"}\n" + entities + "\0");
        Lump(1, w => { byte[] name = new byte[64]; Encoding.ASCII.GetBytes("textures/prime/test").CopyTo(name,0); w.Write(name); w.Write(0); w.Write(1); });
        Lump(2, w => { foreach (float[] p in new[] { new[]{1f,0,0,128},new[]{-1f,0,0,128},new[]{0f,1,0,128},new[]{0f,-1,0,128},new[]{0f,0,1,0},new[]{0f,0,-1,16} }) foreach(float v in p) w.Write(v); });
        Lump(7, w => {
            foreach(float v in new[]{-128f,-128,-16,128,128,0}) w.Write(v);
            foreach(int v in new[]{0,1,0,1}) w.Write(v);
            foreach(float v in new[]{-16f,-16,0,16,16,32}) w.Write(v);
            foreach(int v in new[]{0,0,1,0}) w.Write(v);
        });
        Lump(8, w => { w.Write(0); w.Write(6); w.Write(0); });
        Lump(9, w => { for(int i=0;i<6;i++){w.Write(i);w.Write(0);} });
        Lump(10, w => {
            foreach(float[] point in new[]{new[]{-128f,-128,0},new[]{128f,-128,0},new[]{128f,128,0},new[]{-128f,128,0}})
            { foreach(float v in point)w.Write(v); for(int i=0;i<4;i++)w.Write(0f);w.Write(0f);w.Write(0f);w.Write(1f);w.Write(uint.MaxValue); }
        });
        Lump(11, w => { foreach(int v in new[]{0,1,2,0,2,3})w.Write(v); });
        Lump(13, w => {
            foreach(int v in new[]{0,-1,1,0,4,0,6,-1})w.Write(v);
            w.Write(new byte[52]); w.Write(0f);w.Write(0f);w.Write(1f);w.Write(0);w.Write(0);
        });
        using var output = new BinaryWriter(File.Create(path));
        output.Write(Encoding.ASCII.GetBytes("IBSP"));output.Write(46);
        int offset=8+17*8;
        foreach(byte[] lump in lumps){output.Write(offset);output.Write(lump.Length);offset+=lump.Length;}
        foreach(byte[] lump in lumps)output.Write(lump);
    }
}
