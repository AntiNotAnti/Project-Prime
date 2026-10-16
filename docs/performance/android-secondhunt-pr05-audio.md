# PR-05: Android audio latency audit, diagnostics, native-rate experiment

Project Prime already mixes music and SFX into **one SoundFlow/miniaudio output**.
SecondHunt's AudioTrack mixer must not replace it wholesale. The existing
default requests 32728 Hz (the NCSF original). Many Android output devices
report 48000 Hz and have different buffer-burst sizes; mixing directly at
the native output rate might avoid a resampling stage, but actual low-latency
device selection is implementation/device dependent.

- Default: unchanged 32728 Hz output and identical gameplay/audio code.
- On Android audio initialization: record device's requested output sample rate
  and burst property and the rate actually *requested* of miniaudio.
- Experimental `-p:AndroidNativeAudioRate=true`: request valid native
  device output rate from 32000–192000 Hz for **both** NCSF synthesis and
  the SFX mixer. This changes synthesized audio timing behavior; verify
  music pitch, SFX pitch, cutscene synchronization and online voice before
  considering it for default. A property query does not guarantee FAST output.
- Experimental `-p:AndroidAudioDiagnostics=true`: count average/maximum
  SFX mixer callback work and how often that work exceeds the supplied
  buffer interval; callsite only snapshots counters every Android performance
  reporting interval. `over-buffer` **does not measure hardware underruns**.

## Validation
`dotnet run --project tools/android-pacing-check -c Release`.
Build baseline, diagnostics-only and native-rate diagnostics APKs on the same
Release source and Android hardware. Run 30 minutes each and compare:
1. SFX attack latency, overlapping SFX, music tempo/pitch, audio glitches,
   voice/replay and pause/resume.
2. CPU/thread contention, audio callback timing, frame p95/p99, power and
   thermal throttling (60 Hz and high-refresh).
3. Android 8+ and multiple vendors. Record requested rate/burst and native
   active output state with platform tools where available.

Do not assume native-rate output is automatically lower latency or call a
callback over-buffer statistic a glitch. Only enable a new default when actual
audio latency and fidelity have been measured.

Rollback: omit both properties; all sound/mixer output settings remain as before.
