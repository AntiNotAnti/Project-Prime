# Netcode 2.0 — N2 compact live intents

N2 starts at merged N1/main `ea49144f3a60f9ccb33e60cceca8f74b17ee3ee4`.
Branch: `codex/net2-02-compact-intents`. Final tested production source:
`86a9668be27d025188e67d470f6009e814fcd41c`.
Live protocol changes **41 → 42**; clients and servers must update together.

The final eight-client Mac control passes the existing campaign adjudicator.
Relayed-intent UDP traffic is about **88% lower** than the retained N1 control,
and all **113** owner-offered shot identities are accepted and committed by the
server. This does not close N3's shot-lifecycle work: earlier N2 attempts retain
delivered-but-unplayed shots near death and a combat-verdict timeout.

## Wire and retained events

`CompactIntentCodec` encodes the existing 102-byte input body, one event-count
byte, and exactly `count × 67` bytes of actual `FireEvent` history. No unused
event slots are sent. Count is bounded at 16, and the reader rejects truncated,
trailing, nonfinite and invalid event data. The canonical internal model retains
shot identity, weapon, charge, source frame, world ACK/subframe, continuous/scoped
state, muzzle, direction, aim, view and reticle. Presentation fields are sent only
with actual shots; this slice does not introduce new lossy pose compression.

| Carrier | Input body | Relayed UDP datagram, including slot + transport |
| --- | ---: | ---: |
| No events | 103 B | 128 B |
| One event | 170 B | 195 B |
| Six events | 505 B | 530 B |
| Sixteen events, hard maximum | 1,175 B | 1,200 B |

The 24-byte transport header includes packet type. These figures exclude UDP/IP
headers; a 1,200-byte UDP payload also fits the IPv6 minimum MTU with ordinary
UDP/IPv6 headers. Other packet formats and their limits are unchanged.

The existing `NetFireEvents` owner now uses separate fixed histories for authoring
and receiving. There is no second combat authority or parallel shot-spawn path.

- Authoring retains at most 128 events for the existing inclusive 32-source-frame
  horizon. New events go into carriers first, up to 16. Older events rotate through
  a repeat budget of at least six, growing to `ceil(retained count / 6)` and capped
  at 16. Normal 60 Hz continuous fire therefore gets repeated carriers without
  sending all 16 slots. This is bounded redundancy, not an unlimited delivery ACK.
- Receiving keeps 128 identity/dedup cells per slot, fenced by authority epoch,
  match, slot generation and life through the existing `ShotKey` owner. Source
  recovery is bounded to 64 frames relative to the newest admitted carrier.
  Consumed IDs remain in the serial-number window. A delayed older ID can still
  be selected after a newer ID was consumed; repeats cannot consume twice.
- Event admission precedes movement ordering. Newer empty inputs cannot erase
  pending shots. Same-frame distinct IDs remain separate. Valid reordered carriers
  are also relayed by the dedicated server. Movement and readiness only advance
  with a newer carrier frame.
- Conflicting repeats and occupied-cell collisions reject the entire batch before
  adding any event. Authoring overflow refuses new retention and blocks subsequent
  authoring while full. Existing entries are never evicted to make room. Expiry,
  conflicts and overflows have explicit counters in campaign `IntentHistory`.
  Frame/ID comparisons handle wrap. No ID or shot is synthesized to fill a gap.
- SlotIntent coalescing is removed because different carriers can contain distinct
  events. Snapshot coalescing remains. The 2,048-packet bound now reserves one
  snapshot cell and 128 critical FIFO slots; pump budgets are unchanged.
- Each admitted uplink connection also has a **96,000 B/s, 38,400 B burst** Intent
  bucket, charged for the full datagram before simulation/fanout, alongside the
  existing 180 packets/s, burst 64 limit. Authority SlotIntent uses its existing
  realtime lane. Byte rejections and budget constants are exposed in captures.

Pending selection and actual projectile creation still use the existing gameplay
rules. Death/life transitions, cooldowns and continuous-phase suppression remain
N3/N6 work. N3 should extend this owner/history rather than introduce a competing
queue. Empty histories skip scans on the final hot path.

## Replay compatibility

Live packets decode into canonical `IntentPacket` before gameplay and recording.
`IntentPacket.Write/Read` and full replay bodies stay at 1,175 bytes. Protocol 41
records therefore retain their historical layout; protocol 42 recordings also
store canonical bodies, not compact datagrams. Older adapters are unchanged.

The recorder publishes older event carriers without replacing its latest movement
bootstrap. Replica decoding and pose indexing admit those events separately from
movement, and use bounded ID deduplication instead of dropping all IDs below the
last seen ID. Repeated carriers and two source events in one frame are covered.
Playback still uses its existing scheduling and lifecycle policy.

## Validation

All tests ran on this **macOS arm64** host using .NET 10.0.12 / SDK 10.0.401.
Windows testing was unnecessary for these platform-independent changes. No Linux,
Android, CI, rendered gameplay, or long soak was run.

- Final `--compact-intents`: strict codec, maximum 1,200-byte relay, canonical
  replay, actual-UDP distinct-carrier preservation and byte-flood isolation,
  bounded history/atomic rejection/wrap, and zero-allocation checks pass.
- The history test runs **20 deterministic virtual profiles**: RTT 0/100/250/400 ms
  × loss 0/1/2/5/10%, with reordering, duplicates and a 250 ms receiving stall.
  Every profile recovers **607/607 exact events**. A separate sustained two-events-
  per-frame test retains all 1,200 events. These are virtual fault tests, not claims
  about measured UDP bandwidth or guaranteed delivery under arbitrary loss.
- Final allocation checks pass; asset-backed combat passes **176 assertions**;
  replay v2/v3/v4 compatibility/format checks pass **2,888 checks**.
- Architecture, protocol17, reliable, queue-budget, health-shot and lifecycle
  controls passed during implementation. Python evidence tests pass **46/46**.
- The composite `load-lifecycle` command was manually stopped after 61.379 seconds
  while integrated lobby scenarios were still running. It is **INCOMPLETE**, not a
  pass or an assertion failure. Relevant loading and transport paths also ran in
  the short full-match controls; the broad composite was not repeated.

The first draft build caught a new test file arriving after compile-file discovery.
A draft replay test incorrectly inspected a timeline before its restore point;
the corrected fixture observes published canonical records. Both failed attempts
and corrected results remain in the evidence bundle.

### Actual UDP comparison

The retained N1 and both N2 eight-player controls use the same 25-second missile
scenario, map `MP1 SANCTORUS`, seed 8128 and launch configuration on the Mac.
They are independent live runs, not a deterministic replay of identical inputs.
N1 authored 114 shots; initial N2 authored 105; final N2 authored 113.
Counters are actual successful socket sends, including transport headers.

| Eight-client control | Wire lifetime | Relayed-intent bytes | Relayed-intent B/s | Server offered → accepted/committed |
| --- | ---: | ---: | ---: | ---: |
| N1, `981e52ac` | 30.086 s | 99,388,800 | 3,303,526 | 114 → 114/114 |
| Initial N2, `d19d6000` | 30.940 s | 12,051,788 | 389,527 | 105 → 105/105 |
| Final N2, `86a9668b` | 30.808 s | 12,188,825 | 395,636 | 113 → 113/113 |

The final reduction is **88.02%** in lifetime-normalized relayed-intent bytes/s.
Startup and shutdown are included. Final relay datagrams measured at most 195 B
in this missile workload; the codec/transport tests cover the 1,200 B maximum.
The final control has zero health mismatches, semantic gaps, transport drops,
reliable failures, socket errors, history conflicts, overflows or pending expiry.
Maximum observed FIFO depth is 288/2,048 and reliable high-water is 6/40.

The existing adjudicator excludes a 30-frame closing tail for view comparisons
and a 60-frame tail for semantic comparisons. Unfiltered exact-key differences
are retained too: staggered client exits leave five final-run peer/event pairs
unreceived at shutdown. The 113/113 statement is server acceptance/commit, not a
claim that every exiting view received every end-of-run event.

### Simulation cost and startup

Matching logged simulation frames 301–1500 give the following single-run results:

| Metric | N1 | Initial N2 | Final N2 |
| --- | ---: | ---: | ---: |
| Mean step | 0.350084 ms | 0.416877 ms | 0.313729 ms |
| Coarse p95 bucket | [0.50, 0.75) ms | [0.50, 0.75) ms | [0.50, 0.75) ms |
| Worst step | 8.2044 ms | 6.9274 ms | 6.9697 ms |
| Step allocations, 1,200 steps | 2,868,528 B | 2,890,640 B | 3,048,632 B |

These observations do not isolate causal CPU overhead. The final change skips
empty history scans, but single runs cannot establish its precise CPU benefit.
None of these selected steady windows exceeded 16.67 ms.

Both N2 eight-client runs have nine dropped scheduling steps already present in
the first 60-step window, with no later increase. First-frame wall time was about
240/233 ms; retained N1 also had a first-frame hitch of about 512 ms, although its
dropped-step count was zero. Startup remains a measured limitation. The evidence
does not establish a GC/replay cause, and no startup-performance fix is claimed.

## Failures retained for later slices

The initial eight-player N2 adjudication remains **FAIL**: one delivered missile
identity `(639272664844599080, 2, 4, 1, 2, 13)` lacks commits in five views. Every
affected view records acceptance; authority commits at frame 631 and the first
sampled death is frame 636. This matches the retained N0/N1 near-death phenotype,
without proving its precise cause. Five pending-expiry counters expose those gaps.
The final control passing does not erase or resolve this N3 finding.

The short two-client impaired run on `d19d6000` uses 400 ms RTT, ±80 ms jitter,
5% loss, 3% reorder and 1% duplication. All **28/28** offered shots reached and
committed on the server, with no connection/reliable failures, transport drops,
health mismatches or semantic gaps. Overall status remains **FAIL**: a delivered
shot near death lacks one remote commit, and shot 1 logs `verdict-timeout` after
two sends despite an earlier authority `already resolved` verdict. This remains
combat verdict/lifecycle evidence for N3/N4/N5/N6, not proof of its root cause.
The final empty-queue optimization was checked by the virtual matrix and final
eight-player control; this impaired run was not repeated.

Initial campaign summaries also report protocol mismatch because the runner still
defaulted to 41. Their originals remain untouched. Separate
`adjudicated-protocol42.json` files apply expected protocol 42 to the same raw
reports. The runner now accepts explicit `--protocol` (default 42); historical
adjudication still defaults to 41. No raw failure was rewritten to obtain a pass.

## Provenance and rollback

The tracked machine-readable ledger is
[`net2-n2-20261010.json`](../tools/nettest/baselines/net2-n2-20261010.json).
Raw logs, reports, asset hashes, commands, build identities, comparison scripts,
all attempts, and a verified file index live under `artifacts/net2-n2/`.
`artifacts/net2-n2-evidence.tar.gz` retains this evidence plus the tested primary
DLLs/runtime metadata, excluding bulky duplicate staged dependencies. The ledger
records archive/index hashes; full runtime dependencies can be rebuilt from the
recorded source. N1's existing evidence/archive is referenced without modification.

Final engine SHA-256:
`0f7948b11a68ff61dc769f07e6c5dae742aff4d50ef1a280d5bc7b88d6bb41fb`.
Final test assembly SHA-256:
`341c23022e1dd7651fb1d4fdc6f93a3a29ba815567cace684d2ff2c6559d9371`.
The retained N1 engine is `84b9ce3ecb499935b8473607e3af7fba7f07c3a5484385d6357f8def90a750d6`;
its source `981e52ac` has the same production `src/` tree as N2's merged base.

No rollout flag is added. Rollback deploys the N1 protocol-41 client and server
together, or reverts both N2 implementation commits together. Keep protocol-42
recordings for an updated reader; an old N1 reader need not accept newer metadata.
This PR stays unmerged. N3 has not begun.
