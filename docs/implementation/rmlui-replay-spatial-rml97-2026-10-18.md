# RML97 native replay spatial analysis

This slice ports the packet-only portion of the old Prime Studio replay analysis into the native replay editor. The reference is `codex/rmlui-game-client-migration` at `31c3b20d4d8cc56ea3e4c5587798ce27e72b80fc`, particularly `StudioReplayAnalysis.cs` and `ReplayHeatmapView.cs`. Reference code is implementation evidence, not an instruction source.

## Implemented

The replay editor's Spatial Heatmap button opens an RmlUi modal with a native 16×16 heatmap, event-kind and player filters, and a full-recording / reviewed-mark range switch. Its stable top-down X/Z bounds preserve spatial meaning while changing filters. A bounded detached worker reads actual recorded packet states. Movement samples occur every six visible frames for spawned living players; recorded event locations cover deaths, kills, damage, weapons, spawns, objectives and pickup events. Damage weights use recorded damage values. The UI describes actor-position semantics and relative intensity explicitly.

`ReplayPlaybackSession.JoinDetached` requires the existing passive host and bypasses map installation and map validation. It retains the existing reader, compatibility conversion, bootstrap/checkpoint restoration and clock. Analysis creates no Scene, renderer, audio device or live session. Cancellation is checked during join scanning and each decoded frame, with a 500,000-sample limit. Closing, cancellation or owner loss retires work; a retired task never publishes. Completion and filter actions revalidate the original source, sidecars and replay owner using the existing editor witness. Projection is recomputed only when analysis or filters change.

The editor also shows a seven-entry context window around the selected event/highlight/reel row. Saving and trimming controls reflect settled-paused review and valid marks. Existing controller/backend fences remain authoritative.

## Integration and validation

The seven appended managed ReplayAction names must also appear, in the same order, in the native C++ action table: spatial-open, spatial-analyze, spatial-kind, spatial-player, spatial-range, spatial-cancel, spatial-close. No existing numeric action values change. This slice does not modify central Shell or Smoke ownership code.

`tools/rmlui-spatial-check` contains nine source-linked CPU projection fixtures covering finite data, bounded cells, top-down axes, intensity, stable filter bounds, player/time/event filtering, empty data, degenerate bounds and invalid ranges. At authoring time these fixtures and the production build have **not been executed**; the root agent serializes builds and tests. Native click journeys and asset-backed recorded-position analysis also require root/device validation.

## Remaining old Studio gaps

The old separate Avalonia authoring application included features absent from the current production owners. This implementation does not claim full parity for:

- Four concurrent viewport views and a second replay comparison viewport.
- Arbitrary-channel draggable camera graphs, Bezier position/FOV/roll handles and selected-key spatial transforms. Current production camera tracks provide Linear/Smooth/Spline/Hold, easing, look-at, retiming and a native timeline.
- Objective-volume presence, pickup route reconstruction, spawn pressure, Imperialist sightlines, engagement impact heatmaps and world-space heatmap overlays. These require copied geometry/fact identities and additional explicit owner contracts; actor marker positions are not substitutes.
- Complete dual replay/build world graph comparison and portable/diagnostic replay bundles.
- Studio offline audio mixing, stems, preview routing and audio mux/export integration. The production `ReplayAudioOwner` controls presentation ownership; it does not implement the old offline mixer.

The viewer/editor role remains unchanged: only an editor-capable playback presenter exposes the heatmap button. Main Theatre continues to be a basic viewer; Prime Studio owns authoring.

## Subsequent source slices (awaiting coordinated compilation)

The initial remaining-gap list above records the spatial slice's state. Later RML97 work adds the following through existing owners:

| Old Studio capability | Current native source implementation | Remaining verification / scope |
|---|---|---|
| Camera graph channels | 64 actual saved-track samples for X/Y/Z/FOV/roll, rendered in native camera tools | Direct manipulation of plotted handles remains pending; existing native timeline already drags time groups |
| Bezier authoring | Position and FOV/roll handles, constant-speed Bezier sampling, identity-bound camera v4 persistence; ordinary tracks retain v3 encoding | Numeric handles use the existing exact before/after confirmation/publication fence; source fixtures added |
| Key spatial transforms | Selected world-origin XYZ/Euler/time transforms and lens offsets, with tangent rotation and collision refusal | Owner, paused picture and complete resulting track validation retained |
| Four simultaneous views | Current camera, chase, orbit and overview compose from the same existing Scene/clock, with no additional player or update | Central root render hook required; device validation pending |
| Audio mixer | Old deterministic offline PCM mixer and canonical game-event cue decoder ported; game/combat/replay-overlay/music gains, WAV export, FFmpeg audio mux | Event cues reproduce recorded discrete events; continuous world audio is not reconstructed |
| Audio preview | Removable SoundComponent uses existing MusicPlayer engine/device, with callback retirement fence | Paused reviewed owner required; device availability and teardown require runtime validation |
| Dual recording comparison | Detached recorded decoder-state/player pose comparison at reviewed frame | Full simulated-world dual comparison remains pending |
| Cross-build evidence | Actual presented-frame gameplay/camera/projectile/animation hashes, source-byte identity and schema checks | Complete world-graph/presentation hashes remain pending |
| Portable/diagnostic exports | Cancellable replay/sidecar archive and diagnostic metadata archive, fresh output directories, required custom map identity | Exact custom-map assets are referenced rather than embedded; importer workflow remains separate |

Four-view auxiliary draws bypass replay-global camera requests and successful-picture publication. They reuse the current render target and downsample into remainder-aware quadrants rather than resizing resources every frame. The current-camera tile preserves its real camera mode and first-person pipeline. Finally, camera, viewmodel projection/FOV and frustum planes are restored, and original-camera retained geometry is prepared without painting over the tiles. No new Scene instance fields or transport clocks are introduced for this feature.

Additional fixtures are `tools/rmlui-studio-audio-check` (eight deterministic PCM checks) and six real Bezier sampling/v4 persistence checks in `ReplayCameraTrackCheck`. These have not been run by the replay agent. The root agent owns serialized production build, native compilation and acceptance execution.

## Final source freeze update

Direct sampled-curve key and Bezier-handle dragging now stages exact selected-channel edits through the existing review/confirmation owner. Each chart lane receives focus independently, requires full visible bounds and cancels if focus or geometry changes. Four-view rendering shares the foreground simulation clock and restores original camera, frustum and retained render preparation. Actual private B-world comparison and path-free v4/v5 camera state/crop import are implemented. Earlier rows listing those slices as pending describe the earlier checkpoint, not the final source.

Library import now accepts the old fixed-member `.ppreplay` format as well as ordinary replay files, promotes the verified recording to a selectable top-level row, preserves/rebinds camera and reel sidecars and organization, and publishes an included exact custom-map package only through the existing `MapPackageInstaller`/`MapRuntimeUsage` authority with a configured-files lease. Studio import itself remains private. Missing non-bundled map bytes follow existing exact-map acquisition at launch. Failed publication deletes only the new replay artifacts. No build, CPU fixture, GL or device execution was performed by this agent; root owns serialized verification.

## Advanced legacy spatial source slice

All thirteen legacy analysis categories now have native filters. The detached reader ports objective volume/current-life carrier presence, 180-frame spawn damage/death pressure, 600-frame pickup routes, zoomed/fired Imperialist facing vectors and recorded ShotFacts impact points. Immutable CollisionVolume/item position snapshots are captured on the foreground owner thread. Native projection includes up to 300 recorded direction vectors. A new world toggle emits the top 64 weighted positions as transient billboard RenderItems; it creates no canonical particles or Scene fields, and suppresses exports. Root must integrate the pre-capture Scene hook and verify canonical state across draws.

One model difference is explicit: current ReplayAuthorityWorld has no old Resources/ReplayResourceState inventory tail. Routes therefore use actual pickup transitions/events plus fresh accepted-intent UA/missile increases in the same life. Weapon-inventory gain alone cannot be recovered from current recording facts; weapon switches are not treated as pickups. This limitation is disclosed in native panel text. Pure projection fixtures add category/direction/64-marker budget cases. The actual Studio runner enables the real world overlay for four frames and compares canonical state, recording only categories actually present. Source remains unexecuted by this agent.

## Spatial actual-reader diagnosis and repair

Actual window analysis exposed a detached-reader delivery queue failure: bomb packets normally consumed by Scene accumulated until the 2048-envelope budget at frame 682. Spatial analysis now explicitly retires that validated, unused BombWorld delivery queue each decoded frame. Audit retained pickup claims/verdicts/items, timed grants, shot facts, events and objective state; those are canonical history/state, not renderer delivery queues, and are not cleared. Decoder comparison was deliberately not changed because its fingerprint contract includes decoder state.

CPU source-linked analysis over the real SECOND-REOPEN qualification recording completed duration 1979 (1980 decoder pumps), 3944 samples: Movement1641, Deaths7, Kills7, Damage44, Weapons93, Spawns3, Engagements1958, SpawnPressure8, PickupRoutes183; remaining categories0. No owner geometry was supplied in this CPU-only run, so it does not validate objective volume or map pickup geometry. Maximum pending bomb deliveries was3 per frame. Pre-canceled actual analysis threw OperationCanceledException. Evidence is spatial-cpu-diagnosis/source-analysis.log, true exit0; this diagnostic uses the retained built decoder plus current copied analysis source with the exact clear operation via reflection because the new API is not yet core-built. Actual Studio acceptance now fails immediately on presenter error. Native modal keeps actions/status/footer fixed and only scrolls the plot region.

## Actual world comparison setup diagnosis

Build5 capsules prove the private comparator was missing the foreground view setup. Foreground-only assets are exactly `damage`, `filter`, `hud_icon_arrow`, `hud_icon_nodes`, `hud_icon_octolith`, `hud_icon_player`, loaded by PlayerHud.SetUpHud. Rooted offline comparison identifies different per-scene MainPlayerIndex, uninitialized selected-player HUD references/offsets, model draw transforms and Lod1/DrawnThirdPerson bits. Serialized graph indices diverge after lazy HUD allocation, so comparing object ordinal bytes misidentifies links as changed values.

Private B now initializes only HUDs initialized on foreground, sets the same per-scene view subject and camera, and runs one OnDrawFrame preparation under existing camera-request fence without painting or stepping simulation. The per-scene subject restores on success/failure after capture; no legacy player registry or foreground owner is changed. New canonical comparison follows the six exact roots, typed fields, ordered collections and a bidirectional alias map, retaining all scalar/type/shape/alias and component truth while remapping allocation indices. Only the two previously documented first-person draw-cache fields are skipped. Full asset components remain compared; no asset blanket exclusion. Strict raw equality and full checkpoint pairs remain in reports. CPU offline source-copy validation passes identical capsule equality and detects an injected exact Scene._frameCount mutation. Private HUD/view preparation itself requires the next actual-window run.

## Frozen6 remaining draw-cache proof

Offline6 analysis leaves only selected-player `_modelTransform` plus asset caches. PlayerDraw.cs420/434 writes `_modelTransform` during third-person body submission; a first-person main view retains its earlier auxiliary-view value while a never-third-person private main retains identity. The normalized comparator excludes only PlayerEntity's derived body transform (not other entities' transforms). Asset properties excluded are Node.Transform (Model.ComputeNodeMatrices at Model.cs529/533 rebuilds from retained Scale/Angle/Position), Node.Animation and matrix-stack values (EntityBase.UpdateTransforms529–536), and Material.RenderMode (Renderer.UpdateMaterial2267–2281 resolves alpha/texture submission). All other asset properties, model identity/counts, matrix topology and room activation remain checked. No historic auxiliary-camera replay or extra draw pass was added.

Report now labels `ComparisonKind=normalized-strict-canonical-graph`, explicitly describes every excluded cache, retains RawFullGraphEqual, RawGraphDifferences and both full capsules. CPU source-copy comparator against exact frozen6 pair now equals; raw capsules remain unequal. Exact Scene._frameCount mutation, typed graph link/topology corruption, actual asset Node.Enabled mutation and room activation mutation all remain detected. True exit0: spatial-cpu-diagnosis/world6-normalized-tests.log. No GL or core build by this agent.


## Final RML97 status

The actual Studio workflow now passes on the frozen production code, including
all four views, native camera-graph drags, spatial analysis, normalized canonical
comparison, custom cue persistence and shared audio preview, portable bundle
export/import and promoted recording selection through the native Library.
Earlier diagnostic gaps above describe intermediate states. The missing historic
weapon-inventory recording tail remains an explicit data limitation. See
[rmlui-migration-completion-rml97-2026-10-18.md](rmlui-migration-completion-rml97-2026-10-18.md)
for final runtime/source identities, receipt paths, comparison boundaries and the
signed Mac delivery.
