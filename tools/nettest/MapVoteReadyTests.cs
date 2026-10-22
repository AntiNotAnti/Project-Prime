using System;
using System.IO;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

internal static class MapVoteReadyTests
{
    internal static int Run()
    {
        string temp = Path.Combine(Path.GetTempPath(), "prime-vote-ready-" + Guid.NewGuid().ToString("N"));
        int checks = 0;
        void Check(bool condition, string name)
        {
            checks++;
            if (!condition) throw new InvalidOperationException("MAP VOTE READY " + name);
        }
        try
        {
            Directory.CreateDirectory(temp);
            var map = new MapDefinition { Name = "VOTE_READY_TEST" };
            string FileName(string name) => Path.Combine(temp, name);
            var output = new MapOutputSet(FileName("model"), FileName("anim"),
                FileName("collision"), FileName("entities"), FileName("nodes"));
            var missing = MapVoteReadiness.VerifyOutputs(map, output);
            Check(!missing.Ready && missing.Reason.Contains("model"), "missing model rejected");
            foreach (var file in output.Files) File.WriteAllBytes(file, new byte[] { 1, 2, 3 });
            Check(!MapVoteReadiness.VerifyOutputs(map, output).Ready, "missing manifest rejected");
            MapBuildManifest.Write(map, output);
            Check(MapVoteReadiness.VerifyOutputs(map, output).Ready, "verified exact five-output manifest accepted");
            File.WriteAllBytes(output.Collision, new byte[] { 9, 9, 9 });
            Check(!MapVoteReadiness.VerifyOutputs(map, output).Ready, "tampered collision rejects vote");
            MapBuildManifest.Write(map, output);
            File.Delete(output.Collision);
            Check(!MapVoteReadiness.VerifyOutputs(map, output).Ready, "missing collision rejects vote");
            File.WriteAllBytes(output.Collision, new byte[] { 9, 9, 9 });
            MapBuildManifest.Write(map, output);
            map.ScaleFactor++;
            Check(!MapVoteReadiness.VerifyOutputs(map, output).Ready, "source recipe changed rejects stale build");
            Console.WriteLine("MAP VOTE READY PASS " + checks + " checks");
            return 0;
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch (IOException) { }
        }
    }
}
