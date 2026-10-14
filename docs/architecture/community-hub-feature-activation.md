# Community Hub 3.0 | Feature activation after PRs 10–14

**Scope:** merged Community Hub PR-10 through PR-14, with all usable
application-level features enabled by default where doing so does not
require guessing a host, installing unrelated software, or weakening
immutable package/replay security.

## Enabled by default in this activation

| Feature | Default | Operational behavior |
|---|---|---|
| Game Community Hub | **On** | Existing Discover, Installed, Favorites, Downloads and exact-hash map installation remain available in the game |
| Studio creator Community workspace | **On** | **Online -> Community maps** opens the new creator/transfer/publishing workspace, with **Advanced Revisions** still offering the existing creator manager |
| Durable upload/download and cross-process transfer index | **On** | A shared atomic jobs index and exclusive per-job leases coordinate simultaneous Game and pop-out Studio processes |
| Verified public map object mirroring | **On** | Newly published/discoverable .ppmap files are mirrored asynchronously under immutable SHA-256 keys. Metadata and publishing never wait for a copy. The origin is authoritative on mirror failure |
| Existing v2/v3 Community catalog, typed assets, revision history and optional visual metadata | **Available** | Existing authenticated APIs and exact optional dependency identities remain supported. Optional visuals never block matchmaking or mutate map identity |
| External NetRadiant preference | **Automatically on only when installed/configured** | With `PROJECT_PRIME_NETRADIANT_EXE=/full/path/to/radiant`, Map Studio shows **Edit in NetRadiant**. It exports strict editable .map plus identity sidecar and launches the external tool, without overwriting an unsaved external edit |
| CDN reads | **On when a trusted HTTPS host is configured** | A client trusts only explicit `PROJECT_PRIME_TRUSTED_CDN_HOSTS` and/or local `PROJECT_PRIME_MAP_CDN_BASE`. Every cached byte is checked against exact package SHA-256, MapId and content hash, with authenticated origin fallback |
| NetRadiant BSP compile/import | **Available when q3map2 is configured** | File menu's **Compile NetRadiant Source** uses `PROJECT_PRIME_Q3MAP2_EXE=/full/path/to/q3map2`. Missing tools cannot run and do not block native Studio |

### Safe controls and prerequisites

```bash
# Native game and creator Community features are enabled without configuration.
# The following values are only needed for external infrastructure:
PROJECT_PRIME_NETRADIANT_EXE=/absolute/path/to/radiant
PROJECT_PRIME_Q3MAP2_EXE=/absolute/path/to/q3map2
PROJECT_PRIME_MAP_CDN_BASE=https://your-verified-cdn.example/objects/
PROJECT_PRIME_TRUSTED_CDN_HOSTS=your-verified-cdn.example
```

**CDN deployments:** the operator must configure a real HTTPS base that
serves `sha256/{prefix}/{sha}.ppmap` from the immutable object mirror.
A placeholder, a non-HTTPS hostname, a different CDN path layout, or an
unverified object store **cannot** provide an active CDN. With no trusted
hostnames, clients use the existing origin instead. An external object
store is not provisioned by these code changes. Mirroring uses additional
disk space roughly proportional to the public package set; monitor free
space and turn it off if disk quotas are tight.

**NetRadiant deployments:** Project Prime does not bundle, install,
redistribute or sign upstream NetRadiant or q3map2 binaries. The configured
path must be absolute, refer to a regular non-symlink desktop file, and
the editor must have its separately installed compatible gamepack.
Unsupported Quake surface formats still fail explicitly. When tools are
not installed the native JSON Studio remains available.

**Optional HD and Ultra visuals:** PR-10 implements exact dependency
lockfiles, optional resource caches and immutable metadata, not a complete
hardware-tested renderer-side shader/LOD replacement pipeline. This
activation does **not** force asset packs into live gameplay or silently
substitute an unrelated texture. The renderer integrations and Android
memory budget still require separate work.

## Rollback without map/protocol changes

```bash
# Any of the following can be set independently:
PROJECT_PRIME_STUDIO_COMMUNITY_HUB_PREVIEW=0
PROJECT_PRIME_MAP_OBJECT_MIRROR_ENABLED=0
PROJECT_PRIME_NETRADIANT_DEFAULT=0

# To force origin-only downloads:
# unset PROJECT_PRIME_TRUSTED_CDN_HOSTS and PROJECT_PRIME_MAP_CDN_BASE
```

Rollback returns Studio to the original creator dashboard, disables new
mirror jobs and/or hides preferred external-editor launch. It does
not delete existing mirrored objects, immutable published packages,
historical replay files, identity receipts, installed archives,
legacy v2 endpoints, or saved authoring projects.

**Mixed-version caution:** run both Game and pop-out Studio with the
coordinated transfer-index implementation. An old process that does
not use `transfer-leases/index-v1.lock` must not write the same
`community-transfers` directory simultaneously.

## Tests and release readiness

- Validate default-on/off switches, invalid values, external-tool path
  validation, exact public mirror and trusted-host policy using the
  content-free `community-transfer-check`, `community-cdn-check`, and
  `netradiant-bridge-check` suites.
- Run the complete desktop/Android/server CI matrix and historical
  `.ppmap` packaging checks at this activation PR's exact head.
- Actual NetRadiant/q3map2 GUI compile, in-game collision/render,
  1v1/8-player, Android device, multiplayer Ready and historic replay
  manual tests remain **deferred by the user**, not silently passed.
- A GitHub merge changes the application's default behavior on the next
  build. It does **not** remotely set environment variables or redeploy a
  running Community server/CDN without an explicit deployment action.
