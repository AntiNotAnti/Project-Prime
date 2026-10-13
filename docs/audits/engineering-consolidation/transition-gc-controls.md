# Existing scene-transition GC controls

This diagnostic seam observes only the existing collection at `Scene.OnLoad`
(forced blocking compacting Gen2) and `RenderWindow.EndScene` (default collection).
The normal client has no public setting or environment policy override. Without
a test observer, both original calls execute regardless of the internal policy.

The native QA harness alone accepts `-transition-gc keep|skip-load|skip-end|skip-both`
for ordinary completed-frame runs. Resource stress and explicit finalizer/GC drains
are rejected because their ownership inspections retain a strong scene reference.
Only a weak scene reference and numeric handles cross the diagnostic EndScene
boundary; the snapshot method is not inlined. No extra collection, finalizer drain,
GL finish, readback or asynchronous audio wait is inserted into the measured run.

The observer records bounded value-only snapshots: collection/stage wall time,
managed live estimate, latest collection heap/committed/fragmentation/index,
pause durations, generation counters and RSS. The latest GC snapshot may predate
skipped calls. RSS includes runtime, native audio and driver memory. Stage scopes
cover OnLoad and EndScene, not construction/AddRoom. They include observer and
driver/audio work; collection wall excludes the probes. This is characterization,
not pure CPU/GPU timing or an attribution of RSS to a native allocator.

The four corrected 8-second smoke arms passed recognized owned GL program,
texture and framebuffer retirement. Their weak scene target remained available
in all arms. Renderer construction binds `PlayerEntity.LegacyRegistry` and
`GameState.Current` to scene-owned player state; those registries retain entities
whose `OwningScene` references the scene. EndScene does not currently unbind them.
This is one latest foreground owner, not a chain: the next Scene constructor
creates a fresh registry/state and copies scalars/nickname strings. Shell.EndMatch
and EndNetworkMatchToLobby call EndScene before MatchStart.AfterMatch commits
adventure saves through GameState.CommitSave and the ended scene's StorySave.
Weak survival is therefore not proof that a collector failed; unlinking the
facades during EndScene would violate this existing post-teardown save ordering.
A separate `-transition-rebind` probe loads the next normal foreground room and
samples the previous scene weak target after only the existing collections.
It is reachability evidence outside the completed-frame timing campaign.
No production GC or ownership behavior changes are included.

Matched five-per-arm 24-second runs use Original preset with explicit High shadows,
eight occupied level-1 bots, RNG 12345/98765, synthetic input, 1920x1080,
uncapped completed presentations and fixed 60 Hz simulation. No malloc-stack
logging runs during timing. Agent builds are idle; a user-owned game remains
running. Safe process CPU/RSS/state and VM snapshots accompany every run; this
is an ambient-control window, not global quiet-host optimization proof.

Publication branch is pinned directly to main91b1c0c6c44e8c90effd250729709f791a84765e.
Native runtime evidence uses lifecycle bundle749aa12dc89d41dc83823eafe0929c13b1ebfb99
with #492/#494/#495, recorded separately in the receipt. Original smoke evidence
with a strong scene local is preserved and excluded from residency conclusions.

## Matched transition results

All twenty ordinary GC controls passed (five per arm) with fixed 60 Hz simulation,
1440 total simulation frames, no dropped steps or stalls and retired recognized
GL programs/textures/FBOs. Median per-run FPS/p99 were keep301.94/6.438 ms,
skip-load300.22/6.677 ms, skip-end302.04/6.503 ms and skip-both300.29/6.511 ms.
These do not show a credible rendering benefit from skipping collections.

Existing keep load/end collections took median15.19/18.88 ms; inclusive
OnLoad/EndScene stages took123.31/34.55 ms. Skipping end reduced its stage to
14.99 ms, but kept the managed estimate at213.2 MiB; keep reduced212.7 to96.0
MiB and RSS493.4 to452.1 MiB. Skip-end RSS remained494.2 MiB. Skip-both stages
were105.35/14.50 ms with end managed179.2 MiB and RSS507.4 MiB. This is a
responsiveness/residency tradeoff, not evidence to remove either call. The default
is preserved; longer transitions and physical-platform evidence remain separate
gates. Latest GC heap fields after skipped calls can describe an earlier collection.

## Initial rebind witness and its confounder

Four separate 8-second reachability arms also passed retirement with two normal
loads/ends. The previous scene weak target remained available after the next
load and end, including keep. Every weak query is a separate non-inlined bool
helper; no target survives across the next collection. This is preserved negative
evidence, not a leak classification. The timing/rebind bundle predates #497:
CollisionDetection's static inactive/active candidate pools retain EntityCollision
references, whose Entity owns its Scene. A fresh load without a new simulation
query does not replace those candidates. A separate confirmation bundle will
include #497's cleared query workspace and an actual next simulation/query; it
will not replace any original timing or reachability receipt. Facades have a
source-backed one-foreground-world bound, but this witness does not establish a
bound for other static caches or asynchronous audio owners.
