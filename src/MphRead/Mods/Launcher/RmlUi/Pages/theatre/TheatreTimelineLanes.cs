#if !MPHREAD_SERVER
using System;
using System.Collections.Immutable;
using System.Linq;
using MphRead.Mods.Launcher.Core;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Theatre;

internal enum TimelineLane { Events,Bookmarks,Ranges,Reel,Kills,Damage,Shots,Spawns,Objectives,Audio,Cuts,ExportRange }
internal sealed record TimelineLaneGlyph(TimelineLane Lane,uint First,uint Last,int Count,TheatreEditorEntry? Exact,string Label);

/// <summary>Readonly projections of the existing exact Editor snapshot, bounded to64 native elements per lane.</summary>
internal sealed class TheatreTimelineLanes
{
    internal TheatreEditorContext Context { get; }
    internal TheatreTimelineLanes(TheatreCameraToolsContext camera,TheatreEditorContext editor)
    {
        var pose=camera.Camera;
        if(!ReferenceEquals(camera.Owner,editor.Owner)||!ReferenceEquals(pose.Transport,editor.Transport)
            ||pose.SceneGeneration!=editor.SceneGeneration||pose.LogicalPath!=editor.LogicalPath||pose.PlaybackPath!=editor.PlaybackPath
            ||pose.Frame!=editor.Frame||pose.SeekGeneration!=editor.SeekGeneration||pose.Duration!=editor.Duration
            ||camera.SourceRevision!=editor.SourceRevision||camera.AnnotationRevision!=editor.AnnotationRevision)
            throw new InvalidOperationException("The timeline lanes and reviewed camera do not share the same original replay picture and file witnesses.");
        if(editor.Events.IsDefault||editor.NamedHighlights.IsDefault||editor.Highlights.IsDefault||editor.Reel.IsDefault)
            throw new ArgumentException("The reviewed timeline lanes are unavailable.");
        Context=editor;
    }
    internal ImmutableArray<TimelineLaneGlyph> Project(TimelineWindow window,TimelineLane lane)
    {
        if(window.First>window.Last||window.Last>Context.Duration)throw new ArgumentException("The drawn frame window is invalid.");
        var rows=lane switch
        {
            TimelineLane.Events=>Context.Events.Where(row=>!row.Id.StartsWith("bookmark:",StringComparison.Ordinal)&&!row.Id.StartsWith("named:",StringComparison.Ordinal)),
            TimelineLane.Bookmarks=>Context.Events.Where(row=>row.Id.StartsWith("bookmark:",StringComparison.Ordinal)),
            TimelineLane.Ranges=>Context.NamedHighlights.Concat(Context.Highlights),
            TimelineLane.Reel or TimelineLane.Cuts=>Context.Reel.AsEnumerable(),
            TimelineLane.Kills=>Context.Events.Where(row=>row.Name is "Kill" or "PlayerDeath"),
            TimelineLane.Damage=>Context.Events.Where(row=>row.Name is "Damage" or "Headshot"),
            TimelineLane.Shots=>Context.Events.Where(row=>row.Name=="WeaponFired"),
            TimelineLane.Spawns=>Context.Events.Where(row=>row.Name=="PlayerSpawn"),
            TimelineLane.Objectives=>Context.Events.Where(row=>row.Name is "Objective" or "FlagCapture" or "NodeCapture" or "HardpointCaptured" or "PrimeChanged" or "RelicPickup"),
            TimelineLane.Audio=>Context.Events.Where(row=>row.Name is "WeaponFired" or "PlayerSpawn" or "PlayerDeath" or "Damage"),
            TimelineLane.ExportRange=>Context.MarkIn is uint start&&Context.MarkOut is uint end&&start<end
                ?new[]{new TheatreEditorEntry("marked-export-range",start,end,"Export range","Reviewed In/Out marks")}:Array.Empty<TheatreEditorEntry>(),
            _=>throw new ArgumentException("Unknown timeline lane.")
        };
        return rows.Where(row=>row.EndFrame>=window.First&&row.Frame<=window.Last)
            .GroupBy(row=>(int)Math.Min(63UL,(ulong)(Math.Clamp(row.Frame,window.First,window.Last)-window.First)*64/Math.Max(1u,window.Span)))
            .OrderBy(group=>group.Key).Select(group=>
            {
                var exact=group.OrderBy(row=>row.Frame).ThenBy(row=>row.Id,StringComparer.Ordinal).ToArray();
                return new TimelineLaneGlyph(lane,Math.Max(window.First,exact.Min(row=>row.Frame)),Math.Min(window.Last,exact.Max(row=>row.EndFrame)),
                    exact.Length,exact.Length==1&&lane!=TimelineLane.ExportRange?exact[0]:null,exact.Length==1?exact[0].Name:$"{exact.Length} reviewed entries; zoom to inspect");
            }).ToImmutableArray();
    }
}
#endif
