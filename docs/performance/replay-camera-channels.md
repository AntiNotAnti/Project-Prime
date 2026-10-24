# Independent camera orientation and lens channels

The opt-in `ReplayCameraChannelCurves` contract adds orientation mode/ease, FOV mode/ease, and roll mode/ease. Existing position interpolation/ease remains independent. Every field defaults to Inherit, preserving the exact legacy evaluator, including position arc-length remapping of inherited orientation/lens time. Authoring one channel does not change unselected channels.

Modes are Inherit, Linear, Smooth, Bezier, Hold. Orientation excludes scalar-only Bezier. Ease is Inherit, None, In, Out, InOut. An opted channel samples canonical fractional replay time, independent of position arc-length remapping. Inherit mode with explicit ease retains the old channel interpolation family (orientation Slerp, scalar linear or existing Bezier), with explicitly authored time easing. Position Hold dominates all channels so deliberate cuts remain exact.

## Smooth motion contract

Smooth orientation caches time-aware shared angular tangents at each key. Adjacent shortest quaternion logarithm secants use the neighboring canonical frame durations. Both adjacent intervals bound the same tangent norm to `(pi/2 - epsilon) / max(adjacent durations)`, preventing extreme time spacing from winding an endpoint control. Quaternion sign canonicalization makes q and -q equivalent, including half-turn fixtures.

For a segment duration dt and shortest delta `log(qL^-1 qR)`, ordinary SQUAD controls are `a=qL exp((vL dt-delta)/2)` and `b=qR exp((delta-vR dt)/2)`. SQUAD is used when every pair among qL, qR, a, b has positive dot product above a small safety margin. This common convex region prevents shortest-Slerp branch changes.

Near half-turns and extreme spacing, ordinary shortest SQUAD can jump even with bounded endpoint tangents. Those segments cache a spherical Bezier using the same shared endpoint velocities: `c1=qL exp(vL dt/3)`, `c2=qR exp(-vR dt/3)`. Lift-preserving spherical de Casteljau stays in one open hemisphere. With the shared tangent cap, each control lies within pi/6 of its endpoint; all controls lie within 5pi/12 of the shortest source midpoint. No antipodal interpolation plane is invented. Both cubic methods have exactly the same endpoint derivatives, so adjacent hybrid segments retain C1 angular velocity.

Smooth FOV/roll use nonuniform PCHIP (monotone cubic Hermite) derivatives, including bounded one-sided endpoint slopes. They do not overshoot a pair's scalar values. Scalar Bezier keeps the existing independent incoming/outgoing lens handles and evaluates using double intermediates. Opted FOV clamps to 1–175 degrees and roll to +/-2pi; finite but extreme handles cannot produce nonfinite opted lens output.

Smooth with inherited easing applies the existing track entrance/exit ease and leaves interior time linear, retaining C1 across smooth keys. Explicit channel easing intentionally changes speed, and may introduce an authored derivative break. Hold cuts and switches between different interpolation/easing contracts are intentional boundaries rather than a continuity promise.

All controls/tangents are cached per segment and invalidated by the track mutation revision. Crops retain the source evaluator and source time window. Preview and 30/60/120/240 FPS export use that same fractional-time evaluator. No simulation, checkpoint, protocol, source recording or source POV sample is changed.

## Formats and compatibility

Six new bytes per key are emitted only when any channel curve differs from default: sidecar v7 with a 91-byte key record, portable state v3. All-inherit tracks retain byte-compatible sidecar v6/state v2. Readers retain sidecars v1–v6 and state v1–v2 with default channel metadata. Invalid enum values and orientation Bezier fail closed even if the enclosing checksum is valid. Source/hash/range/checksum safeguards remain unchanged. Bounds derive from 1024 * 91 bytes plus the existing headers/identity allowance.

The native bridge must be rebuilt and packaged with these controls. Its replay
action registry now matches all 317 managed entries: existing indices 0–306 are
unchanged, memory Save/Undo/Redo/Discard use 307–310, and the six channel controls
use 311–316. The 40-byte packet and protocol version remain unchanged. A stale
native DLL silently ignored those appended actions; full table parity and actual
DOM activation checks now cover that integration boundary.

## Local evidence, 2026-10-22

`dotnet run --project tools/replay-camera-channel-check -c Release` passes 78,279 deterministic checks, including noncommuting near-180-degree sign flips, frame spacing [0,1,200,201], twelve seeded unequal-spacing fixtures, angular/lens C1 checks, dense hidden-branch-jump detection, monotone scalar bounds, channel isolation, explicit independent easing, Hold cuts, huge scalar handles, edit/remove/restore cache invalidation, v7/state-v3 and 1024-key/cropped round trips, checksum-valid malformed metadata rejection, and preview/export crop equality at 30/60/120/240 FPS. It also checks nonfinite time rejection before normal/window evaluation, roll C1, exact quaternion half-turns, and inherited smooth entrance/exit easing. Seven additional linked production render-probe checks reject stale actual projection matrices and nonfinite camera/view/projection data, avoiding false lens-acceptance passes. The prior source-linked 1187 camera owner checks still pass with legacy formats.

A controlled Windows SDK 10.0.401 run with `DOTNET_TieredCompilation=0`, 1024 authored keys and cached advanced controls measured p50 294.53 ns, p95 306.25 ns, p99 389.84 ns per sample over 400 batches of 128 samples, with zero measured allocations. These are synthetic CPU batch timings on a shared host, not rendered pointer latency or a device pacing claim.

The same retained HEAD-vs-working-tree legacy benchmark still produces identical checksums and zero measured allocations after the opt-in extension. The recorded latest run measured legacy Spline p95 278.91 -> 62.50 ns and legacy Bezier p95 1117.19 -> 96.88 ns. Different host load makes these runs unsuitable for comparing exact speed ratios with earlier sessions.

Real rendered tests and per-platform evidence are owned by the overall replay acceptance report; synthetic checks do not replace them.
