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
- Broader map geometry/assets (including the local DUST2 collision edits), model
  import/export, editor/modeling, universal/packed-scene and source-model
  runtime changes, plus their dedicated tools and fixtures.

Minimal map metadata remains for CTZ/Payload rules and MP4 playback/package
validation. These fields support retained gameplay/rendering. The existing main offline custom-map picker
implementation is retained, including same-session install refresh behavior.
The two explicitly requested authored runtime packages, `capture-the-zone.ppmap`
and `payload-district.ppmap`, are included byte-for-byte from the preserved local
integration. They carry their authored geometry/material inputs and objective
metadata without new authoring/model pipelines or loose source folders. They
need no extracted game assets to compile. Built-in CTF maps remain
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
- Exact scope checks confirm broader map/model/editor and character pipeline paths
  match main, eight map runtime metadata files are explicitly allowlisted,
  retained HUD/renderer/camera files match the full local integration, and the
  original local checkout remains clean on its preserved integration commit.

## PR #1051 CI and authored packages

- Extracted the unchanged pure scene-color shader helper so the isolated Android
  pacing project can compile it. Shader sync hashes remain identical; Android
  pacing passed 70 assertions and renderer preparation passed 14,748 assertions.
- Updated current live-protocol tests to 86 and future-version refusal to 87,
  retaining historical 85 archive fixtures and explicit old-live-version refusal.
  Party checks verify the exact UMF1 capability suffix and reject missing/wrong
  capabilities. All 41 replay/Momentum commands, 153 Party checks and 1,267 group
  checks passed locally.
- Updated the source-linked Party owner fixture to use the actual public mode
  policy/refusal codec and UMF1 admission. Its 85 checks cover hero-mode refusal
  and ordinary queue admission; retry waits now observe owner-applied results
  before advancing the existing clocks. Production queue behavior is unchanged.
- Kept the GL comparator's strict hashes, dimensions, pixel floor and exact
  inventory checks, extending that inventory to all 42 retained captures. Its
  14 tests passed; Linux CI and Windows evidence each self-compare successfully.
- Refreshed the controller fixture after installing its synthetic arena files;
  launch remains disabled when compatible arena data is absent. Gamepad, pointer
  and map-load-receipt checks passed with isolated profiles.
- Both supplied authored bundles passed 31 strict reader, dependency, gameplay
  schema, compiler and native packer assertions against an empty game-data root.
  CI now runs this check. The asset guard adds exact exceptions for these two
  packages and the four inherited user-supplied CTF banner/announcement resources;
  it adds no wildcard exception for map content or extracted game assets.
- Both authored maps passed native eight-player scene tests and real OpenGL
  checks with copied owned assets, including CTZ HUD and Payload cargo/rider
  movement. Package input hashes remain identical to the preserved local source.
- Desktop Release, dedicated server Release and Android Debug APK builds passed
  again after the CI repairs (6, 1 and 27 existing warnings respectively).
- Normal zero-step CTF first presentation and same-window rematch passed again
  on Alinos Perch and expanded Fault Line with seven bots and linear color.

Generated binaries, cloned owned game assets, profiles, captures and logs stay
in ignored local artifact folders. No Internet multiplayer or Android physical
device execution is claimed.
