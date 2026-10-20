# Replay validation — PR-09 draft

The isolated replay repair passes the previously failing production durable
packet-history comparison for both Native and Momentum. It also preserves every
historical recorded-world scene result. This closes that reproduced failure
within the tested scope; full PR-09, hosted CI, rendered replay and packet-index
performance acceptance remain separate.

The code was published in PR-752 as feature commit
`e950d5cdce4e4c9fc690df66e5e3f6a24e484ec2`. These actual macOS Release tests were
built **before that commit**, from base
`91f3a96d3240d612d4d7e97c1d198570d8197fad` plus the then-uncommitted repair patch.
The tested production source bytes match the later feature commit. Isolated
Core `build03` and tool `tool-build03` passed with zero warnings/errors. The
assembly still identifies its base91f revision; the
[sanitized evidence](evidence/pr09-replay-draft.json) records exact source and
binary hashes without relabelling this as a clean post-merge build.
Host: Apple M4 Pro, macOS 27.0.1 arm64, .NET 10.0.12, headless and offline.

Both final production `-replaydurablecheck` runs pass exact packet-decoder hash
and player equality at **300, 301, 799, 1250, 1500 and 1800**. They also pass actual
world cold/backward bounded seeks, 801-frame and 351-frame nested clip fidelity,
and corrupt-checkpoint fallback/recovery. Each contains nine durable checkpoints:
1,576,037 Native file bytes and 1,589,952 Momentum file bytes.

The [repair explanation](../implementation/replay-packet-index-compatibility-2026-10-19.md)
documents why saved-world indexes cannot supply an exact packet-only decoder
baseline. Packet comparison now declines those indexes, reports an explicit
fallback reason and scans complete packet history. Its detached consumer releases
validated draw packets through the existing `DiscardDetachedDrawPackets` path.
Exact hashes/player equality, malformed-packet checks and the existing 2048-packet
bomb queue budget remain intact. This makes **no packet-index speedup claim**.

The separate recorded-world scene fixture also passes unchanged:

| Model | Assertions | Frames | Snapshot pose checks | Total / memory checkpoints | Continued frames | Memory / durable seeks |
|---|---:|---:|---:|---:|---:|---:|
| Native | 85,673 | 2,982 | 5,964 | 10 / 9 | 2,762 | 9 / 6 |
| Momentum | 85,586 | 2,979 | 5,958 | 10 / 9 | 2,759 | 9 / 6 |

Both report `durableEnabled = true`. All 22 scene result fields exactly equal the
preserved `current-replay-03` and `current-replay-02` results and committed
[PR-08](pr08-results.md) evidence. Against [PR-06](pr06-results.md), all 21 fields
other than assertion count match. These include checkpoint sizes, memory bytes,
every seek result and the unchanged gameplay/presentation digests:

- Native: `BAE5662C01088E7195A093652563122EAB1B2F6C1E9169D5410BC54FA87618B6`.
- Momentum: `C866B47544BB8ADB6775B624237EE7D319C4796719A00E24C7A22B9016EA3A5B`.

Earlier failures remain preserved rather than reclassified:

- Original latest91f production runs failed packet-history equality at frame 301
  in both models under `artifacts/momentum-pr09/latest-durable-index-01/`.
- The first isolated repair still failed exact equality at frame 300.
- The next canonical-history fallback retained unconsumed detached draw packets
  and hit the unchanged 2048-packet bomb queue budget at frame 664.
- Final isolated `run03` results pass after the complete-history fallback and
  existing detached draw-packet drain were applied together.

Original isolated logs remain under
`/tmp/prime-replay-index-compat/artifacts/replay-index-compat/`; numeric logs,
scene JSON, build logs and provenance are also preserved under ignored
`artifacts/momentum-pr09/replay-index-repair-01/`. The original scene02/03 source
leases and executable hashes remain in the sanitized evidence. The initial
`current-replay-01` literal `durable` pathname error remains classified as an
invocation error; it does not classify the later genuine comparator failures.

The prior main91f Core10/tool11 lease separately passed the frame-timing
self-check, including third-person biped/Morph Ball handoff checks at synthetic
60/120/240 Hz. That CPU contract evidence is preserved under
`latest-fast-checks-01` and is not relabelled as a repair-build or measured-device
cadence run. Repair replay rules pass 82 assertions and protocol-58 archives pass
204 byte/archive checks. Hosted CI is queued/tracked separately; no full-green
claim follows from these local checks.

These within-environment CPU replay results do not establish cross-platform bit
equality, GL raster, physical-device, Studio/theatre UI, killcam, export or full
PR-09 acceptance.
