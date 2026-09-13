#!/usr/bin/env bash
# Build a signed player release in GitHub Actions, verify the public draft,
# and publish it only after an explicit operator confirmation.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SOURCE_REPOSITORY="${PROJECT_PRIME_SOURCE_REPOSITORY:-AntiNotAnti/Project-Prime}"
PUBLIC_REPOSITORY="${PROJECT_PRIME_PUBLIC_REPOSITORY:-AntiNotAnti/Project-Prime-Releases}"
WORKFLOW="release.yml"
SOURCE_REF="main"
BUMP="patch"
TAG=""
DRAFT_ONLY=0
ASSUME_YES=0

usage() {
  cat <<'NOTE'
Usage:
  tools/release-player.sh [--bump patch|minor|major] [--draft-only|--yes]
  tools/release-player.sh --tag vX.Y.Z [--draft-only|--yes]

The script requires a clean main checkout exactly matching the private source
repository's main branch. It
starts the existing release.yml GitHub Actions workflow, waits for all builds
and update-contract tests, verifies the seven expected public player assets,
then asks whether to publish the draft.

Options:
  --bump VALUE   Create the next patch, minor, or major source tag (default: patch).
  --tag TAG      Rebuild an existing source tag such as v1.2.3.
  --draft-only   Stop after verifying the public draft.
  --yes          Publish the verified draft without the interactive prompt.
  -h, --help     Show this help.

Environment overrides:
  PROJECT_PRIME_SOURCE_REPOSITORY
  PROJECT_PRIME_PUBLIC_REPOSITORY
NOTE
}

die() {
  printf 'release-player: %s\n' "$*" >&2
  exit 1
}

while (($# > 0)); do
  case "$1" in
    --bump)
      [[ $# -ge 2 ]] || die "--bump requires patch, minor, or major"
      BUMP="$2"
      shift 2
      ;;
    --tag)
      [[ $# -ge 2 ]] || die "--tag requires vX.Y.Z"
      TAG="$2"
      shift 2
      ;;
    --draft-only)
      DRAFT_ONLY=1
      shift
      ;;
    --yes)
      ASSUME_YES=1
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      die "unknown option: $1"
      ;;
  esac
done

case "$BUMP" in
  patch|minor|major) ;;
  *) die "--bump must be patch, minor, or major" ;;
esac
if [[ -n "$TAG" && ! "$TAG" =~ ^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$ ]]; then
  die "--tag must be an exact vX.Y.Z release tag"
fi
if [[ "$DRAFT_ONLY" -eq 1 && "$ASSUME_YES" -eq 1 ]]; then
  die "--draft-only and --yes cannot be used together"
fi

for command in git gh jq python3 openssl; do
  command -v "$command" >/dev/null 2>&1 || die "$command is required"
done

cd "$ROOT"
git rev-parse --is-inside-work-tree >/dev/null 2>&1 || die "$ROOT is not a Git checkout"
[[ -z "$(git status --porcelain)" ]] || die \
  "the checkout is dirty; commit or stash the intended release state first"
[[ "$(git branch --show-current)" == "$SOURCE_REF" ]] || die \
  "releases must be started from the $SOURCE_REF branch"

gh auth status --hostname github.com >/dev/null 2>&1 || die \
  "GitHub CLI is not authenticated"
git fetch --quiet origin "$SOURCE_REF"
LOCAL_SHA="$(git rev-parse HEAD)"
SOURCE_SHA="$(gh api "repos/$SOURCE_REPOSITORY/commits/$SOURCE_REF" --jq '.sha')"
[[ "$LOCAL_SHA" == "$SOURCE_SHA" ]] || die \
  "local $SOURCE_REF does not exactly match $SOURCE_REPOSITORY/$SOURCE_REF"

REQUIRED_SECRETS=(
  UPDATE_SIGNING_PRIVATE_KEY
  PROJECT_PRIME_RELEASE_TOKEN
  ANDROID_KEYSTORE
  ANDROID_KEYSTORE_PASSWORD
  ANDROID_KEY_ALIAS
  ANDROID_KEY_PASSWORD
)
SECRET_NAMES="$(gh secret list --repo "$SOURCE_REPOSITORY" --json name --jq '.[].name')"
for secret in "${REQUIRED_SECRETS[@]}"; do
  if ! grep -Fxq "$secret" <<<"$SECRET_NAMES"; then
    die "required GitHub Actions secret $secret is not configured"
  fi
done

if [[ -n "$TAG" ]]; then
  gh api "repos/$SOURCE_REPOSITORY/git/ref/tags/$TAG" >/dev/null 2>&1 || die \
    "source tag $TAG does not exist; use --bump to create the next release tag"
  EXPECTED_TAG="$TAG"
else
  LATEST_TAG="$(gh api "repos/$SOURCE_REPOSITORY/git/refs/tags" --paginate --jq '.[].ref' \
    | sed 's#^refs/tags/##' \
    | grep -E '^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$' \
    | sort -V | tail -1 || true)"
  if [[ -z "$LATEST_TAG" ]]; then
    EXPECTED_TAG="v0.1.0"
  else
    IFS=. read -r MAJOR MINOR PATCH <<<"${LATEST_TAG#v}"
    case "$BUMP" in
      major) MAJOR=$((MAJOR + 1)); MINOR=0; PATCH=0 ;;
      minor) MINOR=$((MINOR + 1)); PATCH=0 ;;
      patch) PATCH=$((PATCH + 1)) ;;
    esac
    EXPECTED_TAG="v$MAJOR.$MINOR.$PATCH"
  fi
  if gh api "repos/$SOURCE_REPOSITORY/git/ref/tags/$EXPECTED_TAG" >/dev/null 2>&1; then
    die "computed tag $EXPECTED_TAG already exists; retry or rebuild it with --tag"
  fi
fi

STARTED_AT="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
printf 'Starting signed player release %s from %s.\n' "$EXPECTED_TAG" "${LOCAL_SHA:0:12}"
if [[ -n "$TAG" ]]; then
  gh workflow run "$WORKFLOW" --repo "$SOURCE_REPOSITORY" --ref "$SOURCE_REF" \
    -f tag="$TAG" -f bump=none
else
  gh workflow run "$WORKFLOW" --repo "$SOURCE_REPOSITORY" --ref "$SOURCE_REF" \
    -f bump="$BUMP"
fi

RUN_ID=""
for _ in $(seq 1 30); do
  RUN_ID="$(gh run list --repo "$SOURCE_REPOSITORY" --workflow "$WORKFLOW" \
    --event workflow_dispatch --branch "$SOURCE_REF" --limit 20 \
    --json databaseId,createdAt,headSha \
    | jq -r --arg started "$STARTED_AT" --arg sha "$LOCAL_SHA" \
      '[.[] | select(.createdAt >= $started and .headSha == $sha)]
       | sort_by(.createdAt) | last | .databaseId // empty')"
  [[ -n "$RUN_ID" ]] && break
  sleep 2
done
[[ -n "$RUN_ID" ]] || die "could not identify the dispatched release workflow run"

printf 'Watching GitHub Actions run %s.\n' "$RUN_ID"
gh run watch "$RUN_ID" --repo "$SOURCE_REPOSITORY" --exit-status --interval 10

RELEASE_JSON="$(gh release view "$EXPECTED_TAG" --repo "$PUBLIC_REPOSITORY" \
  --json tagName,isDraft,url,assets)" || die \
  "the workflow succeeded but public draft $EXPECTED_TAG was not found"
[[ "$(jq -r '.tagName' <<<"$RELEASE_JSON")" == "$EXPECTED_TAG" ]] || die \
  "the public draft tag does not match $EXPECTED_TAG"
[[ "$(jq -r '.isDraft' <<<"$RELEASE_JSON")" == "true" ]] || die \
  "public release $EXPECTED_TAG is not a draft; refusing to modify it"

VERSION="${EXPECTED_TAG#v}"
EXPECTED_ASSETS=(
  "ProjectPrime-$EXPECTED_TAG-win-x64.zip"
  "ProjectPrime-$EXPECTED_TAG-linux-x64.tar.gz"
  "ProjectPrime-$EXPECTED_TAG-osx-x64.tar.gz"
  "ProjectPrime-$EXPECTED_TAG-osx-arm64.tar.gz"
  "ProjectPrime-$EXPECTED_TAG-android.apk"
  "update-manifest.json"
  "update-manifest.sig"
)
ASSET_COUNT="$(jq '.assets | length' <<<"$RELEASE_JSON")"
[[ "$ASSET_COUNT" -eq "${#EXPECTED_ASSETS[@]}" ]] || die \
  "public draft has $ASSET_COUNT assets; expected ${#EXPECTED_ASSETS[@]}"
for asset in "${EXPECTED_ASSETS[@]}"; do
  jq -e --arg name "$asset" \
    '.assets[] | select(.name == $name and .size > 0)' <<<"$RELEASE_JSON" >/dev/null \
    || die "public draft is missing non-empty asset $asset"
done

MANIFEST_VERSION="$(gh release download "$EXPECTED_TAG" --repo "$PUBLIC_REPOSITORY" \
  --pattern update-manifest.json --output - | jq -r '.version')"
[[ "$MANIFEST_VERSION" == "$VERSION" ]] || die \
  "public manifest version $MANIFEST_VERSION does not match $VERSION"

# Verify the exact signed metadata and every package hash before offering the
# draft for publication. This uses the same pinned public SPKI shipped by the
# client; no GitHub API metadata is trusted as a substitute for the bytes.
VERIFY_DIR="$(mktemp -d "${TMPDIR:-/tmp}/project-prime-release-verify.XXXXXX")"
trap 'rm -rf "$VERIFY_DIR"' EXIT
for asset in "${EXPECTED_ASSETS[@]}"; do
  gh release download "$EXPECTED_TAG" --repo "$PUBLIC_REPOSITORY" \
    --pattern "$asset" --dir "$VERIFY_DIR" --clobber >/dev/null
done
if ! PUBLIC_KEY_B64="$(python3 - <<'PY'
import pathlib
import re
import sys

source = pathlib.Path("src/Client.Presentation/Update/UpdateTrust.cs").read_text(encoding="utf-8")
match = re.search(r'PinnedPublicKeySpkiBase64\s*=\s*"([A-Za-z0-9+/=]+)"\s*;', source)
if match is None:
    sys.exit(1)
print(match.group(1))
PY
)"; then
  die "could not read the pinned update public key"
fi
[[ -n "$PUBLIC_KEY_B64" ]] || die "could not read the pinned update public key"
python3 - "$PUBLIC_KEY_B64" "$VERIFY_DIR/pinned-public.der" <<'PY'
import base64
import pathlib
import sys
pathlib.Path(sys.argv[2]).write_bytes(base64.b64decode(sys.argv[1], validate=True))
PY
openssl pkey -pubin -inform DER -in "$VERIFY_DIR/pinned-public.der" \
  -out "$VERIFY_DIR/pinned-public.pem" >/dev/null 2>&1 || die \
  "the pinned update public key is not valid SPKI"
python3 tools/update-release.py --verify-manifest \
  "$VERIFY_DIR/update-manifest.json" \
  --signature "$VERIFY_DIR/update-manifest.sig" \
  --public-key "$VERIFY_DIR/pinned-public.pem" \
  --asset-dir "$VERIFY_DIR"

RELEASE_URL="$(jq -r '.url' <<<"$RELEASE_JSON")"
printf 'Verified public draft: %s\n' "$RELEASE_URL"
if [[ "$DRAFT_ONLY" -eq 1 ]]; then
  printf 'Draft-only mode selected; the release remains private to maintainers.\n'
  exit 0
fi

if [[ "$ASSUME_YES" -ne 1 ]]; then
  if [[ ! -t 0 ]]; then
    printf 'Non-interactive session; leaving the verified release as a draft.\n'
    printf 'Publish later with: gh release edit %s --repo %s --draft=false --latest\n' \
      "$EXPECTED_TAG" "$PUBLIC_REPOSITORY"
    exit 0
  fi
  printf 'Publish %s to every updater-enabled player now? [y/N] ' "$EXPECTED_TAG"
  read -r answer
  case "$answer" in
    y|Y|yes|YES) ;;
    *)
      printf 'Release left as a verified draft.\n'
      exit 0
      ;;
  esac
fi

gh release edit "$EXPECTED_TAG" --repo "$PUBLIC_REPOSITORY" \
  --draft=false --latest >/dev/null
[[ "$(gh release view "$EXPECTED_TAG" --repo "$PUBLIC_REPOSITORY" \
  --json isDraft --jq '.isDraft')" == "false" ]] || die \
  "GitHub did not publish $EXPECTED_TAG"
printf 'Published %s: %s\n' "$EXPECTED_TAG" "$RELEASE_URL"
