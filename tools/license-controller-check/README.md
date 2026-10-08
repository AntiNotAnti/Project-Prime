# Hunter License contract check

Run `dotnet run --project tools/license-controller-check -c Release` from the repository root. This uses the production immutable License controller through a fake read-model/account boundary. It requires no game content, network access, credentials or renderer and never contacts a production account service.

The checks cover immutable/stable snapshots, bounded latest-match history and local pagination, cancellation and superseded completions, retained authoritative career during outages, all existing stats and achievement thresholds, empty-guest recovery guards, password confirmation and input limits, linked-provider permissions, asynchronous account failure/retry, clearing sensitive inputs after success, worker-thread rejection and ignored late completions after disposal.

Run actual native document checks with a packaged asset bundle:

```
dotnet run --project tools/license-controller-check -c Release -p:MphReadRmlUi=true -- --native <native-bridge> <rmlui-assets>
```

These use the real native draw-list backend for account controls, outer-page wheel
scrolling, compact 640×320 layout, and density 2. The fake account boundary records
zero production account operations. Browser OAuth callbacks and real
authenticated account acceptance remain separate integration gates.
