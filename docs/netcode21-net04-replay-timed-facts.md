# NET-04 authority-recorded timed shot facts

This change is an opt-in persistence prerequisite. It records an authority's
first immutable NET-01 certificate after a complete admitted emission, including
misses. It does not enable gameplay enforcement or reconstruct paid damage.

Set `PRIME_TIMED_REPLAY_FACTS=1` and `PRIME_TIMED_GRANT_PROOF=shadow` before the
authority session/recording starts. Ordinary Off and Shadow recordings retain
their existing replay format3/4/5 and replica-checkpoint12 bytes. Remote client
recordings have no local authority certificate capability. Internal controlled
fixtures can select Enforce, but production Enforce remains unavailable.

Opted authority recordings use format6 with an explicit certificate capability
and a separate nullable firing-continuation policy. Live protocol56 and
PacketType are unchanged. File-only tag251 declares producer scope; tag252/v1
is a fixed216-byte certificate observation. It retains full shot identity,
canonical FireEvent, original source/authority clocks, source evidence, capture
mode, raw ledger answer and sequence, final knowledge, full optional funding,
component count, and actual native damage channels/effect eligibility masks.
FireEvent keeps its existing packed view/reticle precision. The fact includes
no reconstructed trajectory, impact, or native launch geometry.

The private replay decoder owns a fixed8×256 value store allocated only when
the capability is enabled. Identity and source-frame lookups are exact; first
values are immutable; duplicates do not refresh their observation timestamp;
aliases/capacity are refused; age1024 is retained and age1025 expires. Old-life
flight is retained. Epoch/match/room changes fence state. No private decode,
seek, archive copy or range extraction reads live NetSession, the source
journal, current equipment, current timers, or the live grant ledger.

Only timed-capable decoder state writes checkpoint13/feature8. Ordinary state
writes the unchanged checkpoint12/feature7 contract. Both are decoded through
complete detached candidates before commit. The existing world capsule embeds
the decoder; no native object schema/appendix or live-wire change is added.
Format1–5 readers and historical checkpoint layouts remain supported without
invented timed proof. Old builds reject format6/checkpoint13. Existing byte
budgets remain enforced; an oversized seed/checkpoint cannot silently claim
complete coverage. Toggling this opt-in during an active recording is not an
upgrade path: the writer refuses facts/checkpoints inconsistent with its header.

A3 grant presentation/bootstrap and canonical authored fire are reused.
ReplayShotFact still owns actual paid damage/death/feedback. Existing private
hint-driven projectile emission is preserved, and replica damage remains
suppressed. These archival channels cannot be converted to a damage-template
ray or classified as a proven constant-body payout. Unknown source/ownership,
missing funding, unsupported families and capacity gaps remain unavailable.
Shadow channels describe what actually spawned; they are not a claim that all
timed modifiers were sanitized. Native projectile visual fidelity, authenticated
remote delivery, full world/Studio fast seek, natural pickup provenance and
general gameplay acceptance remain separate gates.

Prepared checks:

```text
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --timed-shot-replay
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --timed-shot-replay-scene <assets> [room]
```

The pure suite declares constructed storage fixtures and covers canonical
codecs, malformed values, raw/final knowledge separation, capacity/TTL/rollover,
immutable aliases, detached restore rollback, and published format3/4/5 byte
fixtures. The asset fixture includes a genuine native DoubleDamage item contact
and full normal-carrier steps for contact and C+1 firing, alongside a separately
declared resources/A1-reservation pipeline fixture. Actual accepted private
drains and native emission terminals are followed
by real file reopen, private passive decode/checkpoint/indexed reader seek,
archive copy and range extraction. It separately tests Off/no opt-in,
Shadow/no opt-in and Shadow/opt-in. No graphics/payment/native-pickup proof is
claimed. Tests have been prepared but not run in this implementation commit;
runtime acceptance is pending the exclusive execution slot.

Rollback: unset `PRIME_TIMED_REPLAY_FACTS`, leave production timed proof Off or
ordinary Shadow, or revert the NET-04 commit set. Existing recordings remain
readable; format6 files require a build that supports this archival schema.
