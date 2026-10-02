# Map Studio windows and community maps

Open Forge and choose **Pop out**. The current project is saved before a separate,
maximized, resizable desktop editor starts. Editing in the original view is paused
until the new process exits, then its saved project is reloaded. Closing the OS
window preserves unsaved work in recovery; it does not silently save over a project.
`-mapstudio -studioproject /absolute/path/map.json` opens a project directly.
The standalone editor retains the existing playtest-and-return workflow.

Forge now uses the available window area without the launcher header/footer or its
large-screen zoom curve. File, Edit, View, Build, and Online menus organize commands.
Side panels have draggable dividers; **Maximize view** hides them. **Four views**
switches between the active view and perspective/top/front/side panes. Click a pane
to activate it. The orthographic panes use wireframes and grids on their editing
planes. Grid density adapts to camera distance, while the grid chooser controls
snapping. Selection and undo history are shared between panes. Existing modeling,
material, layer, arrangement, validation, navigation and playtest tools remain
available through the inspector and command palette.

## Sharing and playing

1. Open **Online → Community maps** and refresh. The default library address is
   `https://maps.rebooty.xyz/`; a different address can be entered and is remembered.
   Publishing works with the automatically created guest identity; no registered account or creator secret is required. Link an account if you want to recover map ownership on another device.
2. Authors choose **Upload current**. Project Prime obtains a short-lived Community
   publishing ticket from the author's guest or registered identity automatically; no creator
   token is copied or stored in Map Studio. Map Studio builds a portable `.ppmap`
   including referenced assets before upload. Current clients upload in resumable chunks of
   at most 50 MiB. The service records the exact byte offset on disk, so retrying after a
   dropped connection or service restart continues the same immutable package instead of
   starting it over. Clients also reduce the chunk size automatically if a reverse proxy
   advertises a lower request-body limit.
3. Players can browse Community maps from Forge or directly from **Create Lobby →
   Map Rotation → Source: Community**. Selecting a remote map downloads, validates,
   installs, builds and registers that exact immutable version.
4. **Online → Host current map** packages and installs the editor snapshot, then
   opens normal lobby creation with that map selected. Remote directory-managed
   hosts carry an exact package hash for every custom rotation entry and download
   every missing package before spawning the isolated lobby server.
5. Joining players do not need to pre-install a published map. The lobby advertises
   the exact package identity and Community source; clients download, verify, build,
   prewarm and report Ready before the server's normal start barrier releases.

The existing lobby's **Choose deployment zone** card picker also loads public
Community maps alongside local arenas. Community cards have a COMMUNITY badge;
search matches their name or author. **Use Map** downloads, verifies and builds the
selected immutable package, then checks the lobby's mode/player compatibility.
Cancel closes the picker and cancels pending preparation; service failures leave
local cards available and **Refresh Community** retries the listing.

When an owner selects a Community map in an existing lobby, the game server
fetches and builds the exact package from its configured Community service before
applying the change. The lobby stays responsive during preparation. Ownership,
lobby phase, and revision are checked again before installation; stale requests
are cancelled. This requires the updated game server and client (the client waits
up to three minutes for preparation). New directory-hosted lobbies still prepare
their initial rotation before the server process starts.

To remove an old version from public discovery, open **Online → Community maps**,
choose **My Maps**, press **Refresh**, select the version, and press **Set Unlisted**.
Refresh/reopen the lobby picker afterward. Unlisted packages remain available by
exact package link for existing lobbies. Already installed local maps still have
local cards: remove their local package through the map library, then restart the
app to rebuild its runtime map list. Service-owned legacy listings must be
unlisted by the server operator.

Map installation is unavailable while a conflicting map runtime is active. Remote
hosts only trust their operator-configured Community service rather than arbitrary
URLs supplied by clients.

Downloads live in the writable `user-maps` directory, separate from bundled app
resources. The catalog includes them after restart and prefers installed versions
of the same map identity. Built-in room names and conflicting custom identities
cannot be replaced by downloaded packages.

## Run a community library

The map library is a self-hosted HTTP service included in desktop and server builds:

```sh
# Set PROJECT_PRIME_MAP_UPLOAD_TOKEN to a random secret of at least 24 characters
# through your service manager or environment; do not put it in the command line.
ProjectPrime -maphub http://127.0.0.1:8091/ -maphubstorage /srv/prime/community
```

Use an HTTPS reverse proxy for public access. The package hard limit remains 512 MiB,
but current clients send no individual upload request larger than 50 MiB. Configure the
proxy request-body limit above 50 MiB and give upload requests enough body/read time;
the shipped Caddy/nginx templates at 128 MB are sufficient. HTTP clients are allowed
only for loopback addresses. Normal authors authenticate with Hunter License. Map Studio exchanges the Supabase
session for a short-lived `ppm1` Community ticket, and the map service verifies that
ticket through the `community-map-ticket` Edge Function. The Supabase access token
never reaches the map service. The legacy upload token remains an administrator /
service-owner credential for recovery and automation, not something distributed to
map authors.

Routes beneath the configured prefix:

- `GET health`: service status and map count, no credentials required.
- `GET maps`: JSON listing, no credentials required.
- `POST uploads/<sha256>`: create or resume a package upload session.
- `GET uploads/<sha256>`: read the persisted byte offset for the authenticated creator.
- `PUT uploads/<sha256>?offset=<bytes>`: append one bounded package chunk.
- `POST uploads/<sha256>/complete`: verify SHA-256/package metadata and atomically publish.
- `DELETE uploads/<sha256>`: discard the authenticated creator's partial upload.
- `POST maps`: legacy one-request raw `.ppmap` upload retained for older clients.
- `GET maps/<sha256>`: immutable package bytes, no credentials required.

Upload session metadata and partial bytes survive service restarts and expire after
24 hours if abandoned. At most 16 partial sessions and 2 GiB of partial upload data
are retained at once. Completion still passes through the existing ownership,
version-conflict, archive-size, entry-size, content-hash and package validation.

The library uses SHA-256 filenames, validates package manifests/assets, rejects
traversal and unsupported package entries, bounds archive/expanded sizes, limits
stored maps to 2,000 and published storage to 2 GiB, and serializes publication
while allowing bounded concurrent reads and resumable chunk transfers.
Operators can remove packages from storage while stopped, then restart to rebuild
the listing. Identical uploads are idempotent. Names/authors are user-supplied
metadata, not verified identities. Do not distribute extracted base-game assets.

## VPS deployment

The map service runs separately from the game servers on `ubuntu@51.161.113.128`.
Create a DNS A record for `maps.rebooty.xyz` pointing to `51.161.113.128`; the
installed Caddy site obtains and renews its HTTPS certificate once DNS resolves.

```sh
MPH_MAP_DOMAIN=maps.rebooty.xyz ./deploy-map-service.sh
# Optional SSH identity: MPH_SERVER_SSH_KEY=/absolute/path/to/key
# Optional SDK override: DOTNET="$HOME/.dotnet/dotnet"
```

The script builds for the VPS architecture, installs a versioned release, checks
backend health, and reloads the existing reverse proxy. It restarts only
`prime-maps.service`, leaving game matches running. A public HTTPS check failure
returns exit code 2 after a successful backend deployment, so DNS can be fixed
without reinstalling. Backend/proxy installation failures restore the prior release
and configuration. `deploy-server.sh` can also deploy it with `MPH_DEPLOY_MAPS=1`,
but that full deployment restarts game services.

- Executable: `/home/ubuntu/prime-maps/current/ProjectPrime`
- Persistent packages: `/home/ubuntu/prime-maps/data/packages`
- Private listener: `http://127.0.0.1:8091/`
- Caddy site: `/etc/caddy/prime-maps.caddy`
- Administrator token: `PROJECT_PRIME_MAP_UPLOAD_TOKEN` in root-only
  `/etc/project-prime/maps.env`. Generated on first installation and retained on
  upgrades. Do not distribute it to creators. Normal publishing uses Hunter License.
- Deploy the `community-map-ticket` Supabase Edge Function with JWT gateway
  verification disabled for that function. The function performs its own session
  validation for minting and exposes only ticket verification to the map service.
  `supabase/config.toml` contains the expected setting.
- Players do not need credentials to browse or download published maps.

Use `sudo systemctl status prime-maps` and `sudo journalctl -u prime-maps`
for diagnostics. Back up the packages directory and token file. To rotate the
token, update the environment file and restart only `prime-maps`. Prior executable
releases remain in `/home/ubuntu/prime-maps/releases` for rollback.

## Verification

```sh
dotnet run --project tools/map-editor-check
dotnet run --project tools/map-community-check
dotnet run --project src/MphRead -- -mapstudioshot /tmp/map-studio-shots
```

The community harness exercises authenticated upload, exact-byte downloads,
idempotency, malformed archives, invalid identifiers/sizes, bounded streams,
installed-map discovery, partial-upload persistence, exact-offset resume after a
service restart, and cleanup after successful assembly. Map Studio capture emits
large, small and four-view layouts. Full TrenchBroom compatibility (including its
brush CSG kernel, Quake `.map`/FGD support and UV editor) is outside this change.
