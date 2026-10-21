# CW-12f19 — bounded custom archive player foundation

This source contract describes a private player foundation, not ordinary UI
admission. Local qualification and limits are recorded in the
[evidence ledger](evidence/CW-12f19.md).

`PassiveReplayPlayer.OpenCustomArchive` takes a worker-prepared exact archive
resource owner and an explicit captured store root. Initial Scene creation
restores its selected durable baseline without an eager forward reconstruction
loop. Prepared target frames are converted from absolute archive recording
clocks to transport-relative clocks. Existing native constructors remain unchanged.

The player uses the existing transport Begin/AfterFrame contract for pause,
single-step, rate, seek, EOF and restart. Each Update executes at most120 fixed
steps; seek reconstruction also observes the supplied time budget while allowing
at least one step. Every control seek, including forward and current-frame seeks,
restores a fresh Scene through worker checkpoint preparation. This rebinds
world13 head-history identities to the new transport generation rather than
reusing the old Scene's presentation history.

At most one preparation task is pending. New requests cancel the old request and
wait for its completion before starting another worker. Completed stale results
are disposed and stale faults are observed then discarded. Repeating the same
pending target updates its resume preference without creating another worker.
Active results must match the original archive baseline metadata
and duration exactly. The store path is captured explicitly, not read from
mutable environment state during a seek. There is no native fallback or masked
checkpoint conversion.

Preparation, stepping or replacement errors set `Transport.Error`, mark
verification failed and clean up the failed ownership path; errors do not invoke
a native fallback.

The original Scene thread performs Update and replacement. A replacement Scene
restores the prepared baseline, preserves transport preferences, and advances to
the requested target through later bounded updates. Replacement notifications
precede disposal of the old owner. Player disposal cancels unfinished preparation;
its continuation disposes any completed resource owner and observes faults.
Historical package leases remain independently owned by prepared/private Scenes.

This foundation uses explicit custom archive inspection and retains its existing
exact resource, map, world-history and baseline policies. It adds no native wire,
descriptor, checkpoint or archive schema. Ordinary UI/theatre, killcam and export
admission, server capture and instant clips remain guarded. Inspection remains
silent without an explicit audio owner; captured owner HUD/audio fidelity is not
claimed. Full CW-12 and bots remain open. Android is deferred and CI is unobserved.
