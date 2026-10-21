# CW-12f23 � CPU post-world replay sessions

Base: `d9155dcf0b1344dc8e4db791c61b291e55e83272` (automatic map-generator stamp integration).

This slice applies format8 phases in a private CPU session. Scene, pose/fire,
bounded player, theatre and extraction admission remain separate, closed gates
for8. The new session is not a second gameplay authority and does not create a
Scene, grant a weapon, establish readiness or start server recording.

## Preparation and attachment

`PreparedCustomReplayArchive.PrepareInspection` explicitly admits complete format7
or8 archives, with an exact World12/13 baseline and historical package resources.
Format8 requires a finalized/recovered file; a footerless `.part` must be recovered
through the existing storage path before session inspection. Cancellation and
resource lifetime follow the existing bounded prepared owner. Ordinary `Prepare`
and request preparation remain format7-only.

Attachment restores complete WorldK's decoder and seeks inclusively for8. It
skips ordinary K facts already represented by that decoder and retains deferred
K. Merely attaching or pausing cannot apply that suffix. Baseline identity checks
now include file format as well as existing descriptor/world/clock identities.

## Source and application clocks

Before step N+1, the session injects deferred N facts with source recording clock
N, followed by ordinary N+1. It leaves deferred N+1 for the following step. The
combined groups share4096-record/8MiB bounds; format7 retains its existing bounds
and behavior. Custom authority clocks, input carrier/source times, received-input
ages, grant deadlines, chat times and hit freshness are not retagged.

The replica has an explicit `AcceptPostWorld` projection. Only native ShotFacts'
transient simulation-batch stamp uses application N+1. This lets multiple deferred
N and ordinary N+1 hit facts survive the normal `State.Advance(N+1)` boundary as
one batch. The following simulation boundary clears that batch normally. Regular
native `Accept(packet,frame)` supplies the same source and application frame as
before. No CP17 or wire bytes change.

The final deferred suffix stays inert because no following simulation step exists.
Its entire storage suffix is still consumed for integrity validation, including
later physical chunks, both on reaching EOF and on attaching at an EOF checkpoint.
Corruption is reported without applying those facts or advancing beyond duration.
Semantic254 records remain metadata; construction253 cannot replace an active
inspection world.

## Closed renderer boundary and next dependency

The ordinary preparation path and request constructor continue refusing8.
`PassiveReplayScene.OpenCustomArchiveInspection` also refuses8 before creating a
Scene, so the existing player cannot accidentally admit it through CPU preparation.
Format7 Scene/player/theatre behavior remains available under prior feature flags.

The next slice must implement distinct source, availability and scheduling clocks
in both independent pose/fire readers. Future snapshot/ACK interpolation must not
expose a post-world sample before N+1; source age, generation/life and shot identity
must remain unchanged. That requires actual Scene/cold-seek evidence before opening
any format8 Scene or theatre path. Trusted authority capture and server package
ownership follow that consumer work. Full CW-12 and CW-13 through CW-16 remain open.

See [local evidence](evidence/CW-12f23.md). Tests use constructed accepted-width
facts and content-free World12 envelopes. They do not represent actual traffic,
a loaded Scene, authority damage or server recording. Rollback leaves the explicit
CPU entry point unused; never reinterpret format8 bytes as7. Android is deferred.
