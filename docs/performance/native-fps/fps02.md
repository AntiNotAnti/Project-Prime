# FPS-02: source-owned Display pacing evidence

The original ApplyFrameRateSettings reset its ignored-VSync latch after changing
cap/monitor, then immediately tested the old FrameTiming.MeasuredFrameHz.
Unlimited -> Display on macOS/Linux could therefore disable VSync and install a
software display limiter before measuring a single frame under the new request.
This is a pacing-source correctness defect, not evidence of a faster renderer.

DesktopPacingMeasurement now requires two uninterrupted seconds from the current
source. Cap/refresh changes, moves (including same-Hz monitors), focus events,
presentation-generation resets, invalid intervals and >250 ms stalls clear the
measurement. The transition interval itself is excluded. Simulation diagnostics,
60 Hz accumulation, input ownership, network/replay ticks and render poses are
unchanged. Display still requests VSync; explicit caps still request OpenTK
software pacing with VSync off; Unlimited installs neither. There is no alternate
scheduler or experimental optimization to enable.

Validation: all 53 asset-free desktop-pacing assertions pass, including 14 new
transition/reset/focus/stall cases. Full Release compilation, fixed-step and
backend policy checks pass. Physical cross-device cap/latency qualification is
separate; the user-requested Windows RTX 5080 campaign records its own exact
source/binary receipt and does not establish Mac/Linux swap-interval behavior.
The previous numeric-240 cap policy was already fixed, so it was not rewritten.

Rollback: revert this PR to restore the previous fallback evidence reader.
No configuration schema changes or feature switches were added.
