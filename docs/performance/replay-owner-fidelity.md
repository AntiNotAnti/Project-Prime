# Owner presentation capture and playback

The current renderer already calls `ReplayOwnerCapture.Capture` after a successful
main picture. The audit's missing-producer hypothesis does not describe this
checkout. The producer reads the final camera/FOV and cached prepared gun pose;
replica scenes (including auxiliary Studio cameras), headless clients and non-owner
first-person pictures cannot become source evidence. It never writes gameplay,
packet or checkpoint state.

Enable **Replays > Recording > High Fidelity Replay Recording** before starting a
recording. The persisted preference is `replay_high_fidelity`; the existing
`PRIME_REPLAY_OWNER_CAPTURE=1` opt-in remains supported. The setting applies to the
next recording. This captures owner camera/gun/available animation/cosmetics and
scope blend, not unrecorded remote POV, audio, or every possible visual effect.

New live recordings stream `.ownerpose` schema 4. The render producer admits value
samples through a 4096-entry nonblocking channel; a sole worker builds at most
256-sample chunks, fenced at match/epoch/slot/generation/life changes. Each chunk
uses bounded Deflate compression and SHA-256. The completed footer binds SHA-256
of the exact canonical replay and records sample count, dropped queue/invalid
samples, excluded pictures, gaps larger than one canonical tick, and first/last
captured frames. Saturation makes `ReplayOwnerCapture.LastError` explicit and
survives in playback provenance. A gap count is an observed discontinuity, not a
claim that skipped third-person or paused time was captured. Duplicate draw times
do not create extra source pictures.

There is no 65,536-picture lifetime cap for live recordings. Producer memory is
bounded independently of duration; storage is bounded to 8 GiB and loading to one
million indexed chunks. I/O failure stops only this optional presentation track.
The source replay must complete successfully before atomic destination publication.
Legacy schema 1/2/3 reads remain unchanged; their small in-memory builder still has
its original capacity limit for synthetic fixtures and compatibility tools.

A playback owns one `ReplayOwnerPresentationSource`, task and cancellation lifetime.
At most four actual file reads run concurrently; queued sources await cancellation-aware
admission instead of permanently losing presentation when saturated. Normal and
custom-archive replacement scenes borrow it, so repeated seek rebuilds
do not rehash/reload or consume additional loader slots. Export owns a separate
frozen source for its complete job. Loading verifies the entire sidecar, bounded
chunk decompression, chronological and lifecycle validity, index extents, telemetry
and exact source identity. It retains a private immutable disk snapshot, a compact
index and one decoded chunk. Random sampling performs a bounded synchronous chunk
read/decode on a cache miss; this is a remaining cold-seek latency cost to measure.
Disposal cancels unfinished work and retires its private temporary after completion.
This is a playback lifetime cache, not a global stale-path cache.

Validation: `nettest --replay-owner-track` covers legacy codecs, malformed data,
identity, export freezing and ten synthetic minutes at 240 Hz (144,001 pictures),
backward seeks, lifecycle boundaries, corruption and private snapshot stability.
Offline crop copies use cancellable backpressure to preserve every captured picture;
the live producer remains nonblocking. Saturated/cancelled load admission is tested.
The local Windows acceptance also exported an asset-backed synthetic 240 Hz owner
sidecar on an NVIDIA RTX 5080 at 24/30/48/60/90/120/144/240 FPS, checking repeated PNG
identity, MP4 completion and unchanged gameplay hashes. This exercises the real
renderer and encoder with synthetic source evidence. It does not establish a human
match's source/replay pixel or audio parity, representative GPU performance, physical
display cadence, or 10-minute device recording continuity. Those gates still require
representative captured source evidence and the corresponding device run.
