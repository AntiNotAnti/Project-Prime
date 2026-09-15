# Project Prime AMHE1 intentional deviations

## FID-NET-001 — Modern server authority and transport

- AMHE1 outcomes remain the gameplay reference; its internal ownership is not ported.
- Project Prime keeps a fixed 60 Hz authoritative Worker, isolated `MatchInstance`
  single writers, reliable Node control, and direct Worker UDP gameplay.
- Rationale: deterministic authority, security, isolation, replay, and observers.
- Rulesets: Classic and Competitive.
- Approval: retained by the Project Prime architecture invariant and accepted as a
  terminal deviation under the user's 2026-09-09 blocker waiver.
- Coverage: multi-instance, adversarial UDP, late join, observer, replay, and F8
  integration suites.

## FID-RULESET-001 — Competitive remains separate

- AMHE1 evidence changes Classic only.
- Competitive inherits a correction only through an explicit reviewed decision.
- Rationale: retail fidelity must not silently redefine an explicitly separate
  Project Prime balance surface.
- Approval: retained by the implementation plan and accepted under the user's
  2026-09-09 blocker waiver.
- Coverage: the fixture schema records rulesets; each future corrected case must
  assert both Classic and Competitive outcomes.

## FID-CONTENT-001 — Multiplayer extensions

- Custom maps and supported player-count extensions remain valid Project Prime
  features, but cannot claim AMHE1-specific map fidelity.
- Rationale: these are deliberate Project Prime extensions outside the retail
  reference's geometry and capacity surface.
- Approval: accepted under the user's 2026-09-09 blocker waiver.
- Coverage: shared rules, package validation, capacity bounds, and isolation tests.

## FID-REPLAY-001 — Versioned authoritative replay

- AMHE1 gameplay outcomes remain the reference; its recording implementation and
  file format are not ported.
- Project Prime records versioned authoritative facts and supports delayed observers,
  playback, and indexed seek.
- Rationale: deterministic diagnostics, compatibility, observer support, and safe
  evolution of the modern server-authoritative protocol.
- Rulesets: Classic and Competitive.
- Approval: retained by the Project Prime architecture and accepted under the user's
  2026-09-09 blocker waiver.
- Coverage: replay encode/decode, legacy migration, playback, seek, observer, and
  feedback-deduplication suites.

## FID-BOT-001 — Server-owned bots

- AMHE1 outcomes remain the reference for shared gameplay rules; Project Prime bots
  are a server feature and do not reproduce a retail local-AI ownership model.
- Bots produce inputs into the same authoritative `MatchInstance` path as players.
- Rationale: a single gameplay authority prevents bot-only state and cross-match
  mutation while supporting Practice and hosted/dedicated sessions.
- Rulesets: Classic and Competitive.
- Approval: retained by the Project Prime architecture and accepted under the user's
  2026-09-09 blocker waiver.
- Coverage: bot admission/retirement, mixed teams, objective contention, isolation,
  replay, and reporting suites.

## FID-HUNTER-001 — Guardian/Psycho Bit Project Prime extension

- Guardian is a normal playable Hunter in Project Prime at enum value 7, with
  `Hunter.Random` retained as selector sentinel 8.
- Psycho Bit uses the supplied model/effect inventory and a conservative
  grounded/hover-styled alternate form bounded by Guardian's authored collision
  volume and `PlayerValues`. Its charge/release beam uses the normal authoritative
  projectile, lag-compensation, and damage-attribution path.
- Rationale: the supplied AMHE1 assets identify Psycho Bit resources, but exact
  retail enemy semantics and player animation meaning are not available. This is
  an explicit Project Prime extension, not retail fidelity.
- Coverage: focused catalog, asset/model, morph, attack, collision, replay,
  bot, cosmetic, selector, and Backend tests.

No accessibility or presentation mismatch is pre-approved. The waiver closes missing
evidence as `BlockedByEvidence`; it does not approve a silent gameplay or presentation
mismatch. Add one only after its case records both behaviors, rationale, approval,
and regression coverage.
