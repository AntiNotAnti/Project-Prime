# CW-11c — explicit local package play

The desktop developer command opens one offline Samus Battle scene using an
installed exact package revision:

```text
ProjectPrime -weaponplay <store-directory> <lowercase-package-sha256> "MP3 PROVING GROUND"
ProjectPrime -weaponplay <store-directory> <lowercase-package-sha256> "MP3 PROVING GROUND" -seconds 30
```

Run from a private game-data profile with a valid `paths.txt` referring to the
operator's extracted game files. Build and install with the separate `-weapon`
commands first; `-weaponplay` does not download or install anything. The hash is
the exact archive SHA-256 printed by installation, not its mechanics or content
digest. The optional duration is an integer from 1 through 600 seconds of fixed
simulation ticks; without it the window stays interactive.

The local human starts without custom ownership. Spawn normally and collect
the authored world weapon pickup, then use the ordinary custom weapon input,
weapon cycling or weapon wheel. No weapon is granted by the command. Stock
rooms require an existing authored M7 pickup layout. Custom maps, unsupported
rooms, online/replay/headless modes and additional character options are
refused. Samus is the fixed initial desktop character for this narrow entry.

The command verifies the installed revision before creating the window. After
native scene load and before simulation starts, scene activation acquires its
own package lease and prepares package presentation resources. The scene owns
these until cleanup; uninstall/prune cannot substitute a different active
revision. Replay recording and killcams remain unavailable for custom gameplay.

This slice is a developer local-play entry, not the Studio Weapon Creator,
Community browser/install workflow, custom-match package handshake or custom
replay support. Rendered gameplay and lifecycle evidence belongs in the
corresponding evidence receipt; this document alone does not establish a test
pass or release acceptance.
