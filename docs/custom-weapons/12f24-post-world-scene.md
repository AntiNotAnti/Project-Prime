# CW-12f24 — explicit post-world Scene inspection

This source contract extends format8 consumption from CPU sessions into a private
recipient Scene. Five actual-byte-derived archive cases and fresh compatibility
checks are locally qualified; see [validation evidence](evidence/CW-12f24.md).

Base after integrating PR1004's Replay Studio keyboard ownership and seek-state
changes: `0f04cd0b0b06f8452983d6b2e3acb68208280482`.

`PassiveReplayScene.OpenPostWorldArchiveInspection` requires an explicitly
prepared format8 archive with recipient origin. The ordinary
`OpenCustomArchiveInspection` remains format7-only. Ordinary player/request and
theatre admission remain closed for8; authority-origin Scene admission is not
introduced by this slice.

`ReplayPoseStream` gives both independent pose/fire readers a separate
availability floor while preserving original source recording and authority
clocks. Format7 retains its existing lookahead and scheduling. A post-world fact
recorded atN becomes available with worldN+1. Later ordinary records inherit that
reader's availability floor, preserving file order across phase boundaries. The payload retains
the original input/source time used for age and fire-source mapping. Presentation
may clamp its scheduled application to availability; it must not relabel source
identity or make it younger.

Current and future pose/intent endpoints are filtered against the completed
world's availability frontier. Snapshot interpolation and ACK lookup cannot
expose a deferred endpoint early merely because lookahead has decoded it.
Separate source clocks and availability floors retain bounded future clock evidence
without admitting future presentation. Pending shot/turret outcomes wait for
their floor; scheduled effects use the later of source schedule and availability.
Restored decoder seeds preserve recorded intent age rather than inventing a new
delivery clock.

Terminal deferred facts are validated but skipped before pose/fire indexing,
because no following simulation world exists. Scene stepping retains the CPU
session's source/application separation and combined frame bounds. Semantic
markers remain metadata; construction cannot replace an active world.

No checkpoint, world, descriptor or native wire schema changes. Both World12 and
World13 baselines are eligible, preserving their original head-history capability.
Producer indexed-fire policy is retained exactly, never enabled by flipping a
saved boolean. Exact historical resources, Scene ownership and strict canonical
comparison rules remain intact.

Qualified derived archives use actual f18 World13 and f17/v3 history-bundle World13
sources, both with `IndexedFirePresentation=false`, for pose/ACK coverage. The
genuine indexed source is f15/v3/indexed-fire, World12/protocol85 with its actual
policy true. Cropped first-native-fire/ShotFact terminal controls retain actual
payload bytes and clocks. The oracle shares engine/pose machinery, with separate
source and visibility assertions. These tests establish neither new server capture
nor bot behavior.

Full CW-12 remains open, including ordinary format8
admission, authority capture, export/killcam and owner/audio fidelity. Inspection
is silent; Android is deferred and CI/performance qualification is unobserved.
