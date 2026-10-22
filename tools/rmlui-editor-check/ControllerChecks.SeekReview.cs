using System.Collections.Immutable;
using MphRead.Mods.Launcher.Core;

internal static partial class ControllerChecks
{
    private static void RunWorkspaceSeekReview()
    {
        int checks=0;
        void Check(bool condition,string label){if(!condition)throw new Exception("WORKSPACE SEEK REVIEW: "+label);checks++;}
        var backend=new SeekReviewEditor();using(var editor=new TheatreEditorController(backend))
        {
            editor.SetEventSearch("SHOT");editor.NextFilter();editor.SetName("RETAINED DRAFT");
            var before=editor.Snapshot();backend.ReplaceTransport();
            Check(!editor.Refresh(),"ordinary refresh cannot adopt a replacement transport");
            Check(editor.RefreshWorkspace()&&editor.Snapshot().Live.Current,"explicit provider-validated workspace review adopts same-player seek transport");
            var adopted=editor.Snapshot();
            Check(adopted.EventSearch==before.EventSearch&&adopted.Filter==before.Filter&&adopted.Name==before.Name,
                "seek review preserves search, filter and authored name");
            Check(editor.Refresh(),"ordinary review stays available on the explicitly adopted transport");
            Check(editor.Stage(TheatreEditorEditKind.AddSelection)&&editor.Confirm()&&backend.Memory.Commits==1,
                "normal reviewed reel commit succeeds after seek adoption");
            Check(editor.SetTags("after seek, reviewed")&&editor.SaveTags()&&backend.TagWrites==1&&backend.Memory.Context.Tags.Length==2,
                "source-bound tag publication succeeds after seek adoption");
        }
        var mutations=new (string Label,Func<TheatreEditorContext,TheatreEditorContext> Change)[]
        {
            ("owner",c=>c with{Owner=new object()}),("generation",c=>c with{SceneGeneration=c.SceneGeneration+1}),
            ("logical path",c=>c with{LogicalPath="OTHER.ppdemo"}),("playback path",c=>c with{PlaybackPath="OTHER-CACHE.ppdemo"}),
            ("source",c=>c with{SourceRevision="OTHER"}),("duration",c=>c with{Duration=c.Duration+1}),
            ("unsettled",c=>c with{SettledPaused=false}),("unverified",c=>c with{PublicationVerified=false})
        };
        foreach(var mutation in mutations)
        {
            var denied=new SeekReviewEditor();using var editor=new TheatreEditorController(denied);var original=editor.Snapshot().Context;
            denied.ReplaceTransport();denied.Memory.Context=mutation.Change(denied.Memory.Context);
            Check(!editor.RefreshWorkspace()&&ReferenceEquals(editor.Snapshot().Context,original)&&denied.Memory.Commits==0&&denied.TagWrites==0,
                mutation.Label+" mismatch rejects seek review without publication or context adoption");
        }
        foreach(string reason in new[]{"provider","pending","dirty tags"})
        {
            var denied=new SeekReviewEditor();using var editor=new TheatreEditorController(denied);
            if(reason=="pending")Check(editor.Stage(TheatreEditorEditKind.AddSelection),"pending proof starts with a real staged edit");
            if(reason=="dirty tags")editor.SetTags("unsaved draft");
            var original=editor.Snapshot();denied.ReplaceTransport();denied.Allow=reason!="provider";
            Check(!editor.RefreshWorkspace()&&ReferenceEquals(editor.Snapshot().Context,original.Context)
                &&editor.Snapshot().Pending==original.Pending&&editor.Snapshot().Tags==original.Tags
                &&denied.Memory.Commits==0&&denied.TagWrites==0,reason+" rejection preserves draft and authored metadata");
        }
        // Backends without the optional owner proof must not gain adoption authority.
        var unsupported=new MemoryEditor();using(var editor=new TheatreEditorController(unsupported))
        {unsupported.Context=unsupported.Context with{Transport=new object()};Check(!editor.RefreshWorkspace()&&unsupported.Commits==0,"non-capability backend cannot adopt a seek transport");}
        Console.WriteLine($"PASS {checks} explicit workspace seek review/adoption/publication boundary checks");
    }
    private sealed class SeekReviewEditor:ITheatreEditorBackend,ITheatreEditorMetadataBackend,ITheatreEditorSeekReviewBackend
    {
        internal readonly MemoryEditor Memory=new();internal bool Allow=true;internal int TagWrites;
        internal void ReplaceTransport()=>Memory.Context=Memory.Context with{Transport=new object(),SeekGeneration=Memory.Context.SeekGeneration+1,Frame=20};
        public TheatreEditorContext Capture()=>Memory.Context;
        public TheatreEditorLive ReadLive(TheatreEditorContext context)=>Memory.ReadLive(context) with
        {Current=Memory.Current&&ReferenceEquals(context.Transport,Memory.Context.Transport)&&ReferenceEquals(context.Owner,Memory.Context.Owner)};
        public void ValidateSeekReview(TheatreEditorContext previous,TheatreEditorContext next)
        {if(!Allow||!ReferenceEquals(next,Memory.Context))throw new TheatreEditorStaleException("Synthetic provider denied exact seek owner review.");}
        public TheatreEditorContext SaveTags(TheatreEditorContext context,ImmutableArray<string> tags)
        {
            if(!ReadLive(context).Current)throw new TheatreEditorStaleException("Synthetic tags owner retired.");
            TagWrites++;return Memory.Context=Memory.Context with{Tags=tags,AnnotationRevision="TAGS-"+TagWrites};
        }
        public TheatreEditorContext Commit(TheatreEditorEdit reviewed)
        {if(!ReadLive(reviewed.Context).Current)throw new TheatreEditorStaleException("Synthetic commit owner retired.");return Memory.Commit(reviewed);}
        public void Seek(TheatreEditorContext context,TheatreEditorEntry exact,bool range)=>Memory.Seek(context,exact,range);
        public void InspectShot(TheatreEditorContext context,TheatreEditorEntry? previous,int direction,int player)=>Memory.InspectShot(context,previous,direction,player);
        public void ToggleHud(TheatreEditorContext context,TheatreEditorHud hud)=>Memory.ToggleHud(context,hud);
        public void Retire()=>Memory.Retire();
    }
}
