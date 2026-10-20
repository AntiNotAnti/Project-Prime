#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using MphRead.Entities;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;

namespace MphRead.Mods.Launcher.RmlUi.Render;

internal static partial class RmlUiOpenGlSmoke
{
    private sealed partial class CurrentReplayEditor : ITheatreEditorBackend, ITheatreEditorSuggestionBackend, ITheatreEditorMetadataBackend, ITheatreEditorSeekReviewBackend, ITheatreTimelineSeek, ITheatreSpatialBackend, ITheatreStudioToolsBackend
    {
        private sealed record Files(ReplayReviewedSidecar Annotations,ReplayReviewedSidecar Reel,ImmutableArray<ReplayEvent> Events);
        private sealed record Inspection(object Owner,ReplayTransport Transport,long Seek,uint Target,ReplayEvent Event);
        private Inspection? _inspection;
        private ReplayCombatDiagnostics.Selection? _published;
        private string _diagnostic="Select an event or find a shot near the playhead.";
        public TheatreEditorContext Capture()
        {
            lock(ReplayAnnotationWitness.Gate)
            {
                if(DemoPlayback.PresentationScene is not{} ready||!DemoPlayback.Owns(ready))
                    throw new InvalidOperationException("Wait for the recording's playback player before reviewing editor inputs.");
                var proof=ReplayAnnotationWitness.Capture(); var transport=DemoPlayback.Session.Transport;
                object owner=DemoPlayback.PlaybackOwner;
                string source=ReplayNativeFileWitness.Source(proof.LogicalPath,proof.PlaybackPath);
                var annotation=ReplayReviewedMetadata.Read(proof.LogicalPath+ReplayAnnotations.Extension);
                var reel=ReplayReviewedMetadata.Read(proof.LogicalPath+ReplayReels.Extension);
                var annotations=ReplayAnnotations.ReadReviewed(annotation,proof.Duration);
                var document=ReplayReels.ReadReviewed(reel,proof.LogicalPath,proof.Duration);
                var events=DemoPlayback.Events.ToImmutableArray();
                var players=(DemoPlayback.Metadata?.Players??Array.Empty<ReplayPlayerInfo>()).OrderBy(p=>p.Slot)
                    .Select(p=>new TheatreEditorPlayer(p.Slot,p.DisplayName)).ToImmutableArray();
                string Name(int slot)=>players.FirstOrDefault(p=>p.Slot==slot)?.Name??(slot<8?$"P{slot+1}":"—");
                var named=annotations.Highlights.Select(row=>new TheatreEditorEntry(row.Id.ToString("N"),row.StartFrame,row.EndFrame,row.Name,"Named highlight")).ToImmutableArray();
                var eventRows=events.Select((row,index)=>new TheatreEditorEntry("event:"+index,row.Frame,row.Frame,row.Type.ToString(),
                    $"{Name(row.ActorSlot)} → {Name(row.TargetSlot)} // VALUE {row.Value}",row.ActorSlot,(int)row.Type,0,row.TargetSlot)).ToImmutableArray();
                eventRows=eventRows.AddRange(annotations.Bookmarks.Select(row=>new TheatreEditorEntry("bookmark:"+row.Id.ToString("N"),row.Frame,row.Frame,row.Name,"Bookmark")))
                    .AddRange(named.Select(row=>row with{Id="named:"+row.Id})).OrderBy(row=>row.Frame).ToImmutableArray();
                // Suggestions are explicitly generated on a worker; reviewing
                // sidecars must not synchronously sort/score a large recording.
                var highlights=ImmutableArray<TheatreEditorEntry>.Empty;
                var analytics=ReplayStudio.Analytics(events,proof.Duration); var rows=ImmutableArray.CreateBuilder<TheatreEditorEntry>();
                rows.Add(new("summary",0,0,"MATCH SUMMARY",$"{analytics.TotalKills} KILLS // {analytics.TotalDamage} DAMAGE // {analytics.ObjectiveEvents} OBJECTIVES"));
                foreach(var row in analytics.Players.OrderByDescending(row=>row.Kills).ThenByDescending(row=>row.Damage))
                {
                    uint frame=events.Where(e=>e.ActorSlot==row.Slot).Select(e=>e.Frame).DefaultIfEmpty().First();
                    rows.Add(new("player:"+row.Slot,frame,frame,Name(row.Slot),$"{row.Kills}K/{row.Deaths}D // {row.Damage} DAMAGE // {row.ObjectiveEvents} OBJ // {row.Spawns} SPAWNS",row.Slot));
                }
                rows.Add(new("activity-method",0,0,"RECORDED ACTIVITY / 10s",ReplayAnalyticsSnapshot.ActivityDescription));
                foreach(var row in analytics.DamageTimeline.Where(row=>row.Damage>0||row.Kills>0||row.Objectives>0))
                    rows.Add(new("bucket:"+row.StartFrame,row.StartFrame,row.EndFrame,"RECORDED ACTIVITY",$"{row.ActivityScore}/100 ACTIVITY // {row.Damage} DAMAGE // {row.Kills} KILLS // {row.Objectives} OBJECTIVES"));
                foreach(var row in analytics.WeaponUsage)
                {
                    uint frame=events.Where(e=>e.Type==ReplayEventType.WeaponFired&&e.Value==row.Weapon).Select(e=>e.Frame).DefaultIfEmpty().First();
                    string weapon=ReplayStudio.TryBeamType(row.Weapon,out var beam)?beam.ToString():$"WEAPON {row.Weapon}";
                    rows.Add(new("weapon:"+row.Weapon,frame,frame,weapon,$"{row.Shots} SHOTS"));
                }
                var result=new TheatreEditorContext(proof.SceneGeneration,owner,transport,proof.LogicalPath,proof.PlaybackPath,source,
                    annotation.Revision,reel.Revision,transport.CurrentFrame,proof.Duration,transport.SeekGeneration,transport.ClipIn,transport.ClipOut,
                    !transport.IsSeeking&&(transport.IsPaused||transport.AtEnd),players,eventRows,highlights,named,
                    document.Segments.Select(ToEntry).ToImmutableArray(),rows.ToImmutable(),new Files(annotation,reel,events))
                    { Tags=TheatreReplayTags.Normalize(annotations.Tags) };
                Validate(result,false); return result;
            }
        }
        public void ValidateSuggestions(TheatreEditorContext context)
        { lock(ReplayAnnotationWitness.Gate) Validate(context,false); }
        public System.Threading.Tasks.Task<ImmutableArray<TheatreEditorEntry>> GenerateSuggestions(
            TheatreEditorContext context, System.Threading.CancellationToken cancellation)
        {
            Validate(context, true);
            var events = ((Files)context.Persisted).Events;
            if (events.Length > ReplayStudio.MaximumAnalysisEvents)
                throw new InvalidOperationException("Replay exceeds the bounded highlight analysis limit.");
            return System.Threading.Tasks.Task.Run(() => ReplayContextAnalysis.Suggestions(context.PlaybackPath, events,context.Duration,
                    cancellation: cancellation).Select((row,index)=>new TheatreEditorEntry("highlight:"+index,
                        row.StartFrame,row.EndFrame,row.Label,
                        $"{(row.Kind == ReplayHighlightKind.Context ? "VERIFIED CONTEXT" : "ANNOTATION")} // {row.Kind} // SCORE {row.Score} // FOCUS {row.FocusFrame}",
                        row.ActorSlot,FocusFrame:row.FocusFrame)).ToImmutableArray(),cancellation);
        }
        public void ValidateSeekReview(TheatreEditorContext previous,TheatreEditorContext next)
        {
            lock(ReplayAnnotationWitness.Gate)
            {
                if(!ReferenceEquals(previous.Owner,next.Owner)||previous.SceneGeneration!=next.SceneGeneration
                    ||previous.LogicalPath!=next.LogicalPath||previous.PlaybackPath!=next.PlaybackPath
                    ||previous.SourceRevision!=next.SourceRevision||previous.Duration!=next.Duration)
                    throw new TheatreEditorStaleException("The recording/player changed while reviewing its seek. Reopen the editor.");
                Validate(next,true);
            }
        }
        public TheatreEditorLive ReadLive(TheatreEditorContext context)
        {
            PollComparisonPreview();
            bool current=OwnerCurrent(context);
            if(!current){RetireReview();return new(false,ReplayVideoExporter.Rendering,_diagnostic,ReplayHud.ShowAnalytics,ReplayHud.ShowNetworkDebug,ReplayCombatDiagnostics.ShowHud,ReplayCombatDiagnostics.ShowRays);}
            PollWorldComparison();
            PollInspection();
            return new(true,ReplayVideoExporter.Rendering,_diagnostic,ReplayHud.ShowAnalytics,ReplayHud.ShowNetworkDebug,ReplayCombatDiagnostics.ShowHud,ReplayCombatDiagnostics.ShowRays);
        }
        public void SeekTimelineFrame(TheatreEditorContext context,uint frame)
        {
            lock(ReplayAnnotationWitness.Gate)
            {
                Validate(context,true);
                if(frame>context.Duration)throw new ArgumentException("The requested frame lies beyond the reviewed replay.");
                var transport=(ReplayTransport)context.Transport;
                RetireReview();transport.Seek(frame,resume:false);
                _diagnostic="Seeking the exact reviewed timeline frame. Refresh after seeking settles.";
            }
        }
        public void Seek(TheatreEditorContext context,TheatreEditorEntry exact,bool selectRange)
        {
            lock(ReplayAnnotationWitness.Gate)
            {
                Validate(context,false); RequireEntry(context,exact);
                var transport=(ReplayTransport)context.Transport;
                if(selectRange)
                {
                    if(exact.Frame>=exact.EndFrame)throw new InvalidOperationException("This entry has no nonempty range.");
                    SetRange(transport,exact.Frame,exact.EndFrame);
                }
                if(exact.Id.StartsWith("event:",StringComparison.Ordinal))
                {
                    var events=((Files)context.Persisted).Events;
                    if(!int.TryParse(exact.Id[6..],out int index)||(uint)index>=(uint)events.Length)throw new TheatreEditorStaleException("The event changed. Refresh.");
                    Inspect(context,events[index]);
                }
                else {RetireReview();transport.Seek(exact.FocusFrame??exact.Frame,resume:false);_diagnostic="Seeking reviewed entry. Refresh to review new range inputs.";}
            }
        }
        public void InspectShot(TheatreEditorContext context,TheatreEditorEntry? previous,int direction,int player)
        {
            lock(ReplayAnnotationWitness.Gate)
            {
                Validate(context,false);
                uint anchor=direction==0?((ReplayTransport)context.Transport).CurrentFrame:_inspection?.Target??previous?.Frame??context.Frame;
                var selected=((Files)context.Persisted).Events.Where(e=>e.Type==ReplayEventType.WeaponFired&&(player<0||e.ActorSlot==player)
                    &&(direction==0||(direction<0?e.Frame<anchor:e.Frame>anchor))).OrderBy(e=>Math.Abs((long)e.Frame-anchor))
                    .Select(e=>(ReplayEvent?)e).FirstOrDefault();
                if(selected is not ReplayEvent shot)throw new InvalidOperationException("No recorded shot exists in that direction for this player.");
                Inspect(context,shot);
            }
        }
        private void Inspect(TheatreEditorContext context,ReplayEvent selected)
        {
            RetireReview();var transport=(ReplayTransport)context.Transport;
            transport.Seek(selected.Frame,resume:false);
            if(selected.ActorSlot<PlayerEntity.SlotCapacity)SpectatorMode.Watch(selected.ActorSlot);
            _inspection=new(context.Owner,transport,transport.SeekGeneration,selected.Frame,selected);
            _diagnostic=$"Seeking {selected.Type} at frame {selected.Frame}. Authoritative evidence appears only when this exact seek settles.";
        }
        private void PollInspection()
        {
            if(_inspection is not{} pending)return;
            if(_published is{} published&&ReplayCombatDiagnostics.Selected!=published)
            {_inspection=null;_published=null;_diagnostic="Combat inspection retired because another selection took focus.";return;}
            var transport=pending.Transport;
            if(!ReferenceEquals(DemoPlayback.PlaybackOwner,pending.Owner)||!ReferenceEquals(DemoPlayback.Session.Transport,transport)
                ||transport.SeekGeneration!=pending.Seek||ReplayVideoExporter.Rendering)
            {RetireReview();_diagnostic="Combat selection retired because playback ownership or seek changed.";return;}
            if(transport.IsSeeking)return;
            if(transport.CurrentFrame!=pending.Target || !(transport.IsPaused||transport.AtEnd))
            {RetireReview();_diagnostic="Combat selection retired because playback moved.";return;}
            var scene=DemoPlayback.PresentationScene;
            if(scene==null||!DemoPlayback.Owns(scene)||scene.ReplayPoses==null)return;
            var selected=pending.Event;bool death=selected.Type==ReplayEventType.PlayerDeath;
            int shooter=death?selected.TargetSlot:selected.ActorSlot,victim=death?selected.ActorSlot:selected.TargetSlot;
            if(scene.ReplayPoses.TryCombatDiagnostic(selected.Frame,shooter<PlayerEntity.SlotCapacity?shooter:-1,
                victim<PlayerEntity.SlotCapacity?victim:-1,selected.Type==ReplayEventType.WeaponFired?selected.Value:-1,
                selected.Type==ReplayEventType.Damage?selected.Value:-1,selected.Type is ReplayEventType.Kill or ReplayEventType.PlayerDeath,
                selected.Type==ReplayEventType.Headshot,direction:0,out var diagnostic))
            {ReplayCombatDiagnostics.Select(diagnostic);_published=ReplayCombatDiagnostics.Selected;_diagnostic=ReplayCombatDiagnostics.Describe(diagnostic);}
            else _diagnostic="No authoritative shot fact is associated with this recorded event in the retained diagnostic window.";
        }
        public void ToggleHud(TheatreEditorContext context,TheatreEditorHud hud)
        {
            Validate(context,false);
            switch(hud)
            {case TheatreEditorHud.Analytics:ReplayHud.ShowAnalytics=!ReplayHud.ShowAnalytics;break;
             case TheatreEditorHud.Network:ReplayHud.ShowNetworkDebug=!ReplayHud.ShowNetworkDebug;break;
             case TheatreEditorHud.Combat:ReplayCombatDiagnostics.ShowHud=!ReplayCombatDiagnostics.ShowHud;break;
             case TheatreEditorHud.ShotRays:ReplayCombatDiagnostics.ShowRays=!ReplayCombatDiagnostics.ShowRays;break;
             default:throw new ArgumentException("Unknown replay HUD.");}
        }
        public TheatreEditorContext Commit(TheatreEditorEdit reviewed)
        {
            lock(ReplayAnnotationWitness.Gate)
            {
                var context=reviewed.Context;Validate(context,true);
                if(context.Persisted is not Files files)throw new ArgumentException("The review has no persisted owner.");
                if(reviewed.Kind==TheatreEditorEditKind.RemoveNamedHighlight)
                {
                    var document=ReplayAnnotations.ReadReviewed(files.Annotations,context.Duration);
                    if(!context.NamedHighlights.SequenceEqual(reviewed.Before)||reviewed.Selected==null
                        ||!reviewed.Before.Contains(reviewed.Selected)||reviewed.After.Length!=reviewed.Before.Length-1
                        ||!reviewed.After.SequenceEqual(reviewed.Before.Where(row=>row!=reviewed.Selected)))throw new ArgumentException("Review exactly one named-highlight removal.");
                    Guid id=Guid.ParseExact(reviewed.Selected.Id,"N");
                    document.Highlights.RemoveAll(row=>row.Id==id);
                    var saved=ReplayAnnotations.SaveReviewed(files.Annotations,document,()=>Validate(context,true));
                    return context with { AnnotationRevision=saved.Written.Revision,NamedHighlights=reviewed.After,Persisted=files with {Annotations=saved.Written},PublicationVerified=saved.Verified,PublicationNotice=saved.Notice };
                }
                else
                {
                    if(!context.Reel.SequenceEqual(reviewed.Before))throw new TheatreEditorStaleException("The original reel review changed.");
                    var document=ReplayReels.ReadReviewed(files.Reel,context.LogicalPath,context.Duration);
                    document.Segments=reviewed.After.Select(row=>new ReplayReelSegment(Guid.ParseExact(row.Id,"N"),row.Frame,row.EndFrame,row.Name,(ReplaySegmentCamera)row.Camera)).ToList();
                    // Validate the complete resulting document using the same retained owner before writing.
                    var bytes=System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(document);
                    _=ReplayReels.ReadReviewed(new(files.Reel.Path,"draft",bytes),context.LogicalPath,context.Duration);
                    var saved=ReplayReels.SaveReviewed(files.Reel,document,()=>Validate(context,true));
                    return context with { ReelRevision=saved.Written.Revision,Reel=reviewed.After,Persisted=files with {Reel=saved.Written},PublicationVerified=saved.Verified,PublicationNotice=saved.Notice };
                }
            }
        }
        public TheatreEditorContext SaveTags(TheatreEditorContext context,ImmutableArray<string> tags)
        {
            lock(ReplayAnnotationWitness.Gate)
            {
                Validate(context,true);
                if(!context.PublicationVerified||context.Persisted is not Files files)
                    throw new TheatreEditorStaleException("Refresh the exact annotation owner before saving replay tags.");
                if(tags.IsDefault||!tags.SequenceEqual(TheatreReplayTags.Normalize(tags)))
                    throw new ArgumentException("Review at most 24 distinct replay tags of at most 40 characters.");
                var document=ReplayAnnotations.ReadReviewed(files.Annotations,context.Duration);
                if(!context.Tags.SequenceEqual(TheatreReplayTags.Normalize(document.Tags)))
                    throw new TheatreEditorStaleException("The reviewed replay tags changed. Refresh before saving.");
                if(context.Tags.SequenceEqual(tags))return context;
                document.Tags=tags.ToList();
                var saved=ReplayAnnotations.SaveReviewed(files.Annotations,document,()=>Validate(context,true));
                return context with { AnnotationRevision=saved.Written.Revision,Tags=tags,
                    Persisted=files with {Annotations=saved.Written},PublicationVerified=saved.Verified,PublicationNotice=saved.Notice };
            }
        }
        private static void RequireEntry(TheatreEditorContext context,TheatreEditorEntry exact)
        {
            if(!context.Events.Concat(context.Highlights).Concat(context.NamedHighlights).Concat(context.Reel).Concat(context.Analytics).Contains(exact))
                throw new TheatreEditorStaleException("The selected replay entry changed. Refresh.");
        }
        private static void SetRange(ReplayTransport transport,uint start,uint end)
        {transport.SetMarkOut(end);transport.SetMarkIn(start);transport.SetMarkOut(end);}
        private static bool OwnerCurrent(TheatreEditorContext context)=>GameplayLeaseCurrent&&DemoPlayback.IsActive
            &&_gameplayScene!.LifetimeGeneration==context.SceneGeneration&&ReferenceEquals(DemoPlayback.PlaybackOwner,context.Owner)
            &&ReferenceEquals(DemoPlayback.Session.Transport,context.Transport)&&DemoPlayback.PlaybackPath==context.PlaybackPath;
        private static void Validate(TheatreEditorContext context,bool requireFrame)
        {
            if(!OwnerCurrent(context)||DemoPlayback.LogicalPath!=context.LogicalPath)throw new TheatreEditorStaleException("The original replay owner changed. Close this editor.");
            if(ReplayVideoExporter.Rendering)throw new InvalidOperationException("Cancel the active video export before editing replay inputs.");
            if(ReplayNativeFileWitness.Source(context.LogicalPath,context.PlaybackPath)!=context.SourceRevision
                ||ReplayReviewedMetadata.Read(context.LogicalPath+ReplayAnnotations.Extension).Revision!=context.AnnotationRevision
                ||ReplayReviewedMetadata.Read(context.LogicalPath+ReplayReels.Extension).Revision!=context.ReelRevision)
                throw new TheatreEditorStaleException("The reviewed source or sidecar changed. Refresh before editing.");
            var transport=(ReplayTransport)context.Transport;
            if(transport.DurationFrames!=context.Duration||context.Persisted is not Files files||!DemoPlayback.Events.SequenceEqual(files.Events))
                throw new TheatreEditorStaleException("Replay duration or recorded event snapshot changed.");
            if(requireFrame&&(!context.SettledPaused||transport.IsSeeking||!(transport.IsPaused||transport.AtEnd)
                ||transport.CurrentFrame!=context.Frame||transport.SeekGeneration!=context.SeekGeneration
                ||transport.ClipIn!=context.MarkIn||transport.ClipOut!=context.MarkOut))
                throw new TheatreEditorStaleException("Playback, seek or marks changed. Pause and refresh before saving.");
        }
        private static TheatreEditorEntry ToEntry(ReplayReelSegment row)=>new(row.Id.ToString("N"),row.StartFrame,row.EndFrame,row.Name,"Saved reel segment",Camera:(int)row.Camera);
        public void Retire(){RetireReview();StopComparisonPreview();}
        private void RetireReview()
        {
            StopStudioAudioPreview();
            RetireWorldComparison();
            _inspection=null;
            if(_published is{} owned&&ReplayCombatDiagnostics.Selected==owned)ReplayCombatDiagnostics.ClearSelection();
            _published=null;
        }
    }
}
#endif
