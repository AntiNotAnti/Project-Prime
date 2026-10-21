# Experimental world spatial-AA checks

Run on Windows with the pinned .NET SDK:

```powershell
dotnet build tools/spatial-aa-check/spatial-aa-check.csproj -c Release
dotnet tools/spatial-aa-check/bin/Release/net10.0/spatial-aa-check.dll
$env:PRIME_SPATIAL_AA_CAPTURES='C:\absolute\fresh\evidence'
dotnet tools/spatial-aa-check/bin/Release/net10.0/spatial-aa-check.dll --enabled
```

The default-off run checks that no shader/target is allocated and original RGBA
bytes survive. The enabled run creates a real native GL context and calls the
production Scene partial through asset-free dependency seams. It checks flat
encoded colors, diagonal edges, alpha preservation/discontinuity rejection,
clamped border sampling, incoming GL state restoration, a later sharp overlay,
transactional failure injection, suppressed same-size retry, changed-size
recovery, aggregate target-budget refusal, zero warmed managed allocations and owner disposal.

The cost fixture samples the production five-tap pass and exact-size copy at
1280x720 over 30 repeated synthetic diagonal-edge images. Uploads and explicit
test waits are outside the measured scopes. Its GPU query is diagnostic-only;
production spatial AA creates no query and never waits for GPU completion.
The check requires timer-query support for its cost stage. Evidence is scoped
to the recorded driver/workload; it is not gameplay, motion, sustained tail or
cross-platform acceptance. `PRIME_SPATIAL_AA_CAPTURES` saves original/filtered
PNGs and `cost.json` with renderer and binary MVID.

The production experiment uses `PROJECT_PRIME_GL_SPATIAL_AA=1`, separate from
the retired AA control. It defaults off and remains **NO_ROLLOUT**. GLSL 1.20
and GLES 3.00 sources share the same native-encoded RGB algorithm; Android
native qualification must be performed separately.
