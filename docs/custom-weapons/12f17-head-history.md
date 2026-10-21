# CW-12f17 — bounded historical head presentation

This source contract covers explicit private custom replay inspection.
[Validation evidence](evidence/CW-12f17.md) records qualified v3 capture, restore,
parser and separate cold inspection, with retained legacy12 first-person coverage.

`PassiveReplayScene.BeginCustomHeadHistory()` opts a fresh private owner into
history coverage before stepping or restoring a world. Its origin is the current
recording frame. Without this opt-in, the existing custom world12 writer remains
unchanged. Native world11 and descriptor/checkpoint schemas remain unchanged.

Custom world13 declares flags31, including the new head-history flag16; legacy
custom world12 retains flags15 and remains readable with partial presentation
reconstruction. Version13 carries a length-bounded head seed appendix. Archive
baseline and selected optional checkpoints must agree on history capability and
coverage origin, alongside the existing indexed-fire policy. Mixed capability or
origin is refused rather than silently dropping historical presentation.

`ReplayHeadSeedCodec` admits value-only `HPS1` version1 data, at most128KiB. The
header declares origin and exact current frame. Canonical booleans, finite bounded
vectors, nonfuture clocks and exact end-of-buffer are validated. Fixed arrays
cover eight hit/death slots, bounded32-entry seen-event histories and64 pending
hits; the archive cannot supply arbitrary allocation counts. Event keys are
tagged native/custom identities, never local events, runtime types or pointers.
Custom keys bind the admitted manifest and victim identity.

`SceneReplayHeadHistory` retains cancellation, deduplication, pending facts and
their recorded eligibility, positions, delayed anchors, freeze/form history and
death state. Restore first decodes the entire seed into temporary state, then
commits tracks and prewarms their pose resources. Actor references are rebound to
the new private Scene lifetime and seek generation. It does not advance clocks,
re-evaluate old eligibility or re-offer old contacts.

World13 rebuild refreshes HUD/flight projections without releasing or replaying
the restored history through an old contact offer or simulation step. Legacy12
continues its explicitly partial rebuild. History coverage is bounded by the
declared origin; it does not manufacture earlier events.

Physical corpses, cross-settings/cache fidelity, every hunter/form and owner
sidecar fidelity are not qualified by this contract. Inspection remains silent;
ordinary recording, theatre, killcam and export admission remain closed. Full
CW-12 remains open. Android testing is deferred and CI is unobserved.
