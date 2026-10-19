using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using MphRead;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Theatre;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using OpenTK.Windowing.GraphicsLibraryFramework;

internal sealed partial class ReplayWindowAcceptance
{
    private readonly List<object> _rml97StudioChecks=new();
    private string _rml97Path="",_rml97Baseline="",_rml97Camera="",_rml97Bundle="",_rml97Wave="";
    private object? _rml97Owner,_rml97Transport;
    private bool _rml97PreviewAvailable;
    private uint _rml97Duration;
    private (double X,double Y) _rml97CurveEnd;
    private void PlanRml97StudioFeatures()
    {
        Do("RML97 pause and seek through the actual existing transport owner",()=>
        {Require(_studioRole&&Page is{IsOpen:true},"RML97 fixture requires actual Studio playback");ReplayController.Seek(0,resume:false);});
        Wait("RML97 exact paused frame zero has a successful world picture",()=>ReplayController.CurrentFrame==0&&ReplayController.IsPaused&&!ReplayController.IsSeeking&&_lastWorldFrame==_observedFrames,45000);
        PlanPersistentStudioQualification();
        Do("RML97 retain foreground owner and bounded reviewed range",()=>
        {
            _rml97Duration=ReplayController.DurationFrames;_rml97Path=DemoPlayback.LogicalPath!;_rml97Owner=DemoPlayback.PlaybackOwner;_rml97Transport=DemoPlayback.Session.Transport;
            ReplayController.SetMarkIn(0);ReplayController.SetMarkOut(30);_rml97Baseline=Rml97WorldHash();
            Activate("studio_refresh");
        });
        Wait("RML97 persistent owners review the exact marked range",()=>Editor?.Snapshot().Context.MarkIn==0&&Editor.Snapshot().Context.MarkOut==30);
        PlanRml98OverviewQualification();
        Do("RML97 native workspace enables four views",()=>Activate("replay_four_views"));
        Wait("RML97 real four-view toggle is enabled",()=>Page?.FourViews==true);
        WaitFrames(6,"RML97 composed four pictures preserve canonical world and foreground clock",()=>
        {Rml97SameWorld();Capture("rml97-four-views");RequireStudioCompositeVariation("four-views");_rml97StudioChecks.Add(new{Feature="four-view-composition",Swaps=6,Frame=ReplayController.CurrentFrame,CanonicalHash=_rml97Baseline,Owner=Id(_rml97Owner)});});
        Do("RML97 native four-view toggle returns to single picture",()=>Activate("replay_four_views"));
        Wait("RML97 single actual replay picture restored",()=>Page?.FourViews==false&&_lastWorldFrame==_observedFrames);
        OpenCamera();
        Do("RML97 save one actual presented camera key in isolated recording",()=>Activate("replay_camera_add"));
        Wait("RML97 actual camera owner saved the first key",()=>ReplayCamera.Track.Keys.Count>0);
        Do("RML97 refresh persistent camera tools after saved pose",()=>Activate("studio_refresh"));
        Wait("RML97 actual camera tools have current paused review",()=>TryTools()?.Snapshot().Current==true);
        string graphDiagnostic="";int graphDiagnosticCount=0;
        Do("RML98 expose the entire authored FOV graph through its native Camera Graph control",()=>Activate("studio_camera_graph"));
        Wait("RML98 actual FOV graph lane is wholly presented inside the inspector clip",()=>
        {
            var ticket=Read<CameraCurveTicket>(Page,"_presentedCurves");
            if(graphDiagnosticCount<16)
            {
                object Bounds(string id)
                {
                    bool found=Host!.TryGetElementBounds(Page!.Document,id,out float x,out float y,out float width,out float height);
                    return new{Found=found,X=x,Y=y,Width=width,Height=height};
                }
                var tools=TryTools()?.Snapshot();
                var state=new{Lane=Bounds("replay_camera_curve_lane_3"),Inspector=Bounds("studio_inspector_scroll"),
                    TicketPresent=ticket!=null,Clip=ticket?.Clip,Lanes=ticket==null||ticket.Lanes.IsDefault?null:ticket.Lanes.ToArray(),
                    Controls=ticket?.Controls.Where(c=>c.Id.StartsWith("replay_camera_curve_key_3_",StringComparison.Ordinal)).Select(c=>new{c.Id,c.Box,c.Lane}).ToArray(),
                    ToolsCurrent=tools?.Current,ToolsError=tools?.Error,Focus=Host!.FocusedElement()};
                string json=JsonSerializer.Serialize(state);
                if(json!=graphDiagnostic){graphDiagnostic=json;graphDiagnosticCount++;File.AppendAllText(Path.Combine(_output,"persistent-graph-lane.jsonl"),
                    JsonSerializer.Serialize(new{AtMs=_elapsed.ElapsedMilliseconds,Change=graphDiagnosticCount,State=state})+"\n");}
            }
            return ticket!=null&&!ticket.Lanes.IsDefault&&ticket.Lanes.Any(l=>l.Channel==3&&l.Box.Inside(ticket.Clip));
        });
        Do("RML97 focus authored FOV graph marker",()=>Focus("replay_camera_curve_key_3_0"));
        Wait("RML97 exact native FOV marker was successfully presented",()=>
        {
            var ticket=Read<CameraCurveTicket>(Page,"_presentedCurves");
            return ticket!=null&&ticket.Controls.Any(c=>c.Id=="replay_camera_curve_key_3_0"&&c.Box.Inside(c.Lane)&&c.Box.Inside(ticket.Clip));
        });
        Do("RML97 drag actual graph marker through production native pointer mapping",()=>
        {
            _rml97Camera=Hash(_rml97Path+".camera");var ticket=Read<CameraCurveTicket>(Page,"_presentedCurves")!;
            var marker=ticket.Controls.First(c=>c.Id=="replay_camera_curve_key_3_0");
            double x=(marker.Box.X+marker.Box.Width/2)*ticket.Metrics.ClientWidth/ticket.Metrics.FramebufferWidth;
            double y=(marker.Box.Y+marker.Box.Height/2)*ticket.Metrics.ClientHeight/ticket.Metrics.FramebufferHeight;
            _rml97CurveEnd=(x,y-8);RmlUiOpenGlSmoke.PointerButton(MouseButton.Left,x,y,true);RmlUiOpenGlSmoke.PointerMoved(x,y-8);
        });
        Wait("RML97 native graph drag preview exists without publication",()=>
        {if(Read<object>(Page,"_curveDrag")==null)return false;Require(Hash(_rml97Path+".camera")==_rml97Camera&&Tools.Snapshot().Pending==null,"actual graph preview has not written or staged metadata");return true;});
        Do("RML97 release actual graph drag into reviewed confirmation",()=>RmlUiOpenGlSmoke.PointerButton(MouseButton.Left,_rml97CurveEnd.X,_rml97CurveEnd.Y,false));
        Wait("RML97 actual graph drag stages a curve edit",()=>Tools.Snapshot().Pending is{Kind:TheatreCameraToolsChange.Curve});
        Do("RML97 cancel exact graph edit through its actual native button",()=>{Capture("rml97-curve-review");Activate("replay_camera_tools_confirm_cancel");});
        Wait("RML97 graph cancellation preserves exact camera sidecar",()=>
        {if(Tools.Snapshot().Pending!=null)return false;Require(Hash(_rml97Path+".camera")==_rml97Camera,"cancelled actual curve drag preserved bytes");_rml97StudioChecks.Add(new{Feature="actual-graph-drag-cancel",CameraSha256=_rml97Camera});return true;});
        Do("RML97 switch persistent inspector from Camera to Markers",()=>Activate("studio_tab_markers"));
        Wait("RML97 actual workspace keeps playback controls in foreground",()=>StudioWorkspaceForeground());
        Wait("RML97 actual editor owns a current review",()=>Editor?.Snapshot().Live.Current==true);
        Do("RML97 open spatial analysis through its real native action",()=>Activate("studio_tab_analysis"));
        Do("RML97 analyze actual recorded positions",()=>Activate("replay_spatial_analyze"));
        Wait("RML97 detached actual spatial analysis publishes",()=>
        {
            string error=Read<string>(Page,"_spatialError")??"";Require(error.Length==0,"actual spatial analysis error: "+error);
            var analysis=Read<TheatreSpatialAnalysis>(Page,"_spatialAnalysis");if(analysis==null)return false;
            Require(analysis.Duration==ReplayController.DurationFrames&&Read<Task<TheatreSpatialAnalysis>>(Page,"_spatialTask")==null,"actual bounded analysis matches recording duration");
            _rml97StudioChecks.Add(new{Feature="actual-recorded-spatial-analysis",analysis.Duration,Samples=analysis.Samples.Length});return true;
        },45000);
        Do("RML97 enable real render-only world heatmap markers",()=>Activate("replay_spatial_world"));
        WaitFrames(4,"RML97 world spatial overlays preserve canonical state",()=>
        {
            Rml97SameWorld();Capture("rml97-spatial-world-markers");
            var analysis=Read<TheatreSpatialAnalysis>(Page,"_spatialAnalysis")!;
            _rml97StudioChecks.Add(new{Feature="legacy-advanced-spatial-and-render-only-overlay",Kinds=Enum.GetValues<TheatreSpatialKind>().Length,
                PresentKinds=analysis.Samples.Select(p=>p.Kind.ToString()).Distinct().ToArray(),Claim="actual recording categories only; empty categories are not fabricated"});
        });
        Do("RML97 disable real world markers",()=>Activate("replay_spatial_world"));
        Do("RML97 filter actual heatmap kind, player and reviewed range",()=>{Activate("replay_spatial_kind");Activate("replay_spatial_player");Activate("replay_spatial_range");});
        WaitFrames(3,"RML97 native heatmap filters preserve foreground world",()=>{Rml97SameWorld();Capture("rml97-spatial-filtered");});
        Do("RML97 request second spatial analysis for cancellation control",()=>Activate("replay_spatial_analyze"));
        Do("RML97 cancel spatial worker if still running",()=>
        {
            bool running=Read<Task<TheatreSpatialAnalysis>>(Page,"_spatialTask")!=null;
            if(running)Activate("replay_spatial_cancel");
            _rml97StudioChecks.Add(new{Feature="spatial-cancel-control",RunningWhenOffered=running,Claim=running?"actual cancel action requested":"recording completed before cancellation could be offered"});
        });
        Wait("RML97 spatial worker completion or cancellation retired",()=>Read<Task<TheatreSpatialAnalysis>>(Page,"_spatialTask")==null);
        Do("RML97 return from Analysis to persistent Markers inspector",()=>Activate("studio_tab_markers"));
        Do("RML97 author actual reel entries for portable rebind qualification",()=>Activate("replay_editor_add_top"));
        Wait("RML97 actual reel confirmation is current",()=>Read<object>(Page,"_editorConfirmation") is RmlUiDocumentToken token&&token!=default);
        Do("RML97 confirm bounded real highlight reel",()=>Activate("replay_editor_confirm_apply"));
        Wait("RML97 saved reel refreshes exact editor context",()=>Editor?.Snapshot().Live.Current==true&&Editor.Snapshot().Context.Reel.Length>0);
        Do("RML97 open actual audio/comparison/bundle tools",()=>Activate("studio_tab_audio"));
        Do("RML97 refresh actual persistent audio context after camera and reel changes",()=>Activate("studio_refresh"));
        Do("RML97 prepare isolated authored PCM and bounded actual mixer options",()=>
        {
            _rml97Wave=Path.Combine(_output,"authored-rml97-overlay.wav");
            var pcm=new StudioPcmAudio(48000,1,Enumerable.Repeat(.25f,48000).ToArray());
            StudioOfflineAudio.WriteWave(_rml97Wave,0,60,60,new[]{new StudioAudioClip(0,StudioAudioBus.Replay,pcm)});
            Host!.SetField(Host.CurrentInputDocument,"replay_studio_overlay",_rml97Wave);
            Activate("replay_studio_game_events"); // Authored PCM qualification does not claim extracted-game cue coverage.
        });
        PlanRml97CustomAudio();
        Do("RML97 run actual bounded offline audio mix",()=>Activate("replay_studio_run"));
        Wait("RML97 real offline mixer produced nonzero bounded WAV",()=>
        {
            string path=Rml97ToolOutput();if(path.Length==0||!File.Exists(path))return false;
            var pcm=StudioPcmAudio.ReadWave(path);Require(pcm.Frames==24000&&pcm.Channels==2&&pcm.Samples.Any(s=>Math.Abs(s)>.1f),"reviewed 30-frame authored mix rendered real nonzero PCM");
            VerifyRml97CustomAudioWave(pcm);
            _rml97StudioChecks.Add(new{Feature="actual-offline-audio-mix",pcm.Frames,pcm.SampleRate,pcm.Channels,Output=path,Scope="authored overlay plus persisted custom recorded-event cue; canonical game-event cues disabled"});return true;
        },45000);
        Do("RML97 preview actual reviewed mix when the shared device is available",()=>
        {
            _rml97PreviewAvailable=MusicPlayer.Available;
            _rml97StudioChecks.Add(new{Feature="shared-preview-device-gate",Available=_rml97PreviewAvailable});
            if(_rml97PreviewAvailable)Activate("replay_studio_preview");
        });
        Wait("RML97 actual shared-device preview acquired or device unavailable",()=>!_rml97PreviewAvailable||Read<object>(Read<object>(Page,"_editorBackend"),"_studioPreview")!=null,45000);
        Do("RML97 shared preview source includes persisted custom event cue",()=>
        {
            if(_rml97PreviewAvailable)VerifyRml97CustomAudioWave(StudioPcmAudio.ReadWave(Rml97ToolOutput()));
        });
        Do("RML97 stop actual shared-device preview if acquired",()=>{if(_rml97PreviewAvailable)Activate("replay_studio_stop_preview");});
        Wait("RML97 shared preview component retired",()=>!_rml97PreviewAvailable||Read<object>(Read<object>(Page,"_editorBackend"),"_studioPreview")==null);
        PlanRml97RemoveCustomAudio();
        PlanRml97ToolOperation(TheatreStudioTool.CompareWorld);
        Do("RML97 choose existing private clone for actual same-map world comparison",()=>
        {Host!.SetField(Host.CurrentInputDocument,"replay_studio_input",_replay);Activate("replay_studio_run");});
        Wait("RML97 actual private world comparison produces equal canonical worlds",()=>
        {
            string path=Rml97ToolOutput();if(Path.GetFileName(path)!="world-comparison.json"||!File.Exists(path))return false;
            using var report=JsonDocument.Parse(File.ReadAllText(path));var data=report.RootElement;
            Require(data.GetProperty("GameplayEqual").GetBoolean()&&data.GetProperty("PresentationEqual").GetBoolean()&&data.GetProperty("FullGraphEqual").GetBoolean(),"same recording's independent actual worlds compare equal");
            Rml97SameWorld();_rml97StudioChecks.Add(new{Feature="actual-private-world-comparison",Output=path,Gameplay=true,Presentation=true,FullGraph=true});return true;
        },60000);
        PlanRml98ComparisonPreviewQualification();
        PlanRml97ToolOperation(TheatreStudioTool.PortableBundle);
        Do("RML97 run actual old-format portable export",()=>Activate("replay_studio_run"));
        Wait("RML97 actual portable archive has fixed old-format members",()=>
        {
            string path=Rml97ToolOutput();if(Path.GetFileName(path)!=RmlUiReplayFileNames.PortableBundleName||!File.Exists(path))return false;
            using var archive=ZipFile.OpenRead(path);var names=archive.Entries.Select(e=>e.FullName).ToArray();
            Require(names.Contains("replay.ppdemo")&&names.Contains("manifest.json")&&!names.Any(n=>n.EndsWith(".camera",StringComparison.Ordinal)),"actual portable format uses fixed recording and manifest camera state");
            using var stream=archive.GetEntry("manifest.json")!.Open();using var manifest=JsonDocument.Parse(stream);
            Require(manifest.RootElement.GetProperty("CameraState").GetBytesFromBase64().Length>0,"portable manifest carries real path-free camera authoring state");
            _rml97Bundle=path;_rml97StudioChecks.Add(new{Feature="actual-portable-export",Output=path,Members=names});return true;
        },45000);
        PlanRml97ToolOperation(TheatreStudioTool.ImportPortable);
        Do("RML97 verify and privately import actual exported portable bundle",()=>{Host!.SetField(Host.CurrentInputDocument,"replay_studio_input",_rml97Bundle);Activate("replay_studio_run");});
        Wait("RML97 actual portable import rebinds isolated source and camera",()=>
        {
            string path=Rml97ToolOutput();if(Path.GetExtension(path)!=".ppdemo"||!File.Exists(path))return false;
            Require(Hash(path)==_sourceHash,"imported packet bytes match original source");var camera=new ReplayCameraTrack();Require(camera.Load(path)&&camera.Keys.Count>0,"actual imported camera state rebinds to new replay identity");
            Rml97SameWorld();_rml97StudioChecks.Add(new{Feature="actual-portable-import",Output=path,CameraKeys=camera.Keys.Count});return true;
        },45000);
        PlanDetachedExportAdmission();
        Do("RML97 return persistent workspace to Camera",()=>Activate("studio_tab_camera"));
        Wait("RML97 original playback controls remain available for normal library teardown",()=>Page!=null&&Host!.CurrentInputDocument==Page.Document);
    }
    private void PlanRml97LibraryPromotion()
    {
        string[] before=Array.Empty<string>();object? owner=null;string promoted="",bundleHash="";int promotedSlot=-1,cameraKeys=0,reelSegments=0;
        Do("RML97 submit portable bundle through actual Library import",()=>
        {
            Require(!DemoPlayback.IsActive&&!_window!.HasScene&&Page==null,"bundle promotion begins only after normal replay teardown");
            before=Directory.GetFiles(DemoLibrary.Directory,"imported_*.ppdemo");owner=ReadStatic<RmlUiMenuController>("_controller");
            bundleHash=Hash(_rml97Bundle);Host!.SetField(Host.CurrentInputDocument,"theatre_import_path",_rml97Bundle);
            Activate("theatre_import_path_apply");
        });
        Wait("RML97 native Library publishes promoted recording with rebound sidecars",()=>
        {
            var menu=ReadStatic<RmlUiMenuController>("_controller");if(menu==null||menu.Busy)return false;
            var added=Directory.GetFiles(DemoLibrary.Directory,"imported_*.ppdemo").Except(before).ToArray();
            Require(added.Length==1,"actual library bundle import publishes exactly one fresh top-level recording: "+LibraryFacts());promoted=added[0];
            Require(Hash(promoted)==_sourceHash&&Hash(_rml97Bundle)==bundleHash&&Hash(_rml97Path)==_sourceHash,"promotion preserves packet source and portable archive");
            var camera=new ReplayCameraTrack();Require(camera.Load(promoted)&&camera.Keys.Count>0,"library promotion rebinds actual camera identity");
            var reel=ReplayReels.ReadReviewed(ReplayReviewedMetadata.Read(promoted+ReplayReels.Extension),promoted,_rml97Duration);
            Require(reel.Segments.Count>0&&Path.GetFullPath(reel.SourceReplay)==Path.GetFullPath(promoted),"actual saved reel binds to promoted source");
            Require(ReferenceEquals(owner,menu)&&!DemoPlayback.IsActive&&!_window!.HasScene,"library promotion retains menu ownership and creates no foreground clock");
            var library=Read<RmlUiTheatreIndex>(menu,"_theatre")!;int slot=Enumerable.Range(0,RmlUiTheatreIndex.PageSize).FirstOrDefault(i=>library.Entry(i)?.Path==promoted,-1);
            Require(slot>=0,"fresh promoted replay is selectable in actual top-level library");
            promotedSlot=slot;cameraKeys=camera.Keys.Count;reelSegments=reel.Segments.Count;return true;
        },45000);
        Do("RML97 select promoted row through actual native Library input",()=>
        {
            var menu=ReadStatic<RmlUiMenuController>("_controller");var library=Read<RmlUiTheatreIndex>(menu,"_theatre")!;
            Require(library.Entry(promotedSlot)?.Path==promoted,"promoted native row identity remains current before activation");
            Activate("theatre_entry"+promotedSlot);
        });
        Wait("RML97 native Library processes promoted-row selection",()=>
        {
            var menu=ReadStatic<RmlUiMenuController>("_controller");var library=Read<RmlUiTheatreIndex>(menu,"_theatre");
            if(library?.Selected?.Path!=promoted)return false;
            Require(ReferenceEquals(owner,menu)&&!DemoPlayback.IsActive&&!_window!.HasScene,"promoted row selection preserves original Library owner");
            _rml97StudioChecks.Add(new{Feature="actual-library-portable-promotion",Output=promoted,CameraKeys=cameraKeys,ReelSegments=reelSegments});
            Capture("rml97-library-promoted-bundle");return true;
        });
    }
    private void PlanRml97ToolOperation(TheatreStudioTool operation)
    {
        Do("RML97 open Audio / Tools before selecting "+operation,()=>Activate("studio_tab_audio"));
        Wait("RML97 select actual Studio operation "+operation,()=>
        {
            var actual=(TheatreStudioTool)Page!.GetType().GetField("_studioTool",Private)!.GetValue(Page)!;
            if(actual==operation)return true;Activate("studio_operation_"+operation);return false;
        });
    }
    private string Rml97ToolOutput()
    {
        string error=(string)Page!.GetType().GetField("_studioToolsError",Private)!.GetValue(Page)!;
        if(error.Length>0)throw new InvalidOperationException("Actual RML97 Studio tool failed: "+error);
        return (string)Page.GetType().GetField("_studioToolsOutput",Private)!.GetValue(Page)!;
    }
    private string Rml97WorldHash()=>ReplayStateHash.Compute(DemoPlayback.PresentationScene!,ReplayController.CurrentFrame);
    private void Rml97SameWorld()
    {
        Require(ReferenceEquals(DemoPlayback.PlaybackOwner,_rml97Owner)&&ReferenceEquals(DemoPlayback.Session.Transport,_rml97Transport)
            &&ReplayController.CurrentFrame==0&&ReplayController.IsPaused&&!ReplayController.IsSeeking&&Rml97WorldHash()==_rml97Baseline,"actual Studio tools preserve original canonical world/foreground clock");
    }
}
