# RML98 regression restoration

Date: 2026-10-19  
Status: **RML98 Mac UI restoration accepted and delivered for the tested scope; full migration completion is not claimed.**

User reports after the [RML97 acceptance and delivery](rmlui-migration-completion-rml97-2026-10-18.md) exposed gaps in the editor workflow and lobby/menu presentation. Earlier receipts remain historical evidence for their recorded inputs and checks; they are not erased or extended to cover this restoration.

## Current scope

The lobby selector restores eleven slots in this exact left-to-right order:

| Slot | Content |
| --- | --- |
| 1 | Samus |
| 2 | Inert unavailable placeholder |
| 3 | Spire |
| 4 | Kanden |
| 5 | Trace |
| 6 | Noxus |
| 7 | Sylux |
| 8 | Weavel |
| 9 | Inert unavailable placeholder |
| 10 | Inert unavailable placeholder |
| 11 | Random |

All three placeholders remain inert. Random resolves only to a currently allowed playable Hunter. The menu work also covers right-aligned Quit and the requested 12% secondary-Hunter scale adjustment.

Replay Studio remains a separate editor application; main-app Theatre remains the viewer. The restoration targets a persistent docked workspace: transport controls and timeline stay available while six inspectors expose **Camera**, **Markers / Reel**, **Combat**, **Analysis**, **Audio / Tools**, and **Export**. Playback rates, camera modes/pose, analysis/tool operations and export choices have explicit controls rather than depending on a buried operation cycle. Explicit camera choices include Overview; the FOV range is 10–150°. Event search and tag editing are implemented. Combat inspection and the independent B-recording preview retain their own reviewed recording/owner boundaries.

Project-file work restores explicit native Open for `.ppdemo` recordings and `.ppclip` projects, plus Save As for a reviewed clip range. Save As retains the durable source and rebases camera, markers and reel through staged publication. External recordings/projects enter through validated library import before normal playback adoption. Portable bundles retain the old `replay.ppreplay.zip` default while accepting the newer `.ppreplay` spelling. Native WAV pickers select music and custom cues into the draft without applying it. Saved view preferences and source-bound durable audio preferences are retained through Save As/import. Queued audio mixing and encoder selection are restored, including blank-encoder PNG-sequence output. These implementation statements do not replace the actual-window gates below. Map Studio remains Coming soon.

## Qualification evidence and remaining work

Status: **frozen build22 product passed current Mac UI qualification**. The full Studio test24 fixture passed against identical build22 managed, native and resource inputs. Signed candidate2 Main and Studio apps passed packaged-window and extracted-app checks, strict inventories, signature verification, smoke tests and no-asset startup. The verified ZIP has been delivered. Earlier failures remain preserved. The build13 screen-lock interruption is historical.

Current frozen build22 product inputs:

| Input | SHA-256 |
| --- | --- |
| Client `ProjectPrime.dll` | `52fa76cbfeb32949f51e96a4e1cb38fa8af5229821544d713d4c0128d6420979` |
| Native library, ABI action count 307; unchanged | `84fdfbe0f7bcd1574aa7f8a0aeb1920ba28a4b4800f10aac35c5b58e381d57c2` |

Current qualification results:

- Actual Main build22 passed chat input/send and three replay close/reopen cycles.
- All twelve CPU suites and both replay control/format checks passed.
- All four Studio layout22 configurations passed, including compact 640×360 at density 2 and the actual visual Settings root Back path with Touch/Stylus navigation.
- All three current real-asset Hunter configurations passed in 35.8, 42.2 and 90.9 seconds.
- Full actual Studio test24 passed with true exit 0 in 18.4 seconds: numeric camera controls, Settings, post-seek Save View, graphs, spatial analysis, reel editing, audio add/update/return/remove, mixing and shared preview, CompareWorld, paused B preview, portable export/import, Library promotion and teardown. Receipt scope: `artifacts/rmlui-regressions-rml98-20261019/qualification-test24/acceptance/actual-studio-test24-960x600`.

Test24 rebuilt only an isolated fixture against frozen build22 references; product managed/native/resource inputs are identical to build22. Its helper opens the Audio tab before choosing an operation and uses the actual portable filename constant. The earlier test22/test23 fixture mismatches remain failed historical receipts; test24 does not relabel them as passes.

Actual-window build20 passed post-seek Save View, numeric camera controls, Settings, graphs, spatial analysis and reel editing, then failed Audio initialization. Build21 corrected staged audio reads and initialization retry, covered by seven `workspace-audio-cpu` regression checks. The later actual test24 journey supplies the previously missing Audio acceptance for frozen build22.

The source-linked timeline `test20` run passed **4,329 checks**, including nineteen explicit seek-adoption cases in each editor project and new bootstrap-readiness cases. All process exits were zero and all 127 recorded source hashes stayed unchanged. Evidence: `artifacts/rml98-timeline-cpu/test20/receipt.json`. These CPU/stub receipts remain separate from the actual-window evidence above.

Historical build13 frozen inputs remain in `artifacts/rmlui-regressions-rml98-20261019/qualification-final`, independent clonefile copies of build-attempt13 without hardlinks. Their receipt table and completed checks below apply only to that historical build:

| Input | SHA-256 |
| --- | --- |
| Client `ProjectPrime.dll` | `1dd35f20f85de1cfff766a2b975b6375bf517781d8f9b3ab33e2f8a5a083120f` |
| Native library, ABI action count 307 | `84fdfbe0f7bcd1574aa7f8a0aeb1920ba28a4b4800f10aac35c5b58e381d57c2` |
| Native package manifest | `8b2af3aabc227b8d3deff70267ae3a6ea114c14371330caf28ad1f9d47249d05` |

Historical checks recorded against those build13 inputs:

- Client and all thirteen tools build with zero warnings and errors. The build12 server also builds cleanly; the only production change in build13 is the desktop Studio CSS resource, and the managed client bytes are identical. `server-inheritance.json` identifies the separate server evidence rather than claiming a new server build.
- All **twelve CPU suites** pass, plus replay control and replay format checks.
- **Four native Studio layout configurations** pass: 960×600, 640×360, fractional density, and compact 640×360 at density 2. All seven numeric camera fields, six inspectors, fixed transport and independently scrollable timeline remain reachable.
- **Three real-asset Hunter configurations** pass at 640×360, 960×600 and 1512×818, including all seven playable Hunters, four suits, exact eleven-slot order, three inert placeholders, allowed Random, and right-aligned Quit.
- Main and Studio app bundles have been independently staged and ad hoc signed with inventories and strict signature verification. Their preparation and startup evidence is separate from final archive/extraction acceptance.

Earlier actual-window evidence remains scoped to its recorded inputs. Build11 actual Main passed the full journey and three replay close/reopen cycles. The chat fixture queues real AppKit keyboard events into the owned Cocoa window: two characters reach GLFW text input, the native lobby field contains `ab`, and Send dispatches exactly one message. Its isolated lobby service establishes the production input/send path, not delivery to an external server. Actual Studio build11 passed inspector switching, Settings opening/return and numeric camera Apply changing the reviewed preview.

Subsequent corrections allow explicit workspace refresh to adopt a seek replacement only after provider validation of the original recording/player, scene generation, paths, source, duration and settled verified state. Ordinary refresh retains its transport guard; pending authored work blocks adoption. Bootstrap capture waits for a real presentation scene before establishing the owner baseline. These changes preserve queued Save View requests and HUD/overlay/path choices without reapplying stale view state. Seven camera fields use centered focus scrolling; the compact clipping regression also passed current layout22 qualification.

Visual inspection also exposed opaque Studio CSS covering the rendered world: build11's viewport interior contained 417,600 pixels of exactly one color. Build13 makes the workspace and viewport transparent while keeping transport, inspector and timeline chrome opaque. Actual final-composite pixel assertions now check the settled world, four-view and B-preview pictures. The full actual test24 journey supplies current frozen-build22 Studio acceptance; the earlier failed pictures remain historical evidence. A valid renderer receipt alone does not establish that UI composition reveals the world.

Separate source-linked checks passed camera **467**, editor **186** and timeline **4099** assertions; those focused historical receipts remain distinct from the later test20 source-linked run and current build22/test24 qualification.

Acceptance is limited to the tested Mac UI restoration. B comparison is a paused-frame preview that stops on playback, seek or export; continuous dual playback and an independent video-render-worker architecture are not claimed restored. No Android, Windows, Linux or full-campaign qualification, general physical-input coverage, or blanket restoration of every old feature is claimed. The frozen RML98 source and package remain preserved. [RML99 following-B and detached-export restoration](rmlui-replay-parity-rml99-2026-10-19.md) is separately accepted and delivered for its tested Mac scope. Its evidence does not change RML98’s frozen source, accepted hashes or delivered archive.


## Delivered archive

[ProjectPrime-RmlUi-macOS-arm64-rml98-20261019.zip](/Users/jarrett/Downloads/ProjectPrime-RmlUi-macOS-arm64-rml98-20261019.zip) is **185,572,972 bytes**, SHA-256 `e8ab66a4b0b9f5ec7b45a9e281b2d9cc07dc02a6afaea92f168534ff4af32025`. The delivery receipt is `candidate2/downloads-delivery.json`. Both packaged-window and extracted-app checks passed, including strict inventory/signature verification, smoke and no-asset startup. Delivered product inputs retain the build22 client `52fa76…` and native `84fdfb…` hashes recorded above.
