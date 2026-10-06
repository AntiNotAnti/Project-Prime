# Renderer ownership regression check

Run `dotnet run --project tools/render-lifetime-check`.

This package-free check links the production terminal-release and shared-model
lease implementations. It covers shared owners, duplicate native IDs, failed
native deletion, context loss, duplicate/reentrant cleanup, 128 startup/shutdown
cycles and collection of decoded model data. Only the model data and driver
boundary are substitutes; it does not validate GPU synchronization or Android
surface lifecycle. Run the persistent-context `render-resource-check` and device
pause/resume and device-loss scenarios for those integration gates.
