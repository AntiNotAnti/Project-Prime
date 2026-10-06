# Android renderer ownership check

Run `dotnet run --project tools/android-lifecycle-check -c Release`.

This compiles the production `AndroidRenderLifetime` used by gameplay, hunter previews, in-process map previews, and preview services. It tests cancellation during a deliberately delayed load, replacement ownership waiting for scene/context cleanup, queued-owner cancellation, completion after lease disposal, idempotence, surface generation invalidation, and failed native retirement blocking later owners.

The pure check does not execute EGL, Vulkan, Android view callbacks, or native loading. Device acceptance still requires repeated cancel/restart during map loading, pause/resume, background/foreground, rotation, activity destruction/recreation, duplicate preview-service commands, and repeated preview/gameplay transitions with resource counts and validation logs recorded.
