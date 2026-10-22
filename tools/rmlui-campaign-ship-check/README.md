# Native campaign ship controller check

Run `dotnet run --project tools/rmlui-campaign-ship-check -c Release`.

This source-linked fixture uses an in-memory backend and opaque synthetic launch payload. It checks detached read-only destination projections, cockpit/map navigation, locked destinations, exact slot/planet/SHA/hunter requests, refresh after failed validation, single-consumption launch/return effects, malformed read safety and owner-thread checks. No game assets, user saves, preferences or scenes are opened or changed.

Production projection and final departure authorization remain in `CurrentNativeCampaignShipBackend`, which reuses `CampaignNavigation.StarMap`, `CampaignShipTravel.TryChooseLanding` and `AdventureLaunch.Create`. Native input/layout, that adapter's real owners and live scene handoff require composed acceptance separately.
