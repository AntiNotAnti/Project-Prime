using System;

namespace MphRead.Mods.Input
{
    /// <summary>30 Hz pointer action tape observed by render and fixed simulation clocks.</summary>
    internal static class TouchSamplingChecks
    {
        internal static void Run()
        {
            bool stylus = PointerInput.StylusMode;
            bool guard = PointerInput.GuardJumps;
            bool zone = StylusZone.Enabled;
            try
            {
                PointerInput.StylusMode = true;
                PointerInput.GuardJumps = false;
                StylusZone.Enabled = false;
                foreach (int refresh in new[] { 30, 60, 120, 144 })
                {
                    PointerDevice.Reset();
                    Sample(0);
                    int simulationTime = 0;
                    float motionX = 0, motionY = 0, driveX = 0, driveY = 0;
                    // Exact integer timeline: 720 units/s, input every 24,
                    // simulation every 12. Presentation may run zero or two steps.
                    for (int renderTime = 720 / refresh; renderTime <= 720;
                        renderTime += 720 / refresh)
                    {
                        Sample(renderTime / 24);
                        while (simulationTime + 12 <= renderTime)
                        {
                            simulationTime += 12;
                            PointerDevice.AdvanceSimulationStep();
                            (float x, float y) = PointerDevice.TakeDelta();
                            motionX += x;
                            motionY += y;
                            (float xDrive, float yDrive) = AltFormGesture.StockRollDrive(x, y, 24, 1);
                            driveX += xDrive;
                            driveY += yDrive;
                            if (PointerDevice.TakeDelta() != (0f, 0f))
                            {
                                throw new InvalidOperationException($"{refresh} Hz reused a consumed touch sample");
                            }
                        }
                    }
                    if (simulationTime != 720 || motionX != 120 || motionY != -60
                        || MathF.Abs(driveX - 5) > .00001f || MathF.Abs(driveY + 2.5f) > .00001f)
                    {
                        throw new InvalidOperationException($"{refresh} Hz lost or repeated the 30 Hz Morph Ball tape");
                    }
                }
                Console.WriteLine("TOUCH SAMPLING PASS (30 Hz samples, 60 Hz simulation, 30/60/120/144 Hz presentation)");
            }
            finally
            {
                PointerDevice.Reset();
                PointerInput.StylusMode = stylus;
                PointerInput.GuardJumps = guard;
                StylusZone.Enabled = zone;
            }
        }

        private static void Sample(int index)
            => PointerDevice.Update(new PointerSample(PointerDeviceType.Touch, 1,
                500 + index * 4, 500 - index * 2, true, true, true), 1920, 1080);
    }
}
