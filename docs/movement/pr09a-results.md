# PR-09a — advisory identity and durable diagnostic counters

This independently reviewable PR-09 slice adds model/profile identity to movement
observations and retains opt-in served-rewind evidence across native world
resets. It does not close the PR-09 release gate. Classic remains the default;
the observer has no player-state writes or packet-rejection path.

Schema 2 has 28 bounded Hunter/form/model groups. The original fourteen Native
groups and thresholds are preserved. Momentum uses broader profile-derived
horizontal envelopes; vertical limits and impulse/form exclusions stay native.
The Python consumer accepts historical schema-1 Native evidence and rejects
unknown schemas/profiles, missing or duplicate groups, and rows attached to the
wrong group. See [the diagnostic contract](advisory-validation.md).

Served shot rewind counters normally reset when the last peer leaves or the
world changes. With campaign evidence enabled, completed-world counts now
contribute exactly once to a process-lifetime report. Current-world policy is
labelled separately. Disabled evidence returns before aggregation; gameplay
rewind, health, collision, movement and wire formats are unchanged.

Focused checks pass on macOS and Windows: movement-observer lifecycle/source
time/exemption/bound tests, five actual diagnostic-reset retention assertions,
and 21 Python compatibility/negative tests. Release builds on main
`3de8887a8b04bd85db8b60ea14da9add15c1972a` plus the validation overlay passed
with zero warnings or errors. The latest main revision
`91f3a96d3240d612d4d7e97c1d198570d8197fad` has been integrated without conflicts;
its actual macOS Core/tool Release build also passes with zero warnings/errors.
The observer, retention, replay-rule, math, profile and frame-timing checks pass
on that build. Windows combat/source refresh remains a separate evidence lease.

CI now runs both C# diagnostic tests in the existing three-OS movement job.
Hosted CI was queued at publication preparation; local passes do not establish
a hosted pass. The staged source asset guard and whitespace check pass. The
remaining PR-09 tooling/evidence is a subsequent slice. Integrated performance
still fails the retained provisional budget, and unavailable device/input
acceptance remains unverified; this diagnostic PR makes no release-acceptance
claim.

The new main replay packet-comparison index check fails at frame 301 for both
accepted recordings, while gameplay replay digests still match. That separately
isolated latest-main compatibility issue remains open; passing diagnostic tests
does not establish full replay acceptance.
