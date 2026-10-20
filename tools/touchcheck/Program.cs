using System;
using MphRead.Droid;
using MphRead.Mods;

InputOwnershipChecks.Run();
WeaponTouchChecks.Run();

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
// A complete down/up event can arrive between game frames. Held-state
// sampling loses it; the one-shot queue must retain each physical press.
controls = new TouchControls();
controls.ClipEnabled = true;
controls.Layout(1920, 1080, 1);
var clipButton = System.Linq.Enumerable.First(controls.Buttons, b => b.Action == TouchAction.Clip);
void ClipDown(int id) => controls.PointerDown(id, clipButton.CentreX, clipButton.CentreY);
void CheckClip(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
ClipDown(100);
controls.PointerUp(100);
CheckClip(!controls.IsHeld(TouchAction.Clip) && controls.TakeClipPress(), "short clip tap lost");
CheckClip(!controls.TakeClipPress(), "short clip tap repeated");
ClipDown(101);
ClipDown(102);
CheckClip(controls.TakeClipPress() && controls.TakeClipPress() && !controls.TakeClipPress(),
    "simultaneous clip fingers lost or repeated");
controls.PointerUp(101);
CheckClip(controls.IsHeld(TouchAction.Clip), "first release cancelled second finger");
CheckClip(!controls.TakeClipPress(), "held clip repeated");
controls.PointerUp(102);
for (int i = 0; i < 100; i++) { ClipDown(200 + i); controls.PointerUp(200 + i); }
int queued = 0;
while (controls.TakeClipPress()) queued++;
CheckClip(queued == 16, "clip queue did not enforce its bound");
ClipDown(400);
controls.ReleaseEverything();
CheckClip(!controls.TakeClipPress(), "lifecycle reset retained clip request");
ClipDown(401);
controls.PointerUp(401);
controls.ClipEnabled = false;
CheckClip(!controls.TakeClipPress(), "disabled replay retained clip request");
Console.WriteLine("TOUCHCHECK PASS: short taps, concurrent fingers, held non-repeat, bounded clip queue and cancellation");
var savedLayout = MphRead.Mods.Input.TouchSettings.CopyLayout();
float buttonScale = MphRead.Mods.Input.TouchSettings.ButtonScale;
float stickScale = MphRead.Mods.Input.TouchSettings.StickScale;
float opacity = MphRead.Mods.Input.TouchSettings.OverlayOpacity;
try
{
    var control = MphRead.Mods.Input.TouchControl.Clip;
    MphRead.Mods.Input.TouchSettings.SetLayout(control, .5f, .5f, 1f);
    MphRead.Mods.Input.TouchSettings.ReadSetting("touch_layout_clip", "NaN,0.5,1");
    CheckClip(!MphRead.Mods.Input.TouchSettings.CopyLayout().ContainsKey(control),
        "nonfinite layout failed to fall back to default");
    MphRead.Mods.Input.TouchSettings.ButtonScale = float.NaN;
    MphRead.Mods.Input.TouchSettings.StickScale = float.PositiveInfinity;
    MphRead.Mods.Input.TouchSettings.OverlayOpacity = float.NegativeInfinity;
    CheckClip(MphRead.Mods.Input.TouchSettings.ButtonScale == 1f
        && MphRead.Mods.Input.TouchSettings.StickScale == 1f
        && MphRead.Mods.Input.TouchSettings.OverlayOpacity == 1f,
        "nonfinite touch appearance settings did not use defaults");
    Console.WriteLine("TOUCHCHECK PASS: nonfinite layout and appearance use usable defaults");
}
finally
{
    MphRead.Mods.Input.TouchSettings.ReplaceLayout(savedLayout);
    MphRead.Mods.Input.TouchSettings.ButtonScale = buttonScale;
    MphRead.Mods.Input.TouchSettings.StickScale = stickScale;
    MphRead.Mods.Input.TouchSettings.OverlayOpacity = opacity;
}
return 0;
