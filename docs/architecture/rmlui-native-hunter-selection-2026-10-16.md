# RML-11: Native local Hunter / Suit selection

This opt-in RmlUi slice replaces the Home Hunter picker with a native
seven-Hunter and four-suit draft, retaining the historical Project Prime
selectable card styling and existing LauncherPrefs persistence.

- Selecting a Hunter/Suit does not change the saved choice until Apply.
- The current HunterRules playable pool and four suit colors are respected.
- Concurrent legacy profile saves prevent overwriting stale draft values.
- Save failures and invalid arguments never acknowledge success.
- The **Full Hunter License** button continues to use the current
  authenticated legacy license page. Cosmetics, alt-form/weapon previews,
  account stats and server roster controls are not reimplemented here.
- Native Back and header navigation refuse to lose unsaved preferences or
  Hunter choices. Discard is explicit.

Asset-free `tools/rmlui-hunter-check` verifies the draft and persistence
contract without profile credentials, personal data or Nintendo game assets.

Remaining: full native 3D Hunter preview, cosmetics, Hunter License,
account settings, complete controller/touch accessibility, and device tests.
