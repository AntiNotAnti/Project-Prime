using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MphRead.Mods.Campaign;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Core;
using MphRead.Text;
using Clock=MphRead.Mods.Campaign.CampaignTimeRecords;

namespace MphRead.NetTest;

// Own numeric observations and synthetic saves. The empty invented ScanLog
// cache below lets the real StorySave constructor execute without ROM IO.
// These tests do not claim to simulate Renderer eligibility or movie playback.
internal static class CampaignTimingTests
{
    private static int checks;
    internal static int Run()
    {
        checks=0;string previousDirectory=Directory.GetCurrentDirectory();byte previousSlot=Menu.SaveSlot;
        SaveWhen previousRequest=Menu.NeededSave;CultureInfo priorCulture=CultureInfo.CurrentCulture,priorUi=CultureInfo.CurrentUICulture;
        string root=Path.Combine(Path.GetTempPath(),"prime-campaign-time-"+Guid.NewGuid().ToString("N"));
        var cache=(IDictionary)typeof(Strings).GetField("_cache",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
        object language=Scene.Language;bool hadCache=cache.Contains(language);object? priorCache=hadCache?cache[language]:null;
        try
        {
            Directory.CreateDirectory(root);Directory.SetCurrentDirectory(root);
            cache[language]=new Dictionary<string,IReadOnlyList<StringTableEntry>>{{StringTables.ScanLog,Array.Empty<StringTableEntry>()}};
            RecordsAndJson();CounterArithmetic();Formatting();FileOwnership(root);CheckpointOwnership();EndingAndRetry(root);
            Console.WriteLine($"CAMPAIGN TIMING PASS {checks} checks; real isolated save IO and pure60Hz counters; renderer eligibility/playthrough not simulated.");
            return 0;
        }
        catch(Exception error){Console.Error.WriteLine("CAMPAIGN TIMING FAIL "+error);return 1;}
        finally
        {
            // A failed assertion must not leave the existing immutable commit
            // owner pending in this test process, or retain generated saves.
            string blocker=Path.Combine(root,"retry","Savedata");if(File.Exists(blocker))File.Delete(blocker);
            MatchStart.RetryPendingCommit();MatchStart.ClearRecentCampaignEnding();MatchStart.ClearRecentShipDock();
            if(hadCache)cache[language]=priorCache;else cache.Remove(language);
            Menu.SaveSlot=previousSlot;Menu.NeededSave=previousRequest;
            CultureInfo.CurrentCulture=priorCulture;CultureInfo.CurrentUICulture=priorUi;
            Directory.SetCurrentDirectory(previousDirectory);if(Directory.Exists(root))Directory.Delete(root,true);
        }
    }
    private static void RecordsAndJson()
    {
        Check(Clock.CurrentVersion==1&&Clock.TicksPerSecond==60,"new observation schema and gameplay fixed clock are explicit");
        Check(new StorySave().Stats.CampaignTime==null,"ordinary constructor preserves unknown historical observation");
        var fresh=StorySave.CreateNewCampaign();
        Check(fresh.Stats.CampaignTime is {Version:1,PlayTicks:0,GoreaFirstTicks:0,GoreaSecondTicks:0},"explicit new-campaign factory alone starts all observations at known zero");
        Check(Clock.Capture(fresh.Stats)==new Clock.Snapshot(0,0),"new campaign captures known zero total and both-phase sum");
        var legacy=JsonSerializer.Deserialize<StorySave>("{\"Stats\":{\"Deaths\":13,\"EnemyKills\":7}}")!;
        string legacyJson=JsonSerializer.Serialize(legacy);
        Check(legacy.Stats.CampaignTime==null&&!legacyJson.Contains("CampaignTime",StringComparison.Ordinal),"legacy absent time remains absent on JSON roundtrip without schema migration");
        Check(legacy.Stats.Deaths==13&&legacy.Stats.EnemyKills==7&&Clock.Capture(legacy.Stats)==default,"legacy gameplay facts survive while duration remains unknown");
        var roundtrip=JsonSerializer.Deserialize<StorySave>(JsonSerializer.Serialize(fresh))!;
        Check(roundtrip.Stats.CampaignTime is {Version:1,PlayTicks:0,GoreaFirstTicks:0,GoreaSecondTicks:0},"new observation JSON roundtrip retains known zero facts");
        foreach(string payload in new[]{"null","{}","{\"Version\":1}","{\"PlayTicks\":12}","{\"Version\":2,\"PlayTicks\":12,\"GoreaFirstTicks\":3,\"GoreaSecondTicks\":4}"})
        {
            var stats=JsonSerializer.Deserialize<StorySave.SaveStats>("{\"CampaignTime\":"+payload+"}")!;
            string before=JsonSerializer.Serialize(stats);Clock.Advance(stats,91);
            Check(Clock.Capture(stats)==default&&JsonSerializer.Serialize(stats)==before,"empty/unsupported record is unknown and never opted in by Advance");
        }
        var partial=JsonSerializer.Deserialize<StorySave.SaveStats>("{\"CampaignTime\":{\"Version\":1,\"PlayTicks\":12,\"GoreaSecondTicks\":4}}")!;
        Clock.Advance(partial,91);
        Check(partial.CampaignTime is {PlayTicks:13,GoreaFirstTicks:null,GoreaSecondTicks:4}&&Clock.Capture(partial)==new Clock.Snapshot(13,null),"partial version1 advances only present counters without inventing missing phase history");
        var statsNull=JsonSerializer.Deserialize<StorySave>("{\"Stats\":null}")!;
        Clock.Advance(statsNull.Stats,92);Check(Clock.Capture(statsNull.Stats)==default,"explicit null stats remains safely unknown");
        var nullRecord=JsonSerializer.Deserialize<StorySave.SaveStats>("{\"Deaths\":2,\"CampaignTime\":null}")!;
        Check(!JsonSerializer.Serialize(nullRecord).Contains("CampaignTime",StringComparison.Ordinal)&&nullRecord.Deaths==2,"explicit null extension omitted while existing fields survive");
    }
    private static void CounterArithmetic()
    {
        Clock.Advance(null,91);Check(Clock.Capture(null)==default,"missing stats never materialize timing");
        var stats=Stats(0,0,0);for(int i=0;i<30;i++)Clock.Advance(stats,45);
        Check(stats.CampaignTime!.PlayTicks==30&&Clock.FormatGorea(stats.CampaignTime.PlayTicks)=="0m 00s","thirty timeline/menu ticks are not a gameplay second");
        for(int i=0;i<30;i++)Clock.Advance(stats,45);
        Check(stats.CampaignTime.PlayTicks==60&&Clock.FormatGorea(stats.CampaignTime.PlayTicks)=="0m 01s","sixty eligible fixed gameplay steps form one second");
        Clock.Advance(stats,91);Clock.Advance(stats,92);Clock.Advance(stats,-1);
        Check(stats.CampaignTime is {PlayTicks:63,GoreaFirstTicks:1,GoreaSecondTicks:1}&&Clock.Capture(stats)==new Clock.Snapshot(63,2),"total observes each eligible caller while phase counters depend only on rooms91/92");
        var immutable=Clock.Capture(stats);Clock.Advance(stats,91);
        Check(immutable==new Clock.Snapshot(63,2)&&Clock.Capture(stats)==new Clock.Snapshot(64,3),"captured timing facts are detached from mutable save counters");
        var overflow=Stats(long.MaxValue-1,long.MaxValue,0);Clock.Advance(overflow,45);
        Check(Clock.Capture(overflow)==new Clock.Snapshot(long.MaxValue,long.MaxValue),"largest nonoverflowing observations retained exactly");
        Clock.Advance(overflow,91);
        Check(overflow.CampaignTime is {PlayTicks:null,GoreaFirstTicks:null,GoreaSecondTicks:0}&&Clock.Capture(overflow)==default,"overflow becomes unknown instead of wrapping or clamping to false elapsed time");
        var sum=Stats(6,long.MaxValue,1);Check(Clock.Capture(sum)==new Clock.Snapshot(6,null),"Gorea phase sum overflow is independently unknown");
        var secondOverflow=Stats(0,0,long.MaxValue);Clock.Advance(secondOverflow,92);
        Check(secondOverflow.CampaignTime is {PlayTicks:1,GoreaFirstTicks:0,GoreaSecondTicks:null}&&Clock.Capture(secondOverflow)==new Clock.Snapshot(1,null),"second phase overflow independently retires only its own observation");
        var negative=Stats(-1,-3,5);Clock.Advance(negative,45);
        Check(negative.CampaignTime is {PlayTicks:null,GoreaFirstTicks:-3,GoreaSecondTicks:5}&&Clock.Capture(negative)==default,"negative observations are never exposed; inactive phase is not rewritten");
        Clock.Advance(negative,91);Check(negative.CampaignTime!.GoreaFirstTicks==null&&negative.CampaignTime.GoreaSecondTicks==5,"advancing invalid active phase retires it without changing other phase");
        foreach(var pair in new (long? First,long? Second)[]{(null,0),(0,null),(null,null),(-1,0),(0,-1)})
            Check(Clock.Capture(Stats(12,pair.First,pair.Second))==new Clock.Snapshot(12,null),"both known nonnegative Gorea phases are required for sum");
    }
    private static void Formatting()
    {
        CultureInfo.CurrentCulture=new CultureInfo("ar-SA");CultureInfo.CurrentUICulture=new CultureInfo("fr-FR");
        foreach(long? unknown in new long?[]{null,-1,long.MinValue})
            Check(Clock.FormatTotal(unknown)=="NOT AVAILABLE"&&Clock.FormatGorea(unknown)=="NOT AVAILABLE","unknown durations have explicit stable text");
        foreach(var pair in new (long Ticks,string Total,string Gorea)[]{(0,"0h 00m","0m 00s"),(59,"0h 00m","0m 00s"),(60,"0h 00m","0m 01s"),
            (3599,"0h 00m","0m 59s"),(3600,"0h 01m","1m 00s"),((3600+2*60+59)*60L,"1h 02m","2m 59s"),(100*3600*60L,"100h 00m","0m 00s")})
            Check(Clock.FormatTotal(pair.Ticks)==pair.Total&&Clock.FormatGorea(pair.Ticks)==pair.Gorea,"invariant format/truncation and authored Gorea hour omission");
    }
    private static void FileOwnership(string root)
    {
        string path=Path.Combine(root,"files");Directory.CreateDirectory(Path.Combine(path,"Savedata"));Directory.SetCurrentDirectory(path);Menu.SaveSlot=1;
        const string recorded="{\"Stats\":{\"CampaignTime\":{\"Version\":1,\"PlayTicks\":123,\"GoreaFirstTicks\":4,\"GoreaSecondTicks\":5}}}";
        File.WriteAllText("Savedata/save001.json",recorded);var state=new SceneGameState(new ScenePlayerRegistry());state.StartNewSave();
        Check(Clock.Capture(state.StorySave.Stats)==new Clock.Snapshot(0,0)&&File.ReadAllText("Savedata/save001.json")==recorded,"New Game initializes live observations without rewriting prior slot");
        var continued=state.ReadSave();Check(Clock.Capture(continued.Stats)==new Clock.Snapshot(123,9)&&File.ReadAllText("Savedata/save001.json")==recorded,"continue reads persisted observations without advancing/migrating bytes");
        Menu.SaveSlot=2;var missing=state.ReadSave();Check(Clock.Capture(missing.Stats)==new Clock.Snapshot(0,0)&&!File.Exists("Savedata/save002.json"),"genuinely missing file starts new observation but does not write");
        Menu.SaveSlot=3;File.WriteAllText("Savedata/save003.json","null");Check(state.ReadSave().Stats.CampaignTime==null&&File.ReadAllText("Savedata/save003.json")=="null","existing JSONnull falls back to unknown instead of claiming new history");
        foreach(string legacy in new[]{"{}","{\"Stats\":{}}","{\"Stats\":{\"CampaignTime\":null}}"})
        {File.WriteAllText("Savedata/save003.json",legacy);Check(state.ReadSave().Stats.CampaignTime==null&&File.ReadAllText("Savedata/save003.json")==legacy,"existing empty/legacy JSON never opts into new timing");}
        File.WriteAllText("Savedata/save003.json","{");Expect<JsonException>(()=>state.ReadSave(),"malformed existing save is not replaced by a guessed fresh observation");
        Check(File.ReadAllText("Savedata/save003.json")=="{","failed read preserves original malformed bytes");
        Menu.SaveSlot=0;Check(Clock.Capture(state.ReadSave().Stats)==new Clock.Snapshot(0,0),"unslotted fresh state is explicit new game without reading numbered save");
        Menu.SaveSlot=2;var persisted=PostMatchSaveTests.Fixture();persisted.Stats.CampaignTime=NewRecord(321,7,11);var owner=State(persisted);
        owner.CommitSave();var loaded=owner.ReadSave();Check(Clock.Capture(loaded.Stats)==new Clock.Snapshot(321,18),"actual native save serializer/byte-array converter roundtrips timing fields");
    }
    private static void CheckpointOwnership()
    {
        var live=PostMatchSaveTests.Fixture();live.Stats.CampaignTime=NewRecord(100,2,3);var clean=PostMatchSaveTests.Fixture();clean.Stats.CampaignTime=NewRecord(1,0,0);
        var destinationStats=clean.Stats;var sourceStats=live.Stats;live.CopyTo(clean);
        Check(ReferenceEquals(clean.Stats,destinationStats)&&Clock.Capture(clean.Stats)==new Clock.Snapshot(1,0)&&ReferenceEquals(live.Stats,sourceStats),"CopyTo keeps existing independent Stats owner rather than aliasing or rolling back global observations");
        var state=State(live);typeof(SceneGameState).GetField("_cleanStorySave",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(state,clean);
        live.Health=200;state.UpdateCleanSave(force:true);Clock.Advance(live.Stats,91);live.Health=1;state.RestoreCleanSave();
        Check(live.Health==200&&ReferenceEquals(live.Stats,sourceStats)&&Clock.Capture(live.Stats)==new Clock.Snapshot(101,6),"checkpoint health restore preserves live Stats and elapsed time accrued since checkpoint");
    }
    private static void EndingAndRetry(string root)
    {
        var save=PostMatchSaveTests.Fixture();save.Stats.CampaignTime=NewRecord(3601,7,11);
        var summary=CampaignEndingSummary.Capture(1,92,save,_=>89)!.Value;
        var positional=new CampaignEndingSummary(1,CampaignEndingKind.Complete,false,89,8,13,11,7);
        Check(positional.PlayTicks==null&&positional.GoreaTicks==null,"existing positional summary constructor preserves unknown optional timing");
        var native=new NativeCampaignEndingReport(summary.Slot,true,false,summary.CompletionPercent,summary.Octoliths,summary.Deaths,summary.HunterKills,summary.EnemyKills)
            {PlayTicks=summary.PlayTicks,GoreaTicks=summary.GoreaTicks};
        using(var controller=new NativeCampaignEndingController(native))
        {save.Stats.CampaignTime.PlayTicks=999;Check(summary.PlayTicks==3601&&summary.GoreaTicks==18&&controller.Snapshot().Report is {PlayTicks:3601,GoreaTicks:18},"summary and native report remain immutable after live save timing mutation");}
        string retry=Path.Combine(root,"retry");Directory.CreateDirectory(retry);Directory.SetCurrentDirectory(retry);File.WriteAllText("Savedata","block generated destination");
        save.Stats.CampaignTime=NewRecord(3601,7,11);var state=State(save);state.LastCampaignDeparture=CampaignDeparture.Ending;state.LastCampaignDepartureRoomId=92;
        Menu.SaveSlot=1;Menu.NeededSave=SaveWhen.Always;MatchStart.ClearRecentCampaignEnding();var commit=MatchStart.PrepareAfterMatch(state);
        Check(commit.Ending is {PlayTicks:3601,GoreaTicks:18,SaveCommitted:false}&&MatchStart.RecentCampaignEnding==null,"timing receipt captured before teardown without premature publication");
        bool failed=false;try{MatchStart.AfterMatch(commit);}catch(Exception error)when(error is IOException or UnauthorizedAccessException){failed=true;}
        Check(failed&&commit.State==PostMatchCommitState.Failed&&commit.HasPayload&&MatchStart.RecentCampaignEnding==null,"real writer failure retains immutable timing payload and withholds ending publication");
        save.Stats.CampaignTime=NewRecord(999,222,333);Menu.SaveSlot=2;Menu.NeededSave=SaveWhen.Always;File.Delete("Savedata");MatchStart.RetryPendingCommit();
        using var json=JsonDocument.Parse(File.ReadAllBytes("Savedata/save001.json"));var time=json.RootElement.GetProperty("Stats").GetProperty("CampaignTime");
        Check(time.GetProperty("Version").GetInt32()==1&&time.GetProperty("PlayTicks").GetInt64()==3601&&time.GetProperty("GoreaFirstTicks").GetInt64()==7&&time.GetProperty("GoreaSecondTicks").GetInt64()==11,
            "retry writes original captured timing values after mutable live record replacement");
        Check(MatchStart.RecentCampaignEnding is {PlayTicks:3601,GoreaTicks:18,SaveCommitted:true}&&commit.State==PostMatchCommitState.Released&&!commit.HasPayload,
            "successful retry publishes original timing receipt and releases serialized owner");
        Check(!File.Exists("Savedata/save002.json")&&Menu.SaveSlot==2&&Menu.NeededSave==SaveWhen.Always,"retry cannot redirect timing to replacement slot or clear newer request");
    }
    private static StorySave.SaveStats Stats(long? play,long? first,long? second)=>new(){CampaignTime=NewRecord(play,first,second)};
    private static Clock.Record NewRecord(long? play,long? first,long? second)=>new(){Version=1,PlayTicks=play,GoreaFirstTicks=first,GoreaSecondTicks=second};
    private static SceneGameState State(StorySave save)
    {var state=new SceneGameState(new ScenePlayerRegistry());typeof(SceneGameState).GetProperty(nameof(SceneGameState.StorySave))!.SetValue(state,save);return state;}
    private static void Check(bool value,string label){checks++;if(!value)throw new InvalidOperationException("Campaign timing: "+label);}
    private static void Expect<T>(Action action,string label)where T:Exception
    {checks++;try{action();}catch(T){return;}throw new InvalidOperationException("Campaign timing expected "+typeof(T).Name+": "+label);}
}
