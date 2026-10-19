# RmlUi Hunter License stars — RML90 — 2026-10-18

Build, owned-art acceptance, native GL qualification, ZIP packaging and local delivery have passed as detailed below. Wider RmlUi migration remains in progress, with Settings last.

The native Hunter License presents the original MPH ranks from existing backend-authoritative career data. Server tiers are 1-based; the original game’s rank levels are 0-based. The UI does not award points or create a tier from an unrated record. A null tier displays Unranked with no stars. Verified, consistent points and tier select the original title, color and star count; invalid or unverified facts fail closed. The next-rank display gives both the absolute threshold and points remaining. Legendary Hunter has no fabricated successor.

| Original level | Server tier / stars | Points | Title | Model / color | Next threshold |
|---|---|---|---|---|---|
| 0 | 1 | 0–39 | Bounty Hunter | redstar / red | 40 |
| 1 | 2 | 40–139 | Super Hunter | bronzestar / bronze | 140 |
| 2 | 3 | 140–389 | Elite Hunter | silverstar / silver | 390 |
| 3 | 4 | 390–749 | Master Hunter | goldstar / gold | 750 |
| 4 | 5 | 750–850 | Legendary Hunter | blackstar / black | — |

Original behavior and art references are `/Users/jarrett/Documents/MPH/Repos/SecondHunt/src/MphRecomp.Core/Frontend/HunterLicense.cs:72–83,256–267`, `MultiplayerMenu.cs:3193–3194,3333–3354`, and `RecompMenus.Replays.cs:765–785` in the same directory. The original gain/loss table uses local rank as rows and opponent rank as columns. Bots, unknown ranks and teammates are excluded; tied placements yield zero. Each eligible opponent contributes a delta, clamped to 0–850 after each opponent, with local rank fixed until the final recomputation. The existing Project Prime backend normalizes pair deltas by opponent count (maximum factor 3). Existing eligibility and persistence remain unchanged. The original-table audit therefore does not claim full reproduction of the original per-opponent algorithm or verification of production ingestion.

The five original models contain rows of one through five stars, each repeating one material/texture/palette pair. Each source texture is 32×32; each quad samples UV 0–18 on both axes. Native artwork therefore preserves the top-left 18×18 RGBA crop rather than stretching the whole bitmap. Red through gold have opaque black backing. Blackstar uses A3I5 alpha. Its palette includes white, but the visible crop contains only RGB black: 88 opaque pixels, 26 partially transparent pixels and 210 transparent pixels. Black pixels retain their original alpha and must never be keyed away; the native row supplies light backing for contrast. Original blackstar quads are approximately 17.6×18 versus 18×18 for the other colors; the native row preserves that width distinction.

Owned source assets remain external under the configured private root’s `files/AMHE1/gamerscard/`. The canonical read-only reference is `/Users/jarrett/Library/Application Support/Project Prime/files/AMHE1/gamerscard/`; private acceptance uses cloned files, never writes there, and bundles no extracted art. `src/MphRead/Metadata/FrontendMeta.cs` supplies the GamersCard mappings; `Formats/RawFormats.cs` and `Read.cs` define headers and pixel alpha handling.

The shared asynchronous `RmlUiLicenseArtworkCache` retains portrait behavior while adding rank artwork mode. It publishes content-addressed native TGA files under `rmlui-media/license-stars/`, runs source decoding away from the UI owner, and retires pending work on changed selection, empty selection, configured-root change or disposal. Account retirement clears rank facts and pending artwork through the presenter. Existing text/font fallbacks preserve readable rank information if artwork is unavailable. Portraits remain supported by the same cache with their original mode and dimensions.

`tools/license-star-check/sql.mjs` executes the checked-in pure helpers `project_prime_rating_tier` and `project_prime_rating_pair_delta` from `supabase/migrations/20260921195500_authoritative_career_ingestion.sql` in ephemeral in-memory PGlite. The recorded result is **PASS: 85 checks**: ten point boundaries and 75 pair cases (all 25 rank pairs, win/loss/tie). Output is `artifacts/rmlui-license-stars-rml90-20261018/acceptance/sql/proof.json`; the proof records source/helper/script hashes and dependency identity. This verifies those helpers only; it does not verify live SQL deployment, full ingestion, schema constraints, UI or GL. No live SQL deployment or ingestion run is claimed.

The RML89 native library is reused unchanged, SHA-256 `e053991e7e9e887574daf4e53ccd1b9b0f376025fa8642d7c822d794a201387f`. No physical Cocoa input or global smoke dispatcher qualification is claimed. The verified local archive delivery is recorded below.


## Qualification evidence

`artifacts/rmlui-license-stars-rml90-20261018/build-attempt1/` records a full client/server build plus seven tools with zero warnings and errors. The main binary SHA-256 is `094c72421bcb3d7bd531635900427d48e1b1422f4b3cd9d074d17723566e1d8f`; the native library remains the unchanged RML89 library above. The final asset-only stage-2 compiled-client manifest hash is `02f47bf95ab724c66575c472bc9a9116cedf95b3ed06c1351d51707719f3a00d`; it identifies the final staged client, not a second full compilation.

All six CPU suites passed under `acceptance/cpu-attempt1`, including 180 new `RML LICENSE STAR` checks. Actual owned-source acceptance passed for all five rank crops, cache behavior and alpha preservation under `build-attempt1/acceptance/owned-star-art`; all seven portrait regressions passed under `build-attempt1/acceptance/owned-portraits-regression`. These are headless acceptance results and do not extend to physical input.

`build-attempt2` is an explicitly **asset-only** release candidate: copied RCSS repairs black-star contrast and compact padding. All binaries are identical to build-attempt1; no managed compilation or new code/test changes occurred, and RCSS is the sole source repair. `asset-repair-proof` and `unchanged-code-and-tests-proof` record that boundary.

Final native GL qualification under `build-attempt2/acceptance` passed three viewport sizes—1280×720, 960×600 and 640×360—each at density 2. Each size passed 629 assertions, totaling **1,887**. The final retained-account-privacy check passed with actual exit code 0. `license-stars-fallback-960x600` passed 579 checks without owned artwork; visible text stars were visually reviewed. Native composed-page checks do not establish global dispatcher or physical Cocoa behavior. No live ingestion or SQL deployment was performed. ZIP qualification and delivery passed as recorded below.

The first package retained the old `CFBundleVersion` 89 and is preserved as diagnostic evidence. The fresh independent package attempt2 corrects the version to 90; it is the qualified delivery.


## Verified local delivery

`artifacts/ProjectPrime-RmlUi-macOS-arm64-rml90-20261018/attempt2/` records version 90, 26 ARM-capable Mach-O components, strict deep ad hoc signature verification and the no-game-assets guard. Bundled and extracted smoke tests, plus extracted-window-and-resize qualification, all completed successfully with exit code 0. The signed inventory remained unchanged afterward.

An owned thumbnail child (PID 18423) had already exited when cleanup inspected it; `cleanup-verified` records an empty remaining-process list. No owned processes remain. `final-source-delivery-guard` verifies that all 1,966 source/test inputs, including 1,837 production inputs, remained unchanged through delivery.

The delivered archive is [ProjectPrime-RmlUi-macOS-arm64-rml90-20261018.zip](/Users/jarrett/Downloads/ProjectPrime-RmlUi-macOS-arm64-rml90-20261018.zip): **85,205,175 bytes**, SHA-256 `fc3a461c58352d3af94d1b0391fddd5e8c7aec4cff80ba2bc998aa8f8eaa73a5`. The Downloads copy has an independent inode and the verified archive hash.

These results do not claim physical Cocoa input, global profile dispatcher behavior or live backend ingestion qualification. Wider RmlUi migration remains in progress, with Settings last.
