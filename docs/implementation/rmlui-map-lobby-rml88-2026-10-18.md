# RML-88 map picker and lobby usability

Date: October 18, 2026. Work remains exclusive to RmlUi; Settings stays last.

## Changes

The map picker opens its MPH cards immediately. Installed-package hashing runs
on a serialized background task with the existing configured-files lease.
Closing, selecting, disposing or changing the rule session retires publication.
The installed registry constructs outside the shared catalog lock and publishes
only against the unchanged definition snapshot. Deferred UI registration can
proceed while cold archive verification runs. Exact package validation and
authoritative rule submission remain in place.

Tabs are MPH, Echoes, Halo, Quake and Community. Legacy packages lack a uniform
origin field, so title/description aliases and importer metadata provide display
grouping; this grants neither provenance nor hosting permission. Community
contains all installed custom packages. Qualified local-source Echoes playtests
are explicitly marked unavailable for multiplayer and cannot enter a network draft.

All six visible cards bind preview images. Existing PNGs decode off the UI
thread; missing previews use the existing thumbnail owner, one render request
at a time. Page/root changes cancel obsolete work and cannot publish another
room's image. Dark loading/unavailable states replace unbound white rectangles.
Cards scroll independently of the tab bar and pager.

Three Home party platforms are smaller and separated from one another and the
foreground pad. The procedural chamber, foreground pad and eight-player Lobby
formation remain intact. Current Session retains four seats.

Lobby publishes the missing Start-enable binding, with fresh active, owner,
readiness and Starting gates at dispatch. Readiness text reports existing
validation reasons. Chat uses a bounded flex layout that keeps its field and
Send button inside the panel at compact sizes.

## Qualification

Client/server and six tools compile with zero warnings/errors. Final client:
`c38779d88e865b76603b14684efd83ecd9efa5431d6159129b1ae0bb9911124e`.
Native bridge:
`4e49ac04efa2d9980c7c3d1cfcc2a8cc2146cc4913aaa357fbd9c9387c49784a`.

Five CPU suites pass under
`artifacts/rmlui-map-lobby-rml88-20261018/acceptance/cpu-attempt2/`.
Final native evidence is under `build-attempt3/acceptance/`. Its production
inputs, managed/native binaries and CPU tool sources match build-attempt2.
The explicit reuse proof records the sole subsequent change: the GL fixture
presses End before asserting text appended after a caret-repositioning click.
Earlier fixture compile/key-code/caret failures are preserved in prior attempts.

Eight native cases pass 1,103 checks: map picker and Lobby interactions at
640×360, 960×600 and 1280×720 client sizes with explicit Retina density 2;
actual RenderWindow Home/Lobby at 960×600 and 1280×720 with private owned Hunter
assets and a real AD1 thumbnail. Synthetic action authority never starts a real
match or sends live chat. Coverage includes five native tab commands, immediate
MPH selection with a held catalog, texture pixels, scrolling/paging, disabled
local-only maps, Start pointer/Enter activation, pointer-first chat focus,
Unicode text, Backspace, Enter/Send, draft retention and modal/stale-scope
retirement. Two headless cases add 167 checks for real PNG decoding, serialized
thumbnail requests, cancellation/root/disposal races and cold-registry races.
Representative visuals across all sizes/categories pass review. Emoji font
coverage remains limited; exact Unicode input survives in the field/service.

Physical Mac input remains **INCOMPLETE**. A signed candidate ran in a private
profile with independent copies of 3,858 owned files and 27 installed archives
(2,071,849,686 bytes). Desktop inspection repeatedly timed out binding the exact
running app. No physical typing was injected or claimed. The private process
group and an inspection-launched candidate instance were terminated; no account
actions were sent and the user's original running application was retained.
Native scoped input acceptance is separate from physical callback acceptance.

## Delivery

Final package evidence:
`artifacts/ProjectPrime-RmlUi-macOS-arm64-rml88-20261018-final/`.
Ad hoc signatures, extracted inventory, bundled/extracted startup, packaged
Home/resize and actual AD1 thumbnail generation pass. The bundled runtime is
.NET 10.0.12; no game assets or player/account state are packaged.

ZIP: `/Users/jarrett/Downloads/ProjectPrime-RmlUi-macOS-arm64-rml88-20261018.zip`.
85,194,278 bytes; SHA-256
`2379a9cddba29e6f61675c01248c1cc629e71cde3b6471a3ad60c80fce961939`.
The earlier signed build-attempt2 candidate is preserved and unarchived.

The wider migration remains in progress. Native Install & Host needs an exact
package continuation into a reviewed native custom-map creation form; remaining
Hunter customization, rich-screen handoffs and final Settings integration remain.
