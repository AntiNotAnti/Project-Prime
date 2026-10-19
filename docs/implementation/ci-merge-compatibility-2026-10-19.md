# Community revision and campaign ending CI compatibility

The main build's Community revision fixture compiled a selected list of portable
controller source files. Discovery now validates installation selections through
`NativeCommunityInstallController`, so that list no longer compiled the current
production dependency graph. The fixture now references the production project,
matching the other Community acceptance harness. A scoped acceptance-assembly
friend allows its synthetic authority to inspect
the existing internal account leases and preflight policy without duplicating
controller types. No installer or account is created by these tests. This changes
test visibility metadata only.

The campaign ending GL fixture retained the former credits-sequence expectation.
The native results page now enters `CampaignAudioPage.EndingResults`, whose
cartridge sequence is `SeqId.RESULTS`. The fixture checks that mapping and the
exact sequence while retaining its single start/cue, positive pumping and
ownership/disposal assertions. It continues to use an injected silent sink.

Both changes affect test harnesses only. They do not modify production movement,
Community installation behavior, campaign saves, or audio routing.

## Verification

- Community revision harness: 183 passing synthetic-authority assertions.
- Current production and OpenGL fixture build: zero warnings and errors.
- Campaign ending native GL acceptance: all lifecycle/semantic checks pass at
  actual framebuffers 640×360, 1280×720 and 1920×1080, matching the CI dimensions.
  macOS Retina doubles requested client dimensions, so the 640 and 1920 runs
  requested 320×180 and 960×540 respectively. An additional 2560×1440 run passes.
- An exploratory actual 3840×2160 run fails the existing slot/save semantic-label
  assertion before reaching the repaired audio assertion. No 4K acceptance is
  claimed by this fixture repair.

Logs remain in `artifacts/ci-merge-compatibility/` in the retained worktree.

After rebasing onto main `24fb2bcf5` (including the authoritative movement rules
and beam cosmetic changes), the Community harness again passes all 183 checks,
the GL fixture builds with zero warnings/errors, and ending GL acceptance passes
again at actual 1280×720. No controller or ending source conflict occurred.
