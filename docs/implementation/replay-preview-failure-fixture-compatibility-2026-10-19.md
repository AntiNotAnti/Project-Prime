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

Pending, on an available isolated worker:

```
dotnet build tools/rmlui-opengl-check/rmlui-opengl-check.csproj -c Release
```

This must build its project reference; a previously built normal engine lacks
the deliberately fixture-only API. Then run the existing RML97 Studio failure
and retry acceptance through the hosted software-frame job. No macOS full build,
UI run or performance test was performed for preparation.
