# RmlUi live menu checks

Run the focused local dedicated-server preparation regression with:

```powershell
dotnet run --project tools/rmlui-live-menu-check/rmlui-live-menu-check.csproj -- --local-server-install-only
```

It exercises the production readiness/install gate with injected local delegates,
so it needs no release download or dedicated-server package. The checks cover
reuse of an installed server, worker-thread installation, serialized retries,
install failure, success without a ready binary, gate recovery, cancelled
waiters, and cancellation after the installer returns. A filesystem-only
fixture uses an isolated launcher directory and synthetic server executable
to verify that an incomplete-install marker hides a partial extraction,
survives cancellation, and clears after a successful retry.
The focused mode also runs the existing created-lobby completion checks for
acceptance, cancellation, stale results, and retry ownership.

These checks also run as part of the default live-menu acceptance suite.
