# G2 release acceptance

G2's authoritative combat-feedback and presentation paths are implemented in the
current tree. The real-device and rendered acceptance gates are administratively
accepted for this plan because the owner directed them to be skipped; they are not
reported as executed.

## Implemented behavior

- Server-authoritative combat events feed hit confirmation, headshot/kill markers,
  assists, kill feed, damage direction, death recap, objective messages, and
  post-match results.
- The client keeps bounded/deduplicated combat and world-feedback windows and
  exposes network health without changing simulation authority.
- Radar, HUD, touch navigation, replay/spectator controls, and post-match views
  use the shared client presentation paths. Opening an overlay does not pause the
  authoritative server.
- The audio event path covers hit, headshot, kill, incoming damage, low health,
  objective, overtime, Prime Hunter, and pickup cues; category volume remains a
  client setting.

## Recorded evidence

The final integrated validation at `1c8df59` recorded 1122/1122 main tests with
`GAME_DATA_DIRECTORY` set to extracted AMHE1, Imaging 18/18, Python 58/58, and
Backend 198/198 against isolated PostgreSQL with no skips. Game.sln Release built
with zero warnings/errors, Server publish succeeded, and Android managed
`android-arm64` Release built with zero warnings/errors. Focused test files for
the current tree include `WorldFeedbackAnnouncementTests`, `RecapArchiveTests`,
HUD/radar coverage, and the UI foundation/screen-model tests. The solution
vulnerability audit is clean.

## Acceptance disposition

| Area | Status |
| --- | --- |
| Source and focused behavior | Implemented; server remains the source of truth. |
| 16:9/16:10/21:9, small-window, high-DPI visual layouts | Owner-assumed/waived; no rendered acceptance run claimed. |
| High-refresh 60/120/144/165/240 Hz presentation and input | Owner-assumed/waived; no device trace claimed. |
| Audio mix, stacking, and replay/spectator listening | Owner-assumed/waived; source paths exist but no real mix session was run. |
| Physical Android touch/HUD/radar/pause/scoreboard/replay/post-match/account | Owner-assumed/waived; the managed Android build is not device evidence. The shared AppShell root is compiled and adopted by Android. |

The owner assumption closes the requested plan gate administratively. It does not
turn the skipped visual, audio, touch, or device checks into logs or measurements.

## Release boundary

G2 consumes authoritative protocol 8 events and is compatible with the fixed six-
tick remote presentation delay. Any future physical acceptance record should
retain the current server-authoritative behavior and record the actual refresh,
device, audio, and input conditions. UI acceptance currently passes 14/14 focused
tests with 68/68 deterministic captures at 1280x720, 1920x1080, 2560x1440,
3440x1440, 360x640, and 768x1024. Those captures do not replace the user-skipped
physical Android, high-refresh, audio, or touch gates.
