# RmlUi living chamber home

The RmlUi home uses the Project Prime deployment chamber as a persistent hero scene rather than a static backdrop. This slice adds peripheral motion and functional side rails so the screen stays visually active without competing with the selected Hunter.

## Background life

The existing chamber renderer remains the far/midground source of truth. The presentation-only `LauncherStageFx` layer now adds:

- two recessed side-bay telemetry fields behind the left and right UI zones;
- deterministic animated signal bars driven by the current activity accent;
- slow scanner sweeps across both side bays;
- orbiting service lamps around the Hunter bay;
- runway pips travelling toward the center platform;
- a wider field of low-density energy dust;
- per-Hunter ambient motifs:
  - Samus: scanner bands;
  - Kanden: unstable organic pulses;
  - Trace: stealth scan slivers;
  - Sylux: cold power nodes;
  - Noxus: crystalline points;
  - Spire: ember orbit and floor shimmer;
  - Weavel: split mechanical rails.

All motion freezes when Reduce Menu Motion is enabled. The effects are screen-space presentation only and never mutate gameplay entities, Hunter materials, match state, or the chamber shader layout.

## Left side rail

The unused upper-left space now hosts a low-opacity **Deployment Link** panel. It mirrors only live state that already exists in the home model:

- selected activity;
- selected Hunter;
- activity/status hint;
- decorative link-strength bars.

The main activity selector remains the primary action surface at the lower left.

## Right side rail

Below Current Session, the home now includes a **Social Activity** panel backed by `SocialRuntime.Summary`.

It publishes:

- friends online;
- incoming game/party invites;
- incoming friend requests;
- up to three online friends with current activity and JOINABLE state;
- a real route to the full Social page.

The panel is populated on the existing 250 ms home-social cadence and does not perform new network queries.

## Responsive behavior

Both side rails are intentionally secondary. They are hidden below 1180 dp so the Hunter, activity selector, and primary session controls retain priority on narrower layouts. The 1500 dp and 760 dp breakpoints compact their dimensions.

## Validation

`tools/rmlui-ux-check` now publishes representative home social data and verifies both new side modules and friend rows remain inside the viewport at desktop sizes. `tools/rmlui/full-source-check.sh` runs that real-native DOM/layout check against the exact client assembly and native bridge used by the full RmlUi acceptance job.

The chamber renderer itself is unchanged in this slice, so the generated modern shader/WGSL layout remains authoritative and does not require regeneration.
