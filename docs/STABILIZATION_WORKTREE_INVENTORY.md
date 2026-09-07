# G1–G5 stabilization worktree inventory

Captured 2026-09-07 at `main` (`0228cd9`) for the S0 integration checkpoint.
The earlier G1–G5 subsystem commits are already present; this file records the
remaining dirty paths before the final integration seams are committed. G6 is
outside this checkpoint.

The combined bots + observers + replay + telemetry + Backend-outage endurance
soak and the physical Android/high-refresh acceptance and long tests are
explicitly skipped by the user request. They are not represented as passed.

## Scoped checkpoint paths

### G2 combat/HUD/radar and G5 bots

    src/Game/Gameplay/Hunters/PlayerEntitySimulation.cs

Bot activation state, radar snapshot flags, and assist projection are the
remaining authoritative entity seam.

### G3 match/network UX

    tools/server-update-package.py

The server update package protocol constant is aligned with protocol 8.

### Integrated tests and tooling

    src/Client/Runtime/AssemblyInfo.cs
    tools/nettest/Program.cs

The first file exposes the runtime internals to the focused test assembly; the
second registers the focused interpolation, overtime, and observer checks.

### Docs and generated-artifact guard

    .gitignore
    docs/STABILIZATION_WORKTREE_INVENTORY.md

The root ignore file explicitly covers every nested `bin/` and `obj/` directory
and the generated `/FruityPrime` root. No tracked build artifacts were found.

## Preserved outside this checkpoint

These paths were inspected and intentionally remain unstaged:

    LICENSE                         (pre-existing deletion)
    maps/**                         (all tracked and untracked Parallax work)

No unknown current status path remains. The ignored generated root and build
directories were checked with `git check-ignore`; they are not source inputs.

## Validation boundary

This checkpoint uses `git diff --check` and the project-boundary guard only. A
full .NET test suite, the combined endurance soak, physical Android testing, and
high-refresh acceptance are outside this checkpoint and must remain reported as
unverified/skipped.
