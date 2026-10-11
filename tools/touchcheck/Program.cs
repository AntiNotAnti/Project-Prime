using System;
using MphRead.Droid;
using MphRead.Mods;

var controls = new TouchControls();
controls.Layout(1920, 1080, 1);
controls.PointerDown(1, 1400, 500);
controls.PointerMove(1, 1404, 498, Environment.TickCount64, Environment.TickCount64);
controls.PointerUp(1);
var drive = controls.SamusAltMoveDrive;
var delta = controls.TakeAimDelta();
if (!drive.Engaged || drive.X <= 0 || drive.Y >= 0 || delta != (4f, -2f))
{
    Console.Error.WriteLine($"TOUCHCHECK FAIL: released drag dropped from roll: drive={drive}, delta={delta}");
    return 1;
}
if (controls.SamusAltMoveDrive != (false, 0f, 0f) || controls.TakeAimDelta() != (0f, 0f))
{
    Console.Error.WriteLine("TOUCHCHECK FAIL: released drag reused after consumption");
    return 1;
}
Console.WriteLine("TOUCHCHECK PASS: released drag consumed by roll and aim exactly once");

float sensitivity = InputSettings.AltSwipeSensitivity;
InputSettings.AltSwipeSensitivity = 1;
try
{
    foreach (int refresh in new[] { 30, 60, 120, 144 })
    {
        controls = new TouchControls();
        controls.Layout(1920, 1080, 1);
        controls.PointerDown(1, 1400, 500);
        int sample = 0, simulationTime = 0;
        float totalX = 0, totalY = 0, rollX = 0, rollY = 0;
        long start = Environment.TickCount64;
        for (int renderTime = 720 / refresh; renderTime <= 720; renderTime += 720 / refresh)
        {
            while ((sample + 1) * 24 <= renderTime)
            {
                sample++;
                controls.PointerMove(1, 1400 + sample * 4, 500 - sample * 2,
                    start + sample * 1000 / 30, start + sample * 1000 / 30);
            }
            // The final move and release arrive before the final simulation.
            if (sample == 30) controls.PointerUp(1);
            while (simulationTime + 12 <= renderTime)
            {
                simulationTime += 12;
                drive = controls.SamusAltMoveDrive;
                delta = controls.TakeAimDelta();
                totalX += delta.X;
                totalY += delta.Y;
                rollX += drive.X;
                rollY += drive.Y;
                if (controls.SamusAltMoveDrive.X != 0 || controls.SamusAltMoveDrive.Y != 0)
                    throw new InvalidOperationException($"{refresh} Hz repeats consumed roll input");
            }
        }
        if (sample != 30 || simulationTime != 720 || totalX != 120 || totalY != -60
            || MathF.Abs(rollX - 5) > .00001f || MathF.Abs(rollY + 2.5f) > .00001f)
            throw new InvalidOperationException($"{refresh} Hz loses or repeats Android roll input");
        // Lifecycle cancellation clears both movement and presentation state.
        controls.PointerDown(2, 1400, 500);
        controls.PointerMove(2, 1404, 498, start + 2000, start + 2000);
        controls.ReleaseEverything();
        if (controls.SamusAltMoveDrive != (false, 0f, 0f)
            || controls.TakeAimDelta() != (0f, 0f) || controls.PeekAimDelta() != (0f, 0f))
            throw new InvalidOperationException($"{refresh} Hz lifecycle reset retains stale input");
        Console.WriteLine($"TOUCHCHECK PASS: Android 30 Hz tape at {refresh} Hz presentation, lifecycle reset");
    }
}
finally
{
    InputSettings.AltSwipeSensitivity = sensitivity;
}
return 0;
