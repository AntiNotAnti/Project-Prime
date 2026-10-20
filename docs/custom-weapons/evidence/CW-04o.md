# CW-04o evidence — recipient protocol and bounded delivery

Base main `9adaacd3f` (CW-04n); latest main `38dc5b104` integrated as
`2afb1b8db` before final acceptance. Protocol/registry `183892efc`, pure tests
`3f12ff765`, core `81254d51d`. Scope: [delivery contract](../04o-recipient-delivery-contract.md).
Environment macOS arm64, .NET10.0.401, DOTNET_PROCESSOR_COUNT=2.
Final game and both tool-copy DLL SHA256:
`b787a1a6aadb94a3218a0b78bae6712a4143ef0c8841c06a4f082e1bf6602a0f`.

Protocol66 adds reliable Background94 replica page,95 replica event and96 replica
ACK. Pure delivery owners retain the recipient bootstrap independently of each
original source bootstrap/life. No live game-session handler or spectator
activation is claimed in this slice. The next integration must commit consumer
facts before sending candidate ACKs and keep observer capacity separate from
mandatory original-owner private payment.

| Check | Actual result | Local evidence under artifacts/custom-weapons/CW-04o |
| --- | --- | --- |
| Final main Release build | PASS71.88s, zero warnings/errors | build-replica-integrated.log |
| Fresh nettest / final custom tool builds | PASS3.405s /1.763s, zero warnings/errors | o-INTEGRATED-registry-nettest-build.json, o-INTEGRATED-custom-results.json |
| Pure contracts | PASS10476,2.135s | o-INTEGRATED-custom-results.json |
| New N capture and J/H/G/F scenes | PASS173/127/249/318/985 | o-INTEGRATED-custom-results.json |
| Offline / rewind scenes | PASS46/156 | same |
| Existing actual M/runtime/L/K/I/J UDP | PASS609/51/883/1268/134/84 | o-INTEGRATED-udp-results.json |
| Native lobby / waitlist | PASS7750/309,101.415s /54.884s | o-INTEGRATED-udp-results.json |
| Thirteen registry/native modes | All PASS, including91 new pause-map/source CPU checks | o-INTEGRATED-registry-native-results.json |
| Native tables and fourteen combat modes | All PASS | integrated-parent/o-native-results.json |
| Native accepted / Imperialist scenes | PASS343/949 | integrated-parent/o-content-results.json |
| Fidelity | 46 PASS; known single divergence | integrated-parent/native-fidelity-final.log |

The10476 pure run adds an independent Python-packed empty Life0 viewer page and
SHA256/115-byte ACK golden;64 flights in17 pages; arbitrary/reverse order, each
missing page, equal/conflicting duplicates and no partial output; exact recipient
occupancy/generation/source-life joins; original old-life source beside replacement
state; original source bootstrap contact decoding; same-world independent state
revisions; outer world fences and uint world0;219-byte flight offsets, actual
muzzle duplication and lifetime seconds bounds;3000 random payloads and every-byte
page mutation without throws; forged/unsent/foreign-connection ACKs; launch-before-
contact application consumption; fixed256-group backpressure; nonwrapping prepaid
token headroom and1024-token dedupe span; original deadline0 wrap, exact inclusive
expiry and repeated Advance without double release; no-spawn/uncertain handling;
and independently payable private leases after observer closure.

One hundred thousand warmed duplicate page/event/copy probes allocate0B and
produce one delivered fact. These are supplied pure authority facts, not actual
source authentication, UI cues or network combat. The existing real engine/UDP
suites provide the separate gameplay regressions; no new replica server handler
is exercised yet. The K client-only160ms RTT/10% seeded loss run records96 action
drops, six receipt packets and two contact packets, with one payment.

Review corrected a wrapped-deadline sentinel, absent/mismatched recipient and
future-source-life joins, stale outer worlds, a discarded duplicated muzzle field,
unbounded flight age, repeated owed-capacity decrement and unknown emission.
The unknown255 emission test reproduced RED against DLL
`a0f538946c523889daa419fa2e8b052363ebe65646cb53927bb5379fd9cd884c`, then passed
against the final DLL (o-unknown-emission-red-results.json). No wrap RED is claimed:
its copied preflight DLL already contained that fix. Native lifecycle can change
within a world, so state uses an independent nonwrapping revision instead of world
as its only version. Final canonical no-spawn/uncertain fixture corrections added
six checks with no game changes; the earlier10470 timing2.374s is retained as its
own execution and is not relabelled as the10476 run.

All92 preexisting registry names/IDs remain; static native78+custom80–96 registry
checks pass. Historical65 archive/checkpoint/status shapes remain supported; live
65 handshakes and unknown future67 refuse explicitly. Actual UDP delivers each
new semantic type at exactly1200-byte datagram budget including reliable ID.
Upstream packet/event/shot digests match N; only live protocol metadata changes.

Fidelity remains only missile splash tick177 victim.position4096.z,
expected52814/actual52815. No tolerance or baseline changed. Imperialist headshot
F2 remains `6fc157d2c71568e56a37675a590c04c764f29e2d8e2fc5f1cda394b3c3d78351`.
Exact commands/hash records are in the listed JSON files and
o-INTEGRATED-parent-validation.json. The earlier preintegration runs retain DLL
`e2e570695f2620bad30c7534d1a21f6ccdf2cd886f4967648ccacb9dcdaad2b3`
and separate metadata; they are not relabelled as executions of the integrated DLL.

Remaining CW-04 gates: live recipient adapters and baseline/ACK readiness,
late join/reconnect/spectator runtime delivery, source parity and final host/remote
fairness. Public input/HUD, custom replay, physical/rendered Android, WAN and
p50/p95/p99 are NOT_RUN. Review/CI are neither awaited nor reported PASS. Both
developer flags off remain rollback; CW-05 remains gated on the rest of CW-04.
