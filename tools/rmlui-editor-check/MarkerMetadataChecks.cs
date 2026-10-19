using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;

internal static class MarkerMetadataChecks
{
    internal static void Run()
    {
        int checks=0;
        void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;}
        bool Reject(Action action){try{action();return false;}catch(Exception error)when(error is IOException or InvalidDataException or InvalidOperationException or ArgumentException){return true;}}
        string directory=Path.Combine(Path.GetTempPath(),"prime-editor-tags-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path=Path.Combine(directory,"SYNTHETIC.ppdemo"),annotation=path+ReplayAnnotations.Extension,camera=path+".camera";
            File.WriteAllBytes(path,new byte[]{1,2,3,4,5,6,7,8});File.WriteAllBytes(camera,new byte[]{9,8,7,6});
            string Hash(string file)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
            string sourceHash=Hash(path),cameraHash=Hash(camera);
            DemoPlayback.LogicalPath=DemoPlayback.PlaybackPath=path;
            DemoPlayback.Session.Transport=new();DemoPlayback.IsActive=true;
            DemoPlayback.Events=new[]{new ReplayEvent(10,ReplayEventType.WeaponFired,0,1,0),new ReplayEvent(20,ReplayEventType.Damage,0,1,50),new ReplayEvent(30,ReplayEventType.Kill,2,1),new ReplayEvent(500,ReplayEventType.MatchEnded)};
            DemoPlayback.Metadata=new(){Players=new[]{new ReplayPlayerInfo(0,0,0,"Hunter"),new ReplayPlayerInfo(1,1,1,"Target"),new ReplayPlayerInfo(2,2,2,"Other")}};
            var bookmark=ReplayAnnotations.AddBookmark(path,15,"OWNED BOOKMARK");
            var highlight=ReplayAnnotations.AddHighlight(path,10,40,"OWNED HIGHLIGHT");
            ReplayAnnotations.SetOrganization(path,new[]{"original"},new[]{"KEEP COLLECTION"});
            var backend=RmlUiOpenGlSmoke.Fixture();var metadata=(ITheatreEditorMetadataBackend)backend;
            using var controller=new TheatreEditorController(backend);
            string beforeSearch=Hash(annotation);
            Check(controller.SetEventSearch("wEaPoN")&&controller.Snapshot().Entries.Single().Name=="WeaponFired",
                "recorded enum-name substring search is case insensitive");
            Check(controller.NextPlayer()&&controller.Snapshot().Entries.Length==1&&controller.NextPlayer()&&controller.Snapshot().Entries.Length==1,
                "event text search intersects actor and target player filters");
            Check(controller.NextPlayer()&&controller.Snapshot().Entries.IsEmpty&&controller.NextPlayer(),
                "event search never invents another player's matching event");
            for(int i=0;i<4;i++)controller.NextFilter();
            Check(controller.SetEventSearch("damage")&&controller.Snapshot().Entries.IsEmpty,
                "event text and existing Shots category filter intersect");
            for(int i=0;i<4;i++)controller.NextFilter();
            Check(controller.Snapshot().Entries.Single().Name=="Damage"&&controller.SetEventSearch("not-recorded")&&controller.Snapshot().Entries.IsEmpty,
                "search exposes actual matching recorded type only, with truthful empty results");
            Check(!controller.SetEventSearch(new string('x',129))&&controller.Snapshot().EventSearch=="not-recorded"
                &&Hash(annotation)==beforeSearch&&Hash(path)==sourceHash&&Hash(camera)==cameraHash,
                "bounded event search retains prior draft and never writes source or sidecars");
            Check(controller.SetEventSearch("")&&controller.Snapshot().Entries.Any(row=>row.Id.StartsWith("bookmark:")),
                "clearing search restores native annotation rows as well as recorded events");
            Check(controller.SetTags("  Duel, duel, Map Review ,, ")&&controller.Snapshot().TagsDirty&&Hash(annotation)==beforeSearch,
                "tag text remains an explicit unsaved draft");
            Check(!controller.SetTags(new string('x',1025))&&controller.Snapshot().Tags=="  Duel, duel, Map Review ,, ",
                "oversize tags preserve exact previous draft");
            Check(controller.Stage(TheatreEditorEditKind.AddSelection)&&!controller.SaveTags()&&Hash(annotation)==beforeSearch,
                "pending reel confirmation fences tag publication");controller.Cancel();
            using(var locked=new FileStream(annotation,FileMode.Open,FileAccess.Read,FileShare.None))
                Check(!controller.SaveTags()&&controller.Snapshot().TagsDirty,"failed metadata write keeps exact tag draft available for retry");
            Check(controller.SaveTags()&&ReplayAnnotations.Tags(path).SequenceEqual(new[]{"Duel","Map Review"})&&!controller.Snapshot().TagsDirty,
                "explicit Save Tags normalizes and atomically publishes once through the retained annotation owner");
            Check(ReplayAnnotations.Bookmarks(path).Single()==bookmark&&ReplayAnnotations.Highlights(path).Single()==highlight
                &&ReplayAnnotations.Collections(path).SequenceEqual(new[]{"KEEP COLLECTION"})&&Hash(path)==sourceHash&&Hash(camera)==cameraHash
                &&!File.Exists(path+ReplayReels.Extension),
                "saving tags preserves actual bookmark/highlight identities, collection, packet stream, camera and absent reel");
            byte[] saved=File.ReadAllBytes(annotation);DateTime savedStamp=File.GetLastWriteTimeUtc(annotation);
            Check(controller.SaveTags()&&File.ReadAllBytes(annotation).SequenceEqual(saved)&&File.GetLastWriteTimeUtc(annotation)==savedStamp,
                "duplicate Save Tags leaves exact bytes and timestamp unchanged");
            var context=backend.Capture();
            File.WriteAllBytes(annotation,Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(saved).Replace("\"Duel\"","\"RACE\"",StringComparison.Ordinal)));
            File.SetLastWriteTimeUtc(annotation,savedStamp);
            byte[] raced=File.ReadAllBytes(annotation);
            Check(raced.Length==saved.Length&&Reject(()=>metadata.SaveTags(context,ImmutableArray.Create("must not save")))&&File.ReadAllBytes(annotation).SequenceEqual(raced),
                "same-length same-stamp annotation ABA after review cannot overwrite external metadata");
            File.WriteAllBytes(annotation,saved);File.SetLastWriteTimeUtc(annotation,savedStamp);
            context=backend.Capture();var sourceBytes=File.ReadAllBytes(path);DateTime sourceStamp=File.GetLastWriteTimeUtc(path);
            File.WriteAllBytes(path,sourceBytes.Reverse().ToArray());File.SetLastWriteTimeUtc(path,sourceStamp);
            Check(Reject(()=>metadata.SaveTags(context,ImmutableArray.Create("must not save")))&&File.ReadAllBytes(annotation).SequenceEqual(saved),
                "same-stamp packet source replacement rejects tag save");
            File.WriteAllBytes(path,sourceBytes);File.SetLastWriteTimeUtc(path,sourceStamp);
            context=backend.Capture();DemoPlayback.Session.Transport.Seek(DemoPlayback.Session.Transport.CurrentFrame,false);DemoPlayback.Session.Transport.IsSeeking=false;
            Check(Reject(()=>metadata.SaveTags(context,ImmutableArray.Create("must not save"))),"new same-frame seek generation cannot publish reviewed tags");
            context=backend.Capture();ReplayVideoExporter.Rendering=true;
            Check(Reject(()=>metadata.SaveTags(context,ImmutableArray.Create("must not save"))),"export owner fences tag publication");ReplayVideoExporter.Rendering=false;
            context=backend.Capture();DemoPlayback.ReplaceOwner();
            Check(Reject(()=>metadata.SaveTags(context,ImmutableArray.Create("must not save")))&&File.ReadAllBytes(annotation).SequenceEqual(saved),
                "replacement durable replay owner cannot adopt an old tag review");
            var normalized=TheatreReplayTags.Normalize(Enumerable.Range(0,30).Select(i=>new string('x',45)+i));
            Check(normalized.Length==1&&normalized[0].Length==40,"tag normalization matches existing 40-character case-insensitive label semantics");
            Check(TheatreReplayTags.Parse(string.Join(',',Enumerable.Range(0,30).Select(i=>"tag"+i))).Length==24,
                "tag list has the retained maximum of 24 distinct labels");
            Check(!Directory.EnumerateFiles(directory,".reviewed-*").Any(),"reviewed tag publication leaves no proposed temporary files");
            Console.WriteLine($"PASS {checks} source-linked event search/replay tags/reviewed owner and sidecar checks; temporary synthetic metadata only");
        }
        finally{ReplayVideoExporter.Rendering=false;Directory.Delete(directory,true);}
    }
}
