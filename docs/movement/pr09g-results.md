# PR-09g — fixed air-turn and jump envelope observations

**Fixture PASS; maximum-cap and competitive-balance acceptance remain open.**
This slice adds the optional `--momentum-balance-envelope-scene ASSETS FRESH_JSON`
route. Production movement, coefficients, networking, replay formats and Classic
defaults are unchanged. The existing four-scenario balance Run, remaining helpers
and CLI branch are byte-identical; their old results are not relabelled or rerun.

## Actual input and fixed schedule

On Proving Ground and Sanctorus, all seven Hunters receive paired Native/Momentum
stationary jump calibrations and mirrored air-turn/jump trials. Each calibration
has 180 ticks; each envelope arm has 480 fixed-60-Hz ticks. Both models receive
identical input: W for ticks 1–48, then A/−4 mouse pixels or D/+4 pixels each tick,
with one-tick Space pulses from tick 49 at that Hunter/map's stationary Native
landing period. Input is fixed before observing results, without landing-adaptive
scheduling, seed rerolls or body/velocity/facing/health/ability writes.

Raw mouse positions use the ordinary production pointer snapshot/aim path.
Assertions verify gathered deltas, actual facing changes, keyboard levels/edges,
living native biped state and real traversal. Native calibration Y/vertical-speed
bits and jump/ground flags match exactly. Actual Momentum accepted-jump witnesses
preserve the native Hunter impulse and Y bits; Native upward-press evidence remains
explicitly inferred. Bounded traces retain uncapped air contributions, contact
phase, input, incoming/outgoing speed and actual collision overflow/recovery.
These are headless controlled input tests, not physical-device or achieved-Hz tests.

## Windows results

One fixed fixture invocation passes **245,829 assertions and 31,920 body samples**:
28 envelope pairs and 14 stationary calibration pairs. No failed trial is replaced.

- All 13,440 Momentum envelope ticks exercise the actual solver: 1,547 grounded,
  11,536 airborne, 182 legal-jump and 175 landing witnesses.
- 6,202 ticks have nonzero uncapped air-acceleration contribution. This is a vector
  contribution and can reduce speed; it is not a claim that every tick accelerates.
- Cap engagement is **0/28 pairs**, with zero limited or incoming-over-cap steps.
  Maximum observed incoming/uncapped/outgoing solver speed is 8.887898 world units/s,
  below the unchanged 10.799561 retention cap. No schedule, cap or threshold is
  changed to obtain engagement. This does not prove optimal-strafe maximum speed.
- Both models in all 28 pairs reach radial 3/6/10-unit objectives. All arrival times,
  speed distributions, air contributions and zero censored arrivals are retained
  per Hunter/map/direction; these are observations, not competitive fairness proof.
- Actual native collision overflows and recovery fallbacks are zero in both models.
  No collision normals or generic correction causes are inferred from those counts.

[Numeric evidence](evidence/pr09-air-turn-envelope.json) includes every pair,
distributions, objectives, contact/input counters, source and binary hashes. Full
bounded samples and the exact raw JSON hash remain in the ignored evidence bundle.

## Provenance, integration and remaining work

The genuine test-source base is main
`9cd7a8f9aa1d1ec55658f4dbdde0e1ceca59f38e` with exactly two changed C# test files.
A new isolated Windows checkout builds only the tool, reusing a copied verified
normal Core with unchanged hash. The compatibility guard admits only the exact
already-verified collision source and PR-759's two fixture-only source files;
ordinary Core excludes that fixture define. Existing F/previous outputs stay
untouched. This is source-equivalence review plus a tool build/runtime lease,
not a claim that production Core was rebuilt at the new source SHA.

The first isolated build omitted an existing transitive reference assembly and
failed before any fixture ran. Its log is retained. Copying the existing reference
outputs into G permits the same-source tool build to pass; no production rebuild,
fixture reroll or assertion change occurs. Source/asset and whitespace checks pass.
Hosted CI remains pending and is not waited on.

PR-09 is incomplete. The separate CPU trace experiment has all 32 captures, but
its predeclared differential observer control fails and offline CPU audits remain
pending; no 5% performance acceptance follows. Maximum-cap/competitive calibration,
physical-device coverage and other stated source/platform limitations remain
explicit. No Mac simulation timing is collected while the user's host is busy.
