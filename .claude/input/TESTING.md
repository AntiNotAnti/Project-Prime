# Input testing

Run the narrow input checks first, then the content-free suite and builds:

```bash
dotnet test tests/Tests/Tests.csproj -c Release \
  --filter "FullyQualifiedName~ControllerInputTests|FullyQualifiedName~ControllerP2InputTests|FullyQualifiedName~ControllerProcessingCoreTests|FullyQualifiedName~GyroConditioningTests|FullyQualifiedName~AimAssistSliceBTests|FullyQualifiedName~InputBalanceTelemetrySliceBTests"
dotnet test tests/Tests/Tests.csproj -c Release \
  --filter "RequiresGameContent!=true"
dotnet build src/Client/Client.csproj -c Release
dotnet build src/Android/Android.csproj -c Release
dotnet publish src/Android/Android.csproj -c Release
python3 tools/check-project-boundaries.py
```

The shared-input manifest validation target must report every neutral input file
exactly once for both Client and Android. Content-free tests must not attempt
AMHE1 extraction. Allocation tests measure only the engine loop; assertions run
outside the measured region.

Automated coverage includes radial/dead-zone behavior, every movement sector
boundary, reversal/reset, response and acceleration presets, mixed contributors,
aim-assist filtering/escape/current-form geometry, fixed-step telemetry, stylus
recontact/flick/buttons/hover/palm policy/scaling, gyro bias/noise/staleness/modes,
and haptic strength/queue ownership.

Opt-in firing telemetry records nearest-enemy and retained-target error plus
the fixed-step controller target error after the unassisted and assisted look
deltas. Those distributions use bounded static histograms; snapshot copies are
created only by offline diagnostics, never by the recording path.

Software success is not hardware proof. Before release, execute the matrix in
the input stabilization plan on Xbox Series, DualSense, Switch Pro, S Pen, and
available desktop pens/tablets. Record USB versus Bluetooth, device/OS version,
drift, latency, thresholds, target-switch behavior, button events, pressure,
hover, direct/indirect scaling, haptics, and launcher navigation. Mark a row
unavailable rather than converting an unrun hardware check into a pass.
