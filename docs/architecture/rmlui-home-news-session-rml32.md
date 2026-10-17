# RML-32: Home News and truthful session/social parity

## Root cause

The authored `pages/home/home.rml` feature rail survived the October 9
migration, but its `home_feature_*` bindings were initialized empty and
never filled by the new Home/Play/Lobby controller. This left a large blank
panel in the restored launcher. Home and Lobby also included active
`route:social` controls even though the current client route resolver
has no Social destination or connected friends/party implementation.

## Implementation

- Use the **existing** `BundledNewsProvider` to fill the current
  `home_feature_category`, `home_feature_title` and
  `home_feature_summary`. This source is already used by the native News
  page. Avoid inventing a new news API, client cache or network request.
- Project the actual launcher session label as SOLO / IN LOBBY /
  LOBBY OWNER / MATCH STARTING from the current authoritative Lobby view.
- Do not invent friends, online counts, invites or party data. Until the
  real Social/Party service is ported, show a clear unavailable status and
  disable the Home and Lobby Social actions. They previously opened no page.
- Preserve the authored Home layout, focus, News navigation and icon/theme,
  keeping all added logic in the existing presentation-neutral controller.
- No full Settings work, no new credentials, backend, gameplay code or default
  RmlUi cutover.

## Acceptance

`tools/rmlui-live-menu-check` verifies News is present and properly bound,
the session state is real, and unavailable Social controls are disabled
instead of presenting fake metrics or dead buttons.

The broader **Social/Party backend and page** remains a separate explicit
parity migration requiring account/privacy, friends, presence, invitation,
party ownership and network acceptance. After that, the availability flag
should be derived from the real connected service rather than hardcoded false.

RML-29 restores Home/Lobby 3D stage, RML-30 restores real Lobby chat and
roster interactions, and RML-31 restores standalone Hunter picker preview.
Full Settings remains **last**.
