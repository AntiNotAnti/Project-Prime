using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;
using MphRead.Mods.StudioReplay;

internal static partial class Program
{
    private sealed record ReplayProbeResult(int Checks, string[] Recordings, string[] PrivateRoots, int ProcessId);

    private static async Task CheckReplayProcessesAsync(string directory, string assets)
    {
        string fixtures = Path.Combine(directory, "replay"); Directory.CreateDirectory(fixtures);
        string repo = FindRepository();
        foreach (string source in new[] { Path.Combine(repo, "artifacts", "studio-baseline", "fcf311c", "reference-hashes.ppdemo"),
            Path.Combine(repo, "artifacts", "fps-audit", "followup", "v134-complete", "v134-source.ppdemo") })
            File.Copy(source, Path.Combine(fixtures, Path.GetFileName(source)));
        var before = Directory.GetFiles(fixtures).ToDictionary(path => Path.GetFileName(path)!, path => SHA256.HashData(File.ReadAllBytes(path)));
        using Process worker = StartChild(["--replay-probe", Path.GetFullPath(assets), fixtures]);
        string output, errors;
        try
        {
            string[] lines = await Task.WhenAll(worker.StandardOutput.ReadToEndAsync(), worker.StandardError.ReadToEndAsync()).WaitAsync(TimeSpan.FromSeconds(90));
            output = lines[0]; errors = lines[1];
            await worker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch { if (!worker.HasExited) { worker.Kill(entireProcessTree:true); await worker.WaitForExitAsync(); } throw; }
        Check(worker.ExitCode == 0, "standalone replay simulation worker: " + errors + output);
        string result = output.Split('\n').Single(line => line.StartsWith("REPLAY ", StringComparison.Ordinal));
        ReplayProbeResult probe = JsonSerializer.Deserialize<ReplayProbeResult>(result[7..])!;
        Check(probe.Checks > 30 && probe.ProcessId != Environment.ProcessId && probe.PrivateRoots.Distinct().Count() == 2,
            "replay acceptance uses a separate no-window worker and two distinct historical package resource roots");
        Check(before.All(pair => pair.Value.SequenceEqual(SHA256.HashData(File.ReadAllBytes(Path.Combine(fixtures, pair.Key!))))),
            "historical source recordings remain byte-identical after seek, playback and authoring sidecars");
        Console.WriteLine("Standalone replay worker passed: "+probe.Checks+" assertions, two historical recordings and two isolated custom-map versions.");
    }

    private static int RunReplayProbe(string[] args)
    {
        try
        {
            if (args.Length != 3 || !File.Exists(args[1]) || !Directory.Exists(args[2])) return 2;
            string root = args[2];
            Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA", Path.Combine(root, "game-user"));
            Headless.Enter(); Paths.UpdatePaths(args[1]); Paths.ChooseMphPath();
            string stockRoot = Paths.FileSystem;
            CustomRooms.UserMapDirectory = Path.Combine(root, "installed");
            CustomRooms.MapDirectory = Path.Combine(root, "packages");
            string[] historicalRecordings = Directory.GetFiles(root, "*.ppdemo");
            (string first, string second) = CreateHistoricalReplayFixtures(root);
            string[] privateRoots;
            using (var one = new StudioReplayPlayer(first, Path.Combine(root, "cache-one"), [CustomRooms.MapDirectory]))
            using (var two = new StudioReplayPlayer(second, Path.Combine(root, "cache-two"), [CustomRooms.MapDirectory]))
            {
                one.OnGraphicsInitialize(256, 192); two.OnGraphicsInitialize(256, 192);
                WaitReplayReady(one); WaitReplayReady(two);
                Check(one.Status.Room == two.Status.Room && one.Status.CustomMapRoot is not null && two.Status.CustomMapRoot is not null,
                    "historical versions resolve their common room through private resources");
                privateRoots = [one.Status.CustomMapRoot!, two.Status.CustomMapRoot!];
                Check(privateRoots[0] != privateRoots[1] && privateRoots.All(path => path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)),
                    "same map identity versions use separate absolute private generated-resource roots");
                var files = privateRoots.Select(HashDirectory).ToArray();
                Check(!files[0].Values.Order().SequenceEqual(files[1].Values.Order()), "historical versions retain different canonical generated geometry");
                one.Seek(15); WaitReplayReady(one); two.Seek(25); WaitReplayReady(two);
                string bundle=Path.Combine(root,"historical-portable.ppreplay");
                one.ExportPortableAsync(bundle).GetAwaiter().GetResult();
                using(var archive=ZipFile.OpenRead(bundle))
                {
                    using var manifestStream=archive.GetEntry("manifest.json")!.Open();using var manifest=JsonDocument.Parse(manifestStream);
                    using var mapStream=archive.GetEntry("map.ppmap")!.Open();byte[] mapBytes;
                    using(var bytes=new MemoryStream()){mapStream.CopyTo(bytes);mapBytes=bytes.ToArray();}
                    using var replayStream=archive.GetEntry("replay.ppdemo")!.Open();byte[] replayBytes;
                    using(var bytes=new MemoryStream()){replayStream.CopyTo(bytes);replayBytes=bytes.ToArray();}
                    Check(Convert.ToHexString(SHA256.HashData(mapBytes)).Equals(manifest.RootElement.GetProperty("RequiredPackageHash").GetString(),StringComparison.OrdinalIgnoreCase)
                        &&replayBytes.SequenceEqual(File.ReadAllBytes(first)),"portable replay carries exact historical package hash and immutable recording bytes");
                }
                var imported=StudioReplayPlayer.ImportPortableAsync(bundle,Path.Combine(root,"imported-portable")).GetAwaiter().GetResult();
                using(var portable=new StudioReplayPlayer(imported.ReplayPath,Path.Combine(root,"imported-cache"),imported.PackageDirectories))
                {
                    portable.OnGraphicsInitialize(256,192);WaitReplayReady(portable);portable.Seek(15);WaitReplayReady(portable);
                    Check(SameReplayWorld(one.Snapshot(),portable.Snapshot()),"portable historical replay reopens exact private scene without installing into game library");
                }
                StudioReplayWorldSnapshot secondBefore = two.Snapshot();
                one.Dispose();
                two.Advance(TimeSpan.Zero);
                Check(SameReplayWorld(secondBefore, two.Snapshot()) && files[1].All(pair => HashDirectory(privateRoots[1]).GetValueOrDefault(pair.Key) == pair.Value),
                    "closing first historical version leaves second scene and generated resources identical");
            }
            Check(Paths.FileSystem == stockRoot && !NetSession.Active && !MphRead.Mods.Launcher.Gui.Shell.Active,
                "standalone replay ownership leaves asset roots unchanged and constructs no game shell or network session");
            Check(!Directory.Exists(CustomRooms.UserMapDirectory), "private replay preparation does not install historical packages into game library");
            Console.WriteLine("Historical custom package resource isolation passed.");
            foreach (string path in historicalRecordings) CheckReplayRecording(path, root);
            Console.WriteLine("REPLAY " + JsonSerializer.Serialize(new ReplayProbeResult(_checks,
                Directory.GetFiles(root, "*.ppdemo").Select(Path.GetFileName).ToArray()!, privateRoots, Environment.ProcessId)));
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void CheckReplayRecording(string path, string root)
    {
        byte[] recording = File.ReadAllBytes(path);
        using var player = new StudioReplayPlayer(path, Path.Combine(root, "cache-" + Path.GetFileNameWithoutExtension(path)));
        player.OnGraphicsInitialize(256, 192); WaitReplayReady(player);
        Check(player.Status.Ready && player.Status.DurationFrames > 100, "historical recording prepares a passive standalone scene");
        uint duration = player.Status.DurationFrames;
        uint[] targets = new uint[] { 0, 1, 30, 60, 120, Math.Min(300u, duration), Math.Min(600u, duration), duration }.Distinct().Order().ToArray();
        var reference = new Dictionary<uint, StudioReplayWorldSnapshot>();
        foreach (uint target in targets)
        {
            while (player.Status.Frame < target)
            { player.StepForward(); player.Advance(TimeSpan.FromSeconds(1d / 60)); }
            Check(player.Status.Frame == target, "linear replay reaches exact frame " + target);
            reference[target] = player.Snapshot();
        }
        foreach (uint target in targets.Reverse().Concat(targets))
        {
            player.Seek(target); WaitReplayReady(player);
            var actual = player.Snapshot();
            if (!SameReplayWorld(reference[target], actual)) Console.Error.WriteLine("Seek mismatch " + Path.GetFileName(path) + " frame " + target
                + ": expected=" + JsonSerializer.Serialize(reference[target]) + ", actual=" + JsonSerializer.Serialize(actual) + ", performance=" + JsonSerializer.Serialize(player.Performance));
            Check(SameReplayWorld(reference[target], actual), "standalone seek restores canonical gameplay/presentation/full graph at " + target);
        }
        uint checkpoint = targets.Contains(120u) ? 120u : targets[1];
        foreach (float rate in new[] { .25f, .5f, 1f, 2f, 4f })
        {
            player.Seek(0); WaitReplayReady(player); player.SetRate(rate); player.TogglePause();
            var clock = Stopwatch.StartNew();
            while (player.Status.Frame < checkpoint && clock.Elapsed < TimeSpan.FromSeconds(10)) player.Advance(TimeSpan.FromSeconds(1d / 60));
            player.Pause(); player.Advance(TimeSpan.Zero);
            Check(player.Status.Frame == checkpoint && SameReplayWorld(reference[checkpoint], player.Snapshot()),
                "standalone playback rate " + rate + " preserves authoritative fixed-step state");
        }
        StudioReplayWorldSnapshot before = player.Snapshot();
        player.AddBookmark(checkpoint, "Acceptance bookmark"); player.AddHighlight(30, 60, "Acceptance annotation");
        player.AddReel(30, 60, "Acceptance reel"); player.SetOrganization(["acceptance"], ["historical"]);
        Check(player.PutCameraKey(new(checkpoint, new(1, 5, 8), System.Numerics.Quaternion.Identity)), "canonical camera key sidecar edits save");
        Check(player.Markers.Count >= 3 && player.CameraKeys.Any(key => key.Frame == checkpoint)
            && SameReplayWorld(before, player.Snapshot()) && recording.SequenceEqual(File.ReadAllBytes(path)),
            "annotations, camera keys and reels preserve recording bytes and authoritative world");
        Check(File.ReadAllBytes(path+".camera")[4] == 3, "ordinary camera keys retain canonical version 3 sidecar format");
        var left = new StudioReplayCameraKey(20, new(-2,5,8), System.Numerics.Quaternion.Identity,
            Interpolation:StudioReplayCameraInterpolation.Bezier, Ease:StudioReplayCameraEase.None,
            OutgoingTangent:new(0,12,0), FovOutgoingTangent:20, RollOutgoingTangent:15);
        var right = new StudioReplayCameraKey(80, new(2,5,8), System.Numerics.Quaternion.Identity,
            Ease:StudioReplayCameraEase.None, IncomingTangent:new(0,12,0), FovIncomingTangent:20, RollIncomingTangent:15);
        Check(player.PutCameraKey(left) && player.PutCameraKey(right), "standalone camera authoring saves explicit Bezier handles");
        Check(player.SampleCamera(20)!.Position == left.Position && player.SampleCamera(80)!.Position == right.Position
            && player.SampleCamera(50)!.Position.Y > 10 && player.SampleCamera(50)!.Fov > 78,
            "Bezier camera curve is continuous at endpoints and differs from straight position/FOV interpolation");
        var lengths = Enumerable.Range(1,20).Select(index => System.Numerics.Vector3.Distance(
            player.SampleCamera(20+(index-1)*3,constantSpeed:true)!.Position, player.SampleCamera(20+index*3,constantSpeed:true)!.Position)).ToArray();
        Check(lengths.Max()/lengths.Min() < 1.2, "Bezier constant-speed sampling bounds travel-distance scatter");
        Check(File.ReadAllBytes(path+".camera")[4] == 4, "explicit camera handles select canonical version 4 format");
        using (var roundTrip = new StudioReplayPlayer(path,Path.Combine(root,"camera-roundtrip")))
            Check(roundTrip.CameraKeys.SequenceEqual(player.CameraKeys), "camera version 4 round trip preserves exact key/tangent values");
        player.TransformCameraKeys([20,80],new(3,0,0),System.Numerics.Quaternion.Identity);
        Check(player.CameraKeys.Single(key=>key.Frame==20).Position == left.Position+new System.Numerics.Vector3(3,0,0)
            && SameReplayWorld(before,player.Snapshot()) && recording.SequenceEqual(File.ReadAllBytes(path)),
            "multiple camera-key transforms preserve recording and authoritative gameplay graph");
        byte[] validCamera = File.ReadAllBytes(path+".camera"); byte[] camera = (byte[])validCamera.Clone(); camera[25]^=1; File.WriteAllBytes(path+".camera",camera);
        using (var corrupt = new StudioReplayPlayer(path,Path.Combine(root,"camera-corrupt")))
            Check(corrupt.CameraKeys.Count==0, "camera sidecar checksum rejects corrupted authored track without changing recording");
        File.WriteAllBytes(path+".camera",validCamera);
        CheckReplayEvidence(player,path,root,before);
        StudioReplayAnalysis analysis=player.AnalyzeAsync().GetAwaiter().GetResult();
        Check(analysis.SourceHash.Equals(Convert.ToHexString(SHA256.HashData(recording)),StringComparison.OrdinalIgnoreCase)
            &&analysis.DurationFrames==duration&&analysis.Samples.All(sample=>float.IsFinite(sample.Position.X)
                &&float.IsFinite(sample.Position.Y)&&float.IsFinite(sample.Position.Z)&&float.IsFinite(sample.Weight))
            &&SameReplayWorld(before,player.Snapshot())&&!NetSession.Active&&!MphRead.Mods.Launcher.Gui.Shell.Active,
            "detached recorded-packet analytics preserves source identity and all owner world fields without game session");
        using(var cancelled=new CancellationTokenSource())
        {
            cancelled.Cancel();bool rejected=false;
            try{player.AnalyzeAsync(cancelled.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){rejected=true;}
            Check(rejected&&SameReplayWorld(before,player.Snapshot()),"analytics cancellation leaves owner replay graph unchanged");
        }
        var originalSidecars=Directory.GetFiles(Path.GetDirectoryName(path)!,Path.GetFileName(path)+".*")
            .ToDictionary(file=>file,File.ReadAllBytes);
        string clip=Path.Combine(root,Path.GetFileNameWithoutExtension(path)+"-saved.ppclip");
        player.SaveClipProjectAsync(30,80,clip).GetAwaiter().GetResult();
        string sharedClipCache=Path.Combine(root,"clip-cache-"+Path.GetFileNameWithoutExtension(path));
        using(var clipPlayer=new StudioReplayPlayer(clip,sharedClipCache))
        {
            clipPlayer.OnGraphicsInitialize(256,192);WaitReplayReady(clipPlayer);
            Check(clipPlayer.Status.DurationFrames==50&&clipPlayer.CameraKeys.Any(key=>key.Frame==50)
                &&recording.SequenceEqual(File.ReadAllBytes(path)),"canonical Save As clip retains selected range and retimed camera keys without rewriting original replay");
            clipPlayer.Seek(17);clipPlayer.SetRate(2);clipPlayer.SetRange(5,45);WaitReplayReady(clipPlayer);
            StudioReplayWorldSnapshot clipBefore=clipPlayer.Snapshot();
            clipPlayer.OnGraphicsDeinitialize(false);clipPlayer.OnGraphicsInitialize(256,192);WaitReplayReady(clipPlayer);
            Check(SameReplayWorld(clipBefore,clipPlayer.Snapshot())&&clipPlayer.Status is {Frame:17,Rate:2,State:"Paused",ClipIn:5,ClipOut:45},
                "clip viewport recreation retains nonzero exact graph, rate, pause and marked range");
            using(var overlapping=new StudioReplayPlayer(clip,sharedClipCache))
            {
                overlapping.OnGraphicsInitialize(256,192);WaitReplayReady(overlapping);overlapping.Seek(23);WaitReplayReady(overlapping);
                var overlapBefore=overlapping.Snapshot();clipPlayer.Dispose();overlapping.Advance(TimeSpan.Zero);
                Check(SameReplayWorld(overlapBefore,overlapping.Snapshot()),"overlapping clip players sharing cache retain independent source leases and passive world");
            }
        }
        using(var reopened=new StudioReplayPlayer(clip,sharedClipCache))
        {reopened.OnGraphicsInitialize(256,192);WaitReplayReady(reopened);reopened.Seek(17);WaitReplayReady(reopened);
            Check(reopened.Status.Frame==17&&reopened.Status.DurationFrames==50,"clip can reopen sequentially with exact same private cache root");}
        Check(originalSidecars.All(pair=>pair.Value.SequenceEqual(File.ReadAllBytes(pair.Key)))&&recording.SequenceEqual(File.ReadAllBytes(path)),
            "clip Save As, overlap and recreation leave original recording and authored sidecars byte-identical");
        var savedClipFiles=Directory.GetFiles(root,Path.GetFileName(clip)+"*").ToDictionary(file=>file,File.ReadAllBytes);
        using(var cancelled=new CancellationTokenSource())
        {
            cancelled.Cancel();bool rejected=false;
            try{player.SaveClipProjectAsync(40,70,clip,cancelled.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){rejected=true;}
            Check(rejected&&savedClipFiles.All(pair=>pair.Value.SequenceEqual(File.ReadAllBytes(pair.Key))),
                "cancelled clip Save As preserves existing descriptor and every authored sidecar atomically");
        }
        string detachedSource=Path.Combine(root,"detached-analytics-"+Path.GetFileName(path));File.WriteAllBytes(detachedSource,recording);
        Task<StudioReplayAnalysis> detached;
        using(var analyticsOwner=new StudioReplayPlayer(detachedSource,Path.Combine(root,"detached-analytics-cache-"+Path.GetFileNameWithoutExtension(path))))
        {analyticsOwner.OnGraphicsInitialize(256,192);WaitReplayReady(analyticsOwner);detached=analyticsOwner.AnalyzeAsync();}
        File.Move(detachedSource,detachedSource+".renamed");var detachedResult=detached.GetAwaiter().GetResult();
        Check(detachedResult.SourceHash.Equals(Convert.ToHexString(SHA256.HashData(recording)),StringComparison.OrdinalIgnoreCase)
            &&detachedResult.DurationFrames==duration&&!NetSession.Active&&!MphRead.Mods.Launcher.Gui.Shell.Active,
            "detached recorded-packet analytics completes with immutable source after owner disposal and original-path rename");
        Check(player.Performance.SeekSimulationSteps <= PassiveReplayPlayer.MaximumStepsPerUpdate && player.Status.CheckpointBytes <= 64L * 1024 * 1024,
            "standalone checkpoint ownership remains bounded");
        player.OnGraphicsDeinitialize(false);
        player.OnGraphicsInitialize(256, 192); WaitReplayReady(player);
        Check(SameReplayWorld(before, player.Snapshot()) && player.Status.Rate == 4 && player.Status.State == "Paused",
            "passive viewport detach/recreation restores prior scene and transport preferences without game shell");
    }

    private static void CheckReplayEvidence(StudioReplayPlayer player,string path,string root,StudioReplayWorldSnapshot before)
    {
        var evidence=player.Evidence(new(256,192));string report=Path.Combine(root,Path.GetFileNameWithoutExtension(path)+"-evidence.json");
        StudioReplayPlayer.SaveEvidenceAsync(evidence,report).GetAwaiter().GetResult();
        var loaded=StudioReplayPlayer.LoadEvidenceAsync(report).GetAwaiter().GetResult();var comparison=StudioReplayPlayer.CompareEvidence(evidence,loaded);
        Check(comparison is {SameSource:true,ProjectilesEqual:true,AnimationsEqual:true,CameraEqual:true,ResolvedShotsEqual:true}
            &&comparison.World is {GameplayEqual:true,PresentationEqual:true,FullGraphEqual:true}&&SameReplayWorld(before,player.Snapshot()),
            "actual replay evidence round trip retains world, animations, projectiles, camera and resolved combat without owner mutation");
        string json=File.ReadAllText(report);var rootNode=JsonNode.Parse(json)!.AsObject();
        Action<JsonObject>[] malformed=[
            node=>node["World"]=null,node=>node["Camera"]=null,node=>node["Camera"]!["Descriptor"]=null,
            node=>node["Camera"]!["View"]=null,node=>node["World"]!["Players"]=null,
            node=>node["Projectiles"]=null,node=>node["Animations"]=null,node=>node["ResolvedShots"]=null,
            node=>node["World"]!["Players"]=new JsonArray((JsonNode?)null),
            node=>node["Projectiles"]=new JsonArray((JsonNode?)null),node=>node["Animations"]=new JsonArray((JsonNode?)null),
            node=>node["ResolvedShots"]=new JsonArray((JsonNode?)null),node=>node["SourceHash"]="not-a-hash",
            node=>node["World"]!["FullGraphHash"]="00",node=>node["Camera"]!["View"]![0]=1e100,
            node=>node["Camera"]!["Descriptor"]!["Fov"]=1e100,
            node=>node["Camera"]!["View"]=new JsonArray(Enumerable.Repeat(0,17).Select(value=>(JsonNode?)JsonValue.Create(value)).ToArray()),
            node=>node["Animations"]=new JsonArray(Enumerable.Range(0,32769).Select(_=>JsonSerializer.SerializeToNode(
                new StudioReplayAnimation(0,"Player",0,0,0,0,0,0,0))).ToArray())];
        for(int index=0;index<malformed.Length;index++)
        {
            var mutated=rootNode.DeepClone().AsObject();malformed[index](mutated);File.WriteAllText(report,mutated.ToJsonString());
            bool rejected=false;try{StudioReplayPlayer.LoadEvidenceAsync(report).GetAwaiter().GetResult();}catch(InvalidDataException){rejected=true;}
            Check(rejected,"malformed nested replay evidence is rejected with a data error "+index);
        }
        File.WriteAllText(report,"{");bool invalidJson=false;
        try{StudioReplayPlayer.LoadEvidenceAsync(report).GetAwaiter().GetResult();}catch(InvalidDataException){invalidJson=true;}
        Check(invalidJson,"malformed evidence JSON is normalized to a data error");File.WriteAllText(report,json);
    }

    private static void WaitReplayReady(StudioReplayPlayer player)
    {
        var timer = Stopwatch.StartNew();
        do
        {
            player.Advance(TimeSpan.Zero);
            if (player.Status.Ready && !player.Status.Preparing) return;
            if (player.Status.State == "Error") throw new InvalidOperationException(player.Status.Error);
            Thread.Sleep(1);
        } while (timer.Elapsed < TimeSpan.FromSeconds(30));
        throw new TimeoutException("Passive replay did not reach ready: " + JsonSerializer.Serialize(player.Status));
    }

    private static bool SameReplayWorld(StudioReplayWorldSnapshot first, StudioReplayWorldSnapshot second)
        => first.Frame == second.Frame && first.GameplayHash == second.GameplayHash && first.PresentationHash == second.PresentationHash
            && first.FullGraphHash == second.FullGraphHash && first.Players.SequenceEqual(second.Players);

    private static (string First, string Second) CreateHistoricalReplayFixtures(string root)
    {
        Directory.CreateDirectory(CustomRooms.MapDirectory);
        MapDefinition definition = MapTemplates.Create("STUDIO_HISTORICAL", true).Definition;
        definition.MapId = Guid.NewGuid();
        string first = Make(1); definition.Geometry[0].Transform.Scale[0] += 3;
        string second = Make(2); return (first, second);

        string Make(int version)
        {
            definition.Version = version.ToString();
            string package = MapPackageBuilder.Build(definition, Path.Combine(CustomRooms.MapDirectory, "historical-" + version + ".ppmap"));
            MapContentIdentity identity = MapContentIdentity.FromPackage(package);
            var match = new MatchStatePacket { Mode = (byte)GameMode.Battle, RoomKey = definition.Name, NextRoomKey = "",
                MatchId = 1, AuthorityEpoch = 1, PlayerCount = 1, Flags = MatchStatePacket.FlagInProgress, PointGoal = 7 };
            var session = new SessionStatePacket { Phase = SessionPhase.InMatch, Policy = ServerSessionPolicy.Continuous,
                OwnerSlot = 0, MaxPlayers = 8, Revision = 1, MatchId = 1, AuthorityEpoch = 1,
                StartStage = StartStage.InMatch, StartGeneration = 1, ExpectedParticipants = 1, LoadedParticipants = 1, WorldReadyParticipants = 1,
                WorldProfile = new MatchWorldProfile(2, ResourceSpawnProfile.Low), Match = new MatchDefinition { RoomKey = definition.Name,
                    Mode = GameMode.Battle, Format = MatchFormat.FreeForAll, PointGoal = 7,
                    MapIdentity = new(identity.MapId, identity.ContentHash, identity.PackageHash, NetworkMapFlags.Custom) } };
            byte[] matchPacket = new byte[1 + MatchStatePacket.Size]; matchPacket[0] = (byte)PacketType.MatchState; match.Write(matchPacket.AsSpan(1));
            byte[] sessionPacket = new byte[1 + SessionStatePacket.Size]; sessionPacket[0] = (byte)PacketType.SessionState; session.Write(sessionPacket.AsSpan(1));
            string path = Path.Combine(root, "historical-custom-" + version + ".ppdemo");
            using var writer = new ReplayWriterV3(path, new ReplayMetadata { RoomKey = definition.Name, Mode = GameMode.Battle,
                Players = [new ReplayPlayerInfo(0, (byte)Hunter.Samus, 0, "Historical")], Bootstrap = new() { Packets = [matchPacket, sessionPacket] } });
            writer.WriteRecord(0, [(byte)PacketType.Ping]); writer.WriteRecord(30, [(byte)PacketType.Ping]);
            return path;
        }
    }

    private static Dictionary<string,string> HashDirectory(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(root, path), path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
    private static string FindRepository()
    {
        for (DirectoryInfo? directory = new(Environment.CurrentDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src", "MphRead", "MphRead.csproj"))) return directory.FullName;
        throw new DirectoryNotFoundException("Run replay acceptance from the repository or supply existing fixtures.");
    }
}
