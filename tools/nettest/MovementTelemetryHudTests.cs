using System;
using System.Globalization;
using MphRead.Mods.Movement;

namespace MphRead.NetTest;

internal static class MovementTelemetryHudTests
{
    internal static int Run()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            if (MovementTelemetryHud.Format(new(12.5f, .75f, true)) != ("SPEED 12.5 U/S", "AIR EFF 75%"))
                throw new InvalidOperationException("Movement HUD units and efficiency use invariant formatting.");
            if (MovementTelemetryHud.Format(new(0, -1, true)).Efficiency != "AIR EFF --"
                || MovementTelemetryHud.Format(new(0, .75f, false)).Efficiency != "AIR EFF --")
                throw new InvalidOperationException("No-input and grounded samples have no air efficiency.");
            if (MovementTelemetryHud.Format(new(float.NaN, float.NaN, true)) != ("SPEED -- U/S", "AIR EFF --")
                || MovementTelemetryHud.Format(new(float.PositiveInfinity, float.PositiveInfinity, true)).Efficiency != "AIR EFF --")
                throw new InvalidOperationException("Non-finite telemetry stays out of movement HUD text.");
            if (MovementTelemetryHud.Format(new(0, 2, true)).Efficiency != "AIR EFF 100%")
                throw new InvalidOperationException("Air efficiency presentation is bounded to 100 percent.");
            var cache = new MovementTelemetryHud.TextCache();
            var first = cache.Get(new(12.5f, .75f, true));
            for (int frame = 0; frame < 1000; frame++)
            {
                var repeated = cache.Get(new(12.5f, .75f, true));
                if (!ReferenceEquals(first.Speed, repeated.Speed) || !ReferenceEquals(first.Efficiency, repeated.Efficiency))
                    throw new InvalidOperationException("Repeated render samples reuse formatted telemetry text.");
            }
            if (cache.Get(new(20, .5f, true)) != ("SPEED 20.0 U/S", "AIR EFF 50%")
                || cache.Get(new(20, .5f, false)) != ("SPEED 20.0 U/S", "AIR EFF --")
                || cache.Get(new(20, -1, true)).Efficiency != "AIR EFF --")
                throw new InvalidOperationException("New completed samples refresh speed, grounded and no-input text.");
            var otherScene = new MovementTelemetryHud.TextCache();
            otherScene.Get(new(999, 1, true));
            if (cache.Get(new(12.5f, .75f, true)) != first)
                throw new InvalidOperationException("Independent scene text caches cannot contaminate one another.");
            Console.WriteLine("Movement telemetry HUD formatting/cache PASS (7)");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("Movement telemetry HUD formatting FAIL: " + error);
            return 1;
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
