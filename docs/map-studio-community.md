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
   Upload credentials are never saved.
2. Authors enter the community operator's upload token and choose **Upload current**.
   Map Studio builds a portable `.ppmap` including referenced assets before upload.
   Selecting a published map also displays its direct package link for sharing.
3. Players browse/search and select **Install** or **Install & host**. They can also
   import a downloaded `.ppmap` directly. Installation checks the archive hash,
   package identity, references and compilation before registering the map.
4. **Online → Host current map** packages and installs the editor snapshot, then
   opens lobby creation with that map and local hosting selected. The local server
   receives the same map directories, including the writable user library.

Players must install the same published version before joining. This implementation
uses an explicit shared library, not automatic UDP map negotiation. Community maps
are hosted on the player's computer; remote directory-managed servers do not yet
accept map uploads. Existing requirements for Internet-reachable local hosting
still apply. Map installation is unavailable during an active online session.

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

Use an HTTPS reverse proxy for public access. Configure its request/body timeouts
and upload limit (at most 128 MiB). HTTP clients are allowed only for loopback
addresses. Operators supply the upload token to trusted map authors. Tokens grant publishing
access; there are no individual author accounts or moderation UI yet.

Routes beneath the configured prefix:

- `GET health`: service status and map count, no credentials required.
- `GET maps`: JSON listing, no credentials required.
- `POST maps`: raw `.ppmap` body, `Authorization: Bearer <token>` required.
- `GET maps/<sha256>`: immutable package bytes, no credentials required.

The library uses SHA-256 filenames, validates package manifests/assets, rejects
traversal and unsupported package entries, bounds archive/expanded sizes, limits
stored maps to 2,000 and storage to 2 GiB, and processes one request at a time.
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
- Upload token: `PROJECT_PRIME_MAP_UPLOAD_TOKEN` in root-only
  `/etc/project-prime/maps.env`. Generated on first installation and retained on
  upgrades. Retrieve it privately over SSH with sudo and give it only to publishers.
  Players do not need a token to browse or download.

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
installed-map discovery and service restart persistence. Map Studio capture emits
large, small and four-view layouts. Full TrenchBroom compatibility (including its
brush CSG kernel, Quake `.map`/FGD support and UV editor) is outside this change.
