Run `dotnet run --project tools/studio-export-check -c Release`.

The asset-free check links the canonical fractional sampler, encoder job, and offline
PCM mixer. It tests all supported output rates over 60 Hz simulation, exact audio duration,
event alignment, independent game/combat/replay/music volumes, deterministic WAV bytes,
decoded PCM8/PCM16 conversion, script pitch/pan/stop timing, resampling, atomic cancellation,
nested diagnostic credential redaction, and real child-process progress/stderr/cancellation.
Portable ZIP preflight tests reject traversal, duplicate names, symlinks and oversized
sidecars before extracting, and verify preserved recording/package fixture bytes. These ZIP
fixtures do not claim canonical replay parsing or exact custom-map acceptance; those require
the engine/native fixture suite.
The child fixture tests encoder process ownership; it does not claim FFmpeg video or native
replay rendering acceptance, which requires the renderer/native export suite.

With `--ffmpeg <executable> --ffprobe <executable>`, the check additionally creates PNG
codec fixtures, muxes the production offline WAV through the canonical encoder job, and
independently decodes the resulting H.264/AAC MP4 to verify frame count, rate, sample rate
and channels. This proves encoder and audio integration, while native replay pixels remain
the responsibility of the native lifecycle fixture suite.
