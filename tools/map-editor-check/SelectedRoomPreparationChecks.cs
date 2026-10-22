using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using MphRead.Mods.MapGen;

static class SelectedRoomPreparationChecks
{
    public static void Run(Action<bool, string> check)
    {
        MapDefinition first = CustomRooms.Definitions.Single(d => d.Name == "RUNTIME LEASE CHECK");
        MapDefinition second = CustomRooms.Definitions.Single(d => d.Name == "ANIMATED MATERIAL CHECK");
        string previousNamespace = CustomRooms.RuntimeNamespace;
        double selectedMilliseconds = 0, warmMilliseconds = 0, nativeMilliseconds = 0, allMilliseconds = 0;
        try
        {
            CustomRooms.RuntimeNamespace = "selected-room-check-" + Guid.NewGuid().ToString("N");
            check(CustomRooms.NeedsGenerating(first) && CustomRooms.NeedsGenerating(second),
                "two isolated selected-room fixtures start without generated outputs");

            var clock = Stopwatch.StartNew();
            CustomRooms.PrepareRoom(first.Name);
            selectedMilliseconds = clock.Elapsed.TotalMilliseconds;
            check(!CustomRooms.NeedsGenerating(first) && CustomRooms.NeedsGenerating(second),
                "selected-room preparation builds only the requested custom room");

            var afterFirstBuild = Snapshot(first, second);
            clock.Restart();
            CustomRooms.PrepareRoom(first.Name);
            warmMilliseconds = clock.Elapsed.TotalMilliseconds;
            check(afterFirstBuild.SequenceEqual(Snapshot(first, second)),
                "repeating selected-room preparation preserves output timestamps and bytes");

            check(MphRead.Metadata.GetRoomByName("MP3 PROVING GROUND").Item1 != null,
                "native-room no-op fixture names a registered native room");
            clock.Restart();
            CustomRooms.PrepareRoom("MP3 PROVING GROUND");
            nativeMilliseconds = clock.Elapsed.TotalMilliseconds;
            check(afterFirstBuild.SequenceEqual(Snapshot(first, second))
                && CustomRooms.NeedsGenerating(second),
                "native-room preparation creates no custom map outputs");

            string originalTextureSource = second.TextureSource;
            string?[] originalTextures = second.Materials.Select(material => material.Texture).ToArray();
            byte[] sourceBeforeFailure = File.ReadAllBytes(second.SourcePath);
            MphRead.ProgramException? failure = null;
            try
            {
                second.TextureSource = "NO SUCH SELECTED ROOM SOURCE";
                foreach (var material in second.Materials) material.Texture = null;
                CustomRooms.PrepareRoom(second.Name);
            }
            catch (MphRead.ProgramException ex) { failure = ex; }
            finally
            {
                second.TextureSource = originalTextureSource;
                for (int i = 0; i < second.Materials.Count; i++)
                    second.Materials[i].Texture = originalTextures[i];
            }
            check(failure != null
                && failure.Message.Contains(second.Name, StringComparison.Ordinal)
                && failure.Message.Contains("could not be built", StringComparison.Ordinal)
                && failure.Message.Contains("no room to load", StringComparison.Ordinal)
                && CustomRooms.NeedsGenerating(second)
                && sourceBeforeFailure.SequenceEqual(File.ReadAllBytes(second.SourcePath))
                && afterFirstBuild.SequenceEqual(Snapshot(first, second)),
                "a failed selected-room build reports a readable error without changing source or outputs");

            CustomRooms.RuntimeNamespace = "all-room-check-" + Guid.NewGuid().ToString("N");
            check(CustomRooms.NeedsGenerating(first) && CustomRooms.NeedsGenerating(second),
                "all-room comparison starts with both fixture outputs missing");
            clock.Restart();
            CustomRooms.GenerateMissing();
            allMilliseconds = clock.Elapsed.TotalMilliseconds;
            check(!CustomRooms.NeedsGenerating(first) && !CustomRooms.NeedsGenerating(second),
                "all-room generation prepares both isolated fixtures");

            Console.WriteLine($"[map-preparation] selected=1/2 cold={selectedMilliseconds:0.0} ms, "
                + $"repeat={warmMilliseconds:0.0} ms, native=0/2 in {nativeMilliseconds:0.0} ms; "
                + $"all=2/2 in {allMilliseconds:0.0} ms (synthetic map-editor fixtures)");
        }
        finally { CustomRooms.RuntimeNamespace = previousNamespace; }
    }

    private static (string Path, bool Exists, long Length, long LastWriteTicks)[] Snapshot(
        params MapDefinition[] definitions)
        => definitions.SelectMany(definition => CustomRooms.OutputsFor(definition).Files)
            .Select(path =>
            {
                if (!File.Exists(path)) return (path, false, 0L, 0L);
                var info = new FileInfo(path);
                return (path, true, info.Length, info.LastWriteTimeUtc.Ticks);
            })
            .ToArray();
}
