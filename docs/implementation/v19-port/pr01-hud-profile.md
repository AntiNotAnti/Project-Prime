# V19 PR-01: single HUD/crosshair profile and forward-safe v2 storage

**Based on:** main `f17a5de`, actual live protocol **56**. No change to wire, simulation, replays or render backend.

## Changed behavior

* Current launcher still uses Avalonia. Its existing **HUD Studio** is surfaced as `CUSTOM HUD / CROSSHAIR`, the primary editor for both HUD elements and weapon/zoom crosshair profiles.
* `HudProfile` moves from schema **1 -> 2**, with a **pure** v1-to-v2 migration that retains the old scale, element anchors, colors, per-weapon reticles, independent gauges, radar and killfeed settings. Do not borrow v19's migration multiplication of legacy reticle scales: that assumes a different codebase.
* Schema-less saved profiles are treated as v1 and upgraded.
* `HudProfile` carries inert `JsonExtensionData` for **unknown top-level v19 schema-2 fields**, including future `decorations`, `typography` and `haloLowHealthSound`. Those settings survive import/save/export without claiming their renderers already exist. Each dedicated feature PR must validate and explicitly adopt its fields before use.
* `Halo` and `Approved 4:3` source `basePreset` names are preserved if imported; this PR deliberately **does not expose** incomplete visual presets in the selectable presets list.
* The sole published `HudProfiles.Runtime` remains a compiled render snapshot. Legacy `Features` toggles continue as compatibility projections; they are not a new independent profile store.
* `HudProfileStore` continues bounded JSON parsing, atomic settings write and recovery backups.

## Coverage

Feature **1** (unified editor entry), **17** (retain layouts/meters), **23** (schema migration, profile import/share/reset). Their full UI/visual acceptance is gated by later stacked HUD, custom crosshair, typography and preset PRs.

## Tests / not yet certified

Run from project root:

```bash
dotnet run --project tools/hud-check/hud-check.csproj -c Release
dotnet run --project tools/hud-ui-check/hud-ui-check.csproj -c Release
dotnet build src/MphRead.sln -c Release
```

New `hud-check` assertions cover: v1 geometry/scale/meter preservation, unversioned input, inert v19 themes/decorations/typography roundtrip, rejection of future schema, and continued save/backup recovery.

**NOT RUN here:** .NET SDK unavailable in the assistant execution container. Check Windows/Linux/Android, user data migration/backups, screen-reader labels and visual parity before merge.

## Dependencies

PR-00 must reach accepted baseline before merge. PR-02/03/04/05/06/07/08 should branch/rebase after this storage contract is merged. No RmlUi migration is part of this work.
