using System;
using System.Collections.Generic;
using System.Diagnostics;
using OpenTK.Mathematics;

namespace MphRead.Mods
{
    /// <summary>
    /// Renders one multiplayer room and captures its intro camera view.
    ///
    /// The picture comes from the game's own match-start sequence
    /// (Scene.CameraSequences.Intro, looped by match flow while the match has not
    /// begun): a spectator fly-through the developers framed to show the
    /// level off. That beats any bounds-derived camera this code could
    /// compute.
    ///
    /// One window per room: Presentation.AddRoom must be called before OnLoad and
    /// refuses a second room, so a room per window lifetime is the shape the
    /// engine supports. Batches therefore parallelize across *processes*
    /// (see ThumbnailBatch), not threads -- each render host owns its native
    /// window/event thread and GPU resources.
    ///
    /// Everything is local. Previews render from the user's own extracted
    /// files into a git-ignored cache; no game asset is ever committed.
    /// </summary>
    public sealed class ThumbnailCapture : IRenderToolClient
    {
        private readonly IRenderToolHost _host;
        private readonly string _roomKey;
        private int _settleFrames;
        private bool _captured;

        // The intro sequence needs to start and the match-start fade
        // (20/30s, set by match flow) to clear before the frame is worth
        // keeping. Authoritative simulation is 60 Hz, so allow more than
        // the fade's forty ticks before taking the first picture.
        private const int SettleFrames = 48;
        private const int RetryFrames = 20;
        private const int MaxAttempts = 3;
        private int _attempts;

        public Scene Scene { get; }
        public ScenePresentation Presentation { get; }
        public bool Succeeded => _captured;

        private readonly Vector2i _asked;

        private ThumbnailCapture(string roomKey, int width, int height, IRenderToolHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _asked = new Vector2i(width, height);
            _roomKey = roomKey;
            _settleFrames = SettleFrames;
            Scene = new Scene(features: ClientMatchFeatures.Capture());
            Presentation = host.CreatePresentation(Scene);
            // A player must exist for the multiplayer intro path to run:
            // The match flow sets the sequence up against the scene local player's
            // camera info, and EnsureIntroCamSeq only fires for Multiplayer.
            Scene.AddPlayer(Hunter.Samus, recolor: 0, team: -1);
            Presentation.AddRoom(roomKey, GameMode.Battle, playerCount: 1);
        }

        public void OnLoad()
        {
            // Scene was constructed with the *requested* size, but the window
            // system can clamp it to what the display can hold, so asking for
            // 1920x1440 on a smaller screen yields a smaller window. The
            // render target would then be larger than the framebuffer being
            // drawn into, leaving unwritten black bands on two edges. Adopt
            // the size the window actually got before anything is allocated.
            Presentation.Size = _host.Size;
            Presentation.OnLoad();
            // OnResize normally sets the viewport and resizes the offscreen
            // targets; a window that is never shown or resized never gets
            // that call.
            Presentation.OnResize();
            // Once, and only from the first worker, so a batch of thirty
            // rooms does not print it thirty times. What it answers is the
            // question a black picture cannot: whether the driver gave this
            // process a context it can actually draw in.
            if (!_describedContext)
            {
                _describedContext = true;
                string line = $"build {Update.BuildVersion.Display} / "
                    + $"{System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "?"}, "
                    + $"backend sdl-gpu, window asked {_asked.X}x{_asked.Y}, "
                    + $"got {_host.Size.X}x{_host.Size.Y}";
                Console.WriteLine($"[thumbnails] {line}");
                ThumbnailLog.Write(line);
            }
        }

        private static bool _describedContext;

        /// <summary>
        /// A custom map has no intro sequence to borrow a viewpoint from, so
        /// if it named one, use it -- every frame, since the roam camera is
        /// otherwise left wherever the scene put it.
        /// </summary>
        private void ApplyPreviewCamera()
        {
            foreach (MapGen.MapDefinition def in MapGen.CustomRooms.Definitions)
            {
                if (def.Preview != null && def.Name.Equals(_roomKey, StringComparison.OrdinalIgnoreCase))
                {
                    Presentation.SetPreviewCamera(
                        new Vector3(def.Preview.Position[0], def.Preview.Position[1], def.Preview.Position[2]),
                        new Vector3(def.Preview.Target[0], def.Preview.Target[1], def.Preview.Target[2]));
                    return;
                }
            }
        }

        public void OnFrame()
        {
            Presentation.OnSimulationFrame();
            // Legacy OnUpdateFrame built the draw list before this override,
            // then applied the camera before OnRenderFrame uploaded uniforms.
            // SDL seals matrices during OnDrawFrame, so apply the same
            // override between the simulation and the single draw instead of
            // drawing twice or capturing the previous camera.
            ApplyPreviewCamera();
            bool capturing = !_captured && _settleFrames-- <= 0;
            if (!capturing)
            {
                // The intro camera advances on the update, not on the draw,
                // and the update clears and rebuilds the render lists either
                // way -- so the forty-odd frames that exist only to let the
                // camera reach its mark need not be drawn at all. They were
                // 2367 ms of a 5336 ms capture.
                return;
            }
            var requestedCapture = new RenderToolCapture(CaptureTargetKind.ThumbnailTarget,
                ThumbnailGenerator.PathFor(_roomKey));
            RenderToolFrameResult frame = _host.Render(Presentation, requestedCapture);
            if (!frame.Submitted)
            {
                return;
            }
            bool captured = ConsumeCaptures(frame.Captures);
            bool giveUp = false;
            // The result decides, rather than the attempt. Saving refuses
            // an all-black frame, and reporting that as a capture is what
            // let a run of thirty rooms announce success and leave thirty
            // black pictures behind.
            if (_captured)
            {
                ThumbnailLog.Write($"{_roomKey}: captured at {Presentation.Size.X}x{Presentation.Size.Y}"
                    + (_attempts > 0 ? $" on attempt {_attempts + 1}, window shown" : ""));
            }
            if (!_captured)
            {
                ThumbnailLog.Write($"{_roomKey}: attempt {_attempts + 1} produced nothing usable "
                    + "from the SDL GPU thumbnail target");
                // Show the window and try again.
                //
                // A capture window is never displayed -- there is nothing
                // to look at and thirty-three of them flashing would be
                // worse than useless. But a driver is entitled to do
                // nothing at all for a window with no visible surface,
                // and some do: an Intel Iris Xe on a compatibility 3.2
                // context rendered every one of thirty-three rooms at
                // 0.00% lit while the game itself ran fine on the same
                // machine. Mesa has the mirror image of this, recorded in
                // CLAUDE.md -- a hidden window there has no usable back
                // buffer, which is why captures read the offscreen target
                // in the first place.
                //
                // So it stays hidden for the attempt that costs nothing,
                // and only a machine that needs the window sees it.
                if (!_host.IsVisible)
                {
                    _host.IsVisible = true;
                    ThumbnailLog.Write($"{_roomKey}: showing the window and retrying -- "
                        + "this driver appears not to render to a hidden one");
                }
                // A few more frames in case the first one simply came too
                // early, then stop: if the context cannot draw, no number
                // of frames will change that.
                _settleFrames = RetryFrames;
                giveUp = ++_attempts >= MaxAttempts;
            }
            if (_captured || giveUp)
            {
                _host.Close();
            }
        }

        public void OnCapture(RenderCaptureResult capture)
        {
            if (_captured || capture.Target != CaptureTargetKind.ThumbnailTarget)
            {
                return;
            }
            if (ConsumeCapture(capture))
            {
                ThumbnailLog.Write($"{_roomKey}: captured at {Presentation.Size.X}x{Presentation.Size.Y}");
                _host.Close();
            }
        }

        private bool ConsumeCaptures(IReadOnlyList<RenderCaptureResult> captures)
        {
            bool captured = false;
            for (int i = 0; i < captures.Count; i++)
            {
                captured |= ConsumeCapture(captures[i]);
            }
            return captured;
        }

        private bool ConsumeCapture(RenderCaptureResult capture)
        {
            if (capture.Target != CaptureTargetKind.ThumbnailTarget)
            {
                return false;
            }
            bool captured = RenderToolCaptureSupport.Save(capture,
                ThumbnailGenerator.PathFor(_roomKey));
            _captured |= captured;
            return captured;
        }

        public void OnClosing()
        {
            Presentation.DoCleanup();
        }

        /// <summary>
        /// Render a share of rooms, one after another, in this process.
        /// Returns how many pictures were written.
        ///
        /// A worker used to be one room, so a batch of twenty-eight paid for
        /// twenty-eight runtimes to start, twenty-eight passes of the same
        /// code through the JIT and twenty-eight reads of the same metadata,
        /// to take twenty-eight pictures. Android never did that -- its
        /// workers are handed a list (see PreviewRun.Render) -- and there was
        /// no reason the desktop had to. Measured: the first room in a worker
        /// costs 0.92 s and the ones after it 0.38-0.45 s.
        ///
        /// Still a window per room, though. Presentation.AddRoom must be called
        /// before OnLoad and refuses a second room, so a room per window
        /// lifetime is the shape the engine supports; what is saved here is
        /// everything *outside* the window, which is most of what a short
        /// capture spends its time on.
        /// </summary>
        public static int CaptureRooms(IReadOnlyList<string> rooms, int width, int height)
        {
            int captured = 0;
            for (int i = 0; i < rooms.Count; i++)
            {
                var clock = Stopwatch.StartNew();
                bool ok = CaptureRoom(rooms[i], width, height);
                if (ok)
                {
                    captured++;
                }
                Console.WriteLine($"[thumbnails] {(ok ? "ok" : "FAILED")}  {rooms[i]}"
                    + $"  {clock.Elapsed.TotalSeconds:0.00}s");
            }
            return captured;
        }

        /// <summary>Render and save one room's preview. Returns false if it could not be captured.</summary>
        public static bool CaptureRoom(string roomKey, int width, int height)
        {
            try
            {
                ThumbnailMode.Enter();
                MapGen.RoomContentPreparationResult preparation =
                    MapGen.MapPreparation.PrepareRoomAsync(
                        new MapGen.RoomContentRequest(roomKey, null,
                            MapGen.GameplayContentIdentity.Tool("thumbnail"),
                            MapGen.RoomContentPurpose.Thumbnail),
                        System.Threading.CancellationToken.None)
                    .GetAwaiter().GetResult();
                MapGen.MapPreparation.RequirePreparedRoom(preparation);
                // Each capture owns its player roster and fresh random streams.


                using IRenderToolHost host = RenderToolHostFactory.Create(
                    new Vector2i(width, height), $"{Branding.Name} thumbnails",
                    updateFrequency: 0, visible: false, presentable: false);
                var capture = new ThumbnailCapture(roomKey, width, height, host);
                host.Run(capture);
                return capture.Succeeded;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[thumbnails] failed {roomKey}: {ex.Message}");
                ThumbnailLog.Write($"{roomKey}: threw {ex.GetType().Name}: {ex.Message}");
                return false;
            }
            finally
            {
                ContentEnvironment.UnmountMap();
                ThumbnailMode.Exit();
            }
        }
    }
}
