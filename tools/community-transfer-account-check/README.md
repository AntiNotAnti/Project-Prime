# Community durable transfer account checks

Asset-free, temporary-directory acceptance checks for account-bound upload admission,
durable schema migration, retry and response account fencing, and same-account restart.
The HTTP fixture runs only on loopback, never against a live Community endpoint.
No user queue, session, credential store, editor, or installation is opened.

Build the main application first at the coordinated checkpoint, then build without
rebuilding the shared application and run from an isolated working directory:

```sh
dotnet build tools/community-transfer-account-check -c Release -p:BuildProjectReferences=false --no-restore
```

The scripted service returns valid immutable package identities and deliberately
holds responses so account switches occur at deterministic HTTP boundaries. It does
not claim server atomic compare-and-swap or validate a real identity provider.

The fresh-application RML-57 run passed 49 assertions, including actual post-completion
filesystem cleanup failure, observer isolation and explicit receipt retirement.
