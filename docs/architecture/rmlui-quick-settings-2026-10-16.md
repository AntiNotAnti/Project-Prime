# RML-10: native transactional Quick Settings

This slice adds a focused native RmlUi Settings page for launcher preferences
that can safely change without a GPU swapchain, audio backend, or credential
side effect: **Reduce Motion**, **Automatic Updates**, and **Debug Logging**.

- `QuickLauncherSettingsController` owns a private draft on the UI thread.
  Toggling is not a live mutation. Applying first compares the store with
  the captured original to reject stale updates by other settings windows.
- The launcher adapter persists using the existing `LauncherPrefs.Save()`,
  without creating a new settings file or changing user/game data paths.
- Apply, Discard, Back, and All Settings are native actions with document,
  sequence and generation checks. The full existing Settings screen remains
  available for video, audio, controls, credentials and maintenance.
- The full Settings choice closes the native document before the legacy
  route takes focus. Returning Home restores the original native page.
- The page is compiled and published only with the opt-in RmlUi desktop
  build. All other clients retain their existing UI by default.

Asset-free `rmlui-quick-settings-check` validates no optimistic changes,
one-save behavior, concurrent preference conflict refusal, discard
rehydration, persistence failure, invalid choices and owner-thread checks.

This slice **does not claim complete native Settings parity**. Video
confirmation, key binds, input/controller device settings, online
credentials and Accessibility/Android acceptance remain future gates.
