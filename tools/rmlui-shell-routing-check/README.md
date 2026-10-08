# Shared Shell native routing check

This standalone friend assembly exercises the actual compiled desktop Shell,
shipped native documents, native keyboard/mouse packets, Settings presenter and
engine persistence backend. It covers dirty Settings navigation to Home and
Offline through Apply/Discard, subsequent Home controls, required/busy Setup
blocking Studio IPC and native navigation, and Studio playtest requests deferred
through the Settings decision and cancelled before approval or before a scene
starts. Returning to editing from the unsaved dialog cancels the deferred IPC
request. Other cancellation restores a usable Home page and keeps later controls
working.

It also opens and retires 100 actual native modal documents on the same
persistent Home page and calls Shell's actual presentation-policy publisher.
The policy cache must remain bounded at three entries or fewer, including idle
checks after the churn; changing routes between modals would hide this leak.

The harness attaches the real native page composition to Shell using narrow
reflection into static fields. It does not create a `RenderWindow`, render a
game scene, start a match, or stand in for full window gameplay acceptance. The
busy Setup case supplies an unresolved harness task to the existing controller;
it does not run a picker, extraction, download or update installation. Offline
navigation rebinds the shipped navigation button using the production action
setter to emit a real `route:offline` packet.

The IPC polling tests register an isolated immutable Accepted fixture record in
the real broker's cache, then exercise its actual owner polling and matching
Stop command. This avoids publishing a map or starting a local authenticated
endpoint while still checking deferred Accepted/Rejected/Ended status behavior.

Before any engine static is accessed, the process creates fresh empty temporary
user data, makes it the working directory, points preferences/game-data/export
paths into it, and enables the existing UI diagnostic guard. No auth files are
copied. Account/Social/update startup is suppressed; no account HTTP or production
mutations are authorized. Saved Settings exist only inside the deleted fixture.
The harness verifies both central diagnostic account guards in the actual client
assembly before accessing engine statics and rejects session/ticket/token files.
Run in a standalone process because it exercises engine static globals.

Run against a frozen built client without rebuilding the game (both the native
client and the transitional client with legacy fallback are supported):

```sh
dotnet run --project tools/rmlui-shell-routing-check -c Release \
  -p:PrimeAssemblyDirectory=/absolute/client/bin/Release/net10.0 \
  -- /absolute/libProjectPrime.RmlUi.Native.dylib \
  /absolute/client/bin/Release/net10.0/rmlui
```

Without `PrimeAssemblyDirectory`, the project builds its normal game project
reference with RmlUi enabled. Supply an actual native bridge built for the host
and matching the current intent registry. A passing run prints its assertion
count and the explicit harness limitation.
