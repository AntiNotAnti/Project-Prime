# RML-89 original MPH-style Hunter License

Date: October 18, 2026. **Native qualification and local package delivery complete.**
Work remains exclusive to RmlUi; Settings stays last.

## Implemented behavior

Hunter License follows the original MPH DS license-card reference: card header,
favorite-hunter portrait, player identity, rank and ranking points, three compact
records, and five record tabs. The native card tabs are Record, Combat, Skill,
Objectives and Time. They expose existing accepted career totals and derived
ratios/durations, rather than inventing DS statistics the account service does
not provide. Unverified records show sync-required/unverified states; absent
optional career data remains unavailable.

The existing Overview, Stats, Match History, Achievements, Emblems, Titles,
Benchmark, Account and Customization controls remain accessible beside the card.
The native page can open as a foreground overlay from the profile route and
retains its existing account, privacy and credential-clearing owners. Privacy
and credential behavior is not delegated to the portrait cache.

Sources are `NativeLicenseOverviewPage`, `NativeLicenseOverviewBindings` and the
portable `NativeLicenseOverviewController`. The reader projects the existing
Hunter License profile, rating and accepted career/history records. Card-tab
commands use the typed native license-action registry. This remains a native
adaptation of the DS reference, not a claim of pixel-identical original frame
artwork or complete DS menu behavior.

## Original owned portraits

`RmlUiLicensePortraitCache` decodes only Samus, Kanden, Trace, Sylux, Noxus, Spire
and Weavel. Their metadata maps to `gamerscard/{samusmost,kandenmost,tracemost,
syluxmost,noxmost,spiremost,weavelmost}_Model.bin` beneath the configured owned
MPH files. All seven source portraits have one mesh/material, a default recolor
and a 32×32 texture. The cache selects the material's texture and palette IDs and
uses the existing `Read.GetModelInstance(..., MetaDir.GamersCard, noCache: true)`
and `Recolor.GetPixels` path. It does not decode or stretch the original
`frame200`/`cardbgUS` texture/geometry assets into the new card.

The cache holds the existing configured-files read lease while decoding on a
serialized background worker per cache instance. Root, requested hunter and request generation
fence publication. Empty/unverified favorite-hunter requests retire the image;
the page does not substitute a guessed favorite. Failures are cosmetic. Content-
addressed native TGA files live only under the user's
`rmlui-media/license-portraits` cache. Owner requests and disposal do not wait on
worker filesystem locks. No engine cache clearing, GL texture work, global
Headless-mode toggle, account mutation or network request belongs to this cache.

No cartridge artwork is checked into source or intended for the application ZIP.
Validation deliberately uses independent private copies of owned assets:
`artifacts/rmlui-license-card-rml89-20261018/acceptance/owned-portraits/user-data/`.
Its 3,858 files total 840,039,391 bytes; full source/copy hashes match, inode sets
are disjoint, and the source fixture remains unchanged. Those private test files
are separate from packaging inputs. No player packages, account state,
preferences or saves were copied into this fixture.

## Qualification completed

Final build-attempt3 client/server and seven tools compile with zero warnings/errors.
Managed client SHA-256:
`e42a3bf8c13be5eb2392e8ce75f20f02ce40197220bd32f0d2b3f455a9d44145`.
Native bridge SHA-256:
`e053991e7e9e887574daf4e53ccd1b9b0f376025fa8642d7c822d794a201387f`.

Six frozen CPU suites pass with true exit 0 and unchanged source/runtime guards:
Community, Hunter, Runtime, Live Menu, Social and License. Receipts are under
`artifacts/rmlui-license-card-rml89-20261018/acceptance/cpu-attempt2/`.

The actual owned-portrait acceptance passes with true exit 0 and no timeout. It
checks every RGB/alpha pixel of all seven source textures against native TGA
output, dimensions/orientation, content-addressed reuse, unchanged source files,
serialized held work, hunter/root retirement and nonblocking disposal. It does
not create a GL context or toggle the process's Headless state. Execution proof:
`build-attempt1/acceptance/owned-portraits/execution.json`; detailed result:
`acceptance/owned-portraits/user-data/license-portrait-acceptance/summary.json`
within the RML89 artifact root. The final build's
`build-attempt3/portrait-source-reuse-proof.json` proves that the three portrait
source/test files and native bridge are identical to the passing actual-portrait
run; this is explicit reuse, not a claim of a second real-source execution.

Three final native card cases pass **495 checks each, 1,485 total**, at client
sizes 1280×720, 960×600 and 640×360 with Retina density 2. Evidence is under
`build-attempt3/acceptance/license-card-<size>-v2/`. Every size requires actual
locally decoded favorite-hunter portraits, checks that stale account state
clears the image, exercises all five card tabs and retained faces, preserves
Lobby drafts across overlay close/focus restoration, and verifies the
Customization handoff. Screenshots and raw semantic/bounds evidence qualify the
responsive layout. Final-main retained account/privacy regression also passes
under `build-attempt3/acceptance/retained-account-privacy/`.

The v2 GL tool changes only its fixture: after the small stacked layout scrolls,
it uses the real native wheel operation to return to the top before asserting
whole-card viewport bounds. All original activation and bounds assertions remain.
There is no production change from that repair. `build-attempt3/test-v2-repair.json`
preserves the exact delta and the original failed 640 case. The final GL tool uses
`source-and-test-freeze-v2.json`; the CPU runs use the prior frozen tool sources
against the exact same unchanged final production inputs and main/native bytes.

## Diagnosed repairs and preserved evidence

The initial native card GL case is **not qualified**: it exits -6 after the
`retained face visibility stats/stats` assertion. The original log, failure
capture and execution receipt are preserved under
`build-attempt1/acceptance/license-card-1280x720/`.

Diagnosis found that removing the `prime-shell` template also removed its shared
`components/controls.rcss` import. The resulting unstyled scrollbar consumed
content width and pointer hit area. A diagnostic restoring the shared controls,
while retaining attempt2's original flex/percentage layout, passes Home detail
faces, all five card tabs, Close and retirement. The viewport-width and absolute-
position exploratory layouts were not adopted; their candidates and failed
receipts remain preserved in `layout-diagnostics/`.

That diagnostic also exposed the older `disabled:profile = lobby_mode` binding.
The final source repair removes that binding and its dimming CSS so the Lobby
profile control can reach the native license overlay. It restores the shared
controls import and tightens the missing-page-manager guard. Those repairs are
included in the fully rebuilt, qualified build-attempt3 above. The earlier
failures and diagnostic candidates remain intact; they are not presented as
passing final runs.

The synthetic native fixture invokes the composed pages and license presenter
with explicit in-memory career/account authority. It does not exercise the
global `RmlUiOpenGlSmoke` dispatcher, send account mutations, or establish that
physical Cocoa keyboard/IME callbacks work. No new physical Mac input success is
claimed by this slice.

## Verified local delivery

The build-attempt3 package is staged and ad hoc signed under
`artifacts/ProjectPrime-RmlUi-macOS-arm64-rml89-20261018/`. The asset guard,
26 ARM-capable Mach-O component checks, exact archive inventory and strict deep
signature verification pass. `archive-execution.json` records passing bundled
and extracted startup checks.

The delivered archive is `ProjectPrime-RmlUi-macOS-arm64-rml89-20261018.zip`:

- Size: **85,201,121 bytes**.
- SHA-256: `25dcd37b298d42398081e17727cb6c14d401dc3d648dd66eef51f983eb842cbd`.
- Downloads copy: `/Users/jarrett/Downloads/ProjectPrime-RmlUi-macOS-arm64-rml89-20261018.zip`.

`downloads-delivery.json` confirms that the Downloads copy has an independent
inode and the exact verified archive hash. The extracted window and resize check
passes on the Apple M4 Pro through OpenGL 2.1 Metal, with framebuffer sizes
2560×1536 and 2200×1480. These checks use only the configured private fixture;
the bundle remains unchanged and passes strict deep verification afterward.

`window-owned-process-cleanup.json` records cleanup of one leftover thumbnail
worker (PID 1014) belonging to the exact extracted app and its owned process
group. The original user app is untouched. This packaged window check does not
extend the synthetic fixture's scope to physical Cocoa input or runtime
validation of the global root profile dispatcher.

Wider RmlUi migration remains in progress, with Settings last.
