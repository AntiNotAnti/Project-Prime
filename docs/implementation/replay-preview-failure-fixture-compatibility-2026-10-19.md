# Replay preview failure fixture compatibility

Base: `b05e74d41` (merged PR-758). Hosted job
[118378649678](https://github.com/AntiNotAnti/Project-Prime/actions/runs/39346005637/job/118378649678)
failed compilation with CS0117 at the three OpenGL fixture references to
`Scene.NativeReplayMultiViewFailureForTests`. No software-frame acceptance
executed. The references introduced with PR-740 had no corresponding Scene hook.

The explicit OpenGL fixture now passes
`MphReadReplayPreviewFailureFixture=true` to its engine project reference.
Inside the existing RmlUi-enabled property group this adds a dedicated fixture
constant. The nullable internal hook and invocation compile only with both UI
and fixture constants. Ordinary desktop builds (which already enable RmlUi by
default) contain neither the hook nor the additional draw branch.

Injection occurs after the actual tile's `OnDrawFrame` and before
`OnRenderFrame`, inside the existing exception wrapper and auxiliary render
scope. The original injected-third-tile, playback-survival and successful-retry
assertions remain intact. No simulation or replay format changes are made.

Cheap checks passed: evaluated normal defines exclude the fixture constant;
explicit fixture defines include it; source ordering places injection inside
the actual render scope; page validation passes 148 markup/styles; diff
whitespace passes. These are not compilation or GL execution evidence.

Actual Windows x64 verification used a fresh detached checkout of source
`5ef4b31715588e2dcd1e0e0d6800fecc5b3390b1`, with its own Core/tool outputs:

- Full fixture and actual engine project-reference Release build: PASS, zero warnings/errors, 48.14 seconds.
- `--replay-files`: PASS.
- `--studio-owner`: PASS, 686 checks, with a fresh short user-data root.

The initial `--studio-owner` attempt under the deeper checkout-local evidence
path failed with an IOException during the existing symbolic-link redirection
case. That failure is retained. The same unchanged binaries and assertions
passed with the shorter fresh root; no fixture source/path policy was changed.
This comparison establishes a user-data path/environment limitation, without
claiming its exact Windows API cause. Both attempts, source identity and binary
hashes remain in ignored `artifacts/replay-preview-fixture-compile/` evidence.
Existing benchmark/normal engine outputs were not overwritten. All owned
Windows processes exited and the exclusive lease was released afterward.

No actual GL window, input event, macOS full build or performance test ran.
Hosted software-frame acceptance and real replay-window injected-failure/retry
acceptance remain pending. A future fixture build must include its project
reference; an ordinary engine intentionally lacks this fixture-only API.
