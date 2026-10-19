# Beam selector fixture compatibility

Base: `64894cd4558f5869a7968e02e9594c897d338c3e`.
The current-main `no-game-assets` job
[118370665968](https://github.com/AntiNotAnti/Project-Prime/actions/runs/39343558778/job/118370665968)
stopped in `tools/rmlui-runtime-check/Program.cs:90` with
`Out-of-range argument accepted for HunterBeamSelect`.

The native beam gallery introduced in PR-741 (`1abf6dac4`) defines both
`HunterBeamSelect` and `HunterBeamColorSelect` as indices 0 through 8 in the
production typed-intent registry. The fixture's count switch omitted these
families and used its default count of one, incorrectly expecting index 1 to
be refused. The fixture now explicitly enumerates nine choices for both
families. It round-trips every valid index through the production linked-source
registry, retains rejection of index 9, and also rejects -1. Production bounds,
command routing and gameplay are unchanged.

Validation on this base plus the fixture change:

- `dotnet run --project tools/rmlui-runtime-check -c Release -p:UseSharedCompilation=false`: PASS, including typed intent lifecycle/range contracts and 100 reinitializations.
- `python3 tools/rmlui/check-pages.py`: PASS, 148 markup/styles and all local links.
- `git diff --check`: PASS.

The runtime fixture compiles the actual production Host sources directly. No
full engine build, Hunter project-reference test, native GL or performance run
was performed for this test-only correction. Older engine binaries are not
presented as current-base verification. Hosted CI remains to be rerun.
