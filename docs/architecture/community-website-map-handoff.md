# Community map website handoff

The player-facing Community workspace now opens the corresponding stable
website map project page using `https://projectprime.gg/maps/{MapId}` through
the existing `Updater.OpenLink` platform browser abstraction. The game does
not register a custom URI handler, and no privileged local action is
triggered from the webpage.

The in-game map card derives its URL only from the server-returned strongly
typed Guid MapId. The specific install/play target remains the exact
immutable `.ppmap` package hash and content identity, not the latest
website revision. Opening a web page never replaces the selected game
package or auto-installs anything.

This works on supported desktop and Android platform heads through the
existing browser opener; where none is available the game displays the
HTTPS URL in its status text.

## Acceptance

1. Select a published map in Game > Community > Discover.
2. Use VIEW ON WEBSITE and confirm the stable /maps/{MapId} page opens.
3. Install the selected exact package; verify no revision was changed by
   visiting the website.
4. Offline/unavailable browser should display the URL instead of throwing.
5. Existing multiplayer exact-hash checks and replay retrieval are
   unchanged.

A separate production deployment is necessary for website pages and map
service CORS. Merging this UI PR is not itself a deployment.
