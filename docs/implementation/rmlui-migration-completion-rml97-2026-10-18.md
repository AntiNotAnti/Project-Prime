# RML97 desktop migration completion

Status: **historical RML97 acceptance, superseded as a current completion claim.** The macOS ARM64 receipts and delivered package recorded here remain valid evidence for their stated checks. Subsequent user reports exposed editor workflow/UX gaps and lobby/menu regressions that those checks did not establish. Current restoration and qualification are tracked in [RML98 regression restoration](rmlui-regression-restoration-rml98-2026-10-19.md); migration and full editor usability must not be called complete on the strength of this historical report. Map Studio remains intentionally **Coming soon**. Networking, presentation-rate performance, content and original-game fidelity acceptance remain separate workstreams.

## Historical RML97 delivered behavior

- Full Settings and HUD editing use native RmlUi. Settings follows the existing working category layout, with naturally scrolling sections, genuine input-device subpages, search, sliders and precise numeric entry, binding capture/conflict resolution, controller profiles/calibration, maintenance and settings archives. Apply, Discard and Revert Category retain their draft boundaries. HUD changes return to the Settings draft. Display-mode trials require a successfully drawn usable native frame before Keep is enabled, and support timed/focus-loss rollback.
- Startup/setup, version browsing/update prompts, constrained Create Lobby, quit and remaining recovery/pause/results/ship/ending presentation routes have native owners. The native-enabled Mac client defaults to RmlUi without a launch-time opt-in. Explicit diagnostic opt-out and the separate non-native build profile remain; normal native routes do not present the old UI as a fallback. This completes presentation migration, not full campaign or ending gameplay validation, which the user removed from scope.
- Home measures its actual side columns and fits the Hunter and four party platforms into their gap. The rear platform is lower and reserves occupied-Hunter headroom. The chamber uses coherent continuous lighting/haze and subdued motion. Hover, focus, pressed, page and modal feedback preserve stable hit regions and respect reduced motion. Hunters, Social/Party and News/Community use explicit procedural backgrounds rather than a retained map scene.
- The MPH-style Hunter License overlay displays the original transparent rank-star artwork directly on the card. Its black backing is removed. Customization belongs on Hunters; the license retains account, records and career presentation.
- Main **Theatre** remains a replay viewer. **PRIME STUDIO** opens the internal chooser; explicit workspace commands launch the separate Studio app. Map Studio is disabled behind its future placeholder.
- Replay Studio restores the old branch's authoring enhancements: sampled camera graphs and Bezier handles, numeric transforms/lens values, four views, spatial filters and world markers, private-world comparison, portable replay import/export, custom recorded-event WAV cues, offline mixing and shared-output audio preview. Library teardown restores replay selection, including repeated close/open and resize journeys. Main-app viewer actions are fenced from authoring.
- The exact supplied `prime_hunters_future_atmosphere_loop.mp3` is embedded. Menu music uses the existing shared output and volume setting, pauses when inactive and yields to scenes. Five original MPH menu cues decode asynchronously from the user's installed game data; ROM sound data is not distributed in the app.

## Final evidence

`artifacts/rmlui-completion-rml97-20261018/qualification-final13/final-acceptance-closure.json` identifies the passing receipts and exact inherited-runtime relationships. Production code is the clean client/server build from `build-attempt9`; the only later production changes are authored Settings RML/RCSS. Each qualification runtime is an independent copy with immutable file manifests. Final13 records both resource deltas and pins the original compiled code, rather than claiming a new compilation.

| Check | Evidence | Result |
|---|---|---|
| Client/server and all eleven tools | `build-attempt9/` | Zero warnings/errors |
| Ten CPU suites | `qualification-final11/acceptance/cpu/` | Pass; final13 changes only Settings styling |
| Native Settings, HUD, versions and composed-page input at 640×360, 960×600 and increased density | `qualification-final13/acceptance/studio-hub-*` | All three pass |
| Actual main window, all 25 directed tab pairs, Settings trial/rollback, real HUD preview and draft return, Social/News background ownership, training/update recovery, three replay openings and menu audio lifecycle | Final13 `actual-main-reopen-640x360-retry`; final11 `actual-main-reopen-960x600` | Both sizes pass |
| Actual Studio authoring, four views, real graph drags, spatial analysis, comparison, custom cues and portable Library import | Final11 `actual-studio-reopen-960x600` | Pass |
| Real seven-Hunter/four-suit geometry and headroom at 640×360, 960×600 and 1512×818 | Final12 `real-hunters-*` | All three pass |
| Actual composed license and all rank presentations | Final12 `license-card-*` | Both sizes pass |
| Viewer/editor capability boundary | Final12 `playback-capability-960x600` | Pass |
| Exact PCM identity and non-silent output for all five installed-ROM menu cues | Final13 `owned-menu-audio` | Pass |
| Replay control/camera and recording-format checks | `build-attempt9/acceptance/` | Pass |

Root visually reviewed the final precision-field width, transparent five-star card, full-size Trace/platform composition, and actual Social/News backgrounds. Automated actual-window checks exercise production native input and renderer ownership with isolated test user data and a local recording. They do not certify physical controller/IME delivery, human audibility, live account/matchmaking services, other operating systems or refresh-rate performance.

Failed attempts remain preserved. Qualification10 exposed a genuinely collapsed high-density Settings form; the secondary controls moved inside the natural scroller. Visual review then caught the collapsed numeric precision field, corrected with a fixed basis and separate readout line. A compact actual-window audio test ran unfocused, where production correctly pauses music; its fixture now obtains actual window focus before asserting active playback. Final13's first compact run transiently failed to focus News after Social return; the unchanged full rerun passed. The dormant Avalonia `replayuicheck` also fails the RML96 baseline and is not the native replay qualification; actual native viewer/editor and replay control/format checks pass.

## Replay fidelity boundaries

Spatial analysis uses facts present in the recording. Current recordings lack the old authority weapon-inventory tail, so weapon-gain-only pickup history cannot be recreated; switches are not fabricated as pickups. Sightlines are directional proxies rather than occlusion proofs. World comparison reports a normalized strict canonical graph and retains raw differences/full capsules: only explicitly source-proven renderer caches and allocation ordinals are normalized. Simulation fields, graph aliases/topology, room activation and non-cache asset values remain checked. These boundaries appear in the native tools and supporting replay evidence.

## Package

The signed, independently cloned game and Studio bundles are staged from final13 under `artifacts/ProjectPrime-RmlUi-macOS-arm64-rml97-candidate3-20261018/`. Its provenance pins compiled inventories, authored source, native ABI/library, reused wrapper/runtime, signing entitlements and the inherited-code/resource-delta receipts. The final delivery receipt records archive size/hash and successful packaged window, no-game-assets, extracted signature/inventory and startup checks.

Keep both apps together after extracting the delivered ZIP. The package includes the user-supplied menu MP3; installed game data, accounts, saves and recordings remain outside it.


Delivered archive: `/Users/jarrett/Downloads/ProjectPrime-RmlUi-macOS-arm64-rml97-20261018.zip`
(185,278,311 bytes), SHA-256 `d1479169acc1ecad61c151213c16a42387c786bd61e1f395c9faa3abc71d458c`.
Both packaged render/resize checks, both extracted startup checks, strict signatures,
exact inventories and the no-game-assets check pass. See `downloads-delivery.json`
and `checks/` in the candidate3 package evidence.
