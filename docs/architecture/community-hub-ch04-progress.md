# Community Hub CH-04A | Efficient transfer progress and foreground cancellation

**Stack:** CH-01 PR #600 -> CH-04A. Independent of CH-02 and CH-03.

## Changes
- Stream progress callbacks previously acquired `index-v1.lock` and parsed `jobs-v1.json` every 64 KiB, even when the acknowledgement threshold meant no write. Fast-path suppressed updates now consult local state under the existing process mutex only.
- Durability is unchanged at terminal state and state transitions. During streaming, journal acknowledgements are limited to 8 MiB advance or two seconds, whichever occurs first. Transient UI notifications can update at most every 250 ms and are explicitly not shared across processes until persisted.
- `WaitAsync` now accepts an optional CancellationToken. A cancelled foreground waiter exits promptly, leaving the upload/download job and its partial bytes untouched. The player Community handoff passes its screen token.
- Existing cross-process pause/cancel always acquires the receipt transaction lock before any subsequent persisted progress write; terminal states are never overwritten by an old byte callback.

## Tests
`tools/community-transfer-check` includes foreground-cancel checks for both completed receipts and another live process's active upload. Existing cross-process adoption, remote pause/resume/cancel, job leases and recovery tests remain required.

## Validation
No device or live-network benchmarking was performed. Measure journal write count, UI event latency and large 512 MiB upload/download throughput in staging. HTTP handler pooling, adaptive chunk tuning, cache storage policy, Community browser redesign and creator UX remain separate CH-04B through CH-07 work.
