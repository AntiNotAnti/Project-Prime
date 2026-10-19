# RML-39: Native Hunter License career navigation

Date: October 17, 2026. Base: main `dc7f097067d755ec76d145fc7cc333068566863d`
(RML-38 / #701 merged).

Continuation: [RML-40](rmlui-license-privacy-rml40.md) adds an account-bound
native opt-out. Showing a public profile and the remaining account actions
continue through Full Hunter License.

The opt-in native Hunter License now has Overview, Stats, Match History,
Achievements and per-match Benchmark tabs. Account security, recovery,
customization and privacy mutations keep the existing Full Hunter License
fallback. Full Settings stays last; the default launcher does not change.

## Data and ownership

- Reuse `HunterLicenseClient.LoadAsync` and its existing authenticated session.
  Its normal cosmetic merge/retry behavior during explicit sync is preserved.
  No new endpoint, credential store or database policy is introduced.
- Project only the safe career counters and up to 25 recent match display
  records. Preserve service order and paginate locally in eight-row pages;
  the UI explicitly describes a bounded recent snapshot, not complete history.
- Wins/ties, eligibility, objectives, play time, seven career milestones and
  per-match benchmarks follow the existing License workspace. A history row
  is CAREER only when both eligibility flags are true. Career totals are
  never recomputed from the recent snapshot. Displayed timestamps say UTC.
- All reads run off the render thread. Results apply on the owner thread with
  request-epoch and document/foreground/sequence guards. Accepted row lists
  are copied and made read-only. Unchanged revisions reuse their snapshots.
- Explicit sync immediately retires the prior identity, totals and history.
  There is no identity lease allowing an old account to remain on screen
  while a new session is checked. Failed/cancelled/offline sync cannot
  resurrect that record. Error bodies and raw service status are not shown.
  Unknown public visibility remains unavailable.

## Automated acceptance

`tools/rmlui-license-check` exercises actual controller-to-DOM bindings for
five faces; 0/1/8/9/25-match paging and shrinking lists; eligibility and tie
labels; career-derived stats/milestones; numeric edge cases; immutable
snapshots; malformed counters; cross-account retirement; redacted errors;
cancelled/superseded/disposed results; and owner-thread enforcement.
`tools/rmlui/check-pages.py` checks the authored asset references. The opt-in
GL build includes the actual presenter and current-service adapter.

The first actual GL page-open attempt exposed an existing packaging failure:
SDK-wide `None` items retained the full source path in `RecursiveDir`, placing
pages under `rmlui/pages/Mods/Launcher/RmlUi/Pages/...`. The conditional
MSBuild items now re-include the authored globs with the intended roots.
`check-pages.py --published-root <rmlui directory>` verifies all 87 authored
resources at their runtime paths and compares their bytes with the source;
the existing build workflow runs this after the opt-in build.

The existing Linux software-GL job now uses the actual packaged asset tree
and invokes the optional `--license` fixture, retaining its synthetic
framebuffer evidence. This adds regression coverage for page paths,
actual pointer dispatch, focus, pagination, foreground/modal/sequence and
retired-document guards, and GL errors. Button transitions and smooth
scrolling settle before captures are read.

Native parsing also exposed missing whitespace after `@media` in the
License stylesheet, which silently invalidated its compact-layout rules.
The rules now parse correctly. Initial page focus moves to Overview rather
than the footer's sync button, avoiding an immediate jump toward the bottom.

## Local results

- Opt-in desktop build: passed; the existing four unrelated nullable warnings
  remain in the menu/lobby controllers.
- Dedicated-server build: passed with zero warnings and zero errors.
- License career fixture, runtime fixture (including 100 reinitializations)
  and live-menu contracts: passed.
- Packaged source/asset identity: all 87 authored resources matched.
- Actual macOS OpenGL captures and native clicks: passed at framebuffer
  1920×1080, 1280×720 and 640×360 on Apple M4 Pro, macOS 27.0.0,
  OpenGL `2.1 Metal - 91.7`. Each run uses synthetic License records and
  verifies the actual focused control, five faces, paging, sequence/modal
  guards and clean GL state. Smaller surfaces scroll to reach content.

Local raw logs, synthetic PPM/PNG captures and framebuffer metadata are in
`artifacts/rmlui-license-rml39/`. The test accepts an optional client-window
size after `--license`; the actual framebuffer dimensions are printed and
recorded independently (the local Retina display doubles window dimensions).
This local evidence does not establish a green remote CI run for this branch.

## Outstanding acceptance

Live authenticated service actions, full Account/Customization/privacy
parity, historical screenshot comparison, Windows/Linux and physical Android
input/lifecycle remain unverified. Party's proposed backend is still
unconnected. Shared service DTOs default omitted stats/history sections;
strict field-presence validation of service JSON is not established by the
native fixture. These changes do not claim CPP-02 or release qualification
complete.
