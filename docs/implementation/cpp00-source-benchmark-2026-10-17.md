# CPP-00 source/current-state lock and benchmark evidence

Snapshot: October 17, 2026, approximately 22:55–23:01 UTC. This is source/API
research and evidence-contract testing. **CPP-00 remains IN PROGRESS**: no clean
exact-head native baseline or comparable Project/Fruity capture was collected.
No performance winner, device PASS or release qualification follows from it.

## Actual source pins

| Repository | Observed ref | Actual SHA | Latest release observed |
| --- | --- | --- | --- |
| [Project Prime](https://github.com/AntiNotAnti/Project-Prime) | `main` | `dc7f097067d755ec76d145fc7cc333068566863d` | `v0.1.53`, published October 13 |
| [Fruity Prime](https://github.com/liveteklol/Fruity-Prime) | `master` | `62b128af60e9bb69faf868a00e0fac2017636109` | `v0.16.3`, published October 17 |
| [Second Hunt](https://github.com/BountyHunterKanden/SecondHunt) | `master` | `c16a907b3658c2fd4079e394a73711596a85caee` | `v0.2.1-beta`, published October 16 |

Observed with `git ls-remote` against each repository and GitHub's release API,
not copied from the attached audit. Fruity advanced beyond that audit's
`d44833c937272261bfb135e91618689dd03feacd`; its current commit adds `.fpmap`
catalogue support ([source commit](https://github.com/liveteklol/Fruity-Prime/commit/62b128af60e9bb69faf868a00e0fac2017636109)).
Second Hunt's existing port contract remains pinned to the same current upstream;
its recorded Project Prime target baseline is historical, not today's HEAD.

The actual Project Prime wire protocol is **57**, from
`NetConfig.ProtocolVersion` in `src/MphRead/Mods/Network/NetProtocol.cs`.
Protocol 57 expands the accepted player-name glyph repertoire; mixed older
clients are rejected even though packet layouts are unchanged. The audit's 53
is stale. No protocol change is made by this slice.

The shared branch `codex/rmlui-license-career-rml39` starts at committed
`dc7f097067d755ec76d145fc7cc333068566863d` and has extensive uncommitted
RML-39 onward work. The new source-lock command independently returns
`DIRTY_SOURCE` / exit 2 for that tree. A build of it is a development working-tree
build, not exact-head acceptance of `dc7f0970`.

## Active integration graph and CI

The original RmlUi PR gaps are merged:

| PR | Observed state | Merge commit |
| --- | --- | --- |
| [#698](https://github.com/AntiNotAnti/Project-Prime/pull/698) | MERGED, 20:44:40 UTC | `52918dad71f830909eb653f09ecb345307d36306` |
| [#699](https://github.com/AntiNotAnti/Project-Prime/pull/699) | MERGED, 20:50:48 UTC | `6289354664d642ee142b898d26fd89ebc51cd68b` |
| [#700](https://github.com/AntiNotAnti/Project-Prime/pull/700) | MERGED, 20:51:29 UTC | `62d961c95cbc48b682297efae8d791390c8a7e66` |
| [#701](https://github.com/AntiNotAnti/Project-Prime/pull/701) | MERGED, 20:52:45 UTC | `dc7f097067d755ec76d145fc7cc333068566863d` |

The only open Project Prime PR returned by `gh pr list --state open --limit 100`
was [#561: Replace primary map editing with native Radiant mesh and gameplay tools](https://github.com/AntiNotAnti/Project-Prime/pull/561),
head `65e4a1ef433036fed2c497954d500361e4c9fa94`, branch
`codex/studio-radiant-implementation` → `main`. That is a separate creator/Studio
track with overlapping navigation and content interfaces; it is not merged and
its historical acceptance claims do not transfer to this shared tree. No new PR
or dependency on an unmerged branch was introduced here.

GitHub's exact committed-head check-runs endpoint with `per_page=100` returned
**41/41 completed, success**, for `dc7f097067d755ec76d145fc7cc333068566863d`.
This includes desktop/server/Android build and focused policy/package checks;
it is not runtime/device certification. The default API page contains only 30
records, so its first page alone must not be called a complete CI result. These
41 checks cover committed HEAD, not uncommitted RML-39 onward work.

## Current source/import/UI and asset truth

The existing port is substantial source infrastructure, not a completed playable
expansion: GameCube FST/disc and PAK readers, Retro animation/skin weights,
private Echoes map import/geometry/collision, and the PORT-26 complete-five local
visual matrix already exist. Continue `docs/porting/second-hunt-content.md` and
its pinned manifest; do not create a second importer or restart PORT-00.
PORT-26 explicitly leaves first-person/morph models and animations, Trooper
mechanics, multiplayer/replay and physical hardware acceptance open. Its reports
do not set `fullPortReady` or `onlineMultiplayerEnabled` true.

Local availability was checked without reading/distributing asset content:
`PROJECT_PRIME_USER_DATA` was unset; the canonical macOS `AppPaths` location
`~/Library/Application Support/Project Prime/paths.txt` existed and its AMHE1 root
existed. This does not establish file completeness, map identity, imported discs,
or availability on Windows/Android/CI. No proprietary files, map hashes, replay
hashes or normalized simulation hashes were produced by this slice. Local source
paths and source content stay private.

RmlUi continuation includes local License/privacy, offline rules, Theatre,
gameplay overlays, Party and presence work. Root's local GL evidence uses
synthetic fixtures at three viewport sizes, including Input's 159 assertions;
it is not a real world/network benchmark. The restored backend is not deployed,
live-service actions and physical Android remain unverified. This snapshot does
not upgrade those claims. The direct user policy for this continuation is that
RmlUi is **not a prerequisite for backend work**, and all future product UI uses
RmlUi. Full Settings stays last in the existing UI migration sequence.

**CPP-01 is COMPLETE_BY_SKIP under user direction**, not AGX acceptance.
`docs/engineering-next/agx04.md` still records `UNRESOLVED_P0 / NOT_ACCEPTED`.
Physical Android startup/background/restore and high-Hz validation remain
`NOT_VERIFIED`. No AGX mitigation, cache, disposal or GC policy changes were made.

## Existing harnesses and the missing piece added here

The repository already has the native normal-frame-loop fixture,
`-perfcheck`/`run-perf-suite.sh`, OpenGL ABBA/resource runners, frame pacing and
simulation/replay/network fixtures. Their measurement scopes differ: `-perfcheck`
measures draw work excluding Present/Swap, while `native-render-check` records
completed application frames; neither measures panel scanout/input latency.
Screenshot/readback and lifecycle/resource stress arms must stay separate from
timing. Local native bots do not prove dedicated online behavior.

`tools/compare-perf.py` checks room, player count, dimensions, render scale and
presentation rate, but has no device/driver/source/map/replay/simulation identity
gate and treats absent numeric fields as zero. Existing native capture helpers
already hash binaries and assets and isolate `paths.txt`; those owners are reused.

The new `tools/competition-evidence/evidence.py` supplies only the missing
manifest/integrity/comparability layer. It requires clean matching source/build
declarations, hashes actual preserved binary/raw/build/source/asset/replay/sim
files, checks native observed settings/measurements against the manifest, rejects
missing identities and device/map/config mismatches, and reports
`NOT_COMPARABLE` instead of a relative performance victory. The source-lock
command preserves dirty observations and refuses overwrite. `COMPARABLE` and
`IDENTITIES_COMPLETE` are integrity scopes, not performance, build-source or
hardware attestation. Build logs/invocations and actual trace execution still
need independent review. See `tools/competition-evidence/README.md` for the full
schema and canonical 672-cell collection matrix.

The adapter currently accepts existing unprofiled **offline** native results.
Dedicated-online and Fruity completed-frame adapters remain missing; attaching
a replay or renaming fields cannot turn ordinary bot runs into those captures.
The current gate deliberately refuses them. No replacement engine, renderer,
importer, network controller or benchmark runner was created.

## Validation and remaining acceptance

Observed local host metadata: macOS 27.0 build `26A428`; SDK
`/Users/jarrett/.dotnet/dotnet` reports `10.0.401`. No GPU/driver or physical device
measurement was collected, so none is invented here. Root centrally owns app and
server builds; this slice did not compile MphRead or run against the shared
server-built output as if it were a desktop client.

- `python3 -m unittest discover -s tools/competition-evidence -p 'test_*.py' -v`:
  **12 test methods passed**, covering mismatched devices/maps/settings/hashes,
  dirty source/build, untracked source changes, missing metrics, tampered raw
  files, native observation disagreement, synthetic/emulator/readback/profile
  rejection and local-bot/dedicated-online mislabeling. These are synthetic
  contract tests, not performance samples.
- Actual source-lock invocation on the shared checkout: `DIRTY_SOURCE`, exit 2;
  private observation saved to `/tmp/prime-cpp00-source-lock-2026-10-17.json`.
- Direct comparison of historical GL JSON reports without the new provenance:
  `NOT_COMPARABLE`, exit 2. Their historical results retain their original scope.

CPP-00 closure still needs a clean pinned client/harness build with immutable
build receipts and hashes, canonical real-asset/map/replay/simulation witnesses,
actual matched raw measurements, missing online/Fruity capture integration, and
declared hardware limitations. Until independently run, frame/GPU/CPU/input
latency/GC-pause/direct-native metrics and any cross-fork performance comparison
remain unmeasured. CPP-13 must independently qualify each enabled device slice.


## Observed-workload receipt continuation

The existing native timing runner now emits a schema1 `observedWorkload` receipt;
competition manifests use workload fixture `cpp00-v2` and harness label
`native-render-check-v2`. Earlier raw reports cannot pass the stronger admission.
The receipt observes actual start/end roster (slot/hunter/bot), seed assignment
readback, start/end RNG states, offline authority, measurement tick boundaries,
entry-only phase and actual client/harness SHA plus loaded/file PE MVID agreement.
Binary receipt work precedes the measurement baseline, final observations follow
the last timed interval, and their allocation overhead is excluded. No engine,
new runner, replay consumer, simulation owner or rendering path was introduced.

The adapter rejects missing/changed roster, seed/tick/binary disagreements,
diagnostic/capture runs, room reentry, online mislabels and fabricated replay
execution. It explicitly requires frozen input snapshots with bot AI and
`replayExecuted: false`. Slot zero uses the requested hunter; other slots keep
the existing rotating hunter assignment. A preserved external replay and Oracle
report remain reference artifacts rather than evidence the timed bots consumed
that replay. Canonical replay-driven/online/Fruity execution remains open.

Validation:16 synthetic Python contract tests PASS, including new old-report,
roster, seed, tick, binary, replay-label and lifecycle rejections. The complete
native-render-check sources, including the parallel pixel-qualification option,
compiled via isolated direct references to immutable64 client with zero warnings
and errors (`/tmp/prime-cpp00-native-syntax-build.log`). No normal timing run, GL
run, relative performance result or new device qualification was performed by
this continuation. The dirty shared working tree remains ineligible for clean
exact-head acceptance.
