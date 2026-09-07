# G3.1 overtime framework

Overtime is opt-in: `OvertimePolicy.Disabled` is the constructor and preset default. Dedicated servers select the mode-aware policy with `-overtime mode`; `-overtime disabled` is explicit legacy behavior. The setting survives rotation through AuthoritativeServer's rule construction. Invalid or missing option values fail parsing.

## State and ordering

MatchPhase stays Playing throughout overtime, so simulation and network input continue. MatchRuntime separately exposes MatchPeriod (Regulation, Overtime, SuddenDeath) and PeriodStartTick. Entering overtime removes the regulation deadline and uses the existing unlimited MatchTime sentinel -1. The lifecycle does not reapply the expired regulation deadline, and entering overtime does not advance PhaseRevision or invalidate input bundles. A normal ending still follows the existing Ending/Intermission lifecycle.

MatchFlow evaluates overtime immediately after its existing MatchLogic.ProcessMode call. World processing, score aggregation and result capture remain in their existing order. Primary score/time ties use current aggregated data; secondary kills/deaths do not break a primary tie for the score/time modes. During opt-in regulation expiry and active overtime, point-goal clamping is bypassed so it cannot fabricate a leader by clamping only the first tied team. Regulation point-goal endings before expiry retain their existing behavior.

Forced completion, explicit completion messages, invalid-team endings and Survival elimination remain terminal. The explicit reason captured before ProcessMode cannot be overridden by overtime. Objective-time-goal ties can enter overtime even before a regulation time limit expires.

PeriodStartTick uses the server's active combat tick when available and the scene frame count for local simulation. Countdown and competitive reset return the period to Regulation. No new wall-clock timer or duration limit is introduced.

## Mode behavior

| Mode family | Entry at regulation expiry | Completion while extended |
| --- | --- | --- |
| Battle / TeamBattle | Equal leading points → SuddenDeath | First completed score lead |
| Capture | Equal leading points, or any flag away from base/carried → Overtime | A score lead once all live flag play resolves |
| Bounty / TeamBounty | Equal leading points → Overtime | First objective score lead |
| Nodes / TeamNodes | Equal leading points or contested node → Overtime | A score lead after contested control resolves |
| Defender / TeamDefender | Equal leading objective time or contested node → Overtime | An objective-time lead after contest resolves |
| PrimeHunter | Equal leading holder time → Overtime | First objective-time lead |
| Survival / TeamSurvival | Equal leading remaining-life standing → SuddenDeath | Existing shared-life/elimination rules reach a sole eligible competitor |

Survival grants/removes no lives. A temporary lives lead during sudden death does not terminate it while multiple competitors remain eligible to live/respawn. Capture includes a dropped flag until it returns/reset-to-base, preserving live objective play across the boundary. A primary tie with no scoring activity can continue indefinitely; this is the explicit unlimited policy rather than an implicit timeout.

## Rule additions shared with G3 late join

The separate LateJoinPolicy enum has JoinImmediately, SpectateUntilNextMatch and Disabled. The MatchRules constructor defaults to JoinImmediately for compatibility with manually built rules and frozen legacy adapters. CreateDefault selects SpectateUntilNextMatch for Survival/TeamSurvival and JoinImmediately otherwise. Server admission/late-join behavior is owned by the separate late-join implementation; the enum alone does not claim that behavior is complete.

## Replication and verification

The protocol owner is implementing Rules byte 69 for overtime, byte 70 for late join, and Lifecycle D/E for period/start tick. Match's other state record does not duplicate period ownership. Protocol-7 adapters remain frozen. Completion requires these codec/replica checks as well as the real-content mode tests.

Focused OvertimePolicyTests exercise all twelve mode tie/non-tie boundaries, secondary-stat independence, explicit ends, disabled defaults, a Battle score lead, Survival continued lives, lifecycle deadline behavior and strict CLI/rule validation. Real-content objective activity and full-flow tests are being added separately; focused validation initially passed **28/28**; the added score-goal/expiry and authoritative-Playing cases bring that set to 30. Combined existing match baseline, lifecycle, result and overtime tests passed **73/73**, with no failures or skips. Logs: `/tmp/codex-re-prime-g1/overtime-tests.log` and `/tmp/codex-re-prime-g1/overtime-baseline-tests.log`. This is focused source/unit evidence; real-content and replication gates remain separate.

A full built-snapshot run completed with 611 passed and one failure: the protocol test still treated newly assigned Rules bytes 69/70 as reserved. The protocol owner is updating that expectation and the new field tests. All prior stale overtime fixture failures were absent. Full log: `/tmp/codex-re-prime-g1/overtime-main-tests.log`.

## Final integration update

The final assembled main suite passes 954/954, including the updated policy wire
fixtures. The real AMHE1 `--overtime-check` harness passes all twelve modes, tied
and untied expiry, objective contest, Survival lives, and forced terminal-once
checks. Earlier checkpoint failures above remain historical evidence. See
[G1_G5_VALIDATION.md](G1_G5_VALIDATION.md) for remaining acceptance boundaries.
