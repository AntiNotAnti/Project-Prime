# G4 ranking specification — draft awaiting approval

Status: **proposal only; the eight-player policy has not been approved and must not be implemented yet.** This document follows G4.1–G4.15 of [the implementation plan](PROJECT_PRIME_G1_G5_IMPLEMENTATION_PLAN.md). The evidence section records static observations from the exact user-owned ROM. Later sections propose new behavior and explicitly identify departures. No ROM assets are included.

Read-only investigation scratch: `/tmp/codex-re-prime-g4/rp/audit.py` and `audit.txt`. The standalone reproduction below makes the numeric evidence recoverable without depending on those temporary files.

## Exact target

ROM `/Users/jarrett/Downloads/Metroid Prime - Hunters (USA) (Rev 1).nds`, game AMHE, revision 1, SHA256 `bcd9c2d408825589c35c6754c0efb547cbae78fbda9ce7f69500a9cab8e70b8f`.
ARM9 ROM offset 0x4000, load 0x02004000, entry 0x02004800, compressed size 0x7EDA4; raw SHA256 `2d17479a1c4cbd7f6fce58935b344aeb4607ea0bbf6a30a3edbe01b88c1c8aa9`.
ndspy code decompression produces 909912 bytes, SHA256 `1b70b078ec1b026004c89272acf619e7510e60fd294aa776b7bda48733ae850b`, exactly equal to repository AMHE1/_bin/arm9.bin. All addresses below use the header's actual 0x02004000 load address, not an assumed 0x02000000.

## Confirmed threshold and bounds behavior

At decompressed file offset 0xC74DC / RAM 0x020CB4DC are five little-endian u16 values: **40, 140, 390, 750, 851**. Consumer 0x02056A20 clears bits 24..26 of license+0x18, then 0x02056A3C..0x02056A78 increments this three-bit tier for each threshold at or below the license's u16 points at +0x1C. Thus legal points 0..850 map to zero-based tiers 0..4, with boundaries 40/140/390/750. Literal 0x02056B64 references the table.

At 0x02056930..0x02056944 the fifth threshold is loaded and decremented, yielding **850**, and 0x020569C8..0x020569D0 clamps an increased total to that maximum. Loss 0x020569F4..0x02056A08 saturates at zero. The fifth threshold 851 is an upper sentinel, not a sixth legal star tier. Visible names and glyph counts are not established by this numeric consumer alone; they are supplied by the requested plan/independent historical-reference audit.

License base used by this ROM consumer is 0x020EB8A8, making points address 0x020EB8C4 and tier bitfield address 0x020EB8C0. A historical copy of the offline inspection helper recorded `LicenseInfo.RankPoints` at u16 offset 0x1C, which agrees with this consumer. Its historical license absolute addresses are for keys a76e and amhp1, not AMHE1; do not transplant 0x020EB948 from those entries into this target.

## Confirmed point matrix

At decompressed offset 0xC7560 / RAM 0x020CB560 is a 100-byte table: row stride 20, column stride 4, each cell two little-endian u16 fields, gain at+0 and loss at+2. Literal 0x02056B68 references it.

| Self tier / opponent tier | 1 | 2 | 3 | 4 | 5 |
|---|---|---|---|---|---|
| 1 | +2 / -1 | +4 / -1 | +7 / -1 | +10 / 0 | +15 / 0 |
| 2 | +1 / -2 | +3 / -3 | +6 / -1 | +10 / 0 | +15 / 0 |
| 3 | +1 / -4 | +2 / -3 | +4 / -4 | +8 / -3 | +12 / -2 |
| 4 | +1 / -8 | +1 / -8 | +4 / -6 | +5 / -5 | +8 / -4 |
| 5 | +1 / -15 | +1 / -12 | +2 / -10 | +4 / -8 | +6 / -6 |

Consumer 0x020569A4..0x020569BC computes row from r7+0xA0 and column from r4+0xA0, then reads gain. Consumer 0x020569E0..0x020569F4 computes the same cell and reads loss at+2. r7 is participant-array base plus local slot at 0x02056414; r4 advances one participant byte per loop; local index is skipped at 0x02056968. Therefore self-row/opponent-column is strongly supported by direct data flow, not chosen from a web table.

Placement comparison at 0x02056994..0x020569DC uses participant bytes at+0x250: lower self placement gains, higher self placement loses, equal placement makes no change. It loops over four participant slots, excludes self, gates participation through a bit in player+0x84C, skips a nonzero byte at player+0x84E, and conditionally skips same-team opponents (participant+0x9C). Exact semantic names for those participation flags remain unverified; copying this whole match-eligibility policy into a new backend is outside this bounded audit.

A second consumer 0x0205A9F4..0x0205AAEC reads loss entries, subtracts with zero saturation, clears processed flagged records and recomputes the tier. Its surrounding lifecycle meaning remains unverified, so this is not claimed as a verified disconnect-penalty design.

## Limits and compatibility conclusions

The matrix is asymmetric and differs from a secondary web guide supplied by the other research agent. Do not substitute the guide's matrix for this exact Rev1 binary. This audit proves static values and arithmetic/data-flow consumers for the named ROM, not live Nintendo service policy, original authoritative validation, other regional/revision versions, or a complete modern rating/security policy. No binary execution or live original-game session was performed.


## Reproduce the numeric evidence

Use a local copy of the exact ROM and a Python environment with `ndspy`; no ROM upload is needed. The code below validates both compressed and decompressed hashes before reading the tables. It does not modify the ROM or repository. `ndspy.codeCompression` handles the ARM9 compression; a hex viewer or ARM disassembler can independently inspect the addresses and consumer instructions listed above.

```python
from pathlib import Path
import hashlib, struct
from ndspy.codeCompression import decompress

rom = Path("Metroid Prime - Hunters (USA) (Rev 1).nds").read_bytes()
assert hashlib.sha256(rom).hexdigest() == "bcd9c2d408825589c35c6754c0efb547cbae78fbda9ce7f69500a9cab8e70b8f"
assert rom[12:16] == b"AMHE" and rom[30] == 1
offset, entry, load, size = struct.unpack_from("<4I", rom, 0x20)
assert (offset, entry, load, size) == (0x4000, 0x02004800, 0x02004000, 0x7EDA4)
raw = rom[offset:offset + size]
assert hashlib.sha256(raw).hexdigest() == "2d17479a1c4cbd7f6fce58935b344aeb4607ea0bbf6a30a3edbe01b88c1c8aa9"
arm9 = decompress(raw)
assert hashlib.sha256(arm9).hexdigest() == "1b70b078ec1b026004c89272acf619e7510e60fd294aa776b7bda48733ae850b"
print(struct.unpack_from("<5H", arm9, 0x020CB4DC - load))
for self_rank in range(5):
    print([struct.unpack_from("<HH", arm9,
        0x020CB560 - load + self_rank * 20 + opponent_rank * 4)
        for opponent_rank in range(5)])
```

## Proposed modern policy: PairwiseNormalizedV1

This is a concrete proposal for approval, not a claim of retail equivalence. The recommended model uses the verified matrix unchanged, current Backend ranks frozen together inside the report transaction, signed integer summation, a three-opponent normalization cap and one final clamp. Its version identifier must be recorded with every transaction.

### Eligibility and standings

Only completed official matches between authenticated registered humans on backend-authorized VerifiedCasual or Ranked servers qualify. Tournament rating is disabled until an explicit backend policy enables it. Practice, Private, Community, guests and bots never receive or contribute official RP. Proposed v1 conservatively excludes the entire match from RP if a participating combatant is a bot or unauthenticated guest; spectators are not combatants. This is stricter than merely omitting bot pairs and avoids indirect bot influence on official placement. Separate practice/community ledgers remain possible.

Require at least two eligible humans and at least one opposing pair. Use the authority's immutable finalized standings: individual `Standings` for free-for-all; `TeamStanding` for team modes, comparing only opposing teams. Among finishers, equal standings contribute zero, even if display order has a slot/name tie-break; the forfeit ordering below takes precedence. Teammates never exchange RP. Reject malformed, missing or contradictory standings instead of inventing an order. Team membership and participant identity must refer to the completed participation record, not whichever connection currently occupies a slot.

The official rating roster and team assignment are frozen when Playing begins and retain every starting participant through completion, including quitters. Official matches require `SpectateUntilNextMatch`: mid-match arrivals and slot replacements observe until the next match and cannot acquire a rating place. A guest or bot cannot replace a departing official participant. A valid match is not cancelled merely because a participant leaves.

**Proposed anti-quit rule (new behavior, not attributed to retail):**

- An explicit Leave/forfeit immediately marks that participant forfeited for this match. Transport loss begins a **30-second grace period (1,800 authoritative ticks)** when the authority detects the disconnect. Reconnection must prove the same PlayerId, occur before that deadline and before match completion, and resume the retained participant. It does not create a second entry or erase earlier statistics.
- A grace deadline that expires, or a match that completes while the participant is still disconnected, finalizes a forfeit. No post-result reconnect can rewrite the ledger. Once forfeited, a reconnect can spectate until the next match; repeatedly reconnecting after the deadline cannot reset the penalty.
- Every forfeit ranks below every finisher; all forfeits tie with each other. For free-for-all, compare this forfeit/finisher class first, then the authority's normal standing among finishers. In team modes exclude teammates as usual, compare forfeit/finisher class first for each opposing pair, then finalized team standing when both finished. A quitter therefore loses against every opposing finisher even if the quitter's team wins. No additional flat penalty is invented.
- Retain forfeits in the opposing-human count used for normalization. Finishing humans gain from them; quitters cannot remove losing pairs or cancel other players' earned changes. If everyone forfeits, all pairs tie and RP is unchanged; this is an explicit outcome, not deletion of the match. Zero-point floor and zero-loss matrix entries still apply, so some forfeits legitimately produce zero applied loss.
- A participant within reconnect grace remains a contender for departure-triggered completion (but does not postpone an ordinary goal/time-limit finish). Once only one non-forfeited FFA participant or one non-forfeited team remains, the authority completes the valid started match using the retained roster and forfeit outcomes. Departure alone must not produce `InvalidTeams`, an aborted report, or a roster with the quitter erased. If nobody remains, the authority still completes and reports the all-forfeit result. This needs an explicit lifecycle integration test because current slot cleanup is not sufficient.

A genuinely aborted match, operator-forced completion or invalid starting configuration is excluded from RP, with a durable reason. `Forced` and `InvalidTeams` are never accepted as ordinary rated completion. These exclusions cannot be selected by a client, and normal departures are handled by the completion rule above rather than routed through these exceptions. Administrative misuse of abort remains a verified-server trust/operations concern. Individual forfeit outcome controls career W/L (forfeit = loss); among finishers normal individual/team standing determines W/L or draw. All-forfeit matches record losses for career purposes despite pairwise RP ties.

### Deterministic calculation

1. After establishing this is a new eligible report, lock all participating player/license rows in stable PlayerId order. Read their **current committed** Backend points together inside this transaction; reject values outside 0..850. Freeze these balances/tiers for the whole calculation, deriving each tier from boundaries 40/140/390/750. Neither server nor client supplies authoritative points or tiers. A duplicate returns its original receipt before recalculation.
2. For player `p`, inspect each eligible opposing human `q` once. Read matrix cell `[transactionTier(p), transactionTier(q)]`. Use the forfeit-aware pair ordering above. A better final standing contributes `+gain`; a worse standing contributes `-loss`; a tie contributes zero.
3. Sum all signed contributions in an integer `rawDelta`. Do not update tiers, clamp intermediate totals, or round per pair. Enumeration order cannot affect the answer.
4. Let `n` be the number of eligible opposing humans, including tied opponents. Compute `scaledDelta = truncateTowardZero(rawDelta * min(3, n) / n)` using signed integer arithmetic. There is no per-pair rounding. Zero opponents is ineligible, so division by zero is impossible. Then compute `pointsAfter = clamp(pointsBefore + scaledDelta, 0, 850)` once. Record `appliedDelta = pointsAfter - pointsBefore` separately from `rawDelta` and `scaledDelta`, along with both tiers, opponent count and all pair contributions.
5. Commit all players' transactions, the immutable match ledger and aggregate updates atomically. A repeated identical MatchId returns the stored result; a conflicting payload for that ID is rejected.

### Concrete normalization examples

The multiplier is one for one, two or three opponents and `3/n` for four through seven opponents. Truncation is toward zero for both signs: +8*3/7 becomes+3 and -8*3/7 becomes-3. Include tied opponents in the denominator so changing which opponents tie cannot amplify an unchanged nonzero pair contribution. Scores use the transaction-frozen current tiers for every pair even when an eventual RP change crosses a tier boundary.

| Match / equal transaction-frozen tier | Winner raw → scaled | Last-place raw → scaled |
|---|---:|---:|
| Two-player FFA, tier 1 | +2 → +2 | -1 → -1 |
| Four-player FFA, tier 1 | +6 → +6 | -3 → -3 |
| Eight-player FFA, tier 1 | +14 → +6 | -7 → -3 |
| Two-player FFA, tier5 | +6 → +6 | -6 → -6 |
| Four-player FFA, tier5 | +18 → +18 | -18 → -18 |
| Eight-player FFA, tier5 | +42 → +18 | -42 → -18 |
| 2v2 teams, tier 1 (two opponents each) | +4 → +4 | -2 → -2 |
| 4v4 teams, tier 1 (four opponents each) | +8 → +6 | -4 → -3 |

Team pairs compare finalized team standings; teammates are excluded from both the sum and denominator. A tied match produces zero. Uneven teams are handled by each player's own opposing-human count; this is deterministic but does not assert balanced progression for unequal team sizes. The verified-server rules policy may restrict official team sizes separately.

At 848 RP with normalizedDelta+12, the final balance is 850 and appliedDelta+2. At 2 RP with normalizedDelta-10, the balance is 0 and appliedDelta-2. At 0 RP with two opposing contributions -2 and+4, raw and scaled deltas are+2 and the final balance is 2 regardless of enumeration order. Retail per-opponent saturation could produce 4 if the loss is processed first; this proposal intentionally removes that ordering effect. The asymmetric retail matrix is not zero-sum; this proposal does not silently rebalance it. The raw magnitude is at most 105 across seven pairs; normalization bounds magnitude to 45 before the final balance clamp.

### Anti-quit and transaction examples

- Two tier-1 humans start at20 RP each. One explicitly quits: finisher22, quitter19. The quitter remains an opponent and the match is not cancelled.
- Four tier-1 humans start at20 RP; two finish first/second, two forfeit. Contributions are first `+2+2+2 = +6`, second `-1+2+2 = +3`, each forfeit `-1-1+0 = -2`. Final balances26/23/18/18. Equal forfeits exchange zero; they still contribute to each other's denominator.
- In2v2 with all tier1, teamA wins but one A participant forfeits. The A finisher earns+4 from opposing finishers; the A quitter loses2; each B finisher nets+1 from beating the A quitter and losing to the A finisher. This deliberate personal anti-quit override does not rewrite the authoritative team score.
- A player saw39 RP when a match started, but an earlier accepted result raised the current balance to41. This report uses tier2 for every pair involving that player, freezes41 as `pointsBefore`, and records that input. It does not reserve39 or overwrite the earlier result. Duplicate submission returns exactly the first accepted output.

### Alternative considered: unnormalized sum

A simpler `PairwiseSumV1` would use `rawDelta` directly and clamp once. Eight-player FFA then offers seven pairs instead of three: tier 1 winner gains 14 rather than 6, and maximum raw magnitude rises from 45 to 105. The absolute 0..850 balance limit does not prevent faster progression away from the endpoints. This alternative departs from the plan's normalization recommendation and is **not recommended**. It must not be selected silently during implementation.

### Deliberate departures and approval scope

- Retail iterates up to four participant slots and saturates after each opponent; proposal supports up to eight, normalizes to at most three opposing-player contributions, and clamps once.
- Numeric table and thresholds are verified for USA Rev1 only. Titles and win-emblem milestones come from the plan/historical research, not this table consumer.
- Modern identity, server trust, roster eligibility, authenticated reports and idempotency are new backend rules. Retail participation bits and the second loss consumer remain unresolved.
- Current-at-transaction rank semantics, the fixed starting roster, 30-second reconnect grace, forfeit ordering, eligibility exclusions and normalization/rounding are proposed modern choices requiring approval together. No hidden MMR is introduced; future MMR remains separate.

## Backend domain review and implementation boundaries

The initial audit found only the immutable gameplay snapshot and session-scoped wire MatchId. The G4 foundation now adds persistent PlayerId, a separate UUID report MatchId, authenticated Backend admission, a durable server outbox, and PostgreSQL career/history projections. The wire match counter remains distinct from the durable report identity. Ranking Points, star progression, rating transactions, and the proposed anti-quit lifecycle remain pending approval of this specification; existing gameplay result fields do not establish a persisted Hunter License rank.

| Boundary | Owns | Must not own |
|---|---|---|
| Game | Immutable PlayerId value; gameplay result and minimal identity contracts | Database entities, HTTP calls, account credentials, rating state, wall-clock authority |
| Server | Validated ticket-to-participant binding; UUID match/report envelope; trust identity; bounded asynchronous outbox | Client-supplied RP/trust; synchronous database/network work on simulation thread |
| Backend identity | ASP.NET Identity account authentication; immutable PlayerId mapping; short-lived server-bound tickets and replay checks | Game slot/IP/name as persistent identity; custom password cryptography |
| Backend match ledger | Authenticated reporter, schema/content validation, canonical payload hash, idempotent report transaction | Trusting a body's claimed server or accepting changed data under an existing MatchId |
| Backend rating | Approved versioned pure calculation, transaction-frozen current balances and immutable transactions | Client standings authority; unapproved eight-player/forfeit policy |
| Backend queries / Client | Paginated license/history/leaderboard DTOs and presentation | Mutating career stats or deriving official RP locally |

The server report should wrap the gameplay result in a persistence envelope containing globally unique MatchId, reporter identity, UTC start/end, protocol/build and canonical rules version, authenticated participant IDs, human/bot classification and completion status. Keep the small network counter distinct. Capture participants throughout the match so disconnected/replaced slots do not erase attribution. Map display names as historical labels while PlayerId retains ownership across renames.

The raw match ledger plus versioned rating input/contribution ledger must be sufficient for rebuilding aggregates. Store before/after points, raw/applied delta, transaction-frozen input points/tiers and processing sequence, policy version and explicit eligibility result. Derived tier should not be independently mutable. Match UUID uniqueness and per-match/per-player transaction uniqueness belong in database constraints; match validation, rating writes and aggregate writes share one database transaction. Lock affected player records in stable PlayerId order and read current balances only after acquiring those locks. Freeze all inputs before calculating any participant output, so row/update enumeration cannot change opponent tiers.

**Current-rating transaction policy:** no match-start rating reservation, snapshot-version rejection or long-lived account lock is required. Overlapping matches are permitted; their reports serialize only while they update shared player rows. Whichever valid report transaction processes first uses the current balances first; the next uses the committed outputs. Preserve per-server FIFO submission, record the accepted calculation order and each player's before/after points plus policy inputs, and do not retroactively sort rating changes by played-at time. Delayed outbox reports therefore may use tiers different from those visible during play. This is an explicit modern choice consistent with the plan's CURRENT RP input, not an assertion about retail timing. It keeps gameplay independent of Backend availability. Replaying the committed input/contribution ledger reproduces ratings; replaying raw match times alone does not. Serialization/deadlock failures retry the whole transaction with fresh current balances, while an ambiguous successful commit is recovered by MatchId idempotency.

The outbox should serialize immutable reports in background, write them atomically to a durable local spool, retry with bounded backoff, and expose pending/rejected states to operators. A bounded memory queue alone is not durable: define the crash gap between result creation and persisted spool, enqueue-full handling, disk-full handling and shutdown drain. Gameplay must continue; an unpersisted report must be visibly unranked/pending, never reported as successfully submitted. Do not perform durable disk writes on the 60 Hz thread simply to hide this gap.

Implement foundation work in dependency order: stable identity and contracts; established account authentication; authenticated server identities and replay-protected tickets; participant-preserving report envelope; durable outbox; PostgreSQL ledger/idempotency and aggregate rebuild; only then the approved RatingService and license queries/UI. Do not add unused future telemetry/achievement/season tables. Backend may reference Game's small contracts but no Client, executable Server, Android, graphics or audio dependencies.

## Required approval and validation gates

Before rating implementation: approve PairwiseNormalizedV1 or an explicitly named alternative, the fixed starting roster, forfeit ordering/30-second reconnect grace, current-at-transaction balances, completion exclusions and policy-version rules. These decisions can be reviewed without blocking unrelated identity/outbox scaffolding.

After approval, required tests include all 25 matrix cells (gain and loss), tier boundaries0/39/40/139/140/389/390/749/750/850 and invalid values; pair ties; mixed signed sums; permutation invariance; saturation; eight-human/team examples; guest/bot/private exclusion; duplicate/conflicting reports; transaction rollback; concurrent current-balance updates; overlapping matches; out-of-order submissions; immediate Leave and grace timeout; same-identity reconnect before deadline; reconnect at/after deadline; completion during grace; quitters retained below finishers; equal forfeits; losing/winning-team quitters; last-team departure without whole-match cancellation; all-forfeit completion; rename continuity; expired/replayed/wrong-server tickets; outbox outage/crash/disk-full recovery; deterministic leaderboard ties; and complete aggregate reconstruction from persisted ledgers. Static ROM verification is not backend integration, live authentication, deployed PostgreSQL or public ranking evidence.
