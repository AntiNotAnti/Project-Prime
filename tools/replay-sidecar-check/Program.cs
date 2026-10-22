using System.Text.Json;
using MphRead.Mods.Replay;
using MphRead.Mods.Network;

int passed = 0, failed = 0;
string directory = Path.Combine(Path.GetTempPath(), "prime-sidecar-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
string replay = Path.Combine(directory, "fixture.dem");
byte[] replayBytes = [0x50, 0x52, 0x49, 0x4d, 0x45];
File.WriteAllBytes(replay, replayBytes);
Guid first = Guid.Parse("11111111-1111-1111-1111-111111111111");
Guid second = Guid.Parse("22222222-2222-2222-2222-222222222222");
string created = "2026-10-13T00:00:00Z";
void Check(string name, Action assertion)
{
    try { assertion(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; Console.WriteLine($"FAIL {name}: {ex.GetType().Name}: {ex.Message}"); }
}
void Require(bool value) { if (!value) throw new InvalidOperationException("contract failed"); }
void Studio(string json) => File.WriteAllText(replay + ReplayAnnotations.Extension, json);
void Reel(string json) => File.WriteAllText(replay + ReplayReels.Extension, json);
string bookmark(Guid id, uint frame, string name) => JsonSerializer.Serialize(new ReplayBookmark(id, frame, name, DateTime.Parse(created).ToUniversalTime()));
string highlight(Guid id, uint start, uint end, string name) => JsonSerializer.Serialize(new ReplayNamedHighlight(id, start, end, name, DateTime.Parse(created).ToUniversalTime()));
string segment(Guid id, uint start, uint end, string name, ReplaySegmentCamera camera) => JsonSerializer.Serialize(new ReplayReelSegment(id, start, end, name, camera));
try
{
    Check("bookmark null siblings preserve identity and sorted frames", () => {
        Studio($"{{\"Version\":1,\"Bookmarks\":[null,{bookmark(second, 20, "second")},null,{bookmark(first, 10, "first")},null]}}");
        string original = File.ReadAllText(replay + ReplayAnnotations.Extension);
        var values = ReplayAnnotations.Bookmarks(replay);
        Require(values.Count == 2 && values[0].Id == first && values[1].Id == second && values[0].Frame == 10 && values[0].Name == "first" && values[0].CreatedUtc == DateTime.Parse(created).ToUniversalTime());
        Require(File.ReadAllText(replay + ReplayAnnotations.Extension) == original);
    });
    Check("highlight null siblings preserve identity and sorted ranges", () => {
        Studio($"{{\"Version\":1,\"Highlights\":[{highlight(second, 20, 30, "second")},null,{highlight(first, 10, 15, "first")}]}}");
        var values = ReplayAnnotations.Highlights(replay);
        Require(values.Count == 2 && values[0].Id == first && values[1].Id == second && values[0].StartFrame == 10 && values[0].EndFrame == 15 && values[0].Name == "first");
    });
    Check("reel null siblings preserve authored order and camera", () => {
        Reel($"{{\"Version\":1,\"SourceReplay\":\"other.dem\",\"Segments\":[null,{segment(second, 20, 30, "second", ReplaySegmentCamera.FirstPerson)},null,{segment(first, 10, 15, "first", ReplaySegmentCamera.Director)},null]}}");
        string original = File.ReadAllText(replay + ReplayReels.Extension);
        var values = ReplayReels.Segments(replay);
        Require(values.Count == 2 && values[0].Id == second && values[1].Id == first && values[0].Camera == ReplaySegmentCamera.FirstPerson && values[1].Camera == ReplaySegmentCamera.Director);
        Require(File.ReadAllText(replay + ReplayReels.Extension) == original);
    });
    Check("all-null arrays become empty", () => {
        Studio("{\"Version\":1,\"Bookmarks\":[null],\"Highlights\":[null]}"); Reel("{\"Version\":1,\"Segments\":[null]}");
        Require(ReplayAnnotations.Bookmarks(replay).Count == 0 && ReplayAnnotations.Highlights(replay).Count == 0 && ReplayReels.Segments(replay).Count == 0);
    });
    Check("existing invalid-record filters retain valid siblings", () => {
        Studio($"{{\"Version\":1,\"Bookmarks\":[{bookmark(second, ReplayFormatV3.MaxFrame + 1, "bad")},{bookmark(first, 0, "valid")}],\"Highlights\":[{highlight(second, 4, 4, "bad")},{highlight(first, 0, 1, "valid")}]}}");
        Reel($"{{\"Version\":1,\"Segments\":[{segment(second, 5, 4, "bad", ReplaySegmentCamera.Current)},{segment(first, 0, 1, "valid", ReplaySegmentCamera.FirstPerson)}]}}");
        Require(ReplayAnnotations.Bookmarks(replay).Single().Id == first && ReplayAnnotations.Highlights(replay).Single().Id == first && ReplayReels.Segments(replay).Single().Id == first);
    });
    foreach (string json in new[] { "{\"Version\":1,\"Bookmarks\":null,\"Highlights\":null,\"Segments\":null}", "{\"Version\":2}", "null", "{" })
        Check("existing empty/future/malformed fallback: " + json, () => { Studio(json); Reel(json); Require(ReplayAnnotations.Bookmarks(replay).Count == 0 && ReplayAnnotations.Highlights(replay).Count == 0 && ReplayReels.Segments(replay).Count == 0); });
    Check("existing organization normalization tolerates null labels", () => {
        Studio("{\"Version\":1,\"Tags\":[null,\"tag\",\"TAG\"],\"Collections\":[null,\"collection\"]}");
        Require(ReplayAnnotations.Tags(replay).SequenceEqual(new[] { "tag" }) && ReplayAnnotations.Collections(replay).SequenceEqual(new[] { "collection" }));
    });
    Check("editing a mixed sidecar keeps schema and source identity", () => {
        Studio($"{{\"Version\":1,\"Bookmarks\":[null,{bookmark(first, 10, "first")}]}}");
        ReplayAnnotations.RemoveBookmark(replay, second);
        Require(ReplayAnnotations.Bookmarks(replay).Single().Id == first);
        Reel($"{{\"Version\":1,\"Segments\":[null,{segment(first, 10, 15, "first", ReplaySegmentCamera.FirstPerson)},{segment(second, 20, 25, "second", ReplaySegmentCamera.Chase)}]}}");
        ReplayReels.Move(replay, second, -1);
        var values = ReplayReels.Segments(replay);
        using var doc = JsonDocument.Parse(File.ReadAllText(replay + ReplayReels.Extension));
        Require(values[0].Id == second && values[1].Id == first && doc.RootElement.GetProperty("Version").GetInt32() == 1 && doc.RootElement.GetProperty("SourceReplay").GetString() == replay);
    });
    Check("underlying recording bytes preserved", () => Require(File.ReadAllBytes(replay).SequenceEqual(replayBytes)));
}
finally { Directory.Delete(directory, recursive: true); }
Console.WriteLine($"{passed} PASS / {failed} FAIL");
return failed == 0 ? 0 : 1;
