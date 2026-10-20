Run: dotnet run --project tools/menu-environment-check -c Release

This portable check links the production environment budget, motion timeline,
lobby visual state and shared formation. Only external Hunter/quality enums and
preferences are stubs. It requires no game files, native renderer or services.

Checks cover scalable work budgets (including Android's reduced-cost policy),
120/144/240 Hz time-based convergence, reduced motion, focus/suspend gaps,
authoritative ready/identity/session/team state, join/leave/reveal signals,
launch cancellation, lifecycle reset and zero-allocation steady-state updates.
Android policy checks do not qualify a real Android renderer or device.

The separate rmlui-opengl-check --menu-environment mode exercises actual
procedural GL pixels and native compositor state in an existing GL context.
