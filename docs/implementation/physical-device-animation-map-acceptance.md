# Physical-device acceptance: Hunters, bosses, Quake maps and multiplayer

This is an evidence contract, **not a claim that hardware testing was performed**. This workspace cannot drive a physical Windows/Linux/macOS/Android GPU or gather new real-game footage. Do not treat synthetic GL2.1 CI, local unit tests, emulator results, or missing samples as native acceptance.

## What the native game reports

Desktop `OpenGlFrameProfiler` writes four or more 240-completed-frame `[glframe-json]` and `[glgpu-json]` windows after `-glprofile -glgpu`. This PR adds `animationBlend` and the six eligible animation channel switches to each frame row, verifying that A and B genuinely select different presentation sampling. Game simulation remains 60 Hz. Nothing in the profiler changes its cadence or how GPU timers are collected.

- OFF arm: `PROJECT_PRIME_ANIMATION_BLEND=0 ProjectPrime -glprofile -glgpu`
- ON arm: `PROJECT_PRIME_ANIMATION_BLEND=1 ProjectPrime -glprofile -glgpu`
- Use **the exact same binary (SHA-256 and assembly MVID)**, map, route, player count, graphics settings, display cap, OS driver, and hardware. Log four adjacent 240-frame windows per arm, excluding the first as warmup.
- Record real PNG captures with screenshots for all seven Hunters, Gorea, Alimbic turret, Quake moving geometry, Quake fog, Quake tcMod, replay, and occlusion. Sign off images after independent human comparison. For Quake movers/fog not yet integrated, mark **NOT_VERIFIED**, never fabricate an accepted image.
- Monitor drops/stalls, p95/p99 CPU/present intervals, GPU timer p99, 1% low FPS, and GL errors. Unsupported GPU timings are **NOT_VERIFIED**; no default-on recommendation can arise from them.

## Desktop evidence command

```sh
python3 tools/device-acceptance/validate.py \
  --manifest real-device/acceptance.json \
  --baseline real-device/animation-off.log \
  --enabled real-device/animation-on.log \
  --output real-device/hardware-result.json
```

The manifest must be placed alongside its real PNG screenshots and supply:

```json
{
  "source": "native-game-on-physical-device",
  "emulator": false,
  "reviewed": true,
  "reviewed_by": "<real independent reviewer>",
  "build_sha": "<40-character commit SHA>",
  "binary_sha256": "<64-character SHA-256>",
  "assembly_mvid": "<MVID in glframe context>",
  "device": {
    "platform": "windows-opengl",
    "gpu": "<GPU model>",
    "driver": "<driver release>",
    "model": "<actual hardware>",
    "refresh_hz": "144"
  },
  "visual_cases": {
    "samus": {
      "reviewed": true,
      "passed": true,
      "baseline": {"path": "off-samus.png", "sha256": "<actual PNG sha256>"},
      "enabled": {"path": "on-samus.png", "sha256": "<actual PNG sha256>"}
    }
  }
}
```

Provide all cases defined in `REQUIRED_SCENES` in `validate.py`, not just the example Samus case. Replace placeholders with actual immutable evidence. The validator verifies real file hashes, trace fingerprints, original device declaration, reported feature modes, and visual-review attestation. It cannot guarantee a screenshot has not been fabricated by a dishonest submitter, so evidence must be independently reviewed and tied to a real, recorded test session.

## Android GLES status

`OpenGlFrameProfiler` is compiled out under `ANDROID`. Until an Android GLES3 profiler emits the corresponding hardware counters, the validator deliberately rejects `android-gles` as **NOT_VERIFIED**. An emulator is not a substitute for a physical Android GPU. Do not mark any Android effect, shader, updater, map or high-Hz test passed on this evidence alone.

## Other mandatory game acceptance

- 60/120/144/165/240 Hz with all seven Hunters, LOD0/1, first-person gun, alternate form, Kanden and Spire specialized rigs, boss death, turret aim and muzzle, player outlines, frozen overlays.
- Quake doors, lifts, rotated platforms, biped and alt-form passenger contact, packet-loss/lifecycle respawn, camera path, wall visibility and native collision.
- Match parity for 2, 4, 8 players, 45/150/350/750 ms network conditions, join-in-progress, spectator, pause/seek/replay, death and disconnect.
- Quake lightmaps, animated UV and fog/alpha edges with depth, translucent glass and postprocessing; identical fixed camera captures.
- Scene unload/reload, GL reset, Android background/resume, worker stop, long-session memory and no per-frame allocations beyond baseline.

**Current evaluation**: `NOT_VERIFIED` for full Hunter/boss integration, interactive Quake movers/fog, and all physical device performance until native evidence is supplied. Asset-free CI only verifies the fail-closed parser and selected math.

## Automated regression gate

```sh
python3 -m unittest discover -s tools/device-acceptance -p 'test_*.py'
```

These tests are intentionally negative/synthetic. They verify that a missing or false receipt cannot be accepted. They do not produce any successful physical-device benchmark.
