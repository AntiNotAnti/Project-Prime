# Prime Hunters G6 UI design system

This document defines the shared launcher, lobby, and session UI foundation. It applies to desktop and Android Avalonia views. Gameplay HUD and radar scale remain independent because they have different legibility and safe-area constraints.

## Product principles

- Keep contrast high and text readable while the game is moving behind an overlay.
- Use science-fiction styling as a frame for information, never as a substitute for clear labels.
- Present dense information only where players compare options, such as a lobby roster or server list.
- Keep animation restrained and remove it when reduced motion is enabled.
- Make keyboard and controller focus obvious. A focused control uses the accent border at `FocusThickness`.
- Give every state a text label or icon plus text. Color can reinforce `Ready`, `Warning`, `Failed`, and team identity, but cannot carry their meaning alone.
- Keep the UI event-driven. Rebuild collections when their revision changes, not on the render tick.

## Tokens

The source of truth is `src/Client/UI/Theme`.

| Group | Tokens and use |
| --- | --- |
| Color | `Ink`, `Panel`, `PanelRaised`, `Edge`, `Text`, `TextMuted`, `Accent`, `Success`, `Warning`, `Danger` |
| Spacing | `Space1` through `Space7`, from 4 to 48 logical pixels |
| Type | `TextMinimum`, `TextSmall`, `TextBody`, `TextHeading`, `TextDisplay`; Inter is the shared embedded family |
| Shape | `RadiusSmall`, `RadiusMedium`, `RadiusLarge`, `FocusThickness` |
| Input | `MinimumTouchTarget` is 48 logical pixels |
| Layout | `NavigationRailWidth`, `ContentMaxWidth`, `SafeArea` |
| Motion | `Fast`, `Standard`, `Slow`; reduced motion resolves each to zero |

Screens should compose these tokens instead of introducing close local variants. New tokens belong in the shared group when two or more screens need them.

## Responsive layout

`UiBreakpoints.FromWidth` defines three stable modes:

| Mode | Logical width | Navigation and content |
| --- | ---: | --- |
| Compact | up to 719 | Bottom navigation, one content column, touch-sized actions |
| Medium | 720 to 1099 | Navigation rail, one primary content column |
| Wide | 1100 and above | Navigation rail with room for multi-column feature screens |

The shell switches mode only when its logical width crosses a breakpoint. Feature screens may adapt their own grids from the current mode but must keep the same information architecture and route on every platform.

Safe-area padding is a preference owned by `AccessibilityPreferences`. Screens must not counteract it with negative margins.

## Typography and scale

Text sizes are logical pixels. `UiTypography.Scale` applies UI scale and the large-text multiplier while enforcing `TextMinimum`. Large text must be tested without truncating status, action, or validation labels. UI scale is bounded to 80–150 percent so the shell remains recoverable at small viewports.

## Color and status

The standard team palette has blue and red identities. Deuteranopia, protanopia, and tritanopia palettes use distinguishable alternate pairs from `UiColors.TeamPalette`. Team names, glyphs, or slot labels remain visible with every palette.

`StatusBadge`, `ErrorBanner`, and `LoadingIndicator` expose automation names and visible text. Loading and failure states must state the operation in progress or the failed action. Disabled actions must include a visible reason nearby; Ranked must never silently route to unranked play.

## Input and focus

Every action must work with touch, pointer, keyboard, and controller. Controls use a minimum 48-pixel target. `UiFocusNavigationPolicy` owns explicit directional relationships; `UiFocusCoordinator` maps stable keys to controls. Feature screens provide an initial focus key and stable action keys.

Opening a modal records the invoking focus key. Back or Escape closes the top modal first and restores that key. Route history stores the source focus key and restores it when returning. Modal content stays inside `UiModalHost`; screens do not create independent workflow windows.

Controller mappings use the platform input adapter to translate directional input into the shared focus directions, primary action into activation, and cancel into router back. Hold versus toggle behavior comes from `AccessibilityPreferences.HoldActions`.

## Navigation and ownership

`UiRouter` exclusively owns the current route, back stack, modal stack, route parameters, and focus restoration. The routes are Home, Play, Lobby, Server Browser, Private Match, Hunter License, Replays, Settings, Post-match Results, and Account. The AppShell owns top-level navigation and the persistent session status.

Feature screens receive state and issue requests through their controller or coordinator. A screen does not claim that a lobby, account, rule, ready, Hunter, or match mutation succeeded. It presents the authoritative state returned by its owner.

## Component policy

Use `PrimaryButton` once for the main action in a decision area and `SecondaryButton` for supporting actions. Use `FocusCard` and the summary-card types for selectable records, `Tabs` for sibling views, and the feedback controls for loading, empty, warning, and failure states. Components set their own focus visuals, touch size, palette, and automation label requirements.

Custom drawing remains appropriate for branded or high-frequency presentation. Standard layout and form controls should use Avalonia composition so accessibility and responsive behavior remain available.

## Motion and performance

Transitions use shared durations and must resolve to zero under reduced motion. Animation must not delay navigation, input, or authoritative state updates. Lobby rosters, server results, account history, and replay libraries update on state/revision changes. Thumbnail caches stay bounded, and network/backend queries stay asynchronous and cancellable.

## Foundation acceptance

- Every route can be created through `IUiScreenFactory` and displayed in one AppShell.
- Route back, nested modal back, and focus restoration have pure unit coverage.
- Compact, medium, and wide boundaries have pure unit coverage.
- Visible state labels and automation names are present in shared feedback components.
- The Client builds with the existing framework set; `Tmds.DBus.Protocol` is
  explicitly pinned to 0.21.3, the first patched release selected for the client.
- Android compiles the shared `src/Client/UI/**` implementation and roots
  `MainView` at `ClientUiRuntime.Shell` through `AndroidApp`. The managed
  `android-arm64` Release build passed with zero warnings and errors; physical
  Android acceptance remains owner-assumed.
- The focused UI acceptance suite passes 14/14 tests. Its deterministic 68/68
  capture matrix covers 1280x720, 1920x1080, 2560x1440, 3440x1440, 360x640,
  and 768x1024, including Home, Play, lobby/browser, private match, Hunter
  License, Post-match, Replays, Settings, and mouse-free launch-path focus.

## Screen-model contracts and pure-test expectations

Screen models are presentation state and request intent. They do not become a
second authority for matches, accounts, ratings, replays, or settings. Their
pure contracts should remain deterministic and testable without Avalonia,
network sockets, a database, a running game, or a clock.

The Home and Play models expose ranked availability as an explicit state. When
the account, verified-server, rating-policy, or transport-security prerequisite
is missing, Ranked is disabled with a user-facing reason and its action does not
select Quick Play, Practice, or any other fallback. The Home model exposes only
the top-level actions; server configuration belongs to Play or the flow it
opens.

Server-browser filtering and search operate on the advertised snapshot already
owned by the browser service. Search is case-insensitive and whitespace-trimmed,
matches the supported identity fields, and composes with mode, favorites,
recent, max-ping, full, compatibility, and sort filters. Filtering must not
mutate the source collection or invent reachability, ranking, rules, or ping
values. Empty results are a distinct state from loading, offline, and failure.

Map and lobby previews use a bounded asynchronous thumbnail cache. A repeated
key shares an in-flight request, completed entries are reused, eviction is
deterministic, and closing or replacing a screen cancels work that no longer
has a consumer. A cancelled or failed request cannot publish a stale image into
a newer request for the same key.

Private-match validation returns field-level reasons before a create request is
issued. It rejects unsupported rule combinations, invalid counts and limits,
and missing required map or mode values; it never silently repairs a value into
a different match. The model may expose a valid snapshot for the server-owned
request, but the server remains the authority for acceptance.

Hunter License history is cursor-paginated. Initial loading, loaded, empty,
loading-next-page, end-of-history, offline, and failed-page states are explicit;
an old page remains visible while the next page loads, duplicate cursors do not
repeat records, and a failed next page can be retried without discarding loaded
history. Replay deletion is a two-step contract: selecting Delete only opens a
confirmation containing the exact local replay identity, and storage is called
only after explicit confirmation. Cancel and stale confirmations do not delete
another replay.

Settings models group Gameplay, Controls, Video, Audio, HUD, Radar, Network,
Accessibility, and Account categories. Accessibility state includes scale,
large text, reduced motion, color-vision mode, safe area, and hold/toggle input
preferences; category changes preserve unrelated values and clearly identify
restart-required changes. All asynchronous screens expose distinct loading,
empty, offline, and error states with a retry or clear next action where one is
meaningful.

Each screen publishes stable focus keys and a deterministic initial focus. The
pure focus graph covers the launch path from Home through Play to the selected
entry, the compact and wide server-browser paths, private-match validation, and
modal confirmation. Back closes the top modal first and returns focus to its
invoker; a route transition cannot leave focus on a removed control.
