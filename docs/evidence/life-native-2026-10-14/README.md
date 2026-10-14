# Portable native LIFE evidence at 88f9

These are sanitized derivatives of the actual integrated
`88f9d29e58d0cf6254b2d33e682955bd432d0bd2` campaigns. The package is based on the
newer documentation branch's parent `09ada0ab`, but **does not qualify that newer
runtime or its new NET/UI fixture source**. Full plan acceptance remains
NOT_ACCEPTED. The [earlier loaded-world package](../life-campaign-2026-10-14/README.md)
keeps the separate 38ae 610-world/save results.

## Source and loader proof

The physical 88 client is SHA256
`785d507a682811f31d4262ab39ac8427074d4fc3563c8a9cdc863787f53cb009`,
MVID `896c230f-be96-4e23-b0cc-3fb2824d1a15`, BuildId
`local+88f9d29e58d0cf6254b2d33e682955bd432d0bd2`. It passes 15 focused routes plus
123 Python tests and 16 additional pure/configuration routes. Metadata reports
maximum supported checkpoint 13; ordinary non-opted checkpoint 12 remains the
default. Live protocol 56/world checkpoint 10 are unchanged.

Official complete `native-render-check` and `music-lifecycle-check` projects were
built with their normal ProjectReference and identical engine flags. The emitted
client hash equals this exact physical client, rather than substituting an older
DLL or a partial source template. Source-file/project hashes, entry/dependency
hashes, emitted client identity, actual startup loaded-module MVID and owned
build Collector closure observations are preserved. Entries remain distinct:
native harness SHA `6416dab4578f9b7e6495ccfc75446ef7c22e23b90f8239af7e732408dc092660`;
music entry SHA `dc861dbf91897dc78a8820e14685848d61610b4e6c6def5a3507d19f3a51e369`.
No entry/dependency binary is shipped. Original incomplete-loader attempts keep
their own different identities and failures.

## Actual stage dispositions

| Attempt / stage | Actual status | Scope |
|---|---|---|
| 1 / resource entry | FAIL | Managed dependency FileNotFound/SIGABRT before GL; later stages NOT_RUN. |
| 2 / resource entry | FAIL | Native GLFW DllNotFound/SIGABRT before GL; later stages NOT_RUN. |
| 3 / four resource arms | PASS | Combined, map-gl, Studio renderer/cache and real audio; ten cycles per arm. Map/combined requested 120 simulation frames, eight configured actors, readback and High shadows. Studio/audio have their own actual denominators in JSON. |
| 3 / replica and seek | PASS | Actual recording: 5,690 gameplay/presentation hashes/decoder restores, 44,507 animation restores, 19 detached worlds, 5,390 continuation frames, 757 projectile frames; 62 file/clip comparisons and 18 checkpoints. |
| 3 / export | PASS | Native 720p/4K HUD targets; repeated identical 24/30/48/60/90/120/144 cadence samples, fractional frames, gameplay invariance, serialized queue/reel/cancel. These are export checks, not live delivered FPS. |
| 3 / music | PASS | Native MiniAudio/NCSF: 120 load/play/pause/control/remove cycles, 3,539 checks and final owner drain. |
| 3 / full shell script | FAIL | Process exit 0, but executor concatenated mirrored stdout/debug markers and rejected duplicated cycles. Actual FAIL receipt retained; attempt3 respawn NOT_RUN. |
| 4 / full shell script | FAIL | Actual process exit 1 at blanket CreateLobby preview assertion; respawn NOT_RUN in this attempt. |
| 5 / respawn only | PASS | Nine cycles; 10 deaths/10 respawns, 4,935 frames/3,106 checked, 0 black/GL/invariant/failure counts, 39 fade-excluded frames, resize/scale/cel controls. Separate run, not a replacement for failed shell. |

All completed campaign stage groups close. Resource records retain every
chronological memory/audio/name/cleanup observation, source identity, context,
window event, failure injection and limitation. The recorded combined/map-gl
control/candidate readbacks match in all 20 RGBA/float-depth pairs, including
actual failure-path execution. Studio checks renderer/cache rebuilds, not full
editor UI. Owned GL-name retirement and descriptive RSS/GC/slopes do **not**
prove P0 native allocation boundedness, a driver leak, FPS or a frame-time win.
GPU timer data is not supplied by this campaign.

## UI classification and additional source negative

The mirrored-stream diagnostic keeps raw stream cycles [1,2] separate from
actual debug stream cycles [1,2] and different assets-ready observations. It does
not erase the original executor FAIL by treating two streams as one chronology.

The subsequent modal classification verifies that CreateServerScreen,
HunterStand and Launcher source blobs at 88 are byte-identical to then-main
7df. It states **source/capture classification only; no actual latest-main shell
campaign**. The authored CreateLobby panel owns its active HunterStand; the
blanket assertion forbids that legitimate ownership. Keep the actual FAIL,
source anchors and capture hash. Corrected owner-specific helper source is in
the later c715 integration, awaiting its own runtime/native UI receipt here;
source equivalence is not an automatic inherited-failure waiver or new PASS.

The additional `f24b07b2` full build fails with CS0136 at
TimedImperialistImpactTests: an introduced local `profile` collides with an
outer scope. The original build log/receipt and closed process group remain
negative. Source correction 7563 does not relabel that build; fresh qualification
of the final combined NET/UI source is outside this package.

## Capture metadata and provenance

`capture-artifacts.json` is explicitly packager-generated metadata from existing
immutable files and raw readback records, not a new campaign. It records actual
SHA256/byte counts and PNG dimensions for 266 PNG/18 MP4 artifacts; neither
images nor media are copied. Nineteen existing linear/restored replica PNG pairs
are byte-identical. That file-encoding observation is separate from the source
fixture's gameplay/seek checks and the 20 native readback pairs. It is not a
complete world/HUD/depth/stencil or all-views/platform parity claim.

The package manifest separates original relative name/SHA256/bytes from each
sanitized derivative's SHA256/bytes. Original source-tree dumps and per-file
asset maps are omitted with canonical value hashes/counts; retained numerical
values, nulls, boolean values and array order/length are unchanged. Private
prefixes are replaced with `${WORKSPACE}`/`${HOME}`. Assembly PublicKeyToken
strings identify managed dependencies; they are public assembly metadata, not
network credentials. One exception log contains original trailing whitespace; its derivative is JSON-escaped text, preserving that whitespace while passing the mandatory diff check. No original log is trimmed. No assets, PNG, video, runtime DLL, savedata, auth profile,
Paths.txt content or full stack/allocation dump is shipped. Brief retained
exception traces identify actual source failures; no native allocation stacks
are copied.

```sh
python3 tools/life-evidence/verify_native.py \
  --evidence docs/evidence/life-native-2026-10-14

# Optional original-input equality and existing capture hashes; no native run.
python3 tools/life-evidence/verify_native.py \
  --evidence docs/evidence/life-native-2026-10-14 \
  --original-workspace-root /absolute/original/workspace --check-artifacts
```

The ordinary portable verifier checks derivative hashes, numeric/array
structure, exact client/official entry contracts, individual PASS/FAIL/NOT_RUN
states, ownership closure, capture counts and actual native readback equality.
Optional local validation checks every original immutable hash/byte count,
exact sanitization and retained scalar/array/text equality, plus original
capture-file hashes. Neither invocation builds source, opens GL/audio/UI or runs
a game/campaign.

For new reproduction use the committed
[serial LIFE executor](../../../tools/nettest/run_life_campaign.py) and
[asset-backed/native recipes](../../audits/engineering-consolidation/asset-backed-lifecycle.md)
with a fresh source-qualified physical client/full official tool runtime,
licensed assets, genuine recording, clean isolated profile and fresh output.
Preserve all negative arms, owned process closure and separate stream semantics.
Do not reuse old 88 hashes for a new runtime or infer supported exit-path/all
callbacks/all-hunter/impaired NET acceptance from these tests. Roll back the
portable evidence/verifier independently; runtime ownership rollback follows
the implementation index and handoff contracts.
