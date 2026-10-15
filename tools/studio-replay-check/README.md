# Studio replay ownership checks

Run `dotnet run --project tools/studio-replay-check -c Release`. This references the canonical Studio engine projection and exercises independent immutable snapshots; export leases that retain bytes after document close; exact hashes; cancellation and partial-copy cleanup; source, backing recording, case and symlink output protection; canonical camera identity/checksum roundtrips; independent sampling; atomic annotations/reels; and fixed rational sampling at every supported rate. Real child processes verify bounded startup timeout, heartbeat timeout, cancellation without graphics callbacks, and exact parent process identity. No cartridge, game window, socket or GPU is required for these checks.

A native macOS/Windows/Linux smoke mode uses a private copy of an existing historical replay and the configured extracted assets:

```
DOTNET_HOST_PATH=/path/to/dotnet dotnet run --project tools/studio-replay-check -c Release --no-build -- --native-export /path/to/ProjectPrimeStudio.dll /path/to/original.ppdemo /path/to/output /path/to/paths.txt
```

It exports two seconds at 1280×720 and 1920×1080 with game HUD, replay overlay and offline audio, hashes the original before and after each export, and retains MP4 output and small job diagnostics. It removes intermediate PNGs/WAVs and private replay copies. The original recording is never written. Use `--native-export-composite` with the same arguments for one 720p audio+overlay+multiview export with two ordered reel segments and a Director camera cut. An unavailable original map, graphics context or OpenAL Soft loopback device must be reported as a failed native acceptance check, never a silent fallback.

The implementation uses the canonical player, render/capture, rational sample clock, director, reel segments and encoder; audio uses a private linear replay with explicitly clocked OpenAL Soft and selected combat cue decoding. Multiview uses independent render targets over one playback state. These checks alone do **not** establish ST-05 completion: audiovisual parity, long recordings/reels, multi-document seek/POV isolation under native graphics, and Windows/Linux/macOS acceptance still require direct verification against Game. No replay wire format changes are introduced.
