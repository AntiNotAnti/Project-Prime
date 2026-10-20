# Android performance opt-in and GLES submission

Project Prime Android Release builds enroll in the performance-optimization
profile by default. The project property `AndroidPerfOptIn` is true unless
explicitly overridden. All shared OpenGL optimization names, including future
names passed through `OpenGlOptimizationPolicy.Enabled`, follow that enrollment
on Android only. Feature capability checks, force-legacy, and per-feature opt-out
remain authoritative. Desktop and server behavior is unchanged.

## Controls

- Disable the build-level profile: `-p:AndroidPerfOptIn=false`.
- Disable the opt-in at runtime: `PROJECT_PRIME_ANDROID_PERF_OPT_IN=0`.
- Disable one GL experiment: `PROJECT_PRIME_GL_<NAME>=0`.
- Force legacy GL: `PROJECT_PRIME_GL_FORCE_LEGACY=1`.
- Disable retained GLES scene VAO binds with the runtime opt-out above.

These are opt-ins to implementations, **not proof** of gains on every Android GPU.
Device-capability checks and validation are still required. Future optimizations
should use the shared policy only when they retain a working, independently
testable fallback. Never auto-enable a feature that is known unsafe or requires
additional permissions just because it exists.

## GLES VAO ownership

The GLES retained-list and dynamic draw paths no longer force binding VAO 0
between every call in the enrolled profile. Exact same-VAO binds are suppressed.
EndList still unbinds after configuring attributes, deleting the currently bound
VAO invalidates the tracked state, and new contexts reset the state. Every new
scene frame invalidates state before drawing because the Android UI compositor
owns independent GLES objects. The prior path is retained behind opt-out.

## Acceptance requirements

1. Run `dotnet run --project tools/android-pacing-check -c Release`.
2. Compare identical Android Release APKs with and without enrollment on Adreno
   and Mali: p50/p95/p99 frame time, CPU render time, draw/state calls, GPU time,
   allocation rate, memory and thermal state across 1v1 and eight-player matches.
3. Exercise HUD, outlines, alpha/depth, transitions, pause/results UI and
   opt-in RmlUi overlay, EGL loss/recreation and repeated match restarts.
4. A regression in GL state, texture correctness or driver compatibility blocks
   promotion even if average FPS improves. Keep the one-variable rollback.
