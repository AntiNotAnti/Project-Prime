# Replay export and auxiliary preview pipeline

## Export ownership

`ReplayVideoExporter` retains the rational `ReplayExportSampler` and the same
camera/source-presentation evaluator used by preview. Before reading a picture,
the render owner reserves one of three `ReplayFrameWriter` slots. Each slot
includes its pending readback, queued RGB buffer or currently encoding PNG. A
full queue holds the exact sample and schedules no replay step. Sequence indices
advance only after the detached pixels were accepted, with no skipping or retry
duplication.
The native export controls and manifest admission include 240 FPS alongside the
existing rates; quarter-frame samples use the same integer/rational evaluator.

The GL owner reads the finished replay HUD composite when Game HUD or Replay
Overlay is requested; otherwise it reads the processed scene target. The single
PNG worker receives only detached bottom-up RGB bytes and dimensions. It checks
black frames, encodes and writes in FIFO order. It cannot access scenes, cameras,
simulation, GL or audio. The existing checked synchronous GL readback remains the
portable path; this change does not introduce PBO/fence support or pooled pixel
buffers. At 3840x2160 the three RGB slots retain at most 71.2 MiB, plus the existing
31.7 MiB temporary RGBA readback and codec allocations. This is an allocation
bound, not a measured process-memory result.

Completion closes producer admission and drains every PNG before audio/video
finalization or FFmpeg starts. Cancel discards pending buffers and retains output
ownership until an already-running native PNG write returns. A failed worker
stops admission, releases queued buffers, reports the error and retains completed
images. Another job cannot start while that worker remains active. Rendering
state/camera ownership is restored on the engine thread.

Independent render workers keep accepted ticket/source/audio/package snapshots
under short per-user `pp-export-<GUID>/work` temporary roots, independent of the
requested output path. Windows `CreateProcess` limits its working-directory
argument even when absolute media paths support long names. Retry output
directories are siblings under the original output directory, preventing recursive
nesting. A retry whose preparation fails or is cancelled retains its predecessor's
accepted ticket for subsequent attempts. Snapshot retention is unchanged;
automatic cleanup of accepted snapshots is deferred rather than deleting inputs
that another attempt may still need.

The encoder retains its ordinary working-directory contract for short paths.
On Windows, output directories of at least 240 characters instead use a short
owned process directory, retired after process exit and pipe-reader completion.
Media paths remain absolute, and `encode.txt`/`encode.log` stay in the requested
output directory. The subprocess regression exercises an output directory over
310 characters, actual process startup, bounded working directories, retry
siblings, cancellation and pipe retirement (29 checks).
An additional source-linked probe passes actual FFmpeg 9.0.1/libx264 encoding of
two frames into a 344-character output directory, including progress and log
receipts; its diagnostic log is
`artifacts/replay-validation/ffmpeg-longpath-final.log`.

`ReadbackMilliseconds` reports accumulated CPU wall time for GL readback and RGB
repacking. `PngEncodeMilliseconds` reports worker wall time for pixel validation,
encoder lock, encoding and file writing. Neither reports isolated GPU time.
`FramesWritten` counts completed writes; render progress counts accepted samples.

## Four-camera preview

The first tile reuses the normal render's completed HUD composite only when
source presentation time, view/projection, target dimensions and preview bounds
match. A missing or incompatible primary target retains the four-camera fallback.
A compatible four-view picture therefore makes three auxiliary `OnRenderFrame`
calls after the ordinary main render, instead of four. `LastReplayFourViews`
reports that call count, primary/cache reuse, and CPU wall time for the auxiliary
operation. It does not count individual GL draw calls or establish GPU savings.

The draw/render wrapper restores dimensions only when it entered the temporary DS
LCD scope. Ordinary replay preparation retains its viewport dimensions through
the completed picture and auxiliary passes. A native acceptance run exposed the
previous unconditional restoration to full-window size, which prevented reuse
inside the smaller Studio viewport. The linked DS regression covers both viewport
retention and LCD restoration on success and exceptions.

Auxiliary cleanup rebinds the saved draw/read framebuffers before popping GL
attributes. Buffer selections belong to the saved framebuffer domain, while
framebuffer bindings are not part of that attribute stack. Popping first produced
`GL_INVALID_OPERATION` on NVIDIA and poisoned a later scene-target allocation.
The fix retains exception-safe camera, program, texture and viewport restoration;
native diagnostics check GL errors at composition, fault recovery, toggle and
seek boundaries.

During timeline, curve or viewport gestures, optional auxiliary tiles can reuse a
scene-owned RGB atlas for less than 100 ms. The primary image remains fresh every
draw. Releasing the gesture forces all auxiliary cameras to refresh on the next
draw. Subject, target or viewport changes invalidate reuse. The atlas is capped
at 3840x2160, participates in the scene render-target byte budget, and is released
with replay output. This is adaptive refresh cadence; render resolution remains
unchanged. Scope restoration and the native exception boundary remain responsible
for camera/GL cleanup and automatic single-view recovery.
The atlas is allocated/copied only on demand during a gesture. Start with
`PRIME_REPLAY_PRIMARY_REUSE=0` to compare against the original four auxiliary
renders and disable atlas reuse; the default enables the optimized path.

## Checks and remaining qualification

`dotnet run --project tools/replay-export-queue-check -c Release` checks bounded
reservations, FIFO/once-only writes, worker-thread execution, drain-before-handoff,
in-flight cancellation, disk failure, abandoned readback, exact 30/60/120/240 FPS
sample clocks and auxiliary gesture refresh policy without ROM assets or a GPU.

The real `-replayexportcheck` checks PNG/movie cadence equality, HUD output,
source-POV fixtures and gameplay hashes. These checks passed locally on Windows
with extracted assets, NVIDIA OpenGL and FFmpeg, using generated accepted-fact
recordings. Independent curve preview/export camera and projection equality also
passed at all eight rates. Native Studio acceptance passes compatible primary
reuse with exactly three auxiliary render calls, 100 toggles, injected tile
failure/retry, seeks and clean GL boundaries. The resource harness independently
checks framebuffer/attribute restoration on normal, failed and repeated scopes.

See [the local acceptance report](replay-studio-fidelity-status.md) for commands,
hardware and measurement scope. Genuine captured-source image/audio parity,
isolated GPU timings, sustained high-FPS throughput and other-platform runtime
qualification remain outstanding. No hardware timings are inferred from the
content-free checks.
