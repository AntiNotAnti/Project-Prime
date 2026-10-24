# Filtered integration against main

This change starts from remote main `27ee7f2cc`, retains the earlier gameplay,
HUD, renderer and replay integration, and includes the subsequent CTF bot and
first-frame camera fixes. The complete local source remains on
`codex/local-integration-20261022` at `57d2473c4`.

Excluded from this PR:

- Competitive/ranked/progression work, Dust2Competitive and authored Lua
  competitive rules, including their input, UI, network and replay hooks.
- Character model assets, import/material/source-GX pipeline changes and
  character-specific tooling. Existing main character behavior remains intact.
- Map geometry/assets (including the local DUST2 collision package), model
  import/export, editor/modeling, universal/packed-scene and source-model
  runtime changes, plus their dedicated tools and fixtures.

Minimal map metadata remains for CTZ/Payload rules and MP4 playback/package
validation. These fields support retained gameplay/rendering; they add no
map models or authored map assets. The existing main offline custom-map picker
implementation is retained, including same-session install refresh behavior.
CTZ/Payload authored map packages stay local with the excluded map work; these
modes require a separately supplied compatible package. Built-in CTF maps remain
available and are exercised by the startup regression.

Retained rendering includes linear-color output, texture decode/residency
limits, MP4 playback, captured-output contracts, HUD layouts/settings/previews,
replay editing/export and the camera readiness fix. Optional existing source-GX
bodies keep their encoded color contract: generic scene admission uses native
color for those frames and resumes linear color when they leave the collected
draw list. Character programs and model dispatch remain unchanged.

The filtered mode contract uses protocol 86 with a distinct `UMF1` capability
tag. It does not advertise the full local integration's excluded source-model,
Lua or competitive wire contracts. Current main bot-AI and combat fixes remain.

Earlier dated integration and crash-review receipts describe the full local
build. Fresh validation of this filtered branch is recorded below before PR
submission; it is not inferred from those earlier results.

## Fresh validation

- Windows game and Studio Release builds, dedicated server Release and Android
  Debug APK builds passed. The builds retain existing warnings.
- Fourteen checks passed: frame timing/camera initialization, retained render
  graph, CTF/CTZ/Oddball/hero integration, CTF lobby, game/Studio smoke and window
  checks, real OpenGL rendering, and normal CTF startup/rematch on Alinos Perch
  and expanded Fault Line with seven bots and linear color enabled.
- OpenGL validation passed 585 pixel checks and 42 captures, including native
  source-body color fallback and linear-color resumption.
- HUD resources passed 43 assertions; the existing main offline custom-map
  catalog passed 20 assertions.
- Material/texture tooling built and passed 62 CPU assertions plus real OpenGL
  residency, mip, color/alpha, stable-binding and cleanup checks. Native renderer
  qualification tooling built with model-specific fixtures removed; nine Python
  capture-region/qualification checks passed.
- Real FFmpeg lossless RGB/H.264, packaging, OpenGL playback/color, admission,
  retry, retirement and teardown passed 163 video assertions. The adapted
  harness adds no model authoring dependency or editor-rendering claim.
- Lobby/UDP lifecycle tests passed 7,756 assertions. Advanced rules,
  architecture, replay owner tracks and checkpoint prefetch checks passed.
- Bot checks passed 99 assertions after correcting two current-main fixture
  assumptions about other-teammate occupancy and explicit recovery goals.
  Production bot policies remain identical to main. Two inherited corrupted
  Unicode lobby fixture strings were restored verbatim from main.
- Exact scope checks confirm map/model/editor and character pipeline paths
  match main, eight map runtime metadata files are explicitly allowlisted,
  retained HUD/renderer/camera files match the full local integration, and the
  original local checkout remains clean on its preserved integration commit.

Generated binaries, cloned owned game assets, profiles, captures and logs stay
in ignored local artifact folders. No Internet multiplayer or Android physical
device execution is claimed.
