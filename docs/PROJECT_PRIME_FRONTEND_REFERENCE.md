# Project Prime frontend reference (UI0)

This document is the durable native-client reference for the Project Prime game
shell. It records the route, component, token, responsive, motion, and status
decisions extracted from the supplied `Project Prime: Online` prototype and the
implementation plan. The prototype is a design reference only; the production
runtime is the existing C# and Avalonia client.

## Production routes

The authenticated shell exposes exactly these root routes:

```text
PLAY
HUNTER LICENSE
ARMORY
THEATER
RANKINGS
SETTINGS
```

`GATEWAY` is a conditional pre-session state, not a navigation destination.
`HUNTER LICENSE` merges the prototype's Hunters and License concepts and owns
the sections `OVERVIEW`, `HUNTERS`, `CAREER`, and `MATCHES`.

| Route | Responsibility | Back behavior |
| --- | --- | --- |
| Gateway | Restore, sign in, register, confirm, resend, guest entry | Returns to startup or closes the conditional state |
| Play | Node directory, Node session, lobbies, lobby configuration, handoff | Internal Play state first, then prior root |
| Hunter License | Identity, career, roster, favorite, match history | Section first, then prior root |
| Armory | Canonical local weapon reference | Prior root |
| Theater | Local `DemoLibrary` and `DemoPlayback` actions | Prior root |
| Rankings | Official leaderboard metrics and pages | Prior root |
| Settings | Existing settings persistence grouped by category | Prior root |

Selecting a root navigation item replaces the root route. Escape/B first closes
an internal state or bounded history entry and never accidentally exits a match.

## Shared visual language

All new shell colors come from the Prime theme resources. Values are taken from
the supplied plan; no web font, image URL, or remote runtime is a production
dependency.

| Token | Value | Use |
| --- | --- | --- |
| Background Deep | `#0A0E16` | Shell backdrop |
| Background Main | `#0F131C` | Main content |
| Panel | `#181C24` | Cards and navigation |
| Panel Raised | `#1C2028` | Focused/raised cards |
| Control | `#262A33` | Inputs and secondary controls |
| Border | `#3B494C` | Dividers and focus outlines |
| Text Primary | `#DFE2EE` | Normal copy |
| Text Bright | `#C3F5FF` | Selected and telemetry copy |
| Text Muted | `#849396` | Supporting copy |
| Prime Cyan | `#00E5FF` | Primary accent |
| Prime Cyan Dark | `#00DAF3` | Pressed/secondary accent |
| Warm Accent | `#FFB68D` | Non-status emphasis |
| Orange | `#FE7600` | Action emphasis |
| Warning | `#FFAA00` | Recoverable warning |
| Danger | `#FF5555` | Error/destructive action |

Inter is embedded through `Avalonia.Fonts.Inter`. New controls use uppercase
compact labels where useful, a visible focus outline, and a minimum touch target
of 44 device-independent pixels.

## Components and spacing

The shell owns the header/navigation, content host, notification strip, and
action bar. Screens own only their content and page-specific state. Reusable
building blocks are `PrimeButton`, `PrimeCard`, `PrimeTabs`, `PrimeStatusChip`,
`PrimeStatTile`, `PrimeSectionHeader`, `PrimeEmptyState`, `PrimeLoadingState`,
`PrimeModal`, `PrimeActionBar`, and `PrimeNavBar` (implemented as theme styles
and small native controls where a separate class is not useful).

Spacing follows a 4-pixel base grid: 4/8/12/16/24/32. Cards use a restrained
corner radius and a one-pixel border. The background is static/tiled; scanline,
bracket, and glow effects remain low-opacity and are disabled when reduced
effects are requested. Screens do not run a continuous animation loop.

## Responsive contract

One visual tree serves desktop and Android.

* Wide (`>=1100px`): full navigation, two or three content columns, larger
  previews and stat grids.
* Medium (`720-1099px`): compact navigation, two columns, smaller cards.
* Narrow (`<720px`): compact header, horizontally scrollable tabs, one column,
  touch-sized controls, bottom action hints, and reduced decoration.

Transitions are a single short opacity/translation transition (160-220ms) and
must honor reduced motion. Loading is localized: one slow career/history query
does not replace an already-loaded page with a full-screen spinner.

## Truthful status inventory

Every screen has explicit loading, loaded, empty, recoverable-error, and fatal
states. The shell status area reports only known facts: signed-in identity,
account eligibility, Backend connectivity, Node name/region, and the last input
device. It never displays fabricated latency, population, rank, history, social
presence, or progression.

| Area | Real source | Empty/recoverable state |
| --- | --- | --- |
| Identity | `AccountSession.Identity`, `GetLicenseAsync` | Gateway, then retry |
| Nodes | `AccountSession.GetNodesAsync` | “No compatible Nodes online” |
| Lobby | `NodeControlClient.State` | “No public lobbies” |
| Career/history | `GetCareerAsync`, `GetHistoryAsync` | “No official matches recorded” |
| Rankings | `GetLeaderboardAsync` | “No eligible players on this board” |
| Weapons | `Weapons.Current`, `Metadata.WeaponNames` | Canonical data only |
| Replays | `DemoLibrary`, `DemoPlayback` | “No local replays found” |
| Maps | Existing map catalog/thumbnail pipeline | Explicit no-preview state |
| Settings | `ClientSettings` and existing `SettingsView` | Validation/error text |

## Prototype screenshot references

These captured screenshots are the visual evidence set for UI0. They are local
artifacts and are not runtime assets:

* [Gateway](../output/playwright/project-prime-prototype/gateway.png)
* [Play](../output/playwright/project-prime-prototype/play.png)
* [Hunters](../output/playwright/project-prime-prototype/hunters.png)
* [License](../output/playwright/project-prime-prototype/license.png)
* [Armory](../output/playwright/project-prime-prototype/armory.png)
* [Theater](../output/playwright/project-prime-prototype/theater.png)
* [Rankings](../output/playwright/project-prime-prototype/rankings.png)
* [Settings](../output/playwright/project-prime-prototype/settings.png)

## Fake-data replacement/remove matrix

| Prototype item | Production decision | Source or reason |
| --- | --- | --- |
| Random latency/ping | Remove until measured Node RTT exists | No fabricated network telemetry |
| Fake ranking points/ranks | Replace with license/career rating | `HunterLicense`, `CareerRatingSummary` |
| Fake player rows, friends, rivals, online status | Remove | No social backend contract |
| Fake match history/results | Replace with paginated history | `GetHistoryAsync` |
| Fake account providers | Remove | Existing `AccountSession` email flow only |
| Remote Hunter/weapon imagery | Replace with generated, content-scoped local previews | `ModelPreviewCatalog`/`ModelPreviewGenerator`; no CDN/runtime download |
| Hunter skins, levels, currency, progression | Remove | No canonical client systems |
| Map preview art | Replace with existing local thumbnails | `ThumbnailGenerator`/`ThumbnailHost` |
| Weapon stats | Replace with `Weapons.Current` metadata | Canonical local game data |
| Replay cards/results | Replace with `DemoLibrary` metadata | Local recordings only |
| Featured/spectate/social surfaces | Defer/remove | No authoritative service support |
| “Connected” status | Show only when `NodeControlClient.Connected` | Live session state |

The V1 preview pipeline renders the finite metadata-backed Hunter and weapon
catalog in isolated desktop workers, validates the PNG, and caches it by content
identity and renderer version. Live 3D inspection remains a V2 renderer-mode
candidate. If generation is unavailable or fails, the UI must say so rather
than faking an image or downloading one.

Android shares the catalog, cache identity, decoder, and shell presentation,
but fresh-device Hunter/weapon generation still needs an Android-owned isolated
GL worker. The shared source exposes that platform hook and fails explicitly
until it is installed; cached valid previews remain readable. Do not mark the
Android asset-preview requirement accepted until that worker and a real-device
run are verified.
