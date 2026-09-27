# Enhanced radar

The radar remains the existing `core.radar` HUD element. Basic, Minimal, Ring and
Square retain enum values 0–3; Tactical, Holographic, Scanner and Competitive are
appended. Classic and Project Prime still default to Basic. Competitive, Minimal,
Accessibility and Broadcast use the corresponding radar presets. Radar-only
presets never change the element's anchor, position, opacity, tint or scale.

## Shared presentation pipeline

- `HudRadarProfile.cs`: additive schema-1 settings, safe defaults and validation.
  Range is restricted to 0.5–1 times the legacy 24-unit radius. Trails are 0–4.
- `HudRadarProjection.cs`: engine-handed Heading-Up/North-Up bases, circle/square
  clipping, reduced range, Hide/Clamp/Edge Arrow and vertical classification.
- `HudRadarStyles.cs`: style catalog and private, precompiled tick/segment
  directions. Runtime profile construction initializes the shared templates.
- `HudRadarGeometry.cs`: caller-owned spans for frames, contact polygons,
  elevation chevrons, edge arrows, self markers and conservative editor bounds.
- `PlayerEntityRadar.cs`: collection, ordering and final OpenGL drawing. The
  reusable 4,096-contact buffer prioritizes hunters, authorized objectives,
  weapons and then powerups. Basic preserves the original pickup traversal order.
  Extremely dense maps can omit trailing pickups at capacity.
- `HudStudioCanvas`: consumes the same projection, marker geometry, style catalog
  and bounds. `HudRadarPreview` supplies deterministic elevated, distant, friendly,
  enemy, pickup and objective samples. Avalonia and OpenGL are the only distinct
  drawing backends; their fonts and circle rasterizers still differ.

Hunter and pickup eligibility is unchanged. Objective semantics are attached to
native locator inputs. Bounty, Capture and Defender reuse their locator-only
methods. Nodes and Prime Hunter share extracted locator-only helpers with their
native HUD processors. Replay collection uses a separate locator scratch list and calls these helpers because replica
simulation deliberately skips the foreground mode-HUD processor. No HUD timers,
messages, animations, network requests or simulation state advance in collection.
Survival's special reveal pipeline is not treated as a new objective source.

## Motion and lifecycle

Positions use existing translation-only presentation sampling, including the
replica pose stream. Its cursor is prepared once per simulation presentation
capture so radar sampling does not decode replay records inside drawing. Camera heading determines Heading-Up orientation. Scanner
phase uses scene simulation frames plus the scene's presentation fraction; replay
pause freezes that fraction. The sweep changes appearance only.

Fixed hunter histories are sampled by `ModCaptureDrawState`, once per simulation
tick, and cleared by `ModResetDrawState`. Eligibility loss, generation/life changes,
match/authority changes, backwards/discontinuous time and teleports clear them.
Replay seeks create a replacement scene, so old histories cannot cross a seek;
checkpoint schemas do not serialize the radar buffers or histories. Replica
history reads replica lifecycle identity and never falls back to live-session
identity. Trails of contacts hidden by the current range policy are not drawn.
Reduce Motion suppresses trails and sweep/pulse animation while retaining contacts.
Reduce Transparency uses the HUD's 0.85 minimum alpha.

## Editor

All radar properties participate in existing profile serialization, validation,
undo/redo and element reset. The inspector groups appearance, orientation, contacts
and motion. Scanner-only controls are conditional. Trail count uses whole-number
increments. Selection, dragging, snapping and group bounds include radius, marker
size, elevation indicators, frame strokes and glow. No radar/kill-feed automatic
repositioning or new HUD layering system is introduced.

## Validation commands

```sh
dotnet run --project tools/hud-check
dotnet run --project tools/hud-ui-check
dotnet build src/MphRead/MphRead.csproj
dotnet run --project src/MphRead -- -hudradarpreview OUTPUT_DIRECTORY
dotnet run --project src/MphRead -- -hudpreview OUTPUT_DIRECTORY
```

`-radarcheck DIRECTORY` runs the real contact collector against existing `.ppdemo`
fixtures named Bounty, BountyTeams, Capture, Defender, DefenderTeams, Nodes,
NodesTeams and PrimeHunter. It checks eligibility, filters, objective presence,
warmed allocations and unchanged gameplay hashes. It needs extracted game assets. `-radarcheck "room:MP1 SANCTORUS"` generates
current-protocol synthetic fixtures, selecting another installed room when the
requested room lacks that mode's objectives. Prime winner state is seeded by the
diagnostic before collection because the generic actor fixture has no winner.
`-hudmetrics` now reports radar-specific warmed bytes, peak bytes and timing, plus
allocation totals for setup, frame, collection, trails, contacts and self/labels.
The first 16 draws are excluded from radar metrics for initialization.

The pure suite covers old profiles, enum/range validation, roundtrips, reset,
independent cardinal-direction expectations, square/circle boundaries, elevation
including directly-overhead contacts, style bounds, Basic geometry fixtures,
Reduce Motion, Reduce Transparency, trail lifecycle and 60/120/144/240 Hz sampling.
It measures shared geometry with preallocated spans. The UI suite also exercises
all presets, dynamic bounds, undo/redo/reset, large-radar dragging and portrait
layout. Real GL metrics are required in addition to those allocation tests.

Platform execution and screenshot evidence for this implementation is recorded in
`hud-customization-status.md`; cross-compilation is not native device validation.
