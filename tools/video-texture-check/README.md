# Live video material acceptance

Runs real FFmpeg/ffprobe, CPU RGB decoding, GPU upload/readback, playback and
production Scene video binding/update/resolve/teardown hooks. It creates synthetic
lossless RGB and ordinary H.264 yuv420p CRF18 MP4s, verifies their metadata with
ffprobe, and assigns the known fixture clock/dimensions directly to the retained
`MapVideoTexture` DTO. It saves/reopens a project and strictly verifies its
source-only `.ppmap`. No model importer or Map Studio authoring API is used. No game
extraction, player settings, backend credentials or downloaded media are needed.

Prerequisites: desktop OpenGL compatibility context/display and FFmpeg with
`libx264rgb` and `libx264`, plus ffprobe. Explicit tool paths also become this process's PATH,
so production decoder discovery uses the same tools. Existing bundled `video/`
tools take production precedence. Use an empty output directory per invocation.

```powershell
& C:/Users/Jarrett/.dotnet/dotnet.exe build tools/video-texture-check/video-texture-check.csproj -c Release --artifacts-path C:/path/to/the-coordinated-client-build -p:BuildProjectReferences=false --disable-build-servers -m:1 -p:UseSharedCompilation=false
& C:/Users/Jarrett/.dotnet/dotnet.exe C:/path/to/the-coordinated-client-build/bin/video-texture-check/release/video-texture-check.dll --ffmpeg C:/path/to/ffmpeg.exe --ffprobe C:/path/to/ffprobe.exe --output artifacts/video-acceptance-01
```

Build the client into the same artifacts path first. `BuildProjectReferences=false`
reuses that exact application build rather than rebuilding it during coordinated
runtime validation. FFmpeg must expose both encoders named above; missing tools,
encoders or OpenGL prerequisites fail the applicable checks.

`--cpu-only` executes fixture/package/source/decoder acceptance without opening
an OpenGL window and explicitly reports that GPU acceptance did not run. The
default creates one hidden OpenTK window at a time and an actual framebuffer for
each codec. Run under
the coordinated GPU slot to avoid competing graphics acceptance processes.
`--retirement-only` isolates the real running-decoder retirement/admission race
for before/after reproduction; reports explicitly distinguish it from full
acceptance. It requires genuinely pending worker completion throughout the
replacement admission attempt and uses no artificial production delay.

`report.json` records every assertion, the tested application path/hash/MVID,
renderer and tools; `lossless-rgb-scene-texture.ppm` and
`h264-yuv420p-scene-texture.ppm` (also PNG) preserve pixels read from the actual
GPU binding returned by the production Scene video resolver. Lossless decode/upload requires exact RGB;
ordinary H.264 checks independently generated solid-region centers with an
explicit eight-code-value per-channel bound for lossy color conversion. Both
codecs must pass all required sections.
Failure exits 1 with the first failed invariant and retained evidence. Output
contains original generated media and an isolated `user-data` directory.

Coverage includes exact RGB/order, queue backpressure, clean EOF, missing tools,
invalid sources/dimensions, concurrent extracted-source deduplication, GL state,
mips, encoded/sRGB upload formats, pause/resume/loop/final-frame hold, shared and
independent Scene channels, graphics disable, memory admission/retry, fallback,
reference release, decoder cancellation and scene disposal. The actual production
scene encode shader also draws sRGB video into encoded output to check for double
gamma. The asset-free Scene has no model texture-source table: after graphics
settings refresh, this harness explicitly reattaches its synthetic primary binding.
Automatic retry of an already blocked independent binding is exercised through
the production pending-admission queue. Retirement admission is tested while a real old FFmpeg worker is still
pending; recorded child PIDs must have exited before completion releases their
resource reservations. It does not claim
runtime map compilation, Map Studio/model material drawing or automatic model
reattachment, a running multiplayer world, physical Android video
playback or universal third-party codec support.
