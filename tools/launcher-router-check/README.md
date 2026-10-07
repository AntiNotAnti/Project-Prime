# Launcher navigation contracts

Run `dotnet run --project tools/launcher-router-check -c Release`.

The content-free executable compiles the production shared router, page-lifetime and snapshot
contracts, and legacy `PrimeRouter` facade without an Avalonia, RmlUi, graphics, or game-content
dependency. It verifies shipped route aliases/tab order, Unicode deep links, guarded navigation,
modal priority, focus restoration, cancellation, revision ordering, bounded/copied history,
owner-thread mutation, and the legacy active-lobby redirect.

Physical keyboard/gamepad input, native document focus, real server/session transitions, and
visual parity remain separate integration gates; this check makes no claim about those gates.
