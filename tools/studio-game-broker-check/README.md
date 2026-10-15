# Studio Game broker checks

Run `dotnet run --project tools/studio-game-broker-check -c Release` with .NET 10.
The check builds a real map package, compiles and installs it through the authenticated local pipe, and verifies exact map/content/package identities, owner dispatch, scene preparation rejection with byte-identical package/runtime outputs, duplicate request handling, playtest/status/stop callbacks, version mismatches, and narrow publishing tickets.

The check uses a private disposable user data and runtime root. It does not start a native game window, make an online match, or assert paired-editor/physical-device acceptance. Those remain separate acceptance gates. Startup, authentication, child forwarding, and peer crash isolation are additionally checked by `tools/studio-process-check`.
