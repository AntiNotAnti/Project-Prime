using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using MphRead;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Settings;
using MphRead.Mods.Launcher.RmlUi.Pages.Theatre;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using OpenTK.Windowing.GraphicsLibraryFramework;

internal sealed partial class ReplayWindowAcceptance
{
    private bool StudioWorkspaceForeground()
        => HeaderWindowFocused() && Page is { IsOpen: true } page && Host is { Active: true } host
            && host.CurrentInputDocument == page.Document
            && Read<RmlUiPageManager>(page, "_pages") is { PageKey: "studio-workspace", ModalCount: 0 } pages
            && pages.Top == page.Document && _lastWorldFrame == _observedFrames;

    private TimelineBox StudioBounds(string id)
    {
        Require(Host!.TryGetElementBounds(Page!.Document, id, out float x, out float y, out float width, out float height)
            && width > 0 && height > 0, "actual persistent Studio element bounds: " + id);
        return new(x, y, width, height);
    }

    private void RequireStudioLayout(string stage)
    {
        Require(StudioWorkspaceForeground(), "persistent Studio owns actual current world and native document: " + stage);
        var framebuffer = new TimelineBox(0, 0, _window!.FramebufferSize.X, _window.FramebufferSize.Y);
        var transport = StudioBounds("studio_transport");
        var timeline = StudioBounds("studio_timeline");
        var inspector = StudioBounds("studio_inspector");
        var viewport = StudioBounds("replay_viewport");
        var tabs = StudioBounds("studio_tabs");
        foreach (var box in new[] { transport, timeline, inspector, viewport, tabs })
            Require(box.Inside(framebuffer), "actual Studio transport, timeline, inspector and viewport fit the same framebuffer: " + stage);
        Require(viewport.Width >= 80 && viewport.Height >= 60 && inspector.Width >= 120 && inspector.Height >= 60
            && viewport.X + viewport.Width <= inspector.X + 2
            && transport.Y + transport.Height <= viewport.Y + 2
            && viewport.Y + viewport.Height <= timeline.Y + 2,
            "persistent workspace keeps usable transport, world picture, inspector and timeline visible together: " + stage);
        string selectedTab = Read<string>(Page, "_workspaceTab") ?? "camera";
        TimelineBox selectedButton = default;
        foreach (string tab in new[] { "camera", "markers", "combat", "analysis", "audio", "export" })
        {
            var button = StudioBounds("studio_tab_" + tab);
            Require(button.Width >= 20 && button.Height >= 15, "actual Studio tab retains a usable hit area: " + tab);
            // This is an authored horizontal overflow strip. At 360dp its six
            // minimum-width buttons cannot all fit simultaneously. The real
            // native focus path scrolls the activated button into view; the
            // qualification below visits every tab through that path.
            if (tab == selectedTab)
            {
                selectedButton = button;
                Require(button.Inside(tabs) && button.Inside(inspector),
                    "actual selected tab is fully visible in its scrolled inspector strip: " + tab);
            }
        }
        _rml97StudioChecks.Add(new { Feature = "persistent-workspace-visible-layout", Stage = stage,
            Document = Page!.Document, Transport = transport, Timeline = timeline, Inspector = inspector, Viewport = viewport,
            Tabs = tabs, SelectedTab = selectedTab, SelectedButton = selectedButton });
    }

    private void PlanPersistentStudioQualification()
    {
        TheatrePlaybackPagePresenter? page = null;
        object? owner = null, transport = null, editor = null;
        RmlUiDocumentToken document = default;
        string path = "", source = "";
        Dictionary<string, string> sidecars = new(StringComparer.Ordinal);
        TheatreWorkspaceCameraValues? original = null, changed = null;
        uint expectedFrame = 0;
        long numericPicture = 0,numericDiagnosticAt=-1000;
        (double X, double Y) start = default, end = default;
        string emptyLane = "";
        string savedViewPath = "", savedViewSource = "";
        byte[]? priorSavedView = null;
        string lastViewSaveDiagnostic = ""; int viewSaveDiagnosticCount = 0;
        string lastReviewDiagnostic = ""; int reviewDiagnosticCount = 0;

        DoHeader("retain exact persistent Studio document and source witnesses", () =>
        {
            Require(StudioWorkspaceForeground(), "actual Studio playback opens the persistent workspace");
            page = Page; document = page!.Document; owner = DemoPlayback.PlaybackOwner;
            transport = DemoPlayback.Session.Transport; editor = Editor;
            path = DemoPlayback.LogicalPath!; source = Hash(path); sidecars = Sidecars();
            RequireStudioLayout("initial-camera");
        });
        foreach (string tab in new[] { "markers", "combat", "analysis", "audio", "export", "camera" })
        {
            DoHeader("activate actual persistent Studio " + tab + " tab", () => Activate("studio_tab_" + tab));
            WaitHeaderFrames(2, "actual " + tab + " inspector retains all workspace regions", () =>
            {
                Require(Read<string>(Page, "_workspaceTab") == tab, "authored tab selects its exact inspector");
                RequireStudioLayout(tab); SameSource();
            });
        }
        DoHeader("open full Settings through actual Studio transport action", () => Activate("studio_settings"));
        Wait("actual Settings temporarily owns native foreground over retained Studio workspace", () =>
        {
            if (!HeaderWindowFocused() || ReadStatic<NativeFullSettingsPage>("_fullSettings") is not { OwnsForeground: true }) return false;
            Require(ReferenceEquals(page, Page) && Page!.Document == document
                && ReferenceEquals(owner, DemoPlayback.PlaybackOwner) && ReferenceEquals(transport, DemoPlayback.Session.Transport),
                "opening actual Settings retains exact workspace/player/transport");
            SameSource(); Capture("persistent-studio-settings"); return true;
        }, 12000);
        DoHeader("leave clean actual Settings with native Back", () =>
        {
            var settings=ReadStatic<NativeFullSettingsPage>("_fullSettings");
            Require(settings is {OwnsForeground:true}&&Host!.CurrentInputDocument==settings.Document,
                "full Settings owns the exact native Back action document");
            PressSettingsSemanticButton("BACK");
        });
        Wait("Settings returns to exact persistent Studio workspace", () =>
            ReadStatic<NativeFullSettingsPage>("_fullSettings") == null && StudioWorkspaceForeground(), 12000);
        WaitHeaderFrames(2, "actual Settings return paints two settled final composites", () => { });
        DoHeader("actual Settings return preserves exact workspace owners and geometry", () =>
        {
            Require(ReferenceEquals(page, Page) && Page!.Document == document && ReferenceEquals(editor, Editor),
                "actual Settings return restores the same workspace document and editor controller");
            RequireStudioLayout("settings-return"); SameSource(); Capture("persistent-studio-workspace"); RequireStudioCompositeVariation("settings-return");
        });
        DoHeader("stage a finite numeric camera transaction through actual native fields", () =>
        {
            var backend = Read<ITheatreCameraBackend>(Page, "_cameraBackend")!;
            var context = backend.Capture(); numericPicture = context.PresentationRevision;
            original = ((ITheatreWorkspaceCameraBackend)backend).ReadDirectCamera(context);
            changed = original with { X = original.X + .25f, Fov = Math.Clamp(original.Fov + 3, 20, 140) };
            SetCameraFields(changed); RecordNumericCamera("staged-before-native-apply");Activate("studio_camera_apply");
            RecordNumericCamera("native-apply-queued");
        });
        Wait("actual direct numeric camera transaction publishes only the reviewed preview pose", () =>
        {
            if(_elapsed.ElapsedMilliseconds-numericDiagnosticAt>=250){numericDiagnosticAt=_elapsed.ElapsedMilliseconds;RecordNumericCamera("await-actual-preview");}
            if (!StudioWorkspaceForeground() || !(Read<string>(Page, "_workspaceCameraStatus") ?? "").StartsWith("Preview camera applied.", StringComparison.Ordinal)) return false;
            var backend = Read<ITheatreCameraBackend>(Page, "_cameraBackend")!;
            var context = backend.Capture();
            var actual = ((ITheatreWorkspaceCameraBackend)backend).ReadDirectCamera(context);
            if (context.PresentationRevision <= numericPicture || Math.Abs(actual.X - changed!.X) > .002f || Math.Abs(actual.Fov - changed.Fov) > .002f) return false;
            SameSource(); _rml97StudioChecks.Add(new { Feature = "actual-numeric-camera-preview-transaction", Before = original, Expected = changed, Actual = actual, SidecarsUnchanged = true });
            return true;
        }, 12000);
        DoHeader("restore exact original numeric camera through actual Apply", () => { SetCameraFields(original!); Activate("studio_camera_apply"); });
        Wait("restored numeric preview has a successful actual world picture", () =>
        {
            if (!StudioWorkspaceForeground()) return false;
            var backend = Read<ITheatreCameraBackend>(Page, "_cameraBackend")!;
            var actual = ((ITheatreWorkspaceCameraBackend)backend).ReadDirectCamera(backend.Capture());
            if (Math.Abs(actual.X - original!.X) > .002f || Math.Abs(actual.Fov - original.Fov) > .002f) return false;
            SameSource(); return true;
        }, 12000);
        DoHeader("explicitly refresh reviewed tools after numeric camera preview restore", () =>
        {
            Require(TryTools()?.Snapshot().Pending==null&&Editor?.Snapshot().Pending==null,
                "explicit refresh has no pending camera or editor mutation to discard");
            Activate("studio_refresh");
        });
        DoHeader("focus persistent timeline without changing its actual empty tracks", () => Focus("replay_timeline_surface"));
        Wait("actual persistent timeline is presented before empty-track pointer gesture", ReadyTimeline, 12000);
        DoHeader("begin actual production pointer gesture on an empty Studio track", () =>
        {
            var lanes = Read<TheatreTimelineLanes>(Page, "_timelineLanes")!;
            var ticket = Ticket!;
            var panel = StudioBounds("replay_timeline_panel");
            TimelineLane lane = new[] { TimelineLane.Audio, TimelineLane.Cuts, TimelineLane.Reel, TimelineLane.Ranges }
                .First(candidate => lanes.Project(ticket.Window, candidate).Length == 0
                    && StudioBounds("replay_timeline_" + candidate.ToString().ToLowerInvariant()).Inside(panel));
            emptyLane = lane.ToString().ToLowerInvariant();
            var box = StudioBounds("replay_timeline_" + emptyLane);
            var surface = StudioBounds("replay_timeline_surface");
            Require(box.Inside(surface) && box.Inside(StudioBounds("replay_timeline_panel")), "empty actual track is inside the presented visible timeline");
            double x = box.X + box.Width * .37, y = box.Y + box.Height / 2;
            double finalX = Math.Min(box.X + box.Width - 3, x + 18);
            expectedFrame = ticket.Window.Frame(finalX, surface);
            start = (x * ticket.Metrics.ClientWidth / ticket.Metrics.FramebufferWidth, y * ticket.Metrics.ClientHeight / ticket.Metrics.FramebufferHeight);
            end = (finalX * ticket.Metrics.ClientWidth / ticket.Metrics.FramebufferWidth, start.Y);
            Require(RmlUiOpenGlSmoke.PointerMoved(start.X, start.Y)
                && RmlUiOpenGlSmoke.PointerButton(MouseButton.Left, start.X, start.Y, true), "production pointer accepts visible empty-track press");
            Require(Read<object>(Page, "_workspaceScrub") != null, "actual empty track acquires the workspace scrub transaction");
            RmlUiOpenGlSmoke.PointerMoved(end.X, end.Y);
        });
        WaitHeaderFrames(2, "held empty-track gesture changes no recording or sidecar", () => SameSource());
        DoHeader("release actual empty-track gesture through production pointer mapping", () =>
            RmlUiOpenGlSmoke.PointerButton(MouseButton.Left, end.X, end.Y, false));
        Wait("empty-track release seeks exactly the reviewed frame on the same transport", () =>
        {
            if (!StudioWorkspaceForeground() || ReplayController.IsSeeking || ReplayController.CurrentFrame != expectedFrame) return false;
            Require(Read<object>(Page, "_workspaceScrub") == null, "released empty-track gesture retires its transaction");
            SameSource(acceptSettledSeekTransport: true); Capture("persistent-studio-empty-track-seek");
            _rml97StudioChecks.Add(new { Feature = "actual-empty-track-production-pointer-seek", Lane = emptyLane, Frame = expectedFrame, Input = "production-pointer", SidecarsUnchanged = true });
            return true;
        }, 45000);
        DoHeader("save actual recording view through native control after staged seek", () =>
        {
            SameSource();
            savedViewPath = path + ReplayStudioPresentationPreferencesStore.Extension;
            priorSavedView = File.Exists(savedViewPath) ? File.ReadAllBytes(savedViewPath) : null;
            var witness = typeof(RmlUiOpenGlSmoke).GetNestedType("ReplayNativeFileWitness", System.Reflection.BindingFlags.NonPublic)!;
            savedViewSource = (string)witness.GetMethod("Source", StaticPrivate)!.Invoke(null, new object[] { path, DemoPlayback.PlaybackPath! })!;
            Activate("studio_tab_camera");
            Focus("studio_save_view");
            var saveBounds = StudioBounds("studio_save_view");
            Require(saveBounds.Inside(StudioBounds("studio_inspector_scroll")), "actual Save View control is visible in its camera inspector");
            Activate("studio_save_view");
        });
        Wait("native Save View publishes reviewed preferences under the current post-seek owner", () =>
        {
            RecordViewSaveDiagnostic();
            if (!StudioWorkspaceForeground() || !File.Exists(savedViewPath)) return false;
            string status = Read<string>(Page, "_workspaceProblem") ?? "";
            if (!status.StartsWith("Saved this recording's camera", StringComparison.Ordinal)) return false;
            var review = ReplayStudioPresentationPreferencesStore.Read(path, savedViewSource);
            Require(review.Preferences != null && review.Reviewed.Bytes != null, "actual saved view is accepted by the production source-bound preferences store");
            var current = Sidecars();
            Require(current.Where(pair => pair.Key != savedViewPath).Count() == sidecars.Where(pair => pair.Key != savedViewPath).Count()
                && sidecars.Where(pair => pair.Key != savedViewPath).All(pair => current.TryGetValue(pair.Key, out var hash) && hash == pair.Value),
                "native Save View changes only its presentation preference sidecar");
            // This isolated fixture restores only the explicitly saved preference file.
            if (priorSavedView == null) File.Delete(savedViewPath); else File.WriteAllBytes(savedViewPath, priorSavedView);
            sidecars = Sidecars();
            SameSource();
            _rml97StudioChecks.Add(new { Feature = "actual-post-seek-save-view", Frame = ReplayController.CurrentFrame,
                Reviewed = true, SameForegroundOwner = true, RecordingUnchanged = true, OtherMetadataUnchanged = true, FixturePreferenceRestored = true });
            return true;
        }, 15000);
        DoHeader("restore actual workspace playhead to zero through transport Go", () =>
        { Host!.SetField(document, "studio_frame", "0"); Activate("studio_seek"); });
        Wait("persistent workspace qualification restores paused frame zero", () =>
            StudioWorkspaceForeground() && ReplayController.CurrentFrame == 0 && !ReplayController.IsSeeking && ReplayController.IsPaused, 45000);
        DoHeader("refresh all persistent owners after numeric preview and empty-track seek", () => Activate("studio_refresh"));
        Wait("persistent workspace retains current camera editor and timeline review", () =>
        {
            var camera = Camera?.Snapshot(); var tools = TryTools()?.Snapshot(); var editorState = Editor?.Snapshot();
            if (reviewDiagnosticCount < 32)
            {
                int Id(object? value) => value == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
                object? Context(TheatreCameraContext? value) => value == null ? null : new
                { value.SceneGeneration, Transport = Id(value.Transport), Scene = Id(value.PresentationScene), value.SeekGeneration,
                    value.Frame, value.SettledPaused, value.PresentationRevision, value.TrackPlaying };
                var state = new
                {
                    Foreground = StudioWorkspaceForeground(), Frame = ReplayController.CurrentFrame, Seeking = ReplayController.IsSeeking,
                    Paused = ReplayController.IsPaused, CurrentTransport = Id(DemoPlayback.Session.Transport), CurrentScene = Id(DemoPlayback.PresentationScene),
                    Camera = camera == null ? null : new { camera.Current, camera.Error, camera.Status, Context = Context(camera.Context) },
                    Tools = tools == null ? null : new { tools.Current, tools.Error, tools.Status, Context = Context(tools.Context.Camera) },
                    Editor = editorState == null ? null : new { editorState.Live.Current, editorState.Error, editorState.Status, editorState.Live.Diagnostic,
                        editorState.Context.Frame, editorState.Context.SeekGeneration, editorState.Context.SettledPaused,
                        Transport = Id(editorState.Context.Transport), Owner = Id(editorState.Context.Owner) }
                };
                string serialized = JsonSerializer.Serialize(state);
                if (serialized != lastReviewDiagnostic)
                {
                    lastReviewDiagnostic = serialized; reviewDiagnosticCount++;
                    File.AppendAllText(Path.Combine(_output, "persistent-review-refresh.jsonl"),
                        JsonSerializer.Serialize(new { AtMs = _elapsed.ElapsedMilliseconds, Change = reviewDiagnosticCount, State = state }) + "\n");
                }
            }
            return StudioWorkspaceForeground() && camera?.Current == true && tools?.Current == true && editorState?.Live.Current == true;
        }, 12000);

        void RecordViewSaveDiagnostic()
        {
            if (viewSaveDiagnosticCount >= 32) return;
            object? Field(object? value, string name) => value?.GetType().GetField(name, Private)?.GetValue(value);
            int Id(object? value) => value == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
            var preferenceOwner = ReadStatic<object>("_studioPreferenceOwner");
            object? read = Field(preferenceOwner, "Read"), save = Field(preferenceOwner, "Save");
            string problem = Read<string>(Page, "_workspaceProblem") ?? "";
            string problemKind = problem.StartsWith("Saved this recording's camera", StringComparison.Ordinal) ? "saved"
                : problem.StartsWith("Saving this recording's view", StringComparison.Ordinal) ? "saving"
                : problem.StartsWith("Loading this recording's saved view", StringComparison.Ordinal) ? "loading"
                : problem.Length == 0 ? "empty" : "other";
            var state = new
            {
                Owner = Id(preferenceOwner), OwnerPresent = preferenceOwner != null,
                OwnerPage = Id(Field(preferenceOwner, "Page")), CurrentPage = Id(Page),
                OwnerPlayback = Id(Field(preferenceOwner, "PlaybackOwner")), CurrentPlayback = Id(DemoPlayback.PlaybackOwner),
                OwnerTransport = Id(Field(preferenceOwner, "Transport")), CurrentTransport = Id(DemoPlayback.Session.Transport),
                ReadTask = Id(read), ReadStatus = (read as System.Threading.Tasks.Task)?.Status.ToString(),
                SaveTask = Id(save), SaveStatus = (save as System.Threading.Tasks.Task)?.Status.ToString(),
                ReadHandled = Field(preferenceOwner, "ReadHandled"), RestoreHandled = Field(preferenceOwner, "RestoreHandled"),
                PendingSave = Field(preferenceOwner, "PendingSave"), ReadErrorPresent = Field(preferenceOwner, "ReadError") != null,
                Loaded = Field(preferenceOwner, "Loaded") != null, Request = Field(Page, "_studioSaveViewRequest"),
                ProblemKind = problemKind, ProblemLength = problem.Length,
                LeaseCurrent = typeof(RmlUiOpenGlSmoke).GetMethod("NativeReplayLeaseCurrent", StaticPrivate)!.Invoke(null, new object[] { _window! }),
                FileExists = File.Exists(savedViewPath), Frame = ReplayController.CurrentFrame, Seeking = ReplayController.IsSeeking,
                Paused = ReplayController.IsPaused
            };
            string serialized = JsonSerializer.Serialize(state);
            if (serialized == lastViewSaveDiagnostic) return;
            lastViewSaveDiagnostic = serialized; viewSaveDiagnosticCount++;
            File.AppendAllText(Path.Combine(_output, "persistent-view-save.jsonl"),
                JsonSerializer.Serialize(new { AtMs = _elapsed.ElapsedMilliseconds, Change = viewSaveDiagnosticCount, State = state }) + "\n");
        }
        Dictionary<string, string> Sidecars()
        {
            // ReplayArtifacts owns the exact durable sidecar namespace. Thumbnail PNGs
            // are derived asynchronous render outputs, not authored editor metadata.
            // Never glob caches, in-flight thumbnail files, checkpoint data or temp files.
            var suffixes = (string[])typeof(ReplayArtifacts).GetField("SidecarSuffixes", StaticPrivate)!.GetValue(null)!;
            return suffixes.Where(suffix => suffix is not (".thumb0.png" or ".thumb1.png" or ".thumb2.png"))
                .Select(suffix => path + suffix).Where(File.Exists)
                .ToDictionary(file => file, Hash, StringComparer.Ordinal);
        }
        void SameSource(bool acceptSettledSeekTransport = false)
        {
            var currentTransport = DemoPlayback.Session.Transport;
            bool samePage = ReferenceEquals(page, Page), sameDocument = Page!.Document == document;
            bool sameOwner = ReferenceEquals(owner, DemoPlayback.PlaybackOwner), sameTransport = ReferenceEquals(transport, currentTransport);
            bool sameRecording = DemoPlayback.LogicalPath == path && Hash(path) == source;
            int Identity(object? value) => value == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
            File.AppendAllText(Path.Combine(_output, "persistent-source-identities.jsonl"), JsonSerializer.Serialize(new
            {
                AtMs = _elapsed.ElapsedMilliseconds, SamePage = samePage, SameDocument = sameDocument, SameOwner = sameOwner,
                SameTransport = sameTransport, SameRecording = sameRecording, OriginalPage = Identity(page), CurrentPage = Identity(Page),
                OriginalOwner = Identity(owner), CurrentOwner = Identity(DemoPlayback.PlaybackOwner), OriginalTransport = Identity(transport),
                CurrentTransport = Identity(currentTransport), Frame = ReplayController.CurrentFrame, ExpectedFrame = expectedFrame,
                Seeking = ReplayController.IsSeeking, Paused = ReplayController.IsPaused, AcceptSettledSeekTransport = acceptSettledSeekTransport
            }) + "\n");
            Require(samePage && sameDocument && sameOwner && sameRecording,
                "persistent workspace retains exact original recording, page and foreground player");
            if (acceptSettledSeekTransport)
            {
                // PassiveReplayPlayer replaces its decoded replica/session/transport on staged seek,
                // copying preferences while retaining its strong foreground player identity.
                Require(!ReplayController.IsSeeking && ReplayController.IsPaused && ReplayController.CurrentFrame == expectedFrame,
                    "replacement transport is adopted only after the exact reviewed seek settles paused");
                transport = currentTransport;
            }
            else Require(sameTransport, "persistent workspace preserves transport until an explicit staged seek settles");
            var current = Sidecars();
            Require(current.Count == sidecars.Count && sidecars.All(pair => current.TryGetValue(pair.Key, out string? hash) && hash == pair.Value),
                "actual workspace layout, Settings, numeric preview and empty-track gestures preserve exact sidecar bytes");
        }
        void RecordNumericCamera(string stage)
        {
            TheatreWorkspaceCameraValues? actual=null;long? picture=null;string error="";
            try
            {
                var backend=Read<ITheatreCameraBackend>(Page,"_cameraBackend")!;var context=backend.Capture();picture=context.PresentationRevision;
                actual=((ITheatreWorkspaceCameraBackend)backend).ReadDirectCamera(context);
            }
            catch(Exception ex){error=ex.GetType().Name+": "+ex.Message;}
            var fields=new[]{"x","y","z","yaw","pitch","roll","fov"}.ToDictionary(k=>k,k=>Host!.ReadField(document,"studio_camera_"+k));
            var review=Read<TheatreCameraContext>(Page,"_workspaceCameraReview");
            File.AppendAllText(Path.Combine(_output,"persistent-numeric-camera.jsonl"),JsonSerializer.Serialize(new
            {Stage=stage,AtMs=_elapsed.ElapsedMilliseconds,Focus=Host!.FocusedElement(),Status=Read<string>(Page,"_workspaceCameraStatus"),
             Review=review==null?null:new{review.Frame,review.PresentationRevision,review.Pose,review.TrackPlaying,review.SeekGeneration},Expected=changed,Original=original,Actual=actual,Picture=picture,InitialPicture=numericPicture,Fields=fields,Error=error})+"\n");
        }
        void SetCameraFields(TheatreWorkspaceCameraValues values)
        {
            Focus("studio_camera_x");
            foreach (var field in new[] { ("x", values.X), ("y", values.Y), ("z", values.Z), ("yaw", values.Yaw),
                ("pitch", values.Pitch), ("roll", values.Roll), ("fov", values.Fov) })
                Host!.SetField(document, "studio_camera_" + field.Item1, field.Item2.ToString("R", CultureInfo.InvariantCulture));
        }
    }
}
