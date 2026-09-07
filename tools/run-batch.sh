#!/usr/bin/env bash
# Seeded rendered authoritative matches: run-batch.sh [runs=10] [seed].
# Requires Python 3, a desktop OpenGL display and extracted GAME_DATA_DIRECTORY.
# GAME_BUILD_DIRECTORY defaults to this repo's src/MphRead/bin/Release/net9.0;
# DOTNET defaults to dotnet on PATH, then ~/.dotnet/dotnet. No build is performed.
# GAME_DATA_VERSION defaults to AMHE1. Runtime files and generated paths.txt are
# staged under a fresh BATCH_OUTPUT directory (default tools/batch-SEED).
# BATCH_MIN_SECONDS/BATCH_MAX_SECONDS default 70/129 (each client: 10..300).
# Optional BATCH_PLAYERS=2..8 and BATCH_STAGGER_SECONDS=0..10 bound local smokes.
# BATCH_ROTATION may name an existing rotation; otherwise each local run gets an
# explicit two-map Battle rotation with randomized minutes and point goals.
# The remote wrapper uses MPH_SERVER_HOST/MPH_SERVER_PORT; it never changes a
# remote server, and its rotation/content must be configured by its operator.
set -euo pipefail
TOOLS_DIRECTORY="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
exec "${PYTHON:-python3}" "$TOOLS_DIRECTORY/run-network-batch.py" "$@"
