# CW-12f22 — post-world replay storage

Base after integrating PRs993/999/1000: `46746cc528a29eb3c56fe1ae2488511502c2268b`.

Facts can be accepted at recording clock N after the completed private WorldN
has already been captured. Rewriting them to N+1 changes source-input ages;
dropping them at a durable checkpoint loses accepted facts. This slice defines
storage for that phase. It does not select format8 in a live recorder or admit
format8 to Scene playback, pose readers, ordinary theatre or extraction.

## File contract

File format8 retains format7's exact custom descriptor, CP17 construction,
optional explicit World12/13 and PCW1 metadata capability. Recipient CWRB1 and
authority CWRB2 remain distinct valid metadata origins. The file version declares
the phase extension; protocol85 and all existing network packets are unchanged.
This storage contract does not imply that an authority World13 exists.

A file-only envelope has tag249, version1, phase1 (post-world), reserved byte0,
a four-byte little-endian inner length and exact canonical inner bytes. Its
outer record keeps recording frame N. The application contract is to retain it
while paused at WorldN and apply it, with its original clock, before step N+1.
At EOF it remains inert until a following simulation step exists. This is a
future consumer requirement; no consumer is enabled in this slice.

Only explicitly validated recorder families can be enveloped: canonical current
SlotIntent, match/configuration/roster/snapshot, authority-world fragments,
cosmetics/chat/semantic/award/native shot facts, native bomb/grant/pickup/turret
facts, timed-shot251/252, custom250 and semantic254. Parsers validate syntax;
stateful match/lifecycle/sequence acceptance still belongs to the replica.
Intent uses the complete current canonical size, nonzero lifecycle and existing
fire-event validation. Inner native records retain their native packet limit;
custom records retain16KiB. Nested249, construction253, quiet255 and unknown
families are refused. The enclosing archive must independently declare custom
and timed capabilities. No wire packet can become a file-phase declaration.

Both writer admission and complete-chunk reader validation enforce bounded
frame ordering. Ordinary nonquiet frame0 is refused; baseline suffix facts use
post-world0. At any frame, ordinary records precede post-world records, followed
optionally by a quiet frontier. A quiet frontier cannot have further same-frame
facts. The combined preceding deferred phase and current frame are capped at
4096 records and8MiB, conservatively counting the current deferred suffix too.
These limits persist across compressed chunks. Reader preflight validates the
whole chunk before exposing any prefix, including valid-CRC malformed payloads.

## Cursor, copy and recovery

`DemoReader.SeekAtOrAfter(K)` and the corresponding indexed reader API retain
equal-clock records, including multiple chunks ending at K. Format8 seeks warm
the preceding phase for the combined memory budget before filtering to K.
`SeekAfter(K)` keeps its exclusive behavior. Recoverable footerless files use a
sequential validated scan for inclusive or format8 seeks. A future checkpoint consumer must
skip ordinary K facts represented by WorldK while retaining post-world K in file
order. Neither API itself applies records or advances a Scene.

Copy and complete-chunk recovery preserve format8 and exact envelopes. Range
extraction explicitly returns UnsupportedFormat before output creation until it
can preserve origin/end phases. Existing format7 extraction remains supported. The native-format check now
requires format8 to reject native metadata without a custom capability and keeps
version9 as the unsupported future-version adversary.
Prepared custom playback remains restricted to format7. Ordinary native playback
continues refusing custom archive metadata. Installation and gameplay acquisition
are unaffected.

## Validation and remaining work

See [local evidence](evidence/CW-12f22.md). Fixtures are constructed storage inputs,
including a content-free World12 capsule. They do not represent a loaded authority
Scene, real late server traffic or a server recording. Existing actual format7
client recording/player/theatre regressions provide compatibility evidence.

Next: implement bounded phase application and distinct pose visibility fences,
then capture actual committed authority facts with a retained prepared package.
Cold seeks must retain post-world K; pose/fire readers must distinguish visibility
N+1 from source carrier N. Server capture, bots, CW-13 through CW-16 and deferred
Android acceptance remain open. Rollback keeps format8 writers and consumers
unselected; do not relabel format8 bytes as format7.
