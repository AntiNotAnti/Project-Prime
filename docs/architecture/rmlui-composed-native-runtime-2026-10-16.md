# RML-22: wire the authored page manager into the desktop native runtime

## Why this slice is required

The current optional desktop OpenGL host was still publishing data and reading
form inputs directly against the bootstrap Home document. The RML-20 native
map selector and RML-21 Hunter identity modal existed in the controller and
RML assets, but their multi-document adapter was not connected in the desktop
entry point. Adding further native screens to that path would pass controller
tests without ever becoming visible to users.

## Runtime integration

- RmlUiOpenGlSmoke now creates a shared RmlUiPageManager and
  RmlUiLauncherPages when its pinned native bridge initializes.
- The baseline page starts at pages/home/home.rml. Live authoritative controller
  snapshots hydrate the real Home, Play, Lobby and Rules documents rather than
  the hidden bootstrap home.
- Foreground document token and sequence admission run through
  RmlUiLauncherPages.HandleIntent, including the restored Maps and Hunters
  modals. Background or retired-document events do not reach the controller.
- Editable create/join/rules fields are read from their actual document; draft
  values survive navigation, modal opening and return from other workspaces.
- Existing native News, Community Discover, Hunter picker, Hunter License
  overview, and Quick Settings presenters share the same manager. The baseline
  is suspended before a standalone page opens and resumes only when all
  independent native pages have closed.
- Initializing, rendering, shutdown and recoverable fallback still use the
  existing game OpenGL context. Avalonia's default presentation is unchanged.

## Acceptance

tools/rmlui-runtime-check exercises actual composed Home/Play/Lobby page
lifetime, simultaneous Rules + Maps modal ownership, background event
rejection, Hunter identity modal opening/retirement, native field persistence,
and suspension/resumption around independent pages. CI must also build the
actual desktop native runtime and run its software-OpenGL pixel test.

This is not final UI parity: dedicated Lobby Admin, full cosmetics and 3D
previews, the remaining rich workspaces, Android launch/input lifecycle and
cross-platform visual acceptance remain follow-ups.

**Full Settings is deliberately deferred until after those slices.**
