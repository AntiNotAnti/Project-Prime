# Prime Hunters multiplayer regression catalog

This catalog is the QZ0-A deliverable from the Q-Zandronum-inspired
enhancement plan. It translates historical multiplayer failure themes into
Prime-owned invariants. It does not import Q-Zandronum code, data, captures, or
runtime binaries. It is QZ0 acceptance evidence only; later QZ workstreams
retain their own implementation and validation gates.

## Test contract

Every QZ0-A test is deterministic:

- ticks, event IDs, identities, timestamps, and random inputs are synthetic,
  fixed, or generated from one of the documented deterministic fixture seeds;
- tests use Prime codecs, histories, interpolation, feedback, routing, and
  token buckets already present in the repository;
- no extracted game data, client process, database, WAN, or foreign binary is
  required;
- an invariant comment is kept beside each test and this catalog entry maps the
  test to the same invariant.

The xUnit regression locations are:

```text
tests/Tests/Regression/MultiplayerHistory/
tests/Server.Node.Tests/Regression/
```

`tools/nettest/Regression/` remains available for network-condition and UDP
probes. The Node rejoin regression already exercises the local Worker process
boundary from `tests/Server.Node.Tests/Regression/`.

## Long-running clocks

| ID | External inspiration | Prime equivalent risk | Test type | Expected Prime invariant | Status / test |
|---|---|---|---|---|---|
| QZ0-CLOCK-001 | Server tick rollover | `NetClock` treats a wrapped server tick as stale | deterministic unit | A valid wrapped observation advances one continuous estimate | Implemented — `NetClockAcceptsTickWrapWithoutMovingTheEstimatedTimelineBackward` |
| QZ0-CLOCK-002 | Delayed ping after rollover | Old reply reopens a completed time epoch | deterministic unit | Stale wrapped replies are rejected | Implemented — `NetClockRejectsAStaleReplyAfterTheEpochHasAdvanced` |
| QZ0-CLOCK-003 | Snapshot interpolation at uptime boundary | Remote pose jumps when wire tick wraps | deterministic unit | Wrapped snapshots remain one interpolation timeline | Implemented — `SnapshotInterpolationKeepsWrappedSnapshotsInOneTimeline` |
| QZ0-CLOCK-004 | Late snapshot after rollover | Prior epoch overwrites newest snapshot | deterministic unit | Older wrapped packets cannot replace history | Implemented — `SnapshotInterpolationRejectsAnOldPacketAfterTickWrap` |
| QZ0-CLOCK-005 | ACK bitmask rollover | Reliable loss accounting misclassifies packets near zero | deterministic unit | ACK membership remains correct across wrap | Implemented — `ReceiveWindowAcknowledgementsRemainCorrectAcrossWrap` |
| QZ0-CLOCK-006 | Reliable sequence rollover | Retransmit is delivered twice after event ID wraps | deterministic unit | Wrapped reliable event IDs are delivered exactly once | Implemented — `ReliableEventIdsCrossWrapAndDeliverExactlyOnce` |
| QZ0-CLOCK-007 | Multi-day worker uptime | Large scheduler gap causes an unbounded catch-up burst | deterministic unit | Fixed 60 Hz catch-up is bounded and dropped work is explicit | Implemented — `FixedTickSchedulerBoundsSyntheticMultiDayCatchup` |
| QZ0-CLOCK-008 | Historical shot near tick rollover | View hint requests future/unbounded rewind | deterministic unit | Lag compensation stays within the server rewind budget | Implemented — `LagCompensationWrapKeepsRewindWithinTheServerBudget` |
| QZ0-CLOCK-009 | Join during server-tick rollover | A valid admission starts in a false future epoch or disconnects at wrap | deterministic packet/clock unit | Join acceptance, clock sync, and the first wrapped snapshots remain one timeline | Implemented — `JoinDuringTickWrapProducesOneContinuousClockAndSnapshotTimeline` |

## Moving geometry and latency presentation

QZ0-A records the compatibility invariants that motivated moving-geometry
history. The separately gated QZ1 workstream owns the production history and
query implementation.

| ID | External inspiration | Prime equivalent risk | Test type | Expected Prime invariant | Status / test |
|---|---|---|---|---|---|
| QZ0-GEOM-001 | Player riding a moving platform under latency | Smoothing changes connection/life ownership | deterministic unit | Platform-carried motion interpolates only within one player life | Implemented — `APlayerCarriedByMovingGeometryInterpolatesWithinItsOwnLife` |
| QZ0-GEOM-002 | Platform stops during client prediction | Prediction runs past an authoritative stop | deterministic unit | Extrapolation is capped at the configured bound | Implemented — `APlatformStoppingDuringPredictionCannotExtrapolatePastTheBound` |
| QZ0-GEOM-003 | Historical player hitbox rewind | Compensation mutates the live collider | deterministic unit | Historical player queries do not move live state | Implemented — `HistoricalPlayerTraceDoesNotMoveTheLiveCollider` |
| QZ0-GEOM-004 | Door opens/closes during a shot | Current door state replaces query-tick state | deterministic collision unit | Door collision is selected from the historical query state | Implemented — `DoorOpenCloseTransitionUsesTheHistoricalPlaneAtTheQueryTick` |
| QZ0-GEOM-005 | Force field toggles during a shot | Current active flag contradicts historical trace | deterministic collision unit | Historical active/inactive state controls the trace | Implemented — `ForceFieldToggleUsesHistoricalActiveState` |
| QZ0-GEOM-006 | Several moving surfaces in one trace | Collision ordering differs after rewind | deterministic collision unit | Nearest valid historical surface wins | Implemented — `HistoricalNearestDynamicSurfaceWinsTheCollisionOrdering` |
| QZ0-GEOM-007 | Two matches reuse dynamic entity IDs | One match reads another match's history | deterministic ownership unit | History storage is isolated per match owner | Implemented — `DynamicHistoryStorageCannotLeakBetweenMatchOwners` |

## Slot and life reuse

| ID | External inspiration | Prime equivalent risk | Test type | Expected Prime invariant | Status / test |
|---|---|---|---|---|---|
| QZ0-ID-001 | Player leaves and slot is reused | Old connection reads the new slot's history | deterministic history unit | Connection identity fences historical lookup | Implemented — `LagCompensationHistoryRejectsAReusedSlotWithAnOldConnection` |
| QZ0-ID-002 | Player dies and life is reused | Old projectile or assist is attributed to a new life | deterministic history unit | Life identity fences historical lookup | Implemented — `LagCompensationHistoryRejectsAReusedLifeWithTheSameConnection` |
| QZ0-ID-003 | Snapshot arrives across occupant replacement | Previous pose blends into a new occupant | deterministic interpolation unit | Slot replacement is a discrete transition | Implemented — `SnapshotInterpolationDoesNotBlendAReusedSlotAcrossLives` |
| QZ0-ID-004 | Late damage after respawn | Old damage changes current recap/health state | deterministic feedback unit | Old-life damage is not applied to the replacement life | Implemented — `CombatFeedbackDoesNotApplyDamageFromAFormerLocalLife` |
| QZ0-ID-005 | Reliable world event arrives after match handoff | Match A event changes Match B presentation | deterministic feedback unit | Match binding rejects foreign world events | Implemented — `WorldFeedbackRejectsAnOldMatchEventAfterMatchBindingChanges` |
| QZ0-ID-006 | Phase/intermission event arrives late | Old phase event survives a lifecycle boundary | deterministic feedback unit | Phase revision fences old events | Implemented — `WorldFeedbackRejectsAnOldPhaseEventAfterThePhaseBoundary` |
| QZ0-ID-007 | Prepared frame survives a reset | Old rendered picture is presented in a reused match | deterministic presentation unit | Reset generation invalidates prepared frames | Implemented — `PreparedSnapshotPresentationIsInvalidAfterAReusableMatchReset` |
| QZ0-ID-008 | Old ready/reconnect packet | Loading state accepts a previous match epoch | deterministic connection unit | Readiness is scoped to the current match ID | Implemented — `NetConnectionRejectsAnOldReadyAfterMatchTransition` |
| QZ0-ID-009 | Old reliable combat/world queue | Application events cross a match transition | deterministic reliable unit | Match transition cancels application events but preserves admission welcome | Implemented — `MatchTransitionCancelsQueuedApplicationEventsButKeepsWelcome` |
| QZ0-ID-010 | Cross-match UDP routing | Missing route falls through to an arbitrary match | deterministic routing unit | Routed joins require and preserve an explicit `WireMatchId` | Implemented — `RoutedJoinRequiresAnExplicitWireMatchIdentity` |
| QZ0-ID-011 | Old projectile survives its owner's respawn | Prior-life projectile is attributed to the replacement life | deterministic authoritative combat unit | The projectile retains its original full actor identity and a same-connection self-hit is a validated suicide, never replacement credit | Implemented — `CombatAttributionTests.OwnProjectileFromAnEarlierLifeIsAValidatedSuicideWithoutCountersOrAssists` and `MatchAwardsTests.EarlierLifeSelfProjectileRetainsIdentityAndIsStillSuicide` |
| QZ0-ID-012 | Old assist contribution survives slot/life reuse | Replacement life inherits historical assist damage | deterministic contribution-ledger unit | Reuse evicts prior full-actor contribution; a replacement cannot inherit its threshold | Implemented — `OldAssistContributionCannotTransferToAReplacementLife` and authoritative integration `AssistResultTests.ResolvedDamageEmitsKillAndRejectsAnAssistFromAnEarlierLife` |

## Match boundary and admission tickets

| ID | External inspiration | Prime equivalent risk | Test type | Expected Prime invariant | Status / test |
|---|---|---|---|---|---|
| QZ0-BOUNDARY-001 | Reconnect ticket replay / stale match route | A ticket is reused or moved to another worker match | deterministic admission unit | Admission tickets are one-time and bound to `WireMatchId` | Implemented — `WorkerAdmissionTicketCannotReplayOrCrossWireMatchBoundaries` |
| QZ0-BOUNDARY-002 | Old replay event after match rotation | Match A replay fact mutates Match B playback | deterministic replay-decoder unit | Match transition clears queued facts and rejects a late event carrying the prior match ID | Implemented — `OldReplayEventFromMatchACannotEnterMatchB` |

## Objective edges and semantic presentation

Carrier death/reset, score/timer boundaries, and objective transitions are
represented as typed server facts. The existing authoritative match-flow tests
continue to own full content-backed carrier and timer simulation; QZ0-A adds the
content-free wire and exactly-once regression layer here.

| ID | External inspiration | Prime equivalent risk | Test type | Expected Prime invariant | Status / test |
|---|---|---|---|---|---|
| QZ0-OBJECTIVE-001 | Carrier/score transition retransmit | Flag capture cue plays more than once | deterministic feedback unit | Duplicate semantic event ID produces no duplicate cue | Implemented — `FlagCaptureIsPresentedExactlyOnceWhenTheReliableEventRetransmits` |
| QZ0-OBJECTIVE-002 | Pickup/respawn event around sequence wrap | Wrapped event is lost or replayed | deterministic feedback unit | A newer wrapped ID is accepted once | Implemented — `PickupRespawnAcrossEventIdWrapRemainsExactlyOnce` |
| QZ0-OBJECTIVE-003 | Ordinary pickup respawn | Every pickup becomes a high-priority objective cue | deterministic feedback unit | Only configured major pickups announce | Implemented — `OrdinaryPickupRespawnDoesNotBecomeAHighPriorityObjectiveCue` |
| QZ0-OBJECTIVE-004 | Node capture with malformed team/subject | Client-like objective claim reaches presentation | deterministic validation unit | Node capture requires valid team and node shape | Implemented — `NodeCaptureRequiresAnAuthoritativeTeamAndNodeShape` |
| QZ0-OBJECTIVE-005 | Regulation expiry / overtime boundary | Overtime value is ambiguous or out of range | deterministic validation unit | Overtime and match point use bounded typed values | Implemented — `OvertimeAndMatchPointUseTypedObjectiveBoundaries` |
| QZ0-OBJECTIVE-006 | Score limit at timer expiration | Simultaneous regulation expiry invents overtime or replaces the score end reason | deterministic authoritative match-flow unit | `ScoreGoal` wins the same-tick collision and the match remains out of overtime | Implemented — `ScoreLimitWinsDeterministicallyWhenTheTimerExpiresOnTheSameTick`; event-order companion `ObjectiveFeedbackPublishesOvertimeThenMatchPointInServerOrder` |
| QZ0-OBJECTIVE-007 | Flag reset near long uptime | Wire round trip changes objective identity | deterministic codec unit | Event ID, tick, match, phase, and objective fields round-trip exactly | Implemented — `WorldEventRoundTripPreservesObjectiveIdentityAcrossTickWrap` |
| QZ0-OBJECTIVE-008 | Foreign/unknown semantic event | Unsupported transition is treated as a known cue | deterministic validation unit | Unknown semantic kinds fail closed | Implemented — `UnsupportedSemanticTransitionCannotReachPresentation` |
| QZ0-OBJECTIVE-009 | Reordered objective packets | Safe reordering is mistaken for duplication | deterministic feedback unit | Bounded reordering is accepted, duplicate IDs are not | Implemented — `WorldFeedbackAcceptsReorderedObjectiveFactsButNeverDuplicatesAnId` |
| QZ0-OBJECTIVE-010 | Carrier suicide | Carrier status accidentally turns self-destruction into an Interceptor or streak award | deterministic semantic-event unit | Suicide remains non-competitive even when the authoritative pre-mutation fact marks the victim as a carrier | Implemented — `CarrierSuicideCannotCreateAnInterceptorOrSelfAward` |
| QZ0-OBJECTIVE-011 | Objective reset on the score tick | One same-tick transition erases or duplicates the other | deterministic feedback unit | Reset and score-boundary facts are each accepted exactly once in server order | Implemented — `ObjectiveResetOnScoreTickPreservesBothTransitionsExactlyOnce` |
| QZ0-OBJECTIVE-012 | Participation changes on an objective boundary | Slot replacement inherits the prior occupant's capture or played interval | deterministic authoritative ledger unit | The original span closes and replacement span opens at the same tick with independent objective metrics | Implemented — `MatchParticipantLedgerTests.ParticipationChangeOnObjectiveBoundaryKeepsCaptureWithOriginalIdentity` |

## Presentation synchronization

| ID | External inspiration | Prime equivalent risk | Test type | Expected Prime invariant | Status / test |
|---|---|---|---|---|---|
| QZ0-PRESENT-001 | Door close/open transition | A steady simulation frame re-emits the presentation signal for an already-applied door edge | deterministic authoritative entity/presentation unit | `DoorEntity.Process` emits one matching audio/presentation request per open or close state edge and none on the following steady frame | Implemented — `DoorCloseOpenEdgesEmitOnePresentationSignalWithoutSteadyFrameDuplicates` |

Pickup-respawn and objective-transition synchronization are mapped above as
`QZ0-OBJECTIVE-002` and `QZ0-OBJECTIVE-001`; they are not duplicated as extra
catalog cases.

## Flood and malformed-input abuse

| ID | External inspiration | Prime equivalent risk | Test type | Expected Prime invariant | Status / test |
|---|---|---|---|---|---|
| QZ0-ABUSE-001 | Status/discovery flood | Discovery consumes unbounded control work | deterministic limiter unit | Status bucket has a fixed burst and refill | Implemented — `StatusFloodStopsAtTheDeterministicTokenBucketCapacity` |
| QZ0-ABUSE-002 | Join flood | Admission attempts overwhelm the Node/worker boundary | deterministic limiter unit | Join bucket has a fixed admission burst | Implemented — `JoinFloodCannotExceedTheBoundedAdmissionBurst` |
| QZ0-ABUSE-003 | Chat flood / clock rollback | Chat queue grows or refill rolls back | deterministic limiter unit | Chat bucket is monotonic and rate-bound | Implemented — `ChatFloodHonorsMonotonicTimeAndRefillsOnlyAtTheConfiguredRate` |
| QZ0-ABUSE-004 | Duplicate JSON envelope fields | Parser chooses an attacker-controlled duplicate value | deterministic codec unit | Duplicate envelope fields fail before mutation | Implemented — `DuplicateControlEnvelopeFieldsFailClosedBeforeCommandMutation` |
| QZ0-ABUSE-005 | Duplicate nested JSON fields | Nested DTO has two contradictory values | deterministic codec unit | Duplicate nested fields fail closed | Implemented — `DuplicateNestedControlFieldsFailClosed` |
| QZ0-ABUSE-006 | Invalid enum input | Unknown mode/visibility changes admission policy | deterministic codec unit | Invalid enums are rejected | Implemented — `InvalidControlEnumFailsClosed` |
| QZ0-ABUSE-007 | Oversize control frame | Frame size amplifies parser memory/work | deterministic codec unit | Control frame maximum is enforced before parsing | Implemented — `OversizeControlFrameIsRejectedBeforeParsing` |
| QZ0-ABUSE-008 | Unknown control fields | Silent schema drift changes server behavior | deterministic codec unit | Strict DTO schema rejects unknown fields | Implemented — `UnknownControlFieldsAreRejectedByTheStrictSchema` |
| QZ0-ABUSE-009 | Lobby command/revision flood | Replayed command partially mutates lobby state | deterministic lobby unit | Revision check is atomic and stale commands do not mutate | Implemented — `ReplayingAStaleLobbyRevisionCannotPartiallyMutateTheLobby` |
| QZ0-ABUSE-010 | Worker control stream duplicate | IPC parser resynchronizes after malformed JSON | deterministic IPC codec unit | Duplicate fields fail the frame; no resynchronization | Implemented — `WorkerIpcDuplicateFieldsAreRejectedWithoutResynchronizingTheStream` |
| QZ0-ABUSE-011 | Worker IPC oversize frame | Local control channel allocates beyond its bound | deterministic IPC codec unit | IPC length bound is enforced before payload allocation | Implemented — `WorkerIpcOversizeLengthIsRejectedBeforePayloadAllocation` |
| QZ0-ABUSE-012 | Match rejoin flood | Repeated retries mint credentials or enqueue unbounded coordinator work | deterministic Node/Worker fixture | Only the first retry returns a fresh handoff; the rest fail with the bounded rejoin rate limit while placement remains intact | Implemented — `RejoinFloodReturnsOneFreshHandoffAndRateLimitsTheRest` |

## Reusable deterministic fixtures

The fixtures are test-only types; no fixture or foreign-engine abstraction is
referenced by a production project.

| Fixture | Seed | Reused boundary |
|---|---:|---|
| `WraparoundClockFixture` | `0x514A0001` | join, clock, and snapshot values around `uint` rollover |
| `SlotReuseFixture` | `0x514A0002` | original, replacement-life, replacement-connection, victim, and killer identities |
| `MovingGeometryFixture` | `0x514A0003` | fixed traces, door/force-field planes, and stable entity IDs |
| `MatchBoundaryFixture` | `0x514A0004` | Match A/B epochs, phase revisions, replay records, and one exact boundary tick |
| `FloodFixture` | `0x514A0005` | bounded attempts, monotonic synthetic time, and deterministic Node identities |

## Validation status

QZ0-A now carries 55 tests tagged `Regression=QZ0`: 43 in the main test project
(42 in the dedicated regression directory plus the participant-ledger boundary)
and 12 in the Node/control-plane regression directory. The catalog maps 55
Prime invariants because the old-projectile and door-pose rows also reuse
stronger permanent tests in their owning suites rather than duplicating them.
The implementation remains uncommitted by design. Full solution validation is
still a separate gate from this catalog; in particular, unrelated dirty
worktree changes must compile before the focused filters can execute.
