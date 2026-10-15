# V19 PR-13: custom depth-tested team/player outline colors

Features 51–53: `Custom` outline style, Teams 1–4 and FFA Players 1–8, two-column editor with per-slot/group reset, Apply/Discard, and a 60-second temporary preview. FFA numbers refer to **server slots**, not hunter, team or rank.

`OutlineColorPalette` uses one atomically published immutable settings/RGB snapshot. The renderer reads resolved RGB directly; no per-frame JSON parsing, no forced brightening, and no new scene/color mask. Existing depth-tested outline pass, cloaking/frozen/status filters, and local-player suppression remain unchanged.

The existing `LauncherPrefs` file persists the JSON palette (`player_outline_colors`) with strict validation and 2 KiB maximum input; invalid input cannot change applied colors. `SettingsView` holds a detached local draft; temporary preview automatically reverts on timeout, cancel, Apply, or detached settings view. Draft only persists on Apply.

`dotnet run --project tools/nettest/nettest.csproj -c Release -- --outline-colors` tests all four team/eight FFA slots, exact RGB, malformed JSON, snapshot immutability and legacy outline styles. CI runs it with the existing nettest matrix.

Pending platform verification: Windows/Linux macOS GL, Android GLES, low-res depth mask, solo vs bot visuals, team/FFA online slots, controller focus, persistence archive and full build.
