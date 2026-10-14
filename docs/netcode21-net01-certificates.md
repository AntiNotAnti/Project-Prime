# NET-01: immutable shot-time timed-grant certificates in Shadow

**Status: bounded local Shadow gates PASS; new PR hosted CI is tracked separately.** Fresh production `10601328` passes 15 focused regressions, 119 Python tests and 6,463 native assertions. The corrected controlled-remote fixture passes 343 assertions against that exact physical client; identical tests preserve an expected red on original `0c52bc8a`. The unchanged store passes 5,238 constructed assertions and the local benchmark. This is NET-01 correctness acceptance within the declared scope, with NET-02 through NET-04 still open.

## Hypothesis and change boundary

An authority observation of a timed grant can classify an admitted shot at its exact source frame, and that classification can remain immutable after grant renewal, expiry, death, component reuse, or source-journal compaction. NET-01 tests this hypothesis in a separately enabled Shadow path. It does not apply the certificate to projectile modifiers, rescue payouts, damage ceilings, or presentation; those are subsequent work.

`PRIME_TIMED_GRANT_PROOF=shadow` enables the new observer. Unset, `off`, or any other value selects **Off**, the default. The new mode has only Off and Shadow. It is independent of existing `PRIME_SHOT_PROOF=off/shadow/enforce`; enabling timed Shadow must not change that path's enforcement decisions. Timed certificates are authority-only, multiplayer-only, and excluded from private replay replicas.

The intended boundary preserves native ammo gating, cooldowns, damage, timed duration, collision, accepted-shot disposition, hit arbitration and exact-once payments. No packet field, packet number, protocol version, replay format, checkpoint capability, or timed-grant publication behavior is added. Native equivalence is an acceptance obligation, not something the pure storage tests prove.

## Evidence construction and admission

`NetTimedShotProof` uses the complete `ShotKey`—authority epoch, match, shooter slot, occupant generation, life and ShotId—plus exact immutable `FireEvent`, room and source frame. Arrival clocks and claim IDs cannot substitute for that identity. A certificate retains its authority observation frame, native/accepted lane, source/pose evidence, A1 answer, Granted/NotGranted/Unavailable classification, copied grant funding and completed component count.

The source evaluator is factored from existing `NetShotProof`. Timed Shadow suppresses only the legacy Double hint comparison while evaluating its own certificate: Double authorization comes from A1, while weapon, charge, ammo, cadence, affinity, Prime and source-pose contracts retain their independent checks. Hint agreement is three-state: true, false or unknown when ownership evidence is unavailable. A missing observation does not become a false mismatch or a negative grant.

A1 ownership is read through `NetTimedGrants.TryReadObservation` and `TryRecord`; the snapshot does not call the advancing `GrantedAt` lookup or publish new grant facts. Funding is copied after sequence, power-up type, full life, spawn epoch/match/room/incarnation, source contact, effective boundary and authority expiry agree. Thus contact-frame shots cannot borrow a later pickup, and a future source timestamp cannot extend an expired authority grant. Trusted source and copied Granted funding are both required for `AuthorizedDouble`; the property is diagnostic and has no damage consumer in NET-01.

There are two provenance gates:

- Local/bot native input enters an authority scope around actual `ProcessInput`, after the existing input observation. The attempt must match that scope's exact frame and life and the active native shot identity.
- Remote discrete playback can create an attempt only from the private admitted `DrainAccepted` lane and an exact pending ledger key/fire receipt. Direct helper playback, even with plausible shot metadata, does not supply that admission.

The attempt is taken before emission. Retention occurs only after the native spawn reports spend and completed `AddEntity` components with the same shot key. A success return without emitted components, partial exception, child, continuous/turret owner, zero component identity, unknown/nonfinite or over-bound lifespan cannot prove an ordinary emission. Duplicate completion preserves the first certificate; it cannot refresh its timestamp or resnapshot later grants. The shared resource journal records a completed spend once per exact ShotKey; timed Shadow does not maintain a competing mutable ammo ledger.

The observer has a private fixed **8 slots × 8 retained-life uncertainty table**. Unsupported or incomplete expenditure makes later timed source classification unavailable for that exact life. It preserves previously retained certificates and leaves legacy proof invalidation/enforcement untouched. Uncertainty can be recorded only for a retained matching journal life; retired-life uncertainty must not replace a newer life sharing the same modulo cell. The fresh `10601328` native run and corrected remote fixture validate these changes. Shared source observations are reused; the uncertainty table only records evidence loss.

## Storage, bounds and unsupported scope

The store is fixed **8 × 256 value cells**, with **1,024 authority frames** of retention. Age 1,024 is inclusive; age 1,025 expires, including uint rollover. Zero stream/shot/generation/life identities, mismatched fire/source life, absent component counts, expired/future authority candidates and out-of-range slots are invalid. Exact-key/source/room reads cannot borrow another certificate. Fire/source alias conflicts preserve the original. Full owner capacity refuses new retention without evicting live evidence. A life boundary preserves old-life flight; room, stream or occupant changes permit eligible cell reuse. Capacity is a refusal bound, not a promise to retain arbitrary offered traffic.

The source-backed bound is **510 + max(424, 124 + 144, 128, 256) = 934 nominal authority frames** from an ordinary originating emission. Native metadata lifespan 255 means 255/30 seconds, or 510 simulation frames at 60 Hz. Delivery 424 already contains maximum pending grace 144; world history 128 supports judgement within that obligation, and rewind is bounded at 120. These windows overlap rather than forming additional post-receipt waits. The 256-source-frame resource reorder window also fits. A small float/phase allowance fits within the 90-frame margin to 1024. Accepted-shot retention is 64 **source** frames, not an authority elapsed timer; proof observation occurs adjacent to emission. Even a diagnostic sum including that whole source backdate is 998. The separate turret negative-terminal bound 624 must not be appended to ordinary flights.

The detailed inspected constants and original-source anchors are in the [retention research report](/Users/jarrett/Documents/Codex/2026-10-13/implement-the-attached-project-prime-next/work/a3-review/net01-retention-bounds-0c8108ba.md). Ordinary Power Beam, Volt Driver, Missile, Battlehammer, Imperialist and originating Magmaul rows fit the timing bound. Timing fit does not remove genuine source or mechanics limitations:

- Continuous ticks, turrets, children, immediate AOE and effect descendants remain unsupported. NET-03 owns their accounting/provenance. Judicator and Omega are explicitly excluded by the snapshot's weapon-family gate.
- Automatic Power Beam cadence remains source-input unavailable. Reported Enhanced context retains the legacy source limitation. Homing flight itself does not require a longer horizon, but an Enhanced child or unsupported effect does not inherit ordinary support.
- Magmaul's parent duration fits; burn/pool/effect descendants are not certified from that parent. Unknown/custom rows and enemy/platform owner emissions do not acquire ordinary player support from a shared beam enum.

Unsupported expenditure must leave subsequent timed funding uncertain, rather than silently treating it as free. No unsupported family is promoted to damage enforcement or authenticated proof.

## Qualified current production

The fresh physical client is source `106013282eed587fdbe0583bcd26e52aa2637fa4`, tree `dd3b062535c365a92a8e18c69de7f0d19f060201`, SHA-256 `4020f302ff8f7c8b1efddcd72a365a09fa4dc15449a094ec892a80cabdbf9ccf`, MVID `43453875-0ba3-452d-8a94-33ed8e0df8a1`, BuildId `local+106013282eed587fdbe0583bcd26e52aa2637fa4`. Protocol 56 / PPRC12 / world10 remain unchanged. Later test/document edits do not relabel this physical binary.

The 6,463-assertion native run proves full headless keyboard/input/Spawn/Commit, real native Double contact and source chronology, immutable later expiry, rejected helper certification, and identical unmodified canonical intent/replay bytes, damage, lifespan, ammo and semantics across old Off/Shadow × new Off/Shadow. The no-target shot has one real replay WeaponFired semantic fact and zero impact ReplayShotFacts. Natural zero bank switches to PowerBeam and can truthfully emit it. A separate controlled mid-frame depletion reaches native NoAmmo without spend or certificate. A declared post-spend setter fault changes the native bank 100→90, produces exactly one expected simulation failure and no certificate, preserves an earlier certificate and leaves later source evidence unavailable. It is a negative fault injection, not a natural positive arm.

The corrected remote fixture uses actual lifecycle/event carrier intake, Pending ledger entries, private DrainAccepted and native projectile terminals. Its explicitly declared resource/reservation seeds are separate from genuine native pickup evidence. It passes 343 checks: Double hints cannot manufacture/remove A1 funding; Prime/affinity remain independent; C/C+1/C+1799/C+1800, expiry, renewal and immutable retrieval agree; helpers/incomplete pose do not certify; unsupported expenditure and retired modulo aliases remain unavailable; old Off/Shadow/Enforce terminal outcomes and accepted replay bytes remain equal under new Off/Shadow. The deliberately adjacent expiry fire retains its real cooldown failure and only proves ownership absence.

The original combined106 remote fixture failed because its 2800 carrier contradicted the already observed 2799 Shoot=true with false backfill. The input proof correctly poisoned that life. The corrected test supplies the actual preceding input; identical corrected test source SHA `04f4f1c98f4ee4a8f35f6070b32078bc74c351185af3bdeb9c217255937e6a3c` passes against frozen106 in a newly compiled external friend harness. Against original0c it fails after 178 checks at the genuine unsupported-spend falsifier. Both production clients remain unchanged and independently identified. The external harness is not relabeled as original106 nettest.

[Portable measured results](evidence/net01-shadow-results.json) preserve raw benchmark samples, identities, positive scopes, negatives and rollback. Full commands/logs/process receipts remain in the implementation task's `work/net01-evidence/`, including `candidate-10601328`, `accepted-diagnostic106`, `accepted-original0c` and `accepted-proof-summary.json`. All owned groups close and original asset Paths remain unchanged. Current source follows merged main f85, whose 31 hosted checks are green; this new PR needs its own exact-head CI. No hosted-build wait is implied by local qualification.

## Earlier frozen evidence and limits

The tested physical candidate was built from clean source **`0c52bc8a91b072f2b7cc4fc04bc69b0df82839b7`**, tree `64fb17e12a528a45f9d392cd6af2001d0f0d24d7`. Its loaded identity is independent of a subsequently changed checkout:

| Module | Actual SHA-256 | MVID |
|---|---|---|
| ProjectPrime.dll | `d34157766d7615cd035300017faae88f45280f27ff4dd6ff121414c5748b96ec` | `f3e07399-2347-4c12-be67-0e6c9a78a95b` |
| nettest.dll | `82985816f263c22b6028d8401db6f3bc562ed8404e43ecc721c72e51cbef33c6` | `18b8e25a-7bcc-4131-8d41-33ee0121c043` |

Fifteen existing focused runs returned exit 0: architecture, protocol19, timed-grants, shot-source-journal, pickup-protocol, pickup-claims, pickup-network, pickup-replay, terminal-retries, claim-stress, timed-grant-codec, timed-grant-presentation, grant-network, grant-replay and reliable. Relevant Python checks passed **119 tests**. The [candidate receipt](/Users/jarrett/Documents/Codex/2026-10-13/implement-the-attached-project-prime-next/work/net01-evidence/candidate-0c52bc8a/receipt.json) retains commands, logs and the physical-source tree; the [Python log](/Users/jarrett/Documents/Codex/2026-10-13/implement-the-attached-project-prime-next/work/net01-evidence/candidate-0c52bc8a/python-relevant.log) retains its result. These are compatibility checks, not native shot-time pickup equivalence.

`--timed-shot-certificates` passed **5,238 pure constructed storage assertions** on the frozen candidate: full identity/source/room, immutable later candidates, alias refusal, all 2,048 cells, overflow preservation, duplicate timestamp nonrefresh, 1024/1025 expiry, rollover, old-life flight and scope reuse. Constructed values prove storage semantics only; they do not authenticate fire admission, execute native emission or establish grant funding. The [pure receipt](/Users/jarrett/Documents/Codex/2026-10-13/implement-the-attached-project-prime-next/work/net01-evidence/pure-0c52/receipt.json) and [log](/Users/jarrett/Documents/Codex/2026-10-13/implement-the-attached-project-prime-next/work/net01-evidence/pure-0c52/pure.log) retain clean-environment commands, startup physical identity and closed owned process groups.

The storage benchmark ran on .NET 10.0.12 Arm64, five raw batches of 20,000 operations per workload after 10,000 warmup operations. Each measured batch reported 0 thread-allocated bytes, excluding setup, reporting and CPU timer queries. This is a measured local result, not a universal allocation or optimization claim. Elapsed milliseconds, with every sample retained:

| Workload | Samples, ms |
|---|---|
| Record | 120.016666, 14.226500, 14.204584, 13.698541, 13.635875 |
| Duplicate | 10.995084, 11.178417, 11.168417, 11.255000, 10.778583 |
| Read | 25.879875, 25.798333, 26.254292, 26.127792, 25.958875 |
| Full-capacity refusal | 18.263292, 18.347250, 18.021042, 18.007125, 18.567125 |

The first Record outlier is preserved. Process CPU is captured separately and includes runtime/background threads and timer overhead. Checksums agree across samples. [Raw samples](/Users/jarrett/Documents/Codex/2026-10-13/implement-the-attached-project-prime-next/work/net01-evidence/pure-0c52/benchmark.json) and [raw stdout](/Users/jarrett/Documents/Codex/2026-10-13/implement-the-attached-project-prime-next/work/net01-evidence/pure-0c52/benchmark.log) stay outside Git; no synthetic speedup is inferred.

Negative evidence is retained. Candidate `b66a730729ba504b6b405b3251578c628b11c39f` failed the full nettest build: the native fixture referenced nonexistent `PlayerEntity.Input` at lines 113/114. An emitted production DLL from that partial build is not a successful integration runtime. Its initial process-closure result was also false; the later disposition observed no owned members and required no cleanup action. The [failed build log](/Users/jarrett/Documents/Codex/2026-10-13/implement-the-attached-project-prime-next/work/net01-evidence/candidate-b66a7307/build.log) and [disposition](/Users/jarrett/Documents/Codex/2026-10-13/implement-the-attached-project-prime-next/work/net01-evidence/candidate-b66a7307/failed-build-disposition.json) preserve the original failure. The later `0c52bc8a` fixture fixes the sampled-control observation and completes the build.

The successful `0c52bc8a` build initially left an owned Avalonia build-services collector running, so its original process-closure result is false. The [cleanup disposition](/Users/jarrett/Documents/Codex/2026-10-13/implement-the-attached-project-prime-next/work/net01-evidence/candidate-0c52bc8a/setup-closure/disposition.json) records identity-verified SIGTERM and absence afterward; the original receipt is preserved. Compile success, setup-process closure and gameplay acceptance are separate facts.

The earlier frozen results above retain their original identities. The current qualification supersedes their pending native/remote work without erasing original failures. Authenticated UDP, GL/audio, broad platform runtime play and exhaustive post-spend failure locations remain unverified; CI packaging is tracked in the PR. NET-02 damage enforcement, NET-03 special families and NET-04 persisted certificate rollout remain separate gates.

## Reproduction and rollback

Build once after final source is stable, freeze the physical outputs, and capture loaded SHA/MVID outside timed samples. Run from fresh evidence/profile directories with inherited experimental environment removed. The pure commands are:

```sh
/absolute/path/to/dotnet /absolute/path/to/frozen/nettest.dll --timed-shot-certificates
/absolute/path/to/dotnet /absolute/path/to/frozen/nettest.dll --timed-shot-certificate-benchmark /absolute/path/to/fresh-evidence/benchmark.json
```

The asset-backed command is `dotnet /absolute/path/to/frozen/nettest.dll --timed-shot-native /absolute/path/to/assets "MP1 SANCTORUS"`. Its result must be reported separately from pure storage and remote/authentication coverage. The existing focused dispatch names above reproduce compatibility checks; exact frozen invocations and Python selections are in the receipts.

Runtime rollback is to unset `PRIME_TIMED_GRANT_PROOF` or set it to `off`. It does not require changing the existing shot-proof mode or protocol negotiation. Code rollback reverts the complete NET-01 implementation/test/document commit set while preserving accepted A1–A3 prerequisites. Revert all changes in this focused PR as a unit; the preliminary `b66a7307` feature commit alone is not the complete rollback after follow-up fixes.
