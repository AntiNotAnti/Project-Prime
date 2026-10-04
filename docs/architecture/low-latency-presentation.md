# Low-latency presentation architecture

Project Prime keeps low-latency integration on the presentation side of the
fixed-step boundary. Gameplay simulation, networking, hit resolution, replay
recording and authoritative state remain fixed at 60 Hz and do not depend on a
vendor latency API.

## Shared frame vocabulary

`LowLatencyController` exposes one backend-neutral frame ID and ordered marker
set:

1. InputSample
2. SimulationStart
3. SimulationEnd
4. RenderSubmitStart
5. RenderSubmitEnd
6. PresentStart
7. PresentEnd

The renderer also exposes one `WaitForFrame` hook before gameplay frame work.
Today the installed provider is `NullLowLatencyProvider`, so this path is a
no-op and changes no pacing or gameplay behavior.

A future provider, such as NVIDIA Reflex on a supported DX12 or Vulkan backend,
implements only `ILowLatencyProvider`. Gameplay code must not import a vendor
SDK or branch on a GPU vendor.

## Provider rules

A provider may:

- receive ordered frame markers;
- perform a backend/vendor latency sleep through `WaitForFrame`;
- expose whether it is supported;
- map Enabled/Boost onto the backend's supported low-latency modes.

A provider may not:

- advance or delay the fixed 60 Hz simulation clock;
- alter input values, aim assist, hit registration or network packet timing;
- create a second software frame-rate limiter;
- silently force a renderer/backend switch;
- turn an unsupported request into anything other than Disabled.

When a real wait provider is enabled, its activation policy must coordinate with
the existing frame-pacing owner. OpenTK software caps, Android deadlines,
blocking FIFO presentation and a vendor sleep must never stack as independent
cadences. Exactly one mechanism owns deliberate CPU-side waiting for a frame.

## Failure behavior

Unsupported providers degrade to Disabled while retaining the requested mode for
diagnostics. Out-of-order or stale markers are dropped and counted instead of
being forwarded to the provider. Starting a new frame before the previous
PresentEnd records an incomplete-frame diagnostic. Provider failure must be
handled as an optimization failure, not a match-lifecycle failure.

## Validation

`-renderbackendcheck` verifies the marker order, wait hook, unsupported-provider
fallback and stale/out-of-order containment without requiring a GPU.

Native `-renderfullcheck` remains responsible for backend presentation,
surface lifetime and device-recovery validation. A future Reflex implementation
must add native provider acceptance on supported NVIDIA hardware before exposing
a player-facing setting.
