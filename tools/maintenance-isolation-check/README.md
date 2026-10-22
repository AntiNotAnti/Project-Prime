# Maintenance cache isolation

Build against the current client with `BuildProjectReferences=false`, after the
client has been built. Launch a fresh process without `PROJECT_PRIME_USER_DATA`
for `--default` (path observation only). Launch a separate fresh process with
the override set to an existing empty absolute private directory and pass
`--isolated` followed by that directory.

The isolated check calls the actual shared map scheduler and actual
`Maintenance.ClearMapBuildCache`. It verifies that cleanup uses the root
captured by that scheduler even if the environment changes later, preserves an
alternate cache and a save sentinel, and is harmless when repeated. It never
creates, enumerates or deletes the ordinary installation's cache. No game
assets, settings, account data, application startup or GL context are needed.
