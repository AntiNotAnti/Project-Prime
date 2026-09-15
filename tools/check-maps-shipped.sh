#!/usr/bin/env bash
# Fail if a custom map would reach a player without the data it needs.
#
# An imported map in maps/ is the recipe, the level it converts and the
# textures baked from that level -- in a folder, or cooked into one .fpmap. A
# brush-only map is complete in its recipe and deliberately has no level. A
# missing imported level or named texture registers a room that the game then
# declines to build, so it is checked rather than assumed.
#
#   tools/check-maps-shipped.sh                    # the repository
#   tools/check-maps-shipped.sh publish/win-x64    # a build we are about to release
set -uo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.." || exit 1

root="${1:-}"
if [ -n "$root" ]; then
  maps="$root/maps"
  what="$root"
else
  maps="maps"
  what="the repository"
fi

fail=0
found=0
if [ ! -d "$maps" ]; then
  echo "no maps/ in $what; nothing to check"
  exit 0
fi

# The redistribution decision is a top-level boolean in manifest.json.  Parse
# the document instead of searching its text: a nested notice or a string such
# as "true" must never satisfy the shipping guard, and malformed JSON fails
# closed. Python is already a required Project Prime tooling dependency.
manifest_allows_redistribution() {
  python3 -c '
import json
import sys

try:
    manifest = json.load(sys.stdin)
except ValueError:
    raise SystemExit(1)

if (type(manifest) is not dict
        or type(manifest.get("redistribution")) is not bool
        or manifest["redistribution"] is not True):
    raise SystemExit(1)
'
}

# A bundle is a map and its level in one file, so there is nothing beside it to
# look for: what has to be true is that the level is inside. Read the index
# without unpacking anything -- unzip -l is in every runner image, and a bundle
# with no .bsp in it is exactly the failure this script exists to catch.
while IFS= read -r file; do
  found=$((found + 1))
  name=$(basename "$file")
  manifest=$(unzip -p "$file" manifest.json 2>/dev/null)
  if printf '%s' "$manifest" | manifest_allows_redistribution; then
    echo "ok:      $name permits online redistribution"
  else
    echo "ERROR:   $name cannot ship because online redistribution is disabled"
    fail=1
  fi
  recipe=$(unzip -p "$file" '*.json' 2>/dev/null)
  # Brush-only maps (for example TEST ARENA) are complete in their recipe:
  # their geometry and materials are generated from the JSON and they borrow
  # textures from a shipped room. Imported maps must carry the trimmed level
  # that the recipe names.
  if printf '%s' "$recipe" | grep -qiE '"import"[[:space:]]*:[[:space:]]*\{'; then
  # Do not use grep -q here. With pipefail, grep can exit as soon as it sees
  # the level while unzip is still writing the listing; unzip then receives
  # SIGPIPE and the pipeline is reported as failed even though the level is
  # present. Let grep consume the complete listing and discard its output.
    if unzip -l "$file" 2>/dev/null | grep -iE '\.bsp$' >/dev/null; then
      echo "ok:      $name carries its level inside it"
    else
      echo "MISSING: $name is an imported bundle with no level in it"
      fail=1
    fi
  else
    echo "ok:      $name carries its description-only recipe"
  fi
  # And its textures, which are the half that goes missing quietly. The pack is
  # baked from the level's own art, so it is derived and not in git -- a bundle
  # cooked where nobody had baked one carried a level and no art, and the room
  # it built had no materials at all: listed in the launcher, and a crash when
  # picked. The recipe inside names the pack; the pack has to be in there too.
  textures=$(printf '%s' "$recipe" \
    | grep -oiE '"textures"[[:space:]]*:[[:space:]]*"[^"]*"' \
    | head -n1 | sed -E 's/.*:[[:space:]]*"([^"]*)".*/\1/')
  if [ -n "$textures" ]; then
    if unzip -l "$file" 2>/dev/null | grep -iF "$textures" >/dev/null; then
      echo "ok:      $name carries its textures ($textures)"
    else
      echo "MISSING: $name names $textures and does not carry it"
      fail=1
    fi
  elif printf '%s' "$recipe" | grep -qE '"Materials"[[:space:]]*:[[:space:]]*\[\]'; then
    echo "MISSING: $name has no textures in it and borrows none, so its room has no materials"
    fail=1
  else
    echo "ok:      $name wears a shipped room's textures"
  fi
done < <(find "$maps" -name '*.fpmap' | sort)

# The level a map converts is named by import.source. Read it without a JSON
# parser: the field is one line in every file this writes, and a dependency on
# jq for a five-line check is worse than a grep.
#
# A recipe that has been cooked into a bundle beside it is the bundle's
# business, not its own: the working copy of a map keeps somebody's .pk3 in a
# folder, and that folder is not what ships.
while IFS= read -r file; do
  found=$((found + 1))
  name=$(basename "$file")
  source=$(grep -oiE '"source"[[:space:]]*:[[:space:]]*"[^"]+"' "$file" \
    | head -n1 | sed -E 's/.*"source"[[:space:]]*:[[:space:]]*"([^"]+)".*/\1/')
  if [ -z "$source" ]; then
    echo "ok:      $name builds from its own description, no level needed"
    continue
  fi
  bundle="$maps/$(basename "$file" .json).fpmap"
  if [ -f "$bundle" ]; then
    echo "ok:      $name ships as $(basename "$bundle")"
  elif [ -f "$(dirname "$file")/$source" ]; then
    echo "ok:      $name has $source beside it"
  else
    echo "MISSING: $name converts $source, which is not in $(dirname "$file")"
    fail=1
  fi
done < <(find "$maps" -name '*.json' -not -name '*.example' | sort)

if [ "$found" -eq 0 ]; then
  echo "no maps in $what"
  exit 0
fi
if [ "$fail" -ne 0 ]; then
  echo
  echo "A map whose level is absent is left out of the room list at startup; one"
  echo "that arrives without its textures is listed and cannot be built. Either"
  echo "ship what it needs beside it, or take the map file out."
  exit 1
fi
echo "every map in $what has what it needs"
